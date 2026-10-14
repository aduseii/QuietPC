using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace QuietPC
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)))
            {
                Installer.Uninstall(false);
                MessageBox.Show("Quiet PC has been removed. Turbo Boost, app priorities and Edge settings are back to normal.",
                    "Quiet PC", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }

            // Downloaded copy: install (or update) into Program Files, then start the installed copy.
            if (!Installer.IsRunningFromInstallDir)
            {
                string verb = Installer.IsInstalled ? "update" : "install";
                var answer = MessageBox.Show(
                    "Quiet PC keeps your fans quiet by throttling background apps and capping Turbo Boost, " +
                    "and cuts Microsoft Edge's memory use.\n\n" +
                    "It will " + verb + " to Program Files, start every time you sign in, and sit in the system tray. " +
                    "You can turn any part off from the tray icon, and uninstalling puts everything back.\n\n" +
                    (verb == "install" ? "Install" : "Update") + " Quiet PC " + Installer.Version + " now?",
                    "Quiet PC", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer != DialogResult.Yes) return 0;
                try
                {
                    Installer.Install();
                    MessageBox.Show(
                        "Quiet PC is installed and running.\n\nLook for the round teal fan icon in the system tray " +
                        "(click the ^ arrow at the bottom-right if you don't see it). You can delete this downloaded file.",
                        "Quiet PC", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Install failed: " + ex.Message, "Quiet PC", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
                return 0;
            }

            bool created;
            using (var mutex = new Mutex(true, @"Global\QuietPC_Tray", out created))
            {
                if (!created)
                {
                    MessageBox.Show("Quiet PC is already running. Look for its fan icon in the system tray.",
                        "Quiet PC", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }
                Application.Run(new TrayApp());
            }
            return 0;
        }
    }
}
