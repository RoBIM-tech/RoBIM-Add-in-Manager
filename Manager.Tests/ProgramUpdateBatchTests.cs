using RoBIM.Manager.Core;
internal static class ProgramUpdateBatchTests
{
    public static void Run(Action<string,Action> test)
    {
        void Require(bool ok,string why){if(!ok)throw new Exception(why);}
        void Reject(Action action){try{action();}catch(IOException){return;}throw new Exception("Unsafe batch accepted");}
        var stable=new ReleaseOffer("1.1.0","stable");var prerelease=new ReleaseOffer("1.2.0-test.1","test");
        var a=new ProgramUpdateItem("a","A","revit","1.0.0","stable",true,true,false,stable,prerelease);
        test("BatchScopesInstalledUpdatesToOneProgramAndDeduplicates",()=>{
            var plan=ProgramUpdateBatch.Plan("revit",true,[a,a,a with{Id="cad",Host="autocad"},a with{Id="missing",Installed=false},a with{Id="blocked",Enabled=false},a with{Id="error",LookupFailed=true}]);
            Require(plan.Length==1&&plan[0].Id=="a"&&plan[0].Offer.Channel=="stable","Unrelated or unsafe tool included");
            Require(ProgramUpdateBatch.Plan("navisworks",true,[a]).Length==0,"Unsupported host accepted");
        });
        test("BatchNeverOptsInstalledStableIntoOptionalTest",()=>{
            Require(ProgramUpdateBatch.Plan("revit",true,[a with{Version="1.1.0"}]).Length==0,"Optional TEST became bulk update");
            Require(ProgramUpdateBatch.Plan("revit",true,[a with{Channel="unknown"}]).Length==0,"Unknown release accepted");
        });
        test("BatchHonorsTestPermissionAndStablePromotion",()=>{
            var t=a with{Channel="test",Version="1.1.0-test.1",Stable=null};
            Require(ProgramUpdateBatch.Plan("revit",false,[t]).Length==0,"Ordinary user received TEST");
            Require(ProgramUpdateBatch.Plan("revit",true,[t]).Single().Offer.Channel=="test","TEST user lost TEST update");
            Require(ProgramUpdateBatch.Plan("revit",false,[t with{Stable=stable}]).Single().Offer.Channel=="stable","Stable promotion not selected");
        });
        test("BatchRejectsChangedTargetOrInstallationBeforeMutation",()=>{
            var target=ProgramUpdateBatch.Plan("revit",true,[a]).Single();
            ProgramUpdateBatch.Verify(target,"v1.1.0","stable");ProgramUpdateBatch.EnsureCurrent(target,"1.0.0","stable",true);
            Reject(()=>ProgramUpdateBatch.Verify(target,"1.1.1","stable"));Reject(()=>ProgramUpdateBatch.Verify(target,"1.1.0","test"));
            Reject(()=>ProgramUpdateBatch.EnsureCurrent(target,"1.0.1","stable",true));Reject(()=>ProgramUpdateBatch.EnsureCurrent(target,"1.0.0","test",true));Reject(()=>ProgramUpdateBatch.EnsureCurrent(target,"1.0.0","stable",false));
        });
        test("BatchContinuesIndependentToolsAndReportsPartialFailure",()=>{
            var plan=ProgramUpdateBatch.Plan("revit",true,[a,a with{Id="b"},a with{Id="c"}]);var seen=new List<string>();
            var result=ProgramUpdateBatch.Execute(plan,item=>{seen.Add(item.Id);if(item.Id=="b")throw new IOException("fixture failure");return Task.CompletedTask;}).GetAwaiter().GetResult();
            Require(seen.SequenceEqual(new[]{"a","b","c"})&&result.Prepared.Length==2&&result.Failed.Single().Id=="b","Partial update result lost");
        });
    }
}
