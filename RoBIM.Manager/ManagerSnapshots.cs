using System.Diagnostics;
using System.Text.Json;
using RoBIM.Manager.Core;

namespace RoBIM.Manager;

public sealed partial class ManagerForm
{
    private ButtonCard[] commandSnapshot = [];
    private Dictionary<string, CardAppearance> appearanceSnapshot = [];
    private bool refreshingSnapshot;
    private bool snapshotVerified;
    private string stateStamp = "";
    private int inventoryScanCount;
    private double lastLocalRefreshMs;
    private double firstDisplayMs;
    private bool usedDisplayCache;
    private int repositoryLookupCount;
    private sealed record CardAppearance(bool Enabled, bool Update, bool Legacy, string Message, string Instruction);
    private sealed record DisplaySnapshot(int SchemaVersion, ProductRow[] Products, ButtonCard[] Commands,
        Dictionary<string, CardAppearance> Appearance, PackageRow[] Available, DateTimeOffset CapturedAt);
    private string DisplayCachePath => SafeFiles.Child(paths.Data, "manager-display-cache.json");

    private async Task StartLocalDisplay()
    {
        var firstDisplay = Stopwatch.StartNew();
        try
        {
            refreshingSnapshot = true;
            var cached = await Task.Run(() =>
            {
                try
                {
                    var file = DisplayCachePath;
                    if (!File.Exists(file) || new FileInfo(file).Length > 4 * 1024 * 1024) return null;
                    var value = JsonSerializer.Deserialize<DisplaySnapshot>(File.ReadAllText(file));
                    return value is { SchemaVersion: 2, Commands.Length: <= 2000, Products.Length: <= 500, Available.Length: <= 500 }
                        && value.Appearance is not null ? value : null;
                }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
            });
            if (IsDisposed) return;
            if (cached is not null)
            {
                try
                {
                    productRows.AddRange(cached.Products); commandSnapshot = cached.Commands;
                    appearanceSnapshot = cached.Appearance; availablePackages.AddRange(cached.Available);
                    RenderProductCards();
                    firstDisplayMs = firstDisplay.Elapsed.TotalMilliseconds; usedDisplayCache = true;
                }
                catch (Exception ex)
                {
                    productRows.Clear(); commandSnapshot = []; appearanceSnapshot = []; availablePackages.Clear(); cached = null;
                    WriteLog("화면 캐시를 다시 구성합니다: " + ex.Message);
                }
            }
            footer.Text = cached is null ? "로컬 설치 목록을 확인하고 있습니다…" : "저장된 목록 표시 · 설치 상태를 백그라운드에서 확인하고 있습니다…";
            var registered = await Task.Run(() => new ExtensionCatalog(paths).Registered()
                .SelectMany(repository => repository.Packages.Select(package => new PackageRow(repository, package))).ToArray());
            foreach (var entry in registered)
            {
                var id = ExtensionCatalog.Tool(entry.Repository, entry.Package).Id;
                availablePackages.RemoveAll(x => ExtensionCatalog.Tool(x.Repository, x.Package).Id == id);
                availablePackages.Add(entry);
            }
            await RefreshStatuses();
            if (!usedDisplayCache) firstDisplayMs = firstDisplay.Elapsed.TotalMilliseconds;
            if (!IsDisposed) { await RefreshProfileLabel(); footer.Text = "로컬 설치 상태 확인 완료 · 저장소 조회는 도구 등록에서 실행할 수 있습니다."; }
        }
        catch (Exception ex) { if (!IsDisposed) { WriteLog("로컬 목록 확인 실패: " + ex.Message); footer.Text = "설치 상태 확인 실패 · 새로 고침으로 다시 확인하세요."; } }
        finally { refreshingSnapshot = false; if (!IsDisposed) RefreshCardActions(); }
    }

