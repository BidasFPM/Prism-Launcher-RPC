namespace PrismRpc;

public static class Log
{
    private static readonly object _lock = new();
    private static StreamWriter? _writer;
    private static bool _debug;

    public static bool IsDebug => _debug;

    public static void Init(string path, bool debug)
    {
        _debug = debug;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            _writer = new StreamWriter(stream) { AutoFlush = true };
        }
        catch { }
    }

    public static void Info(string msg)  => Write("INFO",  msg);
    public static void Warn(string msg)  => Write("WARN",  msg);
    public static void Error(string msg) => Write("ERROR", msg);
    public static void Debug(string msg) { if (_debug) Write("DEBUG", msg); }

    public static void Exception(string msg, Exception ex)
    {
        Write("ERROR", $"{msg}: {ex.GetType().Name}: {ex.Message}");
        if (_debug) Write("ERROR", ex.ToString());
    }

    private static void Write(string level, string msg)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {msg}";
        lock (_lock)
        {
            try { _writer?.WriteLine(line); } catch { }
            if (_debug)
            {
                try { Console.WriteLine(line); } catch { }
            }
        }
    }
}
