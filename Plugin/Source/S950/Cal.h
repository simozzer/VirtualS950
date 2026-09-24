#pragma once

#include <cmath>
#include <algorithm>

/*
 * What the machine was measured to do.
 *
 * A straight port of AkaiS950Engine/Cal.cs, which is itself the same set of numbers as the
 * web version's audio.js. All three are meant to agree to the digit, and there is a check
 * that says so: Tests/ConformanceCheck.cpp holds the values EngineCheck prints for the same
 * inputs, so this port can be held to the numbers the C# is already held to rather than to
 * whether it sounds about right.
 *
 * Every constant came off a recording of a real S950 playing a run written for the purpose.
 * Where a value is an assumption rather than a measurement it says so - those are the ones
 * another afternoon with a recorder would settle.
 */
namespace s950::cal
{
    // ------------------------------------------------------------------------ filter

    /// Measured: 16311 Hz at 44100, and 16232 on an earlier take.
    inline constexpr double MaxRatio = 0.37;

    /// Measured: the cutoff will not close below this.
    inline constexpr double FloorHz = 311.0;

    /// Measured: keyToFilter 50 is 1:1 tracking.
    inline constexpr double KeyFull = 50.0;

    /*
     * Measured: the note at which key tracking adds nothing.
     *
     * NOT 60, which is what this assumed for as long as it had a tracking term at all. The
     * same keygroup played at five keys four octaves apart tracked 0.980 octaves per octave
     * and crossed its own untracked value at note 62.0, with the five untracked controls
     * flat to 0.000 octaves so there was nothing else it could have been.
     *
     * It matters beyond the tracking: a pivot in the wrong place quietly offsets every
     * cutoff ever read from a keygroup with tracking on, which is where the 1878 Hz that
     * used to sit at Curve's stored-50 point came from.
     */
    inline constexpr double KeyPivot = 62.0;

    /// Measured: octaves across the full velocity range.
    inline constexpr double VelOctaves = 8.34;

    /// Measured: the velocity that leaves the filter where it is.
    inline constexpr double VelPivot = 65.0;

    /*
     * Measured: how far the filter envelope moves the cutoff at full amount.
     *
     * Five amounts each way, from bases chosen to leave room in the direction under test,
     * with the envelope held open so the corner stands still and can be read properly
     * rather than traced through a moving sweep:
     *
     *     opening   0.167  0.171  0.168  0.162  octaves per unit  ->  8.37
     *     closing   0.170  0.163  0.164                           ->  8.28
     *
     * Straight to within 0.013 octaves both ways, and the two agree to 1.1% - so a negative
     * amount really does invert the envelope and go exactly as far, which the model had
     * assumed without any evidence for it.
     *
     * It also starts a unit or two off zero rather than at it. That dead zone is real and
     * measured but not modelled here; it is worth less than the 9% this corrects.
     */
    inline constexpr double EnvOctaves = 8.5;

    // --------------------------------------------------------------------- envelopes

    /*
     * Measured points, not a formula - see envSeconds below.
     *
     * This replaces a pair of constants, one measured and one assumed: a shortest time of
     * 1.68 ms taken from a single VCA decay at stored 80, and a 10000:1 span across the range
     * that nothing had ever checked. The span is the part that was wrong. The real one is
     * nearer 1400:1, so the old curve ran half as fast as the machine below stored 70 and
     * nearly twice as fast above stored 85.
     *
     * Nine settings, measured on the filter envelope where a moving cutoff can be watched all
     * the way down a fourteen-second note. Attack, decay and release are three separate
     * readings of this one curve, and where they overlap they agree to 1.07x - so it really
     * is one curve, and it is this one.
     *
     *     stored      50     55     60     65     70     80     85     90     95
     *     measured  0.357  0.418  0.722  0.881  1.404  2.814  4.037  4.117  8.095
     *     old model 0.176  0.280  0.446  0.711  1.131  2.868  4.567  7.272 11.580
     *
     * Measured twice over, because the analysis has a bias of its own - a window averages the
     * sweep passing through it - and that bias is found by putting a RENDER of the model
     * through the same analysis, where the answer is known. The first pass used a model that
     * was out by up to 2x, so its biases were taken at the wrong sweep rates; adopting its
     * table and measuring again moved every point by less than 8%. A third pass against THIS
     * table reads every setting back at 0.95 to 1.04 of it, and the points oscillate rather
     * than drift - so what is left is the measurement's own repeatability and not an error
     * still to be chased.
     *
     * Stored 50 only appears at all on the second pass: under the old curve the render was
     * over inside one analysis window, so there was nothing to take a bias from.
     *
     * The VCA decay confirms it independently, from the same take and a different envelope:
     * measured against a fixed depth it gives 3.96 s at stored 85 against this table's 4.04,
     * and 8.49 s at stored 95 against 8.09.
     *
     * STORED 90 IS THE ODD ONE
     *
     * Everything else sits within a few per cent of a plain exponential through these points.
     * Stored 90 sits 41% off it, and all three of attack, decay and release put it there,
     * agreeing with each other to 1.03x. So it is kept as measured rather than smoothed away
     * - but it is the one point a second take should be asked about first.
     *
     * THE ENDS ARE EXTRAPOLATED, NOT MEASURED
     *
     * Nothing reaches below 50 or above 95: the fast end is over inside one analysis window
     * and the slow end outlasts a note. Both ends continue at the slope fitted across every
     * measured point, which is the best that can honestly be said of them. Extrapolating from
     * the two nearest points instead put stored 0 at 320 ms, which every percussive sample in
     * the library refutes.
     */
    struct EnvPoint { double stored, seconds; };

