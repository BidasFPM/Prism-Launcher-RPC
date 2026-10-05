using System.Diagnostics;
using DiscordRPC;
using Button = DiscordRPC.Button;

namespace PrismRpc;

public static class PresenceBuilder
{
    private static readonly HashSet<int> WebPorts = new() { 53, 80, 443 };

    public static RichPresence? Build(Config cfg, Process? prism, Process? game)
    {
        var buttons = cfg.Show.Buttons && cfg.Buttons.Count > 0
            ? cfg.Buttons.Take(2)
                .Select(b => new Button { Label = Helpers.Clip(b.Label, 32), Url = b.Url })
                .ToArray()
            : null;

        if (game != null) return BuildGame(cfg, game, buttons);
        if (prism != null && cfg.Show.LauncherIdle) return BuildLauncher(cfg, prism, buttons);
        return null;
    }

    private static RichPresence? BuildGame(Config cfg, Process game, Button[]? buttons)
    {
        var started = SafeStartTime(game);
        var info = Detection.ReadInstanceInfo(game);
        if (info == null) return null;

        var (loaded, mode, server, port) = LogReader.Read(info.GameDir, started);
        if (mode == "mp" && MpConnected(game, port) == false)
        {
            mode = null; server = null;
        }
        if (!loaded && (DateTime.Now - started).TotalSeconds > 180) loaded = true;

        string activity;
        if (!loaded) activity = "Loading the game…";
        else if (mode == "mp" && server != null)
        {
            var hide = !cfg.Show.ServerAddress ||
                       (cfg.Privacy.HideIpServers && Helpers.IsIpAddress(server));
            activity = hide ? "In a server"
                            : "Multiplayer · " + (port == 25565 ? server : $"{server}:{port}");
        }
        else if (mode == "sp") activity = "Singleplayer";
        else activity = "In the main menu";

        string name = (cfg.Privacy.HideInstanceName || !cfg.Show.InstanceName)
            ? "Minecraft"
            : Helpers.DisplayName(cfg, info.Name);

        var details = $"{name} — {activity}";

        var parts = new List<string>();
        if (cfg.Show.ModLoader) parts.Add(info.Loader);
        if (cfg.Show.MinecraftVersion && !string.IsNullOrEmpty(info.McVersion))
            parts.Add(info.McVersion!);
        var state = string.Join(" ", parts);

        if (cfg.Show.ModCount && info.Loader != "Vanilla")
            state += $" · {info.ModCount} mods";

        if (cfg.Show.Ram)
        {
            try
            {
                game.Refresh();
                var rss = game.WorkingSet64 / (1024.0 * 1024.0 * 1024.0);
                state += $" · {rss:0.0} GB RAM";
            }
            catch { }
        }

        var tips = new List<string>();
        if (cfg.Show.TotalPlaytime)
        {
            var total = info.PlayedSeconds + (long)(DateTime.Now - started).TotalSeconds;
            tips.Add("Total playtime: " + Helpers.FormatDuration(total));
        }
        if (cfg.Show.JavaVersion)
        {
            var jv = GetJavaVersion(game);
            if (!string.IsNullOrEmpty(jv)) tips.Add($"Java {jv}");
        }

        var loaderKey = cfg.Assets.Loaders.TryGetValue(info.Loader, out var lk)
            ? lk : cfg.Assets.Launcher;

        var rp = new RichPresence
        {
            Details = Helpers.Clip(details),
            State = Helpers.Clip(string.IsNullOrWhiteSpace(state.Trim(' ', '·'))
                                   ? "Playing Minecraft" : state.Trim(' ', '·')),
            Assets = new Assets
            {
                LargeImageKey  = cfg.Assets.Game,
                LargeImageText = Helpers.Clip(tips.Count > 0
                                               ? string.Join(" | ", tips)
                                               : "Minecraft via Prism Launcher"),
                SmallImageKey  = loaderKey,
                SmallImageText = Helpers.Clip($"{info.Loader} {info.LoaderVersion}".Trim())
            }
        };
        if (cfg.Show.SessionTimer) rp.Timestamps = new Timestamps(started.ToUniversalTime());
        if (buttons != null)       rp.Buttons    = buttons;
        return rp;
    }

    private static RichPresence BuildLauncher(Config cfg, Process prism, Button[]? buttons)
    {
        var n = CountInstances(prism, cfg);
        var rp = new RichPresence
        {
            Details = "Browsing instances",
            State   = n.HasValue ? $"{n.Value} instances" : "Prism Launcher",
            Assets  = new Assets
            {
                LargeImageKey  = cfg.Assets.Launcher,
                LargeImageText = "Prism Launcher"
            }
        };
        if (cfg.Show.SessionTimer)
            rp.Timestamps = new Timestamps(SafeStartTime(prism).ToUniversalTime());
        if (buttons != null) rp.Buttons = buttons;
        return rp;
    }

    private static DateTime SafeStartTime(Process p)
    {
        try { return p.StartTime; }
        catch { return DateTime.Now; }
    }

    private static int? CountInstances(Process prism, Config cfg)
    {
        var candidates = new List<string?>();
        if (!string.IsNullOrEmpty(cfg.PrismDataDir)) candidates.Add(cfg.PrismDataDir);
        var appdata = Environment.GetEnvironmentVariable("APPDATA");
        if (!string.IsNullOrEmpty(appdata)) candidates.Add(Path.Combine(appdata, "PrismLauncher"));
        try { candidates.Add(Path.GetDirectoryName(prism.MainModule?.FileName)); } catch { }

        foreach (var b in candidates)
        {
            if (string.IsNullOrEmpty(b)) continue;
            var inst = Path.Combine(b, "instances");
            if (Directory.Exists(inst))
                return Directory.GetDirectories(inst)
                    .Count(d => File.Exists(Path.Combine(d, "instance.cfg")));
        }
        return null;
    }

    private static bool? MpConnected(Process game, int? port)
    {
        try
        {
            var pid = (uint)game.Id;
            var conns = TcpConnections.GetEstablished()
                .Where(c => c.Pid == pid).ToList();
            if (conns.Count == 0) return false;

            if (port.HasValue && conns.Any(c => c.RemotePort == port.Value)) return true;

            foreach (var c in conns)
            {
                if (WebPorts.Contains(c.RemotePort)) continue;
                if (c.RemoteIp.StartsWith("127.")) continue;
                if (c.RemoteIp == "0.0.0.0" || c.RemoteIp == "::1") continue;
                return true;
            }
            return false;
        }
        catch { return null; }
    }

    private static string? GetJavaVersion(Process game)
    {
        try
        {
            var exe = game.MainModule?.FileName;
            if (string.IsNullOrEmpty(exe)) return null;
            var release = Path.Combine(Path.GetDirectoryName(exe)!, "..", "release");
            if (!File.Exists(release)) return null;
            foreach (var line in File.ReadLines(release))
                if (line.StartsWith("JAVA_VERSION="))
                    return line.Split('=', 2)[1].Trim('"');
        }
        catch { }
        return null;
    }
}
