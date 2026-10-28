using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace RoBIM.Manager.Core;

public sealed record RevitButton(string Id, string Title, string Description, string Group, string? Icon);
public sealed record ButtonSourceCache(string Root, Dictionary<string, string> Hashes);
public sealed record RevitButtonSelection(int SchemaVersion, RevitButton[] Buttons, string[] Selected, ButtonSourceCache? Cache = null);

// Reads metadata only. Command scripts and compiled assemblies are never executed for discovery.
public static class RevitButtons
{
    public const string SelectionFile = "robim-button-selection.json";
    private const string DisabledDirectory = ".robim-disabled";
    private const string DisabledManifest = "robim-disabled-bundles.json";
    public static string ResourcePath(string source, string relative)
    {
        var active = SafeFiles.Child(source, relative);
        if (File.Exists(active)) return active;
        var cached = ReadSelection(source)?.Cache;
        return cached is not null ? SafeFiles.Child(cached.Root, relative) : SafeFiles.Child(source, DisabledDirectory + "/" + relative);
    }
    private static readonly HashSet<string> Commands = new(StringComparer.OrdinalIgnoreCase) { ".pushbutton", ".linkbutton", ".smartbutton", ".urlbutton", ".content" };
    private static readonly HashSet<string> Containers = new(StringComparer.OrdinalIgnoreCase) { ".tab", ".panel", ".stack", ".pulldown", ".splitbutton", ".splitpushbutton" };
    public static YamlMappingNode Metadata(string folder)
    {
        var path = SafeFiles.Child(folder, "bundle.yaml");
        if (!File.Exists(path)) return new();
        if (new FileInfo(path).Length > 256 * 1024) throw new InvalidDataException("버튼 설명 파일이 너무 큽니다.");
        var text = File.ReadAllText(path);
        var parser = new Parser(new StringReader(text)); var count = 0;
        while (parser.MoveNext()) if (++count > 20000 || parser.Current is AnchorAlias) throw new InvalidDataException("지원하지 않는 YAML 별칭 또는 크기");
        var yaml = new YamlStream(); yaml.Load(new StringReader(text));
        if (yaml.Documents.Count == 0) return new();
        if (yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not YamlMappingNode map) throw new InvalidDataException("버튼 YAML 구조 오류");
        return map;
    }
    private static YamlNode? Value(YamlMappingNode map, string name) => map.Children.TryGetValue(new YamlScalarNode(name), out var value) ? value : null;
    public static string Text(YamlMappingNode map, string name, string fallback = "")
    {
        var node = Value(map, name);
        if (node is YamlMappingNode locale) node = Value(locale, "ko") ?? Value(locale, "ko_kr") ?? Value(locale, "en_us") ?? locale.Children.Values.FirstOrDefault();
        return node is YamlScalarNode scalar ? (scalar.Value ?? fallback).Replace("\r", "").Replace("\n", " ").Trim() : fallback;
    }
    private static string Stem(string path) => Path.GetFileNameWithoutExtension(path);
    private static string LayoutName(string text) => text.Contains('[') ? text[..text.IndexOf('[')] : text;
    private static string[]? Layout(YamlMappingNode map) => Value(map, "layout") is YamlSequenceNode seq ? seq.Children.OfType<YamlScalarNode>().Select(x => x.Value ?? "").ToArray() : null;
    public static RevitButton[] Read(string source)
    {
        SafeFiles.NoLinks(source);
        var stored = SafeFiles.Child(source, SelectionFile);
        if (File.Exists(stored)) return ReadSelection(source)!.Buttons;
        var result = new List<RevitButton>();
        void Visit(string directory, string group, int depth)
        {
            if (depth > 10) throw new InvalidDataException("버튼 폴더 깊이 초과");
            SafeFiles.NoLinks(directory); var metadata = Metadata(directory); var layout = Layout(metadata);
            var children = Directory.GetDirectories(directory).Where(x => Commands.Contains(Path.GetExtension(x)) || Containers.Contains(Path.GetExtension(x))).ToArray();
            if (depth == 0) children = children.Where(x => Path.GetExtension(x) == ".tab" && !Stem(x).Contains("TEST", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (layout is { Length: > 0 }) children = children.Where(x => layout.Any(name => LayoutName(name) == Stem(x))).OrderBy(x => Array.FindIndex(layout, name => LayoutName(name) == Stem(x))).ToArray();
            foreach (var child in children)
            {
                var relative = Path.GetRelativePath(source, child).Replace('\\', '/'); _ = SafeFiles.Child(source, relative);
                var meta = Metadata(child); var label = Text(meta, "title", Stem(child));
                var nextGroup = Path.GetExtension(child) is ".tab" or ".panel" ? string.Join(" / ", new[] { group, label }.Where(x => x.Length > 0)) : group;
                if (Commands.Contains(Path.GetExtension(child)))
                {
                    var icon = File.Exists(Path.Combine(child, "icon.png")) ? relative + "/icon.png" : null;
                    var description = Text(meta, "tooltip");
                    if (description.Length == 0) description = ScriptLiteral(child, "__doc__");
                    if (description.Length == 0) description = LegacyDescription(relative);
                    result.Add(new(relative, label, description.Length == 0 ? "등록된 기능 설명이 없습니다." : description, group, icon));
                    if (result.Count > 2048) throw new InvalidDataException("버튼 수 초과");
                }
                else Visit(child, nextGroup, depth + 1);
            }
        }
        Visit(source, "", 0); return result.ToArray();
    }
    // Parse a string literal only; never import Python or evaluate an expression.
    private static string ScriptLiteral(string folder, string name)
    {
        var file = SafeFiles.Child(folder, "script.py"); if (!File.Exists(file)) return "";
        using var reader = new StreamReader(file); var buffer = new char[65536]; var text = new string(buffer, 0, reader.ReadBlock(buffer, 0, buffer.Length));
        var match = Regex.Match(text, @"(?m)^" + Regex.Escape(name) + @"\s*=\s*[uUrR]{0,2}", RegexOptions.None, TimeSpan.FromSeconds(1));
        if (!match.Success || match.Index + match.Length >= text.Length) return "";
        var index = match.Index + match.Length; var quote = text[index]; if (quote is not ('\'' or '"')) return "";
        var triple = index + 2 < text.Length && text[index + 1] == quote && text[index + 2] == quote; index += triple ? 3 : 1;
        var result = new StringBuilder();
        while (index < text.Length && result.Length < 8000)
        {
            var current = text[index++];
            if (current == '\\' && index < text.Length) { var next = text[index++]; result.Append(next is 'n' or 'r' or 't' ? ' ' : next); continue; }
            if (current == quote && (!triple || index + 1 < text.Length && text[index] == quote && text[index + 1] == quote)) return result.ToString().Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (!triple && current is '\r' or '\n') return "";
            result.Append(current);
        }
        return "";
    }
    private static string LegacyDescription(string id) => id switch
    {
        "RoBIM 기본.tab/Rooms.panel/RoomNameCopy.pushbutton" => "룸 이름을 지정한 문자 파라미터로 복사합니다. 원래 Room Name은 유지하며 적용 범위와 처리 결과를 확인할 수 있습니다.",
        "RoBIM 도면.tab/Tags.panel/RoomTagTypeChange.pushbutton" => "선택한 뷰에서 기존 태그 유형과 룸 이름이 일치하는 룸 태그를 지정한 유형으로 변경합니다.",
        _ => ""
    };
    public static RevitButtonSelection? ReadSelection(string source)
    {
        var file = SafeFiles.Child(source, SelectionFile); if (!File.Exists(file)) return null;
        if (new FileInfo(file).Length > 2 * 1024 * 1024) throw new InvalidDataException("버튼 선택 기록 크기 초과");
        var state = JsonSerializer.Deserialize<RevitButtonSelection>(File.ReadAllText(file)) ?? throw new InvalidDataException("버튼 선택 기록 오류");
        if (state.SchemaVersion != 1 || state.Buttons is null || state.Selected is null || state.Buttons.Length > 2048 || state.Buttons.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != state.Buttons.Length || state.Selected.Distinct().Count() != state.Selected.Length || state.Selected.Any(id => !state.Buttons.Any(x => x.Id == id))) throw new InvalidDataException("버튼 선택 기록의 ID 오류");
        foreach (var button in state.Buttons)
        {
            if (!Commands.Contains(Path.GetExtension(button.Id)) || (state.Cache is null && !Directory.Exists(SafeFiles.Child(source, button.Id)) &&
                !Directory.Exists(SafeFiles.Child(source, DisabledDirectory + "/" + button.Id)))) throw new InvalidDataException("버튼 경로 오류");
            _ = SafeFiles.Child(source, button.Id);
            if (state.Selected.Contains(button.Id) && !Directory.Exists(SafeFiles.Child(source, button.Id))) throw new InvalidDataException("선택 버튼의 활성 경로가 없습니다.");
            if (button.Icon is not null) _ = SafeFiles.Child(source, button.Icon);
        }
        return state;
    }
    private const string CacheProof = ".robim-source-proof.json";
    private static void ValidateCache(ButtonSourceCache cache)
    {
        if (!Path.IsPathFullyQualified(cache.Root) || !Path.GetFileName(cache.Root).StartsWith(".robim-button-source-", StringComparison.Ordinal) || cache.Hashes is null)
            throw new IOException("버튼 원본 캐시 경로 오류");
        SafeFiles.NoLinks(cache.Root);
        var proof = SafeFiles.Child(cache.Root, CacheProof);
        if (!File.Exists(proof) || new FileInfo(proof).Length > 2 * 1024 * 1024) throw new IOException("버튼 원본 캐시 확인 정보 누락");
        var recorded = JsonSerializer.Deserialize<ButtonSourceCache>(File.ReadAllText(proof)) ?? throw new IOException("버튼 원본 캐시 확인 오류");
        if (recorded.Root != cache.Root || !SafeFiles.Same(recorded.Hashes, cache.Hashes)) throw new IOException("버튼 원본 캐시 확인 정보 불일치");
        var actual = SafeFiles.Hashes(cache.Root); actual.Remove(CacheProof);
        if (!SafeFiles.Same(actual, cache.Hashes)) throw new IOException("버튼 원본 캐시가 변경되었습니다.");
    }
    public static bool ValidEmptySelection(string source)
    {
        var selection = ReadSelection(source);
        if (selection is not { Selected.Length: 0, Cache: not null }) return false;
        ValidateCache(selection.Cache); return true;
    }
    public static SourcePackage Prepare(SourcePackage source, string output, IReadOnlyList<string> selected)
    {
        var buttons = Read(source.Path); var known = buttons.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selected.Count != selected.Distinct(StringComparer.OrdinalIgnoreCase).Count() || selected.Any(x => !known.Contains(x))) throw new InvalidDataException("존재하지 않거나 중복된 Revit 버튼");
        if (Directory.Exists(output)) throw new IOException("버튼 설치 준비 폴더가 이미 있습니다.");
        var before = SafeFiles.Hashes(source.Path);
        var cache = ReadSelection(source.Path)?.Cache;
        if (cache is not null)
        {
            ValidateCache(cache);
            foreach (var pair in before)
            {
                if (pair.Key == SelectionFile || pair.Key == "extension.json" || pair.Key.EndsWith("bundle.yaml", StringComparison.OrdinalIgnoreCase)) continue;
                if (!cache.Hashes.TryGetValue(pair.Key, out var original) || original != pair.Value)
                    throw new IOException("설치 사본이 원본 캐시와 다릅니다. 변경 파일을 보존하고 새 배포본에서 다시 준비하십시오: " + pair.Key);
            }
        }
        SafeFiles.Copy(cache?.Root ?? source.Path, output, false);
        if (File.Exists(Path.Combine(output, CacheProof))) File.Delete(Path.Combine(output, CacheProof));
        if (!SafeFiles.Same(before, SafeFiles.Hashes(source.Path))) throw new IOException("준비 중 원본이 변경되어 적용하지 않습니다.");
        var disabledManifest = SafeFiles.Child(output, DisabledManifest);
        if (File.Exists(disabledManifest))
        {
            var disabled = JsonSerializer.Deserialize<string[]>(File.ReadAllText(disabledManifest)) ?? throw new IOException("비활성 버튼 기록 오류");
            if (disabled.Length > 4096 || disabled.Distinct(StringComparer.OrdinalIgnoreCase).Count() != disabled.Length) throw new IOException("비활성 버튼 기록 크기/중복 오류");
            foreach (var id in disabled)
            {
                if (!Commands.Contains(Path.GetExtension(id))) throw new IOException("비활성 버튼 형식 오류");
                var original = SafeFiles.Child(output, id); var saved = SafeFiles.Child(output, DisabledDirectory + "/" + id);
                if (Directory.Exists(original) || !Directory.Exists(saved)) throw new IOException("비활성 버튼 경로 충돌");
                Directory.CreateDirectory(Path.GetDirectoryName(original)!); Directory.Move(saved, original);
            }
        }
        if (Directory.Exists(Path.Combine(output, DisabledDirectory))) SafeFiles.DeleteOwnedTree(output, Path.Combine(output, DisabledDirectory));
        if (File.Exists(disabledManifest)) File.Delete(disabledManifest);
        if (cache is null)
        {
            var cacheRoot = SafeFiles.Child(Path.GetDirectoryName(Path.GetFullPath(output))!, ".robim-button-source-" + Guid.NewGuid().ToString("N"));
            SafeFiles.Copy(output, cacheRoot, false);
            var selection = Path.Combine(cacheRoot, SelectionFile); if (File.Exists(selection)) File.Delete(selection);
            cache = new(cacheRoot, SafeFiles.Hashes(cacheRoot));
            File.WriteAllText(Path.Combine(cacheRoot, CacheProof), JsonSerializer.Serialize(cache));
        }
        IEnumerable<string> AllContainers(string directory, int depth)
        {
            if (depth > 10) throw new InvalidDataException("버튼 폴더 깊이 초과");
            foreach (var child in Directory.GetDirectories(directory).Where(x => Containers.Contains(Path.GetExtension(x))))
            {
                yield return Path.GetRelativePath(output, child).Replace('\\', '/');
                foreach (var nested in AllContainers(child, depth + 1)) yield return nested;
            }
        }
        // pyRevit does not consistently enforce an extension-root tab layout. Explicitly
        // empty every unselected container, including TEST tabs absent from the catalog.
        var directories = buttons.SelectMany(b => Ancestors(b.Id)).Concat(AllContainers(output, 0)).Append("").Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(x => x.Length);
        foreach (var relative in directories)
        {
            var directory = relative.Length == 0 ? output : SafeFiles.Child(output, relative); var meta = Metadata(directory);
            var desired = selected.Where(x => relative.Length == 0 || x.StartsWith(relative + "/", StringComparison.OrdinalIgnoreCase))
                .Select(x => x[(relative.Length == 0 ? 0 : relative.Length + 1)..].Split('/')[0]).Distinct(StringComparer.OrdinalIgnoreCase).Select(Stem).ToArray();
            var original = Layout(meta) ?? Directory.GetDirectories(directory).Select(Stem).ToArray();
            var names = original.Where(x => desired.Contains(LayoutName(x), StringComparer.OrdinalIgnoreCase)).Concat(desired.Where(x => !original.Any(o => LayoutName(o) == x))).Distinct().ToArray();
            // pyRevit treats layout: [] as 'show all'. A nonmatching sentinel hides an empty container.
            meta.Children[new YamlScalarNode("layout")] = new YamlSequenceNode((names.Length == 0 ? ["__ROBIM_NO_SELECTED_BUTTONS__"] : names).Select(x => (YamlNode)new YamlScalarNode(x)));
            var yaml = new YamlStream(new YamlDocument(meta)); using var writer = new StringWriter(); yaml.Save(writer, false); File.WriteAllText(Path.Combine(directory, "bundle.yaml"), writer.ToString());
        }
        IEnumerable<string> CommandDirectories(string directory, int depth)
        {
            if (depth > 10) throw new InvalidDataException("버튼 폴더 깊이 초과");
            foreach (var child in Directory.GetDirectories(directory))
            {
                var extension = Path.GetExtension(child);
                if (Commands.Contains(extension)) yield return child;
                else if (Containers.Contains(extension)) foreach (var nested in CommandDirectories(child, depth + 1)) yield return nested;
            }
        }
        // An uninstall removes the actual command bundle from the installation. A verified
        // original package cache lives outside the installation for later reinstallation.
        foreach (var command in CommandDirectories(output, 0).ToArray())
        {
            var id = Path.GetRelativePath(output, command).Replace('\\', '/');
            if (selected.Contains(id, StringComparer.OrdinalIgnoreCase))
            {
                // IronPython test harness projects are development assets. Their original
                // sources remain in the verified package cache, outside the live extension.
                var harness = SafeFiles.Child(command, "tests/ironpython");
                if (Directory.Exists(harness) && Directory.GetFiles(harness, "*.csproj").Length > 0)
                    SafeFiles.DeleteOwnedTree(Path.GetDirectoryName(harness)!, harness);
                continue;
            }
            SafeFiles.DeleteOwnedTree(Path.GetDirectoryName(command)!, command);
        }
        // Empty pyRevit containers can reappear on startup even with a sentinel layout.
        // Keep the complete reinstall source in the verified cache, outside the live extension.
        var retained = selected.SelectMany(Ancestors).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var relative in AllContainers(output, 0).OrderByDescending(x => x.Length).ToArray())
            if (!retained.Contains(relative))
            {
                var obsolete = SafeFiles.Child(output, relative);
                SafeFiles.DeleteOwnedTree(Path.GetDirectoryName(obsolete)!, obsolete);
            }
        File.WriteAllText(Path.Combine(output, SelectionFile), JsonSerializer.Serialize(new RevitButtonSelection(1, buttons, selected.ToArray(), cache)));
        File.WriteAllText(Path.Combine(output, "extension.json"), JsonSerializer.Serialize(new { name = "RoBIM-Selected", type = "extension", default_enabled = false, description = "RoBIM 버튼별 선택 설치" }));
        return new(output, source.Version);
    }
    private static IEnumerable<string> Ancestors(string relative)
    {
        var parts = relative.Split('/'); for (var count = parts.Length - 1; count > 0; count--) yield return string.Join('/', parts.Take(count));
    }
}
