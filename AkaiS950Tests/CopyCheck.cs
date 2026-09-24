using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AkaiS950List;

/*
 * Copying samples and programs between disk images, across every pair of disks available.
 *
 * This writes to a disk image, so the checks are less about the copy arriving and more
 * about what it must not disturb. Four things are asserted every time:
 *
 *   - the copy is the same sound. Decoded audio identical to the source, and the header
 *     fields that describe it - rate, tuning, loop markers, loop mode - carried over
 *   - every zone of a copied program names a sample that is actually on the target,
 *     following any rename the plan had to make
 *   - nothing already on the target changed. Every file that was there before is still
 *     there, byte for byte
 *   - the result is a disk the sampler would believe. The image is rebuilt and reloaded,
 *     and RebuildPointers on the reloaded copy must report NOTHING left to fix - a
 *     pointer this code failed to recompute would show up there as a non-zero count
 *
 * A plan that does not fit is not a failure; an 800K disk fills up, and refusing is the
 * correct answer. Those are counted separately.
 *
 *   CopyCheck [folder of .hfe]
 */
static class CopyCheck
{
    static int fails = 0, checks = 0;
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
            Console.WriteLine("needs at least two disk images to copy between");
            Environment.Exit(1);
        }

        var raw = new Dictionary<string, byte[]>();
        foreach (string f in files) raw[f] = File.ReadAllBytes(f);

        int copiedSamples = 0, copiedPrograms = 0, tooBig = 0, renames = 0, skipped = 0;

        for (int a = 0; a < files.Length; a++)
        {
            // Into the next disk round the ring, so every image is both source and target.
            string targetFile = files[(a + 1) % files.Length];
            var source = AkaiDisk.Load(files[a]);

            foreach (var what in source.Entries.Where(e => e.Type == 'S' || e.Type == 'P').ToList())
            {
                // A fresh target every time, so one copy cannot mask the next.
                var target = AkaiDisk.LoadFromBytes(targetFile, (byte[])raw[targetFile].Clone());

                var before = Snapshot(target);
                int freeBefore = target.FreeBlocks;

                var plan = target.PlanCopy(source, what);

                if (!plan.Ok)
                {
                    // The only acceptable reason to refuse is that it will not fit.
                    bool space = plan.Problems.Any(p => p.Contains("Needs"));
                    Check(Where(files[a], what) + ": refused for a reason other than room - " +
                          string.Join("; ", plan.Problems.ToArray()), space);
                    if (space) tooBig++;
                    continue;
                }

                var landed = target.ApplyCopy(plan);

                renames += plan.Items.Count(i => i.Renamed);
                skipped += plan.Items.Count(i => i.AlreadyHere);
                if (what.Type == 'S') copiedSamples++; else copiedPrograms++;

                string who = Where(files[a], what);

                Check(who + ": something landed", landed.Count == plan.Writes.Count);

                // 1. Nothing that was already there changed.
                CheckUntouched(who, before, target);

                // 2. The copy is the same sound.
                foreach (var item in plan.Items)
                {
                    if (item.AlreadyHere || item.Type != 'S') continue;

                    var got = target.Entries.FirstOrDefault(
                        e => e.Type == 'S' && e.Name.Trim() == item.To);

                    if (got == null) { Check(who + ": sample '" + item.To + "' is not there", false); continue; }

                    var wantPcm = source.SamplePcm(item.Source);
                    var gotPcm = target.SamplePcm(got);

                    Check(who + ": '" + item.To + "' audio length", wantPcm.Length == gotPcm.Length);
                    Check(who + ": '" + item.To + "' audio identical", Same(wantPcm, gotPcm));

                    Check(who + ": '" + item.To + "' rate", got.SampleRate == item.Source.SampleRate);
                    Check(who + ": '" + item.To + "' tuning", got.Tuning == item.Source.Tuning);
                    Check(who + ": '" + item.To + "' loop mode", got.LoopMode == item.Source.LoopMode);
                    Check(who + ": '" + item.To + "' loop start", got.LoopStart == item.Source.LoopStart);
                    Check(who + ": '" + item.To + "' loop end", got.LoopEnd == item.Source.LoopEnd);
                    Check(who + ": '" + item.To + "' loop length", got.LoopLength == item.Source.LoopLength);
                }

                // 3. A copied program's zones resolve on the target.
                if (what.Type == 'P')
                {
                    var item = plan.Items.Last();
                    var prog = target.Entries.FirstOrDefault(
                        e => e.Type == 'P' && e.Name.Trim() == item.To);

                    if (prog == null) { Check(who + ": the program is not there", false); }
                    else
                    {
                        Check(who + ": keygroup count carried over",
                              AkaiDisk.KeygroupCount(prog) == AkaiDisk.KeygroupCount(what));

                        // Names the source could not resolve either are not this copy's fault.
                        var hopeless = new HashSet<string>(
                            source.ZoneSampleNames(what)
                                  .Where(n => !source.Entries.Any(
                                      e => e.Type == 'S' &&
                                           string.Equals(e.Name.Trim(), n, StringComparison.OrdinalIgnoreCase))),
                            StringComparer.OrdinalIgnoreCase);

                        foreach (string named in target.ZoneSampleNames(prog))
                        {
                            if (hopeless.Contains(named)) continue;

                            bool here = target.Entries.Any(
                                e => e.Type == 'S' &&
                                     string.Equals(e.Name.Trim(), named, StringComparison.OrdinalIgnoreCase));

                            Check(who + ": zone names '" + named + "', which is not on the target", here);
                        }
                    }
                }

                // 4. The blocks add up.
                Check(who + ": free blocks fell by what the plan said",
                      target.FreeBlocks == freeBefore - plan.Blocks);

                // 5. The sampler would believe the result.
                var rebuilt = AkaiDisk.LoadFromBytes("rebuilt.hfe", target.BuildImage("hfe"));

                Check(who + ": the rebuilt image has the same files",
                      rebuilt.Entries.Count == target.Entries.Count);

                int left = rebuilt.RebuildPointers();
                Check(who + ": " + left + " pointer(s) still wrong after the copy", left == 0);
            }
        }

        /*
         * The corpus never collides - its disks name their files differently - so the two
         * policies that only fire on a clash have to be set up by hand. They are the whole
         * of what makes a copy safe to repeat, so they cannot go unchecked.
         */
        CollisionPass(files, raw);

        Console.WriteLine();
        Console.WriteLine(files.Length + " disks: " + copiedSamples + " sample copies, " +
                          copiedPrograms + " program copies, " + renames + " renamed, " +
                          skipped + " already present, " + tooBig + " refused for want of room");
        Console.WriteLine(checks + " checks, " + (fails == 0 ? "ALL PASSED" : fails + " FAILED"));

        foreach (string p in problems) Console.WriteLine("  " + p);
        Environment.Exit(fails == 0 ? 0 : 1);
    }

    static string Where(string disk, AkaiEntry e)
    {
        return Path.GetFileNameWithoutExtension(disk) + "/" + e.Name.Trim();
    }

    /// <summary>
    /// The two cases the corpus cannot produce: a name already taken by a different file,
    /// and a copy made twice.
    ///
    /// The first is built by planting a sample on the target under a name the incoming
    /// program needs, holding different audio. The copy must then rename what it brings
    /// and repoint the program's zones at the new name, leaving the target's own sample
    /// alone - anything else would silently change how the target's other programs sound.
    /// </summary>
    static void CollisionPass(string[] files, Dictionary<string, byte[]> raw)
    {
        Console.WriteLine();

        for (int a = 0; a < files.Length; a++)
        {
            var source = AkaiDisk.Load(files[a]);

            // A program whose zones name a sample that is actually on this disk.
            AkaiEntry prog = null;
            string wanted = null;

            foreach (var p in source.Entries.Where(e => e.Type == 'P'))
            {
                foreach (string n in source.ZoneSampleNames(p))
                {
                    if (!source.Entries.Any(e => e.Type == 'S' &&
                        string.Equals(e.Name.Trim(), n, StringComparison.OrdinalIgnoreCase))) continue;
                    prog = p; wanted = n; break;
                }
                if (prog != null) break;
            }
            if (prog == null) continue;

            string targetFile = files[(a + 1) % files.Length];
            string who = Path.GetFileNameWithoutExtension(files[a]) + "/" + prog.Name.Trim();

            // ---- a different sample already holding the name the program needs ----
            {
                var target = AkaiDisk.LoadFromBytes(targetFile, (byte[])raw[targetFile].Clone());

                // Something audibly different, so a mix-up cannot pass as a match.
                var words = new short[4096];
                for (int i = 0; i < words.Length; i++) words[i] = (short)((i % 512) - 256);

                AkaiEntry planted;
                try { planted = target.AddSample(wanted, words, 20000, 60, 0, 'O'); }
                catch (Exception) { continue; }     // no room to set the case up; try the next disk

                var mine = target.ReadFile(planted);

                var plan = target.PlanCopy(source, prog);
                if (!plan.Ok) continue;

                var item = plan.Items.FirstOrDefault(
                    i => string.Equals(i.From, wanted, StringComparison.OrdinalIgnoreCase));

                Check(who + ": the clashing sample is in the plan", item != null);
                if (item == null) continue;

                Check(who + ": '" + wanted + "' was renamed rather than overwriting", item.Renamed);
                Check(who + ": it was not treated as already present", !item.AlreadyHere);

                target.ApplyCopy(plan);

                // The target's own sample must be exactly as it was.
                var still = target.Entries.FirstOrDefault(
                    e => e.Type == 'S' && string.Equals(e.Name.Trim(), wanted, StringComparison.OrdinalIgnoreCase));

                Check(who + ": the target kept its own '" + wanted + "'", still != null);
                if (still != null)
                    Check(who + ": the target's '" + wanted + "' is untouched", Same(mine, target.ReadFile(still)));

                // The arrival carries the source's audio under its new name.
                var brought = target.Entries.FirstOrDefault(
                    e => e.Type == 'S' && e.Name.Trim() == item.To);

                Check(who + ": the renamed copy '" + item.To + "' is there", brought != null);

                if (brought != null)
                {
                    var wantPcm = source.SamplePcm(item.Source);
                    Check(who + ": '" + item.To + "' holds the source's audio",
                          Same(wantPcm, target.SamplePcm(brought)));
                }

                // And the program that came with it points at the new name, not the old.
                var copied = target.Entries.FirstOrDefault(
                    e => e.Type == 'P' && e.Name.Trim() == plan.Items.Last().To);

                Check(who + ": the copied program is there", copied != null);

                if (copied != null)
                {
                    var names = target.ZoneSampleNames(copied);
                    Check(who + ": its zones follow the rename to '" + item.To + "'",
                          names.Contains(item.To, StringComparer.OrdinalIgnoreCase));
                    Check(who + ": no zone still names the target's '" + wanted + "'",
                          !names.Contains(wanted, StringComparer.OrdinalIgnoreCase));
                }

                var rebuilt = AkaiDisk.LoadFromBytes("rebuilt.hfe", target.BuildImage("hfe"));
                Check(who + ": pointers consistent after a renaming copy", rebuilt.RebuildPointers() == 0);
            }

            // ---- the same program copied twice ----
            {
                var target = AkaiDisk.LoadFromBytes(targetFile, (byte[])raw[targetFile].Clone());

                var first = target.PlanCopy(source, prog);
                if (!first.Ok) continue;
                target.ApplyCopy(first);

                int samplesAfterFirst = target.Entries.Count(e => e.Type == 'S');

                var again = target.PlanCopy(source, prog);
                if (!again.Ok) continue;

                foreach (var i in again.Items.Where(i => i.Type == 'S'))
                    Check(who + ": '" + i.From + "' recognised as already there on a second copy",
                          i.AlreadyHere);

                Check(who + ": a second copy costs no sample blocks",
                      again.Items.Where(i => i.Type == 'S').All(i => i.Blocks == 0));

                /*
                 * The program is identical too - the fields that differ between disks are
                 * the ones SameFile ignores - so the whole second copy is recognised as
                 * already present and writes nothing at all. Copying twice is a no-op
                 * rather than a way to get a second numbered copy, which is what "skip if
                 * identical" has to mean if it is to mean anything.
                 */
                Check(who + ": the program is recognised as already there too",
                      again.Items.Last().AlreadyHere);
                Check(who + ": a second copy writes nothing", again.Writes.Count == 0);
                Check(who + ": and costs nothing", again.Blocks == 0 && again.Slots == 0);

                int before = target.Entries.Count;
                target.ApplyCopy(again);

                Check(who + ": no duplicate samples were written",
                      target.Entries.Count(e => e.Type == 'S') == samplesAfterFirst);
                Check(who + ": the directory did not grow", target.Entries.Count == before);

                var rebuilt = AkaiDisk.LoadFromBytes("rebuilt.hfe", target.BuildImage("hfe"));
                Check(who + ": pointers consistent after copying twice", rebuilt.RebuildPointers() == 0);
            }
        }
    }

    /// <summary>Every file on the disk, by type and name, with its bytes.</summary>
    static Dictionary<string, byte[]> Snapshot(AkaiDisk d)
    {
        var map = new Dictionary<string, byte[]>();
        foreach (var e in d.Entries)
            map[e.Type + ":" + e.Name.Trim()] = d.ReadFile(e);
        return map;
    }

    static void CheckUntouched(string who, Dictionary<string, byte[]> before, AkaiDisk after)
    {
        foreach (var kv in before)
        {
            char type = kv.Key[0];
            string name = kv.Key.Substring(2);

            var e = after.Entries.FirstOrDefault(x => x.Type == type && x.Name.Trim() == name);
            if (e == null) { Check(who + ": '" + name + "' disappeared from the target", false); continue; }

            var now = after.ReadFile(e);

            // A program already on the target can legitimately have its pointers rebuilt
            // around the new arrival; its keygroups and settings must not move.
            bool ok = type == 'P'
                    ? SameProgramContent(kv.Value, now)
                    : Same(kv.Value, now);

            Check(who + ": '" + name + "' on the target was altered", ok);
        }
    }

    /// <summary>
    /// A program compared as the sampler hears it, ignoring the pointers the disk
    /// recomputes around any change: the load address, the chain, and the zone pointers.
    /// </summary>
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

    static bool Same(short[] a, short[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }
}
