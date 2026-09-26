/*
 * Does the port agree with the engine it was ported from?
 *
 * Reference.h holds what the C# computes for a spread of inputs, generated from the working
 * engine by AkaiS950Tests/ReferenceDump.cs. This runs the same inputs through the C++ and
 * insists on the same answers.
 *
 * It exists because of how this port was written: on a machine with no C++ compiler, so not
 * one line of it had ever been run at the point it was committed. Every previous piece of
 * this project was checked by measuring rather than by reading - the LFO against a
 * recording, the engine against the web version, the editor against a rendered screenshot -
 * and a port that was only ever read would be the one thing taken on trust. This is how it
 * stops being taken on trust.
 *
 * Build it with nothing but a compiler:
 *
 *     cl /std:c++17 /EHsc /I..\Source\S950 ConformanceCheck.cpp ..\Source\S950\*.cpp
 *
 * It needs no JUCE, no SDK and no audio device, which is the point - if this fails, nothing
 * built on top of it is worth debugging.
 */

#include "Reference.h"

#include "Cal.h"
#include "Filter.h"
#include "Patch.h"
#include "Engine.h"

#include <cstdio>
#include <cmath>
#include <memory>
#include <vector>
#include <utility>
#include <algorithm>

namespace
{
    int failures = 0;
    int checks   = 0;

    bool close (double a, double b, double tolerance)
    {
        const double diff = std::fabs (a - b);
        if (diff <= tolerance) return true;

        // Relative, for the large ones: 16317 Hz does not need to match to a millionth.
        const double scale = std::max (std::fabs (a), std::fabs (b));
        return scale > 0 && diff / scale <= tolerance;
    }

    void check (bool ok, const char* what, double got, double want)
    {
        ++checks;

        if (ok)
            return;

        ++failures;
        std::printf ("  FAIL %-34s got %.6f, want %.6f\n", what, got, want);
    }

    void same (const char* what, double got, double want, double tolerance = 1e-9)
    {
        check (close (got, want, tolerance), what, got, want);
    }

    // ------------------------------------------------------------------ the mappings

    void checkConstants()
    {
        std::printf ("\n  the measured constants\n");

        same ("MaxRatio",             s950::cal::MaxRatio,             reference::MaxRatio);
        same ("FloorHz",              s950::cal::FloorHz,              reference::FloorHz);
        same ("KeyFull",              s950::cal::KeyFull,              reference::KeyFull);
        same ("VelOctaves",           s950::cal::VelOctaves,           reference::VelOctaves);
        same ("VelPivot",             s950::cal::VelPivot,             reference::VelPivot);
        same ("EnvOctaves",           s950::cal::EnvOctaves,           reference::EnvOctaves);
        same ("VcaAttackSpan",        s950::cal::VcaAttackSpan,        reference::VcaAttackSpan);
        same ("VcfTimeScale",         s950::cal::VcfTimeScale,         reference::VcfTimeScale);
        same ("SustainDb",            s950::cal::SustainDb,            reference::SustainDb);
        same ("LoudnessDbPerUnit",    s950::cal::LoudnessDbPerUnit,    reference::LoudnessDbPerUnit);
        same ("VelDbPerStep",         s950::cal::VelDbPerStep,         reference::VelDbPerStep);
        same ("LfoDepthCentsPerUnit", s950::cal::LfoDepthCentsPerUnit, reference::LfoDepthCentsPerUnit);
        same ("LfoWheelCentsAtFull",  s950::cal::LfoWheelCentsAtFull,  reference::LfoWheelCentsAtFull);
    }

    void checkCutoffs()
    {
        std::printf ("\n  the filter's cutoff, %d points\n",
                     static_cast<int> (std::size (reference::cutoffs)));

        double worst = 0.0;

        for (const auto& c : reference::cutoffs)
        {
            const double got = s950::cal::cutoffHz (c.stored, c.rate);
            worst = std::max (worst, std::fabs (got - c.hz) / std::max (1.0, c.hz));

            char what[64];
            std::snprintf (what, sizeof (what), "cutoff %d at %.0f", c.stored, c.rate);
            same (what, got, c.hz, 1e-9);
        }

        std::printf ("    worst relative difference %.3g\n", worst);
    }

