using RoBIM.Manager.Core;

namespace RoBIM.Manager;

public sealed partial class ManagerForm
{
    private Dictionary<string, ReleaseOffer> updateOffers = [];
    private readonly Dictionary<string, string> updateErrors = [];
    private bool RemoteUpdate(ButtonCard card) => card.Selected && updateOffers.TryGetValue(card.Owner.Status.Tool.Id, out var offer)
        && ReleaseUpdates.IsNewer(card.SelectedVersion, card.SelectedChannel, offer);

    // Applying an explicitly chosen change does not opt into unrelated cached updates.
    private string RequestAutomaticRevitApply(bool native, bool pyRevit, bool layout = false)
    {
        if (!ReloadBridge.RevitRunning()) return "변경 저장 완료 · 다음 Revit 시작 시 적용";
        var messages = new List<string>();
        if (native && File.Exists(Path.Combine(paths.Data, "NativeRevit", "pending.json")))
        {
            try { new NativeRevitCommands(paths).RequestReload(includeCachedUpdates: false); messages.Add("독립 Add-in 자동 적용 요청 완료"); }
            catch (Exception ex) { messages.Add("변경 준비 유지 · 자동 적용 요청 실패: " + ex.Message); }
        }
        if (pyRevit || layout)
        {
            try { messages.Add(new RibbonLayouts(paths).RequestRevit(pyRevit)); }
            catch (Exception ex) { messages.Add("배치/선택 저장 유지 · 자동 적용 요청 실패: " + ex.Message); }
        }
        return string.Join(" · ", messages.Count == 0 ? ["변경 저장 완료"] : messages) + " · Revit 대기 상태에서 처리하며 실제 응답 후 적용 완료로 표시합니다.";
    }

    private async Task RefreshWithUpdates()
    {
        await Run("설치 상태 · 업데이트 확인", async () =>
        {
            await LoadRegisteredPackages();
            var targets = availablePackages.ToArray().GroupBy(x => x.Repository.Repository, StringComparer.OrdinalIgnoreCase).ToArray();
            using var gate = new SemaphoreSlim(4);
            // Fetch independently; one denied/offline repository must not discard other results.
            var results = await Task.WhenAll(targets.Select(async group =>
            {
                await gate.WaitAsync();
                try
                {
                    var catalog = new ExtensionCatalog(paths);
                    var repository = await catalog.Inspect("https://github.com/" + group.Key);
                    foreach (var previous in group) ExtensionCatalog.CurrentPackage(previous.Package, repository);
                    var entries = new List<(string Id, ReleaseOffer? Offer, string? Error)>();
                    foreach (var previous in group)
                    {
                        var id = ExtensionCatalog.Tool(previous.Repository, previous.Package).Id;
                        try
                        {
                            var package = ExtensionCatalog.CurrentPackage(previous.Package, repository);
                            entries.Add((id, await ReleaseUpdates.Check(repository, package), null));
                        }
                        catch (Exception ex) { entries.Add((id, null, ex.Message)); }
                    }
                    return (Repository: repository, Entries: entries);
                }
                catch (Exception ex)
                {
                    return (Repository: (ExtensionRepository?)null, Entries: group.Select(x => (ExtensionCatalog.Tool(x.Repository, x.Package).Id, (ReleaseOffer?)null, (string?)ex.Message)).ToList());
                }
                finally { gate.Release(); }
            }));
            updateErrors.Clear();
            foreach (var result in results)
            {
                if (result.Repository is not null) new ExtensionCatalog(paths).Register(result.Repository);
                foreach (var (id, offer, error) in result.Entries)
                    if (offer is not null) updateOffers[id] = offer;
                    else { updateErrors[id] = error ?? "조회 실패"; WriteLog(id + " 최신 확인 실패: " + error); }
            }
            await RefreshStatuses();
            var count = appearanceSnapshot.Count(x => x.Value.Update);
            refreshSummary = "업데이트 확인 완료 · 업데이트 " + count + "개" + (updateErrors.Count == 0 ? "" : " · 조회 실패 " + updateErrors.Count + "개 (기존 상태 유지)");
            WriteLog(refreshSummary);
        });
        if (refreshSummary is not null) { footer.Text = refreshSummary; refreshSummary = null; }
    }
    private string? refreshSummary;
}
