using System.Drawing;
using RoBIM.Manager.Core;

namespace RoBIM.Manager;

public sealed partial class ManagerForm
{
    private readonly Panel cadRibbonPage = new() { Dock = DockStyle.Fill };
    private readonly Panel navisworksRibbonPage = new() { Dock = DockStyle.Fill };
    private readonly Label cadRibbonSummary = new() { AutoSize = true, MaximumSize = new Size(850,0) };
    private readonly Button cadRibbonEdit = MakeButton("버튼 이름 · 패널 · 순서 편집", true);

    private void BuildHostRibbonPages()
    {
        var cad = new FlowLayoutPanel { Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true };
        cad.Controls.Add(Note("AutoCAD에 설치한 버튼의 이름·패널·순서를 편집합니다. Revit 리본 설정과 별도로 보관합니다."));
        cad.Controls.Add(cadRibbonSummary); cad.Controls.Add(cadRibbonEdit);
        cad.Controls.Add(Note("설정은 적용 준비 후 AutoCAD의 RoBIM → 관리 → Reload에서 반영합니다."));
        cadRibbonEdit.Click += async (_, _) =>
        {
            if (preview) return;
            var owner=productRows.FirstOrDefault(x=>x.Status.Tool.Id==Catalog.Cad.Id);
            if (owner is null || !CadConfigurable(owner)) { MessageBox.Show(this,"AutoCAD 도구를 먼저 설치하거나 설치 상태를 확인하세요.","AutoCAD 리본 편집"); return; }
            selectedProductId=owner.Status.Tool.Id;
            await ConfigureCadButtons(false);
            RefreshCadRibbonPage();
        };
        cadRibbonPage.Controls.Add(cad);
        var navisworks=new FlowLayoutPanel { Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false };
        navisworks.Controls.Add(Note("등록된 Navisworks 도구가 없습니다. 편집할 리본 버튼이 없습니다."));
        navisworks.Controls.Add(Note("Revit·AutoCAD의 리본 설정은 각 프로그램의 리본 편집에서 관리하세요."));
        navisworksRibbonPage.Controls.Add(navisworks);
    }
    private void RefreshCadRibbonPage()
    {
        var owner=productRows.FirstOrDefault(x=>x.Status.Tool.Id==Catalog.Cad.Id);
        cadRibbonEdit.Enabled=!busy && (preview || owner is not null && CadConfigurable(owner));
        var count=CommandInventory().Count(x=>x.Kind==ButtonKind.Cad && x.Selected);
        cadRibbonSummary.Text=count+"개 버튼 설치됨" + (preview || owner is not null && CadConfigurable(owner) ? " · AutoCAD 전용 배치" : " · 도구 설치 후 편집할 수 있습니다.");
    }
}
