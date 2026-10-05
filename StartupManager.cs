using Microsoft.Win32;

namespace PrismRpc;

public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool Enabled()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(Program.AppName) != null;
        }
        catch { return false; }
    }

    public static void Set(bool enable)
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                      ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (k == null) return;
        if (enable)
            k.SetValue(Program.AppName, Command());
        else
            k.DeleteValue(Program.AppName, throwOnMissingValue: false);
    }

    private static string Command()
    {
        var exe = Environment.ProcessPath
                  ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
        return $"\"{exe}\"";
    }
}
