using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SoundboySetup
{
    static class Program
    {
        /// <summary>
        /// SOUNDBOY-Setup.exe                 interactive wizard
        /// SOUNDBOY-Setup.exe /S              silent install (exit code 0 = success)
        ///   /D=&lt;folder&gt;                 install location (default %LOCALAPPDATA%\Programs\SOUNDBOY)
        ///   /NODESKTOP                        no desktop shortcut
        ///   /NOOPENWITH                       don't add SOUNDBOY to "Open with"
        ///   /LAUNCH                           start SOUNDBOY afterwards
        /// </summary>
        [STAThread]
        static int Main(string[] args)
        {
            bool Has(string flag) => args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

            if (Has("/S"))
            {
                var o = new InstallOptions
                {
                    Dir = Product.ExistingInstallDir() ?? Product.DefaultInstallDir,
                    DesktopShortcut = !Has("/NODESKTOP"),
                    OpenWith = !Has("/NOOPENWITH"),
                };
                var d = args.FirstOrDefault(a => a.StartsWith("/D=", StringComparison.OrdinalIgnoreCase));
                if (d != null) o.Dir = Path.GetFullPath(d.Substring(3).Trim('"'));
                try
                {
                    Installer.Run(o, (f, s) => { });
                    if (Has("/LAUNCH")) Installer.Launch(o.Dir);
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    return 1;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
            return 0;
        }
    }
}
