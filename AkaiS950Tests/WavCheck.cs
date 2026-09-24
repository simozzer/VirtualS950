using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Text;
using AkaiS950List;
using AkaiS950Studio;

/*
 * The WAV export, against every sample on every disk in the library.
 *
 * The header is only half of it. The loop has to land exactly where SamplePlayer's
 * ApplyMarkers puts it, and where it should land is worked out here independently rather
 * than by calling the same code - the two disagreeing is the bug nobody would notice
 * until a DAW looped a sound somewhere the program never did.
 *
 * That matters more than it looks. 37 of the 61 samples in the shipped library declare a
 * loop longer than the sample it belongs to, so the clamping is the common path rather
 * than the edge case, and getting it wrong would be quiet and widespread.
 *
 *   WavCheck [folder of .hfe, or one image]
 */
static class WavCheck
{
    static int fails = 0, checks = 0;

    static void Check(string what, bool ok)
    {
        checks++;
        if (!ok) { Console.WriteLine("  FAIL " + what); fails++; }
    }

    static uint U32(byte[] b, int o)
    {
        return (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
    }

    /// <summary>The loop region ApplyMarkers would use, worked out independently here.</summary>
    static void ExpectedLoop(AkaiEntry e, int len, out bool loops, out long from, out long to)
    {
        long start = Math.Max(0, Math.Min(len, e.LoopStart));
        long end = e.LoopEnd <= 0 ? len : Math.Min(e.LoopEnd, len);
        if (end <= start) { start = 0; end = len; }

        long head = end - start;
        long loopLen = Math.Min(e.LoopLength, head);

        loops = (e.LoopMode == 'L' || e.LoopMode == 'A') && e.LoopLength >= 2 && loopLen >= 2;
        from = Math.Max(0, end - loopLen);
        to = Math.Min(len - 1, end - 1);
        if (to <= from) loops = false;
    }

    static void Main(string[] args)
    {
        string where = args.Length > 0 ? args[0] : "disks";
        var images = new List<string>();

        if (Directory.Exists(where)) images.AddRange(Directory.GetFiles(where, "*.hfe"));
        else if (File.Exists(where)) images.Add(where);

        if (images.Count == 0) { Console.WriteLine("no disk images to check"); Environment.Exit(1); }

        int samples = 0, looping = 0, clamped = 0;

        foreach (string image in images)
        {
            AkaiDisk disk;
            try { disk = AkaiDisk.Load(image); }
            catch (Exception ex) { Console.WriteLine("could not read " + image + ": " + ex.Message); fails++; continue; }

            foreach (var e in disk.Entries)
            {
                if (e.Type != 'S') continue;

                var pcm = disk.SamplePcm(e);
                if (pcm.Length == 0) continue;
                samples++;

                string who = Path.GetFileNameWithoutExtension(image) + "/" + e.Name.Trim();
                if (e.LoopLength > e.SampleCount) clamped++;

                var wav = WavFile.Build(pcm, e.SampleRate, e.LoopMode, e.LoopStart,
                                        e.LoopEnd, e.LoopLength, e.Tuning);

                Check(who + ": RIFF", Encoding.ASCII.GetString(wav, 0, 4) == "RIFF");
                Check(who + ": WAVE", Encoding.ASCII.GetString(wav, 8, 4) == "WAVE");
                Check(who + ": RIFF size", U32(wav, 4) == (uint)(wav.Length - 8));

                int at = 12;
                bool sawFmt = false, sawData = false, sawSmpl = false;
                int dataBytes = 0;
                uint loopStart = 0, loopEnd = 0, loopType = 0, unityNote = 0;

                while (at + 8 <= wav.Length)
                {
                    string id = Encoding.ASCII.GetString(wav, at, 4);
                    uint size = U32(wav, at + 4);
                    int body = at + 8;

                    if (id == "fmt ")
                    {
                        sawFmt = true;
                        Check(who + ": PCM", (wav[body] | (wav[body + 1] << 8)) == 1);
                        Check(who + ": mono", (wav[body + 2] | (wav[body + 3] << 8)) == 1);
                        Check(who + ": rate", U32(wav, body + 4) == (uint)e.SampleRate);
                        Check(who + ": 16-bit", (wav[body + 14] | (wav[body + 15] << 8)) == 16);
                    }
                    else if (id == "data") { sawData = true; dataBytes = (int)size; }
                    else if (id == "smpl")
                    {
                        sawSmpl = true;
                        unityNote = U32(wav, body + 12);
                        Check(who + ": one loop", U32(wav, body + 28) == 1);
                        loopType = U32(wav, body + 36 + 4);
                        loopStart = U32(wav, body + 36 + 8);
                        loopEnd = U32(wav, body + 36 + 12);
                    }

                    at = body + (int)size + ((size & 1) == 1 ? 1 : 0);
                }

                Check(who + ": chunks end exactly at EOF", at == wav.Length);
                Check(who + ": has fmt", sawFmt);
                Check(who + ": has data", sawData);
                Check(who + ": data holds every sample", dataBytes == pcm.Length * 2);

                bool wantLoop; long wantFrom, wantTo;
                ExpectedLoop(e, pcm.Length, out wantLoop, out wantFrom, out wantTo);

                Check(who + ": smpl present iff it loops", sawSmpl == wantLoop);
                if (wantLoop) looping++;

                if (sawSmpl && wantLoop)
                {
                    Check(who + ": loop start matches ApplyMarkers (" + wantFrom + ")",
                          loopStart == (uint)wantFrom);
                    Check(who + ": loop end matches ApplyMarkers (" + wantTo + ")",
                          loopEnd == (uint)wantTo);
                    Check(who + ": loop inside the audio", loopEnd < (uint)pcm.Length);
                    Check(who + ": type", loopType == (uint)(e.LoopMode == 'A' ? 1 : 0));
                    Check(who + ": root note", unityNote == (uint)Math.Max(0, Math.Min(127, e.Tuning / 16)));
                }
            }
        }

        // One real file through Windows' own parser: the only check that speaks for every
        // other program that will ever open one of these.
        string probe = Path.Combine(Path.GetTempPath(), "akai-wav-check.wav");
        var d0 = AkaiDisk.Load(images[0]);
        foreach (var e in d0.Entries)
        {
            if (e.Type != 'S') continue;
            var pcm = d0.SamplePcm(e);
            if (pcm.Length == 0) continue;

            File.WriteAllBytes(probe, WavFile.Build(pcm, e.SampleRate, e.LoopMode,
                                                    e.LoopStart, e.LoopEnd, e.LoopLength, e.Tuning));
            try
            {
                var p = new SoundPlayer(probe);
                p.Load();
                Check("Windows parses the file", true);
                p.Dispose();
            }
            catch (Exception ex) { Check("Windows parses the file - " + ex.Message, false); }
            File.Delete(probe);
            break;
        }

        Console.WriteLine();
        Console.WriteLine(images.Count + " disk(s), " + samples + " sample(s), "
                          + looping + " looping, " + clamped + " with a loop longer than the sample");
        Console.WriteLine(checks + " checks, " + (fails == 0 ? "ALL PASSED" : fails + " FAILED"));
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
