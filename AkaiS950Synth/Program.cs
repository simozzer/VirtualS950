using System;
using System.Collections.Generic;
using System.IO;
using AkaiS950List;

namespace AkaiS950Synth
{
    /// <summary>
    /// Build S950 disks full of synthesiser sounds, from nothing.
    ///
    ///     AkaiS950Synth [folder] [--img] [--list]
    ///
    /// No samples are recorded and none are read: every waveform is worked out from its
    /// harmonics, band-limited to what the sample rate can hold, and written straight into
    /// a disk image along with programmes that set the machine's filter, envelopes and
    /// vibrato around it.
    ///
    /// The result loads on a real S950, in AkaiS950Studio, and in the plugin - the same
    /// disks in all three, because all three read the same format and share the same
    /// measured engine.
    /// </summary>
    internal static class Program
    {
        /// <summary>A disk holds 64 directory entries, and a sample and a programme each take one.</summary>
        const int DirectoryLimit = 64;

        static int Main(string[] args)
        {
            string folder = null;
            bool listOnly = false, raw = false;

            foreach (var a in args)
            {
                if (a == "--list") listOnly = true;
                else if (a == "--img") raw = true;
                else if (!a.StartsWith("--")) folder = a;
            }

            if (listOnly) { List(); return 0; }
            if (folder == null) folder = ".";

            try
            {
                Directory.CreateDirectory(folder);

                foreach (var bank in Patches.Banks())
                    BuildBank(bank, folder, raw ? "img" : "hfe");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("failed: " + ex.Message);
                return 1;
            }

            return 0;
        }

        // --------------------------------------------------------------------- listing

        static void List()
        {
            foreach (var bank in Patches.Banks())
            {
                var waves = WavesFor(bank);

                Console.WriteLine();
                Console.WriteLine("  {0}   {1} programmes + {2} samples = {3} of {4} files",
                                  bank.Name, bank.Programmes.Count, waves.Count,
                                  bank.Programmes.Count + waves.Count, DirectoryLimit);

                foreach (var p in bank.Programmes)
                {
                    var layers = new List<string>();
                    foreach (var l in p.Layers)
                        layers.Add(l.Sample
                                   + (l.Loudness != 0 ? " " + l.Loudness.ToString("+0;-0") : "")
                                   + Range(l));

                    Console.WriteLine("    {0,-10} {1}", p.Name,
                                      string.Join(" + ", layers.ToArray()));
                }
            }

            Console.WriteLine();
        }

        /// <summary>The part of the keyboard a layer answers to, when it is not all of it.</summary>
        static string Range(Layer l)
        {
            if (l.LowKey == null && l.HighKey == null) return "";
            return " [" + (l.LowKey ?? 0) + ".." + (l.HighKey ?? 127) + "]";
        }

        /// <summary>
        /// The waves a bank needs, worked out from the layers that name them.
        ///
        /// Derived rather than listed, so a disk can never be missing a sample one of its
        /// programmes refers to - which on this machine is not a warning but a keygroup
        /// that silently plays nothing - or be carrying one that nothing plays. It is also
        /// what makes the generated programmes free: twenty of them over five waves ask
        /// the disk for five samples.
        /// </summary>
        static List<Patches.Wave> WavesFor(Patches.Bank bank)
        {
            var wanted = new List<string>();

            foreach (var p in bank.Programmes)
                foreach (var l in p.Layers)
                    if (!wanted.Contains(l.Sample)) wanted.Add(l.Sample);

            var outp = new List<Patches.Wave>();
            foreach (var w in Patches.Waves())
                if (wanted.Contains(w.Name)) outp.Add(w);

            // A name that matches no wave is a typo, and finding out here beats finding out
            // from a keygroup that plays nothing.
            foreach (var name in wanted)
            {
                bool found = false;
                foreach (var w in outp) if (w.Name == name) { found = true; break; }
                if (!found) throw new InvalidOperationException("no wave called '" + name + "'");
            }

            return outp;
        }

        // -------------------------------------------------------------------- building

