using System.IO.Pipes;
using SOUNDBOY.Model;

namespace SOUNDBOY;

static class Program
{
    public const string Version = "2.1";
    static string ProfileSuffix => AppSettings.Profile.Length == 0 ? "" : "." + AppSettings.Profile;
    public static string PipeName => "SOUNDBOY.Pipe" + ProfileSuffix + "." + Environment.UserName;

    [STAThread]
    static void Main(string[] args)
    {
        args = args.Select(a => MediaFiles.IsUrl(a) || a.StartsWith('/') ? a : SafeFullPath(a)).ToArray();

        // An update swaps the running exe aside as *.old.exe; remove it once that copy has exited.
        try { File.Delete(Path.ChangeExtension(Environment.ProcessPath!, ".old.exe")); } catch { }

        using var mutex = new Mutex(true, @"Local\SOUNDBOY.SingleInstance" + ProfileSuffix, out bool first);
        if (!first && SingleInstance.TrySend(args)) return;

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
            MessageBox.Show(e.Exception.Message, "SOUNDBOY", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        Application.Run(new UI.MainForm(args));
    }

    static string SafeFullPath(string p)
    {
        try { return Path.GetFullPath(p); } catch { return p; }
    }
}

/// <summary>Forwards command-line files to an already running instance over a named pipe.</summary>
static class SingleInstance
{
    public static bool TrySend(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", Program.PipeName, PipeDirection.Out);
            client.Connect(1500);
            using var w = new StreamWriter(client);
            bool enqueue = args.Any(a => a.Equals("/add", StringComparison.OrdinalIgnoreCase));
            w.WriteLine(args.Length == 0 ? "ACTIVATE" : enqueue ? "ADD" : "OPEN");
            foreach (var a in args.Where(a => !a.StartsWith('/'))) w.WriteLine(a);
            w.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void StartServer(Action<string[]> onMessage)
    {
        new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(Program.PipeName, PipeDirection.In, 1);
                    server.WaitForConnection();
                    using var r = new StreamReader(server);
                    var lines = new List<string>();
                    while (r.ReadLine() is { } line) lines.Add(line);
                    if (lines.Count > 0) onMessage(lines.ToArray());
                }
                catch
                {
                    Thread.Sleep(250);
                }
            }
        }) { IsBackground = true, Name = "SOUNDBOY pipe" }.Start();
    }
}
