using System.Text;
using System.Text.RegularExpressions;

namespace PrismRpc;

public record LogState(bool Loaded, string? Mode, string? Server, int? Port);

public static class LogReader
{
    private static readonly Regex ReConnect = new(@"Connecting to (.+?), (\d+)", RegexOptions.Compiled);
    private static readonly Regex ReLoaded  = new(@"Sound engine started|Created: \d+x\d+x\d+ .*atlas", RegexOptions.Compiled);
    private static readonly Regex ReLeave   = new(@"Stopping server|Stopping!|Client disconnected|Lost connection|Disconnected from", RegexOptions.Compiled);

    public static LogState Read(string gameDir, DateTime startedLocal)
    {
        var path = Path.Combine(gameDir, "logs", "latest.log");
        var loaded = false;
        string? mode = null, server = null;
        int? port = null;

        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) return new LogState(false, null, null, null);
            if (fi.LastWriteTime < startedLocal.AddSeconds(-10))
                return new LogState(false, null, null, null);

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var start = Math.Max(0, fs.Length - 262_144);
            fs.Seek(start, SeekOrigin.Begin);
            using var sr = new StreamReader(fs, Encoding.UTF8);
            var text = sr.ReadToEnd();

            foreach (var line in text.Split('\n'))
            {
                if (ReLoaded.IsMatch(line)) loaded = true;

                var m = ReConnect.Match(line);
                if (m.Success)
                {
                    port   = int.Parse(m.Groups[2].Value);
                    mode   = "mp";
                    server = m.Groups[1].Value;
                }
                else if (line.Contains("Starting integrated minecraft server"))
                {
                    mode = "sp"; server = null; port = null;
                }
                else if (ReLeave.IsMatch(line))
                {
                    mode = null; server = null; port = null;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"LogReader: {ex.Message}");
        }
        return new LogState(loaded, mode, server, port);
    }
}
