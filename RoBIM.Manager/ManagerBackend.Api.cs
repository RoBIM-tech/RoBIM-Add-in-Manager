using System.Reflection;
using RoBIM.Cad.Configuration;
using RoBIM.Manager.Core;

namespace RoBIM.Manager;

public sealed partial class ManagerBackend
{
    internal event Action? BackendChanged;
    internal event Action? BackendClosed;
    internal event Action<double?, string>? ManagerUpdateProgressChanged;
    internal Action? OpenModernUiRequested;
    internal Func<GitHubDeviceCode, CancellationTokenSource, Func<Task>, Task>? ModernDeviceLoginRequested;
    private bool hiddenBackendStarted;

    internal async Task StartHiddenBackendAsync()
    {
        if (hiddenBackendStarted || preview) return;
        hiddenBackendStarted = true;
        var display = StartLocalDisplay();
        _ = CheckManagerUpdate();
        await RestoreAccountAccess();
        await display;
        BackendChanged?.Invoke();
    }

    internal bool IsExitRequested => exitRequested;

    internal void CloseBackend()
    {
        exitRequested = true;
        Dispose();
    }

    internal ManagerPaths CorePaths => paths;
    internal bool IsBusy => busy;
    internal bool IsSignedIn => authenticated && connectedLogin.Length > 0;
    internal bool IsTestAllowed => CanUseTest;
    internal bool IsAccessAllowed => Allowed;
    internal string AccountSummaryText => accountStatusText;
    internal string FooterSummaryText => footerText;
    internal string ConnectedLogin => connectedLogin;
    internal string ManagerProductVersion => (typeof(ManagerBackend).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(ManagerBackend).Assembly.GetName().Version?.ToString()
        ?? "2.0.0").Split('+')[0];

    internal async Task CheckManagerUpdateFromModernAsync()
    {
        await CheckManagerUpdateManually();
        BackendChanged?.Invoke();
    }

    internal bool AllowPrereleaseChecked => allowPrereleaseChecked;
    internal bool AutoLoginChecked => automaticLoginChecked;
    internal bool ManagerAutoUpdateChecked => managerAutoUpdateChecked;
    internal bool StartWithWindowsChecked => startWithWindowsChecked;
    internal bool CanRunRevitSession => NativeRevitCommands.Sessions().Count > 0;
    internal int RevitSessionCount => NativeRevitCommands.Sessions().Count;
    internal bool HasPendingManagerUpdate => manuallyCheckedManagerUpdate is not null;
    internal bool IsCheckingManagerUpdate => checkingManagerUpdate;
    internal bool IsManagerUpdateReadyToRestart => pendingManagerUpdate is not null && !checkingManagerUpdate;
    internal string ManagerUpdateStatusText => managerUpdateStatusText;
    internal string LogsText
    {
        get { lock (logBuffer) return logBuffer.ToString(); }
    }
    internal IReadOnlyList<string> SavedAccountLogins
    {
        get { lock (savedAccountLogins) return savedAccountLogins.ToArray(); }
    }

    internal void SetAllowPrereleaseOption(bool value)
    {
        if (allowPrereleaseChecked == value) return;
        allowPrereleaseChecked = value;
        if (preview) return;
        new AccountPreferences(automaticLoginChecked, managerAutoUpdateChecked, allowPrereleaseChecked).Save(paths);
        manuallyCheckedManagerUpdate = null;
        pendingManagerUpdate = null;
        managerUpdateChecked = default;
        _ = CheckManagerUpdateFromModernAsync();
    }

    internal void SetAutoLoginOption(bool value)
    {
        if (automaticLoginChecked == value) return;
        automaticLoginChecked = value;
        if (!preview) new AccountPreferences(automaticLoginChecked, managerAutoUpdateChecked, allowPrereleaseChecked).Save(paths);
    }

    internal void SetManagerAutoUpdateOption(bool value)
    {
        if (managerAutoUpdateChecked == value) return;
        managerAutoUpdateChecked = value;
        if (!preview) new AccountPreferences(automaticLoginChecked, managerAutoUpdateChecked, allowPrereleaseChecked).Save(paths);
    }

