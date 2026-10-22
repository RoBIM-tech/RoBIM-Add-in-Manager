using System.Runtime.InteropServices;
using System.Text;

namespace RoBIM.Manager.Core;

public static class WindowsDataPath
{
    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPath(Microsoft.Win32.SafeHandles.SafeFileHandle handle, StringBuilder path, uint length, uint flags);
    private static string Normalize(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) path = @"\\" + path[8..];
        else if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) path = path[4..];
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
    public static void VerifyFileLocation(string logical, string physical)
    {
        if (!Normalize(logical).Equals(Normalize(physical), StringComparison.OrdinalIgnoreCase))
            throw new IOException("현재 실행 환경이 RoBIM 설치 데이터를 앱 전용 폴더로 연결하고 있습니다. 이 상태에서는 Revit이 설치 파일을 찾지 못하므로 변경하지 않습니다. 매니저를 닫고 Windows 시작 메뉴에서 RoBIM Add-in Manager를 실행하십시오.");
    }
    public static string EnsureDirectory(string directory)
    {
        SafeFiles.NoLinks(directory); Directory.CreateDirectory(directory);
        var probe = Path.Combine(directory, ".robim-path-check-" + Guid.NewGuid().ToString("N"));
        using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read | FileShare.Delete, 1, FileOptions.DeleteOnClose);
        if (!OperatingSystem.IsWindows()) return Path.GetFullPath(directory);
        var value = new StringBuilder(32768);
        var length = GetFinalPath(stream.SafeFileHandle, value, (uint)value.Capacity, 0);
        if (length == 0 || length >= value.Capacity) throw new IOException("설치 데이터의 실제 저장 경로를 확인하지 못했습니다.");
        VerifyFileLocation(probe, value.ToString());
        return Path.GetDirectoryName(Normalize(value.ToString()))!;
    }
}
