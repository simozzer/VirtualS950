using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using AkaiS950List;

namespace AkaiS950Studio
{
    /// <summary>
    /// The commands that add or remove whole files: deleting a sample, creating and
    /// deleting programs, and slicing a break into one-shots.
    ///
    /// They differ from the parameter edits in that the directory itself changes, so each
    /// one rebuilds the tree and reselects afterwards rather than refreshing the panes in
    /// place. All of them go through PushUndo first, so any of them can be taken back.
    /// </summary>
    public sealed partial class MainForm
    {
        // ------------------------------------------------- the key range, by pointing

        /// <summary>
        /// Escape calls off an armed keyboard wherever the focus happens to be, which is
        /// what anyone expects of a mode they have just entered by accident.
        /// </summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape && _piano != null && _piano.RangeArmed)
            {
                _piano.CancelRange();
                SetStatus("Key range left as it was.");
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }


        /// <summary>
        /// Arms the keyboard: the next click on it is the low key of the selected
        /// keygroups, and the one after it the high. Two boxes wanting MIDI numbers are a
        /// poor way to say something the keyboard says better.
        /// </summary>
        void ArmKeyRange()
        {
            var f = SelectedFile;
            if (f == null || f.Entry.Type != 'P') return;
            if (SelectedKeygroups().Count == 0) return;

            _piano.ArmRange();
            SetStatus("Click the low key on the keyboard, then the high key.  " +
                      "The same key twice gives a one-key group; Esc or a right-click cancels.");
        }

        /// <summary>
        /// Both ends, clicked. They go in under one undo - a range is a single edit to
        /// anyone using it - and reach every selected keygroup.
        /// </summary>
        void OnRangePicked(int low, int high)
        {
            var f = SelectedFile;
            if (f == null || f.Entry.Type != 'P') return;

            var picked = SelectedKeygroups();
            if (picked.Count == 0) return;

            int count = AkaiDisk.KeygroupCount(f.Entry);
            PushUndo(f.Disk, picked.Count > 1 ? "key range on " + picked.Count + " keygroups"
                                             : "key range on keygroup " + (picked[0] + 1));
            foreach (int i in picked)
            {
                if (i < 0 || i >= count) continue;
                int at = AkaiDisk.ProgHeaderSize + i * AkaiDisk.KeygroupSize;
                f.Disk.PokeFile(f.Entry, at + 1, (byte)low);     // low key
                f.Disk.PokeFile(f.Entry, at + 0, (byte)high);    // high key
            }

            _editDisk = f.Disk;
            f.Disk.Modified = true;
            ShowFile(f);
            UpdateCommands();

            SetStatus((picked.Count > 1 ? picked.Count + " keygroups cover "
                                        : "Keygroup " + (picked[0] + 1) + " covers ") +
                      PianoKeyboard.NameOf(low) + " - " + PianoKeyboard.NameOf(high) +
                      (low == high ? "  (one key)" : "  (" + (high - low + 1) + " keys)") +
                      ".  Unsaved changes.");
        }

        // ---------------------------------------------------------- finding a loop

