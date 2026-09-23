using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace SoundboySetup
{
    /// <summary>Three-page wizard: options → progress → done.</summary>
    sealed class SetupForm : Form
    {
        readonly Panel options = new Panel(), working = new Panel(), done = new Panel();
        readonly TextBox dirBox;
        readonly CheckBox desktop, openWith, launch;
        readonly Label status;
        readonly ProgressLine bar = new ProgressLine();
        readonly Button installBtn;
        readonly Image? logo = Theme.Logo();
        readonly bool isUpdate;

        public SetupForm()
        {
            Text = "SOUNDBOY Setup";
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(560, 380);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Window;
            ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 9.75f);
            Icon = Theme.AppIcon();
            DoubleBuffered = true;

            string? existing = Product.ExistingInstallDir();
            isUpdate = existing != null && File.Exists(Path.Combine(existing, Product.ExeName));

            foreach (var p in new[] { options, working, done })
            {
                p.SetBounds(0, 110, 560, 270);
                p.BackColor = Theme.Window;
                Controls.Add(p);
            }

            // ---- options page
            options.Controls.Add(At(Theme.Label(isUpdate
                ? "A previous SOUNDBOY installation was found. Setup will update it; your playlist and settings are kept."
                : "SOUNDBOY will be installed for your user account. No administrator rights needed.", 9.75f, muted: true), 32, 4, 496));
            options.Controls.Add(At(Theme.Label("Install location", 9.75f, bold: true), 32, 50));
            dirBox = new TextBox
            {
                Text = existing ?? Product.DefaultInstallDir,
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9.75f),
            };
            dirBox.SetBounds(32, 74, 392, 26);
            options.Controls.Add(dirBox);
            var browse = Theme.Button("Browse…", false);
            browse.SetBounds(432, 72, 96, 30);
            browse.Click += (_, __) => Browse();
            options.Controls.Add(browse);

            desktop = Theme.Check("Create a desktop shortcut", true);
            openWith = Theme.Check("Add SOUNDBOY to “Open with” for music files", true);
            options.Controls.Add(At(desktop, 32, 118));
            options.Controls.Add(At(openWith, 32, 146));
            long mb = Math.Max(1, Installer.PayloadBytes / (1024 * 1024));
            options.Controls.Add(At(Theme.Label($"Requires about {mb} MB of disk space. A Start menu shortcut is always created.", 8.75f, muted: true), 32, 182));

            var cancel = Theme.Button("Cancel", false);
            cancel.SetBounds(300, 216, 110, 34);
            cancel.Click += (_, __) => Close();
            installBtn = Theme.Button(isUpdate ? "Update" : "Install", true);
            installBtn.SetBounds(418, 216, 110, 34);
            installBtn.Click += (_, __) => StartInstall();
            options.Controls.Add(cancel);
            options.Controls.Add(installBtn);
            AcceptButton = installBtn;
            CancelButton = cancel;

            // ---- working page
            status = Theme.Label("Preparing…", 9.75f);
            working.Controls.Add(At(status, 32, 40));
            bar.SetBounds(32, 72, 496, 6);
            working.Controls.Add(bar);

            // ---- done page
            done.Controls.Add(At(Theme.Label(isUpdate ? "SOUNDBOY has been updated." : "SOUNDBOY is installed.", 13f, bold: true), 32, 16));
            done.Controls.Add(At(Theme.Label("Find it in the Start menu" + " — or remove it any time from Settings › Apps.", 9.75f, muted: true), 32, 52, 496));
            launch = Theme.Check("Launch SOUNDBOY now", true);
            done.Controls.Add(At(launch, 32, 96));
            var finish = Theme.Button("Finish", true);
            finish.SetBounds(418, 216, 110, 34);
            finish.Click += (_, __) =>
            {
                if (launch.Checked) Installer.Launch(dirBox.Text.Trim());
                Close();
            };
            done.Controls.Add(finish);

            Show(options);
            ActiveControl = installBtn;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Dark title bar to match the window (Windows 10 20H1+ / 11; ignored elsewhere).
            int on = 1;
            if (DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(Handle, 19, ref on, sizeof(int));
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        static Control At(Control c, int x, int y, int maxWidth = 0)
        {
            c.Location = new Point(x, y);
            if (maxWidth > 0 && c is Label l) { l.AutoSize = false; l.Size = new Size(maxWidth, 40); }
            return c;
        }

        void Show(Panel p)
        {
            options.Visible = p == options;
            working.Visible = p == working;
            done.Visible = p == done;
            if (p == done) AcceptButton = done.Controls.OfType<Button>().First();
        }

        void Browse()
        {
            using (var d = new FolderBrowserDialog { Description = "Choose where to install SOUNDBOY", SelectedPath = dirBox.Text })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                var path = d.SelectedPath;
                // Keep installs tidy: picking a parent folder installs into a SOUNDBOY subfolder.
                if (!Path.GetFileName(path).Equals("SOUNDBOY", StringComparison.OrdinalIgnoreCase)) path = Path.Combine(path, "SOUNDBOY");
                dirBox.Text = path;
            }
        }

        void StartInstall()
        {
            string dir = dirBox.Text.Trim();
            try { dir = Path.GetFullPath(dir); }
            catch { MessageBox.Show(this, "That install location isn't a valid folder path.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            dirBox.Text = dir;

            if (Sys.RunningApps().Length > 0 &&
                MessageBox.Show(this, "SOUNDBOY is running. Setup will close it (your playlist is saved) and continue.",
                    Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
                return;

            var opts = new InstallOptions { Dir = dir, DesktopShortcut = desktop.Checked, OpenWith = openWith.Checked };
            Show(working);
            ControlBox = false;
            new Thread(() =>
            {
                try
                {
                    Installer.Run(opts, (f, s) => BeginInvoke(new Action(() => { bar.Value = f; status.Text = s; })));
                    BeginInvoke(new Action(() => { ControlBox = true; Show(done); }));
                }
                catch (Exception ex)
                {
                    BeginInvoke(new Action(() =>
                    {
                        ControlBox = true;
                        MessageBox.Show(this, "Setup couldn't finish:\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        Show(options);
                    }));
                }
            }) { IsBackground = true }.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            float s = DeviceDpi / 96f;
            if (logo != null)
            {
                float h = 60 * s, w = logo.Width * h / logo.Height;
                g.DrawImage(logo, 32 * s, 28 * s, w, h);
            }
            using (var title = new Font("Segoe UI", 20f, FontStyle.Bold))
            using (var sub = new Font("Segoe UI", 9.75f))
            using (var tb = new SolidBrush(Theme.Text))
            using (var mb = new SolidBrush(Theme.Muted))
            {
                g.DrawString("SOUNDBOY", title, tb, 104 * s, 30 * s);
                g.DrawString($"Version {Product.Version}  ·  music player", sub, mb, 107 * s, 68 * s);
            }
            using (var line = new Pen(Theme.Raised)) g.DrawLine(line, 0, 106 * s, ClientSize.Width, 106 * s);
        }

        /// <summary>Thin rounded progress bar in the accent color.</summary>
        sealed class ProgressLine : Control
        {
            double value;
            public double Value { get => value; set { this.value = Math.Max(0, Math.Min(1, value)); Invalidate(); } }
            public ProgressLine() { DoubleBuffered = true; BackColor = Theme.Window; }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Fill(g, Theme.Raised, new RectangleF(0, 0, Width, Height));
                if (value > 0) Fill(g, Theme.Accent, new RectangleF(0, 0, (float)(Width * value), Height));
            }

            static void Fill(Graphics g, Color c, RectangleF r)
            {
                if (r.Width < 1) return;
                float d = Math.Min(r.Height, r.Width);
                using (var p = new GraphicsPath())
                using (var b = new SolidBrush(c))
                {
                    p.AddArc(r.X, r.Y, d, d, 90, 180);
                    p.AddArc(r.Right - d, r.Y, d, d, 270, 180);
                    p.CloseFigure();
                    g.FillPath(b, p);
                }
            }
        }
    }
}
