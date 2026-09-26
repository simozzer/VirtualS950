using System;
using System.Collections.Generic;

namespace AkaiS950Engine
{
    /// <summary>
    /// One sample's audio, ready to be played.
    ///
    /// Normalised to +-1 once, when the patch is built, rather than per sample in the render
    /// loop. The engine never sees a disk image: whoever builds the patch does the reading,
    /// which is what lets the same engine sit behind a WinForms window now and a plugin
    /// later without either of them knowing about the other.
    /// </summary>
    public sealed class Sound
    {
        public string Name;
        public float[] Audio;            // -1 .. +1
        public int SourceRate;           // hertz, as stored on the disk
        public double RootPitch = 60;    // the MIDI note at which it plays at SourceRate

        /// <summary>
        /// The loop, in frames. The machine plays end-length .. end round and round, so the
        /// start follows from the length rather than from the stored start - which is simply
        /// zero in 250 of the library's 324 looped samples.
        /// </summary>
        public int LoopFrom, LoopTo;
        public bool Loops;

        /// <summary>
        /// Loop mode 'A': the loop runs forward, then backward, then forward again.
        ///
        /// MEASURED, run 13. A loop of N frames comes round every 2N, not 2N-2 - the frame at
        /// each end is played twice as the direction turns rather than once. Four loop lengths
        /// from 20 frames to 128 were played on the hardware and every one autocorrelated at
        /// exactly 2N with a coefficient of 0.9996 or better, while the forward controls gave
        /// exactly N. That is the difference between a tenth of a semitone right and wrong on
        /// a short loop, which is why it was measured rather than chosen.
        ///
        /// 18 of the 1110 samples on the real disks use it, and they are the ones where it
        /// shows: cymbals and crashes, sustained strings and piano, and ambient beds - WIND,
        /// RAIN, THUNDER, WATER, INSECTS, TRAFFIC, JET. Alternating is there to hide the seam
        /// on a long texture, so playing one forward-only puts back the click it was chosen
        /// to avoid.
        /// </summary>
        public bool Alternates;

        /// <summary>How many samples of the loop join were crossfaded, or 0 for a plain splice.</summary>
        public int LoopSmoothed;
    }

    /// <summary>
    /// One keygroup, flattened into what a voice needs.
    ///
    /// The raw 0..99 panel bytes are kept rather than seconds and hertz, because the
    /// mappings from one to the other are the measured part and belong in one place.
    /// </summary>
    public sealed class KeygroupPatch
    {
        public int LowKey, HighKey;

        /*
         * Which velocities this entry answers to.
         *
         * A keygroup holds up to two VELOCITY zones - alternatives, not layers. The
         * switch in byte 2 is the boundary: softer than it plays zone 1, at it or harder
         * plays zone 2, and a switch of 128 is how the panel says "no second zone",
         * since no velocity can reach it.
         *
         * Sounding both was this engine's worst bug. 74 of the library's 168 two-zone
         * keygroups name the SAME sample in both, so playing them together put two
         * copies of one sample on top of each other - and 121 of the 168 set the switch
         * to 128, so their second zone should never have sounded at all. Together they
         * rang like a bell over every note.
         */
        public int VelocityFrom, VelocityTo = 127;

        /// <summary>Which keygroup this came from. Only the checks care, and they care a lot:
        /// two entries from the SAME keygroup must never answer one strike.</summary>
        public int KeygroupIndex = -1;

        public Sound Sound;

        public int VcaAttack, VcaDecay, VcaSustain = 99, VcaRelease;
        public int VcfAttack, VcfDecay, VcfSustain = 99, VcfRelease;
        public bool VcfWritten = true;   // an S900 program leaves the four bytes as spaces
        public int VcfAmount;            // signed, -50..+50

        public int VelToFilter, KeyToFilter, VelToLoudness;

        /// <summary>Byte 9: how far a hard strike shortens the attack. See Cal.VelocityAttackByte.</summary>
        public int VelToAttack;

        /// <summary>Byte 10, signed: which way velocity moves the release. See Cal.VelocityReleaseByte.</summary>
        public int VelToRelease;

        /// <summary>Byte 18 bit 4: the velocity page's ON/OFF. Clear means every note releases
        /// as though struck at velocity 1 - which is not the same as no effect at all.</summary>
        public bool VelocityReleaseOn;

        /// <summary>WARP, bytes 12/13/14: a pitch bend at note-on. See Cal.WarpRatio.
        /// 13 is the depth and is signed; 12 is how far velocity scales it, with 0 meaning
        /// always full; 14 is the time constant.</summary>
        public int WarpVelocity, WarpDepth, WarpTime;

        /// <summary>Byte 19 + 1: 0 ALL, 1..8 the individual outputs, 9 LEFT, 10 RIGHT.
        /// See Cal.OutputGains.</summary>
        public int OutputPort;

        public int LfoDelay, LfoRate, LfoDepth, LfoModwheelDepth;
        public bool LfoDesync = true;    // set in 1652 keygroups of 1908

        public int ZoneFilter = 99;      // zone 1's cutoff, 0..99
        public int ZoneLoudness;         // signed trim, in the machine's decibel count
        public double ZoneTranspose;     // semitones, including the fine part

        public bool ConstantPitch;
        public bool OneShot;

        /// <summary>
        /// The four VCF bytes read as ASCII spaces means the program was written by an S900,
        /// which had no filter envelope at all - not an envelope that happens to be set to
        /// 32. In the library this never bites, because none of the 1684 keygroups with
        /// blank bytes sets an amount, but it would the moment one did.
        /// </summary>
        public static bool LooksWritten(int a, int d, int s, int r)
        {
            return !(a == 0x20 && d == 0x20 && s == 0x20 && r == 0x20);
        }
    }

    /// <summary>A program: the keygroups a note might land in.</summary>
    public sealed class Patch
    {
        public string Name = "";
        public readonly List<KeygroupPatch> Keygroups = new List<KeygroupPatch>();

        /// <summary>
        /// Every entry this note and velocity should sound.
        ///
        /// Plural on purpose, but for one reason only: overlapping KEYGROUPS do layer, and
        /// the machine sounds all of them. The two zones INSIDE a keygroup do not - they
        /// are velocity alternatives, and only one of them answers any given strike.
        /// </summary>
        public void Matching(int note, int velocity, List<KeygroupPatch> into)
        {
            into.Clear();
            for (int i = 0; i < Keygroups.Count; i++)
            {
                KeygroupPatch k = Keygroups[i];
                if (k.Sound == null || k.Sound.Audio == null || k.Sound.Audio.Length == 0) continue;

                int lo = Math.Min(k.LowKey, k.HighKey), hi = Math.Max(k.LowKey, k.HighKey);
                if (note < lo || note > hi) continue;
                if (velocity < k.VelocityFrom || velocity > k.VelocityTo) continue;

                into.Add(k);
            }
        }
    }
}
