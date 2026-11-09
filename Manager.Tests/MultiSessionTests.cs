using System.Text.Json;
using RoBIM.Manager.Core;
using RoBIM.Revit.Reload;
using RoBIM.Cad.Configuration;
internal static class MultiSessionTests
{
 public static void Run(string root,Action<string,Action> test)
 {
  void Require(bool value,string reason){if(!value)throw new Exception(reason);}
  var dir=Path.Combine(root,"multi-session");Directory.CreateDirectory(dir);
  var paths=new ManagerPaths(Path.Combine(dir,"data"),Path.Combine(dir,"cad"),Path.Combine(dir,"revit"));
  RevitSession[] live=[new(101,1001),new(102,1002)];
  var native=new NativeRevitCommands(paths,sessionsProbe:()=>live);
  var payload=Path.Combine(dir,"module");Directory.CreateDirectory(payload);File.WriteAllText(Path.Combine(payload,"Fixture.dll"),"fixture");
  NativeProtocol.Save(Path.Combine(payload,NativeProtocol.ManifestName),new NativeModule(1,"fixture","1.0.0-test.1","command-only",[new("run","Fixture","test","RoBIM","Tools","Fixture.dll","Fixture.Command")]));
  var source=native.Cache(new(payload,"1.0.0-test.1"));
  NativeRequest? desired=null;
  test("MultipleRevitPreparesAndRequestsAllExactSessions",()=>{native.Toggle(source.Path,"run",true);native.RequestReload(false);desired=native.Desired()!;Require(desired.ApplyRequested&&desired.TargetPid==0&&desired.Targets!.Length==2,"Multi request missing exact targets");Require(NativeProtocol.Matches(desired,101,1001)&&NativeProtocol.Matches(desired,102,1002)&&!NativeProtocol.Matches(desired,101,9999),"Restarted PID accepted");});
  void Receipt(int pid,long ticks,string status)=>NativeProtocol.Save(NativeProtocol.Child(native.Data,$"Results/{desired!.Id}-{pid}-{ticks}.json"),new NativeReceipt(desired.Id,status,"fixture",pid,ticks,DateTimeOffset.UtcNow));
  test("OneRevitSuccessDoesNotCompleteOtherSession",()=>{Receipt(101,1001,"applied");var state=native.State("fixture","run");Require(state.Pending&&state.Message.Contains("완료 1개 / 대기 1개"),"One success counted as all");});
  test("RevitFailurePreservesOtherSuccess",()=>{Receipt(102,1002,"failed");Require(native.State("fixture","run").Message.Contains("완료 1개 / 대기 0개 / 실패 1개"),"Partial failure lost");});
  test("AllRevitReceiptsRequiredForCompletion",()=>{Receipt(102,1002,"applied");Require(!native.State("fixture","run").Pending,"Complete multi status remains pending");});
  test("RevitPidReuseDoesNotReuseReceipt",()=>{live=[new(101,9999),new(102,1002)];Require(native.State("fixture","run").Pending,"Old PID receipt reused");live=[new(101,1001),new(102,1002)];});
  test("RevitWrongTargetListIsRejected",()=>{try{NativeProtocol.Validate(native.Data,desired! with{Targets=[new(101,1001),new(101,1002)]});}catch(InvalidDataException){return;}throw new Exception("Duplicate target accepted");});
  test("RevitRestartRebindsValidatedSelectionAndRejectsOldReceipt",()=>{
   var old=desired!;live=[new(101,9999)];native.RequestReload(false);var next=native.Desired()!;
   Require(next.Id!=old.Id&&next.TargetPid==101&&next.TargetStartTicks==9999,"Restart was not rebound to a fresh request");
   Require(next.Modules.SelectMany(m=>m.Selected).SequenceEqual(old.Modules.SelectMany(m=>m.Selected)),"Rebinding changed selected commands");
   Require(native.State("fixture","run").Pending,"Previous session receipt counted as applied");
   live=[new(101,1001),new(102,1002)];
  });
  var ribbon=new RibbonLayouts(paths);
  test("ExplicitRecoveryRequiresNewExactSessionReceipts",()=>{
   native.ReprepareForCurrentSessions();var recovered=native.Desired()!;
   Require(recovered.Id!=desired!.Id&&recovered.Targets!.Length==2,"Recovery reused old request or lost a session");
   Require(native.State("fixture","run").Pending,"Recovery claimed success before replies");
   NativeProtocol.Save(NativeProtocol.Child(native.Data,$"Results/{recovered.Id}-101-1001.json"),new NativeReceipt(recovered.Id,"applied","",101,1001,DateTimeOffset.UtcNow));
   Require(native.State("fixture","run").Pending,"One reply counted as all sessions");
   NativeProtocol.Save(NativeProtocol.Child(native.Data,$"Results/{recovered.Id}-102-1002.json"),new NativeReceipt(recovered.Id,"applied","",102,1002,DateTimeOffset.UtcNow));
   Require(!native.State("fixture","run").Pending,"Exact completed replies were not accepted");
  });
  void Ready(int pid,long ticks,int version)=>File.WriteAllText(Path.Combine(paths.Data,"UiBridgeSessions",pid+".json"),JsonSerializer.Serialize(new{Version=version,StartTicks=ticks}));
  Directory.CreateDirectory(Path.Combine(paths.Data,"UiBridgeSessions"));Ready(101,1001,4);Ready(102,1002,4);
  string batchId="";
  void RibbonReceipt(int pid,long ticks,string status,string? requestId=null){var file=Path.Combine(paths.Data,"revit-ui-results",$"{batchId}-{pid}-{ticks}.json");Directory.CreateDirectory(Path.GetDirectoryName(file)!);File.WriteAllText(file,JsonSerializer.Serialize(new{Id=requestId??batchId,Pid=pid,StartTicks=ticks,Status=status}));}
  test("RibbonRequestsAreRoutedPerSession",()=>{ribbon.RequestRevit(false,()=>live);using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(paths.Data,"revit-ui-request-batch.json")));batchId=json.RootElement.GetProperty("Id").GetString()!;Require(File.Exists(Path.Combine(paths.Data,"revit-ui-requests","101-1001.json"))&&File.Exists(Path.Combine(paths.Data,"revit-ui-requests","102-1002.json")),"Missing per-session requests");});
  test("RibbonOneSuccessLeavesOtherWaiting",()=>{RibbonReceipt(101,1001,"applied");Require(ribbon.RevitResult(sessionsProbe:()=>live).Contains("완료 1개 / 대기 1개"),"Shared ribbon success leaked");});
  test("RibbonIgnoresStaleRequestReceipts",()=>{RibbonReceipt(102,1002,"applied","old-request");Require(ribbon.RevitResult(sessionsProbe:()=>live).Contains("대기 1개"),"Old receipt reused");});
  test("RibbonSessionFailureAndRestartAreIndependent",()=>{RibbonReceipt(102,1002,"failed");Require(ribbon.RevitResult(sessionsProbe:()=>live).Contains("실패 1개"),"Failure lost");Require(ribbon.RevitResult(sessionsProbe:()=>[new(101,9999),new(102,1002)]).Contains("대기 1개"),"Restart accepted old reply");});
  test("CorruptRibbonPeerDoesNotHideHealthyReceipt",()=>{RibbonReceipt(101,1001,"applied");File.WriteAllText(Path.Combine(paths.Data,"revit-ui-results",$"{batchId}-102-1002.json"),"{broken");Require(ribbon.RevitResult(sessionsProbe:()=>live).Contains("완료 1개 / 대기 0개 / 실패 1개"),"Corrupt peer hid success");});
  test("OneOldHostDoesNotBlockNewHostRequest",()=>{Ready(102,1002,3);var message=ribbon.RequestRevit(false,()=>live);Require(message.Contains("요청 1개")&&message.Contains("필요 1개"),"Old host blocked valid peer");});
  var cadRoot=Path.Combine(dir,"cad-bundle");Directory.CreateDirectory(CadReload.WindowsDirectory(cadRoot));File.WriteAllText(Path.Combine(CadReload.WindowsDirectory(cadRoot),"RoBIM.Cad.Loader.dll"),"loader");
  CadSession[] cadLive=[new(201,2001,"CAD A"),new(202,2002,"CAD B")];var cad=new CadReload(paths,()=>cadLive);Directory.CreateDirectory(cad.Data);
  var request=new CadRequest{Id=Guid.NewGuid().ToString("N"),BundlePath=cadRoot,LoaderHash=CadFiles.Hash(Path.Combine(CadReload.WindowsDirectory(cadRoot),"RoBIM.Cad.Loader.dll"))};CadFiles.Save(Path.Combine(cad.Data,"pending.json"),request);
  void CadReceipt(int pid,long ticks,string status)=>CadFiles.Save(CadFiles.Child(cad.Data,$"Results/{request.Id}-{pid}-{ticks}.json"),new CadReceipt(request.Id,status,"fixture",pid,ticks,DateTimeOffset.UtcNow));
  test("CadOneSuccessDoesNotCompleteOtherProcess",()=>{CadReceipt(201,2001,"applied");Require(cad.Status(cadRoot).Contains("완료 1개 / 대기 1개"),"CAD shared success leaked");});
  test("CadSeparateFailureAndRetryPreservePeerReceipt",()=>{CadReceipt(202,2002,"failed");Require(cad.Status(cadRoot).Contains("실패 1개"),"CAD failure not displayed");CadReceipt(202,2002,"applied");Require(cad.Status(cadRoot).Contains("완료 2개 / 대기 0개 / 실패 0개"),"Retry removed peer receipt");});
  test("CorruptCadPeerDoesNotHideHealthyReceipt",()=>{File.WriteAllText(CadFiles.Child(cad.Data,$"Results/{request.Id}-202-2002.json"),"{broken");Require(cad.Status(cadRoot).Contains("완료 1개 / 대기 0개 / 실패 1개"),"Corrupt CAD peer hid success");CadReceipt(202,2002,"applied");});
  test("CadStalePidAndClosedProcessDoNotClaimApplied",()=>{cadLive=[new(201,9999,"restart"),new(202,2002,"peer")];Require(cad.Status(cadRoot).Contains("대기 1개"),"CAD PID reuse accepted");cadLive=[];Require(cad.Status(cadRoot).Contains("다음 AutoCAD"),"Closed process presented as applied");});
  test("CadLegacyReceiptOnlyAppliesToMatchingSession",()=>{cadLive=[new(203,2003,"legacy"),new(204,2004,"other")];CadFiles.Save(CadFiles.Child(cad.Data,$"Results/{request.Id}.json"),new CadReceipt(request.Id,"applied","legacy",203,2003,DateTimeOffset.UtcNow));Require(cad.Status(cadRoot).Contains("완료 1개 / 대기 1개"),"Legacy result counted for all CADs");});
 }
}
