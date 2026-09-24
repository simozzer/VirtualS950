using System;

namespace AkaiS950Engine
{
    /// <summary>
    /// What the machine was measured to do.
    ///
    /// Every number here came off a recording of a real S950 playing a run written for the
    /// purpose, and each one is also written down in the web version's audio.js. The two
    /// implementations are meant to agree to the digit: EngineCheck compares them, in the
    /// same way LoopCheck already compares the loop finder against looptest.js.
    ///
    /// Where a value is an assumption rather than a measurement it says so. Those are worth
    /// knowing about, because they are what another afternoon with a recorder would settle.
    /// </summary>
    public static class Cal
    {
        // ---------------------------------------------------------------- the filter

        /// <summary>Measured: 16311 Hz at 44100, and 16232 on an earlier take.</summary>
        public const double MaxRatio = 0.37;

        /// <summary>Measured: the cutoff will not close below this.</summary>
        public const double FloorHz = 311.0;

        /// <summary>
        /// Measured points, not a formula. The curve steepens as it climbs, so a straight
        /// line in log space is the wrong shape and a table of what was measured is right.
        ///
        /// Nine points now rather than the original ladder's six. The five in between came
        /// from a run built for the purpose, with key tracking, velocity and the envelope
        /// all turned off, so nothing but the stored byte could reach the cutoff.
        ///
        /// The 50 point is the one that changed, from 1878 to 2210 - a quarter of an octave,
        /// in the middle of the range where most of the library sits. The old figure came
        /// from a clip with keyToFilter at 50, and key tracking turns out not to pivot where
        /// the model assumed; see KeyPivot. Two later takes, on two disks, measured 2211 and
        /// 2210 with tracking off, the second of them at five different keys with a spread of
        /// 0.000 octaves.
        ///
        /// A negative frequency means "as far open as it goes" - the reconstruction limit,
        /// which moves with the sample rate, so writing a number would wrongly cap a 48 kHz
        /// sample below what its own ceiling allows.
        /// </summary>
        static readonly double[,] Curve =
        {
            {  0,   311 }, { 20,   311 }, { 30,   544 }, { 40,  1138 }, { 50, 2210 },
            { 60,  4779 }, { 70,  8783 }, { 80,    -1 }, { 99,   -1 }
        };

        /// <summary>Measured: keyToFilter 50 is 1:1 tracking.</summary>
        public const double KeyFull = 50.0;

        /// <summary>
        /// Measured: the note at which key tracking adds nothing.
        ///
        /// NOT 60, which is what this assumed for as long as it had a tracking term at all.
        /// The same keygroup played at five keys four octaves apart tracked 0.980 octaves per
        /// octave and crossed its own untracked value at note 62.0, with the five untracked
        /// controls flat to 0.000 octaves so there was nothing else it could have been.
        ///
        /// It matters beyond the tracking itself: a pivot in the wrong place quietly offsets
        /// every cutoff ever read from a keygroup with tracking on, which is where the old
        /// 1878 Hz in the Curve came from.
        /// </summary>
        public const double KeyPivot = 62.0;

        /// <summary>Measured: octaves across the full velocity range.</summary>
        public const double VelOctaves = 8.34;

        /// <summary>Measured: the velocity that leaves the filter where it is.</summary>
        public const double VelPivot = 65.0;

        /// <summary>
        /// Measured: how far the filter envelope moves the cutoff at full amount.
        ///
        /// Five amounts each way from bases chosen to leave room in the direction under test,
        /// with the envelope held open so the corner stands still and can be read properly
        /// rather than traced through a sweep:
        ///
        ///     opening   0.167  0.171  0.168  0.162  octaves per unit  ->  8.37
        ///     closing   0.170  0.163  0.164                           ->  8.28
        ///
        /// Straight to within 0.013 octaves both ways, and the two agree to 1.1% - so a
        /// negative amount really does invert the envelope and go exactly as far, which the
        /// model had assumed without evidence.
        ///
        /// It also starts a unit or two off zero rather than at it. That dead zone is real
        /// and measured but not modelled here; it is worth less than the 9% this corrects.
        /// </summary>
        public const double EnvOctaves = 8.3;

