using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace QuietPC
{
    /// <summary>
    /// Caps "Maximum processor state" at 99% on the current power plan, which turns off
    /// Intel Turbo Boost. Turbo is what spins up a MacBook's fans under Boot Camp.
    /// The original values are saved so they can always be put back.
    /// </summary>
    internal static class Turbo
    {
        public static bool IsCapped { get { return File.Exists(Paths.TurboOriginal); } }

        public static void Cap()
        {
            if (IsCapped) return;
            int ac, dc;
            if (!TryGetMax(out ac, out dc)) { Log.Write("Could not read processor max state; Turbo cap skipped"); return; }
            Paths.EnsureDataDir();
            File.WriteAllText(Paths.TurboOriginal, ac + " " + dc);
            SetMax(Math.Min(ac, 99), Math.Min(dc, 99));
            Log.Write("Turbo Boost capped (was " + ac + "% plugged in / " + dc + "% on battery)");
        }

        public static void Restore()
        {
            if (!IsCapped) return;
            try
            {
                var parts = File.ReadAllText(Paths.TurboOriginal).Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                int ac, dc;
                if (parts.Length == 2 && int.TryParse(parts[0], out ac) && int.TryParse(parts[1], out dc))
                    SetMax(ac, dc);
                File.Delete(Paths.TurboOriginal);
                Log.Write("Turbo Boost restored");
            }
            catch (Exception ex) { Log.Write("Turbo restore failed: " + ex.Message); }
        }

        private static bool TryGetMax(out int ac, out int dc)
        {
            ac = dc = 0;
            string output = Powercfg("/query SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX");
            // The last two hex values are the current plugged-in (AC) and battery (DC) settings.
            var m = Regex.Matches(output, @"0x[0-9a-fA-F]{8}");
            if (m.Count < 2) return false;
            ac = Convert.ToInt32(m[m.Count - 2].Value, 16);
            dc = Convert.ToInt32(m[m.Count - 1].Value, 16);
            return ac > 0 && dc > 0;
        }

        private static void SetMax(int ac, int dc)
        {
            Powercfg("/setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX " + ac);
            Powercfg("/setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX " + dc);
            Powercfg("/setactive SCHEME_CURRENT");
        }

        private static string Powercfg(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("powercfg.exe", args)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(10000);
                    return o;
                }
            }
            catch { return ""; }
        }
    }
}
