using System;
using System.Collections.Generic;
using System.Linq;

namespace AkaiS950List
{
    /// <summary>
    /// Copying a sample or a program from one disk image to another.
    ///
    /// The work is planned before any of it is written, the way slicing is: PlanCopy says
    /// what would land, under what names, and what it would cost, and ApplyCopy refuses to
    /// run a plan that reported a problem. Nothing is written until the whole set fits.
    ///
    /// Two things make this more than a byte copy.
    ///
    /// A program's zones name their samples by name, so a program is never copied alone -
    /// every sample its zones name comes with it, or it arrives silent. And a sample's
    /// header carries two numbers that belong to the disk it was living on rather than to
    /// the sample: where it sits in the sampler's RAM (0x36..0x38) and where its loop
    /// descriptors sit (0x28). RebuildPointers does not touch either - it rebuilds the
    /// keygroup arena, not the sample table - so they are recomputed here, from the last
    /// sample already on this disk, exactly as AddSample derives them for a new one.
    ///
    /// Everything else in the header is carried over untouched, which is the point of
    /// copying the file rather than re-adding it from its audio: the rate, the tuning, the
    /// loop markers, the loop mode and direction and the loudness all survive.
    /// </summary>
    public sealed partial class AkaiDisk
    {
        /// <summary>One file a copy would bring over, and the name it would take here.</summary>
        public sealed class CopyItem
        {
            public AkaiEntry Source;
            public char Type;
            public string From;            // its name on the source disk
            public string To;              // the name it will have here
            public bool AlreadyHere;       // same name, same file: nothing will be written
            public int Blocks;

            public bool Renamed
            {
                get { return !AlreadyHere && !string.Equals(From, To, StringComparison.OrdinalIgnoreCase); }
            }

            public override string ToString()
            {
                if (AlreadyHere) return From + "  -  already here";
                return From + (Renamed ? "  ->  " + To : "") + "  -  " + Blocks + " block(s)";
            }
        }

        /// <summary>What a copy would do. Nothing is written until the plan is clean.</summary>
        public sealed class CopyPlan
        {
            public AkaiDisk From;
            public AkaiEntry What;

            public List<CopyItem> Items = new List<CopyItem>();
            public List<string> Problems = new List<string>();

            /// <summary>Things worth saying that are not reasons to stop.</summary>
            public List<string> Notes = new List<string>();

            public int Blocks;             // blocks the files actually written will take
            public int Slots;              // directory slots they need

            public bool Ok { get { return Problems.Count == 0; } }

            public List<CopyItem> Writes
            {
                get { return Items.Where(i => !i.AlreadyHere).ToList(); }
            }

            public int SampleWrites
            {
                get { return Items.Count(i => i.Type == 'S' && !i.AlreadyHere); }
            }
        }

        /// <summary>
        /// What it would take to copy one file from another disk onto this one.
        ///
        /// The samples come first in the list because they have to exist here before the
        /// program that names them, and ApplyCopy writes them in that order.
        /// </summary>
        public CopyPlan PlanCopy(AkaiDisk from, AkaiEntry what)
        {
            var plan = new CopyPlan();
            plan.From = from;
            plan.What = what;

            if (from == null || what == null)
            {
                plan.Problems.Add("Nothing to copy.");
                return plan;
            }
            if (ReferenceEquals(from, this))
            {
                plan.Problems.Add("That file is already on this disk.");
                return plan;
            }
            if (what.Type != 'S' && what.Type != 'P')
            {
                plan.Problems.Add("Only samples and programs can be copied - the " +
                                  TypeNameOf(what.Type) + " belongs to the disk it is on.");
                return plan;
            }

            // Samples first, then the program: a zone can only find a sample already here.
            var order = new List<AkaiEntry>();

            if (what.Type == 'P')
            {
                foreach (string named in from.ZoneSampleNames(what))
                {
                    AkaiEntry found = null;
                    foreach (var x in from.Entries)
                    {
                        if (x.Type != 'S') continue;
                        if (!string.Equals(x.Name.Trim(), named, StringComparison.OrdinalIgnoreCase)) continue;
                        found = x;
                        break;
                    }

                    if (found == null)
                    {
                        // Already dangling where it came from. Worth saying, but copying the
                        // program is still the best that can be done with it.
                        plan.Notes.Add("'" + named + "' is named by a zone but is not on " +
                                       ShortSourceName(from) + " either, so it will still be missing here");
                        continue;
                    }

                    bool have = false;
                    foreach (var o in order) if (o.Slot == found.Slot) { have = true; break; }
                    if (!have) order.Add(found);
                }
            }

            order.Add(what);

            // Names in use here, per type - extended as the plan grows, so two incoming
            // samples cannot both be promised the same new name.
            var takenSamples = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var takenPrograms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var x in Entries)
            {
                if (x.Type == 'S') takenSamples.Add(x.Name.Trim());
                else if (x.Type == 'P') takenPrograms.Add(x.Name.Trim());
            }

