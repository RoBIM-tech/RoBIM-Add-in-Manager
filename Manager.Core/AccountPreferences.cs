using System.Text.Json;
using Microsoft.Win32;
using System.Runtime.Versioning;

namespace RoBIM.Manager.Core;

public sealed record AccountPreferences(bool AutoLogin = true, bool AutoUpdate = true)
{
    public static AccountPreferences Read(ManagerPaths paths)
    {
        var file = SafeFiles.Child(paths.Data, "account-preferences.json");
        return File.Exists(file) ? JsonSerializer.Deserialize<AccountPreferences>(File.ReadAllText(file)) ?? new() : new();
    }
    public void Save(ManagerPaths paths)
    {
        Directory.CreateDirectory(paths.Data);
        File.WriteAllText(SafeFiles.Child(paths.Data, "account-preferences.json"), JsonSerializer.Serialize(this));
    }
}

[SupportedOSPlatform("windows")]
public static class StartupRegistration
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static string Command(string exe) => "\"" + Path.GetFullPath(exe) + "\" --background";
    public static bool Enabled(string exe)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Key);
        return string.Equals(key?.GetValue("RoBIMManager") as string, Command(exe), StringComparison.OrdinalIgnoreCase);
    }
    public static void Set(string exe, bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Key);
        var current = key.GetValue("RoBIMManager") as string;
        if (current is not null && !string.Equals(current, Command(exe), StringComparison.OrdinalIgnoreCase))
            throw new IOException("다른 설치가 등록한 RoBIM 자동 시작 항목을 보존합니다.");
        if (enabled) key.SetValue("RoBIMManager", Command(exe)); else key.DeleteValue("RoBIMManager", false);
    }
}
