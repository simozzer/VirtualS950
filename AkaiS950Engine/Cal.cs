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
        public const double EnvOctaves = 8.5;

        // -------------------------------------------------------------- the envelopes

        /// <summary>
        /// Measured points, not a formula - see <see cref="EnvSeconds"/>.
        ///
        /// This replaces a pair of constants, one measured and one assumed: a shortest time
        /// of 1.68 ms taken from a single VCA decay at stored 80, and a 10000:1 span across
        /// the range that nothing had ever checked. The span is the part that was wrong. The
        /// real one is nearer 1400:1, so the old curve ran half as fast as the machine below
        /// stored 70 and nearly twice as fast above stored 85.
        ///
        /// Nine settings, measured on the filter envelope where a moving cutoff can be
        /// watched all the way down a fourteen-second note. Attack, decay and release are
        /// three separate readings of this one curve, and where they overlap they agree to
        /// 1.07x - so it really is one curve, and it is this one.
        ///
        ///     stored      50     55     60     65     70     80     85     90     95
        ///     measured  0.357  0.418  0.722  0.881  1.404  2.814  4.037  4.117  8.095
        ///     old model 0.176  0.280  0.446  0.711  1.131  2.868  4.567  7.272 11.580
        ///
        /// Measured twice over, because the analysis has a bias of its own - a window
        /// averages the sweep passing through it - and that bias is found by putting a RENDER
        /// of the model through the same analysis, where the answer is known. The first pass
        /// used a model that was out by up to 2x, so its biases were taken at the wrong sweep
        /// rates; adopting its table and measuring again moved every point by less than 8%.
        /// A third pass against THIS table reads every setting back at 0.95 to 1.04 of it,
        /// and the points oscillate rather than drift - so what is left is the measurement's
        /// own repeatability and not an error still to be chased.
        ///
        /// Stored 50 only appears at all on the second pass: under the old curve the render
        /// was over inside one analysis window, so there was nothing to take a bias from.
        ///
        /// The VCA decay confirms it independently, from the same take and a different
        /// envelope: measured against a fixed depth it gives 3.96 s at stored 85 against this
        /// table's 4.04, and 8.49 s at stored 95 against 8.09.
        ///
        /// STORED 90 IS THE ODD ONE
        ///
        /// Everything else sits within a few per cent of a plain exponential through these
        /// points. Stored 90 sits 41% off it, and all three of attack, decay and release put
        /// it there, agreeing with each other to 1.03x. So it is kept as measured rather than
        /// smoothed away - but it is the one point a second take should be asked about first.
        ///
        /// THE ENDS ARE EXTRAPOLATED, NOT MEASURED
        ///
        /// Nothing reaches below 50 or above 95: the fast end is over inside one analysis
        /// window and the slow end outlasts a note. Both ends continue at the slope fitted
        /// across every measured point, which is the best that can honestly be said of them.
        /// Extrapolating from the two nearest points instead put stored 0 at 320 ms, which
        /// every percussive sample in the library refutes.
        /// </summary>
        public static readonly double[,] EnvTime =
        {
            {  0, 0.01040 }, { 50, 0.3565 }, { 55, 0.4184 }, { 60, 0.7224 }, { 65, 0.8806 },
            { 70, 1.4037 }, { 80, 2.8136 }, { 85, 4.0370 }, { 90, 4.1172 }, { 95, 8.0947 },
            { 99, 10.7401 }
        };

        /// <summary>
        /// The VCA attack is a counter, and this is how long it takes at one step per tick.
        ///
        /// It was AttackScale - a single multiplier on the shared envelope curve - and no
        /// multiplier can be right, because the attack does not follow that curve and does not
        /// follow any smooth curve at all. Thirteen settings measured on the hardware:
        ///
        ///     stored     30    40    50    55    60    65    70    75    80    85  90  95  99
        ///     seconds  .209  .362  .603  .766  .906 1.081 1.350 1.350 1.796 1.797 2.70 2.70 2.70
        ///     5.4 / n    26    15     9     7     6     5     4     4     3     3   2   2   2
        ///
        /// Every one of them is 5.4/n for a whole number n, to within 0.7%. That is not a fit,
        /// it is the mechanism: an envelope counter adding n units a tick across a fixed span.
        /// It is also why stored 70 and 75 come back identical to four digits, and 80 and 85,
        /// and 90, 95 and 99 - they share an n. And it is why the attack STOPS getting slower
        /// at 2.70 s: n bottoms out at 2, where the shared curve wanted 10.74 s at stored 99.
        ///
        /// The level analysis that measured this reads a rendered model back to within 1%
        /// across a 150:1 range of times, so 0.7% is the hardware quantising and not the rig.
        ///
        /// Only the VCA attack is known to do this. The filter attack was measured in an
        /// earlier run to about 5%, which is too coarse to see a 0.7% quantisation, so it
        /// keeps the shared curve - not because it is smooth but because nothing has looked.
        /// The VCA decay's four points hint at the same structure and cannot carry it.
        /// </summary>
        public const double VcaAttackSpan = 5.4;

        /// <summary>
        /// An attack this short is a step, and is rendered as one.
        ///
        /// A counter whose increment covers the whole span arrives on the first tick, and
        /// there is no ramp left to render - so past a point the right answer is zero rather
        /// than a very small number. A millisecond is well inside one control block.
        ///
        /// This is what makes attack 0 a hard gate. That is not a measurement - nothing in any
        /// run reaches below stored 30 - it is how envelope generators are built. The bottom
        /// of an attack range means no attack stage at all rather than a very short one, and
        /// a sampler that could not gate a drum would be the exception rather than the rule.
        ///
        /// The only thing that ever argued otherwise was an extrapolation: the slope from
        /// stored 30 to 40, carried thirty units down, put stored 0 at 40 ms - a soft attack
        /// on every percussive sample there is. A guess reaching that far loses to how the
        /// things are made.
        /// </summary>
        public const double VcaAttackGate = 0.001;

        /// <summary>
        /// How many units a tick, by stored byte. The measured points are exact.
        ///
        /// Between them n is interpolated and rounded, so the intermediate whole numbers each
        /// get their own stretch of the range - the right shape, though exactly where each
        /// step falls is not measured.
        ///
        /// Two of these were measured sideways, through velocity. The stretch below stored 30
        /// used to be a single guessed entry - 7000 at stored 0, interpolated the whole way to
        /// the measured 26 at stored 30 - because no run ever set an attack byte that low.
        ///
        /// Run 7 reached it from the side. Velocity to attack turned out to be a plain
        /// subtraction from the attack byte (see <see cref="VelocityAttackByte"/>), so a base
        /// of 70 struck hard enough lands wherever you like: velocity 64 at full depth puts it
        /// at stored 20.1 and velocity 80 at stored 7.6. Those two clips are the first
        /// measurements of this stretch, and the guess was out by 3x and 6x:
        ///
        ///     stored        7.6    20.1
        ///     measured n    270      55
        ///     the guess    1684     164
        ///
        /// They are entered at 8 and 20, where they fall to the nearest byte. This leans on
        /// the velocity rule being right, which is fair - that rule is confirmed against seven
        /// clips in the region where this table IS measured, every one within a single counter
        /// step - but it is a rung below the rest of the table, and stored 7.6 is the weakest
        /// thing here: 0.020 s is near the floor of what the analysis can time, and an error
        /// there is large in n.
        ///
        /// Stored 0 is still the gate rather than a measurement. Extrapolating the two new
        /// points downwards puts it at about 7.6 ms, which is the first evidence that ever
        /// bore on it and is not enough to overturn how envelope generators are built. What
        /// the new points change is that the climb out of the gate is now anchored at 8 and
        /// 20 instead of running unguided all the way to 30.
        /// </summary>
        public static readonly double[,] VcaAttackSteps =
        {
            {  0, 7000 }, {  8, 270 }, { 20, 55 },
            { 30,   26 }, { 40,  15 }, { 50, 9 }, { 55, 7 }, { 60, 6 }, { 65, 5 },
            { 70,    4 }, { 75,   4 }, { 80, 3 }, { 85, 3 }, { 90, 2 }, { 95, 2 }, { 99, 2 }
        };

        /// <summary>Measured: 2.25 s against the VCA's 2.86.</summary>
        public const double VcfTimeScale = 0.78;

        /// <summary>
        /// THE RELEASE IS A RATE, NOT A DURATION.
        ///
        /// The release byte sets how fast the envelope falls, not how long it takes to get
        /// there - so a release from half depth is over in half the time. The engines used to
        /// do the opposite for the filter: fall from wherever you are TO ZERO over the release
        /// time, which makes a shallow release crawl. They also, in the same voice, did the
        /// right thing for the amplitude, which drops a fixed number of decibels per second
        /// whatever level it starts from. One generator, two rules, neither measured.
        ///
        /// Nothing had caught it because every release ever measured started from a sustain of
        /// 99 and fell the whole depth, which is the one case where the two agree. Three
        /// sustains at one release setting separate them:
        ///
        ///     depth        2.04 oct   1.24 oct   0.72 oct
        ///     measured       3.13 s     1.88 s     1.13 s
        ///     a rate         3.15       1.91       1.11
        ///     a duration     3.15       3.15       3.15
        ///
        /// Within 2% of a rate at every depth, and 0.652, 0.660 and 0.637 octaves per second
        /// read three ways, which is the same number to 3%.
        ///
        /// Nothing measured before this changes: at full depth the two rules give the same
        /// answer, and every earlier release measurement was taken there.
        /// </summary>
        public const bool ReleaseIsARate = true;

        /// <summary>
        /// How far the VCA release falls in one release time: 40 dB, measured.
        ///
        /// Every engine had this at 80 - the gain ran down to 1e-4 over EnvSeconds(byte) - and
        /// nothing had checked it, because until run 9 no recording timed a release against a
        /// known byte at more than one setting.
        ///
        /// Run 9 times sixteen releases spanning stored 20 to 95. Divide the 40 dB each took by
        /// the curve's time for its byte and the span is flat: twelve of the sixteen give
        /// 41.0 dB with a spread of 0.7, across a 200:1 range of release times. That is a
        /// constant, and it is 40 rather than 80.
        ///
        /// So the envelope curve was right and the span was wrong - the better of the two
        /// answers, since the same curve still serves the attack, the decay and the filter.
        /// It is why every release ran at twice the machine's speed: 699 ms against 1366 at
        /// stored 70, on every programme rather than only those with a velocity depth.
        ///
        /// The four that miss are at stored 44.6 and 46, and imply 49.3 dB. They miss the same
        /// way the velocity-release fit missed, in the same place and the same direction, so
        /// EnvTime is about 20% slow around stored 45. Left alone: one bad setting in a
        /// nine-point measured table wants re-measuring, not a second constant over the top.
        ///
        /// It is a RATE, so this is how far the level falls in one release time from wherever
        /// the key came up - not the distance it has to cover before it stops.
        /// </summary>
        public const double VcaReleaseDb = 40.0;

        /*
         * WARP - keygroup bytes 12, 13 and 14. A pitch bend at note-on, decaying back to pitch.
         *
         *     bend in cents = WarpCentsPerUnit * byte13 * scale,  decaying as exp(-t / tau)
         *     scale = 1                                 when byte 12 is 0
         *           = (byte12 / 99) * (velocity / 127)  when byte 12 is above 0
         *
         * Measured over runs 10, 11 and 12; the model fits 25 clips at 7.3% rms, worst 17%.
         * 98 of the 1908 keygroups on the real disks use it, and until now every engine read
         * the bytes and dropped them.
         *
         * BYTE 13 IS THE DEPTH, AND BYTE 12 IS NOT. The panel calls byte 12 "warp velocity",
         * which reads like the depth; it is how far velocity scales the depth, and ZERO MEANS
         * OFF rather than none. At byte 12 = 0 the bend is full however gently the key is
         * struck - 364, 342, 320 and 338 cents at velocities 1, 32, 64 and 127, no trend. At
         * byte 12 = 99 the same keygroup is flat at velocity 1 and bends 298 cents at 127.
         * That decides how 22 real keygroups sound, the ones setting byte 13 with byte 12 at 0.
         *
         * THERE IS NO KEY FOLLOW. Two published descriptions call byte 13 a key follow that
         * shortens the decay as notes rise. Struck at keys 48, 60 and 72 the same keygroup
         * gave 64.2, 64.2 and 70.7 ms. The panel's name for it, ATTACK OFFSET, survives.
         */

        /// <summary>
        /// Cents per unit of byte 13, at full scale.
        ///
        /// The fit gives 6.21 and cannot separate 6.0 from 6.5 - rms is 9.3%, 7.3% and 7.2%
        /// at 6.0, 6.25 and 6.5. So 6.25 is a CHOICE among values the measurement allows,
        /// taken because it is one sixteenth of a semitone exactly and this machine has form
        /// for mechanism-shaped numbers - the VCA attack is 5.4/n for whole n. It is not read
        /// to that precision.
        /// </summary>
        public const double WarpCentsPerUnit = 6.25;

        /// <summary>
        /// Where a keygroup's output port puts it in the stereo pair, as a left and right gain.
        ///
        /// Byte 19, read off the panel: 0 ALL, 1..8 the individual MONO outputs, 9 LEFT,
        /// 10 RIGHT. 291 of the 1908 library keygroups set it, all of them in drum and
        /// percussion programmes except two - TUBULAR 2, whose eight keygroups read L L L L
        /// R R R R, and PIZ-CHORUS, which is one keygroup each side.
        ///
        /// LEFT and RIGHT are hard: the machine's left and right sockets are two mono outputs,
        /// not a pan pot, so a keygroup sent to one of them is absent from the other.
        ///
        /// MONO 1 TO 8 ARE LEFT CENTRED, WHICH IS A PLACEHOLDER AND NOT A MEASUREMENT.
        ///
        /// Those are eight physical jacks on the back of the machine. What the main stereo
        /// pair does with a keygroup routed to one of them is a question about hardware that
        /// no disk can answer: on many samplers of that era assigning a voice to an individual
        /// output REMOVES it from the main mix, which would make 253 library keygroups silent
        /// here rather than centred. Until somebody plays one and listens to the main outs,
        /// centring them is the change that cannot make anything worse - it is what all three
        /// engines already did with every keygroup.
        /// </summary>
        public static void OutputGains(int port, out double left, out double right)
        {
            if (port == 9)       { left = 1.0; right = 0.0; return; }   // LEFT
            if (port == 10)      { left = 0.0; right = 1.0; return; }   // RIGHT

            left = 1.0; right = 1.0;                                    // ALL, and MONO 1..8
        }

        /// <summary>
        /// The time constant of the bend in seconds, by byte 14. Measured, eleven points.
        ///
        /// Nothing like <see cref="EnvTime"/>: it spans 22:1 where that spans 1000:1. Byte 14
        /// = 50 is the mean of eight independent clips reading 68 to 70 ms; byte 14 = 99 is two
        /// clips in different runs reading 749 and 762. The points at 30, 40, 60, 70, 90 and 95
        /// were measured deliberately because the curve turns over hardest above 80, and
        /// interpolating through a turn is how EnvTime came to be 20% wrong around stored 45.
        ///
        /// 99 is what 1529 of the 1908 real keygroups carry, so 755 ms is the common case.
        /// </summary>
        public static readonly double[,] WarpTime =
        {
            {  0, 0.0343 }, { 20, 0.0432 }, { 30, 0.0483 }, { 40, 0.0577 }, { 50, 0.0695 },
            { 60, 0.0862 }, { 70, 0.1128 }, { 80, 0.1596 }, { 90, 0.2760 }, { 95, 0.4327 },
            { 99, 0.7555 }
        };

        /// <summary>
        /// How far Warp bends the pitch when the key goes down, in cents. Signed: negative
        /// starts flat and rises to pitch, positive starts sharp and falls to it.
        /// </summary>
        public static double WarpCents(int velToWarp, int depth, double velocity)
        {
            double d = Clamp(depth, -50, 50);
            if (d == 0) return 0;                  // a depth of nothing bends nothing

            double v = Clamp(velToWarp, 0, 99);
            double scale = v == 0 ? 1.0 : (v / 99.0) * (Clamp(velocity, 0, 127) / 127.0);

            return WarpCentsPerUnit * d * scale;
        }

        /// <summary>The bend's time constant in seconds, from <see cref="WarpTime"/>.</summary>
        public static double WarpSeconds(double stored)
        {
            double v = Clamp(stored, 0, 99);
            int n = WarpTime.GetLength(0);
            double seconds = WarpTime[n - 1, 1];

            for (int i = 1; i < n; i++)
            {
                if (v > WarpTime[i, 0]) continue;

                double lo = WarpTime[i - 1, 1], hi = WarpTime[i, 1];
                double span = WarpTime[i, 0] - WarpTime[i - 1, 0];
                double t = span == 0 ? 0 : (v - WarpTime[i - 1, 0]) / span;
                seconds = lo * Math.Pow(hi / lo, t);       // straight in log time
                break;
            }

            return seconds;
        }

        /// <summary>
        /// The playback-rate multiplier Warp applies `t` seconds into a note.
        ///
        /// The bend is exponential - traced against a fitted curve it holds to 2-3% from full
        /// depth down to a tenth of it - so this is the whole shape, and 1.0 once it is over.
        /// </summary>
        /// <summary>
        /// The pitch wheel as a multiplier on the playback rate.
        ///
        /// `wheel` is the MIDI value 0..16383, resting at 8192; `range` is the machine's MIDI
        /// page setting in semitones, 1 to 12.
        ///
        /// The two halves are not the same width - 8192 steps below the centre and 8191 above
        /// - so dividing by 8192 both ways leaves a full upward bend one step short of the
        /// range. Inaudible, and the kind of wrong nobody finds later because nobody measures
        /// a wheel at its stop.
        ///
        /// The range belongs to the MACHINE rather than to a programme, so nothing reads it
        /// off a disk: the OVERALL SETTINGS file that would hold it is written only when
        /// somebody saves it deliberately.
        /// </summary>
        public static double BendRatio(int wheel, double range)
        {
            int w = wheel < 0 ? 0 : (wheel > 16383 ? 16383 : wheel);
            double off = w - 8192;
            if (off == 0.0 || range == 0.0) return 1.0;

            double semis = range * (off >= 0 ? off / 8191.0 : off / 8192.0);
            return Math.Pow(2.0, semis / 12.0);
        }

        public static double WarpRatio(int velToWarp, int depth, int time,
                                       double velocity, double t)
        {
            double cents = WarpCents(velToWarp, depth, velocity);
            if (cents == 0) return 1.0;

            return Math.Pow(2.0, cents * Math.Exp(-t / WarpSeconds(time)) / 1200.0);
        }

        /// <summary>
        /// The filter's release follows this same scale, and there is no cap on it.
        ///
        /// There was one, briefly, at a second. It came from two takes whose probe
        /// measurements had three faults in them - a spectrum routine that averaged each
        /// probe with its neighbours, a crossing found by scanning from the noisy end, and
        /// probes placed within a twentieth of an octave of where the sweep began. Fixed,
        /// and read as a rate across the whole sweep, the third take says the release keeps
        /// growing and the original scale was close all along:
        ///
        ///     stored        50     60     70     80
        ///     measured    0.250  0.526  1.159  2.061
        ///     this scale  0.137  0.348  0.882  2.237
        ///
        /// Within 1.8x at the fast end and 1.08x at the slow one, and the slow end is where
        /// the measurement is most trustworthy. So: no cap, and no separate release curve -
        /// one fitted to four points whose fast end is knowingly biased would be worse than
        /// the scale already here.
        /// </summary>

        /// <summary>Measured: a stored 50 read 19.6 dB down, so it counts decibels.</summary>
        public const double SustainDb = 39.6;

        /// <summary>Measured: a stored +20 read 5.7 dB up.</summary>
        public const double LoudnessDbPerUnit = 0.29;

        /// <summary>Measured, at velToLoudness 99.</summary>
        public const double VelDbPerStep = 0.63;

        // ------------------------------------------------ the positional crossfade

        /// <summary>
        /// How far the crossfade pulls a keygroup down, against where the key sits in the
        /// overlap it shares with another. Program header byte 21 turns it on.
        ///
        /// 48 of the 390 library programmes set it AND have overlapping keygroups, and they
        /// are the multi-sampled instruments: GRAND-PNO1 and 2 with nine keygroups apiece,
        /// GRANDX, CB CEL VL. Every engine here used to sound both keygroups at full level -
        /// about 6 dB too loud, with two different recordings of one note beating together.
        ///
        /// WHERE A KEY SITS IN AN OVERLAP
        ///
        ///     x = (i + 1) / (N + 1)    i the 0-based key within the overlap, N its width
        ///
        /// The lower keygroup reads the table at x and the upper at 1 - x; run 17 read both
        /// tones at every key and they mirror. The ends are what make this right rather
        /// than the obvious i/(N-1): seven widths from 1 key to 21 were measured, and
        /// wherever two land on the same x they agree to 0.1 dB - x = 0.14 reads -0.4 at
        /// widths 13 and 21, x = 0.67 reads -8.7 at widths 2, 5 and 21. And a ONE-KEY
        /// overlap, which 13 library pairs have, sits at x = 1/2 and splits evenly at
        /// -4.5 dB, where i/(N-1) divides by zero.
        ///
        /// THE FIRST VERSION OF THIS TABLE WAS MEASURED THROUGH A LIMITER AND WAS WRONG.
        /// Every calibration take up to run 17 was recorded with 20-30% of its samples
        /// pinned within 0.1 dB of -0.49 dBFS. The crossfade is the one TWO-TONE measurement
        /// in the rig, and that is what made it the worst casualty: a weak tone sharing a
        /// signal with a limited strong one is dragged down by the strong one's gain
        /// reduction, so the error grows as the tones become unequal - towards the edges of
        /// the overlap. The old readings were out by 0.2 dB at x = 0.1, 1.5 dB at x = 0.5
        /// and 6.1 dB at x = 0.9, and gave cos(pi x / 2) ^ 1.44 with the pair dipping 1.3 dB
        /// at the midpoint. "No standard crossfade does that" was written down at run 15 and
        /// treated as a curiosity. It was the limiter.
        ///
        /// Clean, the pair conserves power to 0.1 dB.
        ///
        /// A TABLE RATHER THAN A CURVE, AND NOW FOR A MEASURED REASON. Every distinct level
        /// in the clean take is a whole number of 0.4 DECIBEL STEPS - eighteen of them, from
        /// 0 to -28.0, landing on the grid to better than 0.1 dB:
        ///
        ///     dB      0.0  -0.4  -0.8  -1.2  -1.6  -2.0  -3.2  -5.6  -6.0  -6.8  -8.0  -8.8
        ///     steps     0     1     2     3     4     5     8    14    15    17    20    22
        ///
        /// and 0.4 dB is the same unit run 19 measured for the SUSTAIN, at 0.400 dB per
        /// stored unit. The machine counts decibels in 0.4 dB steps and the crossfade is a
        /// lookup of that count, so no smooth function can be right in detail.
        ///
        /// WIDTH 9 DISAGREES AT ITS LAST KEY, and it survived the re-record: x = 0.9 reads
        /// -17.6 where width 13 at 0.929 and width 21 at 0.909 both read -21.6. Four
        /// decibels, against the 0.1 the rest agree to. With the gain quantised that is what
        /// you would expect - two (i, N) pairs landing on nearby x can still fall on
        /// different integer steps - so the real rule is arithmetic on i and N rather than a
        /// function of x, and nobody has worked it out. Both points are kept, so every
        /// measured width reproduces its own reading.
        ///
        /// x = 0 and x = 1 are extrapolations, reached only by overlaps wider than 21 keys:
        /// x lives in [1/(N+1), N/(N+1)] and width 21 already spans 0.045 to 0.955.
        /// </summary>
        /// CONFIRMED ACROSS TWO CLEAN SESSIONS. Run 15 was re-recorded after run 17, on its
        /// own disk, and its thirteen-key overlap sits on keys 72 to 84 where run 17's sits
        /// on 85 to 97. Seven positions, different keys, different disk, different take:
        ///
        ///     x        0.071  0.214  0.357  0.500  0.643  0.786  0.929
        ///     run 17     0.0   -0.4   -1.6   -3.2   -6.0  -11.6  -21.6
        ///     run 15     0.0   -0.4   -1.6   -3.1   -6.0  -11.6  -21.7
        ///
        /// Run 15's SEVEN-key overlap is a width run 17 never played, and it contributes
        /// 0.125, 0.375, 0.625 and 0.875 - each sharpening where a step falls.
        public static readonly double[,] XfadeDb =
        {
            { 0.00000,   0.00 }, { 0.04545,   0.00 }, { 0.07143,   0.00 }, { 0.09091,   0.00 },
            { 0.10000,   0.00 }, { 0.12500,   0.00 }, { 0.13636,  -0.40 }, { 0.14286,  -0.40 },
            { 0.16667,  -0.40 }, { 0.20000,  -0.40 }, { 0.21429,  -0.40 }, { 0.25000,  -0.80 },
            { 0.30000,  -1.20 }, { 0.31818,  -1.60 }, { 0.33333,  -1.60 }, { 0.35714,  -1.60 },
            { 0.37500,  -1.60 }, { 0.40000,  -2.00 }, { 0.50000,  -3.20 }, { 0.60000,  -5.60 },
            { 0.62500,  -5.60 }, { 0.64286,  -6.00 }, { 0.66667,  -6.80 }, { 0.68182,  -6.80 },
            { 0.70000,  -8.00 }, { 0.75000,  -8.80 }, { 0.78571, -11.60 }, { 0.80000, -11.60 },
            { 0.83333, -13.20 }, { 0.85714, -15.20 }, { 0.86364, -15.20 }, { 0.87500, -15.20 },
            { 0.90000, -17.60 }, { 0.90909, -21.60 }, { 0.92857, -21.60 }, { 0.95455, -28.00 },
            { 1.00000, -39.20 }
        };

        /// <summary>
        /// TWO KEYGROUPS ON EXACTLY THE SAME KEYS ARE NOT FADED. They sit at a constant
        /// level down apiece, the same at every key across a thirteen-key range, which is
        /// what run 15 measured with the crossfade on. It is not the table read at some x -
        /// it does not move with the key at all. 17 library pairs are exactly this, the
        /// ARP2600 layers, and treating identical ranges as an overlap to fade across would
        /// have half-silenced every one of them.
        ///
        /// ZERO, AND THE MACHINE'S OWN DISTORTION IS WHAT PROVES IT.
        ///
        /// This read -3.7 dB for a long time, from a take with 25% of its samples pinned.
        /// It was wrong, and how it came out is worth keeping.
        ///
        /// The section is the loudest in the run - the one place two keygroups both sound
        /// at nearly full level - and it is the ONLY section that kept distorting after the
        /// recording level came down. Dropping the input 2.66 dB left the other three
        /// sections completely clean and changed this one's reading not at all: -0.7 dB
        /// apiece and a pair summing to +2.3, identical to the decimal across both takes,
        /// with its ceiling moving down by exactly the 2.66 dB the input had. A ceiling
        /// that scales with the recording gain is UPSTREAM of it. The S950 is distorting
        /// its own output and no recording level will ever fix it.
        ///
        /// That fact is the measurement. Two equal sources at L dB relative to one of them
        /// alone sum to 3.01 + L, and any compressive distortion can only REDUCE what the
        /// pair measures - intermodulation lands away from either tone's bin, never in it.
        /// So the measured sum is a lower bound:
        ///
        ///     sum >= +2.3   ->   L >= -0.71 dB
        ///     a layer cannot be louder than itself alone   ->   L &lt;= 0
        ///
        /// which on the machine's 0.4 dB grid leaves 0 steps or 1, and nothing else. -3.7
        /// would sum to -0.7 and -3.0 to 0.0; both are ruled out by three decibels.
        ///
        /// So identical ranges are NOT faded: no lower keygroup and no upper one, nothing
        /// to fade across, so the machine simply layers them - which is exactly why two
        /// full-level voices overflow its output and it saturates. 17 library pairs are
        /// this, the ARP2600 layers, and they have been playing 3.7 dB too quiet apiece.
        ///
        /// A step of 0.4 dB is still possible and would need a disk that trims both
        /// keygroups down at source, so the machine is not saturating while it is read.
        /// </summary>
        public const double XfadeSameRangeDb = 0.0;

        /// <summary>
        /// The table read at <paramref name="x"/>, straight-line between the measured
        /// points and in decibels, which is the domain the machine counts in.
        /// </summary>
        public static double CrossfadeDb(double x)
        {
            double v = Clamp(x, 0, 1);
            int n = XfadeDb.GetLength(0);

            for (int i = 1; i < n; i++)
            {
                if (v > XfadeDb[i, 0]) continue;

                double span = XfadeDb[i, 0] - XfadeDb[i - 1, 0];
                double t = span == 0 ? 0 : (v - XfadeDb[i - 1, 0]) / span;
                return XfadeDb[i - 1, 1] + t * (XfadeDb[i, 1] - XfadeDb[i - 1, 1]);
            }

            return XfadeDb[n - 1, 1];
        }

        /// <summary>
        /// What one keygroup should be played at, as a linear gain, given every other
        /// keygroup answering the same note.
        ///
        /// PAIRWISE, AND THE DECIBELS ADD. A keygroup overlapping two neighbours is faded
        /// against each and the attenuations multiply. Run 15 plays a three-deep stack -
        /// keygroups at 100-112, 104-116 and 108-120 - and once it was re-recorded clean the
        /// answer is not close:
        ///
        ///     key        106          108              110              112          114
        ///     keygroup  T1    T2   T1    T2    T3   T1    T2    T3   T1    T2   T3   T2    T3
        ///     measured -1.2  -7.9 -3.6 -3.1 -31.4 -11.2 -2.3 -11.4 -31.1 -3.1 -3.8 -8.0 -1.2
        ///     product  -1.2  -8.0 -3.6 -3.2 -30.8 -11.2 -2.4 -11.2 -30.8 -3.2 -3.6 -8.0 -1.2
        ///     deepest  -1.2  -8.0 -3.2 -3.2 -17.6  -8.0 -1.2  -8.0 -17.6 -3.2 -3.2 -8.0 -1.2
        ///
        /// The product fits at 0.21 dB rms over thirteen readings; taking only the deepest
        /// single fade misses by 5.52 and is out by 14 dB where three keygroups pile up.
        ///
        /// THE LIMITED TAKE COULD NOT TELL THEM APART. Its two deep readings sat at the
        /// noise floor - about -33 where the truth is -31 - and it put the product some 2 dB
        /// deep through the middle, so the rule was adopted because the floor ruled out the
        /// alternative rather than because it fitted. Clean, the fit decides it outright.
        /// </summary>
        /// <param name="lows">One per keygroup answering this note, in program order.</param>
        /// <param name="highs">Their top keys, same order.</param>
        /// <param name="count">How many of those arrays are in use.</param>
        /// <param name="self">Which one to work out the gain for.</param>
        public static double CrossfadeGain(int note, int[] lows, int[] highs,
                                           int count, int self)
        {
            if (lows == null || highs == null || self < 0 || self >= count) return 1.0;

            int low = Math.Min(lows[self], highs[self]);
            int high = Math.Max(lows[self], highs[self]);
            if (note < low || note > high) return 1.0;

            double db = 0;

            for (int i = 0; i < count; i++)
            {
                if (i == self) continue;

                int oLow = Math.Min(lows[i], highs[i]), oHigh = Math.Max(lows[i], highs[i]);

                // A keygroup that does not answer this note is not fading it. Skipped
                // rather than clamped into the overlap: clamping turns a caller's mistake
                // into a plausible-looking attenuation.
                if (note < oLow || note > oHigh) continue;

                if (oLow == low && oHigh == high) { db += XfadeSameRangeDb; continue; }

                int lo = Math.Max(low, oLow), hi = Math.Min(high, oHigh);
                if (hi < lo) continue;

                int width = hi - lo + 1;
                double x = (note - lo + 1) / (double)(width + 1);

                // The one that starts lower fades OUT going up. Where they start together
                // the one that ends lower does - which run 17 never played and no library
                // programme yet seen needs, so it is a choice rather than a reading.
                bool lower = low < oLow || (low == oLow && high < oHigh);
                db += CrossfadeDb(lower ? x : 1 - x);
            }

            return Math.Pow(10.0, db / 20.0);
        }

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

        /// <summary>
        /// A stored 0..99 envelope time, in seconds, read off <see cref="EnvTime"/>.
        ///
        /// Straight in log time between the measured points, the same way CutoffHz is
        /// straight in log frequency between its own: the quantity is exponential in the
        /// stored byte, so a straight line in the log is what "between two measurements"
        /// means here.
        ///
        /// It takes a double because the stored byte is no longer where the argument comes
        /// from. Velocity moves the release off the whole numbers - see
        /// <see cref="VelocityReleaseByte"/> - and an int parameter would have truncated
        /// every one of those silently, which the web and C++ engines, both taking a double,
        /// would not have done. The three are meant to agree to the digit.
        /// </summary>
        public static double EnvSeconds(double stored)
        {
            double v = Clamp(stored, 0, 99);
            int n = EnvTime.GetLength(0);
            double seconds = EnvTime[n - 1, 1];

            for (int i = 1; i < n; i++)
            {
                if (v > EnvTime[i, 0]) continue;

                double lo = EnvTime[i - 1, 1], hi = EnvTime[i, 1];
                double span = EnvTime[i, 0] - EnvTime[i - 1, 0];
                double t = span == 0 ? 0 : (v - EnvTime[i - 1, 0]) / span;
                seconds = lo * Math.Pow(hi / lo, t);     // straight in log time
                break;
            }

            return seconds;
        }

        /// <summary>
        /// The attack byte a strike of this velocity actually plays - keygroup byte 9 applied.
        ///
        /// MEASURED, run 7. A harder strike makes the attack SHORTER, and it does it by plain
        /// subtraction from the attack byte, before the counter ever sees it:
        ///
        ///     effective = attack - (velocity / 127) * velToAttack        clamped 0..99
        ///
        /// Eleven clips on one disk, a base attack of 70, depths of 0, 30, 75 and 99. In the
        /// region where <see cref="VcaAttackSteps"/> is itself measured every one lands within
        /// a single counter step, which is all the resolution a counter has:
        ///
        ///     depth  vel   effective byte   measured n   the table
        ///        99    1             69.2          4.0           4
        ///        99   16             57.5          7.0           6
        ///        99   32             45.1         11.0          12
        ///        99   48             32.6         22.0          23
        ///        30  127             40.0         14.1          15
        ///         0    1 / 127       70.0     4.0 / 4.0          4
        ///
        /// There is no pivot. Velocity to FILTER turns about 65 - a soft strike goes down
        /// where a hard one goes up - and the obvious guess was that the attack did the same.
        /// It does not: at full depth velocity 1 played 1.344 s against a base of 1.350, so a
        /// soft strike leaves the byte alone. A pivot at 64 would have put velocity 1 at a
        /// negative byte, which is to say gated, and the recording has a second of ramp on it.
        ///
        /// The panel writes this byte one to one. Setting velocity sensitivity for attack to
        /// 91 on the machine and saving put exactly 91 into byte 9 - no scale, no offset - and
        /// the same save put 17 into byte 10 from a panel reading of 17. So the number on the
        /// panel IS the number in the record, and a depth read off a real disk means what it
        /// says.
        ///
        /// That is worth stating because a panel reading taken before the flip-and-diff
        /// suggested otherwise: 46 was read off a keygroup this disk stores as 99. The diff is
        /// the stronger evidence - it changes one field at a time and reads the result out of
        /// the bytes - and the measurement agrees with it independently. By velocity 80 the
        /// attack is down to 0.020 s, a shift of some 62 byte units, and a depth of 46 could
        /// not shift more than 46 even at full velocity. Whatever that reading was, it was not
        /// this field.
        /// </summary>
        public static double VelocityAttackByte(double stored, double depth, double velocity)
        {
            double vel = Clamp(velocity, 0, 127);
            return Clamp(stored - (vel / 127.0) * Clamp(depth, 0, 99), 0, 99);
        }

        /// <summary>
        /// The release byte a strike of this velocity plays - byte 10 and flag 0x10 applied.
        ///
        /// MEASURED, run 9. Unlike the attack, this one PIVOTS, and about velocity 64:
        ///
        ///     effective = release + 2 * velToRelease * (velocity - 64) / 63   clamped 0..99
        ///
        /// Eighteen clips, a base release of 70, depths of +25, -25, +12, -12 and 0, at up to
        /// five velocities each. Time from key-up to 40 dB down:
        ///
        ///     depth    vel 1    vel 32    vel 64    vel 96   vel 127
        ///      +25      42ms     195ms    1349ms    8272ms   11162ms
        ///      -25   11093ms    8325ms    1354ms     194ms      41ms
        ///      +12     224ms         -    1365ms         -    6994ms
        ///      -12    7081ms         -    1337ms         -     219ms
        ///        0    1366ms         -         -         -    1354ms
        ///
        /// Four open questions closed by that table:
        ///
        ///   THE PIVOT is 64. Every clip at velocity 64 lands on the depth-0 value to within
        ///   15 ms whatever the depth, and fitting the pivot freely gives 64 exactly.
        ///   THE SIGN simply negates - read the -25 row backwards against +25 forwards.
        ///   THE DEPTH is linear: 12 gives half the slope of 25, to 2%.
        ///   THE MULTIPLIER is 2, not 1. Depth 25 swings the byte from 20 to 99, not 45 to 95.
        ///   Fitted freely it is 2.05; 1.5 and 2.5 are both far worse.
        ///
        /// The control passes: depth 0 reads 1366 ms at velocity 1 and 1354 ms at 127, so the
        /// enable bit alone does nothing.
        ///
        /// WITH THE SWITCH OFF, EVERY NOTE IS RELEASED AS THOUGH ITS VELOCITY WERE 1. That is
        /// measured, and it is why this parameter looked inert for two runs: run 7 had bit
        /// 0x10 clear on every keygroup, as do all 1908 in the real library, and read the same
        /// release at velocity 1 and 127 for depths of -50, 0 and +50. Those readings are what
        /// this rule gives at velocity 1 - -50 clamps to 99 and takes eleven seconds, +50
        /// clamps to 0 and is instant. Only the two extremes were tried with the switch off,
        /// so "treated as velocity 1" is the simplest thing that fits, not the only one.
        /// </summary>
        public static double VelocityReleaseByte(double stored, double depth, double velocity,
                                                 bool switchOn)
        {
            double vel = switchOn ? Clamp(velocity, 0, 127) : 1.0;
            return Clamp(stored + 2.0 * depth * (vel - 64.0) / 63.0, 0, 99);
        }

        /// <summary>
        /// The VCA attack in seconds, from <see cref="VcaAttackSteps"/>.
        ///
        /// The count is interpolated in the log and then rounded to a whole number, because a
        /// counter can only add whole units - so the answer steps, and steps widely at the top
        /// where n is 4, 3, 2. That is the machine: two settings sharing an n really do give
        /// the same attack to four digits.
        /// </summary>
        public static double VcaAttackSeconds(double stored)
        {
            double v = Clamp(stored, 0, 99);
            int len = VcaAttackSteps.GetLength(0);
            double steps = VcaAttackSteps[len - 1, 1];

            for (int i = 1; i < len; i++)
            {
                if (v > VcaAttackSteps[i, 0]) continue;

                double lo = VcaAttackSteps[i - 1, 1], hi = VcaAttackSteps[i, 1];
                double span = VcaAttackSteps[i, 0] - VcaAttackSteps[i - 1, 0];
                double t = span == 0 ? 0 : (v - VcaAttackSteps[i - 1, 0]) / span;
                steps = lo * Math.Pow(hi / lo, t);
                break;
            }

            double n = Math.Max(2.0, Math.Round(steps));
            double seconds = VcaAttackSpan / n;
            return seconds < VcaAttackGate ? 0.0 : seconds;
        }

        public static double DbToGain(double db) { return Math.Pow(10.0, db / 20.0); }

        /// <summary>log2, which .NET 4 does not have.</summary>
        public static double Log2(double v) { return Math.Log(v) / Math.Log(2.0); }
    }
}
