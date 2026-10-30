namespace RoBIM.Manager.Core;

public sealed record ProgramUpdateItem(string Id,string Name,string Host,string Version,string Channel,bool Installed,bool Enabled,bool LookupFailed,ReleaseOffer? Stable,ReleaseOffer? Test);
public sealed record ProgramUpdateTarget(string Id,string Name,string Version,string Channel,ReleaseOffer Offer);
public sealed record ProgramUpdateFailure(string Id,string Name,string Error);
public sealed record ProgramUpdateResult(ProgramUpdateTarget[] Prepared,ProgramUpdateFailure[] Failed);
public static class ProgramUpdateBatch
{
    public static ProgramUpdateTarget[] Plan(string host,bool testAllowed,IEnumerable<ProgramUpdateItem> items) => items
        .Where(x=>host is "revit" or "autocad" && x.Host==host && x.Installed && x.Enabled && !x.LookupFailed)
        .GroupBy(x=>x.Id).Select(g=>g.First())
        .Select(x=>(Item:x,Offer:NativeReleasePolicy.Evaluate(x.Version,x.Channel,x.Stable,x.Test).Update))
        .Where(x=>x.Offer is not null && (x.Offer.Channel=="stable" || testAllowed && x.Offer.Channel=="test"))
        .Select(x=>new ProgramUpdateTarget(x.Item.Id,x.Item.Name,x.Item.Version,x.Item.Channel,x.Offer!)).ToArray();
    public static void EnsureCurrent(ProgramUpdateTarget expected,string version,string channel,bool installed)
    {
        if(!installed || version.TrimStart('v','V')!=expected.Version.TrimStart('v','V') || channel!=expected.Channel)
            throw new IOException("일괄 업데이트 확인 후 설치 상태가 변경되어 적용하지 않습니다.");
    }
    public static void Verify(ProgramUpdateTarget expected,string version,string channel)
    {
        if(version.TrimStart('v','V')!=expected.Offer.Version.TrimStart('v','V') || channel!=expected.Offer.Channel)
            throw new IOException("확인한 업데이트와 다운로드한 버전이 달라 적용하지 않습니다. 다시 확인하십시오.");
    }
    public static async Task<ProgramUpdateResult> Execute(IEnumerable<ProgramUpdateTarget> plan,Func<ProgramUpdateTarget,Task> apply)
    {
        var prepared=new List<ProgramUpdateTarget>();var failed=new List<ProgramUpdateFailure>();
        foreach(var item in plan) { try { await apply(item);prepared.Add(item); } catch(Exception error) { failed.Add(new(item.Id,item.Name,error.Message)); } }
        return new(prepared.ToArray(),failed.ToArray());
    }
}
