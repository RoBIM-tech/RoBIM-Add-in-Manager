using RoBIM.Manager.Core;

namespace RoBIM.Manager;

public sealed partial class ManagerForm
{
    private Dictionary<string, ReleaseOffer> testOffers = [];
    private Dictionary<string, string> testOfferErrors = [];
    private ReleaseOffer? OfferFor(ButtonCard card) => (card.TestView ? testOffers : updateOffers).GetValueOrDefault(card.Owner.Status.Tool.Id);
    private bool SelectedInView(ButtonCard card) => card.Selected && (!card.TestView || card.SelectedChannel == "test");
    private bool NewTestAvailable(ButtonCard card)
    {
        if (card.Kind != ButtonKind.Native || card.Channel != "stable" || card.TestView) return false;
        var id = card.Owner.Status.Tool.Id;
        var offer = testOffers.GetValueOrDefault(id);
        return new ReleaseChannels(updateOffers.GetValueOrDefault(id), offer)
            .HasNewTest(card.SelectedChannel == "stable" ? card.SelectedVersion : "") &&
            (!card.Selected || card.SelectedChannel != "test" || offer is not null && ReleaseUpdates.IsNewer(card.SelectedVersion, "", offer));
    }
    private bool ListTestOffer(ButtonCard card) => NewTestAvailable(card) || card.Kind == ButtonKind.Native &&
        card.Channel == "stable" && card.Selected && card.SelectedChannel == "test";
    private IEnumerable<ButtonCard> ProductCommands(string page, IEnumerable<ButtonCard>? inventory = null)
    {
        foreach (var card in inventory ?? CommandInventory())
        {
            if (page == "Test" && card.CollectionChannel != "test")
            {
                if (!ListTestOffer(card)) continue;
                var offer = testOffers.GetValueOrDefault(card.Owner.Status.Tool.Id);
                yield return card with { ViewChannel = "test", ReleaseLabel = "TEST · " + (offer?.Version ?? card.SelectedVersion) +
                    (card.Selected ? " · 현재 " + (card.SelectedChannel == "test" ? "TEST " : "정식 ") + card.SelectedVersion : "") };
            }
            else yield return card;
        }
    }
    private CardAppearance AppearanceFor(ButtonCard card)
    {
        var value = appearanceSnapshot.GetValueOrDefault(card.Id) ?? new(true, false, false, card.Message, "");
        var id = card.Owner.Status.Tool.Id;
        if (card.TestView)
        {
            var offer = testOffers.GetValueOrDefault(id);
            var selected = SelectedInView(card);
            var update = selected && RemoteUpdate(card);
            var available = offer is not null;
            value = value with {
                Enabled = value.Enabled && (selected || available), Update = update,
                Message = selected ? card.Message + (update ? " · TEST " + card.SelectedVersion + " → " + offer!.Version : " · TEST " + card.SelectedVersion)
                    : (card.Selected ? "현재 정식 " + card.SelectedVersion + " · " : "") + "TEST " + (offer?.Version ?? "제공 확인 필요"),
                Instruction = "TEST 설치는 이 버튼의 현재 버전을 교체합니다. 정식으로 돌아가려면 일반 Revit 목록에서 정식으로 전환을 누르십시오. " + value.Instruction };
        }
        else if (NewTestAvailable(card))
        {
            value = value with { Message = "TEST 보기 · " + testOffers[id].Version + " 제공 · " + value.Message,
                Instruction = value.Instruction + "\nTEST 보기를 눌러 TEST 목록에서 선택 설치할 수 있습니다. 정식 설치는 자동으로 바뀌지 않습니다." };
        }
        if (testOfferErrors.TryGetValue(id, out var error))
            value = value with { Message = value.Message + " · TEST 확인 실패", Instruction = value.Instruction + "\n마지막 TEST 조회 결과 유지: " + error };
        return value;
    }
    private string PrimaryText(ButtonCard card, CardAppearance appearance) => card.Kind == ButtonKind.Discover ? "기능 목록 보기" :
        appearance.Legacy ? "전환하기" : card.Selected && card.SelectedChannel == "test" && card.Channel == "stable" && !card.TestView ? "정식으로 전환" : card.TestView ? !SelectedInView(card) ? "TEST 설치" : appearance.Update ? "TEST 업데이트" : "제거" :
        appearance.Update ? "업데이트" : card.Selected ? "제거" : "설치하기";
    private async Task PrimaryCardAction(ButtonCard card)
    {
        if (card.TestView && (!SelectedInView(card) || RemoteUpdate(card))) await ToggleButton(card, true, "test");
        else await ToggleButton(card, card.TestView ? false : null);
    }
    private void ShowTestOffer(ButtonCard card)
    {
        SwitchPage("Test"); productSearch.Text = card.Title; RenderProductCards();
    }
}