    internal void InspectPerformance(string destination)
    {
        ShowInTaskbar = false; Opacity = 0; StartPosition = FormStartPosition.Manual; Location = new(-20000, -20000);
        Shown += async (_, _) =>
        {
            var ticks = 0;
            using var heartbeat = new System.Windows.Forms.Timer { Interval = 50 };
            heartbeat.Tick += (_, _) => ticks++; heartbeat.Start();
            try
            {
                var startup = Stopwatch.StartNew(); await StartLocalDisplay();
                if (!snapshotVerified) throw new IOException("Local snapshot verification failed");
                var startupMs = startup.Elapsed.TotalMilliseconds;
                var before = inventoryScanCount; var timings = new List<object>();
                foreach (var page in new[] { "Revit", "PyRevit", "Cad", "All", "Mine", "PyRevit", "Revit" })
                {
                    var watch = Stopwatch.StartNew(); SwitchPage(page); PerformLayout();
                    timings.Add(new { Page = page, Milliseconds = watch.Elapsed.TotalMilliseconds, Cards = cardBindings.Count });
                }
                for (var i = 0; i < 3; i++) await PollLocalChanges();
                var cached = JsonSerializer.Deserialize<DisplaySnapshot>(File.ReadAllText(DisplayCachePath))!;
                if (inventoryScanCount != before || repositoryLookupCount != 0 || cached.Commands.Length != commandSnapshot.Length)
                    throw new IOException("Navigation/polling performed unexpected discovery or cache roundtrip failed");
                File.WriteAllText(destination, JsonSerializer.Serialize(new { Passed = true, StartupMs = startupMs, FirstDisplayMs = firstDisplayMs,
                    UsedDisplayCache = usedDisplayCache, InventoryRefreshMs = lastLocalRefreshMs, UiHeartbeatTicks = ticks,
                    Commands = commandSnapshot.Length, Navigation = timings, WarmInventoryScans = inventoryScanCount - before,
                    RepositoryLookups = repositoryLookupCount, CacheRoundtripVerified = true }, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) { File.WriteAllText(destination, JsonSerializer.Serialize(new { Passed = false, Error = ex.ToString() })); }
            finally { Close(); }
        };
    }

    private async Task RefreshProductListAsync()
    {
        var statusRows = statuses.Values.ToArray(); var packages = availablePackages.ToArray();
        var selected = selectedProductId;
        var stampBefore = ReadStateStamp();
        var watch = Stopwatch.StartNew();
        var value = await Task.Run(() =>
        {
            var owners = BuildProductRows(statusRows, packages);
            var commands = ReadCommandInventory(owners).ToArray();
            var appearance = BuildAppearances(commands);
            return new DisplaySnapshot(2, owners, commands, appearance, packages, DateTimeOffset.UtcNow);
        });
        if (IsDisposed) return;
        productRows.Clear(); productRows.AddRange(value.Products); selectedProductId = selected;
        commandSnapshot = value.Commands; appearanceSnapshot = value.Appearance; snapshotVerified = true;
        ShowCachedExtensionRows();
        RenderProductCards(); if (currentPage == "Ribbon") RefreshRibbonEditor();
        lastLocalRefreshMs = watch.Elapsed.TotalMilliseconds;
        stateStamp = stampBefore;
        await Task.Run(() =>
        {
            try
            {
                var file = DisplayCachePath; Directory.CreateDirectory(paths.Data);
                var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { File.WriteAllText(temporary, JsonSerializer.Serialize(value)); File.Move(temporary, file, true); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { WriteLog("화면 캐시 저장 생략: " + ex.Message); }
        });
    }

    private async Task PollLocalChanges()
    {
        if (busy || refreshingSnapshot || !snapshotVerified) return;
        refreshingSnapshot = true;
        try
        {
            var stamp = await Task.Run(ReadStateStamp);
            if (IsDisposed || stamp == stateStamp) return;
            RefreshCardActions();
            await RefreshStatuses();
        }
        catch (Exception ex) { if (!IsDisposed) WriteLog("로컬 상태 갱신 보류: " + ex.Message); }
        finally { refreshingSnapshot = false; if (!IsDisposed) RefreshCardActions(); }
    }

    private string ReadStateStamp()
    {
        // Metadata only: no package traversal, hashing, Git or network in the timer.
        var files = new[] { "installed.json", "pyrevit-location.json", "reload-pending.json", "reload-result.json", "reload-profile.json",
            "NativeRevit/pending.json", "NativeRevit/active.json", "NativeRevit/Results", "Cad/pending.json", "Cad/active.json", "Cad/Results" };
        var stamp = string.Join("|", files.Select(relative =>
        {
            var file = SafeFiles.Child(paths.Data, relative);
            if (Directory.Exists(file)) return Directory.GetLastWriteTimeUtc(file).Ticks.ToString();
            var info = new FileInfo(file); return info.Exists ? info.LastWriteTimeUtc.Ticks + ":" + info.Length : "-";
        }));
        foreach (var name in new[] { "Revit", "acad", "accoreconsole" })
        {
            var processes = Process.GetProcessesByName(name);
            try { stamp += "|" + name + ":" + string.Join(",", processes.Select(p => p.Id).Order()); }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        return stamp;
    }

    private Dictionary<string, CardAppearance> BuildAppearances(ButtonCard[] commands)
    {
        var result = new Dictionary<string, CardAppearance>();
        bool? selectedModified = null;
        foreach (var feature in commands)
        {
            var enabled = true; var update = false; var legacy = false; var message = feature.Message;
            var instruction = feature.Kind == ButtonKind.Native ? "독립 Add-in Reload로 추가·변경·제거 적용" : feature.Kind == ButtonKind.Cad ? "AutoCAD → RoBIM → 관리 → Reload" : "pyRevit Reload로 선택 변경 적용";
            try
            {
                if (feature.Kind == ButtonKind.Native)
                {
                    legacy = new NativeLegacyPackages(paths).Present(feature.Owner.Status.Tool);
                    if (legacy) { message = "기존 직접 설치 · 전환 필요"; instruction = "최초 한 번 Revit 종료 후 Reload 방식으로 전환"; }
                    else if (feature.Selected && new NativeRevitCommands(paths).HasUpdate(feature.ModuleId)) { update = true; message = "새 기능 패키지 · 업데이트 가능"; }
                }
                else if (feature.Kind == ButtonKind.Cad)
                {
                    enabled = !feature.Owner.Status.Modified && !feature.Owner.Status.External;
                    if (!feature.Selected && new CadReload(paths).SourceForAction(feature.Owner.Status.Path, feature.CommandId, cadButtonPackage) is { } source && source.NeedsConnection(feature.Owner.Status))
                    { instruction = "연결 모듈 업데이트 필요 · AutoCAD 종료 후 설치"; enabled &= !HostRunning(ToolKind.AutoCAD); }
                }
                else if (feature.Kind == ButtonKind.PyRevit)
                {
                    selectedModified ??= statuses.GetValueOrDefault(Catalog.RevitSelected.Id)?.Modified ?? installer.Status(Catalog.RevitSelected).Modified;
                    enabled = !selectedModified.Value;
                }
            }
            catch (Exception ex) { enabled = false; message = "설치 상태 확인 필요"; instruction = ex.Message; }
            result[feature.Id] = new(enabled, update, legacy, message, instruction);
        }
        return result;
    }
}
