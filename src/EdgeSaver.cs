using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Win32;

namespace QuietPC
{
    /// <summary>
    /// Cuts Microsoft Edge's memory use:
    ///  * Edge policies: sleeping tabs after 5 minutes, efficiency mode always on,
    ///    no Startup Boost, and no running in the background after Edge is closed.
    ///    (Applied on next Edge start; Edge will say it's "managed by your organization".)
    ///  * While Edge is in the background, its idle memory is trimmed out of RAM.
    /// </summary>
    internal static class EdgeSaver
    {
        private const string PolicyKey = @"SOFTWARE\Policies\Microsoft\Edge";

        private static readonly KeyValuePair<string, int>[] Policies =
        {
            new KeyValuePair<string, int>("SleepingTabsEnabled", 1),
            new KeyValuePair<string, int>("SleepingTabsTimeout", 300),
            new KeyValuePair<string, int>("EfficiencyMode", 0),        // 0 = always active
            new KeyValuePair<string, int>("StartupBoostEnabled", 0),
            new KeyValuePair<string, int>("BackgroundModeEnabled", 0),
        };

        private static DateTime? _backgroundSince;
        private static DateTime? _lastTrim;

        private static RegistryKey OpenHklm()
        {
            return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        }

        public static void ApplyPolicies()
        {
            if (File.Exists(Paths.EdgeOriginal)) return;   // already applied
            try
            {
                using (var hklm = OpenHklm())
                using (var key = hklm.CreateSubKey(PolicyKey, true))
                {
                    var original = new List<string>();
                    foreach (var p in Policies)
                    {
                        object cur = key.GetValue(p.Key);
                        original.Add(p.Key + "=" + (cur == null ? "" : cur.ToString()));
                    }
                    Paths.EnsureDataDir();
                    File.WriteAllLines(Paths.EdgeOriginal, original);
                    foreach (var p in Policies) key.SetValue(p.Key, p.Value, RegistryValueKind.DWord);
                }
                Log.Write("Edge memory policies applied");
            }
            catch (Exception ex) { Log.Write("Edge policies failed: " + ex.Message); }
        }

        public static void RestorePolicies()
        {
            if (!File.Exists(Paths.EdgeOriginal)) return;
            try
            {
                using (var hklm = OpenHklm())
                using (var key = hklm.CreateSubKey(PolicyKey, true))
                {
                    foreach (var line in File.ReadAllLines(Paths.EdgeOriginal))
                    {
                        int eq = line.IndexOf('=');
                        if (eq < 1) continue;
                        string name = line.Substring(0, eq), val = line.Substring(eq + 1);
                        int n;
                        if (val.Length == 0) key.DeleteValue(name, false);
                        else if (int.TryParse(val, out n)) key.SetValue(name, n, RegistryValueKind.DWord);
                    }
                }
                File.Delete(Paths.EdgeOriginal);
                Log.Write("Edge memory policies removed");
            }
            catch (Exception ex) { Log.Write("Edge policy restore failed: " + ex.Message); }
        }

        /// <summary>Trims all Edge processes now. Returns MB freed.</summary>
        public static long TrimNow()
        {
            long before = EdgeWorkingSet(true);
            if (before < 0) return 0;
            foreach (var p in Process.GetProcessesByName("msedge"))
                using (p) Native.Trim(p.Id);
            Thread.Sleep(300);
            long after = Math.Max(0, EdgeWorkingSet(false));
            long freedMb = Math.Max(0, (before - after) / (1024 * 1024));
            Log.Write("Edge trimmed: " + freedMb + " MB freed");
            return freedMb;
        }

        private static long EdgeWorkingSet(bool requireRunning)
        {
            var procs = Process.GetProcessesByName("msedge");
            if (procs.Length == 0) return requireRunning ? -1 : 0;
            long sum = 0;
            foreach (var p in procs)
                using (p) { try { sum += p.WorkingSet64; } catch { } }
            return sum;
        }

        /// <summary>Called every tick: trim after Edge has been in the background 30 s, then every 5 min.</summary>
        public static void Tick(string foregroundName, DateTime now)
        {
            if (string.Equals(foregroundName, "msedge", StringComparison.OrdinalIgnoreCase))
            {
                _backgroundSince = null;
                _lastTrim = null;
                return;
            }
            if (_backgroundSince == null) { _backgroundSince = now; return; }
            if ((now - _backgroundSince.Value).TotalSeconds < 30) return;
            if (_lastTrim != null && (now - _lastTrim.Value).TotalSeconds < 300) return;
            _lastTrim = now;
            if (Process.GetProcessesByName("msedge").Any()) TrimNow();
        }
    }
}
