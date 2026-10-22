// Wire contract also compiled by Manager.Core. Keep copies byte-identical.
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RoBIM.Revit.Reload;

public sealed record NativeCommand(string Id, string Title, string Description, string Tab, string Panel, string Assembly, string Type, string? Icon = null);
public sealed record NativeModule(int SchemaVersion, string Id, string Version, string ReloadPolicy, NativeCommand[] Commands);
public sealed record NativeModuleEntry(string Directory, NativeModule Manifest, string[] Selected, Dictionary<string, string> Hashes);
public sealed record NativeRequest(int SchemaVersion, string Id, int TargetPid, long TargetStartTicks, bool ApplyRequested, NativeModuleEntry[] Modules);
public sealed record NativeReceipt(string RequestId, string Status, string Message, int Pid, long StartTicks, DateTimeOffset AppliedAt);

public static class NativeProtocol
{
    public const string ManifestName = "robim-module.json";
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true };
    public static string DataRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RoBIM", "Manager", "NativeRevit");
    public static void NoLinks(string path)
    {
        for (var item = Path.GetFullPath(path); !string.IsNullOrEmpty(item); item = Path.GetDirectoryName(item))
            if ((File.Exists(item) || Directory.Exists(item)) && (File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0) throw new IOException("연결 경로는 Revit Reload에서 사용할 수 없습니다.");
    }
    public static string Child(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':') || relative.Split('/', '\\').Any(p => p.Length == 0 || p is "." or ".." || p.EndsWith(' ') || p.EndsWith('.') || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)) throw new InvalidDataException("Revit 모듈 경로 오류");
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var result = Path.GetFullPath(Path.Combine(prefix, relative));
        if (!result.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Revit 모듈 경로 이탈");
        NoLinks(result); return result;
    }
    public static T Read<T>(string file)
    {
        NoLinks(file); if (new FileInfo(file).Length > 4 * 1024 * 1024) throw new InvalidDataException("Revit Reload 기록 크기 초과");
        return JsonSerializer.Deserialize<T>(File.ReadAllText(file), Json) ?? throw new InvalidDataException("Revit Reload 기록 오류");
    }
    public static void Save<T>(string file, T value)
    {
        NoLinks(file); System.IO.Directory.CreateDirectory(Path.GetDirectoryName(file)!); var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(value, Json)); File.Move(temp, file, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static Dictionary<string, string> Hashes(string root)
    {
        NoLinks(root); var files = new List<string>();
        void Walk(string directory)
        {
            NoLinks(directory);
            foreach (var path in System.IO.Directory.EnumerateFiles(directory)) { NoLinks(path); files.Add(path); if (files.Count > 12000) throw new InvalidDataException("Revit 모듈 파일 수 초과"); }
            foreach (var directoryPath in System.IO.Directory.EnumerateDirectories(directory)) Walk(directoryPath);
        }
        Walk(root); return files.ToDictionary(file => Path.GetRelativePath(root, file).Replace('\\', '/'), file => Hash(file), StringComparer.OrdinalIgnoreCase);
    }
    public static string Hash(string file) { using var stream = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(stream)); }
    public static bool Same(Dictionary<string, string> a, Dictionary<string, string> b) => a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out var hash) && hash == pair.Value);
    public static NativeModule Module(string root)
    {
        var module = Read<NativeModule>(Child(root, ManifestName));
        if (module.SchemaVersion != 1 || !Id(module.Id) || string.IsNullOrWhiteSpace(module.Version) || module.ReloadPolicy != "command-only" || module.Commands is null || module.Commands.Length is < 1 or > 128 || module.Commands.Select(c => c.Id).Distinct().Count() != module.Commands.Length) throw new InvalidDataException("Revit Reload 모듈 정의 오류");
        foreach (var command in module.Commands)
        {
            if (!Id(command.Id) || string.IsNullOrWhiteSpace(command.Title) || command.Title.Length > 120 || string.IsNullOrWhiteSpace(command.Tab) || command.Tab.Length > 80 || string.IsNullOrWhiteSpace(command.Panel) || command.Panel.Length > 80 || string.IsNullOrWhiteSpace(command.Type) || !command.Assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || !File.Exists(Child(root, command.Assembly))) throw new InvalidDataException("Revit Reload 버튼 정의 오류");
            if (command.Icon is not null && !File.Exists(Child(root, command.Icon))) throw new InvalidDataException("Revit 버튼 아이콘 누락");
        }
        return module;
    }
    public static void Validate(string data, NativeRequest request, Action? ensureSession = null)
    {
        if (request.SchemaVersion != 1 || !Guid.TryParseExact(request.Id, "N", out _) || request.TargetPid < 0 || request.TargetStartTicks < 0 || (request.TargetPid == 0) != (request.TargetStartTicks == 0) || request.Modules is null || request.Modules.Length > 128 || request.Modules.Select(x => x.Manifest.Id).Distinct().Count() != request.Modules.Length || request.Modules.Sum(x => x.Selected.Length) > 256) throw new InvalidDataException("Revit Reload 요청 오류");
        ensureSession?.Invoke();
        var prefix = Path.GetFullPath(Child(data, "Packages")) + Path.DirectorySeparatorChar;
        foreach (var entry in request.Modules)
        {
            if (!Path.GetFullPath(entry.Directory).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Revit 모듈 저장 경로 오류");
            var manifest = Module(entry.Directory);
            if (JsonSerializer.Serialize(manifest, Json) != JsonSerializer.Serialize(entry.Manifest, Json) || entry.Selected.Length != entry.Selected.Distinct().Count() || entry.Selected.Any(id => !manifest.Commands.Any(c => c.Id == id)) || !Same(entry.Hashes, Hashes(entry.Directory))) throw new InvalidDataException("Revit 모듈 해시 또는 선택 정보 불일치");
        }
        ensureSession?.Invoke();
    }
    public static void Copy(string source, string destination)
    {
        foreach (var pair in Hashes(source)) { var target = Child(destination, pair.Key); System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(Child(source, pair.Key), target, false); }
    }
    public static NativeRequest Prepare(string data, string? package, string moduleId, string[] selected, int pid, long ticks, NativeRequest? basis = null, bool publish = true)
    {
        NoLinks(data); System.IO.Directory.CreateDirectory(data);
        using var operation = new FileStream(Child(data, "prepare.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var existingPath = File.Exists(Child(data, "pending.json")) ? Child(data, "pending.json") : Child(data, "active.json");
        var existing = basis ?? (File.Exists(existingPath) ? Read<NativeRequest>(existingPath) : new(1, Guid.NewGuid().ToString("N"), 0, 0, false, []));
        if (existing.Modules.Length > 0) Validate(data, existing);
        var entries = existing.Modules.Where(x => x.Manifest.Id != moduleId).ToList();
        if (selected.Length > 0)
        {
            var source = package ?? existing.Modules.Single(x => x.Manifest.Id == moduleId).Directory;
            var sourcePrefix = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if ((Path.GetFullPath(data).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar).StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("Reload 데이터 폴더를 포함하는 모듈 원본은 사용할 수 없습니다.");
            var manifest = Module(source); if (manifest.Id != moduleId) throw new InvalidDataException("Revit 모듈 ID 불일치");
            var before = Hashes(source); var path = Child(data, "Packages/" + moduleId + "/" + Guid.NewGuid().ToString("N"));
            Copy(source, path); if (!Same(before, Hashes(source)) || !Same(before, Hashes(path))) throw new IOException("모듈 준비 중 파일 변경 감지");
            entries.Add(new(path, manifest, selected, before));
        }
        var request = new NativeRequest(1, Guid.NewGuid().ToString("N"), pid, ticks, false, entries.ToArray());
        Validate(data, request); if (publish) Save(Child(data, "pending.json"), request); return request;
    }
    public static void Publish(string data, NativeRequest request, string expectedId)
    {
        using var operation = new FileStream(Child(data, "prepare.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var file = File.Exists(Child(data, "pending.json")) ? Child(data, "pending.json") : Child(data, "active.json");
        if (!File.Exists(file) || Read<NativeRequest>(file).Id != expectedId) throw new IOException("준비 중 다른 Revit 변경 요청이 생겼습니다. 다시 준비하십시오.");
        Validate(data, request); Save(Child(data, "pending.json"), request);
    }
    public static bool Id(string? value) => value is not null && Regex.IsMatch(value, "^[a-z][a-z0-9.-]{0,63}$", RegexOptions.CultureInvariant);
}
