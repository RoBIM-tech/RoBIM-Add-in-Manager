using System.IO.Compression;
using System.Security.Cryptography;

namespace RoBIM.Manager.Core;

public static class SafeFiles
{
    public static string Child(string root, string relative)
    {
        if (Path.IsPathRooted(relative) || relative.Contains(':') || relative.Split('/', '\\').Any(x =>
                x == ".." || x == "." || x.EndsWith(' ') || x.EndsWith('.') || x.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new InvalidDataException("패키지에 허용되지 않는 경로가 있습니다.");
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("경로가 허용된 폴더를 벗어납니다.");
        NoLinks(path);
        return path;
    }

    public static void NoLinks(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("심볼릭 링크·연결 폴더에는 설치하거나 삭제하지 않습니다: " + current);
    }

    public static IEnumerable<string> Files(string root)
    {
        NoLinks(root);
        foreach (var path in Directory.EnumerateFileSystemEntries(root))
        {
            NoLinks(path);
            if (Directory.Exists(path))
            {
                if (Path.GetFileName(path).Equals(".git", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var file in Files(path)) yield return file;
            }
            else yield return path;
        }
    }

    public static Dictionary<string, string> Hashes(string root) => Files(root).ToDictionary(
        x => Path.GetRelativePath(root, x).Replace('\\', '/'), Hash, StringComparer.OrdinalIgnoreCase);

    public static string Hash(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static bool Same(Dictionary<string, string> a, Dictionary<string, string> b) =>
        a.Count == b.Count && a.All(x => b.TryGetValue(x.Key, out var hash) && hash == x.Value);

    public static void Copy(string source, string destination, bool includeGit = true)
    {
        NoLinks(source);
        NoLinks(destination);
        Directory.CreateDirectory(destination);
        foreach (var path in Directory.EnumerateFileSystemEntries(source))
        {
            NoLinks(path);
            if (!includeGit && Path.GetFileName(path).Equals(".git", StringComparison.OrdinalIgnoreCase)) continue;
            var target = Child(destination, Path.GetFileName(path));
            if (Directory.Exists(path)) Copy(path, target, includeGit);
            else File.Copy(path, target, false);
        }
    }

    public static void DeleteOwnedTree(string parent, string path)
    {
        var expected = Child(parent, Path.GetFileName(path));
        if (!Path.GetFullPath(path).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("삭제 경로 검증 실패");
        if (!Directory.Exists(path)) return;
        // Enumerate links before the first mutation, including .git.
        CheckTree(path);
        foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(path, true);
    }

    private static void CheckTree(string root)
    {
        NoLinks(root);
        foreach (var path in Directory.EnumerateFileSystemEntries(root))
        {
            NoLinks(path);
            if (Directory.Exists(path)) CheckTree(path);
        }
    }

    public static void Extract(string archive, string destination)
    {
        Directory.CreateDirectory(destination);
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count > 25000 || zip.Entries.Sum(x => x.Length) > 1024L * 1024 * 1024)
            throw new InvalidDataException("패키지 크기 제한을 초과했습니다.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("패키지의 심볼릭 링크는 지원하지 않습니다.");
            var relative = entry.FullName.Replace('\\', '/');
            var target = Child(destination, relative);
            if (!seen.Add(target)) throw new InvalidDataException("패키지에 중복 경로가 있습니다.");
            if (relative.EndsWith('/')) Directory.CreateDirectory(target);
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var input = entry.Open();
                using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write);
                input.CopyTo(output);
            }
        }
    }
}
