using System.Drawing;
using System.IO;
using RoBIM.Cad.Configuration;
using RoBIM.Manager.Core;
using RoBIM.Revit.Reload;

namespace RoBIM.Manager;

public sealed partial class ManagerForm
{
    private enum ButtonKind { Cad, PyRevit, Native, Discover }
    private sealed record ButtonCard(string Id, string Title, string Description, string Group, string? Icon, ProductRow Owner,
        ButtonKind Kind, bool Selected, bool Pending, string Message, string? Source, string CommandId, string ModuleId = "", string Channel = "unknown", string ReleaseLabel = "미확인", string ReleaseDetail = "", string SelectedChannel = "", string SelectedVersion = "", string ViewChannel = "", ReleaseOffer? CachedOffer = null)
    {
        public bool TestView => ViewChannel == "test";
        public string CollectionChannel => TestView ? "test" : (Selected || Pending) && SelectedChannel is "test" or "stable" ? SelectedChannel : Channel;
    }
    private readonly Dictionary<string, RevitButton[]> revitButtonCache = new(StringComparer.OrdinalIgnoreCase);
    private SourcePackage? cadButtonPackage;
    private SourcePackage? revitButtonPackage;
    private readonly Button nativeReload = MakeButton("Revit Reload");

    private CadButtonSource? CadButtons(ProductRow owner, bool preferInstalled = false)
    {
        return new CadReload(paths).Source(CadFeaturePackages.IsFeature(owner.Package?.Package) ? paths.Target(Catalog.Cad) : owner.Status.Path, cadButtonPackage, preferInstalled);
    }
    private (string? Root, RevitButton[] Buttons, string[] Selected, bool Pending) RevitInventory(ProductRow owner)
    {
        var inventory = RevitButtonInventory.Read(paths, owner.Status, revitButtonPackage);
        return (inventory.Root, inventory.Buttons, inventory.Selected, inventory.Pending);
    }
    private IEnumerable<ButtonCard> CommandInventory() => preview ? PreviewCommands() : commandSnapshot.Where(x => !RevitRuntimePolicy.NativeOnly(paths) || !RevitRuntimePolicy.LegacyRoBIM(x.Owner.Status.Tool));
    private IEnumerable<ButtonCard> ReadCommandInventory(IEnumerable<ProductRow> owners)
    {
        if (preview)
        {
            foreach (var card in PreviewCommands()) yield return card;
            yield break;
        }
        inventoryScanCount++;
        foreach (var owner in owners.Where(x => x.Status.Tool.Kind != ToolKind.NativeRevitHost && (!RevitRuntimePolicy.NativeOnly(paths) || !RevitRuntimePolicy.LegacyRoBIM(x.Status.Tool))))
        {
            var cards = new List<ButtonCard>();
            if (owner.Package is { } registeredCad && CadFeaturePackages.IsFeature(registeredCad.Package))
            {
                var package = registeredCad.Package;
                var current = new CadReload(paths).Source(paths.Target(Catalog.Cad), preferInstalled: true);
                var selectedModule = current?.Catalog.Modules.SingleOrDefault(m => m.Id == package.ModuleId);
                var key = package.ModuleId + ":" + package.CommandId;
                var state = CadButtonState(owner, key);
                var release = selectedModule is null || current is null ? null : ModuleReleases.Cad(current.Root, selectedModule.Id, selectedModule.Version);
                yield return new("cad:" + key, package.Name, package.Description, "AutoCAD", null, owner, ButtonKind.Cad,
                    state.Selected, state.Pending, state.Message, null, key, package.ModuleId, package.Channel,
                    release?.Label ?? "설치 시 버전 확인", SelectedChannel: release?.Channel ?? "", SelectedVersion: selectedModule?.Version ?? "");
                continue;
            }
            try
            {
                if (owner.Status.Tool.Kind == ToolKind.AutoCAD)
                {
                    var source = CadButtons(owner);
                    if (source is not null)
                        foreach (var module in source.Catalog.Modules)
                            foreach (var command in module.Actions.OrderBy(x => x.Order))
                            {
                                var key = CadFiles.Key(module, command);
                                if (key == "delhidden:preview" || owners.Any(o => o.Package is { } p && CadFeaturePackages.IsFeature(p.Package) && p.Package.ModuleId + ":" + p.Package.CommandId == key)) continue;
                                var state = CadButtonState(owner, key);
                                var release = ModuleReleases.Cad(source.Root, module.Id, module.Version);
                                cards.Add(new("cad:" + key, command.Title, command.Description, command.Panel + " / " + module.Name, command.Icon is null ? null : CadFiles.Child(source.Root, command.Icon), owner, ButtonKind.Cad, state.Selected, state.Pending, state.Message, source.Bundle, key, module.Id, release.Channel, release.Label, release.Detail, SelectedVersion: CadSelectedVersion(owner, module.Id)));
                            }
                }
                else if (owner.Status.Tool.Kind == ToolKind.PyRevit)
                {
                    var inventory = RevitInventory(owner);
                    var pending = inventory.Pending;
                    var selectedVersion = new RevitReload(paths).PendingSelection()?.Version ?? statuses.GetValueOrDefault(Catalog.RevitSelected.Id)?.Version ?? owner.Status.Version;
                    var iconCache = inventory.Root is null ? null : RevitButtons.ReadSelection(inventory.Root)?.Cache?.Root;
                    string? Icon(string? relative)
                    {
                        if (relative is null) return null;
                        var active = SafeFiles.Child(inventory.Root!, relative);
                        return File.Exists(active) ? active : iconCache is not null ? SafeFiles.Child(iconCache, relative) : RevitButtons.ResourcePath(inventory.Root!, relative);
                    }
                    foreach (var command in inventory.Buttons) cards.Add(new(owner.Status.Tool.Id + ":" + command.Id, command.Title, command.Description, command.Group, Icon(command.Icon), owner, ButtonKind.PyRevit, inventory.Selected.Contains(command.Id), pending, inventory.Selected.Contains(command.Id) ? (pending ? "설치 준비 · pyRevit Reload 대기" : "설치됨") : (pending ? "제거 준비 · pyRevit Reload 대기" : "설치 안 됨"), inventory.Root, command.Id, SelectedVersion: selectedVersion));
                }
                else
                {
                    var manager = NativeCommands();
                    var registration = owner.Package is null ? null : SingleNativeButton.Registration(owner.Package.Repository, owner.Package.Package);
                    var source = registration is null ? manager.CachedForFolder(owner.Status.Tool.FolderName) : manager.Cached(registration.ModuleId);
                    if (source is null && File.Exists(Path.Combine(owner.Status.Path, NativeProtocol.ManifestName))) source = new(owner.Status.Path, owner.Status.Version);
                    if (source is not null)
                    {
                        var module = owner.Package is not null && registration is not null
                            ? SingleNativeButton.Validate(owner.Package.Repository, owner.Package.Package, source, requireChannel: false) : NativeProtocol.Module(source.Path);
                        var release = ModuleReleases.Native(source.Path, module.Id, module.Version);
                        var chosen = manager.Desired()?.Modules.FirstOrDefault(x => x.Manifest.Id == module.Id);
                        var selectedRelease = chosen is null ? null : ModuleReleases.Native(chosen.Directory, chosen.Manifest.Id, chosen.Manifest.Version);
                        var availableChannel = owner.Package?.Package.Channel is "test" or "stable" ? owner.Package.Package.Channel : release.Channel;
                        var availableLabel = availableChannel == release.Channel ? release.Label : availableChannel == "stable" ? "실사용 제공" : "TEST 제공";
                        foreach (var command in module.Commands)
                        {
                            var state = manager.State(module.Id, command.Id);
                            if (new NativeLegacyPackages(paths).Present(owner.Status.Tool)) state = (true, false, "기존 직접 설치 · Reload 방식 전환 필요");
                            cards.Add(new("native:" + module.Id + ":" + command.Id, command.Title, command.Description, command.Tab + " / " + command.Panel, command.Icon is null ? null : NativeProtocol.Child(source.Path, command.Icon), owner, ButtonKind.Native, state.Selected, state.Pending, state.Message, source.Path, command.Id, module.Id, availableChannel, selectedRelease is not null && (state.Selected || state.Pending)
                                ? availableChannel != selectedRelease.Channel ? availableLabel + " · 설치: " + selectedRelease.Label : selectedRelease.Label + " (선택)" : availableLabel,
                                "배포: " + availableLabel + "\n캐시: " + release.Label + "\n설치/준비: " + (selectedRelease?.Label ?? "없음") + "\n" + release.Detail, selectedRelease?.Channel ?? "", chosen?.Manifest.Version ?? "", CachedOffer: new(module.Version, release.Channel)));
                        }
                    }
                }
            }
            catch (Exception ex) { WriteLog(owner.Status.Tool.Name + " 버튼 목록: " + ex.Message); }
            if (cards.Count == 0 && owner.Status.Tool.Id == Catalog.Cad.Id && owners.Any(o => CadFeaturePackages.IsFeature(o.Package?.Package))) continue;
            if (cards.Count == 0 && owner.Status.Tool.Kind == ToolKind.NativeRevit && owner.Package is { } registered)
            {
                cards.Add(RegisteredNativeCard(owner));
            }
            if (cards.Count == 0) cards.Add(new("discover:" + owner.Status.Tool.Id, owner.Status.Tool.Name, owner.Status.Tool.Description, "버튼 정보 확인 필요", null, owner, ButtonKind.Discover, false, false, "기능 목록을 불러오십시오.", null, "", Channel: owner.Package?.Package.Channel == "test" ? "test" : "unknown", ReleaseLabel: owner.Package?.Package.Channel == "test" ? "TEST · 목록 확인 필요" : "미확인 · 목록 확인 필요"));
            foreach (var card in cards) yield return card with { Title = ToolNames.Display(card.Title) };
        }
    }
    private ButtonCard RegisteredNativeCard(ProductRow owner)
    {
        var registered = owner.Package ?? throw new IOException("버튼 저장소 등록이 필요합니다.");
        var command = SingleNativeButton.Registration(registered.Repository, registered.Package);
        var state = preview || command is null ? (Selected: false, Pending: false, Message: "설치 안 됨") : NativeCommands().State(command.ModuleId, command.CommandId);
        return new(command is null ? "native-package:" + owner.Status.Tool.Id : "native:" + command.ModuleId + ":" + command.CommandId,
            ToolNames.Display(owner.Status.Tool.Name), owner.Status.Tool.Description, "독립 Add-in", null, owner, ButtonKind.Native,
            state.Selected, state.Pending, state.Message, null, command?.CommandId ?? "", command?.ModuleId ?? "",
            registered.Package.Channel, registered.Package.Channel == "test" ? "TEST · 설치 시 버전 확인" : "설치 시 버전 확인");
    }
    private string CadSelectedVersion(ProductRow owner, string moduleId)
    {
        var installed = new CadReload(paths).Source(owner.Status.Path, preferInstalled: true);
        var module = installed?.Catalog.Modules.SingleOrDefault(m => m.Id == moduleId);
        return installed is not null && module is not null ? new CadReleaseVersions(paths).Selected(installed, module, owner.Status.Version) : owner.Status.Version;
    }
    private (bool Selected, bool Pending, string Message) CadButtonState(ProductRow owner, string key)
    {
        var bridge = new CadReload(paths); var desired = bridge.Desired(CadFeaturePackages.IsFeature(owner.Package?.Package) ? paths.Target(Catalog.Cad) : owner.Status.Path);
        if (desired is null) return (false, false, "설치 안 됨");
        var selected = desired.SelectedActions.Contains(key); var file = CadFiles.Child(bridge.Data, "Results/" + desired.Id + ".json");
        var result = File.Exists(file) ? CadFiles.Read<CadReceipt>(file) : null;
        if (result?.RequestId == desired.Id && result.Status == "applied") return (selected, false, selected ? "설치됨 · CAD 적용 완료" : "설치 안 됨");
        var activeFile = CadFiles.Child(bridge.Data, "active.json");
        var wasSelected = File.Exists(activeFile) && CadFiles.Read<CadRequest>(activeFile).SelectedActions.Contains(key);
        if (result?.Status == "failed" && (selected || wasSelected)) return (selected, true, "CAD Reload 실패");
        // An unselected button may be pending removal; never describe it as already removed.
        return (selected, selected || wasSelected, selected ? "설치 준비됨 · CAD Reload 필요" : wasSelected ? "제거 준비됨 · CAD Reload 필요" : "설치 안 됨");
    }
    private async Task LoadButtonCatalog(ProductRow owner)
    {
        await Run(owner.Status.Tool.Kind == ToolKind.NativeRevit ? "업데이트 확인" : "버튼 기능 목록 확인", async () =>
        {
            if (owner.Status.Tool.Kind == ToolKind.AutoCAD) cadButtonPackage = authenticated ? await sources.DownloadCad(CanUseTest ? null : "stable") : await sources.OfflineCad(Path.Combine(AppContext.BaseDirectory, CadLoaderAssets.Name));
            else if (owner.Status.Tool.Kind == ToolKind.PyRevit) revitButtonPackage = await sources.DownloadRevit();
            else if (owner.Package is not null)
            {
                var current = await RefreshNativeRegistration(owner.Package);
                var requested = CanUseTest ? current.Package : ExtensionCatalog.ReleaseChannel(current.Package, "stable");
                await SingleNativeButton.Resolve(paths, current.Repository, requested,
                    () => ExtensionSources().Download(current.Repository, current.Package, CanUseTest ? null : "stable"), refresh: true);
            }
            revitButtonCache.Clear(); await RefreshProductListAsync();
        });
    }
    private async Task<PackageRow> RefreshNativeRegistration(PackageRow previous)
    {
        var catalog = ExtensionSources();
        var repository = await catalog.Inspect("https://github.com/" + previous.Repository.Repository);
        var package = ExtensionCatalog.CurrentPackage(previous.Package, repository);
        catalog.Register(repository);
        await LoadRegisteredPackages();
        if (CanUseTest && previous.Package.Channel == "stable" && package.Channel == "test")
            throw new IOException("배포 구분이 TEST로 변경되었습니다. 새 TEST 표시를 확인한 뒤 다시 설치하십시오.");
        return new(repository, package);
    }
    private async Task ImportNativePackage()
    {
        if (busy || preview) return;
        using var picker = new OpenFileDialog { Title = "독립 Revit 모듈 ZIP 선택 (.sha256 필요)", Filter = "Revit 모듈 ZIP|*.zip", CheckFileExists = true };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        await Run("독립 Revit 기능 확인", async () => { await Task.Run(() => NativeCommands().Import(picker.FileName)); });
        SwitchPage("Revit");
    }
    private async Task ToggleButton(ButtonCard card, bool? installChoice = null, string? releaseChannel = null)
    {
        if (busy || preview) return;
        if (card.Kind == ButtonKind.Discover) { await LoadButtonCatalog(card.Owner); return; }
        if (releaseChannel is not null)
        {
            if (!IndependentRelease(card) || card.Owner.Package is null || installChoice != true) return;
            card = card with { Channel = releaseChannel };
        }
        var currentSelected = card.Kind == ButtonKind.Native ? NativeCommands().State(card.ModuleId, card.CommandId).Selected : card.Kind == ButtonKind.Cad ? CadButtonState(card.Owner, card.CommandId).Selected : RevitInventory(card.Owner).Selected.Contains(card.CommandId);
        var update = currentSelected && (releaseChannel is not null || RemoteUpdate(card));
        var legacyNative = card.Kind == ButtonKind.Native && new NativeLegacyPackages(paths).Present(card.Owner.Status.Tool);
        var enable = installChoice ?? (legacyNative || update || !currentSelected);
        var channelMessage = releaseChannel is null ? "" : "저장소의 최신 " + (releaseChannel == "test" ? "TEST" : "정식") + " 버전을 다운로드합니다. 현재 선택된 이 도구의 코드 버전이 바뀝니다.\n\n";
        if (!Confirm((card.Channel == "test" && enable ? "[TEST 도구] 검증 중인 버전입니다.\n" : "") + card.Title + (legacyNative ? "을 Reload 방식으로 전환합니까?" : enable ? update ? " 버튼을 업데이트합니까?" : " 버튼을 설치합니까?" : " 버튼을 제거합니까?") + "\n\n" + channelMessage + card.Description + "\n\n" + (legacyNative ? "최초 전환은 Revit 종료 후 가능합니다. 확인된 구형 배포본과 파일별로 일치할 때만 설치 기록을 연결하고, 기존 파일을 백업한 뒤 전환합니다." : card.Kind == ButtonKind.Cad ? "변경을 준비한 뒤 AutoCAD의 RoBIM → 관리 → Reload로 적용합니다." : card.Kind == ButtonKind.Native ? "변경을 준비한 뒤 실행 중인 Revit에 자동 적용을 요청합니다." : "Revit 실행 중에는 버튼 선택을 준비한 뒤 자동 적용을 요청합니다. 기존 원본과 공용 파일은 보존합니다."))) return;
        await Run("버튼 " + (enable ? update ? "업데이트" : "설치" : "제거") + " 준비", async () =>
        {
            if (card.Kind == ButtonKind.Cad && card.Owner.Package is { } feature && CadFeaturePackages.IsFeature(feature.Package))
            {
                SourcePackage? input = null;
                var selectedChannel = releaseChannel ?? (currentSelected ? NativeReleaseState(card).Update?.Channel ?? card.SelectedChannel : card.Channel);
                if (enable)
                {
                    RequireChannelAccess(selectedChannel);
                    input = await ExtensionSources().Download(feature.Repository, feature.Package, selectedChannel);
                }
                var sessions = CadReload.Sessions();
                if (sessions.Count > 1) throw new IOException("AutoCAD를 한 개만 실행하십시오.");
                var package = feature.Package with { Channel = selectedChannel };
                new CadFeaturePackages(paths).Set(package, input, enable, sessions.SingleOrDefault());
                WriteLog("독립 CAD 버튼 준비 완료 · AutoCAD에서 Reload하십시오.");
            }
            else if (card.Kind == ButtonKind.Cad)
            {
                if (enable) RequireChannelAccess(card.Channel);
                if (enable && update) cadButtonPackage = await sources.DownloadCad(CanUseTest ? null : "stable");
                var bridgeSource = new CadReload(paths);
                var source = (enable && update ? bridgeSource.Source(card.Owner.Status.Path, cadButtonPackage) : bridgeSource.SourceForAction(card.Owner.Status.Path, card.CommandId, cadButtonPackage)) ?? throw new IOException("CAD 기능 목록을 먼저 확인하십시오.");
                if (enable)
                {
                    var module = source.Catalog.Modules.Single(m => m.Actions.Any(a => RoBIM.Cad.Configuration.CadFiles.Key(m, a) == card.CommandId));
                    RequireChannelAccess(ModuleReleases.Cad(CadReload.WindowsDirectory(source.Bundle), module.Id, module.Version).Channel);
                }
                var status = installer.Status(card.Owner.Status.Tool);
                var needsConnection = source.NeedsConnection(status);
                if (needsConnection)
                {
                    if (source.Previous is not null) throw new IOException("CAD 고정 로더가 변경되었습니다. AutoCAD 종료 후 연결 모듈을 업데이트하고 버튼 구성을 다시 준비하십시오.");
                    RequirePackageAccess(card.Owner.Status.Tool, new(source.Bundle, cadButtonPackage?.Version ?? "CAD 연결 모듈"));
                    EnvironmentProbe.EnsureHostClosed(card.Owner.Status.Tool);
                    await Task.Run(() => installer.Install(card.Owner.Status.Tool, new(source.Bundle, cadButtonPackage?.Version ?? "CAD 연결 모듈")));
                }
                var bridge = new CadReload(paths);
                var sessions = CadReload.Sessions(); if (sessions.Count > 1) throw new IOException("AutoCAD를 한 개만 실행하거나 버튼 구성 화면에서 대상 세션을 선택하십시오.");
                await Task.Run(() => bridge.Toggle(card.Owner.Status.Tool, source, card.CommandId, enable, sessions.SingleOrDefault()));
                if (enable && update && cadButtonPackage is not null) new CadReleaseVersions(paths).Record(source, cadButtonPackage.Version);
                WriteLog("CAD 버튼 변경 준비 완료 — AutoCAD에서 Reload하십시오.");
            }
            else if (card.Kind == ButtonKind.PyRevit)
            {
                if (enable) RequireChannelAccess("test");
                var inventory = RevitInventory(card.Owner);
                var sourceVersion = new RevitReload(paths).PendingSelection()?.Version ?? installer.Status(Catalog.RevitSelected).Version;
                if (string.IsNullOrWhiteSpace(sourceVersion)) sourceVersion = card.Owner.Status.Version;
                var root = inventory.Root ?? throw new IOException("Revit 버튼 원본을 확인하십시오.");
                if (enable && update)
                {
                    var remote = updateOffers.GetValueOrDefault(card.Owner.Status.Tool.Id);
                    revitButtonPackage = await sources.DownloadRevit(remote?.Branch);
                    root = revitButtonPackage.Path; sourceVersion = revitButtonPackage.Version;
                }
                if (ReloadBridge.RevitRunning())
                {
                    _ = await EnvironmentProbe.RevitRoots();
                    if (!Directory.Exists(paths.Target(Catalog.ReloadBridge)))
                        ReloadBridge.Install(paths, Path.Combine(AppContext.BaseDirectory, "RoBIM-ReloadBridge.zip"));
                    new RevitReload(paths).PrepareSelection(new(root, sourceVersion), card.CommandId, enable, inventory.Selected);
                    WriteLog("버튼 선택 준비 완료 — Revit 안의 적용 및 Reload로 pyRevit 리본에 반영하십시오.");
                    WriteLog(RequestAutomaticRevitApply(native: false, pyRevit: true));
                }
                else
                {
                    await new RevitButtonInstaller(paths).Set(new(root, sourceVersion), card.CommandId, enable, inventory.Selected, () => new RevitProfiles(paths).ApplySelection());
                    WriteLog("Revit 버튼 선택 설치 완료 — 다음 Revit 시작 시 반영합니다.");
                }
                revitButtonCache.Clear();
            }
            else
            {
                var manager = NativeCommands();
                SourcePackage source;
                var commandId = card.CommandId;
                if (enable && card.Owner.Package is { } registered)
                {
                    var current = await RefreshNativeRegistration(registered);
                    var target = releaseChannel is null && currentSelected ? NativeReleaseState(card).Update : null;
                    var requestedChannel = releaseChannel ?? target?.Channel ?? (CanUseTest ? (currentSelected && card.SelectedChannel is "test" or "stable" ? card.SelectedChannel : current.Package.Channel) : "stable");
                    RequireChannelAccess(requestedChannel);
                    var requestedPackage = requestedChannel is "test" or "stable" ? ExtensionCatalog.ReleaseChannel(current.Package, requestedChannel) : current.Package;
                    var cachedUpdate = target is { AssetId: 0 } && card.CachedOffer == target;
                    source = await SingleNativeButton.Resolve(paths, current.Repository, requestedPackage,
                        () => ExtensionSources().Download(current.Repository, current.Package, requestedChannel), refresh: !cachedUpdate);
                    if (target is not null)
                    {
                        var resolved = NativeProtocol.Module(source.Path);
                        var resolvedRelease = ModuleReleases.Native(source.Path, resolved.Id, resolved.Version);
                        if (!ReleaseUpdates.IsNewer(card.SelectedVersion, card.SelectedChannel, new(resolved.Version, resolvedRelease.Channel)))
                            throw new IOException("새 버전을 다시 확인하십시오. 현재 선택보다 같거나 낮은 버전으로 업데이트하지 않습니다.");
                    }
                    var downloaded = NativeProtocol.Module(source.Path);
                    WriteLog(card.Title + " 다운로드 검증 완료 · " + ModuleReleases.Native(source.Path, downloaded.Id, downloaded.Version).Label);
                    commandId = NativeProtocol.Module(source.Path).Commands.Single().Id;
                }
                else
                {
                    if (card.Source is null) throw new IOException("기존 버튼의 설치 원본을 확인할 수 없습니다.");
                    var input = new SourcePackage(card.Source, NativeProtocol.Module(card.Source).Version);
                    if (card.Owner.Package is { } current)
                        SingleNativeButton.Validate(current.Repository, current.Package, input, requireChannel: false);
                    source = enable ? manager.Cache(input) : input;
                }
                EnsureHostRuntime(cad: false);
                RequireInstalledRevitLoader();
                if (enable)
                {
                    RequirePackageAccess(card.Owner.Status.Tool, source);
                    var module = NativeProtocol.Module(source.Path); var command = module.Commands.Single(c => c.Id == commandId);
                    var installed = new RibbonTool("native:" + module.Id + ":" + command.Id, ToolNames.Display(command.Title), command.Tab, command.Panel, true);
                    var existing = CommandInventory().Where(c => c.Selected && c.Kind is ButtonKind.Native or ButtonKind.PyRevit).Select(c =>
                    {
                        return RibbonLayouts.FromGroup(c.Id, c.Title, c.Group, true);
                    });
                    new RibbonLayouts(paths).EnsureInstalled(installed, existing,
                        () => manager.ToggleWithMigration(card.Owner.Status.Tool, source.Path, commandId, true));
                }
                else manager.ToggleWithMigration(card.Owner.Status.Tool, source.Path, commandId, false);
                WriteLog(RequestAutomaticRevitApply(native: true, pyRevit: false));
            }
            await RefreshStatuses();
        });
    }
    private ContextMenuStrip BuildButtonMenu(ButtonCard card)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("기능 설명 / 설치 위치", null, (_, _) => MessageBox.Show(this, card.Description + "\n\n분류: " + card.Group + "\n상태: " + card.Message + "\n배포 구분: " + card.ReleaseLabel + "\n" + card.ReleaseDetail + "\n원본: " + (card.Source ?? "목록 확인 필요"), card.Title));
        var legacy = !preview && appearanceSnapshot.GetValueOrDefault(card.Id)?.Legacy == true;
        menu.Items.Add(legacy ? "기존 설치를 Reload 방식으로 전환" : SelectedInView(card) ? "이 버튼 제거" : card.TestView ? "TEST 버전 설치" : "이 버튼 설치", null,
            async (_, _) => { if (card.TestView && !SelectedInView(card)) await ToggleButton(card, true, "test"); else await ToggleButton(card, legacy || !card.Selected); });
        if (card.Kind == ButtonKind.Native && card.Selected) menu.Items.Add("기능 업데이트 준비", null, async (_, _) => await ToggleButton(card, true));
        if (card.Kind == ButtonKind.Native && card.Owner.Package is not null) menu.Items.Add("업데이트 확인", null, async (_, _) => await LoadButtonCatalog(card.Owner));
        else if (card.Kind != ButtonKind.Native) menu.Items.Add("기능 목록 업데이트", null, async (_, _) => await LoadButtonCatalog(card.Owner));
        if (card.Kind == ButtonKind.Native && card.Owner.Package is not null)
        {
            if (CanUseTest) menu.Items.Add("저장소 TEST 버전 설치…", null, async (_, _) => await ToggleButton(card, true, "test"));
            menu.Items.Add("저장소 정식 버전 설치…", null, async (_, _) => await ToggleButton(card, true, "stable"));
        }
        if (card.Kind == ButtonKind.Native) menu.Items.Add("새 버전 ZIP 가져오기", null, async (_, _) => await ImportNativePackage());
        if (card.Kind == ButtonKind.Cad) menu.Items.Add("CAD 버튼 이름·패널·순서 구성", null, async (_, _) => { if (busy || preview) return; SelectProduct(card.Owner.Status.Tool.Id); await ConfigureCadButtons(false); await RefreshProductListAsync(); });
        menu.Items.Add(new ToolStripSeparator());
        if (card.Kind != ButtonKind.Native && (card.Owner.Package is not null || !card.Owner.Status.Tool.CustomPackage))
        {
            var packageItem = new ToolStripMenuItem("공용 패키지 관리") { DropDown = BuildCardMenu(card.Owner) };
            packageItem.DropDownOpening += (_, _) => SelectProduct(card.Owner.Status.Tool.Id);
            menu.Items.Add(packageItem);
        }
        return menu;
    }

    private IEnumerable<ButtonCard> PreviewCommands()
    {
        if (productRows.Count == 0) yield break;
        var cad = productRows.First(x => x.Status.Tool.Id == Catalog.Cad.Id);
        var native = productRows.First(x => x.Status.Tool.Kind == ToolKind.NativeRevit);
        yield return new("native:sheets:create-sheets", "목록표로 시트 생성", "DWG·PDF·스캔 PDF에서 도면번호와 도면명을 읽고, 검토한 목록으로 시트를 일괄 생성합니다.", "RoBIM 도면 / 시트", null, native, ButtonKind.Native, false, false, "설치 안 됨", null, "create-sheets", "sheets", "stable", "실사용 · 0.2.0 · 예시 버전");
        yield return new("native:sample-test:sample", "신규 기능 TEST", "독립 Add-in 검증용 예시입니다.", "RoBIM TEST / 독립 도구", null, native, ButtonKind.Native, false, false, "설치 안 됨", null, "sample", "sample-test", "test", "TEST · 0.1.0-test.2", "화면 확인용 예시");
        yield return new("native:auto-join:run", "자동 결합", "선택 요소의 결합과 절단 순서를 검토합니다.", "RoBIM / Element", null, native, ButtonKind.Native, true, false, "설치됨", null, "run", "auto-join", "stable", "정식 · 예시 버전", SelectedChannel: "stable", SelectedVersion: "0.1.0");
        yield return new("native:parameter-copy:copy", "파라미터 복사", "선택 요소 사이에서 파라미터 값을 복사합니다.", "RoBIM / Parameter", null, native, ButtonKind.Native, true, false, "설치됨", null, "copy", "parameter-copy", "stable", "정식 · 예시 버전", SelectedChannel: "stable", SelectedVersion: "0.1.0");
        yield return new("native:view-duplicate:run", "뷰 복제", "선택한 Revit 뷰를 복제합니다.", "RoBIM View/Sheet / View", null, native, ButtonKind.Native, false, false, "설치 안 됨", null, "run", "view-duplicate", "stable", "정식 · 예시 버전");
        yield return new("cad:sample:run", "CAD TEST 예시", "CAD 독립 기능 TEST 예시", "AutoCAD", null, cad, ButtonKind.Cad, false, false, "설치 안 됨", null, "sample:run", "sample", "test", "TEST · 1.1.0-test.1");
        yield return new("cad:delhidden:cleanup", "숨김 레이어 정리", "꺼짐·전체 동결 레이어 후보를 확인하고 선택 삭제합니다.", "정리 / 레이어 정리", null, cad, ButtonKind.Cad, true, false, "설치됨 · CAD 적용 완료", null, "delhidden:cleanup");
        yield return new("cad:ezsplit:split", "도면 분할", "도곽별 도면번호·도면명을 검토하고 DWG로 분할 저장합니다.", "도면 / 도면 분할", null, cad, ButtonKind.Cad, false, false, "설치 안 됨", null, "ezsplit:split");
    }
}
