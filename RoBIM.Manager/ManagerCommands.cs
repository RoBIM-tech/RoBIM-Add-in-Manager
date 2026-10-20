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
        ButtonKind Kind, bool Selected, bool Pending, string Message, string? Source, string CommandId, string ModuleId = "");
    private readonly Dictionary<string, RevitButton[]> revitButtonCache = new(StringComparer.OrdinalIgnoreCase);
    private SourcePackage? cadButtonPackage;
    private SourcePackage? revitButtonPackage;
    private readonly Button nativeReload = MakeButton("Revit Reload");

    private CadButtonSource? CadButtons(ProductRow owner, bool preferInstalled = false)
    {
        return new CadReload(paths).Source(owner.Status.Path, cadButtonPackage, preferInstalled);
    }
    private (string? Root, RevitButton[] Buttons, string[] Selected, bool Pending) RevitInventory(ProductRow owner)
    {
        var inventory = RevitButtonInventory.Read(paths, owner.Status, revitButtonPackage);
        return (inventory.Root, inventory.Buttons, inventory.Selected, inventory.Pending);
    }
    private IEnumerable<ButtonCard> CommandInventory() => preview ? PreviewCommands() : commandSnapshot;
    private IEnumerable<ButtonCard> ReadCommandInventory(IEnumerable<ProductRow> owners)
    {
        if (preview)
        {
            foreach (var card in PreviewCommands()) yield return card;
            yield break;
        }
        inventoryScanCount++;
        foreach (var owner in owners.Where(x => x.Status.Tool.Kind != ToolKind.NativeRevitHost))
        {
            var cards = new List<ButtonCard>();
            try
            {
                if (owner.Status.Tool.Kind == ToolKind.AutoCAD)
                {
                    var source = CadButtons(owner);
                    if (source is not null)
                        foreach (var module in source.Catalog.Modules)
                            foreach (var command in module.Actions.OrderBy(x => x.Order))
                            {
                                var key = CadFiles.Key(module, command); var state = CadButtonState(owner, key);
                                cards.Add(new("cad:" + key, command.Title, command.Description, command.Panel + " / " + module.Name, command.Icon is null ? null : CadFiles.Child(source.Root, command.Icon), owner, ButtonKind.Cad, state.Selected, state.Pending, state.Message, source.Bundle, key));
                            }
                }
                else if (owner.Status.Tool.Kind == ToolKind.PyRevit)
                {
                    var inventory = RevitInventory(owner);
                    var pending = inventory.Pending;
                    var iconCache = inventory.Root is null ? null : RevitButtons.ReadSelection(inventory.Root)?.Cache?.Root;
                    string? Icon(string? relative)
                    {
                        if (relative is null) return null;
                        var active = SafeFiles.Child(inventory.Root!, relative);
                        return File.Exists(active) ? active : iconCache is not null ? SafeFiles.Child(iconCache, relative) : RevitButtons.ResourcePath(inventory.Root!, relative);
                    }
                    foreach (var command in inventory.Buttons) cards.Add(new(owner.Status.Tool.Id + ":" + command.Id, command.Title, command.Description, command.Group, Icon(command.Icon), owner, ButtonKind.PyRevit, inventory.Selected.Contains(command.Id), pending, inventory.Selected.Contains(command.Id) ? (pending ? "설치 준비 · pyRevit Reload 대기" : "설치됨") : (pending ? "제거 준비 · pyRevit Reload 대기" : "설치 안 됨"), inventory.Root, command.Id));
                }
                else
                {
                    var manager = new NativeRevitCommands(paths);
                    var source = manager.CachedForFolder(owner.Status.Tool.FolderName);
                    if (source is null && File.Exists(Path.Combine(owner.Status.Path, NativeProtocol.ManifestName))) source = new(owner.Status.Path, owner.Status.Version);
                    if (source is not null)
                    {
                        var module = NativeProtocol.Module(source.Path);
                        foreach (var command in module.Commands)
                        {
                            var state = manager.State(module.Id, command.Id);
                            if (new NativeLegacyPackages(paths).Present(owner.Status.Tool)) state = (true, false, "기존 직접 설치 · Reload 방식 전환 필요");
                            cards.Add(new("native:" + module.Id + ":" + command.Id, command.Title, command.Description, command.Tab + " / " + command.Panel, command.Icon is null ? null : NativeProtocol.Child(source.Path, command.Icon), owner, ButtonKind.Native, state.Selected, state.Pending, state.Message, source.Path, command.Id, module.Id));
                        }
                    }
                }
            }
            catch (Exception ex) { WriteLog(owner.Status.Tool.Name + " 버튼 목록: " + ex.Message); }
            if (cards.Count == 0) cards.Add(new("discover:" + owner.Status.Tool.Id, owner.Status.Tool.Name, owner.Status.Tool.Description, "버튼 정보 확인 필요", null, owner, ButtonKind.Discover, false, false, "기능 목록을 불러오십시오.", null, ""));
            foreach (var card in cards) yield return card;
        }
    }
    private (bool Selected, bool Pending, string Message) CadButtonState(ProductRow owner, string key)
    {
        var bridge = new CadReload(paths); var desired = bridge.Desired(owner.Status.Path);
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
        await Run("버튼 기능 목록 확인", async () =>
        {
            if (owner.Status.Tool.Kind == ToolKind.AutoCAD) cadButtonPackage = authenticated ? await sources.DownloadCad() : await sources.OfflineCad(Path.Combine(AppContext.BaseDirectory, "DelHiddenNet-AutoCAD2026.zip"));
            else if (owner.Status.Tool.Kind == ToolKind.PyRevit) revitButtonPackage = await sources.DownloadRevit();
            else if (owner.Package is not null)
            {
                var source = await new ExtensionCatalog(paths).Download(owner.Package.Repository, owner.Package.Package);
                if (!File.Exists(Path.Combine(source.Path, NativeProtocol.ManifestName))) throw new IOException("배포된 패키지에는 버튼별 Reload 정보가 없습니다. 새 robim-module.json 포함 패키지를 가져오십시오.");
                new NativeRevitCommands(paths).Cache(source);
            }
            revitButtonCache.Clear(); await RefreshProductListAsync();
        });
    }
    private async Task ImportNativePackage()
    {
        if (busy || preview) return;
        using var picker = new OpenFileDialog { Title = "독립 Revit 모듈 ZIP 선택 (.sha256 필요)", Filter = "Revit 모듈 ZIP|*.zip", CheckFileExists = true };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        await Run("독립 Revit 기능 확인", async () => { await Task.Run(() => new NativeRevitCommands(paths).Import(picker.FileName)); });
        SwitchPage("Revit");
    }
    private async Task ToggleButton(ButtonCard card, bool? installChoice = null)
    {
        if (busy || preview) return;
        if (card.Kind == ButtonKind.Discover) { await LoadButtonCatalog(card.Owner); return; }
        var currentSelected = card.Kind == ButtonKind.Native ? new NativeRevitCommands(paths).State(card.ModuleId, card.CommandId).Selected : card.Kind == ButtonKind.Cad ? CadButtonState(card.Owner, card.CommandId).Selected : RevitInventory(card.Owner).Selected.Contains(card.CommandId);
        var update = card.Kind == ButtonKind.Native && currentSelected && new NativeRevitCommands(paths).HasUpdate(card.ModuleId);
        var legacyNative = card.Kind == ButtonKind.Native && new NativeLegacyPackages(paths).Present(card.Owner.Status.Tool);
        var enable = installChoice ?? (legacyNative || update || !currentSelected);
        if (!Confirm(card.Title + (legacyNative ? "을 Reload 방식으로 전환합니까?" : enable ? " 버튼을 설치합니까?" : " 버튼을 제거합니까?") + "\n\n" + card.Description + "\n\n" + (legacyNative ? "최초 전환은 Revit 종료 후 가능합니다. 확인된 구형 배포본과 파일별로 일치할 때만 설치 기록을 연결하고, 기존 파일을 백업한 뒤 전환합니다." : card.Kind == ButtonKind.Cad ? "변경을 준비한 뒤 AutoCAD의 RoBIM → 관리 → Reload로 적용합니다." : card.Kind == ButtonKind.Native ? "변경을 준비한 뒤 Revit Reload로 적용합니다. 공용 로더는 유지합니다." : "Revit 실행 중에는 버튼 선택을 준비한 뒤 Revit 안의 적용 및 Reload로 반영합니다. 기존 원본과 공용 파일은 보존합니다."))) return;
        await Run("버튼 " + (enable ? "설치" : "제거") + " 준비", async () =>
        {
            if (card.Kind == ButtonKind.Cad)
            {
                var source = new CadReload(paths).SourceForAction(card.Owner.Status.Path, card.CommandId, cadButtonPackage) ?? throw new IOException("CAD 기능 목록을 먼저 확인하십시오.");
                var status = installer.Status(card.Owner.Status.Tool);
                var needsConnection = source.NeedsConnection(status);
                if (needsConnection)
                {
                    if (source.Previous is not null) throw new IOException("CAD 고정 로더가 변경되었습니다. AutoCAD 종료 후 연결 모듈을 업데이트하고 버튼 구성을 다시 준비하십시오.");
                    EnvironmentProbe.EnsureHostClosed(card.Owner.Status.Tool);
                    await Task.Run(() => installer.Install(card.Owner.Status.Tool, new(source.Bundle, cadButtonPackage?.Version ?? "CAD 연결 모듈")));
                }
                var bridge = new CadReload(paths);
                var sessions = CadReload.Sessions(); if (sessions.Count > 1) throw new IOException("AutoCAD를 한 개만 실행하거나 버튼 구성 화면에서 대상 세션을 선택하십시오.");
                await Task.Run(() => bridge.Toggle(card.Owner.Status.Tool, source, card.CommandId, enable, sessions.SingleOrDefault()));
                WriteLog("CAD 버튼 변경 준비 완료 — AutoCAD에서 Reload하십시오.");
            }
            else if (card.Kind == ButtonKind.PyRevit)
            {
                var inventory = RevitInventory(card.Owner); var root = inventory.Root ?? throw new IOException("Revit 버튼 원본을 확인하십시오.");
                if (ReloadBridge.RevitRunning())
                {
                    _ = await EnvironmentProbe.RevitRoots();
                    if (!Directory.Exists(paths.Target(Catalog.ReloadBridge)))
                        ReloadBridge.Install(paths, Path.Combine(AppContext.BaseDirectory, "RoBIM-ReloadBridge.zip"));
                    new RevitReload(paths).PrepareSelection(new(root, card.Owner.Status.Version), card.CommandId, enable, inventory.Selected);
                    WriteLog("버튼 선택 준비 완료 — Revit 안의 적용 및 Reload로 pyRevit 리본에 반영하십시오.");
                    ShowReloadReady();
                }
                else
                {
                    await new RevitButtonInstaller(paths).Set(new(root, card.Owner.Status.Version), card.CommandId, enable, inventory.Selected, () => new RevitProfiles(paths).ApplySelection());
                    WriteLog("Revit 버튼 선택 설치 완료 — 다음 Revit 시작 시 반영합니다.");
                }
                revitButtonCache.Clear();
            }
            else
            {
                var manager = new NativeRevitCommands(paths);
                var source = manager.Cache(new SourcePackage(card.Source!, NativeProtocol.Module(card.Source!).Version));
                var runtimeFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Autodesk", "Revit 2026", "RevitNET.runtimeconfig.json");
                if (!File.Exists(runtimeFile) || System.Text.Json.JsonDocument.Parse(File.ReadAllText(runtimeFile)).RootElement.GetProperty("runtimeOptions").GetProperty("tfm").GetString() != "net10.0") throw new IOException("이 독립 로더에는 Revit 2026.5+ / .NET 10이 필요합니다.");
                var hostZip = Path.Combine(AppContext.BaseDirectory, "RoBIM-Host-Revit2026.zip");
                manager.InstallHost(hostZip, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "Revit", "Addins", "2026"));
                manager.ToggleWithMigration(card.Owner.Status.Tool, source.Path, card.CommandId, enable);
                WriteLog("독립 Revit 버튼 변경 준비 완료 — Revit Reload로 적용하십시오.");
            }
            await RefreshStatuses();
        });
    }
    private ContextMenuStrip BuildButtonMenu(ButtonCard card)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("기능 설명 / 설치 위치", null, (_, _) => MessageBox.Show(this, card.Description + "\n\n분류: " + card.Group + "\n상태: " + card.Message + "\n원본: " + (card.Source ?? "목록 확인 필요"), card.Title));
        var legacy = !preview && appearanceSnapshot.GetValueOrDefault(card.Id)?.Legacy == true;
        menu.Items.Add(legacy ? "기존 설치를 Reload 방식으로 전환" : card.Selected ? "이 버튼 제거" : "이 버튼 설치", null, async (_, _) => await ToggleButton(card, legacy || !card.Selected));
        if (card.Kind == ButtonKind.Native && card.Selected) menu.Items.Add("기능 업데이트 준비", null, async (_, _) => await ToggleButton(card, true));
        if (card.Kind != ButtonKind.Native || card.Owner.Package is not null) menu.Items.Add("기능 목록 업데이트", null, async (_, _) => await LoadButtonCatalog(card.Owner));
        if (card.Kind == ButtonKind.Native) menu.Items.Add("새 버전 ZIP 가져오기", null, async (_, _) => await ImportNativePackage());
        if (card.Kind == ButtonKind.Cad) menu.Items.Add("CAD 버튼 이름·패널·순서 구성", null, async (_, _) => { if (busy || preview) return; SelectProduct(card.Owner.Status.Tool.Id); await ConfigureCadButtons(false); await RefreshProductListAsync(); });
        menu.Items.Add(new ToolStripSeparator());
        if (card.Owner.Package is not null || !card.Owner.Status.Tool.CustomPackage)
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
        var revit = productRows.First(x => x.Status.Tool.Id == Catalog.Revit.Id);
        var cad = productRows.First(x => x.Status.Tool.Id == Catalog.Cad.Id);
        var native = productRows.First(x => x.Status.Tool.Kind == ToolKind.NativeRevit);
        yield return new("native:sheets:create-sheets", "목록표로 시트 생성", "DWG·PDF·스캔 PDF에서 도면번호와 도면명을 읽고, 검토한 목록으로 시트를 일괄 생성합니다.", "RoBIM 도면 / 시트", null, native, ButtonKind.Native, false, false, "설치 안 됨", null, "create-sheets", "sheets");
        yield return new("revit:auto-join", "AutoJoin", "선택 요소·현재 뷰·프로젝트에서 카테고리별 조인과 절단 순서를 정리합니다.", "RoBIM 기본 / 요소 수정", null, revit, ButtonKind.PyRevit, true, false, "설치됨", null, "auto-join");
        yield return new("revit:beam-to-slab", "보 슬래브 높이 맞춤", "보와 겹치는 슬래브를 찾아 이동량을 검토하고, 선택한 보의 높이를 맞춥니다.", "RoBIM 기본 / 요소 수정", null, revit, ButtonKind.PyRevit, true, false, "설치됨", null, "beam-to-slab");
        yield return new("revit:cad-dimensions", "CAD 치수 도면화", "CAD 선과 기준을 검토하여 Revit 도면 치수를 작성합니다.", "RoBIM 도면 / 치수", null, revit, ButtonKind.PyRevit, false, false, "설치 안 됨", null, "cad-dimensions");
        yield return new("cad:delhidden:cleanup", "숨김 레이어 정리", "꺼짐·전체 동결 레이어 후보를 확인하고 선택 삭제합니다.", "정리 / 레이어 정리", null, cad, ButtonKind.Cad, true, false, "설치됨 · CAD 적용 완료", null, "delhidden:cleanup");
        yield return new("cad:ezsplit:split", "도면 분할", "도곽별 도면번호·도면명을 검토하고 DWG로 분할 저장합니다.", "도면 / 도면 분할", null, cad, ButtonKind.Cad, false, false, "설치 안 됨", null, "ezsplit:split");
    }
}