    inline constexpr EnvPoint EnvTime[] =
    {
        {  0, 0.01040 }, { 50, 0.3565 }, { 55, 0.4184 }, { 60, 0.7224 }, { 65, 0.8806 },
        { 70, 1.4037 }, { 80, 2.8136 }, { 85, 4.0370 }, { 90, 4.1172 }, { 95, 8.0947 },
        { 99, 10.7401 }
    };

    inline constexpr int EnvTimeCount = static_cast<int> (std::size (EnvTime));

    /*
     * The VCA attack against the shared curve. One, within the measurement.
     *
     * It was 1.33, which is what it took to reach the measured attack at stored 70 when the
     * curve underneath was the old one. The curve has moved, and at stored 70 the new curve
     * alone now lands on that same measurement: 1.406 s here against a real machine that
     * reaches full at about 1.5, and a rendered model of the old constants that is
     * indistinguishable from it in the take.
     *
     * It is the weakest number on this page. Stored 85 says the VCA attack reaches full in
     * 2.1 s where this gives 4.04, so the attack's curve is flatter than the shared one and
     * no single multiplier can express that. Two points cannot fix it - fitted as an
     * exponential they put the shortest attack at 312 ms, which is the same absurd answer
     * that two-point extrapolation gives everywhere. It wants a run of its own, with the
     * level driven well clear of the noise so the whole ramp is visible.
     *
     * Until then: 1.0 is right at the one setting two runs agree on, and closer than 1.33 at
     * the other.
     */
    inline constexpr double AttackScale = 1.0;

    /// Measured: 2.25 s against the VCA's 2.86.
    inline constexpr double VcfTimeScale = 0.78;

    /*
     * The filter's release follows this same scale, and there is no cap on it.
     *
     * There was one, briefly, at a second. It came from two takes whose probe measurements
     * had three faults in them - a spectrum routine that averaged each probe with its
     * neighbours, a threshold crossing found by scanning from the noisy end, and probes
     * placed within a twentieth of an octave of where the sweep began. Fixed, and read as a
     * rate across the whole sweep rather than one crossing, the third take says the release
     * keeps growing and the original scale was close all along:
     *
     *     stored        50     60     70     80
     *     measured    0.250  0.526  1.159  2.061
     *     this scale  0.137  0.348  0.882  2.237
     *
     * Within a factor of 1.8 at the fast end and 1.08 at the slow one - and the slow end is
     * where the measurement is most trustworthy, because a slow sweep gives the probes time
     * to be crossed properly. Rendering a known release through the same analysis confirms
     * that: stored 70 came back at 2.27 octaves per second against 2.26 true, while the
     * fastest was out by a quarter.
     *
     * So: no cap, and no separate release curve either. A curve fitted to four points whose
     * fast end is knowingly biased would be worse than the one already here.
     */

    /// Measured: a stored 50 read 19.6 dB down, so it counts decibels.
    inline constexpr double SustainDb = 39.6;

    /// Measured: a stored +20 read 5.7 dB up.
    inline constexpr double LoudnessDbPerUnit = 0.29;

    /// Measured, at velToLoudness 99.
    inline constexpr double VelDbPerStep = 0.63;

    // --------------------------------------------------------------------------- LFO

    /*
     * Measured: eight rungs on a straight line in hertz, r2 0.99998.
     *
     * Linear in the stored byte, where the filter's cutoff is exponential and this was
     * expected to be too. Two takes on different disks agreed to 0.2%.
     */
    inline constexpr double LfoRateHzAtZero  = 1.785;
    inline constexpr double LfoRateHzPerUnit = 0.08917;

    /// Measured: r2 0.9999, so depth 99 swings +-150 cents.
    inline constexpr double LfoDepthCentsPerUnit = 1.527;

    /*
     * Measured: the fade reaches nine tenths of full depth at 7.50/(100-byte) seconds and
     * climbs in a straight line to get there, so the whole ramp is that over 0.9.
     *
     * A fade-in, not a wait - at byte 0 the wobble is at depth within a twentieth of a
     * second, and at 99 it climbs for seven and a half.
     */
    inline constexpr double LfoDelayFadeConstant = 8.33;

