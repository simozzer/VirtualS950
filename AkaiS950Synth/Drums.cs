using System;
using System.Collections.Generic;

namespace AkaiS950Synth
{
    /// <summary>
    /// A drum kit, drawn rather than recorded.
    ///
    /// Everything else here is a wavetable: a few cycles of a waveform, looped, with the
    /// machine's envelope and filter making it into a sound. A drum cannot be built that
    /// way. Its envelope is not a setting, it is the sound - a kick IS a pitch falling as
    /// it decays - so these are one-shots with the whole shape baked into the words, and
    /// the keygroups around them do almost nothing.
    ///
    /// THE RECIPES
    ///
    /// They are the ones the analogue machines used, because they fit in a page of
    /// arithmetic and need no recordings:
    ///
    ///   KICK     a sine whose pitch falls from a couple of hundred hertz to the low
    ///            forties in a few milliseconds, decaying as it goes, driven into a
    ///            saturator hard enough to grow the harmonics that make a kick audible
    ///            on a speaker too small to reproduce its fundamental.
    ///   SNARE    two detuned sines for the shell, band-passed noise for the snares on a
    ///            decay of its own, and a short broadband crack across the front.
    ///   TOM      the kick's sweep, shallower and higher, with a trace of noise for skin.
    ///   CLAP     one noise source through a band-pass, gated three times ten
    ///            milliseconds apart and then left ajar for the room. Gating one source
    ///            is what gives a clap its flam; three mixed sources would be a chorus.
    ///   HAT      six square waves at the TR-808's inharmonic ratios with the bottom
    ///            filtered off. Closed, pedal and open differ only in the envelope.
    ///   CYMBAL   the same bank at wider ratios, with noise filling the gaps between the
    ///            partials and seconds rather than milliseconds to fall.
    ///   CLAVE    one sine at 2.5 kHz, gone in thirty milliseconds.
    ///   RIMSHOT  a 1.7 kHz ping over a short 420 Hz thump.
    ///
    /// WHY THE SQUARES ARE SUMMED RATHER THAN DRAWN
    ///
    /// The same reason the wavetables are, and the reason matters more here: a 205 Hz
    /// square drawn literally at 32 kHz folds a hash back down into the audible range
    /// that no filter afterwards can remove, and a hat is nothing BUT high harmonics, so
    /// there is nowhere for it to hide. Summed to just under Nyquist, there is none.
    ///
    /// WHY EVERY DRUM IS FADED
    ///
    /// Every one of these envelopes is still plainly audible when the sample runs out. A
    /// kick with a half-second decay needs three seconds to fall to nothing, and nobody
    /// is spending 120 KB of an S950 on the last 40 dB of a kick drum. So the decay is
    /// finished by hand: the last quarter of each drum is taken down by a raised cosine,
    /// which leaves the slope zero at both ends and simply reads as the sound decaying a
    /// little faster than it was. A short fade instead would not be a fade - it would be
    /// a cut with the click filed off, and on a tail still at -10 dB it is audible as
    /// one.
    /// </summary>
    internal static class Drums
    {
        /// <summary>0.2 ms, enough to start from silence rather than from a step.</summary>
        const double FadeIn = 0.0002;

        /// <summary>The fraction of each drum the closing fade covers. See the note above.</summary>
        const double Tail = 0.25;

        /// <summary>
        /// The ratios the TR-808's metal oscillator bank ran at.
        ///
        /// Inharmonic by design: no two partials share a fundamental, so the bank rings
        /// rather than sounds a pitch. Six square waves at these ratios and a high-pass
        /// filter is the whole of that machine's hi-hat and cymbal section.
        /// </summary>
        static readonly double[] Metal808 = { 1, 1.4471, 1.6170, 1.9265, 2.5028, 2.6637 };

        /// <summary>
        /// A second set, stretched further apart, for the ride and the crash.
        ///
        /// The 808's own cymbal is the same six oscillators as its hats, which is why
        /// they sound like a family. Spreading the ratios pulls the cymbals out of it.
        /// </summary>
        static readonly double[] MetalWide = { 1, 1.3733, 1.8371, 2.2471, 2.9531, 3.4813, 4.2129 };

