using System.Text.RegularExpressions;

namespace PrismRpc;

public static class Helpers
{
    public static string Clip(string? s, int n = 128)
    {
        s ??= "";
        return s.Length <= n ? s : s[..(n - 1)] + "…";
    }

    public static string FormatDuration(long seconds)
    {
        var h = seconds / 3600;
        var m = (seconds % 3600) / 60;
        return h > 0 ? $"{h}h {m:00}m" : $"{m}m";
    }

    public static Dictionary<string, string> ParseCfg(string path)
    {
        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var line in File.ReadAllLines(path))
            {
                if (string.IsNullOrEmpty(line)) continue;
                if (line.StartsWith('[') || line.StartsWith(';') || line.StartsWith('#')) continue;
                var idx = line.IndexOf('=');
                if (idx < 0) continue;
                var k = line[..idx].Trim();
                var v = line[(idx + 1)..].Trim();
                data[k] = v;
            }
        }
        catch { }
        return data;
    }

    public static string DisplayName(Config cfg, string name)
    {
        if (cfg.NameOverrides.TryGetValue(name, out var ov)) return ov;
        if (cfg.StripSortPrefix)
            name = Regex.Replace(name, @"^[A-Z](?=[A-Z][a-z])", "");
        return name;
    }

    public static bool IsIpAddress(string host)
    {
        host = host.Trim().Trim('[', ']');
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        return System.Net.IPAddress.TryParse(host, out _);
    }
}