    void checkEnvelopes()
    {
        std::printf ("\n  the envelope times, %d points\n",
                     static_cast<int> (std::size (reference::envTimes)));

        for (const auto& e : reference::envTimes)
        {
            char what[64];
            std::snprintf (what, sizeof (what), "envSeconds %d", e.stored);
            same (what, s950::cal::envSeconds (e.stored), e.seconds, 1e-9);
        }

        std::printf ("\n  the VCA attack counter, all %d settings\n",
                     static_cast<int> (std::size (reference::vcaAttacks)));

        for (const auto& a : reference::vcaAttacks)
        {
            char what[64];
            std::snprintf (what, sizeof (what), "vcaAttackSeconds %d", a.stored);
            same (what, s950::cal::vcaAttackSeconds (a.stored), a.seconds, 1e-9);
        }

        std::printf ("\n  the pitch wheel, %d combinations\n",
                     static_cast<int> (std::size (reference::bends)));

        for (const auto& b : reference::bends)
        {
            char what[80];
            std::snprintf (what, sizeof (what), "bend %d at range %g", b.wheel, b.range);
            same (what, s950::cal::bendRatio (b.wheel, b.range), b.ratio, 1e-12);
        }

        std::printf ("\n  the sustain plateau, %d settings\n",
                     static_cast<int> (std::size (reference::sustains)));

        for (const auto& p : reference::sustains)
        {
            char what[80];
            std::snprintf (what, sizeof (what), "sustain %d", p.stored);
            same (what, s950::cal::sustainDbFor (p.stored), p.db, 1e-9);
        }

        std::printf ("\n  the amplitude decay, %d combinations\n",
                     static_cast<int> (std::size (reference::decays)));

        for (const auto& d : reference::decays)
        {
            char what[96];
            std::snprintf (what, sizeof (what), "decay %d to sustain %d", d.decay, d.sustain);
            same (what,
                  s950::cal::vcaDecaySeconds (d.decay, s950::cal::sustainDbFor (d.sustain)),
                  d.seconds, 1e-9);
        }

        std::printf ("\n  the filter decay, %d combinations\n",
                     static_cast<int> (std::size (reference::vcfDecays)));

        for (const auto& d : reference::vcfDecays)
        {
            char what[96];
            std::snprintf (what, sizeof (what), "VCF decay %d to sustain %d",
                           d.stored, d.sustain);
            same (what,
                  s950::cal::vcfDecaySeconds (d.stored, d.sustain / 99.0),
                  d.seconds, 1e-9);
        }

        std::printf ("\n  the positional crossfade table, %d points\n",
                     static_cast<int> (std::size (reference::xfadePoints)));

        for (const auto& p : reference::xfadePoints)
        {
            char what[80];
            std::snprintf (what, sizeof (what), "crossfade at x = %.3f", p.x);
            same (what, s950::cal::crossfadeDb (p.x), p.db, 1e-9);
        }

        std::printf ("\n  the positional crossfade, %d cases\n",
                     static_cast<int> (std::size (reference::xfades)));

        for (const auto& x : reference::xfades)
        {
            char what[128];
            std::snprintf (what, sizeof (what),
                           "crossfade note %d, keygroup %d of %d (%d-%d)",
                           x.note, x.self, x.count, x.lows[x.self], x.highs[x.self]);
            same (what,
                  s950::cal::crossfadeGain (x.note, x.lows, x.highs, x.count, x.self),
                  x.gain, 1e-12);
        }

        std::printf ("\n  warp, %d combinations\n",
                     static_cast<int> (std::size (reference::warps)));

        for (const auto& w : reference::warps)
        {
            char what[112];
            std::snprintf (what, sizeof (what),
                           "warp v%d d%d t%d at velocity %d, %.2fs",
                           w.velWarp, w.depth, w.time, static_cast<int> (w.velocity), w.t);
            same (what,
                  s950::cal::warpRatio (w.velWarp, w.depth, w.time, w.velocity, w.t),
                  w.ratio, 1e-9);
        }

        std::printf ("\n  velocity to release, %d combinations\n",
                     static_cast<int> (std::size (reference::velReleases)));

        for (const auto& r : reference::velReleases)
        {
            char what[96];
            std::snprintf (what, sizeof (what), "release %d depth %d at velocity %d, %s",
                           r.stored, r.depth, r.velocity, r.on ? "on" : "off");
            same (what,
                  s950::cal::envSeconds (
                      s950::cal::velocityReleaseByte (r.stored, r.depth, r.velocity, r.on)),
                  r.seconds, 1e-9);
        }

        std::printf ("\n  velocity to attack, %d combinations\n",
                     static_cast<int> (std::size (reference::velAttacks)));

        for (const auto& a : reference::velAttacks)
        {
            char what[80];
            std::snprintf (what, sizeof (what), "attack %d depth %d at velocity %d",
                           a.stored, a.depth, a.velocity);
            same (what,
                  s950::cal::vcaAttackSeconds (
                      s950::cal::velocityAttackByte (a.stored, a.depth, a.velocity)),
                  a.seconds, 1e-9);
        }

        /*
         * And that it really is a counter, which the ladder above would not notice on its
         * own: a port that interpolated smoothly between the same measured points would match
         * every third value and be wrong everywhere else. The property is what matters -
         * whole steps, never going backwards, and stopping at 5.4/2.
         */
        int backwards = 0, shared = 0;
        double last = -1.0;

        for (int v = 0; v <= 99; ++v)
        {
            const double t = s950::cal::vcaAttackSeconds (v);

            if (t < last - 1e-9)                          ++backwards;
            else if (std::abs (t - last) < 1e-9)          ++shared;

            if (t > 0.0)                                  // 0 is the gate, not a ramp
            {
                const double n = s950::cal::VcaAttackSpan / t;
                if (std::abs (n - std::round (n)) > 1e-9)
                    same ("a whole number of steps", n, std::round (n), 1e-9);
            }

            last = t;
        }

        // Attack 0 is a hard gate - see cal::VcaAttackGate. Not measured; it is what the
        // bottom of an attack range means.
        same ("attack 0 is a hard gate", s950::cal::vcaAttackSeconds (0), 0.0, 1e-12);
        check (s950::cal::vcaAttackSeconds (5) > 0.001 &&
               s950::cal::vcaAttackSeconds (30) > 0.2,
               "and the gate does not swallow settings that should ramp",
               s950::cal::vcaAttackSeconds (5), 0.001);

        same ("the attack never shortens as the byte rises", backwards, 0);
        check (shared > 40, "and it steps rather than sliding", shared, 40);
        same ("the slowest attack is the span over two",
              s950::cal::vcaAttackSeconds (99), s950::cal::VcaAttackSpan / 2.0, 1e-9);
    }

    void checkLfo()
    {
        std::printf ("\n  the LFO\n");

        for (const auto& r : reference::lfoRates)
        {
            const double got = s950::cal::LfoRateHzAtZero
                             + r.stored * s950::cal::LfoRateHzPerUnit;

            char what[64];
            std::snprintf (what, sizeof (what), "rate %d", r.stored);
            same (what, got, r.hz, 1e-9);
        }

        for (const auto& f : reference::lfoFades)
        {
            const double got = s950::cal::LfoDelayFadeConstant
                             / std::max (1, 100 - f.stored);

            char what[64];
            std::snprintf (what, sizeof (what), "delay fade %d", f.stored);
            same (what, got, f.seconds, 1e-9);
        }
    }

    // ------------------------------------------------------- the engine, end to end

    /// A sawtooth, so there is something with harmonics for the filter to work on.
    std::shared_ptr<s950::Sound> makeSaw (int words, int rate)
    {
        auto s = std::make_shared<s950::Sound>();
        s->name       = "SAW";
        s->sourceRate = rate;
        s->rootPitch  = 60.0;
        s->audio.resize (static_cast<size_t> (words));

        const int period = 100;
        for (int i = 0; i < words; ++i)
            s->audio[static_cast<size_t> (i)] =
                static_cast<float> ((i % period) / static_cast<double> (period) * 2.0 - 1.0);

        s->loops    = true;
        s->loopFrom = 0;
        s->loopTo   = words;
        return s;
    }

