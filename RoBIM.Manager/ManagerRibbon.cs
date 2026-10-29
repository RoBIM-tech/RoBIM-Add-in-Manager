using RoBIM.Manager.Core;

namespace RoBIM.Manager;

public sealed partial class ManagerForm
{
    private readonly Panel ribbonPage = new() { Dock = DockStyle.Fill };
    private RibbonEditorControl? ribbonEditor;
    private void BuildRibbonPage()
    {
        ribbonEditor = new(new RibbonLayouts(paths), preview);
        ribbonPage.Controls.Add(ribbonEditor);
        var connection = MakeButton("Revit 연결 설치 / 업데이트");
        connection.AutoSize = true;
        connection.AccessibleName = "Revit 리본 연결 설치 및 업데이트";
        var connectionBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        connectionBar.Controls.Add(connection);
        connectionBar.Controls.Add(Note("리본 편집이 적용되지 않으면 Revit을 저장하고 종료한 뒤 연결을 업데이트하세요."));
        ribbonPage.Controls.Add(connectionBar);
        connection.Click += (_, _) => { if (!preview) SwitchPage("Loaders"); };
        ribbonEditor.RefreshRequested = async () => { if (!preview) await Run("리본 목록 확인", RefreshStatuses); RefreshRibbonEditor(); };
        ribbonEditor.ApplyRequested = async layout =>
        {
            await Task.Yield();
            if (RevitRuntimePolicy.NativeOnly(paths)) RequireInstalledRevitLoader();
            return RequestAutomaticRevitApply(native: true, pyRevit: false, layout: true);
        };
    }
    private void RequireInstalledRevitLoader()
    {
        var host = installer.Status(Catalog.RevitHost);
        var dll = Path.Combine(host.Path, "Contents", "2026", "RoBIM.Revit.Host.dll");
        if (!host.Managed || host.Modified || host.External || !File.Exists(dll))
            throw new IOException("프로그램 로더 화면에서 Revit 로더 설치 상태를 확인하고 적용하십시오.");
    }
    private void RefreshRibbonEditor()
    {
        var tools = RibbonLayouts.WithManagementTools(CommandInventory().Where(x => x.Kind == ButtonKind.Native)
            .Select(x => RibbonLayouts.FromGroup(x.Id, x.Title, x.Group, x.Selected, x.Icon)));
        ribbonEditor?.LoadTools(tools);
    }
}
