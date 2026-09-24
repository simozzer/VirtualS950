using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AkaiS950Studio
{
    /// <summary>
    /// What was open last time, and what a first-time user opens instead.
    ///
    /// Deliberately a text file rather than the registry or a settings framework: one
    /// path per line, readable, editable, and deletable by anyone who wants the program
    /// to forget - which is the whole of its contract. It carries nothing that matters,
    /// so every failure here is swallowed. A settings file that cannot be read is not a
    /// reason to refuse to start, and not worth a dialog either.
    /// </summary>
    internal static class Session
    {
        /// <summary>
        /// Beside the user's other per-user settings, which needs no elevation and
        /// follows a roaming profile to whatever machine they sit at next.
        /// </summary>
        static string SettingsFile
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AkaiS950Studio");
                return Path.Combine(dir, "session.txt");
            }
        }

        /// <summary>
        /// Whether this user has ever run the program before.
        ///
        /// The file's existence is the flag. It is written on every exit, including the
        /// exit that leaves nothing open - which is what separates "never been here" from
        /// "was here and closed the disks on purpose". The first deserves a disk to play
        /// with; the second asked for an empty window and should get one.
        /// </summary>
        public static bool HasRunBefore()
        {
            try { return File.Exists(SettingsFile); }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// The images open at the end of the last session, in the order they were loaded.
        /// Paths only - whether they are still there is the caller's problem, because the
        /// caller is the one that can say so in the status bar.
        /// </summary>
        public static string[] LastOpened()
        {
            var paths = new List<string>();
            try
            {
                if (!File.Exists(SettingsFile)) return new string[0];

                foreach (string line in File.ReadAllLines(SettingsFile, Encoding.UTF8))
                {
                    string s = line.Trim();
                    if (s.Length == 0 || s[0] == '#') continue;
                    paths.Add(s);
                }
            }
            catch (Exception) { /* an unreadable session is an empty one */ }

            return paths.ToArray();
        }

        /// <summary>Write down what is open, replacing whatever was remembered before.</summary>
        public static void Remember(string[] paths)
        {
            try
            {
                string file = SettingsFile;
                string dir = Path.GetDirectoryName(file);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var text = new StringBuilder();
                text.AppendLine("# Disk images open when AkaiS950Studio last closed.");
                text.AppendLine("# Delete this file to start as though for the first time.");

                if (paths != null)
                    foreach (string p in paths)
                        if (!string.IsNullOrEmpty(p)) text.AppendLine(p);

                File.WriteAllText(file, text.ToString(), Encoding.UTF8);
            }
            catch (Exception) { /* nothing here is worth interrupting an exit for */ }
        }

        /// <summary>
        /// The disk a first-time user starts on: BASS.hfe out of the bundled library.
        ///
        /// The installer puts the library in Disks\ beside the executable, so that is
        /// where this looks first. A build run out of the repository has its images in
        /// disks\ at the root while the executable can be several folders down in an SDK
        /// build tree, so the search also walks upwards a little way - which costs four
        /// directory probes and means the program behaves the same way whether it was
        /// installed or just built.
        /// </summary>
        public static string StarterDisk()
        {
            return FindBundled("BASS.hfe");
        }

        static string FindBundled(string name)
        {
            try
            {
                string at = AppDomain.CurrentDomain.BaseDirectory;

                for (int up = 0; up < 5 && !string.IsNullOrEmpty(at); up++)
                {
                    string hit = Path.Combine(Path.Combine(at, "Disks"), name);
                    if (File.Exists(hit)) return hit;

                    DirectoryInfo parent = Directory.GetParent(at.TrimEnd(
                        Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (parent == null) break;
                    at = parent.FullName;
                }
            }
            catch (Exception) { /* no library to be found, then */ }

            return null;
        }
    }
}