    double rms (const std::vector<float>& x)
    {
        double sum = 0.0;
        for (float v : x) sum += static_cast<double> (v) * v;
        return x.empty() ? 0.0 : std::sqrt (sum / x.size());
    }

    void checkEngine()
    {
        std::printf ("\n  the engine, driven end to end\n");

        auto patch = std::make_shared<s950::Patch>();
        patch->name = "TEST";

        s950::KeygroupPatch kg;
        kg.lowKey        = 0;
        kg.highKey       = 127;
        kg.keygroupIndex = 0;
        kg.sound         = makeSaw (48000, 48000);
        kg.vcaSustain    = 99;
        kg.zoneFilter    = 99;
        patch->keygroups.push_back (kg);

        s950::Engine engine (48000.0);
        engine.setPatch (patch);

        std::vector<float> buffer (4800);

        // Silence before anything is asked for.
        engine.render (buffer.data(), static_cast<int> (buffer.size()));
        check (rms (buffer) == 0.0, "silent before any note", rms (buffer), 0.0);

        // A note sounds, and the note-on survives the ring.
        engine.noteOn (60, 100);
        engine.render (buffer.data(), static_cast<int> (buffer.size()));

        const double sounding = rms (buffer);
        check (sounding > 0.01, "a note on sounds", sounding, 0.01);
        check (engine.getActiveVoices() == 1, "one voice", engine.getActiveVoices(), 1);

        // Eight at once, and no more.
        for (int n = 0; n < 12; ++n)
            engine.noteOn (48 + n, 100);

        engine.render (buffer.data(), static_cast<int> (buffer.size()));
        check (engine.getActiveVoices() <= s950::Engine::Polyphony,
               "never more than eight voices", engine.getActiveVoices(), s950::Engine::Polyphony);

        // Everything off, and it goes quiet - release is 0, so one block is enough.
        engine.allNotesOff();
        for (int i = 0; i < 20; ++i)
            engine.render (buffer.data(), static_cast<int> (buffer.size()));

        check (rms (buffer) < 1e-4, "all notes off falls silent", rms (buffer), 0.0);

        // The patch hand-off: the audio thread takes it, the message thread frees it.
        engine.setPatch (nullptr);
        engine.render (buffer.data(), static_cast<int> (buffer.size()));
        engine.collectRetiredPatch();

        engine.noteOn (60, 100);
        engine.render (buffer.data(), static_cast<int> (buffer.size()));
        check (rms (buffer) == 0.0, "no patch, no sound", rms (buffer), 0.0);
    }

    /*
     * A note asked for part way through a block has to start there.
     *
     * The thing this is really guarding is a silent one: applying every event at the top of
     * the block still sounds like a working instrument, just one that quantises everything
     * it is sent to the buffer size. At 512 samples that is 11 ms, which nobody hears as a
     * fault - they hear a drum machine that does not quite swing.
     *
     * So the check is where the sound starts, not whether there is any.
     */
    void checkEventTiming()
    {
        std::printf ("\n  when a note starts inside a block\n");

        auto patch = std::make_shared<s950::Patch>();

        s950::KeygroupPatch kg;
        kg.keygroupIndex = 0;
        kg.sound         = makeSaw (48000, 48000);
        kg.zoneFilter    = 99;
        patch->keygroups.push_back (kg);

        const int block = 512;

        for (const int offset : { 0, 1, 100, 200, 411, 511 })
        {
            s950::Engine engine (48000.0);
            engine.gain.store (0.25f);          // well clear of clipping, so a peak means something
            engine.setPatch (patch);

            /*
             * Two blocks, not one.
             *
             * A note has an attack even when its attack byte is zero - the shortest the
             * machine does is 1.68 ms, which is 80 samples at 48 kHz - and it starts at
             * silence and climbs. So a note placed at sample 511 of a 512-sample block has
             * exactly one sample in which to be audible, and in that one sample it is not:
             * its gain is still about a hundredth of the way up and the filter has not moved
             * off zero. Rendering the block after it as well is what makes the question
             * answerable at all, and costs nothing.
             */
            std::vector<float> buffer (static_cast<size_t> (block) * 2);
            engine.noteOn (60, 127, offset);
            engine.render (buffer.data(), block);
            engine.render (buffer.data() + block, block);

            // The first sample that is not silence. Before the note there is nothing at all
            // in the buffer - it is memset and no voice is running - so this cannot be early.
            int first = -1;
            for (int i = 0; i < static_cast<int> (buffer.size()); ++i)
            {
                if (std::fabs (static_cast<double> (buffer[static_cast<size_t> (i)])) > 1e-7)
                {
                    first = i;
                    break;
                }
            }

            char what[64];
            std::snprintf (what, sizeof (what), "note at sample %d", offset);

            /*
             * Within a control block of where it was asked for.
             *
             * Not to the sample: a voice recomputes its modulators every 32 samples and the
             * envelope climbs from nothing, so the first few samples of a note can be too
             * quiet to see. Landing inside one control block is the difference that matters
             * - the failure being guarded against is a whole buffer out.
             */
            const bool ok = first >= offset && first < offset + 64;
            check (ok, what, first, offset);
        }

        // Two notes in one block, each joining where it belongs.
        {
            s950::Engine engine (48000.0);
            engine.gain.store (0.25f);
            engine.setPatch (patch);

            std::vector<float> buffer (static_cast<size_t> (block));
            engine.noteOn (60, 127, 100);
            engine.noteOn (67, 127, 300);
            engine.render (buffer.data(), block);

            check (engine.getActiveVoices() == 2, "two notes in one block",
                   engine.getActiveVoices(), 2);

            /*
             * Louder where both are sounding than where only one is.
             *
             * Measured as energy rather than as a peak: at full gain two sawtooths sum past
             * +-1 and both halves clip to exactly 1.0, which compares equal and says
             * nothing. That is what the first version of this check did.
             */
            double one = 0.0, both = 0.0;
            for (int i = 150; i < 290; ++i) one  += std::pow (buffer[(size_t) i], 2.0);
            for (int i = 350; i < 490; ++i) both += std::pow (buffer[(size_t) i], 2.0);

            one  = std::sqrt (one  / 140.0);
            both = std::sqrt (both / 140.0);

            check (both > one * 1.1, "the second note joins part way through", both, one);
        }
    }

