using RoBIM.Manager.Core;

namespace RoBIM.Manager;

public sealed partial class ManagerForm
{
    internal event Action? BackendChanged;
    internal Action? OpenModernUiRequested;

    internal async Task StartHiddenBackendAsync()
    {
        _ = Handle;
        var display = StartLocalDisplay();
        _ = CheckManagerUpdate();
        await RestoreAccountAccess();
        await display;
        BackendChanged?.Invoke();
    }

    internal void CloseBackend()
    {
        exitRequested = true;
        Close();
    }

    internal ManagerPaths CorePaths => paths;
    internal bool IsBusy => busy;
    internal bool IsSignedIn => authenticated && connectedLogin.Length > 0;
    internal bool IsTestAllowed => CanUseTest;
    internal bool IsAccessAllowed => Allowed;
    internal string AccountSummaryText => accountStatus.Text;
    internal string FooterSummaryText => footer.Text;
    internal string ConnectedLogin => connectedLogin;
    internal bool AllowPrereleaseChecked => allowPrerelease.Checked;
    internal bool AutoLoginChecked => automaticLogin.Checked;
    internal bool StartWithWindowsChecked => startWithWindows.Checked;
    internal bool CanRunRevitSession => NativeRevitCommands.Sessions().Count > 0;
    internal int RevitSessionCount => NativeRevitCommands.Sessions().Count;

    internal void SetAllowPrereleaseOption(bool value) { if (allowPrerelease.Checked != value) allowPrerelease.Checked = value; }
    internal void SetAutoLoginOption(bool value) { if (automaticLogin.Checked != value) automaticLogin.Checked = value; }
    internal void SetStartWithWindowsOption(bool value) { if (startWithWindows.Checked != value) startWithWindows.Checked = value; }

    internal IReadOnlyList<ButtonCard> GetCardsForHost(string hostKey)
    {
        var all = CommandInventory();
        return hostKey switch
        {
            "revit" => all.Where(x => x.Kind is ButtonKind.Native or ButtonKind.Discover).ToArray(),
            "autocad" => all.Where(x => x.Kind == ButtonKind.Cad).ToArray(),
            _ => []
        };
    }

    internal ReleaseOffer? GetUpdateOfferForCard(ButtonCard card) => card.Selected ? OfferFor(card) : null;
    internal bool HasUpdateForCard(ButtonCard card) => card.Selected && OfferFor(card) is not null;
    internal bool HasTestOfferForCard(ButtonCard card) => CanUseTest && NewTestAvailable(card);
    internal int GetBatchUpdateCount(string hostKey) => hostKey is "revit" or "autocad" ? BatchPlan(hostKey).Length : 0;

    internal Task RefreshAllAsync() => RefreshWithUpdates();

    internal async Task ToggleCardFromModernAsync(ButtonCard card, bool? installChoice = null, string? channel = null)
    {
        await ToggleButton(card, installChoice, channel);
        BackendChanged?.Invoke();
    }

    internal async Task RunBatchUpdateForHostAsync(string hostKey)
    {
        currentPage = hostKey == "autocad" ? "Cad" : "Revit";
        await UpdateProgramTools();
        BackendChanged?.Invoke();
    }

    internal ToolStatus GetLoaderStatus(ToolSpec tool) => installer.Status(tool);

    internal async Task ApplyLoaderForHostAsync(string hostKey)
    {
        if (hostKey == "revit") await ApplyRevitLoader("stable");
        else if (hostKey == "autocad") await ApplyCadLoader("stable");
        BackendChanged?.Invoke();
    }

    internal async Task SignInOrRefreshFromModernAsync()
    {
        await RefreshAccount();
        BackendChanged?.Invoke();
    }

    internal void SignOutFromModern()
    {
        SignOut();
        BackendChanged?.Invoke();
    }

    internal IReadOnlyList<RibbonTool> GetRibbonToolsForHost(string hostKey)
    {
        if (hostKey == "revit")
            return RibbonLayouts.WithManagementTools(CommandInventory().Where(x => x.Kind == ButtonKind.Native)
                .Select(x => RibbonLayouts.FromGroup(x.Id, x.Title, x.Group, x.Selected, x.Icon))).ToArray();
        if (hostKey == "autocad")
            return CommandInventory().Where(x => x.Kind == ButtonKind.Cad)
                .Select(x => RibbonLayouts.FromGroup(x.Id, x.Title, x.Group, x.Selected, x.Icon)).ToArray();
        return [];
    }

    internal RibbonLayout LoadRibbonLayoutForHost(string hostKey)
    {
        var hostName = hostKey == "autocad" ? "AutoCAD" : hostKey == "navisworks" ? "Navisworks" : "Revit";
        var store = new RibbonLayouts(paths);
        var tools = GetRibbonToolsForHost(hostKey);
        var saved = !File.Exists(store.FilePath(hostName)) ? RibbonLayouts.Defaults(hostName, tools) : store.Read(hostName);
        return hostKey == "revit" ? RibbonLayouts.IncludeMissingManagement(saved, tools) : saved;
    }

    internal async Task<string> SaveAndApplyRibbonForHostAsync(string hostKey, RibbonLayout layout)
    {
        var hostName = hostKey == "autocad" ? "AutoCAD" : hostKey == "navisworks" ? "Navisworks" : "Revit";
        var store = new RibbonLayouts(paths);
        store.Save(layout);
        if (hostName == "Revit")
        {
            if (RevitRuntimePolicy.NativeOnly(paths)) RequireInstalledRevitLoader();
            var msg = RequestAutomaticRevitApply(native: true, pyRevit: false, layout: true);
            BackendChanged?.Invoke();
            return msg;
        }
        await Task.Yield();
        BackendChanged?.Invoke();
        return hostName + " 리본 배치를 저장했습니다.";
    }
}
