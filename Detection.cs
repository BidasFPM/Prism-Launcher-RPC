using System.Diagnostics;
using System.Management;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PrismRpc;

public record InstanceInfo(
    string Name,
    string GameDir,
    string? McVersion,
    string Loader,
    string? LoaderVersion,
    int ModCount,
    long PlayedSeconds
);

public static class Detection
{
    private static readonly Dictionary<string, string> Loaders = new()
    {
        ["net.fabricmc.fabric-loader"] = "Fabric",
        ["org.quiltmc.quilt-loader"]   = "Quilt",
        ["net.neoforged"]              = "NeoForge",
        ["net.minecraftforge"]         = "Forge",
        ["com.mumfrey.liteloader"]     = "LiteLoader",
    };

    public static (Process? prism, Process? game) ScanProcesses()
    {
        Process? prism = null, game = null;
        long prismStart = long.MaxValue, gameStart = long.MinValue;

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var name = p.ProcessName;
                if (name.Equals("prismlauncher", StringComparison.OrdinalIgnoreCase))
                {
                    var st = SafeStartTicks(p);
                    if (st < prismStart) { prism?.Dispose(); prism = p; prismStart = st; }
                    else p.Dispose();
                }
                else if (name.Equals("javaw", StringComparison.OrdinalIgnoreCase) ||
                         name.Equals("java",  StringComparison.OrdinalIgnoreCase))
                {
                    var cmd = GetCommandLine(p.Id);
                    if (cmd != null && IsPrismGame(cmd))
                    {
                        var st = SafeStartTicks(p);
                        if (st > gameStart) { game?.Dispose(); game = p; gameStart = st; }
                        else p.Dispose();
                    }
                    else p.Dispose();
                }
                else p.Dispose();
            }
            catch
            {
                try { p.Dispose(); } catch { }
            }
        }
        return (prism, game);
    }

    private static long SafeStartTicks(Process p)
    {
        try { return p.StartTime.ToUniversalTime().Ticks; }
        catch { return DateTime.UtcNow.Ticks; }
    }

    public static string? GetCommandLine(int pid)
    {
        try
        {
            using var s = new ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
            foreach (var o in s.Get())
                return o["CommandLine"]?.ToString();
        }
        catch { }
        return null;
    }

    public static bool IsPrismGame(string cmdline)
    {
        var low = cmdline.ToLowerInvariant();
        if (low.Contains("javacheck")) return false;
        if (cmdline.Contains("org.prismlauncher.EntryPoint") ||
            cmdline.Contains("org.multimc.EntryPoint") ||
            cmdline.Contains("NewLaunch.jar")) return true;
        return low.Contains("prismlauncher") && low.Contains("net.minecraft");
    }

    public static string? GetGameDir(string cmdline)
    {
        var m = Regex.Match(cmdline, @"--gameDir\s+""?([^""\s]+)""?");
        if (m.Success) return m.Groups[1].Value;
        m = Regex.Match(cmdline, @"-Duser\.dir=""?([^""\s]+)""?");
        if (m.Success) return m.Groups[1].Value;
        return null;
    }

    public static InstanceInfo? ReadInstanceInfo(Process game)
    {
        var cmd = GetCommandLine(game.Id);
        var gameDir = cmd != null ? GetGameDir(cmd) : null;
        if (string.IsNullOrEmpty(gameDir)) return null;

        var cwd = new DirectoryInfo(gameDir);
        if (!cwd.Exists) return null;

        var root = (cwd.Name.Equals("minecraft", StringComparison.OrdinalIgnoreCase) ||
                    cwd.Name.Equals(".minecraft", StringComparison.OrdinalIgnoreCase))
            ? cwd.Parent! : cwd;
        if (root == null) return null;

        var cfg     = Helpers.ParseCfg(Path.Combine(root.FullName, "instance.cfg"));
        var name    = cfg.TryGetValue("name", out var n) && !string.IsNullOrEmpty(n) ? n : root.Name;
        long played = cfg.TryGetValue("totalTimePlayed", out var tt) && long.TryParse(tt, out var pv) ? pv : 0;

        string? mc = null;
        string loader = "Vanilla";
        string? loaderVer = null;

        var packPath = Path.Combine(root.FullName, "mmc-pack.json");
        if (File.Exists(packPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(packPath));
                if (doc.RootElement.TryGetProperty("components", out var comps) &&
                    comps.ValueKind == JsonValueKind.Array)
                {
                    foreach (var c in comps.EnumerateArray())
                    {
                        var uid = c.TryGetProperty("uid", out var u) ? u.GetString() : null;
                        string? ver = null;
                        if (c.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String)
                            ver = v.GetString();
                        else if (c.TryGetProperty("cachedVersion", out var cv) && cv.ValueKind == JsonValueKind.String)
                            ver = cv.GetString();

                        if (uid == "net.minecraft") mc = ver;
                        else if (uid != null && Loaders.TryGetValue(uid, out var ln))
                        {
                            loader = ln;
                            loaderVer = ver;
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Warn($"mmc-pack.json: {ex.Message}"); }
        }

        int mods = 0;
        try
        {
            var modsDir = Path.Combine(cwd.FullName, "mods");
            if (Directory.Exists(modsDir))
                mods = Directory.GetFiles(modsDir, "*.jar").Length;
        }
        catch { }

        return new InstanceInfo(name, cwd.FullName, mc, loader, loaderVer, mods, played);
    }
}
