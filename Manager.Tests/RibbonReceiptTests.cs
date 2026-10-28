using System.Text.Json;
using RoBIM.Manager.Core;

internal static class RibbonReceiptTests
{
    public static void Run(string root,Action<string,Action> test)
    {
        var data=Path.Combine(root,"ribbon-receipt");Directory.CreateDirectory(data);
        var store=new RibbonLayouts(new(data,Path.Combine(root,"cad-receipt"),Path.Combine(root,"revit-receipt")));
        var request=Path.Combine(data,"revit-ui-request.json");var result=Path.Combine(data,"revit-ui-result.json");
        var session=new RevitSession(111,222);
        void Require(bool condition){if(!condition)throw new Exception("Ribbon receipt correlation failed");}
        void Request(int pid=111,long ticks=222)=>File.WriteAllText(request,JsonSerializer.Serialize(new{Id="current",Pid=pid,StartTicks=ticks}));
        void Result(string id,string status)=>File.WriteAllText(result,JsonSerializer.Serialize(new{Id=id,Status=status,Message="fixture"}));
        test("RibbonResultDoesNotClaimAppliedWithoutRunningSession",()=>Require(store.RevitResult(()=>null).Contains("실행한 뒤")));
        test("RibbonResultRejectsEarlierRevitSession",()=>{Request(110);Result("current","applied");Require(store.RevitResult(()=>session).Contains("이전 Revit 세션"));});
        test("RibbonResultDoesNotReuseOldAppliedReceipt",()=>{Request();Result("old","applied");Require(store.RevitResult(()=>session).Contains("적용 대기"));});
        test("RibbonResultShowsMatchingFailure",()=>{Request();Result("current","failed");Require(store.RevitResult(()=>session).Contains("적용 실패"));});
        test("RibbonResultShowsOnlyMatchingCurrentAppliedReceipt",()=>{Request();Result("current","applied");Require(store.RevitResult(()=>session).Contains("적용 완료"));});
    }
}
