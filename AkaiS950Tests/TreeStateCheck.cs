using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using AkaiS950List;
using AkaiS950Studio;

/*
 * The disk tree keeping its shape across a rebuild.
 *
 * Every structural edit rebuilds the whole tree, because that is the only way to be sure
 * the tree says what the disks say. That used to throw away which disks and groups were
 * open, which with a stick of images loaded is a good deal of work to lose over copying
 * one keygroup.
 *
 * This drives the real TreeView and the real node construction - MainForm.BuildDiskNode,
 * the same one RebuildTree uses - rather than a copy of them that could drift.
 *
 * The cases that matter are the ones where the tree is not the same shape afterwards:
 *
 *   - a group whose count changed. The node's Text carries the count, so matching on it
 *     would fail exactly when an edit had just happened, which is the only time this runs
 *   - a group that did not exist before and does now. Empty groups are not added at all,
 *     so a disk gaining its first sample shifts every index below it
 *   - a disk deliberately collapsed. A rebuild must not helpfully reopen it
 *
 *   TreeStateCheck [folder of .hfe]
 */
static class TreeStateCheck
{
    static int fails = 0, checks = 0;
    static List<string> problems = new List<string>();

    static void Check(string what, bool ok)
    {
        checks++;
        if (!ok) { fails++; problems.Add(what); }
    }

    [STAThread]
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

        var disks = files.Select(f => AkaiDisk.Load(f)).ToList();

        // ---------------------------------------------- an ordinary rebuild
        {
            var tree = Build(disks);

            // Open the second disk and its Samples group; leave the rest shut.
            tree.Nodes[1].Expand();
            var samples = Group(tree.Nodes[1], "Samples");
            Check("the disks have a Samples group to open", samples != null);
            if (samples != null) samples.Expand();

            Rebuild(tree, disks);

            Check("the open disk is still open", tree.Nodes[1].IsExpanded);
            Check("the closed disk is still closed", !tree.Nodes[0].IsExpanded);

            var after = Group(tree.Nodes[1], "Samples");
            Check("the open group is still open", after != null && after.IsExpanded);

            var programs = Group(tree.Nodes[1], "Programs");
            Check("the group that was shut is still shut", programs == null || !programs.IsExpanded);
        }

        // ------------------------------------- a rebuild after the count changed
        {
            var tree = Build(disks);
            tree.Nodes[0].Expand();
            var samples = Group(tree.Nodes[0], "Samples");
            if (samples != null) samples.Expand();

            string textBefore = samples != null ? samples.Text : "";

            // Copy a sample onto the first disk, which is what changes the count.
            var source = disks[1];
            var what = source.Entries.First(e => e.Type == 'S');
            var plan = disks[0].PlanCopy(source, what);

            Check("a sample can be copied to set the case up", plan.Ok);
            if (plan.Ok)
            {
                disks[0].ApplyCopy(plan);

                Rebuild(tree, disks);

                var now = Group(tree.Nodes[0], "Samples");
                Check("the group is still there after the count changed", now != null);
                Check("its count really did change", now != null && now.Text != textBefore);
                Check("it is still open although its text changed", now != null && now.IsExpanded);
                Check("the disk is still open", tree.Nodes[0].IsExpanded);
            }
        }

