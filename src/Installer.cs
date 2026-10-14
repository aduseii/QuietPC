using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace QuietPC
{
    /// <summary>
    /// Installs Quiet PC to Program Files and registers a sign-in task that starts it with
    /// admin rights (needed to throttle system processes and change the power plan)
    /// without a UAC prompt every time.
    /// </summary>
    internal static class Installer
    {
        public const string TaskName = "Quiet PC";
        private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\QuietPC";

        public static string CurrentExe { get { return Assembly.GetExecutingAssembly().Location; } }

        public static bool IsRunningFromInstallDir
        {
            get
            {
                return string.Equals(Path.GetFullPath(CurrentExe), Path.GetFullPath(Paths.InstallExe),
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        public static bool IsInstalled { get { return File.Exists(Paths.InstallExe); } }

        public static void Install()
        {
            StopOtherInstances();

            Directory.CreateDirectory(Paths.InstallDir);
            File.Copy(CurrentExe, Paths.InstallExe, true);

            Paths.EnsureDataDir();
            Exclusions.EnsureFile();
            if (!File.Exists(Paths.Settings)) new Settings().Save();

            RegisterTask();
            CreateShortcut();
            RegisterUninstallEntry();
            Log.Write("installed " + Version);

            RunTask();
        }

        /// <summary>Puts everything back and removes Quiet PC. Call from the uninstaller or the tray.</summary>
        public static void Uninstall(bool calledFromTray)
        {
            if (!calledFromTray) StopOtherInstances();

            Throttler.RestoreFromStateFile();
            Turbo.Restore();
            EdgeSaver.RestorePolicies();

            Schtasks("/delete /tn \"" + TaskName + "\" /f");
            try { File.Delete(Paths.Shortcut); } catch { }
            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                    hklm.DeleteSubKeyTree(UninstallKey, false);
            }
            catch { }

            // The running exe can't delete itself, so a hidden cmd removes the folders a moment later.
            string cmd = string.Format("/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"{0}\" & rmdir /s /q \"{1}\"",
                Paths.InstallDir, Paths.DataDir);
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe", cmd)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetTempPath()
                });
            }
            catch { }
        }

        public static string Version
        {
            get { return Assembly.GetExecutingAssembly().GetName().Version.ToString(3); }
        }

        private static void StopOtherInstances()
        {
            int self = Process.GetCurrentProcess().Id;
            foreach (var p in Process.GetProcessesByName("QuietPC"))
            {
                using (p)
                {
                    if (p.Id == self) continue;
                    try { p.Kill(); p.WaitForExit(5000); } catch { }
                }
            }
        }

        private static void RegisterTask()
        {
            string user = WindowsIdentity.GetCurrent().Name;
            string xml =
@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>Keeps fans quiet by throttling background apps.</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <UserId>" + SecurityElement.Escape(user) + @"</UserId>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>" + SecurityElement.Escape(user) + @"</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>6</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>""" + SecurityElement.Escape(Paths.InstallExe) + @"""</Command>
    </Exec>
  </Actions>
</Task>";
            string xmlPath = Path.Combine(Path.GetTempPath(), "QuietPC-task.xml");
            File.WriteAllText(xmlPath, xml, Encoding.Unicode);
            string result = Schtasks("/create /tn \"" + TaskName + "\" /xml \"" + xmlPath + "\" /f");
            try { File.Delete(xmlPath); } catch { }
            Log.Write("task: " + result.Trim());
        }

        public static void RunTask()
        {
            Schtasks("/run /tn \"" + TaskName + "\"");
        }

        private static string Schtasks(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe", args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                    p.WaitForExit(15000);
                    return o;
                }
            }
            catch (Exception ex) { return ex.Message; }
        }

        // Start Menu shortcut that starts the task, so reopening Quiet PC needs no UAC prompt.
        private static void CreateShortcut()
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(shellType);
                object lnk = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell,
                    new object[] { Paths.Shortcut });
                Type t = lnk.GetType();
                Set(t, lnk, "TargetPath", Path.Combine(Environment.SystemDirectory, "schtasks.exe"));
                Set(t, lnk, "Arguments", "/run /tn \"" + TaskName + "\"");
                Set(t, lnk, "WindowStyle", 7);
                Set(t, lnk, "IconLocation", Paths.InstallExe + ",0");
                Set(t, lnk, "Description", "Start Quiet PC");
                t.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
            }
            catch (Exception ex) { Log.Write("shortcut failed: " + ex.Message); }
        }

        private static void Set(Type t, object o, string prop, object value)
        {
            t.InvokeMember(prop, BindingFlags.SetProperty, null, o, new[] { value });
        }

        // Shows Quiet PC in Settings > Apps so it can be removed like any other app.
        private static void RegisterUninstallEntry()
        {
            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var k = hklm.CreateSubKey(UninstallKey, true))
                {
                    k.SetValue("DisplayName", "Quiet PC");
                    k.SetValue("DisplayVersion", Version);
                    k.SetValue("Publisher", "aduseii");
                    k.SetValue("DisplayIcon", Paths.InstallExe + ",0");
                    k.SetValue("InstallLocation", Paths.InstallDir);
                    k.SetValue("UninstallString", "\"" + Paths.InstallExe + "\" --uninstall");
                    k.SetValue("URLInfoAbout", "https://github.com/aduseii/QuietPC");
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                }
            }
            catch (Exception ex) { Log.Write("uninstall entry failed: " + ex.Message); }
        }
    }
}
