using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;
namespace RoBIM.Manager.Core;

public enum ToolAccessStatus { Checking, Allowed, Denied, Failed }

// Results are bound to a module and its unambiguous repository; stale or unknown results deny access.
public sealed class ToolRepositoryAccess
{
    private readonly Func<string,Task<HttpResponseMessage>> request;
    private readonly Func<IEnumerable<ExtensionRepository>> registrations;
    private readonly object sync = new();
    private readonly Dictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);
    private int generation;
    private sealed record Entry(string Owner, ToolAccessStatus Status, long Checked);
    public event Action? Changed;
    public ToolRepositoryAccess(ManagerPaths paths, Func<string,Task<HttpResponseMessage>> request, Func<IEnumerable<ExtensionRepository>>? registrations = null)
    { this.request=request; this.registrations=registrations ?? (() => new ExtensionCatalog(paths).Registered()); }
    private static bool Valid(Entry entry) => entry.Status == ToolAccessStatus.Allowed && Stopwatch.GetElapsedTime(entry.Checked) < TimeSpan.FromMinutes(3);
    public bool Allows(string key) { lock(sync) return entries.TryGetValue(key,out var entry) && Valid(entry); }
    public ToolAccessStatus Status(string key) { lock(sync) return entries.TryGetValue(key,out var entry) ? Valid(entry) ? ToolAccessStatus.Allowed : entry.Status == ToolAccessStatus.Allowed ? ToolAccessStatus.Failed : entry.Status : ToolAccessStatus.Checking; }
    public void Clear() { lock(sync) { generation++; entries.Clear(); } }
    public async Task Refresh()
    {
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
        var mappings = candidates.Where(x=>x.Value.Count==1).ToDictionary(x=>x.Key,x=>x.Value.Single(),StringComparer.OrdinalIgnoreCase);
        int current;
        lock(sync)
        {
            current=++generation;
            foreach(var key in entries.Keys.Where(k=>!mappings.TryGetValue(k,out var owner) || !string.Equals(owner,entries[k].Owner,StringComparison.OrdinalIgnoreCase)).ToArray()) entries.Remove(key);
            foreach(var pair in mappings) if(!entries.ContainsKey(pair.Key)) entries[pair.Key]=new(pair.Value,ToolAccessStatus.Checking,0);
            foreach(var pair in candidates.Where(p=>p.Value.Count!=1)) entries[pair.Key]=new("",ToolAccessStatus.Denied,Stopwatch.GetTimestamp());
        }
        using var limit=new SemaphoreSlim(4);
        await Task.WhenAll(mappings.Values.Distinct(StringComparer.OrdinalIgnoreCase).Select(async owner => {
            await limit.WaitAsync();
            try {
                var status=await Check(owner);
                lock(sync) {
                    if(current!=generation) return;
                    foreach(var pair in mappings.Where(p=>string.Equals(p.Value,owner,StringComparison.OrdinalIgnoreCase))) {
                        // Transient failures preserve a still-valid previous result, never its original expiration.
                        if(status==ToolAccessStatus.Failed && entries.TryGetValue(pair.Key,out var old) && Valid(old)) continue;
                        entries[pair.Key]=new(owner,status,Stopwatch.GetTimestamp());
                    }
                }
                Changed?.Invoke();
            } finally { limit.Release(); }
        }));
    }
    private async Task<ToolAccessStatus> Check(string owner)
    {
        try {
            using var response=await request("/repos/"+owner);
            if(!response.IsSuccessStatusCode) return response.StatusCode == HttpStatusCode.NotFound ? ToolAccessStatus.Denied : ToolAccessStatus.Failed;
            using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root=json.RootElement;
            if(!root.TryGetProperty("full_name",out var name) || !string.Equals(name.GetString(),owner,StringComparison.OrdinalIgnoreCase)) return ToolAccessStatus.Denied;
            var isPublic=root.TryGetProperty("private",out var privateValue) && privateValue.ValueKind==JsonValueKind.False;
            var readable=root.TryGetProperty("permissions",out var permission) && permission.ValueKind==JsonValueKind.Object && permission.TryGetProperty("pull",out var pull) && pull.ValueKind==JsonValueKind.True;
            if(!isPublic && !readable) return ToolAccessStatus.Denied;
            using var contents=await request("/repos/"+owner+"/contents");
            if(!contents.IsSuccessStatusCode) return contents.StatusCode == HttpStatusCode.NotFound ? ToolAccessStatus.Denied : ToolAccessStatus.Failed;
            using var listing=JsonDocument.Parse(await contents.Content.ReadAsStringAsync());
            return listing.RootElement.ValueKind==JsonValueKind.Array ? ToolAccessStatus.Allowed : ToolAccessStatus.Failed;
        } catch(Exception error) when(error is IOException or HttpRequestException or JsonException or OperationCanceledException or InvalidOperationException) { return ToolAccessStatus.Failed; }
    }
}
