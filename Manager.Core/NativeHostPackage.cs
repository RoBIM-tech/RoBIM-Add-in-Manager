using System.IO.Compression;
using System.Security.Cryptography;

namespace RoBIM.Manager.Core;

/// <summary>Compare the installed fixed host with the verified bundled package without loading either DLL.</summary>
public static class NativeHostPackage
{
    public static bool Matches(ManagerPaths paths, string archive)
    {
        if (!File.Exists(archive + ".sha256") ||
            !SafeFiles.Hash(archive).Equals(File.ReadAllText(archive + ".sha256").Trim(), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Revit 연결 패키지 SHA-256 검증 실패");
        var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var zip = ZipFile.OpenRead(archive);
        var prefix = Catalog.RevitHost.FolderName + "/";
        foreach (var entry in zip.Entries.Where(x => !x.FullName.EndsWith('/')))
        {
            if (!entry.FullName.StartsWith(prefix, StringComparison.Ordinal) || entry.Length > 128 * 1024 * 1024)
                throw new IOException("Revit 연결 패키지 구성 오류");
            var relative = entry.FullName[prefix.Length..];
            _ = SafeFiles.Child(paths.Target(Catalog.RevitHost), relative);
            using var stream = entry.Open();
            if (!expected.TryAdd(relative, Convert.ToHexString(SHA256.HashData(stream))))
                throw new IOException("Revit 연결 패키지 중복 파일");
        }
        if (!expected.ContainsKey("Contents/2026/RoBIM.Revit.Host.dll"))
            throw new IOException("Revit 연결 프로그램이 없는 패키지입니다.");
        var target = paths.Target(Catalog.RevitHost);
        return Directory.Exists(target) && SafeFiles.Same(expected, SafeFiles.Hashes(target));
    }
}