        // ------------------------------------------------------------------- the kit

        /// <summary>One drum: where it sits, how long it runs, and how to draw it.</summary>
        internal sealed class Drum
        {
            public string Name;
            public int Key;                 // the note it answers to, C3 = 60
            public int Rate;                // its own sample rate
            public double Seconds;
            public int Loudness;            // the keygroup trim that balances the kit
            public int Vel;                 // velocity -> loudness
            public string Why;

            /// <summary>Words, sample rate, generator -> the wave, before quantising.</summary>
            public Func<int, int, Random, double[]> Voice;
        }

        /// <summary>
        /// The kit, laid out close to General MIDI so a sequencer's drum editor names the
        /// right rows.
        ///
        /// Each drum carries its own sample rate. The kick has nothing above 2 kHz in it
        /// and the crash has everything, and paying 32 kHz for the kick would spend 60 KB
        /// of the machine's memory on an empty top octave.
        /// </summary>
        public static List<Drum> Kit()
        {
            return new List<Drum>
            {
                new Drum { Name = "SNARE 3", Key = 33, Rate = 24000, Seconds = 0.20,
                           Loudness = -4, Vel = 40, Why = "piccolo crack",
                           Voice = (n, r, g) => Snare(n, r, g,
                               hz1: 262, hz2: 391, bend: 0.50, bodyTau: 0.055, mix: 0.60,
                               noiseHz: 2700, noiseQ: 1.0, noiseTau: 0.065, crack: 0.68) },

                new Drum { Name = "KICK 3", Key = 34, Rate = 20000, Seconds = 0.25,
                           Loudness = -1, Vel = 25, Why = "tight, short",
                           Voice = (n, r, g) => Kick(n, r, g,
                               startHz: 240, endHz: 62, pitchTau: 0.007, ampTau: 0.11,
                               click: 0.28, clickTau: 0.0015, drive: 3.6) },

                new Drum { Name = "KICK 2", Key = 35, Rate = 20000, Seconds = 0.90,
                           Loudness = 0, Vel = 20, Why = "deep, long tail",
                           Voice = (n, r, g) => Kick(n, r, g,
                               startHz: 118, endHz: 42, pitchTau: 0.034, ampTau: 0.60,
                               click: 0.08, clickTau: 0.0020, drive: 2.0) },

                new Drum { Name = "KICK 1", Key = 36, Rate = 20000, Seconds = 0.45,
                           Loudness = 0, Vel = 25, Why = "the main kick: punchy",
                           Voice = (n, r, g) => Kick(n, r, g,
                               startHz: 190, endHz: 49, pitchTau: 0.014, ampTau: 0.24,
                               click: 0.16, clickTau: 0.0018, drive: 2.8) },

                new Drum { Name = "RIMSHOT", Key = 37, Rate = 24000, Seconds = 0.09,
                           Loudness = -6, Vel = 45, Why = "ping over a thump",
                           Voice = (n, r, g) => Rim(n, r, g,
                               pingHz: 1700, pingTau: 0.012, thumpHz: 420,
                               thumpTau: 0.020, click: 0.30) },

                new Drum { Name = "SNARE 1", Key = 38, Rate = 24000, Seconds = 0.32,
                           Loudness = -2, Vel = 40, Why = "the main snare: crisp",
                           Voice = (n, r, g) => Snare(n, r, g,
                               hz1: 188, hz2: 278, bend: 0.35, bodyTau: 0.105, mix: 0.48,
                               noiseHz: 1900, noiseQ: 0.9, noiseTau: 0.120, crack: 0.58) },

                new Drum { Name = "CLAP", Key = 39, Rate = 24000, Seconds = 0.42,
                           Loudness = -4, Vel = 45, Why = "three hands and a room",
                           Voice = (n, r, g) => Clap(n, r, g,
                               tapTau: 0.0035, tailFrom: 0.031, tailTau: 0.110,
                               tailLevel: 0.62, hz: 1100, q: 1.1) },

                new Drum { Name = "SNARE 2", Key = 40, Rate = 24000, Seconds = 0.42,
                           Loudness = -2, Vel = 40, Why = "fat, slower",
                           Voice = (n, r, g) => Snare(n, r, g,
                               hz1: 152, hz2: 229, bend: 0.30, bodyTau: 0.190, mix: 0.42,
                               noiseHz: 1400, noiseQ: 0.8, noiseTau: 0.170, crack: 0.46) },

                new Drum { Name = "TOM LO", Key = 41, Rate = 20000, Seconds = 0.60,
                           Loudness = -3, Vel = 35, Why = "floor tom",
                           Voice = (n, r, g) => Tom(n, r, g,
                               startHz: 140, endHz: 88, pitchTau: 0.070, ampTau: 0.34,
                               noise: 0.09) },

                new Drum { Name = "HAT CLOSED", Key = 42, Rate = 32000, Seconds = 0.08,
                           Loudness = -9, Vel = 55, Why = "closed hat",
                           Voice = (n, r, g) => Metal(n, r, g,
                               bas: 205.3, ratios: Metal808, hp: 5600, tau: 0.026,
                               snap: 1.0, snapTau: 0.0025) },

                new Drum { Name = "TOM MID", Key = 43, Rate = 20000, Seconds = 0.50,
                           Loudness = -3, Vel = 35, Why = "mid tom",
                           Voice = (n, r, g) => Tom(n, r, g,
                               startHz: 198, endHz: 128, pitchTau: 0.060, ampTau: 0.28,
                               noise: 0.09) },

                new Drum { Name = "HAT PEDAL", Key = 44, Rate = 32000, Seconds = 0.14,
                           Loudness = -10, Vel = 55, Why = "pedal hat: longer, duller",
                           Voice = (n, r, g) => Metal(n, r, g,
                               bas: 205.3, ratios: Metal808, hp: 5000, tau: 0.050,
                               snap: 0.9, snapTau: 0.0030) },

                new Drum { Name = "TOM HI", Key = 45, Rate = 20000, Seconds = 0.42,
                           Loudness = -3, Vel = 35, Why = "high tom",
                           Voice = (n, r, g) => Tom(n, r, g,
                               startHz: 278, endHz: 182, pitchTau: 0.050, ampTau: 0.22,
                               noise: 0.09) },

                new Drum { Name = "HAT OPEN", Key = 46, Rate = 32000, Seconds = 0.75,
                           Loudness = -10, Vel = 50, Why = "the same six, held open",
                           Voice = (n, r, g) => Metal(n, r, g,
                               bas: 205.3, ratios: Metal808, hp: 5400, tau: 0.340,
                               snap: 1.0, snapTau: 0.0080) },

                new Drum { Name = "CRASH", Key = 49, Rate = 32000, Seconds = 2.60,
                           Loudness = -8, Vel = 50, Why = "wide ratios, noise, a slow fall",
                           Voice = (n, r, g) => Metal(n, r, g,
                               bas: 148.0, ratios: MetalWide, hp: 3800, tau: 1.10,
                               snap: 0.7, snapTau: 0.0120,
                               wash: 0.55, washHp: 5000, washTau: 1.40, swell: 0.006) },

                new Drum { Name = "RIDE", Key = 51, Rate = 32000, Seconds = 1.80,
                           Loudness = -10, Vel = 50, Why = "the stick over a steady ring",
                           Voice = (n, r, g) => Metal(n, r, g,
                               bas: 166.0, ratios: MetalWide, hp: 3000, tau: 0.900,
                               snap: 1.3, snapTau: 0.0050,
                               wash: 0.16, washHp: 6000, washTau: 0.500,
                               ping: 0.45, pingHz: 3520, pingTau: 0.060) },

                new Drum { Name = "RIDE BELL", Key = 53, Rate = 32000, Seconds = 1.40,
                           Loudness = -11, Vel = 45, Why = "fewer partials, more of each",
                           Voice = (n, r, g) => Metal(n, r, g,
                               bas: 262.0, ratios: new double[] { 1, 1.4771, 2.1834, 3.0129 },
                               hp: 2400, tau: 0.620, snap: 0.9, snapTau: 0.0040,
                               ping: 0.60, pingHz: 2620, pingTau: 0.220) },

                new Drum { Name = "CLAVE", Key = 75, Rate = 24000, Seconds = 0.13,
                           Loudness = -11, Vel = 40, Why = "one sine, gone",
                           Voice = (n, r, g) => Clave(n, r, hz: 2500, tau: 0.030) }
            };
        }

