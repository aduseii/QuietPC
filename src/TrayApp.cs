using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace QuietPC
{
    internal sealed class TrayApp : ApplicationContext
    {
        private readonly Settings _settings;
        private readonly Throttler _throttler = new Throttler();
        private readonly NotifyIcon _tray = new NotifyIcon();
        private readonly Timer _timer = new Timer();
        private readonly Icon _iconOn, _iconOff;
        private readonly ToolStripMenuItem _miQuiet, _miTurbo, _miEdge;
        private bool _stopped;

        public TrayApp()
        {
            Paths.EnsureDataDir();
            Exclusions.EnsureFile();
            _settings = Settings.Load();

            Throttler.RestoreFromStateFile();
            SyncTurbo();
            SyncEdge();
            Log.Write("started " + Installer.Version);
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }

            _iconOn = MakeIcon(Color.FromArgb(0, 137, 123));
            _iconOff = MakeIcon(Color.FromArgb(128, 128, 128));

            var menu = new ContextMenuStrip();
            _miQuiet = new ToolStripMenuItem("Quiet mode", null, (s, e) => ToggleQuiet());
            _miQuiet.Font = new Font(_miQuiet.Font, FontStyle.Bold);
            _miTurbo = new ToolStripMenuItem("Cap Turbo Boost (quietest)", null, (s, e) => ToggleTurbo());
            _miEdge = new ToolStripMenuItem("Edge memory saver", null, (s, e) => ToggleEdge());
            menu.Items.Add(_miQuiet);
            menu.Items.Add(_miTurbo);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_miEdge);
            menu.Items.Add("Free Edge memory now", null, (s, e) => FreeEdgeNow());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Show throttled apps", null, (s, e) => ShowThrottled());
            menu.Items.Add("Edit exclusions...", null, (s, e) => OpenInNotepad(Paths.Exclusions));
            menu.Items.Add("Open log", null, (s, e) => OpenInNotepad(Paths.Log));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Quit (restores everything)", null, (s, e) => Quit());
            menu.Items.Add("Uninstall Quiet PC...", null, (s, e) => UninstallFromTray());

            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (s, e) => ToggleQuiet();
            UpdateTray();
            _tray.Visible = true;

            if (!_settings.Welcomed)
            {
                Balloon("Quiet PC is running", "It lives here in the tray. Right-click for options, double-click to switch quiet mode on or off.");
                _settings.Welcomed = true;
                _settings.Save();
            }

            _timer.Interval = _settings.IntervalSec * 1000;
            _timer.Tick += (s, e) => OnTick();
            _timer.Start();

            Application.ApplicationExit += (s, e) => Cleanup();
        }

        private void OnTick()
        {
            try
            {
                string fg = ForegroundName();
                if (_settings.EdgeSaver) EdgeSaver.Tick(fg, DateTime.UtcNow);
                _throttler.Tick(_settings.QuietMode, _settings.CpuThreshold, fg, _settings.IntervalSec);
            }
            catch (Exception ex) { Log.Write("error: " + ex.Message); }
            UpdateTray();
        }

        private static string ForegroundName()
        {
            int pid = Native.ForegroundPid();
            if (pid <= 0) return null;
            try { using (var p = Process.GetProcessById(pid)) return p.ProcessName; }
            catch { return null; }
        }

        private void SyncTurbo()
        {
            if (_settings.QuietMode && _settings.CapTurbo) Turbo.Cap(); else Turbo.Restore();
        }

        private void SyncEdge()
        {
            if (_settings.EdgeSaver) EdgeSaver.ApplyPolicies(); else EdgeSaver.RestorePolicies();
        }

        private void ToggleQuiet()
        {
            _settings.QuietMode = !_settings.QuietMode;
            _settings.Save();
            if (!_settings.QuietMode) _throttler.ReleaseAll();
            SyncTurbo();
            UpdateTray();
            if (_settings.QuietMode) Balloon("Quiet mode is on", "Background apps are being throttled to keep the fans down.");
            else Balloon("Quiet mode is off", "Everything is back to full speed.");
        }

        private void ToggleTurbo()
        {
            _settings.CapTurbo = !_settings.CapTurbo;
            _settings.Save();
            SyncTurbo();
            UpdateTray();
        }

        private void ToggleEdge()
        {
            _settings.EdgeSaver = !_settings.EdgeSaver;
            _settings.Save();
            SyncEdge();
            UpdateTray();
            if (_settings.EdgeSaver)
                Balloon("Edge memory saver is on", "Sleeping tabs, efficiency mode and memory trimming are on. Restart Edge for the tab settings to apply.");
            else
                Balloon("Edge memory saver is off", "Edge is back to its own settings. Restart Edge to apply.");
        }

        private void FreeEdgeNow()
        {
            long mb = EdgeSaver.TrimNow();
            Balloon("Edge memory", mb > 0 ? "Freed about " + mb + " MB from Edge." : "Edge isn't running or had nothing to free.");
        }

        private void ShowThrottled()
        {
            string body;
            if (!_settings.QuietMode) body = "Quiet mode is off.";
            else if (_throttler.Count == 0) body = "Nothing is hogging the CPU right now.";
            else
            {
                var lines = _throttler.ThrottledNames
                    .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Count() > 1 ? "- " + g.Key + " (x" + g.Count() + ")" : "- " + g.Key);
                body = "Throttled right now:\n\n" + string.Join("\n", lines);
            }
            if (_settings.QuietMode) body += "\n\nTurbo Boost: " + (Turbo.IsCapped ? "capped" : "normal");
            body += "\nEdge memory saver: " + (_settings.EdgeSaver ? "on" : "off");
            MessageBox.Show(body, "Quiet PC " + Installer.Version, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static void OpenInNotepad(string path)
        {
            try
            {
                if (!File.Exists(path)) File.WriteAllText(path, "");
                Process.Start("notepad.exe", "\"" + path + "\"");
            }
            catch { }
        }

        private void UpdateTray()
        {
            _miQuiet.Checked = _settings.QuietMode;
            _miTurbo.Checked = _settings.CapTurbo;
            _miTurbo.Enabled = _settings.QuietMode;
            _miEdge.Checked = _settings.EdgeSaver;

            string text;
            if (_settings.QuietMode)
            {
                _tray.Icon = _iconOn;
                text = "Quiet PC: ON - " + _throttler.Count + " app(s) throttled";
                if (Turbo.IsCapped) text += ", Turbo capped";
            }
            else
            {
                _tray.Icon = _iconOff;
                text = "Quiet PC: OFF";
            }
            _tray.Text = text.Length > 63 ? text.Substring(0, 63) : text;
        }

        private void Balloon(string title, string text)
        {
            _tray.ShowBalloonTip(3000, title, text, ToolTipIcon.Info);
        }

        private void Cleanup()
        {
            if (_stopped) return;
            _stopped = true;
            _timer.Stop();
            _throttler.ReleaseAll();
            Turbo.Restore();
            try { File.Delete(Paths.ThrottledState); } catch { }
            Log.Write("quit");
            _tray.Visible = false;
        }

        private void Quit()
        {
            Cleanup();
            _tray.Dispose();
            ExitThread();
        }

        private void UninstallFromTray()
        {
            var r = MessageBox.Show("Remove Quiet PC and put everything back to normal?", "Quiet PC",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;
            Cleanup();
            Installer.Uninstall(true);
            MessageBox.Show("Quiet PC has been removed. Turbo Boost, app priorities and Edge settings are back to normal.",
                "Quiet PC", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _tray.Dispose();
            ExitThread();
        }

        // A round icon with three white fan blades.
        private static Icon MakeIcon(Color color)
        {
            using (var bmp = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bmp))
                using (var fill = new SolidBrush(color))
                using (var white = new SolidBrush(Color.White))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    g.FillEllipse(fill, 1, 1, 30, 30);
                    foreach (int angle in new[] { 0, 120, 240 })
                    {
                        g.TranslateTransform(16, 16);
                        g.RotateTransform(angle);
                        g.FillEllipse(white, -3.5f, -13, 7, 12);
                        g.ResetTransform();
                    }
                    g.FillEllipse(fill, 12, 12, 8, 8);
                }
                IntPtr h = bmp.GetHicon();
                Icon icon = (Icon)Icon.FromHandle(h).Clone();
                Native.DestroyIcon(h);
                return icon;
            }
        }
    }
}
