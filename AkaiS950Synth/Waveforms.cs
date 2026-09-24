using System;

namespace AkaiS950Synth
{
    /// <summary>
    /// Waveforms built out of harmonics, so they can be band-limited - and so they can be
    /// morphed, which is the more interesting half.
    ///
    /// WHY ADDITIVE AND NOT THE OBVIOUS THING
    ///
    /// A sawtooth written as a ramp from -1 to +1 has every harmonic up to infinity, and
    /// everything above half the sample rate folds back down as a discordant whine that no
    /// filter can remove afterwards - it is in the sample. Summing harmonics up to Nyquist
    /// and stopping there gives a sawtooth as far as the rate can represent one, and
    /// silence above it.
    ///
    /// It only has to be done once, at generation, because the machine transposes by
    /// varispeed: playing a sample faster scales its harmonics up with the rate it leaves
    /// at, so a wave that was clean when written stays clean at every key.
    ///
    /// WHY THAT MAKES MORPHING FREE
    ///
    /// If the amplitude of each harmonic is a function of position through the sample
    /// rather than a constant, the timbre moves. A sample that sweeps from a sine to a
    /// sawtooth and back, looped, is one note that never quite settles - and because every
    /// harmonic stays phase-locked to the same fundamental, it does it without a single
    /// click or beat. That is a wavetable, and it costs nothing here but the loop.
    /// </summary>
    internal static class Waveforms
    {
        /// <summary>The amplitude of harmonic n. Zero means absent.</summary>
        public delegate double Harmonic(int n);

        // ------------------------------------------------------------------ the shapes

        public static readonly Harmonic Saw = n => 1.0 / n;

        public static readonly Harmonic Square = n => (n % 2 == 1) ? 1.0 / n : 0.0;

        /// <summary>Odd harmonics falling as the square of n, alternating sign.</summary>
        public static readonly Harmonic Triangle = n =>
            (n % 2 == 1) ? (((n - 1) / 2) % 2 == 0 ? 1.0 : -1.0) / (n * (double)n) : 0.0;

        public static readonly Harmonic Sine = n => n == 1 ? 1.0 : 0.0;

        /// <summary>
        /// A rectangular wave of a given duty. A quarter gives the reedy, hollow tone a
        /// square does not - every fourth harmonic missing entirely.
        /// </summary>
        public static Harmonic Pulse(double duty)
        {
            return n => Math.Sin(Math.PI * n * duty) / n;
        }

        /// <summary>
        /// Drawbars: the fundamental with octaves and a fifth above, which is an organ in
        /// the way a sawtooth is a string - not an imitation, a recipe.
        /// </summary>
        public static readonly Harmonic Organ = n =>
        {
            switch (n)
            {
                case 1:  return 1.00;
                case 2:  return 0.60;
                case 3:  return 0.35;
                case 4:  return 0.45;
                case 6:  return 0.20;
                case 8:  return 0.25;
                case 12: return 0.12;
                case 16: return 0.10;
                default: return 0.0;
            }
        };

        /// <summary>Odd harmonics rolled off hard - a clarinet's hollowness.</summary>
        public static readonly Harmonic Hollow = n =>
            (n % 2 == 1) ? 1.0 / (n * Math.Sqrt(n)) : 0.0;

        /// <summary>Only the harmonics that are powers of two: bell-like, airy.</summary>
        public static readonly Harmonic Glass = n =>
            ((n & (n - 1)) == 0) ? 1.0 / Math.Sqrt(n) : 0.0;

        /// <summary>Everything, barely rolled off. Buzzy and bright - a filter's best friend.</summary>
        public static readonly Harmonic Buzz = n => 1.0 / Math.Sqrt(n);

        // ------------------------------------------------------------------ combining

        /// <summary>A fixed blend of two shapes, for when a sweep is not wanted.</summary>
        public static Harmonic Mix(Harmonic a, Harmonic b, double amount)
        {
            return n => a(n) * (1.0 - amount) + b(n) * amount;
        }

        /// <summary>
        /// A wave written as a list of harmonic levels: the first is the fundamental.
        ///
        /// Which harmonic is which interval is worth keeping in mind, because it is what
        /// makes these musical rather than arbitrary:
        ///
        ///     2  an octave              5  two octaves and a major third
        ///     3  an octave and a fifth  6  two octaves and a fifth
        ///     4  two octaves            7  two octaves and a flat seventh
        ///
        /// The seventh is the interesting one. It is about a third of a semitone flatter
        /// than the seventh a keyboard plays, which is exactly why a wave with it in sounds
        /// reedy and slightly sour where one built on the fifth just sounds bigger.
        /// </summary>
        public static Harmonic Partials(params double[] levels)
        {
            return n => (n >= 1 && n <= levels.Length) ? levels[n - 1] : 0.0;
        }

