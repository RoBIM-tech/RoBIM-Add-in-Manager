// Wire format shared by independently built CAD host and desktop Manager.
// Keep the JSON contract compatible; neither product references the other's assemblies.
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RoBIM.Cad.Configuration;

public sealed record CadAction
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Panel { get; init; } = "도구";
    public string Description { get; init; } = "";
    public string ExecuteKey { get; init; } = "";
    public string? Icon { get; init; }
    public int Order { get; init; }
}
public sealed record CadModule
{
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, JsonElement>? Metadata { get; init; }
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Assembly { get; init; } = "";
    public string AssemblyName { get; init; } = "";
    public string EntryType { get; init; } = "";
    public string Version { get; init; } = "1.0.0";
    public List<string> Dependencies { get; init; } = [];
    public List<CadAction> Actions { get; init; } = [];
}
public sealed record CadCatalog
{
    public int SchemaVersion { get; init; } = 1;
    public List<CadModule> Modules { get; init; } = [];
}
public sealed record CadRequest
{
    public int SchemaVersion { get; init; } = 1;
    public string Id { get; init; } = "";
    public DateTimeOffset PreparedAt { get; init; }
    public string BundlePath { get; init; } = "";
    public string LoaderHash { get; init; } = "";
    public int TargetPid { get; init; }
    public long TargetStartTicks { get; init; }
    public List<string> SelectedActions { get; init; } = [];
    public Dictionary<string, string> Hashes { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
public sealed record CadReceipt(string RequestId, string Status, string Message, int Pid,
    long StartTicks, DateTimeOffset AppliedAt);

public static class CadFiles
{
    public const string ManifestName = "robim-cad.json";
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true };
    public static string DataRoot => Environment.GetEnvironmentVariable("ROBIM_CAD_MANAGER_DATA") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RoBIM", "Manager", "Cad");
    public static T Read<T>(string path)
    {
        NoLinks(path);
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new IOException("CAD 설정 파일이 너무 큽니다.");
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("CAD 설정 오류");
    }
    public static void Save<T>(string path, T value)
    {
        NoLinks(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(value, Json)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static string Child(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':') ||
            relative.Split('/', '\\').Any(p => p.Length == 0 || p is "." or ".." || p.EndsWith(' ') || p.EndsWith('.') || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new InvalidDataException("CAD 패키지 경로 오류");
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(prefix, relative));
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("CAD 패키지 경로 이탈");
        NoLinks(path); return path;
    }
    public static void NoLinks(string path)
    {
        for (var p = Path.GetFullPath(path); !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p))
            if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("연결 폴더는 CAD 패키지로 사용할 수 없습니다.");
    }
    public static string Hash(string path)
    {
        using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream));
    }
    public static CadCatalog Catalog(string root)
    {
        var catalog = Read<CadCatalog>(Child(root, ManifestName)); Validate(root, catalog); return catalog;
    }
    public static void Validate(string root, CadCatalog catalog)
    {
        if (catalog.SchemaVersion != 1 || catalog.Modules is null || catalog.Modules.Count > 128) throw new InvalidDataException("지원하지 않는 CAD 카탈로그");
        var modules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in catalog.Modules)
        {
            if (m is null || !Id(m.Id) || !modules.Add(m.Id) || string.IsNullOrWhiteSpace(m.Name) ||
                string.IsNullOrWhiteSpace(m.AssemblyName) || string.IsNullOrWhiteSpace(m.EntryType) || m.Actions is null || m.Dependencies is null)
                throw new InvalidDataException("CAD 기능 ID/진입점 오류 또는 중복");
            if (!m.Assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || !File.Exists(Child(root, m.Assembly))) throw new InvalidDataException("CAD 기능 DLL 누락");
            foreach (var d in m.Dependencies)
                if (!d.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || !File.Exists(Child(root, d))) throw new InvalidDataException("CAD 의존 DLL 누락");
            foreach (var a in m.Actions)
            {
                if (a is null || !Id(a.Id) || !keys.Add(Key(m, a)) || keys.Count > 1024 || string.IsNullOrWhiteSpace(a.Title) ||
                    a.Title.Length > 120 || string.IsNullOrWhiteSpace(a.Panel) || a.Panel.Length > 80 ||
                    string.IsNullOrWhiteSpace(a.ExecuteKey) || a.ExecuteKey.Length > 128)
                    throw new InvalidDataException("CAD 버튼 정의 오류 또는 중복");
                if (a.Icon is not null && !File.Exists(Child(root, a.Icon))) throw new InvalidDataException("CAD 아이콘 누락");
            }
        }
    }
    public static string Key(CadModule m, CadAction a) => m.Id + ":" + a.Id;
    private static bool Id(string? value) => value is not null && Regex.IsMatch(value, "^[a-z][a-z0-9.-]{0,63}$", RegexOptions.CultureInvariant);
    public static IEnumerable<string> PayloadFiles(CadCatalog c) => new[] { ManifestName }.Concat(c.Modules.SelectMany(m =>
        new[] { m.Assembly }.Concat(m.Dependencies).Concat(m.Actions.Where(a => a.Icon is not null).Select(a => a.Icon!))))
        .Distinct(StringComparer.OrdinalIgnoreCase);

    public static string Snapshot(string data, CadRequest request)
    {
        if (request.SchemaVersion != 1 || !Guid.TryParseExact(request.Id, "N", out _) || request.SelectedActions is null || request.Hashes is null || request.Hashes.Count > 4096)
            throw new InvalidDataException("CAD 적용 요청 오류");
        var root = Child(data, "Snapshots/" + request.Id);
        foreach (var pair in request.Hashes)
            if (!Hash(Child(root, pair.Key)).Equals(pair.Value, StringComparison.OrdinalIgnoreCase)) throw new IOException("CAD 준비 파일 해시 불일치");
        var catalog = Catalog(root);
        var expected = PayloadFiles(catalog).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!expected.SetEquals(request.Hashes.Keys)) throw new InvalidDataException("CAD 준비 파일 목록 불일치");
        var actions = catalog.Modules.SelectMany(m => m.Actions.Select(a => Key(m, a))).ToHashSet(StringComparer.Ordinal);
        if (request.SelectedActions.Distinct().Count() != request.SelectedActions.Count || request.SelectedActions.Any(k => !actions.Contains(k)))
            throw new InvalidDataException("존재하지 않는 CAD 버튼 선택");
        return root;
    }
}