            foreach (var src in order)
            {
                var bytes = from.ReadFile(src);
                string name = src.Name.Trim();

                var item = new CopyItem();
                item.Source = src;
                item.Type = src.Type;
                item.From = name;
                item.To = name;

                AkaiEntry clash = null;
                foreach (var x in Entries)
                {
                    if (x.Type != src.Type) continue;
                    if (!string.Equals(x.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)) continue;
                    clash = x;
                    break;
                }

                var taken = src.Type == 'S' ? takenSamples : takenPrograms;

                if (clash != null && SameFile(src.Type, ReadFile(clash), bytes))
                {
                    // The same file under the same name. Copying it again would spend
                    // blocks on a second copy of something already here.
                    item.AlreadyHere = true;
                    item.Blocks = 0;
                }
                else
                {
                    if (clash != null) item.To = FreeCopyName(name, taken);
                    taken.Add(item.To);

                    item.Blocks = BlocksFor(bytes.Length);
                    plan.Blocks += item.Blocks;
                    plan.Slots++;
                }

                plan.Items.Add(item);
            }

            if (plan.Blocks > FreeBlocks)
                plan.Problems.Add("Needs " + plan.Blocks + " block(s), " + FreeBlocks + " free on this disk.");

            int slots = FreeSlots();
            if (plan.Slots > slots)
                plan.Problems.Add("Needs " + plan.Slots + " directory slot(s), " + slots + " free.");

            return plan;
        }

        /// <summary>
        /// Carry out a plan. It refuses one that reported a problem rather than writing
        /// half of it, because a disk holding a program whose samples did not fit is worse
        /// than a disk that was left alone.
        /// </summary>
        public List<AkaiEntry> ApplyCopy(CopyPlan plan)
        {
            if (plan == null) throw new ArgumentNullException("plan");
            if (!plan.Ok)
                throw new InvalidOperationException(string.Join("  ", plan.Problems.ToArray()));

            // Where a sample had to be renamed, the program's zones have to follow it.
            var renamed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var i in plan.Items)
                if (i.Type == 'S' && !i.AlreadyHere &&
                    !string.Equals(i.From, i.To, StringComparison.OrdinalIgnoreCase))
                    renamed[i.From] = i.To;

            var landed = new List<int>();

            foreach (var item in plan.Items)
            {
                if (item.AlreadyHere) continue;

                var bytes = plan.From.ReadFile(item.Source);

                var e = item.Type == 'S'
                      ? WriteCopiedSample(item, bytes)
                      : WriteCopiedProgram(item, bytes, renamed);

                landed.Add(e.Slot);
            }

            ParseDirectory();
            RebuildPointers();
            ParseDirectory();

