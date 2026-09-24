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

    /// Measured: octaves across the full velocity range.
    inline constexpr double VelOctaves = 8.34;

    /// Measured: the velocity that leaves the filter where it is.
    inline constexpr double VelPivot = 65.0;

    /// Measured: from the slope, both amounts agreeing.
    inline constexpr double EnvOctaves = 7.6;

    // --------------------------------------------------------------------- envelopes

    /// Measured: VCA decay 80 at 2.86 s.
    inline constexpr double EnvMinMs = 1.68;

    /// Assumed: the same 10000:1 span, moved with the bottom.
    inline constexpr double EnvMaxMs = 16800.0;

    /// Measured: attack 70 between 1.39 s and 1.66 s.
    inline constexpr double AttackScale = 1.33;

    /// Measured: 2.25 s against the VCA's 2.86.
    inline constexpr double VcfTimeScale = 0.78;

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
     * Measured points, not a formula.
     *
     * Fitting one exponential through the ladder's two unsaturated points put a stored 50 at
     * 2355 Hz; the key-tracking clip, which uses exactly that setting, measured 1878. The
     * curve steepens as it climbs, so a straight line in log space is the wrong shape and a
     * table of what was actually measured is right.
     *
     * A negative frequency means "as far open as it goes" - the reconstruction limit, which
     * moves with the sample rate, so writing a number would wrongly cap a 48 kHz sample
     * below what its own ceiling allows.
     */
    struct CurvePoint { double stored, hz; };

    inline constexpr CurvePoint Curve[] =
    {
        {  0,  311 }, { 20,  311 }, { 40, 1139 }, { 50, 1878 },
        { 60, 4808 }, { 80,   -1 }, { 99,   -1 }
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
     * A stored 0..99 envelope time, in seconds.
     *
     * A double for the same reason cutoffHz takes one: the plugin's envelope trims land
     * between the panel's steps, and this curve is exponential - a whole unit is about a
     * tenth of the time either way, which steps audibly. Truncating to int was also a silent
     * narrowing that the compiler was right to complain about. Whole numbers are unchanged.
     */
    inline double envSeconds (double stored)
    {
        const double v = clamp (stored, 0.0, 99.0) / 99.0;
        return (EnvMinMs * std::pow (EnvMaxMs / EnvMinMs, v)) / 1000.0;
    }

    inline double dbToGain (double db) { return std::pow (10.0, db / 20.0); }
}