    /*
     * Measured: 72.3 cents at the top of the wheel with byte 22 at 99, r2 0.999, and byte
     * 22 = 50 gave 0.509 of that where proportional would be 0.505.
     */
    inline constexpr double LfoWheelCentsAtFull = 72.3;

    // ---------------------------------------------------------------------- mappings

    inline double clamp (double v, double lo, double hi)
    {
        return v < lo ? lo : (v > hi ? hi : v);
    }

    /*
     * Measured points, not a formula. The curve steepens as it climbs, so a straight line in
     * log space is the wrong shape and a table of what was measured is right.
     *
     * Nine points now rather than the original ladder's six. The five in between came from a
     * run built for the purpose, with key tracking, velocity and the envelope all off, so
     * nothing but the stored byte could reach the cutoff.
     *
     * The 50 point is the one that moved, from 1878 to 2210 - a quarter of an octave, in the
     * middle of the range where most of the library sits. The old figure came from a clip
     * with keyToFilter at 50, and tracking turns out not to pivot where the model assumed;
     * see KeyPivot. Two later takes, on two disks, measured 2211 and 2210 with tracking off,
     * the second at five different keys with a spread of 0.000 octaves.
     *
     * A negative frequency means "as far open as it goes" - the reconstruction limit, which
     * moves with the sample rate, so writing a number would wrongly cap a 48 kHz sample
     * below what its own ceiling allows.
     */
    struct CurvePoint { double stored, hz; };

    inline constexpr CurvePoint Curve[] =
    {
        {  0,  311 }, { 20,  311 }, { 30,  544 }, { 40, 1138 }, { 50, 2210 },
        { 60, 4779 }, { 70, 8783 }, { 80,   -1 }, { 99,   -1 }
    };

    inline constexpr int CurveCount = static_cast<int> (std::size (Curve));

    inline double curveAt (int i, double ceiling)
    {
        return Curve[i].hz < 0 ? ceiling : Curve[i].hz;
    }

    /*
     * A stored 0..99 cutoff in hertz, for audio leaving at `rate`.
     *
     * The filter is also the reconstruction filter, so the top of its travel moves with the
     * rate the audio comes out at rather than being a fixed frequency.
     *
     * `stored` is a double rather than the panel's integer because the plugin's cutoff trim
     * slides between the panel's steps: 127 MIDI values across a 198-step range lands between
     * them constantly, and rounding would step the filter audibly. For a whole number this
     * returns exactly what it always did - the value was widened to double on entry anyway -
     * so the conformance numbers are untouched.
     */
    inline double cutoffHz (double stored, double rate)
    {
        const double ceiling = MaxRatio * (rate > 0 ? rate : 48000.0);
        const double floor   = std::min (FloorHz, ceiling);
        const double v       = clamp (stored, 0.0, 99.0);

        double hz = curveAt (CurveCount - 1, ceiling);

        for (int i = 1; i < CurveCount; ++i)
        {
            if (v > Curve[i].stored)
                continue;

            const double lo   = curveAt (i - 1, ceiling);
            const double hi   = curveAt (i,     ceiling);
            const double span = Curve[i].stored - Curve[i - 1].stored;
            const double t    = span == 0 ? 0 : (v - Curve[i - 1].stored) / span;

            hz = lo * std::pow (hi / lo, t);          // straight in log frequency
            break;
        }

        return clamp (hz, floor, ceiling);
    }

    /*
     * A stored 0..99 envelope time, in seconds, read off EnvTime.
     *
     * Straight in log time between the measured points, the same way cutoffHz is straight in
     * log frequency between its own: the quantity is exponential in the stored byte, so a
     * straight line in the log is what "between two measurements" means here.
     *
     * A double for the same reason cutoffHz takes one: the plugin's envelope trims land
     * between the panel's steps, and a whole unit is a fifth of the time or more, which steps
     * audibly. Whole numbers land exactly on the C# engine's answers.
     */
    inline double envSeconds (double stored)
    {
        const double v = clamp (stored, 0.0, 99.0);
        double seconds = EnvTime[EnvTimeCount - 1].seconds;

        for (int i = 1; i < EnvTimeCount; ++i)
        {
            if (v > EnvTime[i].stored)
                continue;

            const double lo   = EnvTime[i - 1].seconds;
            const double hi   = EnvTime[i].seconds;
            const double span = EnvTime[i].stored - EnvTime[i - 1].stored;
            const double t    = span == 0 ? 0 : (v - EnvTime[i - 1].stored) / span;

            seconds = lo * std::pow (hi / lo, t);        // straight in log time
            break;
        }

        return seconds;
    }

    inline double dbToGain (double db) { return std::pow (10.0, db / 20.0); }
}
