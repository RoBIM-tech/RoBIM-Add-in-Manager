using RoBIM.Manager.Core;

namespace RoBIM.Manager;

public sealed partial class ManagerForm
{
    private Dictionary<string, ReleaseOffer> updateOffers = [];
    private readonly Dictionary<string, string> updateErrors = [];
    private bool RemoteUpdate(ButtonCard card) => card.Selected && OfferFor(card) is { } offer
        && ReleaseUpdates.IsNewer(card.SelectedVersion, card.TestView ? "" : card.SelectedChannel, offer);

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

    private PackageRow[] UpdateTargets()
    {
        var registered = availablePackages.ToList();
        if (!registered.Any(p => ExtensionCatalog.Tool(p.Repository, p.Package).Id == Catalog.Revit.Id))
        {
            var package = new ExtensionPackage("robim-revit", "RoBIM for Revit", "pyrevit", ".", Catalog.Revit.FolderName);
            registered.Add(new(new(Catalog.Revit.Repository, Catalog.Revit.Branch, [package], "기본 pyRevit"), package));
        }
        return registered.ToArray();
    }

    private sealed record UpdateCheck(string Id, ReleaseOffer? Offer, string? Error,
        bool CheckedTest = false, ReleaseOffer? Test = null, string? TestError = null);
    private async Task RefreshWithUpdates()
    {
        await Run("설치 상태 · 정식/TEST 업데이트 확인", async () =>
        {
            await LoadRegisteredPackages();
            var targets = UpdateTargets().GroupBy(x => x.Repository.Repository, StringComparer.OrdinalIgnoreCase).ToArray();
            using var gate = new SemaphoreSlim(4);
            var results = await Task.WhenAll(targets.Select(async group =>
            {
                await gate.WaitAsync();
                try
                {
                    var repository = await new ExtensionCatalog(paths).Inspect("https://github.com/" + group.Key);
                    foreach (var previous in group) ExtensionCatalog.CurrentPackage(previous.Package, repository);
                    var entries = new List<UpdateCheck>();
                    foreach (var previous in group)
                    {
                        var id = ExtensionCatalog.Tool(previous.Repository, previous.Package).Id;
                        try
                        {
                            var package = ExtensionCatalog.CurrentPackage(previous.Package, repository);
                            if (package.Host == "revit" && package.Distribution == "release" && package.Channel is "stable" or "test")
                            {
                                var channels = await ReleaseUpdates.CheckChannels(repository, package);
                                var offer = package.Channel == "test" ? channels.Test : channels.Stable;
                                var error = package.Channel == "test" ? channels.TestError : channels.StableError;
                                entries.Add(new(id, offer, error ?? (offer is null ? "기본 배포 구분의 ZIP이 없습니다." : null), true, channels.Test, channels.TestError));
                            }
                            else entries.Add(new(id, await ReleaseUpdates.Check(repository, package), null));
                        }
                        catch (Exception ex) { entries.Add(new(id, null, ex.Message)); }
                    }
                    return (Repository: repository, Entries: entries);
                }
                catch (Exception ex)
                {
                    return (Repository: (ExtensionRepository?)null, Entries: group.Select(x => new UpdateCheck(ExtensionCatalog.Tool(x.Repository, x.Package).Id, null, ex.Message)).ToList());
                }
                finally { gate.Release(); }
            }));
            updateErrors.Clear(); testOfferErrors.Clear();
            foreach (var result in results)
            {
                if (result.Repository is not null) new ExtensionCatalog(paths).Register(result.Repository);
                foreach (var entry in result.Entries)
                {
                    if (entry.Offer is not null) updateOffers[entry.Id] = entry.Offer;
                    else { updateErrors[entry.Id] = entry.Error ?? "조회 실패"; WriteLog(entry.Id + " 최신 확인 실패: " + entry.Error); }
                    if (entry.CheckedTest && entry.TestError is null)
                    {
                        if (entry.Test is null) testOffers.Remove(entry.Id); else testOffers[entry.Id] = entry.Test;
                    }
                    else if (entry.CheckedTest) testOfferErrors[entry.Id] = entry.TestError!;
                }
            }
            await RefreshStatuses();
            var count = appearanceSnapshot.Count(x => x.Value.Update);
            var tests = CommandInventory().Count(NewTestAvailable);
            refreshSummary = "확인 완료 · 업데이트 " + count + "개 · 새 TEST " + tests + "개 (TEST 도구에서 확인)" +
                (updateErrors.Count + testOfferErrors.Count == 0 ? "" : " · 일부 조회 실패 · 기존 상태 유지");
            WriteLog(refreshSummary);
        });
        if (refreshSummary is not null) { footer.Text = refreshSummary; refreshSummary = null; }
    }
    private string? refreshSummary;
}
