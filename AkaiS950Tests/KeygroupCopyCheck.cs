using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AkaiS950List;

/*
 * Copying a keygroup onto another program, within a disk and across disks.
 *
 * A keygroup is not a file, so this one writes differently from CopyCheck: the target
 * program grows by 70 bytes in place, and the keygroup arena moves under every program on
 * the disk. That makes the pointer check the important one here rather than a formality.
 *
 * Asserted on every copy:
 *
 *   - the keygroup arrived and says the same things. Key range, velocity switch, both
 *     envelopes and the zone settings identical to the source, and the zones naming the
 *     samples they named - following any rename the plan had to make
 *   - every sample the copied keygroup names is on the target disk
 *   - the target program's existing keygroups say exactly what they said before. A record
 *     spliced onto the end must not disturb the ones in front of it
 *   - every other file on the target is unchanged
 *   - the rebuilt image reloads with nothing left for RebuildPointers to fix
 *
 * Both directions are covered: from another disk, and from one program to another on the
 * same disk, which a file copy refuses but a keygroup copy has to allow.
 *
 *   KeygroupCopyCheck [folder of .hfe]
 */
static class KeygroupCopyCheck
{
    static int fails = 0, checks = 0, clashRenames = 0;
    static List<string> problems = new List<string>();

    static void Check(string what, bool ok)
    {
        checks++;
        if (!ok) { fails++; if (problems.Count < 25) problems.Add(what); }
    }

    static void Main(string[] args)
    {
        string where = args.Length > 0 ? args[0] : "disks";

        var files = Directory.Exists(where)
                  ? Directory.GetFiles(where, "*.hfe").OrderBy(f => f).ToArray()
                  : new[] { where };

        if (files.Length < 2)
        {
            Console.WriteLine("needs at least two disk images");
            Environment.Exit(1);
        }

        var raw = new Dictionary<string, byte[]>();
        foreach (string f in files) raw[f] = File.ReadAllBytes(f);

        int across = 0, within = 0, brought = 0, tooBig = 0, renamed = 0;

        for (int a = 0; a < files.Length; a++)
        {
            string targetFile = files[(a + 1) % files.Length];
            var source = AkaiDisk.Load(files[a]);

            foreach (var sourceProg in source.Entries.Where(e => e.Type == 'P').ToList())
            {
                int n = AkaiDisk.KeygroupCount(sourceProg);
                if (n == 0) continue;

                // The first keygroup of each program is enough; doing all of them would be
                // the same code path many thousands of times over.
                int index = 0;

                // ---------------------------------------------- across two disks
                {
                    var target = AkaiDisk.LoadFromBytes(targetFile, (byte[])raw[targetFile].Clone());
                    var targetProg = target.Entries.FirstOrDefault(e => e.Type == 'P');
                    if (targetProg == null) continue;

                    if (Run("across", files[a], sourceProg, index, source, target, targetProg,
                            ref brought, ref renamed, ref tooBig)) across++;
                }

                // ---------------------------------------------- within one disk
                {
                    var disk = AkaiDisk.LoadFromBytes(files[a], (byte[])raw[files[a]].Clone());

                    var from = disk.Entries.FirstOrDefault(e => e.Type == 'P' && e.Slot == sourceProg.Slot);
                    var into = disk.Entries.FirstOrDefault(e => e.Type == 'P' && e.Slot != sourceProg.Slot);
                    if (from == null || into == null) continue;

                    if (Run("within", files[a], from, index, disk, disk, into,
                            ref brought, ref renamed, ref tooBig)) within++;
                }
            }
        }

        /*
         * The corpus never collides, so the rename path has to be set up by hand: a sample
         * is planted on the target under the name a zone of the incoming keygroup needs,
         * holding different audio. The copy must rename what it brings, repoint the
         * keygroup's zone at the new name, and leave the planted file alone.
         */
        CollisionPass(files, raw);

        Console.WriteLine();
        Console.WriteLine(across + " keygroup copies across disks, " + within + " within one disk, " +
                          brought + " sample(s) brought along, " + (renamed + clashRenames) + " renamed, " +
                          tooBig + " refused for want of room");
        Console.WriteLine(checks + " checks, " + (fails == 0 ? "ALL PASSED" : fails + " FAILED"));

        foreach (string p in problems) Console.WriteLine("  " + p);
        Environment.Exit(fails == 0 ? 0 : 1);
    }