    void checkFilter()
    {
        std::printf ("\n  the filter\n");

        s950::Butterworth f;
        f.setCutoff (1000.0, 48000.0);

        // A steady input settles to a steady output: the sections are normalised to unity
        // at DC, and if a Q or a coefficient is wrong this is where it shows first.
        double y = 0.0;
        for (int i = 0; i < 20000; ++i)
            y = f.process (1.0);

        same ("unity gain at DC", y, 1.0, 1e-6);

        // Well above the cutoff, very little should come through.
        f.reset();
        f.setCutoff (500.0, 48000.0);

        double peak = 0.0;
        for (int i = 0; i < 48000; ++i)
        {
            const double x = std::sin (2.0 * 3.14159265358979323846 * 8000.0 * i / 48000.0);
            const double out = f.process (x);
            if (i > 24000) peak = std::max (peak, std::fabs (out));
        }

        check (peak < 0.02, "8 kHz is stopped by a 500 Hz cutoff", peak, 0.02);
    }

    // --------------------------------------------------------------- the player's trims

    /*
     * The two controls that sit on top of a programme: cutoff and envelope amount, applied
     * to every keygroup at once.
     *
     * Nothing else here covers them, because everything else here is the port against the
     * C# and the trims exist only in the plugin. They are checked by what they do to the
     * sound rather than by reading the numbers back: a sawtooth is full of harmonics, so
     * closing the filter takes energy out of it and the level falls. That is the property
     * worth holding - a trim that moved a variable without moving the audio would pass any
     * check that asked the variable.
     */
    void checkTrims()
    {
        std::printf ("\n  the player's filter trims\n");

        auto patchWith = [] (int zoneFilter, int amount, bool vcfWritten)
        {
            auto patch = std::make_shared<s950::Patch>();
            patch->name = "TRIM";

            s950::KeygroupPatch kg;
            kg.lowKey        = 0;
            kg.highKey       = 127;
            kg.keygroupIndex = 0;
            kg.sound         = makeSaw (48000, 48000);
            kg.vcaSustain    = 99;
            kg.zoneFilter    = zoneFilter;
            kg.vcfAmount     = amount;
            kg.vcfWritten    = vcfWritten;
            kg.vcfDecay      = 80;
            kg.vcfSustain    = 0;
            patch->keygroups.push_back (kg);
            return patch;
        };

        // One note held from the start, rendered once, at whatever trims are set.
        auto levelAt = [&] (const s950::PatchPtr& patch, double cutoffTrim, double amountTrim)
        {
            s950::Engine engine (48000.0);
            engine.setPatch (patch);
            engine.trims.cutoff.store (static_cast<float> (cutoffTrim));
            engine.trims.amount.store (static_cast<float> (amountTrim));

            std::vector<float> buffer (2400);
            engine.noteOn (60, 100);
            engine.render (buffer.data(), static_cast<int> (buffer.size()));
            return rms (buffer);
        };

        const auto mid = patchWith (60, 0, true);

        const double flat  = levelAt (mid,   0.0, 0.0);
        const double shut  = levelAt (mid, -40.0, 0.0);
        const double open  = levelAt (mid, +39.0, 0.0);

        check (shut < flat * 0.9, "closing the cutoff trim takes energy out", shut, flat);
        check (open > flat,       "opening it puts energy back",              open, flat);

        // Zero has to mean "exactly as the disk says", or the instrument lies about its own
        // programmes the moment the control exists.
        const double again = levelAt (mid, 0.0, 0.0);
        same ("a zero trim changes nothing", again, flat, 1e-12);

        // The trim reaches a note that is ALREADY sounding, which is the whole difference
        // between a control and a setting that waits for the next key.
        {
            s950::Engine engine (48000.0);
            engine.setPatch (mid);

            std::vector<float> buffer (2400);
            engine.noteOn (60, 100);
            engine.render (buffer.data(), static_cast<int> (buffer.size()));
            const double before = rms (buffer);

            engine.trims.cutoff.store (-40.0f);
            engine.render (buffer.data(), static_cast<int> (buffer.size()));
            const double after = rms (buffer);

            check (after < before * 0.9, "a held note follows the trim", after, before);
        }

        /*
         * Amount deepens an envelope the programme has. The envelope here starts open and
         * falls, so a positive amount puts more through in the first moments.
         *
         * Measured from a nearly shut base rather than the mid one, because a keygroup that
         * is already open has barely any room left to be opened further - at stored 60 the
         * same trim moved the level 3%, which is a true result and a poor test.
         */
        const auto low = patchWith (30, 0, true);
        const double amountFlat = levelAt (low, 0.0,  0.0);
        const double amountUp   = levelAt (low, 0.0, 40.0);
        check (amountUp > amountFlat * 1.05, "the amount trim deepens the envelope",
               amountUp, amountFlat);

        /*
         * An S900 programme left the four VCF bytes blank, so it has no filter envelope of
         * its own - and the player can still build one.
         *
         * Two things have to hold at once. Untouched, such a keygroup must sound exactly as
         * it always did, because a programme with no envelope moves its cutoff by nothing.
         * Touched, it must respond, or a whole class of programmes has four controls that do
         * nothing and no way to tell why. The engine gives it a flat envelope - no attack,
         * no decay, full sustain - which satisfies both: an amount of zero is no movement
         * whatever shape it is applied to.
         */
        const auto s900 = patchWith (60, 0, false);

        const double blankFlat  = levelAt (s900, 0.0,  0.0);
        const double writtenSame = levelAt (patchWith (60, 0, true), 0.0, 0.0);
        same ("an unwritten envelope untouched is an envelope that does nothing",
              blankFlat, writtenSame, 1e-12);

        const double blankUp = levelAt (s900, 0.0, 40.0);
        check (blankUp > blankFlat * 1.02, "and the amount trim can still build one",
               blankUp, blankFlat);

        /*
         * A negative amount inverts the envelope: it pulls the cutoff BELOW the keygroup's
         * own setting instead of above it, so the same envelope that would have opened the
         * filter closes it. That is what the signed byte means, and three keygroups in the
         * library use it - all at -50.
         *
         * Measured from a bright base, because the cutoff floor is 311 Hz and inverting a
         * keygroup that already sits near it has nowhere to go. The library's own three do
         * exactly that and move a tenth of an octave.
         */
        const auto bright = patchWith (80, 0, true);
        const double upright  = levelAt (bright, 0.0,   0.0);
        const double inverted = levelAt (bright, 0.0, -40.0);
        check (inverted < upright * 0.9, "a negative amount inverts the envelope",
               inverted, upright);

        /*
         * The control's range is the panel's, -50..+50, so the engine has to hold the sum
         * inside it rather than trusting the caller. A keygroup at +50 taken down by a
         * further 50 lands at 0 - no envelope - and not at some depth off the bottom of what
         * the machine can express.
         */
        /*
         * The control reads -50..+50, the panel's own range, so it has to be able to cancel
         * whatever the keygroup brought - a programme at +15 taken down by 15 is a programme
         * with no filter envelope.
         *
         * From stored 20, because that is where the difference is worth measuring: with the
         * base at the floor the envelope has the whole range above it, and the saw's energy
         * moves properly. From the middle of the range the same change moves a few per cent,
         * because a sawtooth keeps most of its power in the first few harmonics.
         *
         * There is deliberately no check that the sum is clamped at +-50. It is clamped, but
         * the clamp cannot be heard: at 8.3 octaves a full amount runs the cutoff into a stop
         * from any base, so clamped and unclamped land in the same place and a check would
         * pass whether the code did it or not.
         */
        const auto deep = patchWith (20, 15, true);
        const double asWas  = levelAt (deep, 0.0,   0.0);
        const double zeroed = levelAt (deep, 0.0, -15.0);

        check (zeroed < asWas * 0.9, "the trim can cancel a keygroup's own amount",
               zeroed, asWas);

        /*
         * One Filter knob, reaching BOTH samples of a velocity-switched keygroup.
         *
         * A keygroup holds up to two zones - a soft sample and a hard one - each with its own
         * filter value, and 54% of the library's two-zone keygroups set them apart. They are
         * separate voice entries by the time the engine sees them, so the question is whether
         * one control reaches both, and whether it leaves the gap between them alone.
         *
         * It must also change nothing at rest. A control that has not been touched has no
         * business altering a programme.
         */
        {
            auto split = std::make_shared<s950::Patch>();

            s950::KeygroupPatch soft;
            soft.lowKey = 0; soft.highKey = 127; soft.keygroupIndex = 0;
            soft.sound = makeSaw (48000, 48000);
            soft.vcaSustain = 99;
            soft.velocityFrom = 0; soft.velocityTo = 63;
            soft.zoneFilter = 30;

            auto hard = soft;
            hard.velocityFrom = 64; hard.velocityTo = 127;
            hard.zoneFilter = 45;              // the hard sample is brighter, as they often are

            split->keygroups.push_back (soft);
            split->keygroups.push_back (hard);

            auto atVelocity = [&] (int velocity, double filterTrim)
            {
                s950::Engine engine (48000.0);
                engine.setPatch (split);
                engine.trims.cutoff.store (static_cast<float> (filterTrim));

                std::vector<float> buffer (2400);
                engine.noteOn (60, velocity);
                engine.render (buffer.data(), static_cast<int> (buffer.size()));
                return rms (buffer);
            };

            const double softFlat = atVelocity (40, 0.0);
            const double hardFlat = atVelocity (100, 0.0);

            check (hardFlat > softFlat * 1.05,
                   "the hard sample keeps its own brighter filter", hardFlat, softFlat);

            const double softOpen = atVelocity (40, 25.0);
            const double hardOpen = atVelocity (100, 25.0);

            check (softOpen > softFlat * 1.05, "one Filter knob opens the soft sample",
                   softOpen, softFlat);
            check (hardOpen > hardFlat * 1.05, "and the hard one too", hardOpen, hardFlat);

            // untouched, it has to leave both exactly as the disk describes them
            same ("at rest it changes the soft sample not at all",
                  atVelocity (40, 0.0), softFlat, 1e-12);
            same ("nor the hard one", atVelocity (100, 0.0), hardFlat, 1e-12);
        }

        // The stops still hold: a trim cannot open the filter past the reconstruction limit.
        const double wideOpen = levelAt (patchWith (99, 0, true), 0.0, 0.0);
        const double shoved   = levelAt (patchWith (99, 0, true), 99.0, 0.0);
        same ("the trim cannot open past the stop", shoved, wideOpen, 1e-12);
    }