        /// <summary>
        /// A shape with particular harmonics lifted - a fifth, a seventh, whatever is
        /// wanted - without disturbing the rest of it.
        ///
        /// Added rather than replaced, so a sawtooth keeps being a sawtooth and simply
        /// grows a stronger fifth, the way pulling a drawbar does.
        /// </summary>
        public static Harmonic Lift(Harmonic shape, double amount, params int[] which)
        {
            return n =>
            {
                double v = shape(n);
                foreach (int w in which) if (w == n) { v += amount; break; }
                return v;
            };
        }

        /// <summary>One shape shifted up an octave and added under another - a sub, or a ring.</summary>
        public static Harmonic Plus(Harmonic a, Harmonic b, double level)
        {
            return n => a(n) + b(n) * level;
        }

        // ------------------------------------------------------------------ building

        /// <summary>
        /// How many whole cycles a plain, unmorphing wave gets.
        ///
        /// One would do, and would be smaller, but the loop has to be a whole number of
        /// SAMPLES as well as a whole number of cycles. 40000 / 261.626 is 152.9 samples
        /// for middle C: rounding one cycle to 153 is eleven cents flat, where eight cycles
        /// rounded to 1223 is out by a sixth of one.
        /// </summary>
        public const int PlainCycles = 8;

        public static int WordsFor(int cycles, double rate, double hz)
        {
            return (int)Math.Round(cycles * rate / hz);
        }

        /// <summary>The highest harmonic a rate can hold for a given pitch.</summary>
        public static int HighestHarmonic(double rate, double hz)
        {
            int h = (int)((rate / 2.0) / hz);
            return h < 1 ? 1 : h;
        }

        /// <summary>
        /// Render a wavetable: a list of spectra, swept through across the sample.
        ///
        /// The sweep is a ROUND TRIP. Position runs through the table and wraps back to
        /// where it started, so the last cycle is the first one again and the loop joins
        /// without a seam. A one-way sweep would jump from the far end back to the near one
        /// every time round, which is audible as a tick and a lurch in the tone.
        ///
        /// How fast it sweeps is not a setting: the whole sample is the loop, so one pass
        /// through the table takes cycles/pitch seconds. Higher notes morph faster. That is
        /// a consequence of the machine rather than a decision, and it is characterful -
        /// the same patch is a slow swell low down and a flutter at the top.
        ///
        /// Every partial is a harmonic of the same fundamental and every one is summed at
        /// the phase the analysis found, so a morph cannot beat, drift or click however far
        /// apart its ends are.
        /// </summary>
        public static short[] Render(Spectrum[] table, int cycles, double rate, double hz)
        {
            return Render(table, cycles, rate, hz, null);
        }

        /// <summary>
        /// The same, with the wave shaken by noise as it is built.
        ///
        /// The pitch wander is applied as a phase offset shared by every harmonic and
        /// scaled by its number, which is what keeps a shaken wave a wave: all the partials
        /// move together, as they would if the oscillator itself were unsteady, rather than
        /// each drifting on its own into a chorus.
        ///
        /// Because the wander returns to where it began, the loop still joins - which is
        /// the only reason noise can be used as a modulator in a sample that repeats.
        /// </summary>
        public static short[] Render(Spectrum[] table, int cycles, double rate, double hz, Shake shake)
        {
            if (table == null || table.Length == 0)
                throw new ArgumentException("a wave needs at least one spectrum", "table");

            int words = WordsFor(cycles, rate, hz);
            int highest = HighestHarmonic(rate, hz);

            // Worked out once and shared by every harmonic, rather than per harmonic.
            double[] drift = null, level = null;

            if (shake != null && shake.PitchDepth > 0)
                drift = Noise.Wander(shake.Seed, shake.Points, shake.Colour, words);

            if (shake != null && shake.AmDepth > 0)
                level = Noise.Wander(shake.Seed + 7919, shake.Points, shake.Colour, words);

            var wave = new double[words];
            double baseStep = 2.0 * Math.PI * cycles / words;

            for (int n = 1; n <= highest; n++)
            {
                // Skip a harmonic nothing in the table uses.
                bool used = false;
                foreach (var s in table)
                    if (n <= s.Harmonics && (s.Cos[n] != 0.0 || s.Sin[n] != 0.0)) { used = true; break; }
                if (!used) continue;

                for (int i = 0; i < words; i++)
                {
                    double c, s;
                    At(table, n, i / (double)words, out c, out s);
                    if (c == 0.0 && s == 0.0) continue;

                    double phase = baseStep * i;
                    if (drift != null) phase += shake.PitchDepth * drift[i];

                    double angle = n * phase;
                    wave[i] += c * Math.Cos(angle) + s * Math.Sin(angle);
                }
            }

            if (level != null)
                for (int i = 0; i < words; i++)
                {
                    double g = 1.0 + shake.AmDepth * level[i];
                    wave[i] *= g < 0 ? 0 : g;
                }

            return Quantise(wave);
        }

