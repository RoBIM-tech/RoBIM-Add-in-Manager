using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RoBIM.Manager.Core;

public sealed record PyRevitLocationSettings(int SchemaVersion, string Root, string[] PreviousRoots);
public sealed record PyRevitLocationResult(string Root, int MovedExtensions, string Backup);
public sealed class PyRevitLocation(ManagerPaths paths, Action? ensureClosed = null, string? iniFile = null)
{
    private static string Config(ManagerPaths paths) => SafeFiles.Child(paths.Data, "pyrevit-location.json");
    private static PyRevitLocationSettings Read(ManagerPaths paths)
    {
        var file = Config(paths);
        if (!File.Exists(file)) return new(1, Path.GetFullPath(paths.RevitExtensions), []);
        if (new FileInfo(file).Length > 65536) throw new IOException("설치 위치 설정 크기 오류");
        var value = JsonSerializer.Deserialize<PyRevitLocationSettings>(File.ReadAllText(file)) ?? throw new IOException("설치 위치 설정 오류");
        if (value.SchemaVersion != 1 || value.PreviousRoots is null || value.PreviousRoots.Length > 100) throw new IOException("설치 위치 설정 형식 오류");
        Validate(paths, value.Root); foreach (var root in value.PreviousRoots) Validate(paths, root); return value;
    }
    public static string Root(ManagerPaths paths) => Read(paths).Root;
    internal static bool HistoricalTarget(ManagerPaths paths, ToolSpec tool, string path) => Read(paths).PreviousRoots.Any(root => Path.GetFullPath(path).Equals(SafeFiles.Child(root, tool.FolderName), StringComparison.OrdinalIgnoreCase));
    private static string Validate(ManagerPaths paths, string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value)) throw new IOException("설치 폴더의 전체 경로를 지정하세요.");
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
        if (root.Equals(Path.TrimEndingDirectorySeparator(Path.GetPathRoot(root)!), StringComparison.OrdinalIgnoreCase) || root.EndsWith(".extension", StringComparison.OrdinalIgnoreCase)) throw new IOException("드라이브 루트나 .extension 폴더 대신 확장을 보관할 상위 폴더를 선택하세요.");
        var data = Path.TrimEndingDirectorySeparator(Path.GetFullPath(paths.Data));
        if (root.Equals(data, StringComparison.OrdinalIgnoreCase) || root.StartsWith(data + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || data.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Manager 데이터 폴더와 겹치는 위치에는 설치하지 않습니다.");
        SafeFiles.NoLinks(root); return root;
    }
    private static byte[]? Bytes(string file) { SafeFiles.NoLinks(file); return File.Exists(file) ? File.ReadAllBytes(file) : null; }
    private static void Write(string file, byte[]? bytes)
    {
        SafeFiles.NoLinks(file); if (bytes is null) { if (File.Exists(file)) File.Delete(file); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(file)!); var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temp, bytes); File.Move(temp, file, true); } finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static void RegisterSearchRoot(string file, string root)
    {
        var before = Bytes(file); var bytes = before ?? []; var bom = bytes.Length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191;
        var text = Encoding.UTF8.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0)); var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        if (text.Length > 0 && !text.EndsWith('\n')) text += newline;
        var sections = new Regex(@"(?mi)^\[core\][^\r\n]*\r?\n(?<body>(?:(?!^\[)[\s\S])*)"); var matches = sections.Matches(text);
        if (matches.Count > 1) throw new IOException("pyRevit core 설정이 중복되어 변경하지 않습니다.");
        var body = matches.Count == 1 ? matches[0].Groups["body"].Value : "";
        var keys = new Regex(@"(?mi)^[ \t]*userextensions[ \t]*=[^\r\n]*"); var entries = keys.Matches(body);
        if (entries.Count > 1) throw new IOException("pyRevit 확장 검색 경로 설정이 중복되어 변경하지 않습니다.");
        string[] roots;
        try { roots = entries.Count == 0 ? [] : JsonSerializer.Deserialize<string[]>(entries[0].Value.Split('=', 2)[1].Trim()) ?? throw new IOException("확장 경로 목록 오류"); }
        catch (JsonException e) { throw new IOException("기존 pyRevit 경로 목록을 읽지 못해 보존합니다.", e); }
        var list = roots.ToList(); if (!list.Any(x => Path.TrimEndingDirectorySeparator(x).Equals(root, StringComparison.OrdinalIgnoreCase))) list.Add(root);
        var setting = "userextensions = " + JsonSerializer.Serialize(list);
        var updated = entries.Count == 1 ? keys.Replace(body, setting) : setting + newline + body;
        text = matches.Count == 1 ? text[..matches[0].Groups["body"].Index] + updated + text[(matches[0].Groups["body"].Index + body.Length)..] : text + "[core]" + newline + updated;
        if (!(Bytes(file) ?? []).SequenceEqual(before ?? [])) throw new IOException("pyRevit 설정이 변경되어 다시 시도해야 합니다.");
        var encoding = new UTF8Encoding(bom); Write(file, (bom ? encoding.GetPreamble() : []).Concat(encoding.GetBytes(text)).ToArray());
    }
    public PyRevitLocationResult Change(string requested)
    {
        void Check() { if (ensureClosed is null) EnvironmentProbe.EnsureHostClosed(Catalog.Revit); else ensureClosed(); }
        Check(); Directory.CreateDirectory(paths.Data);
        using var reloadLock = new FileStream(SafeFiles.Child(paths.Data, "reload.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var operation = new FileStream(SafeFiles.Child(paths.Data, "operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var previous = Read(paths); var root = Validate(paths, requested); var oldRoot = Path.GetFullPath(previous.Root);
        Directory.CreateDirectory(root);
        var ini = iniFile ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pyRevit", "pyRevit_config.ini");
        var stateFile = SafeFiles.Child(paths.Data, "installed.json"); var pendingFile = SafeFiles.Child(paths.Data, "reload-pending.json");
        var metadata = new[] { stateFile, Config(paths), ini, pendingFile }.ToDictionary(f => f, Bytes);
        var backup = SafeFiles.Child(paths.Data, "LocationBackups/" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(backup);
        var index = 0; var metadataMap = new Dictionary<string, string?>();
        foreach (var pair in metadata)
        {
            var name = pair.Value is null ? null : "metadata-" + index++ + ".bin";
            metadataMap[pair.Key] = name;
            if (name is not null) File.WriteAllBytes(Path.Combine(backup, name), pair.Value!);
        }
        File.WriteAllText(Path.Combine(backup, "manifest.json"), JsonSerializer.Serialize(new { SchemaVersion = 1, PreviousRoot = oldRoot, NewRoot = root, MetadataFiles = metadataMap }));
        var state = new InstallService(paths).ReadState(); var moves = new List<(InstallRecord Record, string Stage, string Target, string Retired)>();
        var installed = new List<string>(); var retired = new List<(string Old, string Temporary)>(); var committed = false;
        var writtenMetadata = new HashSet<string>();
        void Commit(string file, byte[] bytes) { Write(file, bytes); writtenMetadata.Add(file); }
        try
        {
            if (!root.Equals(oldRoot, StringComparison.OrdinalIgnoreCase))
                foreach (var record in state.Values.Where(r => r.Id != Catalog.ReloadBridge.Id && r.Path.EndsWith(".extension", StringComparison.OrdinalIgnoreCase) && Path.GetDirectoryName(Path.GetFullPath(r.Path))!.Equals(oldRoot, StringComparison.OrdinalIgnoreCase)).ToArray())
                {
                    SafeFiles.NoLinks(record.Path);
                    if (!SafeFiles.Same(record.Hashes, SafeFiles.Hashes(record.Path))) throw new IOException("로컬 변경 파일을 보존하기 위해 설치 위치 이전을 중단합니다.");
                    var target = SafeFiles.Child(root, Path.GetFileName(record.Path)); if (Directory.Exists(target) || File.Exists(target)) throw new IOException("선택한 위치에 같은 이름의 확장이 있습니다: " + target);
                    var stage = SafeFiles.Child(root, ".robim-location-stage-" + Guid.NewGuid().ToString("N"));
                    var old = SafeFiles.Child(oldRoot, ".robim-relocated-" + Guid.NewGuid().ToString("N"));
                    moves.Add((record, stage, target, old)); SafeFiles.Copy(record.Path, stage, false);
                    SafeFiles.Copy(record.Path, Path.Combine(backup, Path.GetFileName(record.Path)), false);
                    if (!SafeFiles.Same(record.Hashes, SafeFiles.Hashes(stage))) throw new IOException("설치 위치 복사 검증 실패");
                }
            Check();
            foreach (var move in moves)
            {
                if (!SafeFiles.Same(move.Record.Hashes, SafeFiles.Hashes(move.Record.Path))) throw new IOException("이전 중 원본 파일이 변경되었습니다.");
                Directory.Move(move.Stage, move.Target); installed.Add(move.Target);
                Directory.Move(move.Record.Path, move.Retired); retired.Add((move.Record.Path, move.Retired));
                state[move.Record.Id] = move.Record with { Path = move.Target };
            }
            Check();
            foreach (var pair in metadata) if (!(Bytes(pair.Key) ?? []).SequenceEqual(pair.Value ?? [])) throw new IOException("이전 중 설정 파일이 변경되어 보존합니다.");
            RegisterSearchRoot(ini, root); writtenMetadata.Add(ini);
            Commit(Config(paths), JsonSerializer.SerializeToUtf8Bytes(new PyRevitLocationSettings(1, root, previous.PreviousRoots.Append(oldRoot).Distinct(StringComparer.OrdinalIgnoreCase).ToArray())));
            Commit(stateFile, JsonSerializer.SerializeToUtf8Bytes(state));
            if (metadata[pendingFile] is { } pendingBytes)
            {
                var pending = JsonSerializer.Deserialize<ReloadPackage>(pendingBytes) ?? throw new IOException("준비 기록 오류");
                var moved = moves.FirstOrDefault(m => m.Record.Path.Equals(pending.SelectionSource, StringComparison.OrdinalIgnoreCase));
                if (moved.Record is not null) Commit(pendingFile, JsonSerializer.SerializeToUtf8Bytes(pending with { SelectionSource = moved.Target }));
            }
            foreach (var move in moves) if (!SafeFiles.Same(move.Record.Hashes, SafeFiles.Hashes(move.Target))) throw new IOException("이전 완료 파일 검증 실패");
            committed = true;
        }
        catch
        {
            foreach (var pair in metadata.Where(p => writtenMetadata.Contains(p.Key))) Write(pair.Key, pair.Value);
            foreach (var pair in retired.AsEnumerable().Reverse()) { if (Directory.Exists(pair.Temporary) && !Directory.Exists(pair.Old)) Directory.Move(pair.Temporary, pair.Old); }
            foreach (var target in installed) { var move = moves.Single(m => m.Target == target); if (Directory.Exists(target) && SafeFiles.Same(move.Record.Hashes, SafeFiles.Hashes(target))) SafeFiles.DeleteOwnedTree(root, target); }
            throw;
        }
        finally
        {
            foreach (var move in moves) if (Directory.Exists(move.Stage)) SafeFiles.DeleteOwnedTree(root, move.Stage);
            if (committed) foreach (var pair in retired) { try { SafeFiles.DeleteOwnedTree(oldRoot, pair.Temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
        return new(root, moves.Count, backup);
    }
}