    // ------------------------------------------------------------ the envelope trims

    /*
     * The four VCA stages and the three VCF ones, as offsets on the programme's own values.
     *
     * Checked by what they do to the shape of a note rather than by reading a variable back,
     * for the same reason as the filter trims: a control that moved a number without moving
     * the audio would pass any test that asked the number.
     */
    void checkEnvelopeTrims()
    {
        std::printf ("\n  the envelope trims\n");

        // A flat gate - instant attack, no decay, full sustain - so anything that changes
        // the shape is the trim and not the programme.
        auto flatGate = []
        {
            auto patch = std::make_shared<s950::Patch>();
            s950::KeygroupPatch kg;
            kg.lowKey = 0; kg.highKey = 127; kg.keygroupIndex = 0;
            kg.sound      = makeSaw (48000, 48000);
            kg.vcaAttack  = 0;
            kg.vcaDecay   = 0;
            kg.vcaSustain = 99;
            kg.vcaRelease = 0;
            kg.zoneFilter = 99;
            patch->keygroups.push_back (kg);
            return patch;
        };

        const auto patch = flatGate();

        /// Render one note, with one trim set, and say how loud it was.
        auto withTrim = [&] (std::atomic<float> s950::Engine::AtomicTrims::* field,
                             double value, int samples)
        {
            s950::Engine engine (48000.0);
            engine.setPatch (patch);
            (engine.trims.*field).store (static_cast<float> (value));

            std::vector<float> buffer ((size_t) samples);
            engine.noteOn (60, 100);
            engine.render (buffer.data(), samples);
            return rms (buffer);
        };

        using AT = s950::Engine::AtomicTrims;

        // Attack: a long one means the first tenth of a second is quiet.
        const double fast = withTrim (&AT::vcaAttack,  0.0, 4800);
        const double slow = withTrim (&AT::vcaAttack, 70.0, 4800);
        check (slow < fast * 0.5, "an attack trim slows the attack", slow, fast);

        // Sustain: pulling it down takes level out of a gate that otherwise holds flat.
        const double full  = withTrim (&AT::vcaSustain,   0.0, 4800);
        const double lower = withTrim (&AT::vcaSustain, -50.0, 4800);
        check (lower < full * 0.9, "a sustain trim lowers the level", lower, full);

        // Decay: with sustain pulled down, a slower decay takes longer to get there, so
        // more level survives the window.
        {
            s950::Engine quick (48000.0), slowly (48000.0);
            std::vector<float> a (4800), b (4800);

            quick.setPatch (patch);
            quick.trims.vcaSustain.store (-99.0f);
            quick.trims.vcaDecay.store (0.0f);
            quick.noteOn (60, 100);
            quick.render (a.data(), 4800);

            slowly.setPatch (patch);
            slowly.trims.vcaSustain.store (-99.0f);
            slowly.trims.vcaDecay.store (70.0f);
            slowly.noteOn (60, 100);
            slowly.render (b.data(), 4800);

            check (rms (b) > rms (a) * 1.1, "a decay trim slows the decay", rms (b), rms (a));
        }

        // Release: a longer one rings on after the key is let go.
        {
            auto ringing = [&] (double trim)
            {
                s950::Engine engine (48000.0);
                engine.setPatch (patch);
                engine.trims.vcaRelease.store (static_cast<float> (trim));

                std::vector<float> buffer (2400);
                engine.noteOn (60, 100);
                engine.render (buffer.data(), 2400);      // held
                engine.noteOff (60);
                engine.render (buffer.data(), 2400);      // let go
                return rms (buffer);
            };

            const double curt = ringing (0.0);
            const double rings = ringing (70.0);
            check (rings > curt * 2.0, "a release trim rings on", rings, curt);
        }

        // Nothing moved means nothing changed, which is the promise the whole set makes.
        const double asWritten = withTrim (&AT::vcaAttack, 0.0, 4800);
        same ("every trim at zero plays the disk", asWritten, fast, 1e-12);

        /*
         * The VCF stages reach the filter, not the level. A slower filter decay holds the
         * sweep open for longer, so more of the sawtooth's harmonics survive the window.
         */
        {
            auto swept = [&] (double decayTrim)
            {
                auto p = std::make_shared<s950::Patch>();
                s950::KeygroupPatch kg;
                kg.lowKey = 0; kg.highKey = 127; kg.keygroupIndex = 0;
                kg.sound      = makeSaw (48000, 48000);
                kg.vcaSustain = 99;
                kg.zoneFilter = 20;          // nearly shut, so the envelope has somewhere to go
                kg.vcfAmount  = 40;
                kg.vcfWritten = true;
                kg.vcfDecay   = 10;
                kg.vcfSustain = 0;
                p->keygroups.push_back (kg);

                s950::Engine engine (48000.0);
                engine.setPatch (p);
                engine.trims.vcfDecay.store (static_cast<float> (decayTrim));

                std::vector<float> buffer (9600);
                engine.noteOn (60, 100);
                engine.render (buffer.data(), 9600);
                return rms (buffer);
            };

            const double brief = swept (0.0);
            const double held  = swept (60.0);
            check (held > brief * 1.1, "a VCF decay trim holds the sweep open", held, brief);
        }

        /*
         * The filter's release. Let go of a note with the filter envelope wide open and the
         * cutoff should fall back to the keygroup's own, so what rings out is duller than
         * what was held - which it could not be while there was no release stage at all.
         */
        {
            auto ringing = [&] (int releaseByte)
            {
                auto p = std::make_shared<s950::Patch>();
                s950::KeygroupPatch kg;
                kg.lowKey = 0; kg.highKey = 127; kg.keygroupIndex = 0;
                kg.sound      = makeSaw (48000, 48000);
                kg.vcaSustain = 99;
                kg.vcaRelease = 60;          // long enough to hear the filter close
                kg.zoneFilter = 20;          // nearly shut, so the envelope has somewhere to go
                kg.vcfAmount  = 50;
                kg.vcfWritten = true;
                kg.vcfDecay   = 99;          // still open when the key comes up
                kg.vcfSustain = 99;
                kg.vcfRelease = releaseByte;
                p->keygroups.push_back (kg);

                s950::Engine engine (48000.0);
                engine.setPatch (p);

                std::vector<float> buffer (4800);
                engine.noteOn (60, 100);
                engine.render (buffer.data(), 4800);       // held, bright
                const double bright = rms (buffer);

                engine.noteOff (60);
                engine.render (buffer.data(), 4800);       // let go
                return std::pair<double, double> (bright, rms (buffer));
            };

            const auto instant = ringing (0);
            const auto slow    = ringing (70);

            // With no release time the envelope drops at once, so the tail is darker than
            // one that closes slowly and keeps some brightness on the way down.
            check (slow.second > instant.second * 1.05,
                   "a filter release closes over its own time", slow.second, instant.second);

            // And it does close: the tail is not simply the held sound fading.
            check (instant.second < instant.first,
                   "the filter falls back when the key is let go",
                   instant.second, instant.first);
        }


        /*
         * Letting go can only take energy away.
         *
         * A stored release of 0 drops the cutoff five and a half octaves in about a
         * millisecond, and a sixth-order cascade retuned that hard - keeping the state the
         * old coefficients left it with - can ring instead of closing. Stepped in isolation
         * this filter peaks at 17.85 against a signal of 1.0 when retuned only every 64
         * samples, though the engine at its own 32 has not been made to do it.
         *
         * So this holds the invariant rather than a number: whatever the cascade does on the
         * way down, the note must not get LOUDER for having been let go. It is honest about
         * what it is - a guard that would catch a bad regression, not a reproduction of the
         * measurement above, which was taken on the filter directly and on the web build.
         */
        {
            auto p = std::make_shared<s950::Patch>();
            s950::KeygroupPatch kg;
            kg.lowKey = 0; kg.highKey = 127; kg.keygroupIndex = 0;
            kg.sound      = makeSaw (48000, 48000);
            kg.vcaSustain = 99;
            kg.vcaRelease = 70;
            kg.zoneFilter = 20;          // nearly shut, so the fall is as far as it goes
            kg.vcfAmount  = 50;
            kg.vcfWritten = true;
            kg.vcfDecay   = 99;          // still wide open when the key comes up
            kg.vcfSustain = 99;
            kg.vcfRelease = 0;           // and shut in a millisecond
            p->keygroups.push_back (kg);

            s950::Engine engine (48000.0);
            engine.setPatch (p);

            /*
             * Quietly, so the ring has room to show. The master stage hard-clamps at +-1, and
             * a sawtooth wide open already sits there - so at full gain this measures the
             * clamp and reports 1.0 whether the filter rang or not.
             */
            engine.gain.store (0.25f);

            std::vector<float> buffer (4800);
            engine.noteOn (60, 100);
            engine.render (buffer.data(), 4800);

            double held = 0.0;
            for (float v : buffer) held = std::max (held, (double) std::fabs (v));

            engine.noteOff (60);
            std::fill (buffer.begin(), buffer.end(), 0.0f);
            engine.render (buffer.data(), 4800);

            double letGo = 0.0;
            for (float v : buffer) letGo = std::max (letGo, (double) std::fabs (v));

            // Letting go can only take energy away. Anything louder than the held note is the
            // filter ringing, and at a 32-sample block this was three and a half times it.
            check (letGo < held * 1.2, "closing fast does not make the filter ring",
                   letGo, held);
        }
    }

