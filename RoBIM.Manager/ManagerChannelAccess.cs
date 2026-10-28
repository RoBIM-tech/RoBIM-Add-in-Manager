using RoBIM.Manager.Core;
using RoBIM.Revit.Reload;

namespace RoBIM.Manager;

public sealed partial class ManagerForm
{
    private bool? renderedTestAccess;
    private bool CanUseTest => preview || teamAccess?.TestAllowed == true;
    private void RequireChannelAccess(string channel)
    {
        if (preview) return;
        if (teamAccess is null) throw new IOException("계정 권한을 먼저 확인하십시오.");
        teamAccess.Require(channel);
    }
    private NativeRevitCommands NativeCommands() => new(paths, requireChannel: RequireChannelAccess);
    private ExtensionCatalog ExtensionSources() => new(paths, RequireChannelAccess);
    private void RequirePackageAccess(ToolSpec tool, SourcePackage package)
    {
        if (tool.Kind == ToolKind.NativeRevit)
        {
            var module = NativeProtocol.Module(package.Path);
            RequireChannelAccess(ModuleReleases.Native(package.Path, module.Id, module.Version).Channel);
        }
        else if (tool.Kind == ToolKind.AutoCAD)
        {
            var root = CadReload.WindowsDirectory(package.Path);
            foreach (var module in RoBIM.Cad.Configuration.CadFiles.Catalog(root).Modules)
                RequireChannelAccess(ModuleReleases.Cad(root, module.Id, module.Version).Channel);
        }
        else if (tool.Kind == ToolKind.PyRevit) RequireChannelAccess("test");
    }
}
