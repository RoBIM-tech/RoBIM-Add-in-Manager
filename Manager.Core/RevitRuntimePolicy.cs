using System.Text.Json;
namespace RoBIM.Manager.Core;

public static class RevitRuntimePolicy
{
    public static bool NativeOnly(ManagerPaths paths)
    {
        var file = SafeFiles.Child(paths.Data, "revit-mode.json");
        if (!File.Exists(file)) return false;
        if (new FileInfo(file).Length > 65536) throw new IOException("Revit 모드 설정 크기 오류");
        using var json = JsonDocument.Parse(File.ReadAllText(file));
        return json.RootElement.TryGetProperty("Mode", out var mode) && mode.GetString() == "NativeOnly";
    }
    public static bool LegacyRoBIM(ToolSpec tool) => tool.Id == Catalog.Revit.Id || tool.Id == Catalog.RevitDeveloper.Id || tool.Id == Catalog.RevitSelected.Id || tool.Id == Catalog.ReloadBridge.Id || tool.Kind == ToolKind.PyRevit && (tool.Repository.Equals(Catalog.Revit.Repository, StringComparison.OrdinalIgnoreCase) || tool.FolderName.Equals(Catalog.ReloadBridge.FolderName, StringComparison.OrdinalIgnoreCase));
    public static void EnsureAllowed(ManagerPaths paths, ToolSpec tool)
    {
        if (LegacyRoBIM(tool) && NativeOnly(paths)) throw new IOException("RoBIM pyRevit은 사용 종료했습니다. 독립 Add-in을 설치하십시오. 리본·Reload는 Native Host가 처리합니다.");
    }
}
