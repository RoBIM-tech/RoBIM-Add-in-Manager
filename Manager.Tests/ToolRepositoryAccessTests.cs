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
 }
}
