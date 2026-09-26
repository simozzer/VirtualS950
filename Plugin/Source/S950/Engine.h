#pragma once

#include "Voice.h"

#include <atomic>
#include <vector>

namespace s950
{
    /*
     * The instrument: eight voices, a patch, and a buffer to fill.
     *
     * Nothing in here knows what is driving it. In the C# an editor drives it from a piano
     * keyboard and a MIDI port; here a host drives it from its note events; the checks drive
     * it from a loop and render to a file. That separation is the one decision that made
     * this port a port rather than a rewrite.
     *
     * THREADS
     *
     * render() runs on the audio thread and must never allocate, never lock and never block.
     * Notes arrive from somewhere else, so they go into a small lock-free ring and are picked
     * up at the top of the next render(). A lock here would be a dropout.
     */
    class Engine
    {
    public:
        /// What the machine has. Voices past this steal the oldest.
        static constexpr int Polyphony = 8;

        explicit Engine (double sampleRate);

        double getSampleRate() const { return sampleRate; }

        /// The master trim, as a plain gain. Eight voices at once can clip.
        std::atomic<float> gain { 0.7f };

        /*
         * The player's trims, applied to every keygroup of whatever is loaded. See Trims.
         *
         * One atomic each rather than one lock around the set: they are written by the
         * message thread and read by the audio thread, and nothing here needs them to change
         * together. A control moved half a block before another is exactly what a pair of
         * hands does anyway.
         */
        struct AtomicTrims
        {
            std::atomic<float> cutoff { 0.0f }, amount { 0.0f };
            std::atomic<float> vcaAttack { 0.0f }, vcaDecay { 0.0f },
                               vcaSustain { 0.0f }, vcaRelease { 0.0f };
            std::atomic<float> vcfAttack { 0.0f }, vcfDecay { 0.0f },
                               vcfSustain { 0.0f }, vcfRelease { 0.0f };
            std::atomic<float> lfoRate { 0.0f }, lfoDepth { 0.0f }, lfoDelay { 0.0f };
            std::atomic<float> velToFilter { 0.0f }, velToLoudness { 0.0f };

            /// One reading of the lot, for a stretch of audio to be rendered against.
            Trims read() const
            {
                Trims t;
                t.cutoff     = cutoff.load (std::memory_order_relaxed);
                t.amount     = amount.load (std::memory_order_relaxed);
                t.vcaAttack  = vcaAttack.load (std::memory_order_relaxed);
                t.vcaDecay   = vcaDecay.load (std::memory_order_relaxed);
                t.vcaSustain = vcaSustain.load (std::memory_order_relaxed);
                t.vcaRelease = vcaRelease.load (std::memory_order_relaxed);
                t.vcfAttack  = vcfAttack.load (std::memory_order_relaxed);
                t.vcfDecay   = vcfDecay.load (std::memory_order_relaxed);
                t.vcfSustain = vcfSustain.load (std::memory_order_relaxed);
                t.vcfRelease = vcfRelease.load (std::memory_order_relaxed);
                t.lfoRate    = lfoRate.load (std::memory_order_relaxed);
                t.lfoDepth   = lfoDepth.load (std::memory_order_relaxed);
                t.lfoDelay   = lfoDelay.load (std::memory_order_relaxed);
                t.velToFilter   = velToFilter.load (std::memory_order_relaxed);
                t.velToLoudness = velToLoudness.load (std::memory_order_relaxed);
                return t;
            }
        };

        AtomicTrims trims;

        /*
         * What to play.
         *
         * Called from the message thread. The patch is handed over rather than shared: see
         * the note in Engine.cpp on why this is a two-slot exchange and not simply an
         * atomic pointer. Call collectRetiredPatch() from the message thread now and then -
         * a timer, or the top of the next setPatch - or the old patch is never released.
         */
        void setPatch (PatchPtr patch);

        /*
         * Release whatever the audio thread has finished with.
         *
         * Message thread only. Freeing a patch is freeing every sample buffer in it, and
         * that must not happen where a late free is a click.
         */
        void collectRetiredPatch();

        // ------------------------------------------------------------------- playing

