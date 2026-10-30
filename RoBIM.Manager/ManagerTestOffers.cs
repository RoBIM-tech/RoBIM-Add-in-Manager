using RoBIM.Manager.Core;

namespace RoBIM.Manager;

public sealed partial class ManagerForm
{
    private string hostTestCard = "";
    private Dictionary<string, ReleaseOffer> testOffers = [];
    private Dictionary<string, ReleaseOffer> stableOffers = [];
    private Dictionary<string, string> testOfferErrors = [];
    private Dictionary<string, string> stableOfferErrors = [];
    private static bool IndependentRelease(ButtonCard card) => card.Kind == ButtonKind.Native || CadFeaturePackages.IsFeature(card.Owner.Package?.Package);
    private ReleaseOffer? ChannelOffer(ButtonCard card, string channel)
    {
        var id = card.Owner.Status.Tool.Id;
        var offer = (channel == "test" ? testOffers : stableOffers).GetValueOrDefault(id);
        var fallback = updateOffers.GetValueOrDefault(id);
        if (fallback?.Channel == channel) offer = NativeReleasePolicy.Newest(offer, fallback);
        if (card.CachedOffer?.Channel == channel) offer = NativeReleasePolicy.Newest(offer, card.CachedOffer);
        return offer;
    }
    private NativeReleaseStatus NativeReleaseState(ButtonCard card) => NativeReleasePolicy.Evaluate(
        card.SelectedVersion, card.SelectedChannel, ChannelOffer(card, "stable"), ChannelOffer(card, "test"));
    private ReleaseOffer? OfferFor(ButtonCard card) => IndependentRelease(card) && card.Selected
        ? NativeReleaseState(card).Update : (card.TestView ? testOffers : updateOffers).GetValueOrDefault(card.Owner.Status.Tool.Id);
    private bool SelectedInView(ButtonCard card) => card.Selected && (!card.TestView || card.SelectedChannel == "test");
    private bool NewTestAvailable(ButtonCard card)
    {
        if (!IndependentRelease(card) || (!card.Selected && card.Channel != "stable") || card.TestView || (card.Selected && card.SelectedChannel != "stable")) return false;
        return new ReleaseChannels(ChannelOffer(card, "stable"), ChannelOffer(card, "test"))
            .HasNewTest(card.Selected ? card.SelectedVersion : "");
    }
    private bool ListTestOffer(ButtonCard card) => NewTestAvailable(card) || IndependentRelease(card) &&
        card.Channel == "stable" && card.Selected && card.SelectedChannel == "test";
    private IEnumerable<ButtonCard> ProductCommands(string page, IEnumerable<ButtonCard>? inventory = null)
    {
        if (!CanUseTest && page == "Test") yield break;
        foreach (var original in inventory ?? CommandInventory())
        {
            var card = original;
            if (!CanUseTest && IndependentRelease(card) && card.CollectionChannel != "stable" && !card.Selected)
            {
                var stable = ChannelOffer(card, "stable");
                if (stable is null) continue;
                card = card with { Channel = "stable", ViewChannel = "", Selected = false, Pending = false,
                    SelectedChannel = "", SelectedVersion = "", ReleaseLabel = "정식 · " + stable.Version, CachedOffer = stable };
            }
            if (IndependentRelease(card) && card.Selected && card.SelectedChannel is "stable" or "test")
                card = card with { ReleaseLabel = (card.SelectedChannel == "test" ? "TEST" : "정식") + " · " + card.SelectedVersion + (card.Pending ? " (적용 준비)" : " (설치됨)") };
            if (page == "Mine" && IndependentRelease(card) && card.SelectedChannel == "test")
                card = card with { ViewChannel = "test" };
            if ((page == "Test" || card.Id == hostTestCard && page is "Revit" or "Cad") && card.CollectionChannel != "test")
            {
                if (!ListTestOffer(card)) { if (page != "Test") yield return card; continue; }
                var offer = ChannelOffer(card, "test");
                yield return card with { ViewChannel = "test", ReleaseLabel = card.Selected && card.SelectedChannel == "test" ? card.ReleaseLabel :
                    "TEST · " + (offer?.Version ?? "제공 확인 필요") + (card.Selected ? " · 현재 정식 " + card.SelectedVersion : "") };
            }
            else yield return card;
        }
    }
    private CardAppearance AppearanceFor(ButtonCard card)
    {
        var value = appearanceSnapshot.GetValueOrDefault(card.Id) ?? new(true, false, false, card.Message, "");
        var id = card.Owner.Status.Tool.Id;
        if (IndependentRelease(card) && !value.Legacy)
        {
            var target = card.Selected && SelectedInView(card) ? NativeReleaseState(card).Update : null;
            value = value with { Update = target is not null, Message = card.Message };
            if (card.Selected && card.SelectedChannel is "stable" or "test")
                value = value with { Message = (card.SelectedChannel == "test" ? "TEST" : "정식") + " " + card.SelectedVersion + " · " + card.Message };
            if (target is not null)
                value = value with { Message = (card.SelectedChannel == "test" && target.Channel == "stable" ? "정식 승격 업데이트" : target.Channel == "test" ? "TEST 업데이트" : "업데이트 가능") +
                    " · " + card.SelectedVersion + " → " + target.Version,
                    Instruction = "업데이트를 누르면 " + (target.Channel == "test" ? "TEST" : "정식") + " 버전을 준비하고 Revit에 자동 적용을 요청합니다. " + value.Instruction };
            if (card.TestView && !SelectedInView(card))
            {
                var offer = ChannelOffer(card, "test");
                value = value with { Enabled = value.Enabled && offer is not null, Update = false,
                    Message = (card.Selected ? "현재 정식 " + card.SelectedVersion + " · " : "") + "TEST " + (offer?.Version ?? "제공 확인 필요"),
                    Instruction = "TEST 설치는 이 버튼의 현재 버전을 교체합니다. 이전 정식으로 돌아가려면 ···의 저장소 정식 버전 설치를 선택하십시오. " + value.Instruction };
            }
            else if (CanUseTest && NewTestAvailable(card))
                value = value with { Message = "TEST 보기 · " + ChannelOffer(card, "test")!.Version + " 제공 · " + value.Message,
                    Instruction = value.Instruction + "\nTEST 보기를 눌러 선택 설치할 수 있습니다. 정식 설치는 자동으로 바뀌지 않습니다." };
            if (updateErrors.TryGetValue(id, out var error))
                value = value with { Message = value.Message + " · 최신 확인 실패", Instruction = value.Instruction + "\n마지막 확인 결과 유지: " + error };
        }
        if (testOfferErrors.TryGetValue(id, out var testError))
            value = value with { Message = value.Message + " · TEST 확인 실패", Instruction = value.Instruction + "\n마지막 TEST 조회 결과 유지: " + testError };
        if (stableOfferErrors.TryGetValue(id, out var stableError))
            value = value with { Message = value.Message + " · 정식 확인 실패", Instruction = value.Instruction + "\n마지막 정식 조회 결과 유지: " + stableError };
        if (!CanUseTest && card.Selected && card.SelectedChannel != "stable")
            value = value with { Update = false, Message = "사용 권한 없음 · 기존 설치 유지", Instruction = "TEST 도구는 실행할 수 없습니다. 제거하거나 TEST 권한을 요청하십시오." };
        return value;
    }
    private string PrimaryText(ButtonCard card, CardAppearance appearance)
    {
        if (card.Kind == ButtonKind.Discover) return "기능 목록 보기";
        if (appearance.Legacy) return "전환하기";
        if (card.TestView && !SelectedInView(card)) return "TEST 설치";
        if (appearance.Update)
            return IndependentRelease(card) && card.SelectedChannel == "test"
                ? NativeReleaseState(card).Update?.Channel == "stable" ? "정식 업데이트" : "TEST 업데이트" : "업데이트";
        return card.Selected ? "제거" : "설치하기";
    }
    private async Task PrimaryCardAction(ButtonCard card)
    {
        if (!CanUseTest && card.Selected && card.SelectedChannel != "stable") { await ToggleButton(card, false); return; }
        if (!CanUseTest && IndependentRelease(card) && !card.Selected && card.Channel == "stable") { await ToggleButton(card, true, "stable"); return; }
        if (card.TestView && !SelectedInView(card)) await ToggleButton(card, true, "test");
        else await ToggleButton(card);
    }
    private void ShowTestOffer(ButtonCard card)
    {
        if (!CanUseTest) return;
        hostTestCard = card.Id; SwitchPage(card.Kind == ButtonKind.Cad ? "Cad" : "Revit"); productSearch.Text = card.Title; RenderProductCards();
    }
}