    internal void SetStartWithWindowsOption(bool value)
    {
        if (startWithWindowsChecked == value) return;
        startWithWindowsChecked = value;
        if (preview) return;
        try { StartupRegistration.Set(Environment.ProcessPath!, startWithWindowsChecked); }
        catch (Exception error)
        {
            startWithWindowsChecked = !value;
            ShowInfo(error.Message, "Windows 자동 시작");
        }
    }

    internal async Task InstallManagerUpdateFromModernAsync()
    {
        await InstallManagerUpdateManually();
        BackendChanged?.Invoke();
    }

    internal void ApplyReadyManagerUpdateNow()
    {
        if (pendingManagerUpdate is not { } pending || busy || disposed) return;
        ManagerUpdates.Launch(paths, pending.Installer, pending.Offer.Digest);
        CloseBackend();
    }

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
    internal bool IsRepositoryPermitted(ButtonCard card) => RepositoryPermitted(card);
    internal (bool Update, bool Legacy, bool Enabled, string Message) GetCardAppearance(ButtonCard card)
    {
        var a = AppearanceFor(card);
        return (a.Update, a.Legacy, a.Enabled, a.Message);
    }
    internal bool IsLegacyNativeCard(ButtonCard card) => new NativeLegacyPackages(paths).Present(card.Owner.Status.Tool);
    internal bool IsCadHostConfigurable => productRows.FirstOrDefault(x => x.Status.Tool.Id == Catalog.Cad.Id) is { } cadRow && CadConfigurable(cadRow);

    internal Task RefreshAllAsync() => RefreshWithUpdates();

    internal async Task ToggleCardFromModernAsync(ButtonCard card, bool? installChoice = null, string? channel = null)
    {
        await ToggleButton(card, installChoice, channel);
        BackendChanged?.Invoke();
    }

    internal async Task RefreshCardCatalogFromModernAsync(ButtonCard card)
    {
        await LoadButtonCatalog(card.Owner);
        BackendChanged?.Invoke();
    }

    internal async Task ImportNativeZipFromModernAsync()
    {
        await ImportNativePackage();
        BackendChanged?.Invoke();
    }

    internal async Task ImportCadZipFromModernAsync()
    {
        var cadRow = productRows.FirstOrDefault(x => x.Status.Tool.Id == Catalog.Cad.Id);
        if (cadRow is not null) SelectProduct(cadRow.Status.Tool.Id);
        await PrepareCadArchive();
        BackendChanged?.Invoke();
    }

    internal async Task ConfigureCadButtonsFromModernAsync(ButtonCard? card = null)
    {
        var targetId = card?.Owner.Status.Tool.Id ?? Catalog.Cad.Id;
        SelectProduct(targetId);
        await ConfigureCadButtons(false);
        await RefreshProductListAsync();
        BackendChanged?.Invoke();
    }

    internal async Task RestoreBackupFromModernAsync(ButtonCard card)
    {
        SelectProduct(card.Owner.Status.Tool.Id);
        await ManageProduct(true);
        BackendChanged?.Invoke();
    }

    internal void OpenCardFolderFromModern(ButtonCard card) => OpenFolder(card.Owner.Status.Path);
    internal void OpenCardGitHubFromModern(ButtonCard card) => OpenUrl("https://github.com/" + card.Owner.Status.Tool.Repository);
    internal void ShowCardDetailsFromModern(ButtonCard card)
    {
        var row = card.Owner;
        var status = row.Status;
        var text = string.Join(Environment.NewLine + Environment.NewLine,
            card.Description,
            row.Package?.Package.Description,
            $"분류: {card.Group}",
            $"배포 구분: {card.ReleaseLabel} · {card.ReleaseDetail}",
            $"현재 상태: {card.Message}",
            $"설치 버전: {(string.IsNullOrWhiteSpace(status.Version) ? "없음" : status.Version)}",
            $"설치 경로: {status.Path}",
            $"원본/저장소: {card.Source ?? status.Tool.Repository}");
        ShowInfo(text, card.Title);
    }

