using System.Text.Json;

namespace RoBIM.Manager.Core;

public sealed record RevitProfileState(string Mode, DateTimeOffset AppliedAt);

public sealed class RevitProfiles(ManagerPaths paths, Action? ensureClosed = null,
    Func<string, bool, Task>? setEnabled = null, Func<string, bool>? isEnabled = null)
{
    public async Task Apply(bool developer)
    {
        if (ensureClosed is null) EnvironmentProbe.EnsureHostClosed(Catalog.Revit); else ensureClosed();
        SafeFiles.NoLinks(paths.Data);
        Directory.CreateDirectory(paths.Data);
        using var operation = new FileStream(Path.Combine(paths.Data, "operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var available = new HashSet<string> { "RoBIM", "RoBIM-Developer" };
        if (setEnabled is null)
        {
            var roots = await EnvironmentProbe.RevitRoots();
            available = new HashSet<string>(available.Where(name => roots.Any(root => Directory.Exists(Path.Combine(root, name + ".extension")))));
            if (!available.Contains(developer ? "RoBIM-Developer" : "RoBIM")) throw new IOException("적용할 Revit 프로필이 설치되어 있지 않습니다.");
        }
        var oldUser = (isEnabled ?? EnabledFromUserIni)("RoBIM");
        var oldDeveloper = (isEnabled ?? EnabledFromUserIni)("RoBIM-Developer");
        var setter = setEnabled ?? SetEnabled;
        try
        {
            // Disable first: both profiles are never deliberately enabled together.
            var other = developer ? "RoBIM" : "RoBIM-Developer";
            if (available.Contains(other)) await setter(other, false);
            await setter(developer ? "RoBIM-Developer" : "RoBIM", true);
            if (setEnabled is null && (EnabledFromUserIni("RoBIM-Developer") != developer ||
                (available.Contains("RoBIM") && EnabledFromUserIni("RoBIM") == developer)))
                throw new IOException("pyRevit 활성 상태 검증에 실패했습니다.");
            var file = Path.Combine(paths.Data, "revit-profile.json");
            var temp = file + "." + Guid.NewGuid().ToString("N");
            File.WriteAllText(temp, JsonSerializer.Serialize(new RevitProfileState(developer ? "Developer" : "User", DateTimeOffset.UtcNow)));
            File.Move(temp, file, true);
        }
        catch (Exception original)
        {
            try
            {
                if (available.Contains("RoBIM-Developer")) await setter("RoBIM-Developer", oldDeveloper);
                if (available.Contains("RoBIM")) await setter("RoBIM", oldUser);
            }
            catch { throw new IOException("프로필 적용과 이전 활성 상태 복원에 실패했습니다. pyRevit 확장 설정에서 RoBIM 활성 상태를 확인하십시오.", original); }
            throw;
        }
    }

    public RevitProfileState? State()
    {
        var file = Path.Combine(paths.Data, "revit-profile.json");
        SafeFiles.NoLinks(file);
        return File.Exists(file) ? JsonSerializer.Deserialize<RevitProfileState>(File.ReadAllText(file)) : null;
    }

    private static async Task SetEnabled(string name, bool enabled)
    {
        var result = await ProcessRunner.Run("pyrevit", ["extensions", enabled ? "enable" : "disable", name]);
        if (result.ExitCode != 0) throw new IOException("pyRevit 프로필 활성 설정을 적용하지 못했습니다.");
    }

    public static bool EnabledFromUserIni(string name) => ReadEnabled(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pyRevit", "pyRevit_config.ini"), name);

    public static bool ReadEnabled(string file, string name)
    {
        if (!File.Exists(file)) return name != "RoBIM-Developer";
        var section = "";
        // Only an enabled/disabled boolean leaves this parser; credentials are never copied or returned.
        foreach (var line in File.ReadLines(file))
        {
            var text = line.Trim();
            if (text.StartsWith('[') && text.EndsWith(']')) section = text[1..^1];
            else if (section.Equals(name + ".extension", StringComparison.OrdinalIgnoreCase) && text.Contains('='))
            {
                var pair = text.Split('=', 2);
                if (pair[0].Trim().Equals("disabled", StringComparison.OrdinalIgnoreCase))
                    return bool.TryParse(pair[1].Trim(), out var disabled) ? !disabled : throw new IOException("pyRevit 활성 설정을 확인할 수 없습니다.");
            }
        }
        return name != "RoBIM-Developer";
    }
}
