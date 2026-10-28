using System.Text.Json;
using System.Text.RegularExpressions;

namespace RoBIM.Manager.Core;

public sealed record TestPanel(string Name, int Commands)
{
    public string Id => Name[..^6];
    public override string ToString() => $"{Id}   ·   {Commands}개 도구";
}

public static class RevitPackages
{
    public static void ValidateBranch(string branch)
    {
        if (string.IsNullOrWhiteSpace(branch) || branch.StartsWith('-') || branch.StartsWith('.') || branch.StartsWith('/') ||
            branch.EndsWith('/') || branch.EndsWith('.') || branch.EndsWith(".lock", StringComparison.OrdinalIgnoreCase) ||
            branch.Contains("..") || branch.Contains("@{") || branch.Contains("//") || Regex.IsMatch(branch, @"[\s~^:?*\[\\\x00-\x1F\x7F]"))
            throw new ArgumentException("브랜치 이름이 올바르지 않습니다.");
    }

    public static IReadOnlyList<TestPanel> Panels(string source)
    {
        var root = Path.Combine(source, "RoBIM TEST.tab");
        if (!Directory.Exists(root)) return [];
        return Directory.GetDirectories(root, "*.panel").Where(x =>
                Regex.IsMatch(Path.GetFileName(x), @"^TEST[A-Za-z0-9_-]*\.panel$"))
            .Select(x => new TestPanel(Path.GetFileName(x), SafeFiles.Files(x).Count(file =>
                Path.GetFileName(file).Equals("script.py", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(file).Equals("script.cs", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(file).Equals("script.vb", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(file).Equals("script.dyn", StringComparison.OrdinalIgnoreCase) ||
                (Path.GetFileName(file).Equals("bundle.yaml", StringComparison.OrdinalIgnoreCase) && Path.GetDirectoryName(file)!.EndsWith(".linkbutton")))))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    // Full dependency tree is retained. Explicit root/tab layouts select visible components,
    // so TEST wrappers can still find operational scripts and shared lib/dll at original paths.
    public static SourcePackage Prepare(SourcePackage source, string output, bool developer, IEnumerable<string>? selectedPanels = null)
    {
        InstallService.ValidatePackage(Catalog.Revit, source.Path);
        var panels = selectedPanels?.Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        var known = Panels(source.Path);
        if (developer && (panels.Length == 0 || panels.Any(x => !known.Any(p => p.Name == x))))
            throw new InvalidDataException("저장소에서 확인한 TEST 패널을 선택하십시오.");
        if (developer && known.Where(x => panels.Contains(x.Name)).Sum(x => x.Commands) == 0)
            throw new InvalidDataException("선택한 패널에는 아직 설치할 TEST 도구가 없습니다.");
        if (Directory.Exists(output)) throw new IOException("패키지 준비 경로가 이미 존재합니다.");
        SafeFiles.Copy(source.Path, output, includeGit: false);
        var visibleTabs = developer ? new[] { "RoBIM TEST" } : new[] { "RoBIM 기본", "RoBIM 도면", "RoBIM 관리" }
            .Where(x => Directory.Exists(Path.Combine(output, x + ".tab"))).ToArray();
        if (visibleTabs.Length == 0) throw new InvalidDataException("표시할 운영 탭이 없습니다.");
        WriteLayout(Path.Combine(output, "bundle.yaml"), visibleTabs);
        if (developer)
            WriteLayout(Path.Combine(output, "RoBIM TEST.tab", "bundle.yaml"), panels.Select(x => x[..^6]));
        File.WriteAllText(Path.Combine(output, "extension.json"), JsonSerializer.Serialize(new
        {
            name = developer ? "RoBIM-Developer" : "RoBIM", type = "extension",
            url = "https://github.com/RoBIM-tech/RoBIM-pyrevit.git", default_enabled = !developer,
            description = developer ? "RoBIM 개인 TEST 프로필" : "RoBIM 사용자 프로필"
        }));
        File.WriteAllText(Path.Combine(output, "robim-profile.json"), JsonSerializer.Serialize(new
        {
            mode = developer ? "Developer" : "User", source = Catalog.Revit.Repository,
            version = source.Version, panels
        }, new JsonSerializerOptions { WriteIndented = true }));
        return new(output, source.Version + (developer ? " · " + string.Join(",", panels.Select(x => x[..^6])) : " · 운영"));
    }

    private static void WriteLayout(string path, IEnumerable<string> names) =>
        File.WriteAllLines(path, new[] { "layout:" }.Concat(names.Select(x => "  - \"" + x.Replace("\"", "\\\"") + "\"")));
}