    static bool Run(string kind, string sourceFile, AkaiEntry sourceProg, int index,
                    AkaiDisk source, AkaiDisk target, AkaiEntry targetProg,
                    ref int brought, ref int renamed, ref int tooBig)
    {
        string who = kind + "  " + Path.GetFileNameWithoutExtension(sourceFile) + "/" +
                     sourceProg.Name.Trim() + " kg1 -> " + targetProg.Name.Trim();

        var wantRecord = AkaiDisk.KeygroupRecord(source, sourceProg, index);
        var beforeFiles = Snapshot(target);
        var beforeKgs = Keygroups(target, targetProg);
        int beforeCount = AkaiDisk.KeygroupCount(targetProg);
        int targetSlot = targetProg.Slot;

        var plan = target.PlanCopyKeygroup(source, sourceProg, index, targetProg);

        if (!plan.Ok)
        {
            bool space = plan.Problems.Any(p => p.Contains("Needs") || p.Contains("limit"));
            Check(who + ": refused for a reason other than room - " +
                  string.Join("; ", plan.Problems.ToArray()), space);
            if (space) tooBig++;
            return false;
        }

        brought += plan.Writes.Count;
        renamed += plan.Samples.Count(i => i.Renamed);

        int number = target.ApplyCopyKeygroup(plan);

        Check(who + ": it is the last keygroup", number == beforeCount + 1);

        var prog = target.EntryInSlot(targetSlot);
        Check(who + ": the program is still there", prog != null && prog.Type == 'P');
        if (prog == null) return true;

        Check(who + ": the program has one more keygroup",
              AkaiDisk.KeygroupCount(prog) == beforeCount + 1);

        var after = Keygroups(target, prog);

        // 1. The keygroups that were already there still say what they said.
        for (int k = 0; k < beforeKgs.Count && k < after.Count; k++)
            Check(who + ": keygroup " + (k + 1) + " of the target was disturbed",
                  SameKeygroup(beforeKgs[k], after[k]));

        // 2. The arrival matches the source, allowing for renames and the pointers.
        if (after.Count == beforeCount + 1)
        {
            var got = after[after.Count - 1];

            Check(who + ": key range carried over",
                  got[0] == wantRecord[0] && got[1] == wantRecord[1]);
            Check(who + ": velocity switch carried over", got[2] == wantRecord[2]);
            Check(who + ": the settings carried over", SameKeygroup(wantRecord, got));

            // 3. Every sample it names is on the target disk.
            foreach (string named in ZoneNames(got))
            {
                bool hopeless = !source.Entries.Any(
                    e => e.Type == 'S' && ZoneNames(wantRecord).Contains(named, StringComparer.OrdinalIgnoreCase)
                         && string.Equals(e.Name.Trim(), named, StringComparison.OrdinalIgnoreCase));

                bool wasHopeless = !ZoneNames(wantRecord).Any(orig =>
                    source.Entries.Any(e => e.Type == 'S' &&
                        string.Equals(e.Name.Trim(), orig, StringComparison.OrdinalIgnoreCase)));

                if (hopeless && wasHopeless) continue;

                bool here = target.Entries.Any(
                    e => e.Type == 'S' && string.Equals(e.Name.Trim(), named, StringComparison.OrdinalIgnoreCase));

                Check(who + ": zone names '" + named + "', which is not on the target", here);
            }
        }

        // 4. Nothing else on the target moved.
        CheckUntouched(who, beforeFiles, target, targetSlot);

        // 5. The sampler would believe the result.
        var rebuilt = AkaiDisk.LoadFromBytes("rebuilt.hfe", target.BuildImage("hfe"));
        int left = rebuilt.RebuildPointers();
        Check(who + ": " + left + " pointer(s) still wrong after the copy", left == 0);

        return true;
    }

