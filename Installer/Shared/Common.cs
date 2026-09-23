using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SoundboySetup
{
    /// <summary>Everything the setup and the uninstaller agree on.</summary>
    static class Product
    {
        public const string Name = "SOUNDBOY";
        public const string Version = "2.1.0";
        public const string Publisher = "SOUNDBOY";
        public const string ExeName = "SOUNDBOY.exe";
        public const string UninstallerName = "uninstall.exe";
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SOUNDBOY";
        public const string AppKey = @"Software\Classes\Applications\SOUNDBOY.exe";

        public static readonly string[] OpenWithExtensions =
        {
            ".mp3", ".mp2", ".wav", ".flac", ".ogg", ".oga", ".m4a", ".m4b", ".aac", ".wma", ".aif", ".aiff", ".ac3",
            ".m3u", ".m3u8", ".pls",
        };

        public static string DefaultInstallDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "SOUNDBOY");

        public static string StartMenuLink =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "SOUNDBOY.lnk");

        public static string DesktopLink =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "SOUNDBOY.lnk");

        public static string UserDataDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SOUNDBOY");

        /// <summary>Where a single-file build unpacks its native libraries on first run.</summary>
        public static string ExtractCacheDir =>
            Path.Combine(Path.GetTempPath(), ".net", "SOUNDBOY");

        /// <summary>Install folder of an existing installation, if any.</summary>
        public static string? ExistingInstallDir()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(UninstallKey))
                return k?.GetValue("InstallLocation") as string;
        }
    }

    static class Sys
    {
        public static string Quote(string s) => "\"" + s + "\"";

        public static void CreateShortcut(string lnkPath, string target, string description)
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(type)!;
            try
            {
                dynamic lnk = shell.CreateShortcut(lnkPath);
                lnk.TargetPath = target;
                lnk.WorkingDirectory = Path.GetDirectoryName(target);
                lnk.IconLocation = target + ",0";
                lnk.Description = description;
                lnk.Save();
                Marshal.FinalReleaseComObject(lnk);
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }

        public static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        /// <summary>Only removes shortcuts that point into <paramref name="installDir"/>.</summary>
        public static void DeleteShortcutIfOurs(string lnkPath, string installDir)
        {
            if (!File.Exists(lnkPath)) return;
            try
            {
                var type = Type.GetTypeFromProgID("WScript.Shell");
                dynamic shell = Activator.CreateInstance(type)!;
                string target = shell.CreateShortcut(lnkPath).TargetPath;
                Marshal.FinalReleaseComObject(shell);
                if (target.StartsWith(installDir, StringComparison.OrdinalIgnoreCase)) File.Delete(lnkPath);
            }
            catch { }
        }

        // ---------------------------------------------------------------- running app

        public static Process[] RunningApps() => Process.GetProcessesByName("SOUNDBOY");

        /// <summary>Asks every SOUNDBOY window to close (it saves its playlist), then waits.</summary>
        public static bool CloseRunningApps(int timeoutMs)
        {
            foreach (var p in RunningApps())
                foreach (var h in VisibleWindowsOf(p.Id))
                    PostMessage(h, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
            var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < until)
            {
                if (RunningApps().Length == 0) return true;
                Thread.Sleep(200);
            }
            return RunningApps().Length == 0;
        }

        static IntPtr[] VisibleWindowsOf(int pid)
        {
            var list = new System.Collections.Generic.List<IntPtr>();
            EnumWindows((h, _) =>
            {
                GetWindowThreadProcessId(h, out uint owner);
                if (owner == pid && IsWindowVisible(h))
                {
                    var cls = new StringBuilder(256);
                    GetClassName(h, cls, cls.Capacity);
                    if (cls.ToString().StartsWith("WindowsForms10.Window")) list.Add(h);
                }
                return true;
            }, IntPtr.Zero);
            return list.ToArray();
        }

        // ---------------------------------------------------------------- registry

        public static void RegisterUninstall(string dir, long sizeKb)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(Product.UninstallKey)!)
            {
                string uninstaller = Path.Combine(dir, Product.UninstallerName);
                k.SetValue("DisplayName", Product.Name);
                k.SetValue("DisplayVersion", Product.Version);
                k.SetValue("Publisher", Product.Publisher);
                k.SetValue("DisplayIcon", Path.Combine(dir, Product.ExeName) + ",0");
                k.SetValue("InstallLocation", dir);
                k.SetValue("UninstallString", Quote(uninstaller));
                k.SetValue("QuietUninstallString", Quote(uninstaller) + " /S");
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                k.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, sizeKb), RegistryValueKind.DWord);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }

        public static void UnregisterUninstall()
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(Product.UninstallKey, false); } catch { }
        }

        /// <summary>Lists SOUNDBOY under "Open with" for music files without changing the default app.</summary>
        public static void RegisterOpenWith(string exe)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(Product.AppKey)!)
            {
                k.SetValue("FriendlyAppName", Product.Name);
                using (var icon = k.CreateSubKey("DefaultIcon")!) icon.SetValue("", exe + ",0");
                using (var cmd = k.CreateSubKey(@"shell\open\command")!) cmd.SetValue("", Quote(exe) + " \"%1\"");
                using (var types = k.CreateSubKey("SupportedTypes")!)
                    foreach (var ext in Product.OpenWithExtensions) types.SetValue(ext, "");
            }
            foreach (var ext in Product.OpenWithExtensions)
                using (Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ext}\OpenWithList\{Product.ExeName}")) { }
            SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, IntPtr.Zero, IntPtr.Zero);
        }

        public static void UnregisterOpenWith()
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(Product.AppKey, false); } catch { }
            foreach (var ext in Product.OpenWithExtensions)
            {
                try { Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ext}\OpenWithList\{Product.ExeName}", false); } catch { }
            }
            SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
        }

        // ---------------------------------------------------------------- interop

        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, int m, IntPtr w, IntPtr l);
        [DllImport("shell32.dll")] static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);
    }

    /// <summary>SOUNDBOY palette (same tokens as the app / Figma file).</summary>
    static class Theme
    {
        public static readonly Color Window = Color.FromArgb(0x0F, 0x0F, 0x14);
        public static readonly Color Surface = Color.FromArgb(0x17, 0x17, 0x1F);
        public static readonly Color Raised = Color.FromArgb(0x22, 0x22, 0x2D);
        public static readonly Color Text = Color.FromArgb(0xF4, 0xF4, 0xF8);
        public static readonly Color Muted = Color.FromArgb(0x8B, 0x8B, 0xA3);
        public static readonly Color Accent = Color.FromArgb(0xC8, 0xFF, 0x3D);
        public static readonly Color Ink = Color.FromArgb(0x0F, 0x0F, 0x14);

        public static Button Button(string text, bool primary)
        {
            var b = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : Raised,
                ForeColor = primary ? Ink : Text,
                Font = new Font("Segoe UI Semibold", 9.75f),
                Size = new Size(110, 34),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(0xD8, 0xFF, 0x70) : Color.FromArgb(0x2C, 0x2C, 0x3C);
            b.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(0xA8, 0xD8, 0x30) : Color.FromArgb(0x33, 0x33, 0x45);
            return b;
        }

        public static CheckBox Check(string text, bool on) => new CheckBox
        {
            Text = text,
            Checked = on,
            AutoSize = true,
            ForeColor = Text,
            Font = new Font("Segoe UI", 9.75f),
            FlatStyle = FlatStyle.Standard,
            Cursor = Cursors.Hand,
        };

        public static Label Label(string text, float size = 9.75f, bool muted = false, bool bold = false) => new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = muted ? Muted : Text,
            Font = new Font(bold ? "Segoe UI Semibold" : "Segoe UI", size),
            BackColor = Color.Transparent,
        };

        public static Image? Logo()
        {
            using (var s = typeof(Theme).Assembly.GetManifestResourceStream("setup.logo.png"))
                return s == null ? null : new Bitmap(Image.FromStream(s));
        }

        public static Icon? AppIcon()
        {
            try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { return null; }
        }
    }
}