        static void BuildBank(Patches.Bank bank, string folder, string format)
        {
            var waves = WavesFor(bank);

            int files = waves.Count + bank.Programmes.Count;
            if (files > DirectoryLimit)
                throw new InvalidOperationException(
                    bank.Name + " wants " + files + " files and a disk holds " + DirectoryLimit);

            //
            // An S950 disk is empty when its directory and allocation table are zero, so an
            // image of nothing but zeroes is already a formatted blank.
            //
            var disk = AkaiDisk.LoadFromBytes(bank.Name, new byte[800 * 1024]);

            Console.WriteLine();
            Console.WriteLine("  {0}", bank.Name);

            foreach (var w in waves)
            {
                short[] words;

                if (w.IsNoise)
                {
                    words = Waveforms.Hiss(2000, 12345);
                }
                else
                {
                    int highest = Waveforms.HighestHarmonic(w.Rate, Patches.RootHz);
                    words = Waveforms.Render(w.Table(highest), w.Cycles, w.Rate, Patches.RootHz, w.Shake);
                }

                //
                // Looped, and the loop is the whole sample. AddSample defaults the length to
                // 2000 words, which is right only for the short ones, so it is written out
                // explicitly - a loop over part of a wavetable would sweep through half of
                // it and jump back.
                //
                var e = disk.AddSample(w.Name, words, w.Rate, Patches.RootNote, 0, 'L');
                SetLoopLength(disk, e, words.Length);

                Console.WriteLine("    wave  {0,-10} {1,6} words  {2,6} Hz", w.Name, words.Length, w.Rate);
            }

            foreach (var p in bank.Programmes)
            {
                var prog = disk.AddProgram(p.Name, null);

                // AddProgram starts a programme with one keygroup; a layer each after that.
                for (int i = 1; i < p.Layers.Length; i++) disk.AddKeygroup(prog, 0);

                //
                // And count them, because writing to a keygroup that is not there is not an
                // error - SetKeygroupByte simply has nowhere to put it. A three-layer patch
                // that came out with two layers looked entirely healthy from every other
                // angle for as long as nobody counted.
                //
                int have = AkaiDisk.KeygroupCount(prog);
                if (have != p.Layers.Length)
                    throw new InvalidOperationException(
                        p.Name + " wanted " + p.Layers.Length + " keygroups and has " + have);

                for (int i = 0; i < p.Layers.Length; i++)
                    WriteKeygroup(disk, prog, i, p, p.Layers[i]);

                Console.WriteLine("    prog  {0,-10} {1} layer(s)", p.Name, p.Layers.Length);
            }

            //
            // Zone pointers are positions in the keygroup arena, derived from the directory
            // rather than stored independently, so they are recomputed once everything is
            // in place. Getting this wrong is what crashes a real S950.
            //
            int repaired = disk.RepairKeygroupChains();

            string path = Path.Combine(folder, bank.Name + "." + format);
            disk.SaveAs(path, format);

            int sounding = Verify(path, format);

            Console.WriteLine("    -> {0}   {1} files, {2} blocks, {3} keygroups sound{4}",
                              path, files, disk.UsedBlocks, sounding,
                              repaired > 0 ? ", " + repaired + " pointer(s) fixed" : "");
        }

        /// <summary>
        /// Read the disk back and check that every keygroup will actually play something.
        ///
        /// THIS EXISTS BECAUSE IT DID NOT, ONCE
        ///
        /// SetZoneSample counts zones from zero. Told to write zone "1", it put every
        /// sample name into the SECOND zone - which a velocity switch of 128 makes
        /// unreachable - and left the first holding the template's placeholder. Five disks
        /// were written, both readers parsed them identically, every dump looked plausible,
        /// and not one programme could make a sound: each built a patch with no keygroups
        /// in it, which a host quietly declines, so every programme played whatever had
        /// been playing before.
        ///
        /// Nothing upstream could have caught that. The bytes were valid, the structure was
        /// right, the readers agreed. The only question that would have found it is the one
        /// asked here: given this disk, does this programme have a zone naming a sample that
        /// is on it?
        /// </summary>
        static int Verify(string path, string format)
        {
            var disk = AkaiDisk.Load(path);

            var samples = new List<string>();
            foreach (var e in disk.Entries)
                if (e.Type == 'S') samples.Add(e.Name.Trim());

            int sounding = 0;

            foreach (var e in disk.Entries)
            {
                if (e.Type != 'P') continue;

                var groups = disk.Keygroups(e);
                if (groups.Count == 0)
                    throw new InvalidOperationException(e.Name.Trim() + " has no keygroups");

                foreach (var kg in groups)
                {
                    var zone = kg.Zone1;

                    if (zone == null || !zone.InUse)
                        throw new InvalidOperationException(
                            e.Name.Trim() + " keygroup " + (kg.Index + 1) +
                            " has nothing in zone 1 - it would be silent");

                    if (!samples.Contains(zone.Name.Trim()))
                        throw new InvalidOperationException(
                            e.Name.Trim() + " keygroup " + (kg.Index + 1) +
                            " names '" + zone.Name.Trim() + "', which is not on this disk");

                    sounding++;
                }
            }

            return sounding;
        }

