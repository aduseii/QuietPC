using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace QuietPC
{
    internal static class Paths
    {
        public static readonly string DataDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "QuietPC");
        public static readonly string InstallDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "QuietPC");
        public static readonly string InstallExe = Path.Combine(InstallDir, "QuietPC.exe");

        public static string Settings { get { return Path.Combine(DataDir, "settings.txt"); } }
        public static string Exclusions { get { return Path.Combine(DataDir, "exclusions.txt"); } }
        public static string ThrottledState { get { return Path.Combine(DataDir, "throttled.txt"); } }
        public static string TurboOriginal { get { return Path.Combine(DataDir, "turbo-original.txt"); } }
        public static string EdgeOriginal { get { return Path.Combine(DataDir, "edge-original.txt"); } }
        public static string Log { get { return Path.Combine(DataDir, "log.txt"); } }
        public static string Shortcut
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                    "Programs", "Quiet PC.lnk");
            }
        }

        public static void EnsureDataDir() { Directory.CreateDirectory(DataDir); }
    }

    internal static class Log
    {
        private static readonly object Gate = new object();

        public static void Write(string msg)
        {
            lock (Gate)
            {
                try
                {
                    Paths.EnsureDataDir();
                    var fi = new FileInfo(Paths.Log);
                    if (fi.Exists && fi.Length > 1024 * 1024) File.WriteAllText(Paths.Log, "");
                    File.AppendAllText(Paths.Log, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + msg + Environment.NewLine);
                }
                catch { }
            }
        }
    }

    /// <summary>User settings, stored as key=value lines in %ProgramData%\QuietPC\settings.txt.</summary>
    internal sealed class Settings
    {
        public bool QuietMode = true;
        public bool CapTurbo = true;
        public bool EdgeSaver = true;
        public int IntervalSec = 5;
        public int CpuThreshold = 15;   // % of one CPU core
        public bool Welcomed = false;

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(Paths.Settings)) return s;
                foreach (var raw in File.ReadAllLines(Paths.Settings))
                {
                    int eq = raw.IndexOf('=');
                    if (eq < 1) continue;
                    string k = raw.Substring(0, eq).Trim(), v = raw.Substring(eq + 1).Trim();
                    int n;
                    switch (k)
                    {
                        case "QuietMode": s.QuietMode = v == "1"; break;
                        case "CapTurbo": s.CapTurbo = v == "1"; break;
                        case "EdgeSaver": s.EdgeSaver = v == "1"; break;
                        case "Welcomed": s.Welcomed = v == "1"; break;
                        case "IntervalSec": if (int.TryParse(v, out n)) s.IntervalSec = Math.Max(2, Math.Min(60, n)); break;
                        case "CpuThreshold": if (int.TryParse(v, out n)) s.CpuThreshold = Math.Max(1, Math.Min(100, n)); break;
                    }
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                Paths.EnsureDataDir();
                File.WriteAllLines(Paths.Settings, new[]
                {
                    "# Quiet PC settings. Quit and restart Quiet PC after editing.",
                    "QuietMode=" + B(QuietMode),
                    "CapTurbo=" + B(CapTurbo),
                    "EdgeSaver=" + B(EdgeSaver),
                    "IntervalSec=" + IntervalSec,
                    "CpuThreshold=" + CpuThreshold,
                    "Welcomed=" + B(Welcomed)
                });
            }
            catch { }
        }

        private static string B(bool b) { return b ? "1" : "0"; }
    }

    internal static class Exclusions
    {
        private const string Default =
@"# Quiet PC exclusions - apps listed here are never throttled.
# One name per line. Any process whose name contains this text is skipped (not case-sensitive).
# Calls and music stutter when throttled, so they are excluded by default.
Spotify
Zoom
Teams
Discord
Skype
obs64
vlc
";

        public static void EnsureFile()
        {
            Paths.EnsureDataDir();
            if (!File.Exists(Paths.Exclusions)) File.WriteAllText(Paths.Exclusions, Default);
        }

        public static List<string> Load()
        {
            try
            {
                if (!File.Exists(Paths.Exclusions)) return new List<string>();
                return File.ReadAllLines(Paths.Exclusions)
                    .Select(l => { int h = l.IndexOf('#'); return (h >= 0 ? l.Substring(0, h) : l).Trim(); })
                    .Where(l => l.Length > 0)
                    .ToList();
            }
            catch { return new List<string>(); }
        }

        public static bool Matches(string name, List<string> list)
        {
            foreach (var e in list)
                if (name.IndexOf(e, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }
    }
}
