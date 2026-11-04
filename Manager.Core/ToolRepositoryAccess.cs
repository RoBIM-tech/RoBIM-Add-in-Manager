using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
namespace RoBIM.Manager.Core;
// A missing, ambiguous, expired, or unreachable repository always denies that tool.
public sealed class ToolRepositoryAccess
{
    private readonly Func<string,Task<HttpResponseMessage>> request;
    private readonly Func<IEnumerable<ExtensionRepository>> registrations;
    public ToolRepositoryAccess(ManagerPaths paths, Func<string,Task<HttpResponseMessage>> request, Func<IEnumerable<ExtensionRepository>>? registrations = null)
    {
        this.request=request; this.registrations=registrations ?? (() => new ExtensionCatalog(paths).Registered());
    }
    private sealed record Snapshot(HashSet<string> Keys, long Checked);
    private Snapshot snapshot = new(new(StringComparer.OrdinalIgnoreCase), 0);
    public bool Allows(string key) { var state=Volatile.Read(ref snapshot); return Stopwatch.GetElapsedTime(state.Checked)<TimeSpan.FromMinutes(3) && state.Keys.Contains(key); }
    public void Clear() => Volatile.Write(ref snapshot,new(new(StringComparer.OrdinalIgnoreCase),0));
    public async Task Refresh()
    {
        Clear();
        var candidates=new Dictionary<string,HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach(var repository in registrations())
            foreach(var package in repository.Packages)
            {
                string? module = package.Host == "revit" ? SingleNativeButton.Registration(repository,package)?.ModuleId :
                    package.Host == "autocad" && CadFeaturePackages.IsFeature(package) ? package.ModuleId : null;
                if(string.IsNullOrEmpty(module)) continue;
                var key=package.Host+":"+module;
                if(!candidates.TryGetValue(key,out var owners)) candidates[key]=owners=new(StringComparer.OrdinalIgnoreCase);
                owners.Add(repository.Repository);
            }
        var allowed=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var owner in candidates.Where(x=>x.Value.Count==1).Select(x=>x.Value.Single()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try {
                using var response=await request("/repos/"+owner);
                if(!response.IsSuccessStatusCode)continue;
                using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var root=json.RootElement;
                if(!root.TryGetProperty("full_name",out var name) || !string.Equals(name.GetString(),owner,StringComparison.OrdinalIgnoreCase))continue;
                var isPublic=root.TryGetProperty("private",out var privateValue) && privateValue.ValueKind==JsonValueKind.False;
                var readable=root.TryGetProperty("permissions",out var permission) && permission.ValueKind==JsonValueKind.Object && permission.TryGetProperty("pull",out var pull) && pull.ValueKind==JsonValueKind.True;
                if (!isPublic && !readable) continue;
                // Metadata can be visible while an OAuth/fine-grained token cannot read repository contents.
                using var contents = await request("/repos/" + owner + "/contents");
                if (!contents.IsSuccessStatusCode) continue;
                using var listing = JsonDocument.Parse(await contents.Content.ReadAsStringAsync());
                if (listing.RootElement.ValueKind != JsonValueKind.Array) continue;
                foreach(var entry in candidates.Where(x=>x.Value.Count==1 && x.Value.Contains(owner))) allowed.Add(entry.Key);
            } catch(Exception error) when(error is IOException or HttpRequestException or JsonException or OperationCanceledException) { }
        }
        Volatile.Write(ref snapshot,new(allowed,Stopwatch.GetTimestamp()));
    }
}