        /*
         * `at` is where in the next block the event belongs, in samples.
         *
         * A host hands over a block of audio and, with it, the events that happen part
         * way through it. Applying them all at the start instead - which is what the C#
         * does, because nothing driving it has anything better to offer - rounds every
         * note to the block boundary. At a 512-sample buffer that is 11 ms of jitter,
         * and 11 ms is audible on a drum pattern, which is what this instrument is for.
         *
         * Zero is "at the start of the next block", which is right for a note struck by
         * hand, by a MIDI port, or by anything else with no finer timing to give.
         */
        void noteOn (int note, int velocity, int at = 0) { post (EvNoteOn,  note, velocity, at); }
        void noteOff (int note, int at = 0)              { post (EvNoteOff, note, 0, at); }
        void modwheel (int value, int at = 0)            { post (EvWheel,   value, 0, at); }

        /*
         * The pitch wheel, 0..16383 with 8192 at rest.
         *
         * Split across the event's two byte fields because fourteen bits do not fit in one,
         * and reassembled in applyNextEvent. Sample-accurate like every other message: a bend
         * lands where the host put it rather than on the block boundary, which matters more
         * for a wheel than for a note because a sweep is a stream of them.
         */
        void pitchBend (int value, int at = 0)
        {
            const int v = value < 0 ? 0 : (value > 16383 ? 16383 : value);
            post (EvBend, static_cast<unsigned char> ((v >> 7) & 0x7F),
                          static_cast<unsigned char> (v & 0x7F), at);
        }

        /*
         * How far the wheel bends, in semitones. The machine's MIDI page offers 1 to 12.
         *
         * Not read off the disk. It is a setting of the machine rather than of a programme,
         * and the OVERALL SETTINGS file that would hold it is only written when someone saves
         * it deliberately - so the plugin owns it, as the host's user does.
         */
        std::atomic<double> bendRange { 2.0 };
        void allNotesOff (int at = 0)                    { post (EvAllOff,  0, 0, at); }

        // -------------------------------------------------------------------- render

        /// Fill `count` mono samples. Allocates nothing.
        /// Mono, as it always was. Bit-identical to before.
        void render (float* buffer, int count) { render (buffer, nullptr, count); }

        /*
         * Stereo, honouring each keygroup's output port.
         *
         * The machine's LEFT and RIGHT sockets are two mono outputs rather than a pan pot, so
         * a keygroup sent to one is absent from the other - 38 keygroups across four library
         * programmes, TUBULAR 2's bells among them. Everything else lands on both at full
         * level, which is what the mono path always did with everything.
         */
        void render (float* left, float* right, int count);

        // ------------------------------------------------------------ for the caller

        int getActiveVoices() const;

        const Voice& getVoice (int i) const { return voices[i]; }

    private:
        static constexpr unsigned char EvNoteOn  = 1;
        static constexpr unsigned char EvNoteOff = 2;
        static constexpr unsigned char EvWheel   = 3;
        static constexpr unsigned char EvAllOff  = 4;
        static constexpr unsigned char EvBend    = 5;

        static constexpr int RingSize = 256;

        struct Event
        {
            unsigned char  kind, a, b;
            unsigned short at;          // samples into the block this belongs to
        };

        void post (unsigned char kind, int a, int b, int at);
        void takePendingPatch();

        /// The next event's place in this block, clamped into it. False if there is none.
        bool peekEvent (int& at, int count) const;

        /// Take the next event off the ring and do it.
        void applyNextEvent();

        /// Every voice into one stretch of the buffer.
        void renderSpan (float* left, float* right, int count);
        void repatch();
        void startNote (int note, int velocity);
        void stopNote (int note);
        Voice& take();

        double sampleRate = 48000.0;

        Voice voices[Polyphony];
        std::vector<const KeygroupPatch*> matched;   // reused, so starting a note allocates nothing

        Event            ring[RingSize] {};
        std::atomic<int> writeIndex { 0 };
        std::atomic<int> readIndex  { 0 };

        /*
         * The patch, exchanged between threads without either one waiting.
         *
         * audioPatch belongs to the audio thread and nothing else touches it. pending is
         * filled by the message thread and taken by the audio thread; retired goes the other
         * way. Both hand-offs move the shared_ptr rather than copying it, so no reference
         * count is touched on the audio thread and nothing is ever freed there.
         */
        PatchPtr          audioPatch;
        PatchPtr          pending;
        PatchPtr          retired;
        std::atomic<bool> pendingReady { false };
        std::atomic<bool> retiredReady { false };

        long long sequence = 0;
        double    sharedPhase = 0.0, sharedStep = 0.0;
        int       wheel = 0;
        int       bend14 = 8192;         // the pitch wheel, at rest in the middle
    };
}