    // ----------------------------------------------------------------- the LFO trims

    /*
     * Rate, depth and delay, checked by what they do to the audio.
     *
     * The LFO bends pitch, and pitch is awkward to assert directly - so each is read as how
     * far the rendered note has been pushed away from the same note with the LFO still. That
     * distance is zero when nothing is asked for, grows with depth, and - this is what makes
     * rate and delay separable - arrives at a different TIME for each of them.
     */
    void checkLfoTrims()
    {
        std::printf ("\n  the LFO trims\n");

        auto patch = []
        {
            auto p = std::make_shared<s950::Patch>();
            s950::KeygroupPatch kg;
            kg.lowKey = 0; kg.highKey = 127; kg.keygroupIndex = 0;
            kg.sound      = makeSaw (48000, 48000);
            kg.vcaAttack  = 0;
            kg.vcaDecay   = 0;
            kg.vcaSustain = 99;
            kg.vcaRelease = 0;
            kg.zoneFilter = 99;
            kg.lfoRate = 0; kg.lfoDepth = 0; kg.lfoDelay = 0;
            kg.lfoDesync = true;
            p->keygroups.push_back (kg);
            return p;
        }();

        auto render = [&] (double rate, double depth, double delay, int samples)
        {
            s950::Engine engine (48000.0);
            engine.setPatch (patch);
            engine.trims.lfoRate .store (static_cast<float> (rate));
            engine.trims.lfoDepth.store (static_cast<float> (depth));
            engine.trims.lfoDelay.store (static_cast<float> (delay));

            std::vector<float> buffer ((size_t) samples);
            engine.noteOn (60, 100);
            engine.render (buffer.data(), samples);
            return buffer;
        };

        /// How far apart two renders are, in the same units as rms.
        auto apart = [] (const std::vector<float>& a, const std::vector<float>& b)
        {
            const size_t n = std::min (a.size(), b.size());
            double sum = 0.0;
            for (size_t i = 0; i < n; ++i) { const double d = a[i] - b[i]; sum += d * d; }
            return n ? std::sqrt (sum / (double) n) : 0.0;
        };

        const auto still = render (0.0, 0.0, 0.0, 24000);

        // Nothing asked for is nothing done, which is the promise every trim here makes.
        same ("every LFO trim at zero plays the disk",
              apart (render (0.0, 0.0, 0.0, 24000), still), 0.0, 1e-12);

        const auto wobbling = render (0.0, 99.0, 0.0, 24000);
        check (apart (wobbling, still) > rms (still) * 0.2,
               "a depth trim bends the pitch", apart (wobbling, still), rms (still) * 0.2);

        /*
         * Rate, read as how soon the bending starts.
         *
         * A faster LFO has travelled further from the middle of its swing by any given early
         * moment, so over the first twentieth of a second it has pushed the note further off.
         * Comparing the whole note instead would say only that two different wobbles differ.
         */
        const std::vector<float> stillEarly (still.begin(), still.begin() + 2400);
        const auto slowEarly = render ( 0.0, 99.0, 0.0, 2400);
        const auto fastEarly = render (99.0, 99.0, 0.0, 2400);

        check (apart (fastEarly, stillEarly) > apart (slowEarly, stillEarly) * 1.5,
               "a rate trim makes it wobble sooner",
               apart (fastEarly, stillEarly), apart (slowEarly, stillEarly) * 1.5);

        /*
         * Delay, which is a fade-in rather than a wait: at the top of its range the depth
         * climbs for seven and a half seconds, so the start of the note is barely bent at all
         * where the same note without it is bent hard.
         */
        const auto faded = render (99.0, 99.0, 99.0, 2400);
        check (apart (faded, stillEarly) < apart (fastEarly, stillEarly) * 0.5,
               "a delay trim holds the wobble off the start",
               apart (faded, stillEarly), apart (fastEarly, stillEarly) * 0.5);
    }

