using System.Text.Json;

namespace RoBIM.Manager.Core;

public sealed record RevitProfileState(string Mode, DateTimeOffset AppliedAt);

public sealed class RevitProfiles(ManagerPaths paths, Action? ensureClosed = null,
    Func<string, bool, Task>? setEnabled = null, Func<string, bool>? isEnabled = null)
{
    public async Task Apply(bool developer)
    {
        RevitRuntimePolicy.EnsureAllowed(paths, Catalog.Revit);
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
        var selectedPresent = Directory.Exists(paths.Target(Catalog.RevitSelected));
        var oldSelected = selectedPresent && (isEnabled ?? EnabledFromUserIni)("RoBIM-Selected");
        var setter = setEnabled ?? SetEnabled;
        try
        {
            if (selectedPresent) await setter("RoBIM-Selected", false);
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
                if (selectedPresent) await setter("RoBIM-Selected", oldSelected);
                if (available.Contains("RoBIM-Developer")) await setter("RoBIM-Developer", oldDeveloper);
                if (available.Contains("RoBIM")) await setter("RoBIM", oldUser);
            }
            catch { throw new IOException("프로필 적용과 이전 활성 상태 복원에 실패했습니다. pyRevit 확장 설정에서 RoBIM 활성 상태를 확인하십시오.", original); }
            throw;
        }
    }

    public async Task ApplySelection()
    {
        RevitRuntimePolicy.EnsureAllowed(paths, Catalog.RevitSelected);
        if (ensureClosed is null) EnvironmentProbe.EnsureHostClosed(Catalog.RevitSelected); else ensureClosed();
        SafeFiles.NoLinks(paths.Data); Directory.CreateDirectory(paths.Data);
        using var operation = new FileStream(Path.Combine(paths.Data, "operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var status = new InstallService(paths).Status(Catalog.RevitSelected);
        if (!status.Managed || status.Modified) throw new IOException("정상적인 버튼 선택 설치가 필요합니다.");
        var names = new[] { "RoBIM", "RoBIM-Developer", "RoBIM-Selected" };
        if (setEnabled is null)
        {
            var roots = await EnvironmentProbe.RevitRoots();
            names = names.Where(name => roots.Any(root => Directory.Exists(Path.Combine(root, name + ".extension")))).ToArray();
        }
        var previous = names.ToDictionary(name => name, name => (isEnabled ?? EnabledFromUserIni)(name));
        var setter = setEnabled ?? SetEnabled;
        try
        {
            foreach (var name in names) await setter(name, false);
            await setter("RoBIM-Selected", true);
            if (setEnabled is null && names.Any(name => EnabledFromUserIni(name) != (name == "RoBIM-Selected"))) throw new IOException("버튼 선택 프로필 활성 검증에 실패했습니다.");
            var file = Path.Combine(paths.Data, "revit-profile.json"); var temp = file + "." + Guid.NewGuid().ToString("N");
            File.WriteAllText(temp, JsonSerializer.Serialize(new RevitProfileState("Selected", DateTimeOffset.UtcNow))); File.Move(temp, file, true);
        }
        catch
        {
            foreach (var name in names) await setter(name, false);
            foreach (var pair in previous.Where(x => x.Value)) await setter(pair.Key, true);
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
        var result = await ProcessRunner.Run(EnvironmentProbe.PyRevitExecutable(), ["extensions", enabled ? "enable" : "disable", name]);
        if (result.ExitCode != 0 || EnabledFromUserIni(name) != enabled)
        {
            // CLI extension enable/disable only discovers Git-installed extensions on some versions.
            // Manager-owned copied profiles still use pyRevit's standard per-extension INI flag.
            var roots = await EnvironmentProbe.RevitRoots();
            if (!roots.Any(root => Directory.Exists(Path.Combine(root, name + ".extension"))))
                throw new IOException("pyRevit 프로필 활성 설정을 적용하지 못했습니다.");
            WriteEnabled(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pyRevit", "pyRevit_config.ini"), name, enabled);
        }
    }

    public static void WriteEnabled(string file, string name, bool enabled)
    {
        RevitRuntimePolicy.RejectLegacyOperation();
        if (name is not ("RoBIM" or "RoBIM-Developer" or "RoBIM-Selected")) throw new IOException("지원하지 않는 RoBIM 프로필");
        SafeFiles.NoLinks(file);
        var before = File.Exists(file) ? File.ReadAllBytes(file) : [];
        var hasBom = before.Length >= 3 && before[0] == 0xef && before[1] == 0xbb && before[2] == 0xbf;
        var text = System.Text.Encoding.UTF8.GetString(before).TrimStart('\uFEFF');
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var section = new System.Text.RegularExpressions.Regex(@"(?m)^\[" + System.Text.RegularExpressions.Regex.Escape(name + ".extension") + @"\][^\r\n]*\r?\n(?<body>(?:(?!^\[)[\s\S])*)");
        var matches = section.Matches(text);
        if (matches.Count > 1) throw new IOException("중복 pyRevit 프로필 설정");
        var flag = "disabled = " + (!enabled ? "true" : "false");
        if (matches.Count == 0) text += (text.EndsWith('\n') || text.Length == 0 ? "" : newline) + "[" + name + ".extension]" + newline + flag + newline;
        else
        {
            var body = matches[0].Groups["body"];
            var flags = new System.Text.RegularExpressions.Regex(@"(?m)^\s*disabled\s*=[^\r\n]*");
            if (flags.Matches(body.Value).Count > 1) throw new IOException("중복 pyRevit 활성 설정");
            var updated = flags.IsMatch(body.Value) ? flags.Replace(body.Value, flag) : flag + newline + body.Value;
            text = text[..body.Index] + updated + text[(body.Index + body.Length)..];
        }
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        if (!before.SequenceEqual(File.Exists(file) ? File.ReadAllBytes(file) : [])) throw new IOException("pyRevit 설정이 변경되었습니다.");
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, text, new System.Text.UTF8Encoding(hasBom));
        try { File.Move(temporary, file, true); } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        if (ReadEnabled(file, name) != enabled) throw new IOException("pyRevit 활성 설정 검증 실패");
    }

    public static bool EnabledFromUserIni(string name) => ReadEnabled(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pyRevit", "pyRevit_config.ini"), name);

    public static bool ReadEnabled(string file, string name)
    {
        if (!File.Exists(file)) return name is not ("RoBIM-Developer" or "RoBIM-Selected");
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
        return name is not ("RoBIM-Developer" or "RoBIM-Selected");
    }
}
