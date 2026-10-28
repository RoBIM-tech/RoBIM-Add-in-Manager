using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace RoBIM.Manager.Core;

/// <summary>Offline cleanup of this user's Manager-owned installation only.</summary>
public sealed class ManagerUninstall(ManagerPaths paths, string addinsRoot, string pyRevitConfig,
    Action? ensureClosed = null, Action? clearCredentials = null)
{
    private void CheckClosed()
    {
        if (ensureClosed is not null) { ensureClosed(); return; }
        EnvironmentProbe.EnsureHostClosed(Catalog.RevitHost);
        EnvironmentProbe.EnsureHostClosed(Catalog.Cad);
    }

    public void Run(bool keepPersonalSettings = false)
    {
        CheckClosed();
        // Preflight every tree, record and shared registration before removing anything.
        SafeFiles.NoLinks(paths.Data);
        if (Directory.Exists(paths.Data)) WindowsDataPath.EnsureDirectory(paths.Data);
        ValidateTree(paths.Data);
        var service = new InstallService(paths);
        var state = service.ReadState();
        var roots = new[] { paths.CadPlugins, paths.RevitExtensions, PyRevitLocation.Root(paths) }
            .Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var owned = new List<InstallRecord>();
        foreach (var (id, record) in state)
        {
            if (id != record.Id || record.Hashes is null || !Path.IsPathFullyQualified(record.Path))
                throw new IOException("설치 기록 형식 오류. 기존 데이터를 보존합니다.");
            var target = Path.GetFullPath(record.Path);
            var isHost = id == Catalog.RevitHost.Id && Same(target, paths.Target(Catalog.RevitHost));
            var extension = target.EndsWith(".extension", StringComparison.OrdinalIgnoreCase);
            if (!isHost && (!roots.Any(root => Same(root, Path.GetDirectoryName(target)!)) ||
                !(extension || target.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase))))
                throw new IOException("관리 범위를 벗어난 설치 기록: " + record.Id);
            ValidateTree(target);
            if (Directory.Exists(target))
            {
                if (Directory.Exists(Path.Combine(target, ".git")) || !SafeFiles.Same(record.Hashes, SafeFiles.Hashes(target)))
                    throw new IOException("사용자 변경이 있는 도구를 보존합니다. 변경 파일을 별도로 보관한 뒤 다시 제거하십시오: " + target);
                owned.Add(record);
                // Reuse the installer's exact target validation during preflight as well.
                var kind = isHost ? ToolKind.NativeRevitHost : extension ? ToolKind.PyRevit : ToolKind.NativeRevit;
                _ = service.Status(new(record.Id, record.Id, kind, "", "main", Path.GetFileName(target), "", true));
            }
        }
        var addins = new Dictionary<string, string>();
        SafeFiles.NoLinks(addinsRoot);
        if (Directory.Exists(addinsRoot))
            foreach (var year in Directory.GetDirectories(addinsRoot).Where(x => Regex.IsMatch(Path.GetFileName(x), "^[0-9]{4}$")))
            {
                var file = SafeFiles.Child(year, "RoBIM.NativeHost.addin");
                if (!File.Exists(file)) continue;
                var document = XDocument.Load(file);
                var entries = document.Root?.Elements("AddIn").ToArray() ?? [];
                var expected = Path.Combine(paths.Target(Catalog.RevitHost), "Contents", "2026", "RoBIM.Revit.Host.dll");
                if (document.Root?.Name != "RevitAddIns" || entries.Length != 1 ||
                    !Same(entries[0].Element("Assembly")?.Value ?? "", expected) ||
                    !string.Equals(entries[0].Element("AddInId")?.Value, "9DC5FBA5-A8BA-4C29-BD61-A8F80565033F", StringComparison.OrdinalIgnoreCase) ||
                    entries[0].Element("FullClassName")?.Value != "RoBIM.Revit.Host.App")
                    throw new IOException("다른 설치의 Revit 등록 파일을 보존합니다: " + file);
                addins.Add(file, SafeFiles.Hash(file));
            }
        SafeFiles.NoLinks(pyRevitConfig);
        var configBefore = File.Exists(pyRevitConfig) ? File.ReadAllBytes(pyRevitConfig) : null;
        var configAfter = configBefore is null ? null : CleanConfig(configBefore,
            state.Values.Where(r => r.Path.EndsWith(".extension", StringComparison.OrdinalIgnoreCase)).Select(r => r.Path).ToArray());
        // Once preflight succeeds, existing Remove creates recoverable backups and updates
        // installed.json per tool. On failure those records/backups remain for recovery.
        CheckClosed();
        foreach (var record in owned.Where(r => r.Id != Catalog.RevitHost.Id).Concat(owned.Where(r => r.Id == Catalog.RevitHost.Id)))
        {
            var kind = record.Id == Catalog.RevitHost.Id ? ToolKind.NativeRevitHost :
                record.Path.EndsWith(".extension", StringComparison.OrdinalIgnoreCase) ? ToolKind.PyRevit : ToolKind.NativeRevit;
            service.Remove(new(record.Id, record.Id, kind, "", "main", Path.GetFileName(record.Path), "", true));
        }
        foreach (var (file, hash) in addins)
        {
            CheckClosed(); SafeFiles.NoLinks(file);
            if (SafeFiles.Hash(file) != hash) throw new IOException("제거 중 Revit 등록이 변경되었습니다: " + file);
            File.Delete(file);
        }
        if (configBefore is not null && configAfter is not null && !configBefore.SequenceEqual(configAfter))
        {
            SafeFiles.NoLinks(pyRevitConfig);
            if (!File.ReadAllBytes(pyRevitConfig).SequenceEqual(configBefore)) throw new IOException("제거 중 pyRevit 설정이 변경되었습니다.");
            var temp = pyRevitConfig + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllBytes(temp, configAfter); File.Move(temp, pyRevitConfig, true); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        CheckClosed();
        if (!keepPersonalSettings) (clearCredentials ?? WindowsTokenStore.RemoveAllManagerTokens)();
        // Includes Native modules, selections, discovery cache, settings and backups.
        // Never delete a shared parent such as RoBIM, Autodesk, or pyRevit.
        if (Directory.Exists(paths.Data))
        {
            if (!keepPersonalSettings) SafeFiles.DeleteOwnedTree(Path.GetDirectoryName(Path.GetFullPath(paths.Data))!, paths.Data);
            else
            {
                var settings = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "github-auth.json", "repository-publishers.json", "pyrevit-location.json",
                    "ribbon-layout-revit.json", "ribbon-layout-autocad.json", "revit-mode.json", "account-preferences.json", "host-locations.json"
                };
                foreach (var entry in Directory.GetFileSystemEntries(paths.Data))
                {
                    SafeFiles.NoLinks(entry);
                    if (Directory.Exists(entry)) SafeFiles.DeleteOwnedTree(paths.Data, entry);
                    else if (!settings.Contains(Path.GetFileName(entry))) File.Delete(entry);
                }
            }
        }
    }

    private static bool Same(string a, string b) => !string.IsNullOrWhiteSpace(a) &&
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)).Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    private static void ValidateTree(string root)
    {
        SafeFiles.NoLinks(root);
        if (!Directory.Exists(root)) return;
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            SafeFiles.NoLinks(entry);
            if (Directory.Exists(entry)) ValidateTree(entry);
        }
    }

    internal static byte[] CleanConfig(byte[] bytes, string[] extensions)
    {
        var bom = bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 });
        var text = new UTF8Encoding(false, true).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
        var names = extensions.SelectMany(path => new[] { Path.GetFileName(path), Path.GetFileNameWithoutExtension(path) }).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sections = new Regex(@"(?m)^\[(?<name>[^\]\r\n]+)\][^\r\n]*(?:\r?\n|$)(?<body>(?:(?!^\[)[\s\S])*)");
        var updated = sections.Replace(text, section =>
        {
            if (names.Contains(section.Groups["name"].Value)) return "";
            if (!section.Groups["name"].Value.Equals("core", StringComparison.OrdinalIgnoreCase)) return section.Value;
            return new Regex(@"(?mi)^(?<prefix>[ \t]*userextensions[ \t]*=[ \t]*)(?<value>[^\r\n]*)").Replace(section.Value, match =>
            {
                var roots = JsonSerializer.Deserialize<string[]>(match.Groups["value"].Value) ?? throw new IOException("pyRevit 검색 경로 오류");
                var retained = roots.Where(root => !extensions.Any(extension => Same(root, extension))).ToArray();
                return retained.Length == roots.Length ? match.Value : match.Groups["prefix"].Value + JsonSerializer.Serialize(retained);
            });
        });
        var result = new UTF8Encoding(false).GetBytes(updated);
        return bom ? new byte[] { 239, 187, 191 }.Concat(result).ToArray() : result;
    }
}