    static void CollisionPass(string[] files, Dictionary<string, byte[]> raw)
    {
        for (int a = 0; a < files.Length; a++)
        {
            var source = AkaiDisk.Load(files[a]);

            // A keygroup whose first zone names a sample that is actually on this disk.
            AkaiEntry prog = null;
            string wanted = null;
            int index = -1;

            foreach (var p in source.Entries.Where(e => e.Type == 'P'))
            {
                int n = AkaiDisk.KeygroupCount(p);
                for (int k = 0; k < n && prog == null; k++)
                {
                    foreach (string name in ZoneNames(AkaiDisk.KeygroupRecord(source, p, k)))
                    {
                        if (!source.Entries.Any(e => e.Type == 'S' &&
                            string.Equals(e.Name.Trim(), name, StringComparison.OrdinalIgnoreCase))) continue;

                        prog = p; wanted = name; index = k;
                        break;
                    }
                }
                if (prog != null) break;
            }
            if (prog == null) continue;

            string targetFile = files[(a + 1) % files.Length];
            var target = AkaiDisk.LoadFromBytes(targetFile, (byte[])raw[targetFile].Clone());

            var targetProg = target.Entries.FirstOrDefault(e => e.Type == 'P');
            if (targetProg == null) continue;

            // Something audibly different, so a mix-up cannot pass as a match.
            var words = new short[4096];
            for (int i = 0; i < words.Length; i++) words[i] = (short)((i % 512) - 256);

            AkaiEntry planted;
            try { planted = target.AddSample(wanted, words, 20000, 60, 0, 'O'); }
            catch (Exception) { continue; }

            var mine = target.ReadFile(planted);
            int plantedSlot = planted.Slot;
            int targetSlot = targetProg.Slot;

            string who = "clash  " + Path.GetFileNameWithoutExtension(files[a]) + "/" +
                         prog.Name.Trim() + " kg" + (index + 1);

            var plan = target.PlanCopyKeygroup(source, prog, index, targetProg);
            if (!plan.Ok) continue;

            var item = plan.Samples.FirstOrDefault(
                i => string.Equals(i.From, wanted, StringComparison.OrdinalIgnoreCase));

            Check(who + ": the clashing sample is in the plan", item != null);
            if (item == null) continue;

            Check(who + ": '" + wanted + "' was renamed rather than overwriting", item.Renamed);
            if (item.Renamed) clashRenames++;

            target.ApplyCopyKeygroup(plan);

            // The target's own sample must be exactly as it was.
            var still = target.EntryInSlot(plantedSlot);
            Check(who + ": the target kept its own '" + wanted + "'",
                  still != null && string.Equals(still.Name.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
            if (still != null)
                Check(who + ": the target's '" + wanted + "' is untouched", Same(mine, target.ReadFile(still)));

            // The arrival carries the source's audio under its new name.
            var brought = target.Entries.FirstOrDefault(e => e.Type == 'S' && e.Name.Trim() == item.To);
            Check(who + ": the renamed copy '" + item.To + "' is there", brought != null);

            if (brought != null)
            {
                var wantPcm = source.SamplePcm(item.Source);
                var gotPcm = target.SamplePcm(brought);
                Check(who + ": '" + item.To + "' holds the source's audio",
                      wantPcm.Length == gotPcm.Length && Same16(wantPcm, gotPcm));
            }

            // And the copied keygroup points at the new name, not the target's own file.
            var prog2 = target.EntryInSlot(targetSlot);
            if (prog2 != null)
            {
                var last = AkaiDisk.KeygroupRecord(target, prog2, AkaiDisk.KeygroupCount(prog2) - 1);
                var names = ZoneNames(last);

                Check(who + ": its zone follows the rename to '" + item.To + "'",
                      names.Contains(item.To, StringComparer.OrdinalIgnoreCase));
                Check(who + ": no zone still names the target's '" + wanted + "'",
                      !names.Contains(wanted, StringComparer.OrdinalIgnoreCase));
            }

            var rebuilt = AkaiDisk.LoadFromBytes("rebuilt.hfe", target.BuildImage("hfe"));
            Check(who + ": pointers consistent after a renaming keygroup copy",
                  rebuilt.RebuildPointers() == 0);
        }
    }

    static bool Same16(short[] a, short[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    static List<byte[]> Keygroups(AkaiDisk d, AkaiEntry program)
    {
        var list = new List<byte[]>();
        int n = AkaiDisk.KeygroupCount(program);
        for (int k = 0; k < n; k++) list.Add(AkaiDisk.KeygroupRecord(d, program, k));
        return list;
    }

    /// <summary>
    /// Two keygroups compared as the sampler hears them, ignoring the chain and the zone
    /// pointers - both are recomputed from the layout every time anything on the disk
    /// changes, so neither says anything about the keygroup itself.
    /// </summary>
    static bool SameKeygroup(byte[] a, byte[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;

        var skip = new HashSet<int>();
        skip.Add(AkaiDisk.KeygroupChainOffset);
        skip.Add(AkaiDisk.KeygroupChainOffset + 1);

        for (int z = 0; z < 2; z++)
        {
            int po = AkaiDisk.KeygroupNameOffset + z * AkaiDisk.KeygroupZoneStride + 16;
            skip.Add(po); skip.Add(po + 1);
        }

        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i] && !skip.Contains(i)) return false;

        return true;
    }

    static List<string> ZoneNames(byte[] record)
    {
        var names = new List<string>();
        for (int z = 0; z < 2; z++)
        {
            int at = AkaiDisk.KeygroupNameOffset + z * AkaiDisk.KeygroupZoneStride;
            var chars = new char[10];
            for (int i = 0; i < 10; i++) chars[i] = (char)record[at + i];

            string n = new string(chars).Trim();
            if (n.Length == 0 || n == "2 SAMPLE") continue;
            if (!names.Contains(n, StringComparer.OrdinalIgnoreCase)) names.Add(n);
        }
        return names;
    }

    static Dictionary<string, byte[]> Snapshot(AkaiDisk d)
    {
        var map = new Dictionary<string, byte[]>();
        foreach (var e in d.Entries) map[e.Type + ":" + e.Name.Trim()] = d.ReadFile(e);
        return map;
    }

    /// <summary>
    /// Everything that was on the target before, still there and still itself. The program
    /// being copied into is excluded - it is supposed to have grown - and the other
    /// programs are compared without the pointers the disk recomputes around the change.
    /// </summary>
    static void CheckUntouched(string who, Dictionary<string, byte[]> before, AkaiDisk after, int skipSlot)
    {
        foreach (var kv in before)
        {
            char type = kv.Key[0];
            string name = kv.Key.Substring(2);

            var e = after.Entries.FirstOrDefault(x => x.Type == type && x.Name.Trim() == name);
            if (e == null) { Check(who + ": '" + name + "' disappeared from the target", false); continue; }
            if (e.Slot == skipSlot) continue;

            var now = after.ReadFile(e);
            bool ok = type == 'P' ? SameProgramContent(kv.Value, now) : Same(kv.Value, now);

            Check(who + ": '" + name + "' on the target was altered", ok);
        }
    }

    static bool SameProgramContent(byte[] a, byte[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;

        var skip = new HashSet<int> { 18, 19 };
        int count = a.Length > 23 ? a[23] : 0;

        for (int k = 0; k < count; k++)
        {
            int kg = AkaiDisk.ProgHeaderSize + k * AkaiDisk.KeygroupSize;
            skip.Add(kg + AkaiDisk.KeygroupChainOffset);
            skip.Add(kg + AkaiDisk.KeygroupChainOffset + 1);

            for (int z = 0; z < 2; z++)
            {
                int po = kg + AkaiDisk.KeygroupNameOffset + z * AkaiDisk.KeygroupZoneStride + 16;
                skip.Add(po); skip.Add(po + 1);
            }
        }

        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i] && !skip.Contains(i)) return false;

        return true;
    }

    static bool Same(byte[] a, byte[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }
}
