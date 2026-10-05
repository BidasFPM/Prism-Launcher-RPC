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

    public static string? GetInstanceRoot(string cmdline)
    {
        var m = Regex.Match(cmdline, @"--gameDir\s+""([^""]+)""");
        if (m.Success) return ParentOrSelf(m.Groups[1].Value);

        m = Regex.Match(cmdline, @"--gameDir\s+([^\s]+)");
        if (m.Success) return ParentOrSelf(m.Groups[1].Value);

        var fromLib = ExtractInstanceFromLibPath(cmdline);
        if (fromLib != null) return fromLib;

        m = Regex.Match(cmdline, @"-Duser\.dir=""([^""]+)""");
        if (m.Success) return m.Groups[1].Value;

        m = Regex.Match(cmdline, @"-Duser\.dir=(\S+)");
        if (m.Success) return m.Groups[1].Value;

        return null;
    }

    private static string? ExtractInstanceFromLibPath(string cmdline)
    {
        const string key = "-Djava.library.path=";
        int start = cmdline.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        start += key.Length;

        if (start < cmdline.Length && cmdline[start] == '"') start++;

        for (int i = start; i < cmdline.Length - 8; i++)
        {
            if ((cmdline[i] == '/' || cmdline[i] == '\\') &&
                string.Compare(cmdline, i + 1, "natives", 0, 7,
                               StringComparison.OrdinalIgnoreCase) == 0)
            {
                var raw = cmdline.Substring(start, i - start);
                return raw.Replace('/', Path.DirectorySeparatorChar)
                          .Replace('\\', Path.DirectorySeparatorChar);
            }
        }
        return null;
    }

    private static string ParentOrSelf(string path)
    {
        var trimmed = path.TrimEnd('/', '\\')
                          .Replace('/', Path.DirectorySeparatorChar)
                          .Replace('\\', Path.DirectorySeparatorChar);
        var last = Path.GetFileName(trimmed);
        if (last.Equals("minecraft", StringComparison.OrdinalIgnoreCase) ||
            last.Equals(".minecraft", StringComparison.OrdinalIgnoreCase))
            return Path.GetDirectoryName(trimmed)!;
        return trimmed;
    }

    private static string FindGameDir(string instanceRoot)
    {
        var candidates = new[]
        {
            Path.Combine(instanceRoot, "minecraft"),
            Path.Combine(instanceRoot, ".minecraft"),
            instanceRoot,
        };

        foreach (var c in candidates)
            if (Directory.Exists(c) &&
                (Directory.Exists(Path.Combine(c, "logs")) ||
                 File.Exists(Path.Combine(c, "options.txt"))))
                return c;

        foreach (var c in candidates)
            if (Directory.Exists(c)) return c;

        return instanceRoot;
    }

    public static InstanceInfo? ReadInstanceInfo(Process game)
    {
        var cmd = GetCommandLine(game.Id);
        if (cmd == null) { Log.Debug("ReadInstanceInfo: cmdline null"); return null; }

        var instanceRoot = GetInstanceRoot(cmd);
        if (instanceRoot == null || !Directory.Exists(instanceRoot))
        {
            Log.Debug($"ReadInstanceInfo: instanceRoot inválido '{instanceRoot}'");
            return null;
        }

        if (!File.Exists(Path.Combine(instanceRoot, "instance.cfg")))
        {
            var parent = Path.GetDirectoryName(instanceRoot);
            if (parent != null && File.Exists(Path.Combine(parent, "instance.cfg")))
                instanceRoot = parent;
            else
            {
                Log.Debug($"ReadInstanceInfo: instance.cfg não encontrado em '{instanceRoot}'");
                return null;
            }
        }

        var gameDir = FindGameDir(instanceRoot);

        var cfg  = Helpers.ParseCfg(Path.Combine(instanceRoot, "instance.cfg"));
        var name = cfg.TryGetValue("name", out var n) && !string.IsNullOrEmpty(n)
                   ? n : Path.GetFileName(instanceRoot);
        long played = cfg.TryGetValue("totalTimePlayed", out var tt) && long.TryParse(tt, out var pv)
                       ? pv : 0;

        string? mc = null;
        string loader = "Vanilla";
        string? loaderVer = null;

        var packPath = Path.Combine(instanceRoot, "mmc-pack.json");
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
            var modsDir = Path.Combine(gameDir, "mods");
            if (Directory.Exists(modsDir))
                mods = Directory.GetFiles(modsDir, "*.jar").Length;
        }
        catch { }

        Log.Debug($"Instance: name='{name}' root='{instanceRoot}' gameDir='{gameDir}' mc={mc} loader={loader} mods={mods}");

        return new InstanceInfo(name, gameDir, mc, loader, loaderVer, mods, played);
    }
}
