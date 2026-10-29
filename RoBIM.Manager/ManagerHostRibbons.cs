using RoBIM.Cad.Configuration;
using RoBIM.Manager.Core;

namespace RoBIM.Manager;

public sealed partial class ManagerForm
{
    private readonly Panel cadRibbonPage = new() { Dock = DockStyle.Fill };
    private readonly Panel navisworksRibbonPage = new() { Dock = DockStyle.Fill };
    private readonly Label cadRibbonSummary = new() { Dock = DockStyle.Top, AutoSize = true };
    private RibbonEditorControl? cadRibbonEditor;
    private bool cadEditorLoaded;
    private RibbonEditorControl? navisworksRibbonEditor;

    private void BuildHostRibbonPages()
    {
        var store = new RibbonLayouts(paths);
        cadRibbonEditor = new RibbonEditorControl(store, preview, "AutoCAD");
        navisworksRibbonEditor = new RibbonEditorControl(store, preview, "Navisworks");
        cadRibbonPage.Controls.Add(cadRibbonEditor);
        cadRibbonPage.Controls.Add(cadRibbonSummary);
        navisworksRibbonPage.Controls.Add(navisworksRibbonEditor);
        navisworksRibbonPage.Controls.Add(new Label { Dock = DockStyle.Top, AutoSize = true, Text = "등록된 Navisworks 도구가 없습니다." });
        navisworksRibbonEditor.LoadTools([]);
        navisworksRibbonEditor.ApplyRequested = _ => Task.FromResult("배치를 저장했습니다. Navisworks 호스트 연결은 아직 제공되지 않습니다.");
        cadRibbonEditor.RefreshRequested = () => { cadEditorLoaded = false; RefreshCadRibbonPage(); return Task.CompletedTask; };
        cadRibbonEditor.ApplyRequested = layout =>
        {
            var owner = productRows.FirstOrDefault(x => x.Status.Tool.Id == Catalog.Cad.Id);
            if (owner is null || !CadConfigurable(owner)) throw new IOException("관리 중인 AutoCAD 도구를 먼저 확인하세요.");
            var bridge = new CadReload(paths);
            var source = bridge.Source(owner.Status.Path, preferInstalled: true) ?? throw new IOException("CAD 패키지가 없습니다.");
            var selected = source.Previous?.SelectedActions ?? [];
            var sessions = CadReload.Sessions();
            if (sessions.Count > 1) throw new IOException("AutoCAD를 한 개만 실행하고 다시 시도하세요.");
            bridge.Prepare(owner.Status.Tool, source.Bundle, source.Catalog, selected, sessions.SingleOrDefault(), source.Previous);
            return Task.FromResult("배치를 저장하고 적용을 준비했습니다. AutoCAD에서 ROBIMRELOAD를 실행하세요.");
        };
    }

    private void RefreshCadRibbonPage()
    {
        try
        {
            if (cadEditorLoaded) return;
            var owner = productRows.FirstOrDefault(x => x.Status.Tool.Id == Catalog.Cad.Id);
            if (owner is null || !CadConfigurable(owner)) { cadRibbonEditor?.LoadTools([]); return; }
            var source = new CadReload(paths).Source(owner.Status.Path, preferInstalled: true);
            if (source is null) { cadRibbonEditor?.LoadTools([]); return; }
            var selected = source.Previous?.SelectedActions.ToHashSet(StringComparer.Ordinal) ?? [];
            var tools = source.Catalog.Modules.SelectMany(m => m.Actions.Select(a =>
                new RibbonTool("cad:" + CadFiles.Key(m, a), a.Title, "RoBIM", a.Panel,
                    selected.Contains(CadFiles.Key(m, a)), a.Icon is null ? null : CadFiles.Child(source.Root, a.Icon))));
            cadRibbonEditor?.LoadTools(tools);
            cadEditorLoaded = true;
        }
        catch (Exception error) { ReportPageFailure("CadRibbon", error); }
    }
}
