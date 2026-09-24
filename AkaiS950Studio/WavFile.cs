using System;
using System.IO;
using System.Text;

namespace AkaiS950Studio
{
    /// <summary>
    /// A 16-bit mono RIFF/WAVE file.
    ///
    /// Everything the sample stores comes out: all of the audio, at its own rate, with
    /// the loop and the root note written into the standard 'smpl' chunk rather than
    /// baked into the samples. A sampler or DAW that reads that chunk - most do - picks
    /// the loop up by itself, and one that does not still gets the whole sound and
    /// simply plays it through. Nothing is discarded either way, which is what makes the
    /// export reversible: the markers can still be moved afterwards, because the audio
    /// outside them is still there.
    /// </summary>
    internal static class WavFile
    {
        /// <summary>Audio only, for anything that just wants to hear it.</summary>
        public static byte[] Build(short[] pcm, int rate)
        {
            return Build(pcm, rate, 'O', 0, 0, 0, 0);
        }

        /// <param name="tuning">16ths of a semitone, C3 = 960, as AkaiEntry keeps it.</param>
        public static byte[] Build(short[] pcm, int rate, char loopMode, long loopStart,
                                   long loopEnd, long loopLength, int tuning)
        {
            if (pcm == null) pcm = new short[0];

            // A corrupt header should not produce a file no player will open.
            if (rate < 1000 || rate > 192000) rate = 40000;

            /*
             * The loop as the machine keeps it: the tail running back from the END marker
             * by the loop length, not the whole marked span.
             *
             * Every step here mirrors SamplePlayer.ApplyMarkers deliberately, including
             * the clamps, because the two disagreeing would mean a file that loops
             * somewhere this program never said it did. The clamps are not theoretical:
             * BASS.hfe's 'SQUARE' is 1222 samples long and declares a loop length of
             * 1223, so a loop start worked out by subtraction alone lands before the
             * start of the sound.
             *
             * The markers are kept as absolute positions because the export is the whole
             * sample rather than the marked span - nothing is trimmed, so nothing shifts.
             */
            long start = loopStart;
            if (start < 0) start = 0;
            if (start > pcm.Length) start = pcm.Length;

            long end = loopEnd <= 0 ? pcm.Length : loopEnd;
            if (end > pcm.Length) end = pcm.Length;
            if (end <= start) { start = 0; end = pcm.Length; }

            long head = end - start;
            long loopLen = loopLength < head ? loopLength : head;

            bool loops = (loopMode == 'L' || loopMode == 'A') && loopLength >= 2 && loopLen >= 2;

            // 'smpl' names the last frame inside the loop, where Akai's end marker is one
            // past it - hence the -1.
            long from = end - loopLen;
            long to = end - 1;

            if (from < 0) from = 0;
            if (to > pcm.Length - 1) to = pcm.Length - 1;
            if (to <= from) loops = false;

            int dataBytes = pcm.Length * 2;
            int smplBytes = loops ? 8 + 36 + 24 : 0;

            var ms = new MemoryStream(44 + dataBytes + smplBytes);
            var w = new BinaryWriter(ms);

            w.Write(Encoding.ASCII.GetBytes("RIFF"));
            w.Write(4 + (8 + 16) + (8 + dataBytes) + smplBytes);
            w.Write(Encoding.ASCII.GetBytes("WAVE"));

            w.Write(Encoding.ASCII.GetBytes("fmt "));
            w.Write(16);                  // chunk size for PCM
            w.Write((short)1);            // format: PCM
            w.Write((short)1);            // mono
            w.Write(rate);
            w.Write(rate * 2);            // bytes per second
            w.Write((short)2);            // block align
            w.Write((short)16);           // bits per sample

            w.Write(Encoding.ASCII.GetBytes("data"));
            w.Write(dataBytes);
            for (int i = 0; i < pcm.Length; i++) w.Write(pcm[i]);

            if (loops)
            {
                /*
                 * The root note travels with it. Tuning is 16ths of a semitone with C3 at
                 * 960, so the whole semitones are the MIDI note and the remainder is the
                 * fine tuning - which 'smpl' wants as a fraction of a semitone scaled
                 * across a full 32 bits.
                 */
                int note = tuning > 0 ? tuning / 16 : 60;
                if (note < 0) note = 0;
                if (note > 127) note = 127;

                uint fraction = (uint)((tuning > 0 ? tuning % 16 : 0) * (4294967296L / 16));

                w.Write(Encoding.ASCII.GetBytes("smpl"));
                w.Write(36 + 24);
                w.Write(0);                                     // manufacturer
                w.Write(0);                                     // product
                w.Write((int)(1000000000L / rate));             // sample period, nanoseconds
                w.Write(note);                                  // MIDI unity note
                w.Write(fraction);                              // MIDI pitch fraction
                w.Write(0);                                     // SMPTE format
                w.Write(0);                                     // SMPTE offset
                w.Write(1);                                     // one loop
                w.Write(0);                                     // no extra sampler data

                w.Write(0);                                     // cue point id
                w.Write(loopMode == 'A' ? 1 : 0);               // 0 forwards, 1 alternating
                w.Write((int)from);
                w.Write((int)to);
                w.Write(0);                                     // fraction
                w.Write(0);                                     // play count: 0 is for ever
            }

            w.Flush();
            return ms.ToArray();
        }
    }
}