        // -------------------------------------------------------------- the envelopes

        /// <summary>Measured: VCA decay 80 at 2.86 s.</summary>
        public const double EnvMinMs = 1.68;

        /// <summary>Assumed: the same 10000:1 span, moved with the bottom.</summary>
        public const double EnvMaxMs = 16800.0;

        /// <summary>Measured: attack 70 between 1.39 s and 1.66 s.</summary>
        public const double AttackScale = 1.33;

        /// <summary>Measured: 2.25 s against the VCA's 2.86.</summary>
        public const double VcfTimeScale = 0.78;

        /// <summary>Measured: a stored 50 read 19.6 dB down, so it counts decibels.</summary>
        public const double SustainDb = 39.6;

        /// <summary>Measured: a stored +20 read 5.7 dB up.</summary>
        public const double LoudnessDbPerUnit = 0.29;

        /// <summary>Measured, at velToLoudness 99.</summary>
        public const double VelDbPerStep = 0.63;

        // --------------------------------------------------------------------- the LFO

        /// <summary>
        /// Measured: eight rungs on a straight line in hertz, r2 0.99998.
        ///
        /// Linear in the stored byte, where the filter's cutoff is exponential and this was
        /// expected to be too. Two takes on different disks agreed to 0.2%.
        /// </summary>
        public const double LfoRateHzAtZero = 1.785;
        public const double LfoRateHzPerUnit = 0.08917;

        /// <summary>Measured: r2 0.9999, so depth 99 swings +-150 cents.</summary>
        public const double LfoDepthCentsPerUnit = 1.527;

        /// <summary>
        /// Measured: the fade reaches nine tenths of full depth at 7.50/(100-byte) seconds
        /// and climbs in a straight line to get there, so the whole ramp is that over 0.9.
        ///
        /// A fade-in, not a wait - at byte 0 the wobble is at depth within a twentieth of a
        /// second, and at 99 it climbs for seven and a half.
        /// </summary>
        public const double LfoDelayFadeConstant = 8.33;

        /// <summary>
        /// Measured: 72.3 cents at the top of the wheel with byte 22 at 99, r2 0.999, and
        /// byte 22 = 50 gave 0.509 of that where proportional would be 0.505.
        /// </summary>
        public const double LfoWheelCentsAtFull = 72.3;

        // ------------------------------------------------------------------- mappings

        static double Clamp(double v, double lo, double hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }

        /// <summary>
        /// A stored 0..99 cutoff in hertz, for audio leaving at <paramref name="rate"/>.
        ///
        /// The filter is also the reconstruction filter, so the top of its travel moves with
        /// the rate the audio comes out at rather than being a fixed frequency.
        /// </summary>
        public static double CutoffHz(int stored, double rate)
        {
            double ceiling = MaxRatio * (rate > 0 ? rate : 48000.0);
            double floor = Math.Min(FloorHz, ceiling);
            double v = Clamp(stored, 0, 99);

            int n = Curve.GetLength(0);
            double hz = At(n - 1, ceiling);

            for (int i = 1; i < n; i++)
            {
                if (v > Curve[i, 0]) continue;

                double lo = At(i - 1, ceiling), hi = At(i, ceiling);
                double span = Curve[i, 0] - Curve[i - 1, 0];
                double t = span == 0 ? 0 : (v - Curve[i - 1, 0]) / span;
                hz = lo * Math.Pow(hi / lo, t);          // straight in log frequency
                break;
            }

            return Clamp(hz, floor, ceiling);
        }

        static double At(int i, double ceiling)
        {
            return Curve[i, 1] < 0 ? ceiling : Curve[i, 1];
        }

        /// <summary>A stored 0..99 envelope time, in seconds.</summary>
        public static double EnvSeconds(int stored)
        {
            double v = Clamp(stored, 0, 99) / 99.0;
            return (EnvMinMs * Math.Pow(EnvMaxMs / EnvMinMs, v)) / 1000.0;
        }

        public static double DbToGain(double db) { return Math.Pow(10.0, db / 20.0); }

        /// <summary>log2, which .NET 4 does not have.</summary>
        public static double Log2(double v) { return Math.Log(v) / Math.Log(2.0); }
    }
}