        /// <summary>
        /// Finds a loop in a sample and writes it: the end, the length and the mode.
        ///
        /// A loop is a header edit rather than a change to the audio, but setting the
        /// mode moves the loop descriptor pointers of every later sample, so this
        /// rebuilds and reselects the way the structural commands do rather than
        /// refreshing the panes in place.
        /// </summary>
        void FindLoopFor(FileRef f)
        {
            if (f == null || f.Entry.Type != 'S') return;
            var e = f.Entry;

            short[] words;
            try { words = f.Disk.SampleWords12(e); }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Could not read the sample",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (words == null || words.Length < 512)
            {
                MessageBox.Show(this, e.Name.Trim() + " is too short to loop.",
                                "Find loop", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dlg = new LoopDialog(e, words))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Found == null) return;
                var got = dlg.Found;

                try
                {
                    _audio.Stop();
                    PushUndo(f.Disk, "loop " + e.Name);
                    f.Disk.SetLoop(e, got.End, got.Length, dlg.LoopMode);

                    _editDisk = f.Disk;
                    int slot = e.Slot;
                    RebuildTree();
                    foreach (var fresh in f.Disk.Entries)
                        if (fresh.Slot == slot) { SelectFileNode(f.Disk, fresh); break; }

                    UpdateCommands();
                    SetStatus("Looped " + e.Name.Trim() + " over the last " +
                              got.Length.ToString("N0") + " words (" +
                              got.Seconds(e.SampleRate).ToString("0.000",
                                  CultureInfo.InvariantCulture) + " s), match " +
                              got.Match.ToString("0.000", CultureInfo.InvariantCulture) +
                              ".  Unsaved changes.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Could not set that loop",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // ------------------------------------------------------- deleting a sample

        /// <summary>
        /// Deletes a sample, after naming every keygroup zone that will be emptied by it.
        /// That list is the whole point of the confirmation: a sample is referenced by
        /// name from programs that may not be on screen, and the S950 gives no hint that
        /// a zone has gone quiet.
        /// </summary>
        void DeleteSampleFile(FileRef f)
        {
            var e = f.Entry;
            var users = f.Disk.SampleUsers(e.Name);

            string where;
            if (users.Count == 0)
                where = "No keygroup on this disk refers to it.";
            else
            {
                var lines = users.Take(8).Select(u => "    " + u.ToString()).ToList();
                if (users.Count > 8) lines.Add("    ... and " + (users.Count - 8) + " more");
                where = "These " + users.Count + " keygroup zone" + (users.Count == 1 ? "" : "s") +
                        " will be emptied:" + Environment.NewLine + string.Join(Environment.NewLine, lines.ToArray());
            }

            int blocks = AkaiDisk.BlocksFor(e.Length);

            var ask = MessageBox.Show(this,
                "Delete " + e.Name.Trim() + "?" + Environment.NewLine + Environment.NewLine +
                "Words     " + e.SampleCount.ToString("N0") + "  (" +
                e.Seconds.ToString("0.00", CultureInfo.InvariantCulture) + " s at " +
                e.SampleRate.ToString("N0") + " Hz)" + Environment.NewLine +
                "Frees     " + blocks + " block" + (blocks == 1 ? "" : "s") +
                Environment.NewLine + Environment.NewLine +
                where + Environment.NewLine + Environment.NewLine +
                "Samples after it move down in sampler memory and the zones pointing at " +
                "them are pulled back to match." + Environment.NewLine + Environment.NewLine +
                "Go ahead?",
                "Delete sample", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (ask != DialogResult.OK) return;

            try
            {
                _audio.Stop();
                PushUndo(f.Disk, "delete " + e.Name.Trim());

                string gone = e.Name.Trim();
                var r = f.Disk.DeleteSample(e);

                _editDisk = f.Disk;
                RebuildTree();
                SelectDiskNode(f.Disk);
                UpdateCommands();

                SetStatus("Deleted " + gone + "  -  " + r.ZonesCleared + " zone" +
                          (r.ZonesCleared == 1 ? "" : "s") + " emptied, " +
                          r.PointersAdjusted + " pointer" + (r.PointersAdjusted == 1 ? "" : "s") +
                          " adjusted, " + r.BlocksFreed + " block" + (r.BlocksFreed == 1 ? "" : "s") +
                          " freed, " + f.Disk.FreeBlocks + " now free.  Unsaved changes.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Could not delete the sample",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ------------------------------------------------------ programs

        /// <summary>
        /// Creates a program holding one empty keygroup across the whole keyboard, ready
        /// to have zones pointed at samples.
        /// </summary>
        void CreateProgram(AkaiDisk d)
        {
            if (d == null)
            {
                MessageBox.Show(this, "Select a disk first - a new program has to go somewhere.",
                                "New program", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dlg = new NewProgramDialog(d))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    PushUndo(d, "new program");
                    var p = d.AddProgram(dlg.ChosenName, new AkaiDisk.NewProgram
                    {
                        ProgramNumber = dlg.ProgramNumber
                    });

                    _editDisk = d;
                    RebuildTree();
                    if (p != null) SelectFileNode(d, p);
                    UpdateCommands();

                    SetStatus("Created program " + dlg.ChosenName + "  -  one keygroup, MIDI program " +
                              (dlg.ProgramNumber + 1) + ", " + d.FreeBlocks +
                              " blocks free.  Point its zones at a sample to hear it.  Unsaved changes.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Could not create the program",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        /// <summary>
        /// Deletes a program. Nothing on the disk refers to a program, so this is the one
        /// structural delete with no references to chase - only the samples it played are
        /// left behind, untouched.
        /// </summary>
        void DeleteProgramFile(FileRef f)
        {
            var e = f.Entry;
            int keygroups = AkaiDisk.KeygroupCount(e);
            var samples = f.Disk.ReferencedSamples(e);

            var ask = MessageBox.Show(this,
                "Delete the program " + e.Name.Trim() + "?" + Environment.NewLine + Environment.NewLine +
                "Keygroups " + keygroups + Environment.NewLine +
                "Frees     " + AkaiDisk.BlocksFor(e.Length) + " blocks" +
                Environment.NewLine + Environment.NewLine +
                (samples.Count == 0
                    ? "It names no samples."
                    : "The " + samples.Count + " sample" + (samples.Count == 1 ? "" : "s") +
                      " it plays stay on the disk.") +
                Environment.NewLine + Environment.NewLine +
                "Go ahead?",
                "Delete program", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (ask != DialogResult.OK) return;

            try
            {
                _audio.Stop();
                PushUndo(f.Disk, "delete " + e.Name.Trim());

                string gone = e.Name.Trim();
                int freed = f.Disk.DeleteProgram(e);

                _editDisk = f.Disk;
                RebuildTree();
                SelectDiskNode(f.Disk);
                UpdateCommands();

                SetStatus("Deleted the program " + gone + "  -  " + freed + " block" +
                          (freed == 1 ? "" : "s") + " freed, " + f.Disk.FreeBlocks +
                          " now free.  Unsaved changes.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Could not delete the program",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ------------------------------------------------------------- slicing

        /// <summary>
        /// Cuts a sample into one-shots on its detected onsets, optionally mapping them to
        /// consecutive keys of a program. The dialog costs the whole plan before anything
        /// is written, because this is the operation that can exhaust three limits at once.
        /// </summary>
        void SliceSampleFile(FileRef f)
        {
            var e = f.Entry;
            short[] words;

            try { words = f.Disk.SampleWords12(e); }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Could not read the sample",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (words.Length < 16)
            {
                MessageBox.Show(this, e.Name.Trim() + " is too short to cut up.",
                                "Slice", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dlg = new SliceDialog(f.Disk, e, words))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    Cursor = Cursors.WaitCursor;
                    _audio.Stop();
                    PushUndo(f.Disk, "slice " + e.Name.Trim());

                    AkaiDisk.SliceResult r;
                    try { r = f.Disk.SliceSample(e, dlg.Cuts, dlg.Options); }
                    finally { Cursor = Cursors.Default; }

                    _editDisk = f.Disk;
                    RebuildTree();
                    SelectDiskNode(f.Disk);
                    UpdateCommands();

                    SetStatus("Sliced " + e.Name.Trim() + " into " + r.Added.Count + " samples" +
                              (r.Keygroups > 0
                                  ? ", mapped to " + r.Keygroups + " keygroups from " + NoteName(r.RootKey)
                                  : "") +
                              "  -  " + f.Disk.FreeBlocks + " blocks and " + f.Disk.FreeSlots() +
                              " slots left.  Unsaved changes.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Could not slice the sample",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // ------------------------------------------------- copying between disks

        /// <summary>
        /// The Copy to submenu: every other disk that is open, named as the tree names it.
        ///
        /// Disabled with a reason rather than hidden when there is nowhere to copy to.
        /// A menu item that is simply absent leaves you wondering whether the program can
        /// do it at all.
        /// </summary>
        ToolStripMenuItem BuildCopyToMenu(FileRef f)
        {
            var item = new ToolStripMenuItem("&Copy to");

            var others = _disks.Where(d => !ReferenceEquals(d, f.Disk)).ToList();
            if (others.Count == 0)
            {
                item.Enabled = false;
                item.ToolTipText = "Open another disk image to copy this onto.";
                return item;
            }

            var clashes = DiskNameClashes();

            foreach (var d in others.OrderBy(x => Path.GetFileNameWithoutExtension(x.Source),
                                             StringComparer.OrdinalIgnoreCase))
            {
                var target = d;                  // captured per item, not per loop
                item.DropDownItems.Add(new ToolStripMenuItem(
                    DiskLabel(target, clashes), null, (s, a) => CopyFileTo(f, target)));
            }

            return item;
        }

        /// <summary>
        /// Copy a sample or a program onto another open disk.
        ///
        /// The plan is worked out and shown before anything is written, because a copy can
        /// quietly be bigger than it looks: a program brings every sample its zones name,
        /// and one of those can already be there under the same name holding something
        /// else. Nothing on the target is ever replaced - a clash is renamed - so the worst
        /// case is a file you did not want rather than one you cannot get back.
        /// </summary>
        void CopyFileTo(FileRef f, AkaiDisk target)
        {
            if (f == null || target == null) return;

            AkaiDisk.CopyPlan plan;
            try { plan = target.PlanCopy(f.Disk, f.Entry); }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Could not work out the copy",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string where = DiskLabel(target);

            if (!plan.Ok)
            {
                MessageBox.Show(this,
                    string.Join(Environment.NewLine, plan.Problems.ToArray()),
                    "Will not fit on " + where, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (plan.Writes.Count == 0)
            {
                // Everything it would bring is already there, byte for byte.
                SetStatus(f.Entry.Name.Trim() + " is already on " + where +
                          ", with everything it needs.  Nothing copied.");
                return;
            }

            if (MessageBox.Show(this, DescribeCopy(plan, target, where), "Copy to " + where,
                                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            try
            {
                PushUndo(target, "copy " + f.Entry.Name.Trim() + " to " + where);

                var landed = target.ApplyCopy(plan);

                _editDisk = target;
                RebuildTree();

                // Land on what arrived - the program if one came, else the sample.
                var show = landed.FirstOrDefault(e => e.Type == f.Entry.Type) ?? landed.LastOrDefault();
                if (show != null) SelectFileNode(target, show);

                UpdateCommands();

                int samples = plan.SampleWrites;
                string msg = "Copied " + f.Entry.Name.Trim() + " to " + where;

                if (f.Entry.Type == 'P' && samples > 0)
                    msg += " with " + samples + " sample" + (samples == 1 ? "" : "s");

                int reused = plan.Items.Count(i => i.AlreadyHere);
                if (reused > 0) msg += "  -  " + reused + " already there";

                int renamed = plan.Items.Count(i => i.Renamed);
                if (renamed > 0) msg += "  -  " + renamed + " renamed to avoid a clash";

                SetStatus(msg + ".  Unsaved changes.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Could not copy",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                RebuildTree();
                UpdateCommands();
            }
        }

        // ------------------------------------------------- copying a keygroup

        /// <summary>
        /// Copy one keygroup onto another program, on this disk or another open one.
        ///
        /// Unlike a file copy this can end on the disk it started on - moving a keygroup
        /// between two programs of the same disk is an ordinary thing to want - so the
        /// chooser lists every program that is open except the one being copied from.
        /// Copying a keygroup onto its own program is what Add Keygroup already does.
        /// </summary>
        void CopyKeygroupTo(FileRef f, int row)
        {
            if (f == null || f.Entry.Type != 'P' || row < 0) return;

            var clashes = DiskNameClashes();
            var choices = new List<ProgramChoice>();

            foreach (var d in _disks)
                foreach (var p in d.Entries.Where(e => e.Type == 'P')
                                  .OrderBy(e => e.Name.Trim(), StringComparer.OrdinalIgnoreCase))
                {
                    if (ReferenceEquals(d, f.Disk) && p.Slot == f.Entry.Slot) continue;

                    choices.Add(new ProgramChoice
                    {
                        Disk = d,
                        Program = p,
                        Label = DiskLabel(d, clashes) + "      " + p.Name.Trim() +
                                "   (" + AkaiDisk.KeygroupCount(p) + " keygroups)"
                    });
                }

            if (choices.Count == 0)
            {
                MessageBox.Show(this,
                    "There is no other program to copy it into. Create one, or open another disk image.",
                    "Nowhere to copy to", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dlg = new ChooseProgramDialog(
                       "Copy keygroup " + (row + 1) + " of " + f.Entry.Name.Trim() + " into:", choices))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Chosen == null) return;

                var target = dlg.Chosen.Disk;
                int targetSlot = dlg.Chosen.Program.Slot;
                string into = dlg.Chosen.Program.Name.Trim();
                string where = DiskLabel(target, clashes);

                AkaiDisk.KeygroupPlan plan;
                try { plan = target.PlanCopyKeygroup(f.Disk, f.Entry, row, dlg.Chosen.Program); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Could not work out the copy",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (!plan.Ok)
                {
                    MessageBox.Show(this, string.Join(Environment.NewLine, plan.Problems.ToArray()),
                                    "Cannot copy it into " + into,
                                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                /*
                 * Only worth a question when something beyond the keygroup is written. A
                 * keygroup landing on a program whose disk already holds its samples costs
                 * one record and is undoable, and stopping to confirm that is friction.
                 */
                if (plan.Writes.Count > 0 || plan.Notes.Count > 0)
                {
                    if (MessageBox.Show(this, DescribeKeygroupCopy(plan, target, into, where),
                                        "Copy keygroup into " + into,
                                        MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                        return;
                }

                try
                {
                    PushUndo(target, "copy keygroup into " + into);

                    int number = target.ApplyCopyKeygroup(plan);

                    _editDisk = target;
                    RebuildTree();

                    var fresh = target.EntryInSlot(targetSlot);
                    if (fresh != null) SelectFileNode(target, fresh);

                    UpdateCommands();

                    string msg = "Copied keygroup " + (row + 1) + " of " + f.Entry.Name.Trim() +
                                 " into " + into + " on " + where + " as keygroup " + number;

                    int brought = plan.Writes.Count;
                    if (brought > 0)
                        msg += "  -  " + brought + " sample" + (brought == 1 ? "" : "s") + " came with it";

                    int renamed = plan.Samples.Count(i => i.Renamed);
                    if (renamed > 0) msg += ", " + renamed + " renamed to avoid a clash";

                    SetStatus(msg + ".  Unsaved changes.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Could not copy the keygroup",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                    RebuildTree();
                    UpdateCommands();
                }
            }
        }

        /// <summary>What the confirmation says when a keygroup brings samples with it.</summary>
        static string DescribeKeygroupCopy(AkaiDisk.KeygroupPlan plan, AkaiDisk target,
                                           string into, string where)
        {
            var sb = new System.Text.StringBuilder();

            sb.AppendLine("The keygroup names samples that are not on " + where + ".");
            sb.AppendLine("They come with it, or it arrives silent:");
            sb.AppendLine();

            foreach (var i in plan.Samples)
            {
                if (i.AlreadyHere)
                    sb.AppendLine("   sample  " + i.From + "  -  already there, left alone");
                else if (i.Renamed)
                    sb.AppendLine("   sample  " + i.From + "  ->  " + i.To +
                                  "   (that name is taken by something else)");
                else
                    sb.AppendLine("   sample  " + i.To);
            }

            sb.AppendLine();
            sb.AppendLine("Takes " + plan.Blocks + " of the " + target.FreeBlocks +
                          " block(s) free on " + where + ", and one keygroup of " + into + ".");

            foreach (string note in plan.Notes)
            {
                sb.AppendLine();
                sb.AppendLine(note + ".");
            }

            return sb.ToString();
        }

        /// <summary>What the confirmation box says: every file, and what it will cost.</summary>
        static string DescribeCopy(AkaiDisk.CopyPlan plan, AkaiDisk target, string where)
        {
            var sb = new System.Text.StringBuilder();

            sb.AppendLine("Copy to " + where + ":");
            sb.AppendLine();

            foreach (var i in plan.Items)
            {
                string what = i.Type == 'P' ? "program" : "sample";

                if (i.AlreadyHere)
                    sb.AppendLine("   " + what + "  " + i.From + "  -  already there, left alone");
                else if (i.Renamed)
                    sb.AppendLine("   " + what + "  " + i.From + "  ->  " + i.To +
                                  "   (that name is taken by something else)");
                else
                    sb.AppendLine("   " + what + "  " + i.To);
            }

            sb.AppendLine();
            sb.AppendLine("Takes " + plan.Blocks + " of the " + target.FreeBlocks +
                          " block(s) free on " + where + ".");

            foreach (string note in plan.Notes)
            {
                sb.AppendLine();
                sb.AppendLine(note + ".");
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Asks for the name and MIDI program number of a new program. The number defaults to
    /// the lowest the disk is not already using, so two programs do not answer to one
    /// program change by accident.
    /// </summary>
    /// <summary>One program, wherever it lives, as the chooser lists it.</summary>
    internal sealed class ProgramChoice
    {
        public AkaiDisk Disk;
        public AkaiEntry Program;
        public string Label;

        public override string ToString() { return Label; }
    }

    /// <summary>
    /// Pick a program out of everything that is open.
    ///
    /// A flat list rather than a tree: the disk is already in every line, and a keygroup
    /// copy is a single choice rather than a place to go browsing. Double-clicking takes
    /// it, which is what a list of things to choose from invites.
    /// </summary>
    internal sealed class ChooseProgramDialog : Form
    {
        readonly ListBox _list = new ListBox();
        readonly Button _ok = new Button();

        public ProgramChoice Chosen { get { return _list.SelectedItem as ProgramChoice; } }

        public ChooseProgramDialog(string prompt, IList<ProgramChoice> choices)
        {
            Text = "Copy keygroup";
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(460, 320);
            MinimumSize = new Size(360, 260);
            Font = SystemFonts.MessageBoxFont;

            var label = new Label
            {
                Bounds = new Rectangle(12, 12, 436, 20),
                Text = prompt,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            Controls.Add(label);

            _list.SetBounds(12, 38, 436, 232);
            _list.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _list.IntegralHeight = false;
            foreach (var c in choices) _list.Items.Add(c);
            _list.SelectedIndexChanged += (s, e) => _ok.Enabled = _list.SelectedItem != null;
            _list.DoubleClick += (s, e) =>
            {
                if (_list.SelectedItem == null) return;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(_list);

            _ok.Text = "Copy";
            _ok.DialogResult = DialogResult.OK;
            _ok.SetBounds(276, 282, 84, 26);
            _ok.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _ok.Enabled = false;
            Controls.Add(_ok);

            var cancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Bounds = new Rectangle(364, 282, 84, 26),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            Controls.Add(cancel);

            AcceptButton = _ok;
            CancelButton = cancel;

            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        }
    }

    internal sealed class NewProgramDialog : Form
    {
        readonly TextBox _name = new TextBox();
        readonly NumericUpDown _number = new NumericUpDown();
        readonly Label _note = new Label();
        readonly Button _ok = new Button();
        readonly AkaiDisk _disk;

        public string ChosenName { get { return AkaiDisk.NormaliseName(_name.Text); } }
        public int ProgramNumber { get { return (int)_number.Value - 1; } }

        public NewProgramDialog(AkaiDisk disk)
        {
            _disk = disk;

            Text = "New program";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(400, 188);
            Font = SystemFonts.MessageBoxFont;

            Controls.Add(new Label
            {
                Bounds = new Rectangle(12, 14, 376, 20),
                Text = "Name the new program:"
            });

            _name.SetBounds(12, 38, 200, 24);
            _name.MaxLength = 10;
            _name.CharacterCasing = CharacterCasing.Upper;
            _name.Text = "NEW PROG";
            _name.SelectAll();
            _name.TextChanged += (s, e) => Validate2();
            Controls.Add(_name);

            Controls.Add(new Label
            {
                Bounds = new Rectangle(218, 41, 170, 20),
                Text = "10 characters",
                ForeColor = SystemColors.GrayText
            });

            Controls.Add(new Label
            {
                Bounds = new Rectangle(12, 76, 130, 20),
                Text = "MIDI program"
            });
            _number.SetBounds(146, 74, 66, 24);
            _number.Minimum = 1;
            _number.Maximum = 128;
            _number.Value = disk.FreeProgramNumber() + 1;
            Controls.Add(_number);

            _note.SetBounds(12, 108, 376, 34);
            Controls.Add(_note);

            _ok.SetBounds(192, 150, 96, 26);
            _ok.Text = "Create";
            _ok.DialogResult = DialogResult.OK;
            Controls.Add(_ok);

            var cancel = new Button
            {
                Bounds = new Rectangle(296, 150, 92, 26),
                Text = "Cancel",
                DialogResult = DialogResult.Cancel
            };
            Controls.Add(cancel);

            AcceptButton = _ok;
            CancelButton = cancel;
            Validate2();
        }

        /// <summary>Named to stay clear of Form.Validate.</summary>
        void Validate2()
        {
            string want = ChosenName;
            bool blank = _name.Text.Trim().Length == 0;

            bool taken = _disk.Entries.Any(
                x => x.Type == 'P' && string.Equals(x.Name, want, StringComparison.OrdinalIgnoreCase));

            if (blank) _note.Text = "Give it a name.";
            else if (taken) _note.Text = "'" + want + "' is already used by another program.";
            else if (_disk.FreeSlots() < 1) _note.Text = "The directory is full: 64 files is the limit.";
            else if (_disk.FreeBlocks < 1) _note.Text = "There is no free block for it.";
            else _note.Text = "It arrives with one empty keygroup across the whole keyboard.";

            bool bad = blank || taken || _disk.FreeSlots() < 1 || _disk.FreeBlocks < 1;
            _note.ForeColor = bad ? Color.FromArgb(168, 32, 32) : SystemColors.GrayText;
            _ok.Enabled = !bad;
        }
    }
}