        // ------------------------- a group that did not exist until the edit
        {
            /*
             * A disk with programs but no samples: its Samples group is absent, so every
             * index below it moves when one arrives.
             *
             * The programs have to go first. A sample a program's zones name cannot be
             * deleted, so clearing the samples while the programs are still there quietly
             * leaves the disk exactly as it was - and this case with it.
             */
            var bare = AkaiDisk.LoadFromBytes("bare.hfe", disks[0].BuildImage("hfe"));

            // Always the first one left, never a list captured up front: a delete closes
            // the directory up behind it, so every slot after it moves.
            for (int guard = 0; guard < 200; guard++)
            {
                var p = bare.Entries.FirstOrDefault(e => e.Type == 'P');
                if (p == null) break;
                try { bare.DeleteProgram(p); } catch (Exception) { break; }
            }
            for (int guard = 0; guard < 200; guard++)
            {
                var s = bare.Entries.FirstOrDefault(e => e.Type == 'S');
                if (s == null) break;
                try { bare.DeleteSample(s); } catch (Exception) { break; }
            }

            // One program back, so there is a group above the one that will appear.
            try { bare.AddProgram("BARE", null); } catch (Exception) { }

            Check("the bare disk has a program and no samples",
                  bare.Entries.Any(e => e.Type == 'P') && !bare.Entries.Any(e => e.Type == 'S'));

            if (!bare.Entries.Any(e => e.Type == 'S') && bare.Entries.Any(e => e.Type == 'P'))
            {
                var only = new List<AkaiDisk> { bare };
                var tree = Build(only);

                tree.Nodes[0].Expand();
                var programs = Group(tree.Nodes[0], "Programs");
                Check("a bare disk still shows its Programs", programs != null);
                if (programs != null) programs.Expand();

                Check("and shows no Samples group at all", Group(tree.Nodes[0], "Samples") == null);

                // Now give it one, which inserts a group below Programs.
                var source = disks[1];
                var sample = source.Entries.First(e => e.Type == 'S');
                var plan = bare.PlanCopy(source, sample);

                if (plan.Ok)
                {
                    bare.ApplyCopy(plan);
                    Rebuild(tree, only);

                    Check("a Samples group appeared", Group(tree.Nodes[0], "Samples") != null);

                    var p2 = Group(tree.Nodes[0], "Programs");
                    Check("Programs is still open although a group arrived beside it",
                          p2 != null && p2.IsExpanded);

                    var s2 = Group(tree.Nodes[0], "Samples");
                    Check("the new group is shut, having never been opened",
                          s2 != null && !s2.IsExpanded);
                }
            }
        }

        // ------------------------------- a lone disk that was closed on purpose
        {
            var one = new List<AkaiDisk> { disks[0] };

            var tree = new TreeView();
            var open = MainForm.CaptureTreeState(tree);         // nothing there yet
            Fill(tree, one);
            MainForm.RestoreTreeState(tree, open);
            if (!open.HadNodes && tree.Nodes.Count == 1) tree.Nodes[0].Expand();

            Check("a lone disk opens itself when it first arrives", tree.Nodes[0].IsExpanded);

            tree.Nodes[0].Collapse();
            Rebuild(tree, one);

            Check("and stays shut once it has been closed on purpose", !tree.Nodes[0].IsExpanded);
        }

        Console.WriteLine();
        Console.WriteLine(checks + " checks, " + (fails == 0 ? "ALL PASSED" : fails + " FAILED"));
        foreach (string p in problems) Console.WriteLine("  " + p);
        Environment.Exit(fails == 0 ? 0 : 1);
    }

    /// <summary>A tree built the way the program builds it, from nothing.</summary>
    static TreeView Build(IList<AkaiDisk> disks)
    {
        var tree = new TreeView();
        Fill(tree, disks);
        if (tree.Nodes.Count == 1) tree.Nodes[0].Expand();
        return tree;
    }

    /// <summary>A rebuild, in the order RebuildTree does it: capture, clear, fill, restore.</summary>
    static void Rebuild(TreeView tree, IList<AkaiDisk> disks)
    {
        var open = MainForm.CaptureTreeState(tree);

        tree.BeginUpdate();
        tree.Nodes.Clear();
        Fill(tree, disks);
        MainForm.RestoreTreeState(tree, open);
        tree.EndUpdate();

        if (!open.HadNodes && tree.Nodes.Count == 1) tree.Nodes[0].Expand();
    }

    static void Fill(TreeView tree, IList<AkaiDisk> disks)
    {
        foreach (var d in disks)
            tree.Nodes.Add(MainForm.BuildDiskNode(d, Path.GetFileNameWithoutExtension(d.Source)));
    }

    static TreeNode Group(TreeNode diskNode, string name)
    {
        foreach (TreeNode n in diskNode.Nodes) if (n.Name == name) return n;
        return null;
    }
}
