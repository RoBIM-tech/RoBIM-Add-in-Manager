using System.Text.Json;
namespace RoBIM.Manager.Core;

public static class RevitRuntimePolicy
{
    // RoBIM no longer ships or operates pyRevit. Ignore legacy user mode files;
    // they may be corrupt or left by a previous installation and must not crash CAD UI.
    public static bool NativeOnly(ManagerPaths paths) => true;
    public static bool LegacyRoBIM(ToolSpec tool) => tool.Id == Catalog.Revit.Id || tool.Id == Catalog.RevitDeveloper.Id || tool.Id == Catalog.RevitSelected.Id || tool.Id == Catalog.ReloadBridge.Id || tool.Kind == ToolKind.PyRevit && (tool.Repository.Equals(Catalog.Revit.Repository, StringComparison.OrdinalIgnoreCase) || tool.FolderName.Equals(Catalog.ReloadBridge.FolderName, StringComparison.OrdinalIgnoreCase));
    public static void EnsureAllowed(ManagerPaths paths, ToolSpec tool)
    {
        if (tool.Kind == ToolKind.PyRevit || LegacyRoBIM(tool)) throw new IOException("RoBIM pyRevit은 사용 종료했습니다. Revit Add-in을 설치하십시오. 리본·Reload는 Native Host가 처리합니다.");
    }
}
