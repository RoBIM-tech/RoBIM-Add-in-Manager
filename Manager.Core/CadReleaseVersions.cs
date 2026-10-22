using System.Text.Json;
using RoBIM.Cad.Configuration;

namespace RoBIM.Manager.Core;

// Display provenance follows the immutable selected module, not the older stable loader install.
public sealed class CadReleaseVersions(ManagerPaths paths)
{
    private sealed record Entry(string Version, string Hash);
    private string FilePath => SafeFiles.Child(paths.Data, "Cad/release-versions.json");
    private Dictionary<string, Entry> Read()
    {
        if (!File.Exists(FilePath)) return [];
        SafeFiles.NoLinks(FilePath);
        if (new FileInfo(FilePath).Length > 128 * 1024) throw new IOException("CAD 버전 기록 크기 오류");
        return JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(FilePath)) ?? [];
    }
    public string Selected(CadButtonSource source, CadModule module, string fallback)
    {
        var entry = Read().GetValueOrDefault(module.Id);
        return entry is not null && entry.Hash == SafeFiles.Hash(CadFiles.Child(source.Root, module.Assembly)) ? entry.Version : fallback;
    }
    public void Record(CadButtonSource source, string version)
    {
        var entries = Read();
        foreach (var module in source.Catalog.Modules) entries[module.Id] = new(version, SafeFiles.Hash(CadFiles.Child(source.Root, module.Assembly)));
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + "." + Guid.NewGuid().ToString("N");
        try { File.WriteAllText(temp, JsonSerializer.Serialize(entries)); File.Move(temp, FilePath, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
