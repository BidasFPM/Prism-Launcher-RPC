using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PrismRpc;

internal static class Program
{
    public const string AppName = "PrismRPC";
    public const string Version = "1.6";
    private const string MutexName = @"Local\PrismRPC_Mutex";

    [DllImport("kernel32.dll")] private static extern bool AttachConsole(int dwProcessId);
    [DllImport("kernel32.dll")] private static extern bool AllocConsole();
    private const int ATTACH_PARENT_PROCESS = -1;

    [STAThread]
    private static void Main(string[] args)
    {
        bool debug  = args.Contains("--debug");
        bool test   = args.Contains("--test");
        bool follow = args.Contains("--follow-prism");
        bool noTray = args.Contains("--no-tray");

        if (debug || test)
        {
            if (!AttachConsole(ATTACH_PARENT_PROCESS)) AllocConsole();
            Console.OutputEncoding = System.Text.Encoding.UTF8;
        }

        ApplicationConfiguration.Initialize();

        var baseDir   = AppContext.BaseDirectory;
        var configDir = Path.Combine(baseDir, "config");
        Directory.CreateDirectory(configDir);
        var cfgPath = Path.Combine(configDir, "config.json");
        var logPath = Path.Combine(configDir, "prism_rpc.log");

        Log.Init(logPath, debug);
        Log.Info($"PrismRPC v{Version} | args: [{string.Join(' ', args)}] | base: {baseDir}");
        Log.Info($"Config: {cfgPath}");
        Log.Info($"Log:    {logPath}");

        var cfg = ConfigLoader.Load(cfgPath, out var cfgError);
        if (cfgError != null)
        {
            Log.Error($"Invalid config.json: {cfgError}");
            ShowError($"config.json has a syntax error:\n\n{cfgError}\n\nFile:\n{cfgPath}\n\nFix it (quotes, commas) and start again.");
            OpenInNotepad(cfgPath);
            return;
        }
        if (string.IsNullOrEmpty(cfg.ClientId) || !cfg.ClientId.All(char.IsDigit))
        {
            Log.Error($"Invalid client_id: {cfg.ClientId}");
            ShowError($"\"client_id\" must be your Discord Application ID (numbers only).\n\nCurrent value: \"{cfg.ClientId}\"\n\nFile:\n{cfgPath}");
            OpenInNotepad(cfgPath);
            return;
        }

        Log.Info($"Config loaded: client_id={cfg.ClientId}, poll={cfg.PollSeconds}s, " +
                 $"show.server_address={cfg.Show.ServerAddress}, privacy.hide_ip_servers={cfg.Privacy.HideIpServers}");

        if (test) { RunTest(cfg); return; }

        using var mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            Log.Info("Another Prism RPC instance is already running - exiting");
            return;
        }

        var worker = new Worker(cfg, cfgPath, follow);
        worker.Start();

        if (noTray)
        {
            while (worker.IsRunning) Thread.Sleep(1000);
            return;
        }

        using var tray = new TrayApp(worker, cfgPath, logPath);
        worker.OnExit += () => tray.RequestStop();
        Application.Run(tray);

        worker.Stop();
        worker.Join(3000);
        Log.Info("Exited");
    }

    public static void ShowError(string msg) =>
        MessageBox.Show(msg, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);

    public static void ShowInfo(string msg) =>
        MessageBox.Show(msg, AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);

    public static void OpenInNotepad(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"")
            {
                UseShellExecute = true
            });
        }
        catch
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
        }
    }

    private static void RunTest(Config cfg)
    {
        Console.WriteLine("Prism RPC connection test");
        Log.Info("Test mode: connecting to Discord...");
        try
        {
            using var rpc = new DiscordRPC.DiscordRpcClient(cfg.ClientId.Trim());
            rpc.Initialize();
            Console.WriteLine("[OK] Connected to Discord");
            Log.Info("Connected to Discord");

            rpc.SetPresence(new DiscordRPC.RichPresence
            {
                Details    = "Prism RPC test",
                State      = "If you see this, Discord works",
                Timestamps = DiscordRPC.Timestamps.Now,
                Assets     = new DiscordRPC.Assets
                {
                    LargeImageKey  = cfg.Assets.Launcher,
                    LargeImageText = "Test"
                }
            });
            Console.WriteLine("[OK] Presence sent - check your Discord profile now (kept for 60s)");
            Log.Info("Test presence sent");
            ShowInfo("Prism RPC connection test\n\n[OK] Connected to Discord\n" +
                     "[OK] Presence sent - check your Discord profile now.\n\nPresence kept for 60 seconds.");
            Thread.Sleep(60_000);
            rpc.ClearPresence();
        }
        catch (Exception ex)
        {
            Console.WriteLine("[FAIL] " + ex);
            Log.Exception("Test failed", ex);
            ShowError("Prism RPC connection test\n\n[FAIL]\n\n" + ex.Message);
        }
    }
}