            var result = new List<AkaiEntry>();
            foreach (var e in Entries) if (landed.Contains(e.Slot)) result.Add(e);
            return result;
        }

        // ------------------------------------------------------------ keygroups

        /// <summary>What copying one keygroup onto another program would do.</summary>
        public sealed class KeygroupPlan
        {
            public AkaiDisk From;
            public AkaiEntry SourceProgram;
            public int Index;                  // 0-based, as the file stores them
            public AkaiEntry TargetProgram;

            /// <summary>Samples the keygroup's zones name that are not already here.</summary>
            public List<CopyItem> Samples = new List<CopyItem>();

            public List<string> Problems = new List<string>();
            public List<string> Notes = new List<string>();

            public int Blocks;                 // the samples, and the program growing by one record
            public int Slots;

            public bool Ok { get { return Problems.Count == 0; } }

            public List<CopyItem> Writes
            {
                get { return Samples.Where(i => !i.AlreadyHere).ToList(); }
            }
        }

        /// <summary>
        /// What it would take to copy one keygroup onto a program on this disk.
        ///
        /// Unlike a file copy this is allowed to start on the disk it finishes on: moving a
        /// keygroup from one program to another on the same disk is an ordinary thing to
        /// want, and the samples it names are then already here, which the plan discovers
        /// for itself rather than being told.
        ///
        /// The keygroup is appended to the end of the target program. A keygroup's place in
        /// the chain carries no meaning the sampler reads - the key range decides what
        /// sounds - so there is nothing to be gained by inserting it anywhere else.
        /// </summary>
        public KeygroupPlan PlanCopyKeygroup(AkaiDisk from, AkaiEntry sourceProgram, int index,
                                             AkaiEntry targetProgram)
        {
            var plan = new KeygroupPlan();
            plan.From = from;
            plan.SourceProgram = sourceProgram;
            plan.Index = index;
            plan.TargetProgram = targetProgram;

            if (from == null || sourceProgram == null || targetProgram == null)
            {
                plan.Problems.Add("Nothing to copy.");
                return plan;
            }
            if (sourceProgram.Type != 'P' || targetProgram.Type != 'P')
            {
                plan.Problems.Add("Keygroups can only be copied from one program to another.");
                return plan;
            }

            int have = KeygroupCount(sourceProgram);
            if (index < 0 || index >= have)
            {
                plan.Problems.Add("That program has no keygroup " + (index + 1) + ".");
                return plan;
            }

            int already = KeygroupCount(targetProgram);
            if (already >= MaxKeygroups)
            {
                plan.Problems.Add(targetProgram.Name.Trim() + " already holds " + MaxKeygroups +
                                  " keygroups, which is the limit.");
                return plan;
            }

            var record = KeygroupRecord(from, sourceProgram, index);

            // The samples this one keygroup names, rather than the whole program's.
            var taken = new HashSet<string>(
                Entries.Where(x => x.Type == 'S').Select(x => x.Name.Trim()),
                StringComparer.OrdinalIgnoreCase);

            foreach (string named in ZoneNamesOf(record))
            {
                AkaiEntry src = null;
                foreach (var x in from.Entries)
                {
                    if (x.Type != 'S') continue;
                    if (!string.Equals(x.Name.Trim(), named, StringComparison.OrdinalIgnoreCase)) continue;
                    src = x;
                    break;
                }

                if (src == null)
                {
                    plan.Notes.Add("'" + named + "' is named by a zone but is not on " +
                                   ShortSourceName(from) + " either, so it will still be missing here");
                    continue;
                }

                var bytes = from.ReadFile(src);

                AkaiEntry clash = null;
                foreach (var x in Entries)
                {
                    if (x.Type != 'S') continue;
                    if (!string.Equals(x.Name.Trim(), named, StringComparison.OrdinalIgnoreCase)) continue;
                    clash = x;
                    break;
                }

                var item = new CopyItem();
                item.Source = src;
                item.Type = 'S';
                item.From = named;
                item.To = named;

                if (clash != null && SameFile('S', ReadFile(clash), bytes))
                {
                    item.AlreadyHere = true;
                }
                else
                {
                    if (clash != null) item.To = FreeCopyName(named, taken);
                    taken.Add(item.To);

                    item.Blocks = BlocksFor(bytes.Length);
                    plan.Blocks += item.Blocks;
                    plan.Slots++;
                }

                plan.Samples.Add(item);
            }

            // The target program grows by one record, which may cost it another block.
            int was = ReadFile(targetProgram).Length;
            plan.Blocks += BlocksFor(was + KeygroupSize) - BlocksFor(was);

            if (plan.Blocks > FreeBlocks)
                plan.Problems.Add("Needs " + plan.Blocks + " block(s), " + FreeBlocks + " free on this disk.");

            int slots = FreeSlots();
            if (plan.Slots > slots)
                plan.Problems.Add("Needs " + plan.Slots + " directory slot(s), " + slots + " free.");

            return plan;
        }

        /// <summary>
        /// Carry out a keygroup plan, and return the new keygroup's number, counting from 1
        /// as the list shows them.
        /// </summary>
        public int ApplyCopyKeygroup(KeygroupPlan plan)
        {
            if (plan == null) throw new ArgumentNullException("plan");
            if (!plan.Ok) throw new InvalidOperationException(string.Join("  ", plan.Problems.ToArray()));

            var renamed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var i in plan.Samples)
                if (!i.AlreadyHere && !string.Equals(i.From, i.To, StringComparison.OrdinalIgnoreCase))
                    renamed[i.From] = i.To;

            /*
             * The record is taken before anything is written. Adding the samples reparses
             * the directory, which builds new entry objects, so the ones the plan is holding
             * stop being the ones this disk knows about - their slots stay right, which is
             * how both programs are found again below.
             */
            var record = KeygroupRecord(plan.From, plan.SourceProgram, plan.Index);

            foreach (var item in plan.Samples)
                if (!item.AlreadyHere)
                    WriteCopiedSample(item, plan.From.ReadFile(item.Source));

            // A zone whose sample arrived under a new name has to be told about it.
            if (renamed.Count > 0)
            {
                for (int z = 0; z < 2; z++)
                {
                    int at = KeygroupNameOffset + z * KeygroupZoneStride;
                    if (at + 10 > record.Length) continue;

                    string was = CleanName(record, at).Trim();
                    if (was.Length == 0) continue;

                    string now;
                    if (!renamed.TryGetValue(was, out now)) continue;

                    for (int i = 0; i < 10; i++)
                        record[at + i] = (byte)(i < now.Length ? now[i] : ' ');
                }
            }

            var target = EntryInSlot(plan.TargetProgram.Slot);
            if (target == null || target.Type != 'P')
                throw new InvalidOperationException("The program to copy into is no longer there.");

            return AppendKeygroup(target, record);
        }

        /// <summary>
        /// Put a keygroup record on the end of a program, growing the file by one record.
        ///
        /// The same shape as AddKeygroup, which duplicates one the program already holds;
        /// this one takes its record from outside. The pointers inside it - the chain, and
        /// the two zone pointers - are whatever the disk it came from had, and are left to
        /// RebuildPointers, which works them out from the names rather than carrying them.
        /// </summary>
        public int AppendKeygroup(AkaiEntry program, byte[] record)
        {
            if (program == null || program.Type != 'P')
                throw new ArgumentException("not a program", "program");
            if (record == null || record.Length != KeygroupSize)
                throw new ArgumentException("a keygroup is " + KeygroupSize + " bytes", "record");

            ArenaBase();                       // captured before the count changes

            int count = KeygroupCount(program);
            if (count >= MaxKeygroups)
                throw new InvalidOperationException("A program can hold at most " + MaxKeygroups + " keygroups.");

            var data = ReadFile(program);
            int arenaBase = KeygroupArenaBase(program);

            var bigger = new byte[data.Length + KeygroupSize];
            Array.Copy(data, bigger, data.Length);
            Array.Copy(record, 0, bigger, ProgHeaderSize + count * KeygroupSize, KeygroupSize);

            Relink(bigger, count + 1, arenaBase);
            ResizeFile(program, bigger);

            Modified = true;
            ParseDirectory();
            RebuildPointers();   // an extra keygroup moves the sample table, so every
            ParseDirectory();    // zone pointer past it changes too
            return count + 1;
        }

        /// <summary>One keygroup's 70 bytes, lifted out of the program that holds it.</summary>
        public static byte[] KeygroupRecord(AkaiDisk disk, AkaiEntry program, int index)
        {
            var body = disk.ReadFile(program);
            var record = new byte[KeygroupSize];

            int at = ProgHeaderSize + index * KeygroupSize;
            if (at + KeygroupSize > body.Length)
                throw new InvalidOperationException("That keygroup is past the end of the program.");

            Array.Copy(body, at, record, 0, KeygroupSize);
            return record;
        }

        /// <summary>The sample names one keygroup record's two zones point at.</summary>
        static List<string> ZoneNamesOf(byte[] record)
        {
            var names = new List<string>();

            for (int z = 0; z < 2; z++)
            {
                int at = KeygroupNameOffset + z * KeygroupZoneStride;
                if (at + 10 > record.Length) continue;

                string n = CleanName(record, at).Trim();
                if (n.Length == 0 || n == UnusedZone) continue;

                bool have = false;
                foreach (string x in names)
                    if (string.Equals(x, n, StringComparison.OrdinalIgnoreCase)) { have = true; break; }

                if (!have) names.Add(n);
            }
            return names;
        }

        // ------------------------------------------------------------ writing

        AkaiEntry WriteCopiedSample(CopyItem item, byte[] bytes)
        {
            var file = (byte[])bytes.Clone();
            SetFileName(file, item.To);

            /*
             * The two numbers that belong to the disk rather than to the sample, worked out
             * from the last sample already here - the same walk AddSample does, and the
             * reason the new entry has to land after that sample in the directory.
             */
            int ram = SampleRamBase, ptr = LoopDescriptorBase, lastSlot = -1;
            foreach (var s in Entries)
            {
                if (s.Type != 'S' || s.Slot <= lastSlot) continue;
                ram = s.MemoryAddress + RamSize(s.SampleCount);
                ptr = s.LoopDescriptorPtr + 10 * LoopRecords(s.SampleCount, s.LoopMode);
                lastSlot = s.Slot;
            }

            if (file.Length > 0x38)
            {
                PutU16(file, 0x28, ptr);
                file[0x36] = (byte)(ram & 0xFF);
                file[0x37] = (byte)((ram >> 8) & 0xFF);
                file[0x38] = (byte)((ram >> 16) & 0xFF);
            }

            return AddFile(item.To, 'S', file, lastSlot + 1);
        }

        AkaiEntry WriteCopiedProgram(CopyItem item, byte[] bytes, Dictionary<string, string> renamed)
        {
            var file = (byte[])bytes.Clone();
            SetFileName(file, item.To);

            // Program numbers belong to the disk, not to the program: two sharing one is a
            // conflict the sampler settles by playing whichever it reaches first.
            if (file.Length > 26) file[26] = (byte)FreeProgramNumber();

            // A zone whose sample arrived under a new name has to be told about it.
            if (renamed.Count > 0)
            {
                int count = file.Length > 23 ? file[23] : 0;
                for (int k = 0; k < count; k++)
                {
                    int kg = ProgHeaderSize + k * KeygroupSize;
                    for (int z = 0; z < 2; z++)
                    {
                        int at = kg + KeygroupNameOffset + z * KeygroupZoneStride;
                        if (at + 10 > file.Length) continue;

                        string was = CleanName(file, at).Trim();
                        if (was.Length == 0) continue;

                        string now;
                        if (!renamed.TryGetValue(was, out now)) continue;

                        for (int i = 0; i < 10; i++)
                            file[at + i] = (byte)(i < now.Length ? now[i] : ' ');
                    }
                }
            }

            // After the last program, so the P / O / D / S grouping survives.
            var progs = ProgramsInOrder();
            int slot = progs.Count > 0 ? progs[progs.Count - 1].Slot + 1 : 0;
            if (slot < Entries.Count) MakeRoomAt(slot);

            return AddFile(item.To, 'P', file, slot);
        }

        // ------------------------------------------------------------ helpers

        /// <summary>
        /// The file restates its own name in bytes 0..9, and it is that copy the S950 puts
        /// on its display rather than the directory entry. A renamed copy named only in the
        /// directory would go on showing the name it used to have.
        /// </summary>
        static void SetFileName(byte[] file, string name)
        {
            string clean = NormaliseName(name);
            for (int i = 0; i < 10 && i < file.Length; i++)
                file[i] = (byte)(i < clean.Length ? clean[i] : ' ');
        }

        /// <summary>Every sample name a program's zones point at, in keygroup order.</summary>
        public List<string> ZoneSampleNames(AkaiEntry program)
        {
            var names = new List<string>();
            if (program == null || program.Type != 'P') return names;

            foreach (var kg in Keygroups(program))
            {
                for (int z = 0; z < 2; z++)
                {
                    int at = KeygroupNameOffset + z * KeygroupZoneStride;
                    if (kg.Raw == null || at + 10 > kg.Raw.Length) continue;

                    string n = CleanName(kg.Raw, at).Trim();
                    if (n.Length == 0 || n == UnusedZone) continue;

                    bool have = false;
                    foreach (string x in names)
                        if (string.Equals(x, n, StringComparison.OrdinalIgnoreCase)) { have = true; break; }

                    if (!have) names.Add(n);
                }
            }
            return names;
        }

        /// <summary>
        /// Whether two files are the same one, ignoring the fields that belong to the disk
        /// they happen to be sitting on rather than to the file itself.
        ///
        /// Without this a sample copied onto a disk that already has it would look
        /// different - its RAM address alone would differ - and be copied a second time.
        /// </summary>
        static bool SameFile(char type, byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;

            var skip = new HashSet<int>();

            if (type == 'S')
            {
                skip.Add(0x28); skip.Add(0x29);                    // loop descriptor pointer
                skip.Add(0x36); skip.Add(0x37); skip.Add(0x38);    // where it loads in RAM
            }
            else if (type == 'P')
            {
                skip.Add(18); skip.Add(19);          // where its keygroups load
                skip.Add(26);                        // the program number

                int count = a.Length > 23 ? a[23] : 0;
                for (int k = 0; k < count; k++)
                {
                    int kg = ProgHeaderSize + k * KeygroupSize;
                    skip.Add(kg + KeygroupChainOffset);
                    skip.Add(kg + KeygroupChainOffset + 1);

                    for (int z = 0; z < 2; z++)
                    {
                        int po = kg + KeygroupNameOffset + z * KeygroupZoneStride + 16;
                        skip.Add(po); skip.Add(po + 1);
                    }
                }
            }

            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i] && !skip.Contains(i)) return false;

            return true;
        }

        /// <summary>
        /// A name like the one asked for that nothing of that type is using: NAME2, NAME3,
        /// and so on, shortened from the right when the number needs the room.
        /// </summary>
        static string FreeCopyName(string want, HashSet<string> taken)
        {
            string stem = NormaliseName(want).TrimEnd();
            if (stem.Length == 0) stem = "COPY";

            for (int n = 2; n < 1000; n++)
            {
                string suffix = n.ToString();
                string head = stem;

                if (head.Length + suffix.Length > 10)
                    head = head.Substring(0, Math.Max(1, 10 - suffix.Length)).TrimEnd();

                string candidate = head + suffix;
                if (!taken.Contains(candidate)) return candidate;
            }

            throw new InvalidOperationException("Could not find an unused name based on '" + want + "'.");
        }

        static string ShortSourceName(AkaiDisk d)
        {
            if (d == null || string.IsNullOrEmpty(d.Source)) return "the other disk";
            try { return System.IO.Path.GetFileNameWithoutExtension(d.Source); }
            catch (Exception) { return d.Source; }
        }
    }
}