    private void RequireInstalledRevitLoader()
    {
        var host = installer.Status(Catalog.RevitHost);
        var dll = Path.Combine(host.Path, "Contents", "2026", "RoBIM.Revit.Host.dll");
        if (!host.Managed || host.Modified || host.External || !File.Exists(dll))
            throw new IOException("프로그램 로더 화면에서 Revit 로더 설치 상태를 확인하고 적용하십시오.");
    }

    internal void EditPublishersFromModern() => EditRepositoryPublishers();

    internal async Task RegisterGitUrlFromModernAsync(string url)
    {
        await Run("Git URL 확인", async () =>
        {
            var repository = ExtensionCatalog.Repository(url);
            var entry = await ExtensionSources().Inspect("https://github.com/" + repository);
            ExtensionSources().Register(entry);
            await RefreshExtensionCatalog();
        });
        BackendChanged?.Invoke();
    }

    internal async Task RefreshExtensionCatalogFromModernAsync()
    {
        await Run("애드인 저장소 조회", RefreshAccountAndCatalog);
        BackendChanged?.Invoke();
    }

    internal void RequestManualRevitReloadFromModern()
    {
        NativeCommands().RequestReload();
        WriteLog("Revit Reload 요청을 전송했습니다.");
        BackendChanged?.Invoke();
    }

    internal async Task ReprepareRevitSessionsFromModernAsync()
    {
        await Run("현재 Revit에 다시 준비", async () =>
        {
            RequireInstalledRevitLoader();
            var manager = NativeCommands();
            manager.ReprepareForCurrentSessions();
            new RibbonLayouts(paths).RequestRevit(false);
            await RefreshStatuses();
        });
        BackendChanged?.Invoke();
    }

    internal async Task RunBatchUpdateForHostAsync(string hostKey)
    {
        await UpdateProgramTools(hostKey);
        BackendChanged?.Invoke();
    }

    internal ToolStatus GetLoaderStatus(ToolSpec tool) => installer.Status(tool);
    internal ReleaseChannels? GetLoaderOffers(ToolSpec tool) => loaderOffers.GetValueOrDefault(tool.Id);

    internal async Task CheckLoaderForHostAsync(string hostKey)
    {
        var tool = hostKey == "autocad" ? Catalog.Cad : Catalog.RevitHost;
        await CheckLoaderRepository(tool);
        BackendChanged?.Invoke();
    }

    internal async Task ApplyLoaderForHostAsync(string hostKey, string channel = "stable")
    {
        if (hostKey == "revit") await ApplyRevitLoader(channel);
        else if (hostKey == "autocad") await ApplyCadLoader(channel);
        BackendChanged?.Invoke();
    }

    internal async Task SignInOrRefreshFromModernAsync()
    {
        await RefreshAccount();
        BackendChanged?.Invoke();
    }

    internal async Task LoginOtherGitHubFromModernAsync()
    {
        await Run("GitHub 로그인", async () =>
        {
            teamAccess?.Lock();
            await LoginGitHub();
            await RefreshAccountAndCatalog();
        }, requiresAccess: false);
        BackendChanged?.Invoke();
    }

    internal async Task UseSavedAccountFromModernAsync(string loginName)
    {
        await Run("GitHub 계정 선택", async () =>
        {
            if (string.IsNullOrWhiteSpace(loginName)) throw new IOException("저장된 계정을 선택하십시오.");
            teamAccess?.Lock();
            await GitHubSession.UseBrowser(loginName);
            await RefreshAccountAndCatalog();
        }, requiresAccess: false);
        BackendChanged?.Invoke();
    }

    internal async Task UseTokenFromModernAsync(string token)
    {
        await Run("토큰 확인 및 저장", async () =>
        {
            teamAccess?.Lock();
            await GitHubSession.UseToken(token);
            await RefreshAccountAndCatalog();
        }, requiresAccess: false);
        BackendChanged?.Invoke();
    }

    internal async Task RemoveTokenFromModernAsync()
    {
        await Run("현재 토큰 삭제", async () =>
        {
            teamAccess?.Lock();
            GitHubSession.RemoveToken();
            await RefreshAccount();
        }, requiresAccess: false);
        BackendChanged?.Invoke();
    }

    internal void SignOutFromModern()
    {
        SignOut();
        BackendChanged?.Invoke();
    }