    // ------------------------------------------------------- the velocity sensitivities

    /*
     * How hard you play, and how much of that reaches the filter and the level.
     *
     * Both are depths on something the keygroup already sets, so both are read the same way:
     * play the same programme at two velocities and see how far apart the results are, with
     * the trim down and then up. A depth that works widens that gap.
     *
     * They differ in WHEN they take effect, and the test holds that too. The filter trim
     * reaches a note already sounding, because a filter control you cannot play with is not
     * a control; the loudness trim does not, because how hard a key was struck is settled
     * when it goes down, and no knob reaches back to change it.
     */
    void checkVelocityTrims()
    {
        std::printf ("\n  the velocity sensitivities\n");

        auto patch = []
        {
            auto p = std::make_shared<s950::Patch>();
            s950::KeygroupPatch kg;
            kg.lowKey = 0; kg.highKey = 127; kg.keygroupIndex = 0;
            kg.sound      = makeSaw (48000, 48000);
            kg.vcaAttack  = 0;
            kg.vcaDecay   = 0;
            kg.vcaSustain = 99;
            kg.vcaRelease = 0;
            kg.zoneFilter = 60;          // room to move either way
            kg.velToFilter   = 0;
            kg.velToLoudness = 0;
            p->keygroups.push_back (kg);
            return p;
        }();

        auto at = [&] (std::atomic<float> s950::Engine::AtomicTrims::* field,
                       double trim, int velocity)
        {
            s950::Engine engine (48000.0);
            engine.setPatch (patch);
            (engine.trims.*field).store (static_cast<float> (trim));

            std::vector<float> buffer (4800);
            engine.noteOn (60, velocity);
            engine.render (buffer.data(), 4800);
            return rms (buffer);
        };

        using AT = s950::Engine::AtomicTrims;

        // Loudness: with no depth a soft note is as loud as a hard one; with depth it is not.
        const double flatSoft = at (&AT::velToLoudness,  0.0,  20);
        const double flatHard = at (&AT::velToLoudness,  0.0, 127);
        const double deepSoft = at (&AT::velToLoudness, 99.0,  20);
        const double deepHard = at (&AT::velToLoudness, 99.0, 127);

        same ("no loudness depth makes velocity irrelevant", flatSoft, flatHard, 1e-9);
        check (deepSoft < deepHard * 0.5,
               "a loudness depth makes a soft note softer", deepSoft, deepHard * 0.5);

        /*
         * Filter: measured through the level of a sawtooth after filtering, which falls as
         * the cutoff closes. Velocity 20 is well below the measured pivot of 65, so depth
         * takes the cutoff DOWN there, where at 127 it takes it up.
         */
        const double dullSoft = at (&AT::velToFilter, 99.0,  20);
        const double brightHard = at (&AT::velToFilter, 99.0, 127);
        check (dullSoft < brightHard * 0.9,
               "a filter depth makes a soft note darker", dullSoft, brightHard * 0.9);

        same ("no filter depth makes velocity irrelevant",
              at (&AT::velToFilter, 0.0, 20), at (&AT::velToFilter, 0.0, 127), 1e-9);

        /*
         * And the difference in when they land. The same note is started untrimmed, then the
         * trim is turned up under it: the filter follows, the loudness does not.
         */
        auto turnedUpMidNote = [&] (std::atomic<float> s950::Engine::AtomicTrims::* field)
        {
            s950::Engine engine (48000.0);
            engine.setPatch (patch);

            std::vector<float> before (2400), after (2400);
            engine.noteOn (60, 20);
            engine.render (before.data(), 2400);
            if (field != nullptr) (engine.trims.*field).store (99.0f);
            engine.render (after.data(), 2400);

            return rms (before) > 0.0 ? rms (after) / rms (before) : 0.0;
        };

        /*
         * Against a run where nothing is touched, not against 1.
         *
         * The second half of a note is not the same stretch of sawtooth as the first, so
         * even with no control moved the two halves differ slightly in level - here by a
         * quarter of a per cent. Asking for exactly 1 was asking the sample to repeat.
         */
        const double untouched     = turnedUpMidNote (nullptr);
        const double filterMoved   = turnedUpMidNote (&AT::velToFilter);
        const double loudnessMoved = turnedUpMidNote (&AT::velToLoudness);

        check (std::abs (filterMoved - untouched) > 0.1,
               "the filter depth reaches a note already sounding", filterMoved, untouched);
        same ("the loudness depth waits for the next note", loudnessMoved, untouched, 1e-9);
    }

