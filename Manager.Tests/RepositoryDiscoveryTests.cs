using System.Net;
using System.Text.Json;
using RoBIM.Manager.Core;

internal static class RepositoryDiscoveryTests
{
    internal static void Run(string root, Action<string, Action> test)
    {
        void Require(bool value, string message) { if (!value) throw new Exception(message); }
        var paths = new ManagerPaths(Path.Combine(root, "discovery/data"), Path.Combine(root, "discovery/cad"), Path.Combine(root, "discovery/revit"));
        ExtensionRepository Entry(string repo, string id) => new(repo, "main", [new(id, id, "revit", id+".bundle", id+".bundle", "release", id+".zip", Channel:"test", ModuleId:id, CommandId:"run")], "test");
        var lookups = new List<string>(); int inspections = 0;
        Task<HttpResponseMessage> Request(string url)
        {
            lookups.Add(url);
            object value = url.StartsWith("/user/repos") ? new[] { new { full_name="alice/Private", default_branch="main", pushed_at="1", topics=new[]{"robim-addon"}, disabled=false }, new { full_name="stranger/Outside", default_branch="main", pushed_at="1", topics=new[]{"robim-addon"}, disabled=false } }
                : url.StartsWith("/users/alice/repos") ? new[] { new { full_name="alice/One", default_branch="main", pushed_at="1", topics=new[]{"robim-addon"}, disabled=false } }
                : url.StartsWith("/orgs/team/repos") ? new[] { new { full_name="team/Two", default_branch="main", pushed_at="1", topics=new[]{"robim-addon"}, disabled=false } }
                : url == "/users/team" ? new { type="Organization" } : url == "/users/alice" ? new { type="User" } : (object)Array.Empty<object>();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(value))});
        }
        Task<ExtensionRepository> Inspect(string repo) { inspections++; return Task.FromResult(Entry(repo, repo.EndsWith("/Private")?"discovery-private":repo.StartsWith("alice")?"discovery-one":"discovery-two")); }
        var discovery = new RepositoryDiscovery(paths, Request, Inspect);
        test("PublisherSettingsMultipleOwnersRoundTripAndNoInstall", () =>
        {
            discovery.SaveSettings(new(1,false,[new("alice","user"),new("team","organization"),new("paused","auto",false)]));
            Require(discovery.Settings().Publishers.Length==3,"Multiple owners not persisted");
            Require(!Directory.Exists(Path.Combine(paths.Data,"NativeRevit")),"Saving owners changed install selection");
            Require(RepositoryDiscovery.PublisherName("https://github.com/alice/")=="alice","Publisher URL not normalized");
            foreach(var invalid in new[]{"../alice","https://evil.test/a","alice/repo","-alice"}) {
                bool rejected=false;try{RepositoryDiscovery.PublisherName(invalid);}catch(ArgumentException){rejected=true;}Require(rejected,"Invalid owner accepted");
            }
            bool duplicate=false;try{discovery.SaveSettings(new(1,false,[new("alice"),new("ALICE")]));}catch(IOException){duplicate=true;}Require(duplicate,"Case duplicate accepted");
        });
        test("DiscoveryFindsPersonalAndOrganizationWithoutBundledRegistration", () =>
        {
            var result=discovery.Refresh().GetAwaiter().GetResult();
            Require(result.Warnings.Length==0,string.Join(",",result.Warnings));
            Require(result.Repositories.Any(x=>x.Repository=="alice/One")&&result.Repositories.Any(x=>x.Repository=="team/Two"),"New repositories missing");
            Require(result.Repositories.Any(x=>x.Repository=="alice/Private")&&!result.Repositories.Any(x=>x.Repository.StartsWith("stranger/")),"Private owner discovery or selected-owner scope failed");
            Require(!lookups.Any(x=>x.Contains("paused")),"Disabled publisher was queried");
            var known=new ExtensionCatalog(paths).Registered();Require(known.Count(x=>x.Repository=="alice/One")==1,"Discovery cache not usable after restart");
            Require(SingleNativeButton.Registration(known.Single(x=>x.Repository=="alice/One"),known.Single(x=>x.Repository=="alice/One").Packages[0])?.ModuleId=="discovery-one","Self-declared command cannot create a first card");
            Require(!Directory.Exists(Path.Combine(paths.Data,"NativeRevit")),"Metadata refresh installed a module");
        });
        test("DiscoveryUnchangedStampSkipsManifestAndDeduplicates", () =>
        {
            int before=inspections;discovery.Refresh().GetAwaiter().GetResult();Require(inspections==before,"Unchanged manifests fetched again");
            Require(new ExtensionCatalog(paths).Registered().Count(x=>x.Repository=="alice/One")==1,"Duplicate card source");
        });
        test("DiscoveryPermissionFailurePreservesKnownAndSelection", () =>
        {
            var result=new RepositoryDiscovery(paths,_=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)),Inspect).Refresh().GetAwaiter().GetResult();
            Require(result.Warnings.Length==3,"Per-owner failure missing");Require(result.Repositories.Any(x=>x.Repository=="team/Two"),"Access failure removed known tools");
            Require(!Directory.Exists(Path.Combine(paths.Data,"NativeRevit")),"Failure changed installed state");
        });
        test("DiscoveryRejectsCrossRepositoryModuleAndFolderCollisions", () =>
        {
            Require(RepositoryDiscovery.Conflicts(Entry("evil/Other","discovery-one"),new ExtensionCatalog(paths).Registered()),"ID/folder collision accepted");
            Require(!RepositoryDiscovery.Conflicts(Entry("alice/One","discovery-one"),new ExtensionCatalog(paths).Registered()),"Own identity update rejected");
        });
        test("DiscoverySkipsInfrastructureWithNoPackages", () =>
        {
            var own=new RepositoryDiscovery(paths, Request, _=>throw new NoInstallablePackagesException("no packages"));
            var file=Path.Combine(paths.Data,"discovered-repositories.json");File.Delete(file);
            var result=own.Refresh().GetAwaiter().GetResult();
            Require(result.Warnings.Length==0 && !result.Repositories.Any(x=>x.Repository=="alice/One"),"Empty infrastructure reported as failed button");
            discovery.Refresh().GetAwaiter().GetResult();
        });
        test("DiscoveryOwnerRemovalKeepsPreviouslyKnownTools", () =>
        {
            discovery.SaveSettings(new(1,false,[]));
            lookups.Clear();var result=discovery.Refresh().GetAwaiter().GetResult();
            Require(lookups.Count==0&&result.Repositories.Any(x=>x.Repository=="alice/One"),"Owner removal queried network or removed known tool");
        });
    }
}