    internal void OpenLoaderFolderFromModern(ToolSpec tool) => OpenFolder(paths.Target(tool));
    internal void OpenLoaderRepoFromModern(ToolSpec tool) => OpenUrl("https://github.com/" + tool.Repository);

    private static string? ResolveManagementIcon(string fileName, string? cadRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(cadRoot))
        {
            var cadIcon = Path.Combine(cadRoot, "Icons", fileName);
            if (File.Exists(cadIcon)) return cadIcon;
            var cadRootIcon = Path.Combine(cadRoot, fileName);
            if (File.Exists(cadRootIcon)) return cadRootIcon;
        }
        var addins = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "Revit", "Addins", "2026", "RoBIM.Revit.Host.bundle", "Contents", "2026", "Icons", fileName);
        if (File.Exists(addins)) return addins;
        return "res://" + fileName;
    }

    internal IReadOnlyList<RibbonTool> GetRibbonToolsForHost(string hostKey)
    {
        if (hostKey == "revit")
        {
            var reloadIcon = ResolveManagementIcon("reload.png");
            var managerIcon = ResolveManagementIcon("manager.png");
            return RibbonLayouts.WithManagementTools(CommandInventory().Where(x => x.Kind == ButtonKind.Native)
                .Select(x => RibbonLayouts.FromGroup(x.Id, x.Title, x.Group, x.Selected, x.Icon)))
                .Select(t => t.Id == "manager:reload" && t.Icon is null ? t with { Icon = reloadIcon }
                    : t.Id == "manager:manager" && t.Icon is null ? t with { Icon = managerIcon }
                    : t)
                .ToArray();
        }
        if (hostKey == "autocad")
        {
            var cadRoot = productRows.FirstOrDefault(x => x.Status.Tool.Id == Catalog.Cad.Id) is { } cadRow
                ? new CadReload(paths).Source(cadRow.Status.Path, preferInstalled: true)?.Root
                : null;
            var reloadIcon = ResolveManagementIcon("reload.png", cadRoot);
            var managerIcon = ResolveManagementIcon("manager.png", cadRoot);
            return CommandInventory().Where(x => x.Kind == ButtonKind.Cad)
                .Select(x => RibbonLayouts.FromGroup(x.Id, x.Title, x.Group, x.Selected, x.Icon))
                .Concat([
                    new RibbonTool("manager:cad-reload", "Reload", "RoBIM 관리", "관리", true, reloadIcon),
                    new RibbonTool("manager:cad-manager", "Add-in Manager", "RoBIM 관리", "관리", true, managerIcon)
                ])
                .ToArray();
        }
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
        if (hostName == "AutoCAD")
        {
            var owner = productRows.FirstOrDefault(x => x.Status.Tool.Id == Catalog.Cad.Id);
            if (owner is not null && CadConfigurable(owner))
            {
                var bridge = new CadReload(paths);
                var source = bridge.Source(owner.Status.Path, preferInstalled: true);
                if (source is not null)
                {
                    var selected = source.Previous?.SelectedActions ?? [];
                    bridge.Prepare(owner.Status.Tool, source.Bundle, source.Catalog, selected, null, source.Previous);
                    BackendChanged?.Invoke();
                    return "AutoCAD 리본 배치를 저장하고 적용을 준비했습니다. AutoCAD에서 ROBIMRELOAD를 실행하세요.";
                }
            }
        }
        await Task.Yield();
        BackendChanged?.Invoke();
        return hostName + " 리본 배치를 저장했습니다.";
    }

    private void EditRepositoryPublishers()
    {
        if (busy || preview) return;
        try
        {
            var discovery = new RepositoryDiscovery(paths);
            var dialog = new RepositoryPublishersWindow(discovery.Settings());
            if (OwnerWindow is not null && OwnerWindow.IsVisible) dialog.Owner = OwnerWindow;
            if (dialog.ShowDialog() != true || dialog.Result is null) return;
            discovery.SaveSettings(dialog.Result);
            footerText = "검색 대상 저장 완료 · 새로고침으로 새 도구를 확인하세요. 기존 설치와 리본은 유지됩니다.";
        }
        catch (Exception error)
        {
            ShowInfo(error.Message, "검색 대상 설정");
        }
    }
}
