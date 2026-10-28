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
        ribbonEditor.RefreshRequested = async () => { if (!preview) await Run("리본 목록 확인", RefreshStatuses); RefreshRibbonEditor(); };
        ribbonEditor.ApplyRequested = async layout =>
        {
            await Task.Yield();
            return RequestAutomaticRevitApply(native: true, pyRevit: new RevitReload(paths).PendingSelection() is not null, layout: true);
        };
    }
    private void RefreshRibbonEditor()
    {
        var tools = RibbonLayouts.WithManagementTools(CommandInventory().Where(x => x.Kind is ButtonKind.PyRevit or ButtonKind.Native)
            .Select(x => RibbonLayouts.FromGroup(x.Id, x.Title, x.Group, x.Selected, x.Icon)));
        ribbonEditor?.LoadTools(tools);
    }
}