        // ----------------------------------------------------------------- the voices

        /// <summary>
        /// A sine falling in pitch as it decays, then driven into a saturator.
        ///
        /// The saturation is what makes it solid rather than merely low. A pure sine at
        /// 49 Hz is nearly inaudible on anything small; softly clipped, it grows a family
        /// of harmonics at 98, 147, 196 Hz which a small speaker can reproduce and which
        /// the ear hears as the missing fundamental. That is the same trick every kick
        /// drum on every record uses, and here it costs one call to tanh.
        ///
        /// The click is added AFTER the saturator, not before. Driven, it would be
        /// flattened along with everything else - and the click is the one part of a kick
        /// that wants to stay sharp.
        /// </summary>
        static double[] Kick(int n, int rate, Random rng,
                             double startHz, double endHz, double pitchTau, double ampTau,
                             double click, double clickTau, double drive)
        {
            var wave = Sweep(n, rate, startHz, endHz, pitchTau, ampTau);
            Saturate(wave, drive);

            if (click > 0)
            {
                var hiss = Norm(HighPass(White(n, rng), 2500, rate));
                for (int i = 0; i < n; i++)
                    wave[i] += click * hiss[i] * Math.Exp(-(i / (double)rate) / clickTau);
            }
            return wave;
        }

        static double[] Tom(int n, int rate, Random rng,
                            double startHz, double endHz, double pitchTau, double ampTau,
                            double noise)
        {
            var wave = Sweep(n, rate, startHz, endHz, pitchTau, ampTau);
            var skin = Norm(LowPass(HighPass(White(n, rng), 300, rate), 4000, rate));

            for (int i = 0; i < n; i++)
            {
                double t = i / (double)rate;
                wave[i] = (1 - noise) * wave[i]
                        + noise * skin[i] * Math.Exp(-t / (ampTau * 0.35));
            }
            return wave;
        }