        /// <summary>A plain wave: one shape, eight cycles, no movement.</summary>
        public static short[] Render(Harmonic shape, double rate, double hz)
        {
            var one = Spectrum.FromShape(shape, HighestHarmonic(rate, hz));
            return Render(new[] { one }, PlainCycles, rate, hz);
        }

        /// <summary>
        /// Harmonic n at position t through the sample, 0 to 1.
        ///
        /// t maps onto the table with wraparound, so three spectra go A to B to C and back
        /// to A across the sample - and t = 1 lands on A again, where t = 0 started.
        /// </summary>
        static void At(Spectrum[] table, int n, double t, out double cos, out double sin)
        {
            if (table.Length == 1)
            {
                cos = n <= table[0].Harmonics ? table[0].Cos[n] : 0;
                sin = n <= table[0].Harmonics ? table[0].Sin[n] : 0;
                return;
            }

            double pos = t * table.Length;
            int at = (int)pos;
            double frac = pos - at;

            Spectrum a = table[at % table.Length];
            Spectrum b = table[(at + 1) % table.Length];

            // Smoothstep rather than a straight line: the ends of each leg flatten out, so
            // the sweep does not audibly change gear as it passes a table entry.
            double m = frac * frac * (3.0 - 2.0 * frac);

            double ac = n <= a.Harmonics ? a.Cos[n] : 0, asn = n <= a.Harmonics ? a.Sin[n] : 0;
            double bc = n <= b.Harmonics ? b.Cos[n] : 0, bsn = n <= b.Harmonics ? b.Sin[n] : 0;

            cos = ac * (1 - m) + bc * m;
            sin = asn * (1 - m) + bsn * m;
        }

        /// <summary>
        /// Time-domain hiss, for the percussive end of a library.
        ///
        /// Looped like everything else, so the loop length is its period: short enough and
        /// it is a buzz rather than a hiss.
        /// </summary>
        public static short[] Hiss(int words, int seed)
        {
            var rng = new Random(seed);
            var wave = new double[words];

            double last = 0;
            for (int i = 0; i < words; i++)
            {
                // A gentle average keeps it from being all top end, which at 12 bits is
                // mostly quantisation noise anyway.
                double white = rng.NextDouble() * 2.0 - 1.0;
                last = 0.65 * white + 0.35 * last;
                wave[i] = last;
            }

            // Cross-fade the join, or the loop ticks once per pass. The harmonic waves need
            // none of this - they end where they began by construction.
            int fade = Math.Min(64, words / 8);
            for (int i = 0; i < fade; i++)
            {
                double t = i / (double)fade;
                wave[i] = wave[i] * t + wave[words - fade + i] * (1 - t);
            }

            return Quantise(wave);
        }

        /// <summary>Scale to fill the 12 bits the machine stores, and round.</summary>
        public static short[] Quantise(double[] wave)
        {
            double peak = 0;
            foreach (double v in wave) peak = Math.Max(peak, Math.Abs(v));
            if (peak <= 0) peak = 1;

            // Headroom left here is headroom thrown away: there are only 4096 steps, and a
            // wave written at half scale spends one of its twelve bits on nothing.
            double scale = 2047.0 / peak;

            var outp = new short[wave.Length];
            for (int i = 0; i < wave.Length; i++)
            {
                int v = (int)Math.Round(wave[i] * scale);
                outp[i] = (short)(v > 2047 ? 2047 : (v < -2048 ? -2048 : v));
            }
            return outp;
        }
    }
}
