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
            return new RibbonLayouts(paths).RequestRevit(false);
        };
    }
    private void RefreshRibbonEditor()
    {
        var tools = CommandInventory().Where(x => x.Kind is ButtonKind.PyRevit or ButtonKind.Native).Select(x =>
        {
            var group = x.Group.Split(" / ", StringSplitOptions.TrimEntries);
            return new RibbonTool(x.Id, x.Title, group.Length > 1 ? group[0] : "RoBIM 도구", group.Length > 1 ? group[1] : "도구", x.Selected, x.Icon);
        });
        ribbonEditor?.LoadTools(tools);
    }
}
