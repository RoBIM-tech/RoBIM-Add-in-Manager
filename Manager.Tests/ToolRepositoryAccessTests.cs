using System.Net;
using System.Text.Json;
using RoBIM.Manager.Core;
internal static class ToolRepositoryAccessTests
{
 public static void Run(Action<string,Action> test, Func<string,ManagerPaths> paths)
 {
  static ExtensionRepository Repo(string name,string module,string host="revit") => new(name,"main",[new("pkg-"+module,module,host,"payload","payload","release","tool.zip","","stable",module,"run")],"");
  static HttpResponseMessage Reply(HttpStatusCode code,object body)=>new(code){Content=new StringContent(JsonSerializer.Serialize(body))};
  static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
  test("ToolRepositoryRevocationAndRestoration",()=>{
   bool permitted=true;var entries=new[]{Repo("RoBIM-tech/A","alpha"),Repo("RoBIM-tech/B","beta","autocad")};
   var cache=new ToolRepositoryAccess(paths("tool-access"),route=>Task.FromResult(route.EndsWith("/contents") ? Reply(HttpStatusCode.OK,Array.Empty<object>()) : route.EndsWith("/A") && !permitted ? Reply(HttpStatusCode.NotFound,new{}) : Reply(HttpStatusCode.OK,new{full_name=route.EndsWith("/A")?"RoBIM-tech/A":"RoBIM-tech/B",@private=true,permissions=new{pull=true}})),()=>entries);
   cache.Refresh().GetAwaiter().GetResult();Require(cache.Allows("revit:alpha")&&cache.Allows("autocad:beta"),"Granted tools denied");
   permitted=false;cache.Refresh().GetAwaiter().GetResult();Require(!cache.Allows("revit:alpha")&&cache.Allows("autocad:beta"),"Revocation affected wrong tool");
   permitted=true;cache.Refresh().GetAwaiter().GetResult();Require(cache.Allows("revit:alpha"),"Restored access denied");cache.Clear();Require(!cache.Allows("revit:alpha"),"Logout did not clear");
  });
  test("ToolRepositoryMetadataOnlyTokenCannotUse",()=>{
   var cache=new ToolRepositoryAccess(paths("tool-contents"),route=>Task.FromResult(route.EndsWith("/contents")?Reply(HttpStatusCode.Forbidden,new{}):Reply(HttpStatusCode.OK,new{full_name="RoBIM-tech/A",@private=true,permissions=new{pull=true}})),()=>new[]{Repo("RoBIM-tech/A","alpha")});
   cache.Refresh().GetAwaiter().GetResult();Require(!cache.Allows("revit:alpha"),"Metadata-only token granted execution");
  });
  test("ToolRepositoryUnknownAmbiguousOrMissingPullDenies",()=>{
   var entries=new[]{Repo("RoBIM-tech/A","same"),Repo("RoBIM-tech/B","same"),Repo("RoBIM-tech/C","other")};
   var cache=new ToolRepositoryAccess(paths("tool-ambiguous"),route=>Task.FromResult(Reply(HttpStatusCode.OK,new{full_name="RoBIM-tech/C",@private=true,permissions=new{pull=false}})),()=>entries);
   cache.Refresh().GetAwaiter().GetResult();Require(!cache.Allows("revit:same")&&!cache.Allows("revit:other")&&!cache.Allows("revit:unknown"),"Unexpected tool access");
  });
  test("ToolRepositoryParallelProgressAndLogout",()=>{
   var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
   var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
   var cache=new ToolRepositoryAccess(paths("tool-parallel"),async route=>{
    if(route.EndsWith("/A")){started.TrySetResult();await release.Task;}
    if(route.EndsWith("/contents"))return Reply(HttpStatusCode.OK,Array.Empty<object>());
    return Reply(HttpStatusCode.OK,new{full_name=route.EndsWith("/A")?"RoBIM-tech/A":"RoBIM-tech/B",@private=true,permissions=new{pull=true}});
   },()=>new[]{Repo("RoBIM-tech/A","alpha"),Repo("RoBIM-tech/B","beta")});
   var pending=cache.Refresh();started.Task.Wait(TimeSpan.FromSeconds(3));
   Require(SpinWait.SpinUntil(()=>cache.Allows("revit:beta"),TimeSpan.FromSeconds(3)),"Fast repository waited for slow repository");
   Require(!cache.Allows("revit:alpha") && cache.Status("revit:alpha")==ToolAccessStatus.Checking,"Unverified repository was enabled");
   cache.Clear();release.TrySetResult();pending.GetAwaiter().GetResult();
   Require(!cache.Allows("revit:alpha")&&!cache.Allows("revit:beta"),"Old request restored permissions after logout");
  });
  test("ToolRepositoryRefreshPreservesThenRevokes",()=>{
   var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); bool slow=false;
   var cache=new ToolRepositoryAccess(paths("tool-refresh"),async route=>{
    if(slow){await release.Task;return Reply(HttpStatusCode.NotFound,new{});}
    return route.EndsWith("/contents")?Reply(HttpStatusCode.OK,Array.Empty<object>()):Reply(HttpStatusCode.OK,new{full_name="RoBIM-tech/A",@private=true,permissions=new{pull=true}});
   },()=>new[]{Repo("RoBIM-tech/A","alpha")});
   cache.Refresh().GetAwaiter().GetResult();slow=true;var pending=cache.Refresh();
   Require(cache.Allows("revit:alpha"),"Valid result disappeared during refresh");
   release.SetResult();pending.GetAwaiter().GetResult();Require(!cache.Allows("revit:alpha")&&cache.Status("revit:alpha")==ToolAccessStatus.Denied,"Revocation not applied");
  });
 }
}
