using System.Net;
using System.Text;
using System.Text.Json;
using RoBIM.Manager.Core;
internal static class TeamAccessTests
{
 public static void Run(Action<string,Action> test)
 {
  static HttpResponseMessage Response(object obj, HttpStatusCode code=HttpStatusCode.OK) => new(code){Content=new StringContent(JsonSerializer.Serialize(obj),Encoding.UTF8,"application/json")};
  static object[] List(params (string org,string slug)[] teams) => teams.Select(t=>(object)new {slug=t.slug,organization=new {login=t.org}}).ToArray();
  static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
  foreach(var (team,testAllowed) in new[]{("robim",false),("robim-guest",false),("new-project",false),("robim-test",true),("robim-dev",true),("robim-admin",true)})
   test("DynamicRole_"+team,()=> {using var access=new TeamAccess(false,()=>Task.FromResult(new GitHubIdentity("alice","Device")),route=>Task.FromResult(route.StartsWith("/user/teams?")?Response(List(("RoBIM-tech",team))):Response(new {state="active"})));var r=access.Refresh().GetAwaiter().GetResult();Require(r.Allowed && r.TestAllowed==testAllowed && !access.CanUse("unknown"),"Wrong dynamic role");});
  test("ForeignOrganizationDenied",()=>{using var a=new TeamAccess(false,()=>Task.FromResult(new GitHubIdentity("alice","Device")),_=>Task.FromResult(Response(List(("other","robim-admin")))));Require(!a.Refresh().GetAwaiter().GetResult().Allowed,"Foreign organization granted access");});
  test("PendingInvitationDenied",()=>{using var a=new TeamAccess(false,()=>Task.FromResult(new GitHubIdentity("alice","Device")),route=>Task.FromResult(route.StartsWith("/user/teams?")?Response(List(("RoBIM-tech","robim-guest"))):Response(new {state="pending"})));Require(!a.Refresh().GetAwaiter().GetResult().Allowed,"Pending invitation granted access");});
  test("PaginationIncludesLaterTeam",()=>{var requests=new List<string>();using var a=new TeamAccess(false,()=>Task.FromResult(new GitHubIdentity("alice","Device")),route=>{requests.Add(route);return Task.FromResult(route.Contains("&page=1")?Response(Enumerable.Range(0,100).Select(i=>(object)new {slug="team"+i,organization=new {login="other"}}).ToArray()):route.Contains("&page=2")?Response(List(("RoBIM-tech","robim-guest"))):Response(new {state="active"}));});Require(a.Refresh().GetAwaiter().GetResult().Allowed && requests.Any(x=>x.Contains("page=2")),"Later page ignored");});
  test("NetworkFailureRevokesPriorTeamAccess",()=>{var fail=false;using var a=new TeamAccess(false,()=>Task.FromResult(new GitHubIdentity("alice","Device")),route=>fail?throw new IOException("offline"):Task.FromResult(route.StartsWith("/user/teams?")?Response(List(("RoBIM-tech","robim"))):Response(new {state="active"})));a.Refresh().GetAwaiter().GetResult();Require(a.Allowed,"Initial access failed");fail=true;a.Refresh().GetAwaiter().GetResult();Require(!a.Allowed && a.Result.State==AccountAccessState.RetryPending,"Offline access allowed");});
  test("LogoutCannotBeUndoneByInflightMembershipResponse",()=>{var pending=new TaskCompletionSource<bool>();using var a=new TeamAccess(false,()=>Task.FromResult(new GitHubIdentity("alice","Device")),async route=>{await pending.Task;return route.StartsWith("/user/teams?")?Response(List(("RoBIM-tech","robim"))):Response(new {state="active"});});var refresh=a.Refresh();a.Lock();pending.SetResult(true);refresh.GetAwaiter().GetResult();Require(!a.Allowed,"Inflight login bypassed logout");});
  test("MixedTeamsPermitHighestExplicitRole",()=>{using var a=new TeamAccess(false,()=>Task.FromResult(new GitHubIdentity("alice","Device")),route=>Task.FromResult(route.StartsWith("/user/teams?")?Response(List(("RoBIM-tech","robim-guest"),("RoBIM-tech","robim-test"))):Response(new {state="active"})));var r=a.Refresh().GetAwaiter().GetResult();Require(r.Allowed && r.TestAllowed && r.Role=="TEST 사용자","Combined roles incorrect");});
  test("FailedMembershipDoesNotGrantAccess",()=>{using var a=new TeamAccess(false,()=>Task.FromResult(new GitHubIdentity("alice","Device")),route=>Task.FromResult(route.StartsWith("/user/teams?")?Response(List(("RoBIM-tech","robim-guest"))):Response(new {},HttpStatusCode.Forbidden)));var r=a.Refresh().GetAwaiter().GetResult();Require(!r.Allowed && r.State==AccountAccessState.RetryPending,"Failure treated as membership");});
 }
}