    /*
     * The filter envelope runs on its own clock.
     *
     * It used to be driven by the amplitude envelope's stage timer, which is reset to zero
     * every time that envelope changes stage - so a programme with a slow amplitude decay
     * restarted its filter sweep when the decay ended. Two renders that differ only in the
     * amplitude decay must give the same filter movement, and that is what this holds.
     */
    void checkFilterClock()
    {
        std::printf ("\n  the filter envelope's own clock\n");

        auto sweep = [] (int vcaDecay)
        {
            auto p = std::make_shared<s950::Patch>();
            s950::KeygroupPatch kg;
            kg.lowKey = 0; kg.highKey = 127; kg.keygroupIndex = 0;
            kg.sound      = makeSaw (48000, 48000);
            kg.vcaDecay   = vcaDecay;
            kg.vcaSustain = 99;          // no level change, so only the filter can differ
            kg.zoneFilter = 20;
            kg.vcfAmount  = 50;
            kg.vcfWritten = true;
            kg.vcfDecay   = 80;
            kg.vcfSustain = 0;
            p->keygroups.push_back (kg);

            s950::Engine engine (48000.0);
            engine.setPatch (p);

            std::vector<float> buffer (48000);
            engine.noteOn (60, 100);
            engine.render (buffer.data(), 48000);
            return rms (buffer);
        };

        // A sustain of 99 means the amplitude decay changes nothing about the level; only a
        // filter that restarted with it could make these two differ.
        const double quickDecay = sweep (0);
        const double slowDecay  = sweep (70);

        check (std::fabs (quickDecay - slowDecay) < quickDecay * 0.02,
               "the amplitude decay does not restart the filter", slowDecay, quickDecay);
    }
}

int main()
{
    std::printf ("\n  the C++ port against the engine it came from\n");

    checkConstants();
    checkCutoffs();
    checkEnvelopes();
    checkLfo();
    checkFilter();
    checkEngine();
    checkEventTiming();
    checkTrims();
    checkEnvelopeTrims();
    checkLfoTrims();
    checkVelocityTrims();
    checkFilterClock();

    std::printf ("\n  %d checks, %d failed\n\n", checks, failures);
    return failures == 0 ? 0 : 1;
}
