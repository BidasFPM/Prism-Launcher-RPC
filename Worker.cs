using DiscordRPC;

namespace PrismRpc;

public class Worker
{
    private readonly string _cfgPath;
    private readonly bool _follow;
    private Config _cfg;
    private Thread? _thread;
    private volatile bool _stopRequested;
    private DiscordRpcClient? _rpc;
    private DateTime _cfgMtime;
    private (string? Details, string? LargeText, string? SmallImage, long? StartTicks)? _lastCore;
    private DateTime _lastSent = DateTime.MinValue;
    private bool _seen;
    private DateTime _lastSeen;
    private readonly DateTime _t0 = DateTime.UtcNow;

    public string Status { get; private set; } = "Starting…";
    public volatile bool Paused;
    public bool IsRunning => _thread?.IsAlive == true;
    public event Action? OnExit;

    public Worker(Config cfg, string cfgPath, bool follow)
    {
        _cfg = cfg;
        _cfgPath = cfgPath;
        _follow = follow;
        try { _cfgMtime = File.GetLastWriteTimeUtc(cfgPath); } catch { }
    }

    public void Start()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "RPC Worker" };
        _thread.Start();
    }

    public void Stop() => _stopRequested = true;
    public void Join(int ms = -1) => _thread?.Join(ms);

    private void SetStatus(string s)
    {
        if (Status != s) { Status = s; Log.Info($"Status: {s}"); }
    }

    private bool ReloadIfChanged()
    {
        try
        {
            var m = File.GetLastWriteTimeUtc(_cfgPath);
            if (m == _cfgMtime) return false;
            _cfgMtime = m;
            var newCfg = ConfigLoader.Load(_cfgPath, out var err);
            if (err != null)
            {
                Log.Warn($"config.json has an error, keeping previous: {err}");
                return false;
            }
            bool reconnect = newCfg.ClientId != _cfg.ClientId;
            _cfg = newCfg;
            Log.Info($"Config reloaded: server_address={newCfg.Show.ServerAddress}, hide_ip_servers={newCfg.Privacy.HideIpServers}");
            return reconnect;
        }
        catch { return false; }
    }

    private void Loop()
    {
        while (!_stopRequested)
        {
            try
            {
                if (ReloadIfChanged()) CloseRpc();

                if (Paused)
                {
                    CloseRpc();
                    SetStatus("Paused");
                    Thread.Sleep(2000);
                    continue;
                }

                var (prism, game) = Detection.ScanProcesses();
                try
                {
                    if (_follow)
                    {
                        var now = DateTime.UtcNow;
                        if (prism != null || game != null) { _seen = true; _lastSeen = now; }
                        else if ((_seen && (now - _lastSeen).TotalSeconds > 10) ||
                                 (!_seen && (now - _t0).TotalSeconds > 60))
                        {
                            Log.Info("Prism Launcher closed - exiting");
                            break;
                        }
                    }

                    var payload = PresenceBuilder.Build(_cfg, prism, game);
                    if (payload == null)
                    {
                        CloseRpc();
                        SetStatus("Waiting for Prism Launcher");
                    }
                    else
                    {
                        if (_rpc == null)
                        {
                            _rpc = new DiscordRpcClient(_cfg.ClientId.Trim());
                            _rpc.Initialize();
                            Log.Info("Connected to Discord");
                        }

                        var core = (payload.Details, payload.Assets?.LargeImageText,
                                    payload.Assets?.SmallImageKey, payload.Timestamps?.Start?.Ticks);
                        var now = DateTime.UtcNow;
                        bool coreChanged = _lastCore == null || !CoreEquals(_lastCore.Value, core);
                        bool refresh = (now - _lastSent).TotalSeconds >= 30;
                        if (coreChanged || refresh)
                        {
                            _rpc.SetPresence(payload);
                            _lastCore = core;
                            _lastSent = now;
                            Log.Info($"Presence updated: {payload.Details} | {payload.State}");
                        }
                        SetStatus(payload.Details ?? "");
                    }
                }
                finally
                {
                    prism?.Dispose();
                    game?.Dispose();
                }
            }
            catch (Exception ex)
            {
                Log.Exception("RPC error", ex);
                SetStatus("Discord not available – retrying");
                CloseRpc();
                if (_stopRequested) break;
                Thread.Sleep(10_000);
                continue;
            }

            var wait = Math.Max(2, _cfg.PollSeconds) * 1000;
            var end = DateTime.UtcNow.AddMilliseconds(wait);
            while (!_stopRequested && DateTime.UtcNow < end)
                Thread.Sleep(200);
        }

        CloseRpc();
        if (_follow) OnExit?.Invoke();
    }

    private static bool CoreEquals(
        (string?, string?, string?, long?) a,
        (string?, string?, string?, long?) b)
        => a.Item1 == b.Item1 && a.Item2 == b.Item2 && a.Item3 == b.Item3 && a.Item4 == b.Item4;

    private void CloseRpc()
    {
        if (_rpc != null)
        {
            try { _rpc.ClearPresence(); } catch { }
            try { _rpc.Dispose(); }       catch { }
            _rpc = null;
        }
        _lastCore = null;
    }
}
