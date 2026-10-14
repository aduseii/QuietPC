using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace QuietPC
{
    /// <summary>
    /// Every tick: any process that isn't the app you're using and is burning CPU gets
    /// Idle priority + Windows power throttling. Known background hogs are always throttled.
    /// Bringing an app to the front restores it right away.
    /// </summary>
    internal sealed class Throttler
    {
        private sealed class Entry
        {
            public string Name;
            public ProcessPriorityClass Original;
        }

        private static readonly HashSet<string> AlwaysThrottle = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SearchIndexer", "SearchProtocolHost", "SearchFilterHost",
            "TiWorker", "TrustedInstaller", "MoUsoCoreWorker", "usocoreworker", "wuauclt", "MusNotification",
            "CompatTelRunner", "DeviceCensus", "backgroundTaskHost", "SgrmBroker",
            "OneDrive", "Dropbox", "GoogleDriveFS", "iCloudDrive", "iCloudServices", "APSDaemon",
            "AdobeUpdateService", "AdobeARM", "GoogleUpdate", "MicrosoftEdgeUpdate", "steamwebhelper"
        };

        // Windows, audio, input and Boot Camp need these responsive.
        private static readonly HashSet<string> NeverThrottle = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System", "Idle", "Registry", "Memory Compression", "smss", "csrss", "wininit", "winlogon", "services",
            "lsass", "LogonUI", "dwm", "explorer", "sihost", "ctfmon", "fontdrvhost", "svchost", "audiodg",
            "ShellExperienceHost", "StartMenuExperienceHost", "SearchUI", "SearchApp", "TextInputHost",
            "RuntimeBroker", "Taskmgr", "conhost", "MsMpEng", "NisSrv", "SecurityHealthService",
            "BootCamp", "AppleOSSMgr", "AppleTimeSrv", "KbdMgr", "QuietPC"
        };

        private readonly Dictionary<int, Entry> _throttled = new Dictionary<int, Entry>();
        private readonly Dictionary<int, double> _cpuLast = new Dictionary<int, double>();
        private DateTime _lastTick = DateTime.UtcNow;
        private readonly int _selfPid = Process.GetCurrentProcess().Id;

        public int Count { get { return _throttled.Count; } }

        public IEnumerable<string> ThrottledNames
        {
            get { return _throttled.Values.Select(e => e.Name); }
        }

        public void Tick(bool enabled, int cpuThreshold, string foregroundName, int intervalSec)
        {
            var now = DateTime.UtcNow;
            double elapsed = (now - _lastTick).TotalSeconds;
            if (elapsed < 0.5) elapsed = intervalSec;
            _lastTick = now;

            var exclusions = Exclusions.Load();
            var seen = new HashSet<int>();

            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    int id = p.Id;
                    if (id <= 4 || id == _selfPid) continue;
                    seen.Add(id);

                    double pct = 0;
                    try
                    {
                        double t = p.TotalProcessorTime.TotalSeconds;
                        double last;
                        if (_cpuLast.TryGetValue(id, out last)) pct = (t - last) / elapsed * 100.0;
                        _cpuLast[id] = t;
                    }
                    catch { }

                    if (!enabled) continue;
                    string name;
                    try { name = p.ProcessName; } catch { continue; }
                    if (NeverThrottle.Contains(name)) continue;

                    bool was = _throttled.ContainsKey(id);
                    // The app you're using (all of its processes, e.g. every chrome.exe) stays at full speed.
                    bool isFront = foregroundName != null && string.Equals(name, foregroundName, StringComparison.OrdinalIgnoreCase);
                    if (isFront || Exclusions.Matches(name, exclusions))
                    {
                        if (was) { Release(id); Log.Write("released " + name + " (" + id + ")"); }
                        continue;
                    }
                    if (was) continue;   // stays throttled while it's in the background

                    if (AlwaysThrottle.Contains(name) || pct >= cpuThreshold) Throttle(p, name, pct);
                }
            }

            foreach (var id in _cpuLast.Keys.Where(k => !seen.Contains(k)).ToList()) _cpuLast.Remove(id);
            foreach (var id in _throttled.Keys.Where(k => !seen.Contains(k)).ToList()) _throttled.Remove(id);
            SaveState();
        }

        private void Throttle(Process p, string name, double pct)
        {
            ProcessPriorityClass orig;
            try
            {
                orig = p.PriorityClass;
                p.PriorityClass = ProcessPriorityClass.Idle;
            }
            catch { return; }   // protected or exited
            Native.SetEco(p.Id, true);
            _throttled[p.Id] = new Entry { Name = name, Original = orig };
            Log.Write(string.Format("throttled {0} ({1}) at {2:0}% CPU", name, p.Id, pct));
        }

        private void Release(int id)
        {
            Entry e;
            if (!_throttled.TryGetValue(id, out e)) return;
            _throttled.Remove(id);
            RestoreProcess(id, e.Name, e.Original);
        }

        public void ReleaseAll()
        {
            foreach (var id in _throttled.Keys.ToList()) Release(id);
            SaveState();
        }

        private static void RestoreProcess(int id, string name, ProcessPriorityClass original)
        {
            try
            {
                using (var p = Process.GetProcessById(id))
                {
                    if (!string.Equals(p.ProcessName, name, StringComparison.OrdinalIgnoreCase)) return; // PID reused
                    p.PriorityClass = original;
                    Native.SetEco(id, false);
                }
            }
            catch { }
        }

        private void SaveState()
        {
            try
            {
                Paths.EnsureDataDir();
                File.WriteAllLines(Paths.ThrottledState,
                    _throttled.Select(kv => kv.Key + "\t" + kv.Value.Name + "\t" + (int)kv.Value.Original));
            }
            catch { }
        }

        /// <summary>Undo anything left over from a crash, a restart or a killed instance.</summary>
        public static void RestoreFromStateFile()
        {
            try
            {
                if (!File.Exists(Paths.ThrottledState)) return;
                foreach (var line in File.ReadAllLines(Paths.ThrottledState))
                {
                    var f = line.Split('\t');
                    int id, prio;
                    if (f.Length == 3 && int.TryParse(f[0], out id) && int.TryParse(f[2], out prio))
                        RestoreProcess(id, f[1], (ProcessPriorityClass)prio);
                }
                File.Delete(Paths.ThrottledState);
            }
            catch { }
        }
    }
}
