using RoBIM.Common;
using RoBIM.Revit.Reload;
using RoBIM.Cad.Configuration;
using System.Text.Json;

namespace RoBIM.Manager.Core;

public static class ModuleReleases
{
    public static ReleaseStatus Native(string root, string id, string version)
    {
        var file = SafeFiles.Child(root, ReleaseMetadata.FileName);
        return ReleaseMetadata.Resolve(File.Exists(file) ? Read(file) : null, id, version);
    }
    public static ReleaseStatus Cad(string root, string id, string version)
    {
        // Embed maturity in robim-cad.json so it travels in the existing immutable snapshot.
        var file = SafeFiles.Child(root, CadFiles.ManifestName);
        SafeFiles.NoLinks(file);
        if (new FileInfo(file).Length > 4 * 1024 * 1024) throw new InvalidDataException("CAD 메타데이터 크기 초과");
        using var document = JsonDocument.Parse(File.ReadAllText(file));
        var module = document.RootElement.GetProperty("modules").EnumerateArray().Single(x => x.GetProperty("id").GetString() == id);
        if (!module.TryGetProperty("channel", out _)) return ReleaseMetadata.Resolve(null, id, version);
        var text = "{\"schemaVersion\":1,\"modules\":[" + module.GetRawText() + "]}";
        return ReleaseMetadata.Resolve(ReleaseMetadata.Parse(text), id, version);
    }
    private static ReleaseManifest Read(string file)
    {
        SafeFiles.NoLinks(file);
        if (new FileInfo(file).Length > 128 * 1024) throw new InvalidDataException("배포 상태 파일 크기 초과");
        return ReleaseMetadata.Parse(File.ReadAllText(file));
    }
    public static void ValidateNative(string root)
    {
        var module = NativeProtocol.Module(root);
        _ = Native(root, module.Id, module.Version);
    }
}
