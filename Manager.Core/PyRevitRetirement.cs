using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace RoBIM.Manager.Core;

public sealed record RetirementPlan(string Id, Dictionary<string, Dictionary<string, string>> Tools, string? ConfigHash);

/// <summary>Retires only Manager-owned RoBIM installations. Native modules/other extensions stay untouched.</summary>
public sealed class PyRevitRetirement(ManagerPaths paths, string configFile)
{
    private static readonly ToolSpec[] Tools = [Catalog.RevitSelected, Catalog.RevitDeveloper, Catalog.Revit, Catalog.ReloadBridge];
    private string Backup(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new IOException("정리 계획 ID 오류");
        return SafeFiles.Child(paths.Data, "Backups/PyRevitRetirement/" + id);
    }
    public RetirementPlan Prepare()
    {
        WindowsDataPath.EnsureDirectory(paths.Data);
        var plan = new RetirementPlan(Guid.NewGuid().ToString("N"), [], File.Exists(configFile) ? SafeFiles.Hash(configFile) : null);
        var backup = Backup(plan.Id); Directory.CreateDirectory(backup);
        var service = new InstallService(paths);
        var known = Tools.Select(x => Path.GetFullPath(paths.Target(x))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var root in new[] { paths.RevitExtensions, PyRevitLocation.Root(paths) }.Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists))
            foreach (var directory in Directory.GetDirectories(root).Where(x => Path.GetFileName(x).StartsWith("RoBIM", StringComparison.OrdinalIgnoreCase) && x.EndsWith(".extension", StringComparison.OrdinalIgnoreCase)))
                if (!known.Contains(Path.GetFullPath(directory))) throw new IOException("별도 RoBIM 외부 설치가 있어 원본을 보존합니다: " + directory);
        foreach (var tool in Tools)
        {
            var state = service.Status(tool);
            if (state.External || state.Modified) throw new IOException("외부 설치/로컬 변경이 있어 보존합니다: " + tool.Id);
            if (!state.Managed) continue;
            var hashes = SafeFiles.Hashes(state.Path);
            var copy = SafeFiles.Child(backup, tool.Id);
            SafeFiles.Copy(state.Path, copy, false);
            if (!SafeFiles.Same(hashes, SafeFiles.Hashes(copy)) || !SafeFiles.Same(hashes, SafeFiles.Hashes(state.Path))) throw new IOException("정리 백업 해시 검증 실패");
            plan.Tools.Add(tool.Id, hashes);
        }
        foreach (var file in new[] { "installed.json", "revit-profile.json", "ribbon-layout-revit.json", "reload-pending.json", "reload-profile.json", "revit-mode.json" })
            if (File.Exists(SafeFiles.Child(paths.Data, file))) File.Copy(SafeFiles.Child(paths.Data, file), SafeFiles.Child(backup, file));
        if (File.Exists(configFile))
        {
            SafeFiles.NoLinks(configFile); var copy = SafeFiles.Child(backup, "pyRevit_config.ini"); File.Copy(configFile, copy);
            if (SafeFiles.Hash(copy) != plan.ConfigHash || SafeFiles.Hash(configFile) != plan.ConfigHash) throw new IOException("pyRevit 설정 백업 검증 실패");
        }
        File.WriteAllText(SafeFiles.Child(backup, "plan.json"), JsonSerializer.Serialize(plan));
        return plan;
    }
    public void Apply(RetirementPlan plan, Action? ensureClosed = null, Func<bool>? nativeHostReady = null)
    {
        void Check() { if (ensureClosed is null) EnvironmentProbe.EnsureHostClosed(Catalog.Revit); else ensureClosed(); }
        Check(); WindowsDataPath.EnsureDirectory(paths.Data);
        if (nativeHostReady is not null ? !nativeHostReady() : !HostReady()) throw new IOException("pyRevit 제거 전에 Native 리본 지원 Host를 설치해야 합니다.");
        var backup = Backup(plan.Id);
        var saved = JsonSerializer.Deserialize<RetirementPlan>(File.ReadAllText(SafeFiles.Child(backup, "plan.json"))) ?? throw new IOException("정리 계획 없음");
        if (JsonSerializer.Serialize(saved) != JsonSerializer.Serialize(plan)) throw new IOException("정리 계획 변경");
        if (plan.Tools.Keys.Any(x => !Tools.Any(t => t.Id == x))) throw new IOException("허용되지 않은 정리 대상");
        if ((File.Exists(configFile) ? SafeFiles.Hash(configFile) : null) != plan.ConfigHash) throw new IOException("백업 이후 pyRevit 설정이 변경됐습니다. 새 계획을 준비하십시오.");
        var service = new InstallService(paths, _ => Check());
        foreach (var tool in Tools.Where(x => plan.Tools.ContainsKey(x.Id)))
        {
            var status = service.Status(tool);
            if (!status.Managed || status.Modified || !SafeFiles.Same(plan.Tools[tool.Id], SafeFiles.Hashes(status.Path)) || !SafeFiles.Same(plan.Tools[tool.Id], SafeFiles.Hashes(SafeFiles.Child(backup, tool.Id))))
                throw new IOException("백업 이후 설치/복구 사본 변경: " + tool.Id);
        }
        // Parse the entire proposed configuration before removing the first installation.
        byte[]? updatedConfig = null;
        if (File.Exists(configFile))
        {
            var bytes = File.ReadAllBytes(configFile); var bom = bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 });
            var content = new UTF8Encoding(false, true).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
            var updated = new UTF8Encoding(bom).GetBytes(RemoveRegistrations(content));
            if (bom) updated = new byte[] { 239, 187, 191 }.Concat(updated).ToArray();
            updatedConfig = updated;
        }
        var layout = SafeFiles.Child(paths.Data, "ribbon-layout-revit.json");
        RibbonLayout? nextLayout = null;
        var layoutHash = File.Exists(layout) ? SafeFiles.Hash(layout) : null;
        if (File.Exists(layout))
        {
            var store = new RibbonLayouts(paths); var before = store.Read("Revit");
            var tabs = before.Tabs.Select(t => t with { Panels = t.Panels.Select(p =>
            {
                var items = p.Buttons.Where(b => !b.Id.StartsWith("robim-revit:") && b.Id != "manager:pyrevit-reload").ToArray();
                foreach (var group in items.Where(x => x.Stack is not null).GroupBy(x => x.Stack).ToArray())
                    foreach (var item in group.ToArray())
                    {
                        var index = Array.IndexOf(items, item); var count = group.Count();
                        items[index] = count is 2 or 3 ? item with { StackRows = count } : item with { Stack = null, StackRows = 0 };
                    }
                return p with { Buttons = items };
            }).Where(p => p.Buttons.Any(x => x.Kind == "Button")).ToArray() }).Where(t => t.Panels.Length > 0).ToArray();
            nextLayout = before with { Tabs = tabs };
            RibbonLayouts.Validate(nextLayout);
        }
        // Every target and proposed file was checked before the first removal.
        Check();
        foreach (var tool in Tools.Where(x => plan.Tools.ContainsKey(x.Id))) service.Remove(tool);
        Check();
        if ((File.Exists(configFile) ? SafeFiles.Hash(configFile) : null) != plan.ConfigHash || (File.Exists(layout) ? SafeFiles.Hash(layout) : null) != layoutHash)
            throw new IOException("정리 중 설정/리본이 변경되어 원본을 보존합니다. 복구 백업을 확인하십시오.");
        if (updatedConfig is not null) Atomic(configFile, updatedConfig);
        if (nextLayout is not null) new RibbonLayouts(paths).Save(nextLayout);
        foreach (var file in new[] { "revit-profile.json", "reload-pending.json", "reload-profile.json", "manager-display-cache.json" })
            if (File.Exists(SafeFiles.Child(paths.Data, file))) File.Delete(SafeFiles.Child(paths.Data, file));
        Atomic(SafeFiles.Child(paths.Data, "revit-mode.json"), JsonSerializer.SerializeToUtf8Bytes(new { SchemaVersion = 1, Mode = "NativeOnly", Plan = plan.Id, At = DateTimeOffset.UtcNow }));
        // Do not edit NativeRevit active/pending, CAD, pyRevit itself or other extensions.
    }
    private bool HostReady()
    {
        var state = new InstallService(paths).Status(Catalog.RevitHost);
        if (!state.Managed || state.Modified) return false;
        var file = SafeFiles.Child(state.Path, "Contents/2026/RoBIM.Revit.Host.dll");
        var version = FileVersionInfo.GetVersionInfo(file).ProductVersion ?? "";
        var preview = Regex.Match(version, @"^1\.0\.0-preview\.(\d+)");
        return preview.Success && int.Parse(preview.Groups[1].Value) >= 3;
    }
    private static void Atomic(string file, byte[] bytes)
    {
        SafeFiles.NoLinks(file); var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, file, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static string RemoveRegistrations(string text)
    {
        var sections = new Regex(@"(?m)^\[(?<name>[^\]\r\n]+)\][^\r\n]*(?:\r?\n|$)(?<body>(?:(?!^\[)[\s\S])*)");
        if (sections.Matches(text).Count(x => x.Groups["name"].Value.Equals("core", StringComparison.OrdinalIgnoreCase)) > 1) throw new IOException("중복 pyRevit core 설정은 보존합니다.");
        text = sections.Replace(text, match =>
        {
            var name = match.Groups["name"].Value;
            return name.Equals("RoBIM", StringComparison.OrdinalIgnoreCase) || name.StartsWith("RoBIM-", StringComparison.OrdinalIgnoreCase) || name.StartsWith("RoBIM ", StringComparison.OrdinalIgnoreCase) || name.Equals("RoBIM.extension", StringComparison.OrdinalIgnoreCase) ? "" : match.Value;
        });
        return sections.Replace(text, section =>
        {
            if (!section.Groups["name"].Value.Equals("core", StringComparison.OrdinalIgnoreCase)) return section.Value;
            var updated = new Regex(@"(?mi)^(?<prefix>[ \t]*userextensions[ \t]*=[ \t]*)(?<value>[^\r\n]*)").Replace(section.Groups["body"].Value, match =>
        {
            var roots = JsonSerializer.Deserialize<string[]>(match.Groups["value"].Value) ?? throw new IOException("pyRevit 검색 경로 형식 오류");
            var retained = roots.Where(x =>
            {
                var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(x));
                return !(name.StartsWith("RoBIM", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".extension", StringComparison.OrdinalIgnoreCase));
            }).ToArray();
            return retained.Length == roots.Length ? match.Value : match.Groups["prefix"].Value + JsonSerializer.Serialize(retained);
            });
            return section.Value[..(section.Groups["body"].Index - section.Index)] + updated;
        });
    }
}
