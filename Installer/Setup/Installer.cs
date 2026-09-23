using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace SoundboySetup
{
    sealed class InstallOptions
    {
        public string Dir = Product.DefaultInstallDir;
        public bool DesktopShortcut = true;
        public bool OpenWith = true;
    }

    /// <summary>The actual install steps, shared by the wizard and silent mode.</summary>
    static class Installer
    {
        public static long PayloadBytes
        {
            get
            {
                using (var s = typeof(Installer).Assembly.GetManifestResourceStream("payload.SOUNDBOY.exe"))
                    return s?.Length ?? 0;
            }
        }

        /// <param name="progress">(fraction 0..1, status text)</param>
        public static void Run(InstallOptions o, Action<double, string> progress)
        {
            progress(0.02, "Checking for a running SOUNDBOY…");
            if (Sys.RunningApps().Length > 0 && !Sys.CloseRunningApps(8000))
                throw new InvalidOperationException("SOUNDBOY is still running. Close it and try again.");

            Directory.CreateDirectory(o.Dir);
            string exe = Path.Combine(o.Dir, Product.ExeName);

            progress(0.05, "Copying SOUNDBOY…");
            Extract("payload.SOUNDBOY.exe", exe, f => progress(0.05 + f * 0.8, "Copying SOUNDBOY…"));
            Extract("payload.uninstall.exe", Path.Combine(o.Dir, Product.UninstallerName), null);

            // A new build must not reuse native libraries unpacked by an older one.
            try { if (Directory.Exists(Product.ExtractCacheDir)) Directory.Delete(Product.ExtractCacheDir, true); } catch { }

            progress(0.88, "Creating shortcuts…");
            Directory.CreateDirectory(Path.GetDirectoryName(Product.StartMenuLink)!);
            Sys.CreateShortcut(Product.StartMenuLink, exe, "SOUNDBOY music player");
            if (o.DesktopShortcut) Sys.CreateShortcut(Product.DesktopLink, exe, "SOUNDBOY music player");
            else Sys.DeleteShortcutIfOurs(Product.DesktopLink, o.Dir);

            progress(0.94, "Registering with Windows…");
            if (o.OpenWith) Sys.RegisterOpenWith(exe);
            else Sys.UnregisterOpenWith();
            long kb = new DirectoryInfo(o.Dir).GetFiles().Sum(f => f.Length) / 1024;
            Sys.RegisterUninstall(o.Dir, kb);

            progress(1, "Done");
        }

        static void Extract(string resource, string dest, Action<double>? progress)
        {
            using (var src = typeof(Installer).Assembly.GetManifestResourceStream(resource))
            {
                if (src == null) throw new InvalidOperationException("The installer is damaged (missing " + resource + ").");
                string tmp = dest + ".new";
                using (var dst = File.Create(tmp))
                {
                    var buf = new byte[1 << 20];
                    long total = src.Length, done = 0;
                    int n;
                    while ((n = src.Read(buf, 0, buf.Length)) > 0)
                    {
                        dst.Write(buf, 0, n);
                        done += n;
                        progress?.Invoke(total > 0 ? (double)done / total : 1);
                    }
                }
                if (File.Exists(dest)) File.Delete(dest);
                File.Move(tmp, dest);
            }
        }

        public static void Launch(string dir)
        {
            try { Process.Start(new ProcessStartInfo(Path.Combine(dir, Product.ExeName)) { UseShellExecute = true, WorkingDirectory = dir }); }
            catch { }
        }
    }
}