        /// <summary>
        /// Loop the whole sample.
        ///
        /// The machine plays end-length .. end round and round, so a loop length equal to
        /// the sample makes the loop the entire waveform - which for a wave built from a
        /// whole number of cycles joins without a click by construction.
        /// </summary>
        static void SetLoopLength(AkaiDisk disk, AkaiEntry e, int words)
        {
            for (int i = 0; i < 4; i++)
                disk.PokeFile(e, 0x24 + i, (byte)((words >> (8 * i)) & 0xFF));
        }

        // ------------------------------------------------------------------- keygroups

        static void WriteKeygroup(AkaiDisk disk, AkaiEntry prog, int index, Patch p, Layer layer)
        {
            //
            // Which keys this layer answers to. The whole keyboard unless the layer says
            // otherwise - most of these are synthesiser patches rather than multisamples,
            // and a stack wants every layer under every key.
            //
            // Where a layer does say otherwise it is a split, and it needs nothing else:
            // the keygroup has been a key range all along.
            //
            Set(disk, prog, index, 1, layer.LowKey ?? 0);          // low key
            Set(disk, prog, index, 0, layer.HighKey ?? 127);       // high key

            // 128 is how the panel says there is no second zone - no velocity can reach it.
            Set(disk, prog, index, 2, 128);

            //
            // Each layer takes the patch's settings unless it says otherwise. That is what
            // lets one keygroup be struck and gone while the next is still arriving, and
            // what lets the top of a stack stay bright while its bottom stays round - the
            // envelope, the filter, the vibrato and the velocity response all belong to the
            // keygroup on this machine, not to the programme.
            //
            Set(disk, prog, index, 3, layer.A ?? p.A);
            Set(disk, prog, index, 4, layer.D ?? p.D);
            Set(disk, prog, index, 5, layer.S ?? p.S);
            Set(disk, prog, index, 6, layer.R ?? p.R);

            Set(disk, prog, index, 7, layer.VelToFilter ?? p.VelToFilter);
            Set(disk, prog, index, 8, layer.KeyToFilter ?? p.KeyToFilter);
            Set(disk, prog, index, 11, layer.VelToLoudness ?? p.VelToLoudness);

            Set(disk, prog, index, 15, layer.LfoDelay ?? p.LfoDelay);
            Set(disk, prog, index, 16, layer.LfoRate ?? p.LfoRate);
            Set(disk, prog, index, 17, layer.LfoDepth ?? p.LfoDepth);
            Set(disk, prog, index, 22, p.LfoModwheel);

            //
            // Bit 0 constant pitch, bit 2 LFO desync, bit 3 one-shot.
            //
            // Desync is set because these are synthesiser voices: each note wants its own
            // vibrato rather than all of them wobbling in lockstep, which is what the bit
            // was measured to do.
            //
            int flags = 0x04;
            if (p.OneShot) flags |= 0x08;
            Set(disk, prog, index, 18, flags);

            Set(disk, prog, index, 23, Signed(layer.VcfAmount ?? p.VcfAmount));
            Set(disk, prog, index, 34, layer.VcfA ?? p.VcfA);
            Set(disk, prog, index, 35, layer.VcfD ?? p.VcfD);
            Set(disk, prog, index, 36, layer.VcfS ?? p.VcfS);
            Set(disk, prog, index, 37, layer.VcfR ?? p.VcfR);

            //
            // Zone 1: the sample, then its trim. 24 is the name, 42..45 the rest.
            //
            // SetZoneSample counts zones from ZERO - it multiplies the argument by the zone
            // stride - so zone 1 is 0 here. Passing 1 puts the name 22 bytes further along
            // in zone 2, which leaves zone 1 holding the template's "2 SAMPLE" placeholder
            // and the trim bytes written below, and zone 2 holding a name that a velocity
            // switch of 128 means nothing can ever reach. The programme then has no
            // playable keygroup at all, the host declines to load an empty patch, and every
            // programme sounds like whatever was playing before it.
            //
            disk.SetZoneSample(prog, index, 0, layer.Sample);
            Set(disk, prog, index, 42, layer.Fine < 0 ? 256 + layer.Fine : layer.Fine);
            Set(disk, prog, index, 43, Signed(layer.Transpose));
            Set(disk, prog, index, 44, layer.Filter ?? p.Filter);
            Set(disk, prog, index, 45, Signed(layer.Loudness));
        }

        static void Set(AkaiDisk disk, AkaiEntry prog, int index, int offset, int value)
        {
            if (value < 0) value = 0;
            if (value > 255) value = 255;
            disk.SetKeygroupByte(prog, index, offset, (byte)value);
        }

        /// <summary>A signed byte as the machine stores it.</summary>
        static int Signed(int v)
        {
            if (v < -128) v = -128;
            if (v > 127) v = 127;
            return v < 0 ? 256 + v : v;
        }
    }
}
