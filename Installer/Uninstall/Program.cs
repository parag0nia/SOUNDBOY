using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SoundboySetup
{
    /// <summary>
    /// uninstall.exe          asks for confirmation (and whether to delete settings/playlists)
    /// uninstall.exe /S       silent; keeps user data
    /// uninstall.exe /S /PURGE  silent; also deletes %APPDATA%\SOUNDBOY
    /// </summary>
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            bool Has(string flag) => args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
            bool silent = Has("/S");
            bool purge = Has("/PURGE");
            string dir = Path.GetDirectoryName(Application.ExecutablePath)!;

            if (!silent)
            {
                Application.EnableVisualStyles();
                if (MessageBox.Show("Remove SOUNDBOY from this computer?", "Uninstall SOUNDBOY",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return 1;
                purge = Directory.Exists(Product.UserDataDir) &&
                        MessageBox.Show("Also delete your SOUNDBOY settings, playlist, EQ presets and BPM/key cache?\n\n" +
                                        "Choose No to keep them for a future reinstall.", "Uninstall SOUNDBOY",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
            }

            if (Sys.RunningApps().Length > 0 && !Sys.CloseRunningApps(8000))
            {
                if (!silent) MessageBox.Show("SOUNDBOY is still running. Close it and run the uninstaller again.", "Uninstall SOUNDBOY",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 2;
            }

            Sys.DeleteShortcutIfOurs(Product.StartMenuLink, dir);
            Sys.DeleteShortcutIfOurs(Product.DesktopLink, dir);
            Sys.UnregisterOpenWith();
            Sys.UnregisterUninstall();
            Sys.TryDelete(Path.Combine(dir, Product.ExeName));
            try { if (Directory.Exists(Product.ExtractCacheDir)) Directory.Delete(Product.ExtractCacheDir, true); } catch { }
            if (purge)
            {
                try { if (Directory.Exists(Product.UserDataDir)) Directory.Delete(Product.UserDataDir, true); } catch { }
            }

            // This exe can't delete itself while running: let a hidden cmd finish the job after we exit.
            // Only our own file is deleted, and the folder only if it is then empty.
            string self = Path.Combine(dir, Product.UninstallerName);
            Process.Start(new ProcessStartInfo("cmd.exe",
                $"/c ping 127.0.0.1 -n 3 > nul & del /f /q {Sys.Quote(self)} & rmdir {Sys.Quote(dir)}")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
            });

            if (!silent) MessageBox.Show("SOUNDBOY was removed.", "Uninstall SOUNDBOY", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
    }
}
