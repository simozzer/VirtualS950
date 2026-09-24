using System;
using System.IO;
using System.Media;
using System.Text;

namespace AkaiS950Studio
{
    /// <summary>
    /// Plays a decoded sample by wrapping it in a WAV header and handing it to
    /// SoundPlayer. Deliberately dumb: the sample comes out at its recorded rate
    /// with no filter, envelope, tuning or looping applied.
    /// </summary>
    internal sealed class SamplePlayer : IDisposable
    {
        readonly SoundPlayer _player = new SoundPlayer();

        public void Play(short[] pcm, int sampleRate)
        {
            if (pcm == null || pcm.Length == 0) return;

            // A corrupt header should not hand winmm an absurd rate.
            if (sampleRate < 1000 || sampleRate > 192000) sampleRate = 40000;

            Stop();
            _player.Stream = BuildWav(pcm, sampleRate);
            _player.Play();
        }

        /// <summary>
        /// Plays a sample shifted by a number of semitones, the way the sampler does it:
        /// by reading it out at a different rate, so duration moves with pitch. No
        /// stretching, no interpolation of the source unless the device needs it.
        /// </summary>
        public void PlayShifted(short[] pcm, int sampleRate, double semitones)
        {
            if (pcm == null || pcm.Length == 0) return;

            double target = sampleRate * Math.Pow(2, semitones / 12.0);

            // Within what the audio stack will take, varispeed alone does it.
            if (target >= MinDeviceRate && target <= MaxDeviceRate)
            {
                Play(pcm, (int)Math.Round(target));
                return;
            }

            // Outside it - a very low or very high key - convert the audio instead so
            // the device still gets a rate it will accept, at the same pitch.
            int rate = (int)Math.Round(Math.Max(MinDeviceRate, Math.Min(MaxDeviceRate, target)));
            int from = (int)Math.Round(Math.Max(1, Math.Min(int.MaxValue, target)));

            var f = new float[pcm.Length];
            for (int i = 0; i < f.Length; i++) f[i] = pcm[i] / 32768f;

            var at = AudioImport.Resample(f, from, rate);
            var outp = new short[at.Length];
            for (int i = 0; i < outp.Length; i++)
            {
                int v = (int)Math.Round(at[i] * 32768f);
                if (v > 32767) v = 32767;
                if (v < -32768) v = -32768;
                outp[i] = (short)v;
            }
            Play(outp, rate);
        }

        /// <summary>
        /// How long a looping sample is made to sustain. SoundPlayer plays a buffer to its
        /// end and has no notion of a loop region, so the loop is unrolled into the audio
        /// rather than repeated by the device. Long enough to hear the loop hold, short
        /// enough that nothing runs away.
        /// </summary>
        public const double SustainSeconds = 4.0;

        /// <summary>
        /// The audio a sample actually sounds, with its markers applied: everything from
        /// the start marker to the end marker, and then - if it loops - the loop region
        /// repeated until the sustain is filled.
        ///
        /// The loop is the tail running back from the end marker by the loop length, not
        /// the whole marked span. 'L' repeats it forwards; 'A' alternates, playing it
        /// backwards on every second pass; anything else is a one-shot and stops.
        /// </summary>
        public static short[] ApplyMarkers(short[] pcm, long start, long end, long loopLength,
                                           char loopMode, int rate, double sustainSeconds)
        {
            if (pcm == null || pcm.Length == 0) return pcm;

            int from = (int)Math.Max(0, Math.Min(pcm.Length, start));
            int to = (int)Math.Max(from, Math.Min(pcm.Length, end <= 0 ? pcm.Length : end));
            if (to <= from) { from = 0; to = pcm.Length; }

            int head = to - from;
            bool loops = (loopMode == 'L' || loopMode == 'A') && loopLength >= 2;
            if (!loops)
            {
                if (from == 0 && to == pcm.Length) return pcm;
                var once = new short[head];
                Array.Copy(pcm, from, once, 0, head);
                return once;
            }

            int loopLen = (int)Math.Min(loopLength, head);
            int loopFrom = to - loopLen;

            int want = (int)Math.Round(Math.Max(0, sustainSeconds) * Math.Max(1, rate));
            int repeats = head >= want ? 1 : 1 + (want - head + loopLen - 1) / loopLen;
            if (repeats < 2) repeats = 2;                 // a loop should be heard at least once

            var outp = new short[head + (repeats - 1) * loopLen];
            Array.Copy(pcm, from, outp, 0, head);

            int at = head;
            for (int r = 1; r < repeats; r++)
            {
                bool backwards = loopMode == 'A' && (r % 2) == 1;
                for (int i = 0; i < loopLen; i++)
                    outp[at + i] = pcm[loopFrom + (backwards ? loopLen - 1 - i : i)];
                at += loopLen;
            }
            return outp;
        }
        const int MinDeviceRate = 4000;
        const int MaxDeviceRate = 96000;

        public void Stop()
        {
            try { _player.Stop(); }
            catch (Exception) { /* nothing was playing */ }
        }

        /// <summary>
        /// A 16-bit mono RIFF/WAVE wrapper around the samples, from the same writer the
        /// WAV export uses - no loop chunk here, because this buffer has already had its
        /// loop unrolled into it by ApplyMarkers and SoundPlayer would ignore one anyway.
        /// </summary>
        static MemoryStream BuildWav(short[] pcm, int rate)
        {
            return new MemoryStream(WavFile.Build(pcm, rate));
        }

        public void Dispose()
        {
            Stop();
            _player.Dispose();
        }
    }
}
