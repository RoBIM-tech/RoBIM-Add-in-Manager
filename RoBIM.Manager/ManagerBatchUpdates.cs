using RoBIM.Manager.Core;
namespace RoBIM.Manager;
public sealed partial class ManagerForm
{
    private readonly Button programUpdateAll = new() { Text="일괄 업데이트",AutoSize=true,Height=36 };
    private bool batchUpdating,batchNeedsNative,batchNeedsLayout;
    private readonly List<string> batchErrors=[];
    private string? BatchHost => currentPage=="Revit"?"revit":currentPage=="Cad"?"autocad":null;
    private ProgramUpdateTarget[] BatchPlan(string host) => ProgramUpdateBatch.Plan(host,CanUseTest,
        CommandInventory().Where(IndependentRelease).Select(c=>new ProgramUpdateItem(c.Id,c.Title,c.Kind==ButtonKind.Native?"revit":"autocad",c.SelectedVersion,c.SelectedChannel,c.Selected,
            !AppearanceFor(c).Legacy && AppearanceFor(c).Enabled && RepositoryPermitted(c) && !c.Owner.Status.Modified && !c.Owner.Status.External,
            updateErrors.ContainsKey(c.Owner.Status.Tool.Id)||testOfferErrors.ContainsKey(c.Owner.Status.Tool.Id)||stableOfferErrors.ContainsKey(c.Owner.Status.Tool.Id),ChannelOffer(c,"stable"),ChannelOffer(c,"test"))));
    private async Task UpdateProgramTools()
    {
        if(busy||batchUpdating||preview||BatchHost is not {} host)return;
        batchUpdating=true;batchNeedsNative=false;batchNeedsLayout=false;batchErrors.Clear();
        try
        {
            await RefreshWithUpdates();
            if(batchErrors.Count>0){MessageBox.Show(this,"업데이트 조회에 실패해 적용하지 않았습니다.\n"+string.Join("\n",batchErrors),"일괄 업데이트");return;}
            var plan=BatchPlan(host);
            if(plan.Length==0){MessageBox.Show(this,"업데이트 가능한 설치 도구가 없습니다. 조회 실패 항목은 새로고침 후 확인하십시오.","일괄 업데이트");return;}
            if(!Confirm("다음 "+plan.Length+"개 도구를 업데이트합니까?\n\n"+string.Join("\n",plan.Select(x=>x.Name+" · "+x.Version+" → "+x.Offer.Version+" ("+(x.Offer.Channel=="stable"?"정식":"TEST")+")"))+"\n\n선택 사항인 새 TEST로 자동 전환하지 않습니다. 각 도구의 배치는 유지합니다."))return;
            var result=await ProgramUpdateBatch.Execute(plan,async target=>
            {
                if(!Allowed)throw new IOException("로그인 또는 권한 확인이 필요합니다.");
                var card=CommandInventory().Single(c=>c.Id==target.Id);
                var errors=batchErrors.Count;
                await ToggleButton(card,true,target.Offer.Channel,false,target);
                if(batchErrors.Count!=errors)throw new IOException(batchErrors.Last());
                var after=CommandInventory().Single(c=>c.Id==target.Id);
                ProgramUpdateBatch.Verify(target,after.SelectedVersion,after.SelectedChannel);
            });
            batchUpdating=false;
            var apply=(batchNeedsNative||batchNeedsLayout) && host=="revit"?RequestAutomaticRevitApply(batchNeedsNative,false,batchNeedsLayout):host=="autocad" && result.Prepared.Length>0?"AutoCAD에서 Reload하면 준비된 변경을 적용합니다.":"";
            var message="업데이트 준비 완료 "+result.Prepared.Length+"개 · 실패 "+result.Failed.Length+"개\n"+apply;
            if(result.Failed.Length>0)message+="\n\n"+string.Join("\n",result.Failed.Select(x=>x.Name+": "+x.Error));
            WriteLog(message);MessageBox.Show(this,message,"일괄 업데이트 결과");
        }
        finally {batchUpdating=false;batchErrors.Clear();foreach(var control in navigation)control.Enabled=true;RefreshCardActions();}
    }
}