        /// <summary>
        /// The shell and the snares, on their own decays, with a crack across the front.
        ///
        /// The two sines are detuned rather than harmonic, and both bend upward for the
        /// first twenty milliseconds: a struck head is tightest when it is hit and
        /// relaxes after.
        ///
        /// The band-pass is what keeps the snares from being airy. Wide-open noise is
        /// hiss, and hiss has no edge to it however loud it is; taken down to a band
        /// around a kilohertz or two it becomes a rattle, with the top left for the
        /// crack - a couple of milliseconds of broadband noise that is most of what the
        /// ear calls a sharp snare.
        /// </summary>
        static double[] Snare(int n, int rate, Random rng,
                              double hz1, double hz2, double bend, double bodyTau,
                              double mix, double noiseHz, double noiseQ, double noiseTau,
                              double crack)
        {
            var body = new double[n];
            double p1 = 0, p2 = 0;

            for (int i = 0; i < n; i++)
            {
                double t = i / (double)rate;
                double up = 1 + bend * Math.Exp(-t / 0.020);
                p1 += 2 * Math.PI * hz1 * up / rate;
                p2 += 2 * Math.PI * hz2 * up / rate;
                body[i] = (Math.Sin(p1) + 0.7 * Math.Sin(p2)) * Math.Exp(-t / bodyTau);
            }
            Norm(body);

            var white = White(n, rng);
            var rattle = Norm(HighPass(BandPass(white, noiseHz, noiseQ, rate), 400, rate));
            var edge = Norm(HighPass(white, 3500, rate));

            var wave = new double[n];
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)rate;
                wave[i] = (1 - mix) * body[i]
                        + mix * rattle[i] * Math.Exp(-t / noiseTau)
                        + crack * edge[i] * Math.Exp(-t / 0.0025);
            }
            return wave;
        }

        /// <summary>
        /// One noise source, gated several times. See the note at the top of the file.
        /// </summary>
        static double[] Clap(int n, int rate, Random rng,
                             double tapTau, double tailFrom, double tailTau,
                             double tailLevel, double hz, double q)
        {
            double[] taps = { 0, 0.010, 0.021 };
            var hiss = Norm(HighPass(BandPass(White(n, rng), hz, q, rate), 500, rate));
            var wave = new double[n];

            for (int i = 0; i < n; i++)
            {
                double t = i / (double)rate, gate = 0;

                foreach (double at in taps)
                    if (t >= at) gate += Math.Exp(-(t - at) / tapTau);

                if (t >= tailFrom) gate += tailLevel * Math.Exp(-(t - tailFrom) / tailTau);

                wave[i] = hiss[i] * gate;
            }
            return wave;
        }

        /// <summary>
        /// Hats, ride, crash and bell: one bank of squares under different envelopes.
        ///
        /// <paramref name="snap"/> adds a short burst on top of the decay, so the stick
        /// is heard hitting the metal rather than the metal simply starting to ring -
        /// which is the difference between a sharp hat and an airy one, and matters more
        /// than the decay does. <paramref name="wash"/> fills the gaps between the
        /// partials with high noise, which is most of what separates a cymbal from a very
        /// long hat. <paramref name="ping"/> adds one strong partial, for a bell.
        ///
        /// The high-pass is a pair of one-poles rather than anything steeper. Taking the
        /// bottom off hard leaves only the top of the bank, and a hat with no body to it
        /// is exactly the airy, hissing thing this is trying not to be.
        /// </summary>
        static double[] Metal(int n, int rate, Random rng,
                              double bas, double[] ratios, double hp, double tau,
                              double snap, double snapTau,
                              double wash = 0, double washHp = 0, double washTau = 1,
                              double ping = 0, double pingHz = 0, double pingTau = 1,
                              double swell = 0)
        {
            var bank = new double[n];
            foreach (double r in ratios) Square(bank, n, rate, bas * r, rng);
            bank = Norm(HighPass(HighPass(bank, hp, rate), hp, rate));

            double[] air = null;
            if (wash > 0) air = Norm(HighPass(HighPass(White(n, rng), washHp, rate), washHp, rate));

            var wave = new double[n];
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)rate;

                // A crash takes a moment to open; a hat does not.
                double opening = swell > 0 ? 1 - Math.Exp(-t / swell) : 1;
                double env = Math.Exp(-t / tau) + snap * Math.Exp(-t / snapTau);

                wave[i] = bank[i] * env * opening;

                if (air != null) wave[i] += wash * air[i] * Math.Exp(-t / washTau) * opening;
                if (ping > 0)
                    wave[i] += ping * Math.Sin(2 * Math.PI * pingHz * i / rate)
                                    * Math.Exp(-t / pingTau);
            }
            return wave;
        }

        static double[] Rim(int n, int rate, Random rng,
                            double pingHz, double pingTau, double thumpHz, double thumpTau,
                            double click)
        {
            var wave = new double[n];
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)rate;
                wave[i] = Math.Sin(2 * Math.PI * pingHz * i / rate) * Math.Exp(-t / pingTau)
                        + 0.8 * Math.Sin(2 * Math.PI * thumpHz * i / rate) * Math.Exp(-t / thumpTau);
            }

            var hiss = Norm(HighPass(White(n, rng), 3000, rate));
            for (int i = 0; i < n; i++)
                wave[i] += click * hiss[i] * Math.Exp(-(i / (double)rate) / 0.0012);

            return wave;
        }

        static double[] Clave(int n, int rate, double hz, double tau)
        {
            var wave = new double[n];
            for (int i = 0; i < n; i++)
                wave[i] = Math.Sin(2 * Math.PI * hz * i / rate)
                        * Math.Exp(-(i / (double)rate) / tau);
            return wave;
        }

        // ---------------------------------------------------------------- the workshop

        /// <summary>A sine whose pitch falls exponentially while its level does too.</summary>
        static double[] Sweep(int n, int rate, double startHz, double endHz,
                              double pitchTau, double ampTau)
        {
            var wave = new double[n];
            double phase = 0;

            for (int i = 0; i < n; i++)
            {
                double t = i / (double)rate;
                double hz = endHz + (startHz - endHz) * Math.Exp(-t / pitchTau);
                phase += 2 * Math.PI * hz / rate;
                wave[i] = Math.Sin(phase) * Math.Exp(-t / ampTau);
            }
            return wave;
        }

        /// <summary>
        /// A square wave summed from its odd harmonics, stopping below Nyquist.
        ///
        /// Each partial gets a phase from the drum's own generator. All of them in phase
        /// would put the whole bank's energy into one spike at the start, which sounds
        /// like a click in front of the cymbal rather than the cymbal being struck.
        /// </summary>
        static void Square(double[] wave, int n, int rate, double hz, Random rng)
        {
            double limit = rate * 0.46;

            for (int k = 1; k * hz < limit; k += 2)
            {
                double amp = 1.0 / k;
                double step = 2 * Math.PI * k * hz / rate;
                double phase = Math.PI * (2 * rng.NextDouble() - 1);

                for (int i = 0; i < n; i++) wave[i] += amp * Math.Sin(step * i + phase);
            }
        }

        static double[] White(int n, Random rng)
        {
            var wave = new double[n];
            for (int i = 0; i < n; i++) wave[i] = 2 * rng.NextDouble() - 1;
            return wave;
        }

        /// <summary>One-pole high pass. Cheap, gentle, and stable at any cutoff.</summary>
        static double[] HighPass(double[] x, double hz, int rate)
        {
            double a = Math.Exp(-2 * Math.PI * hz / rate);
            var outp = new double[x.Length];
            double px = 0, py = 0;

            for (int i = 0; i < x.Length; i++)
            {
                py = a * (py + x[i] - px);
                px = x[i];
                outp[i] = py;
            }
            return outp;
        }

        static double[] LowPass(double[] x, double hz, int rate)
        {
            double a = 1 - Math.Exp(-2 * Math.PI * hz / rate);
            var outp = new double[x.Length];
            double y = 0;

            for (int i = 0; i < x.Length; i++)
            {
                y += a * (x[i] - y);
                outp[i] = y;
            }
            return outp;
        }

        /// <summary>
        /// A two-pole band pass - the Chamberlin state variable - for the snares and the
        /// clap.
        ///
        /// It goes unstable as the cutoff approaches a third of the sample rate, which is
        /// why nothing above about 4 kHz is filtered with it and the hats use cascaded
        /// one-poles instead. The cutoff is clamped rather than trusted.
        /// </summary>
        static double[] BandPass(double[] x, double hz, double q, int rate)
        {
            double f = 2 * Math.Sin(Math.PI * Math.Min(hz, rate / 6.0) / rate);
            double damp = Math.Min(2, 1 / Math.Max(0.25, q));

            var outp = new double[x.Length];
            double low = 0, band = 0;

            for (int i = 0; i < x.Length; i++)
            {
                double high = x[i] - low - damp * band;
                band += f * high;
                low += f * band;
                outp[i] = band;
            }
            return outp;
        }

        /// <summary>Peak to one, so a mix coefficient means what it says.</summary>
        static double[] Norm(double[] x)
        {
            double peak = 0;
            foreach (double v in x) peak = Math.Max(peak, Math.Abs(v));
            if (peak > 0) for (int i = 0; i < x.Length; i++) x[i] /= peak;
            return x;
        }

        static void Saturate(double[] x, double drive)
        {
            double k = Math.Tanh(drive);
            for (int i = 0; i < x.Length; i++) x[i] = Math.Tanh(drive * x[i]) / k;
        }

        // ------------------------------------------------------------------ rendering

        /// <summary>
        /// One drum, as the 12-bit words the disk carries.
        ///
        /// The generator is seeded from the drum's name, so a drum made twice is the same
        /// drum twice however many other drums were made in between - which is what makes
        /// a whole disk reproducible, and what lets it be checked against a rebuild.
        /// </summary>
        public static short[] Render(Drum drum)
        {
            int n = (int)Math.Round(drum.Seconds * drum.Rate) & ~1;   // whole words, in pairs
            var rng = new Random(Seed(drum.Name));
            var wave = drum.Voice(n, drum.Rate, rng);

            int rise = Math.Max(1, (int)Math.Round(FadeIn * drum.Rate));
            int fall = Math.Max(2, (int)Math.Round(Tail * n));

            for (int i = 0; i < rise && i < n; i++) wave[i] *= i / (double)rise;
            for (int i = 0; i < fall; i++)
                wave[n - fall + i] *= 0.5 * (1 + Math.Cos(Math.PI * i / (fall - 1.0)));

            return Waveforms.Quantise(wave);
        }

        /// <summary>A name, as a number. Any spread will do; this one is FNV-1a.</summary>
        static int Seed(string name)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in name) { h ^= c; h *= 16777619; }
                return (int)(h & 0x7FFFFFFF);
            }
        }

        // -------------------------------------------------------- waves and programmes

        /// <summary>The kit as waves the disk builder can write.</summary>
        public static List<Patches.Wave> Waves()
        {
            var outp = new List<Patches.Wave>();

            foreach (var drum in Kit())
            {
                var d = drum;
                outp.Add(new Patches.Wave
                {
                    Name = d.Name,
                    Rate = d.Rate,

                    // Its root is the key it sits on, so it plays at the rate it was made
                    // at. The programmes below set constant pitch as well, and the two
                    // agree rather than one relying on the other.
                    Root = d.Key,
                    Oneshot = () => Render(d)
                });
            }

            return outp;
        }

        /// <summary>
        /// The programmes: the kit, a dynamic version of it, and one tuned tom.
        ///
        /// The second and third are free. A programme is a hundred-odd bytes where these
        /// samples are hundreds of kilobytes, so a kit already paid for can carry as many
        /// arrangements of itself as the directory has room for.
        /// </summary>
        public static List<Patch> Programmes()
        {
            var kit = Kit();

            //
            // One keygroup per drum, one key wide.
            //
            // Constant pitch and one-shot: a drum plays at the rate it was made at, all
            // the way to the end, however short the gate a sequencer gives it. Without
            // one-shot a sixteenth-note gate would cut the crash off after 80 ms.
            //
            var plain = new Patch
            {
                Name = "DRUMKIT",
                A = 0, D = 99, S = 99, R = 20,
                Filter = 99, KeyToFilter = 0, VelToFilter = 0,
                OneShot = true, ConstantPitch = true,
                Layers = Rows(kit, 1.0)
            };

            //
            // The same kit played dynamically: velocity moves the level much further and
            // takes the filter down with it, so a soft hit is quieter AND duller, which
            // is what a soft hit actually is.
            //
            var dynamic = new Patch
            {
                Name = "DRUM VEL",
                A = 0, D = 99, S = 99, R = 20,
                Filter = 88, KeyToFilter = 0, VelToFilter = 40,
                OneShot = true, ConstantPitch = true,
                Layers = Rows(kit, 2.0)
            };

            //
            // And one tom across the keyboard, with constant pitch OFF so it tunes.
            //
            // Nothing else on this disk is played as a pitch, and the mid tom is the one
            // sample here with enough body to stand being transposed a couple of octaves
            // either way. Its root is 43, so it plays as it was made at G1.
            //
            var toms = new Patch
            {
                Name = "TOM TUNE",
                A = 0, D = 99, S = 99, R = 25,
                Filter = 99, KeyToFilter = 50, VelToFilter = 0,
                OneShot = true, ConstantPitch = false,
                Layers = new[]
                {
                    new Layer { Sample = "TOM MID", LowKey = 24, HighKey = 72,
                                VelToLoudness = 40 }
                }
            };

            return new List<Patch> { plain, dynamic, toms };
        }

        /// <summary>A keygroup per drum, each one key wide.</summary>
        static Layer[] Rows(List<Drum> kit, double velScale)
        {
            var outp = new List<Layer>();

            foreach (var d in kit)
            {
                int vel = (int)Math.Round(d.Vel * velScale);
                if (vel > 99) vel = 99;

                outp.Add(new Layer
                {
                    Sample = d.Name,
                    LowKey = d.Key,
                    HighKey = d.Key,
                    Loudness = d.Loudness,
                    VelToLoudness = vel
                });
            }

            return outp.ToArray();
        }
    }
}
