using System.Diagnostics;

namespace RoBIM.Manager;

public sealed partial class ManagerForm
{
    private static string NavisworksStatus()
    {
        var autodesk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Autodesk");
        var installed = Directory.Exists(autodesk) ? Directory.GetDirectories(autodesk,"Navisworks*")
            .Select(directory => Path.Combine(directory,"Roamer.exe")).Where(File.Exists)
            .Select(file => Path.GetFileName(Path.GetDirectoryName(file)) + " · " + FileVersionInfo.GetVersionInfo(file).FileVersion).ToArray() : [];
        return "등록된 Navisworks 도구가 없습니다.\n" + (installed.Length == 0 ? "Navisworks 기본 설치 경로를 찾지 못했습니다." : string.Join("\n",installed));
    }
}
