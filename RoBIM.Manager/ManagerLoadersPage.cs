using System.Diagnostics;
using RoBIM.Cad.Configuration;
using RoBIM.Manager.Core;

namespace RoBIM.Manager;

public sealed partial class ManagerForm
{
    private readonly Panel loadersPage = new() { Dock = DockStyle.Fill };
    private readonly Label cadLoaderStatus = new() { AutoSize = true };
    private readonly Label revitLoaderStatus = new() { AutoSize = true };
    private readonly Label cadLoaderBanner = new() { AutoSize = true };

    private readonly Dictionary<string, ReleaseChannels> loaderOffers = [];
    private void BuildLoadersPage()
    {
        var body = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        void Card(string name, string repository, string path, Label status, string action, ToolSpec tool, Func<string, Task> apply)
        {
            var card = new FlowLayoutPanel { Width = 850, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(12), BackColor = Color.White, Margin = new Padding(0, 0, 0, 12) };
            card.Controls.Add(new Label { Text = name, AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
            var link = new LinkLabel { Text = repository, AutoSize = true };
            link.LinkClicked += (_, _) => { if (!preview) OpenUrl("https://github.com/" + repository); };
            card.Controls.Add(link);
            card.Controls.Add(new Label { Text = path, AutoSize = true });
            card.Controls.Add(status);
            var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            var folder = MakeButton("폴더 열기"); folder.AutoSize = true;
            folder.Click += (_, _) => { if (!preview) OpenFolder(path); };
            var channel = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
            channel.Items.Add("정식"); if (CanUseTest) channel.Items.Add("TEST"); channel.SelectedIndex = 0;
            var button = MakeButton("설치 / 업데이트", true); button.AutoSize = true;
            void Update()
            {
                var state = installer.Status(tool);
                var offers = loaderOffers.GetValueOrDefault(tool.Id);
                var offer = channel.SelectedIndex == 1 ? offers?.Test : offers?.Stable;
                var newer = offer is not null && ReleaseUpdates.IsNewer(state.Version, "", offer);
                button.Text = !state.Managed ? "설치" : offer is null ? "버전 확인 필요" : newer ? "업데이트" : "최신 버전";
                button.Enabled = offer is not null && !state.Modified && !state.External && (!state.Managed || newer);
                status.Text = (state.Managed ? "설치 " + state.Version : "미설치") +
                    (offers is null ? " · 저장소 확인 필요" : " · 정식 " + (offers.Stable?.Version ?? "없음") +
                    (CanUseTest ? " · TEST " + (offers.Test?.Version ?? "없음") : "") +
                    (offers.StableError is null && offers.TestError is null ? "" : " · 일부 확인 실패"));
            }
            button.Click += async (_, _) => { if (!preview) { await apply(channel.SelectedIndex == 1 ? "test" : "stable"); Update(); } };
            var check = MakeButton("저장소 확인"); check.AutoSize = true;
            check.Click += async (_, _) => { if (!preview) { await CheckLoaderRepository(tool, status, "stable"); Update(); } };
            channel.DropDown += (_, _) => { if (CanUseTest && channel.Items.Count == 1) channel.Items.Add("TEST"); };
            channel.SelectedIndexChanged += (_, _) => Update();
            actions.Controls.Add(check); actions.Controls.Add(channel); actions.Controls.Add(button); actions.Controls.Add(folder);
            card.Controls.Add(actions); body.Controls.Add(card);
        }
        Card("RoBIM CAD 로더", "RoBIM-tech/RoBIM-CAD-Loader", paths.Target(Catalog.Cad), cadLoaderStatus, "정식 로더 적용 / 업데이트", Catalog.Cad, ApplyCadLoader);
        Card("RoBIM Revit Host 로더", "RoBIM-tech/RoBIM-Revit-Addin", paths.Target(Catalog.RevitHost), revitLoaderStatus, "정식 로더 적용 / 업데이트", Catalog.RevitHost, ApplyRevitLoader);
        body.Controls.Add(new Label { Text = "RoBIM Navisworks 로더 · 준비 중", AutoSize = true, Padding = new Padding(12) });
        loadersPage.Controls.Add(body);
    }

    private void RefreshLoaderStatus()
    {
        string Local(ToolSpec tool)
        {
            try
            {
                var state = installer.Status(tool);
                if (state.Modified) return "로컬 변경 있음 · 보호를 위해 적용 차단";
                if (state.External) return "외부 설치 · 보호를 위해 적용 차단";
                return state.Managed ? "설치됨 · " + state.Version + " · 저장소 확인 가능" : "미설치 · 저장소 확인 가능";
            }
            catch (Exception error) { return "상태 확인 실패: " + error.Message; }
        }
        cadLoaderStatus.Text = Local(Catalog.Cad);
        revitLoaderStatus.Text = Local(Catalog.RevitHost);
        cadLoaderBanner.Text = "로더: RoBIM CAD 로더 (RoBIM-tech/RoBIM-CAD) · " + cadLoaderStatus.Text;
    }

    private async Task CheckLoaderRepository(ToolSpec tool, Label status, string channel)
    {
        await Run("로더 저장소 확인", async () =>
        {
            var offers = await sources.CheckLoader(tool, CanUseTest);
            if (offers.StableError is not null || offers.TestError is not null)
            {
                var previous = loaderOffers.GetValueOrDefault(tool.Id);
                offers = offers with { Stable = offers.StableError is null ? offers.Stable : previous?.Stable,
                    Test = offers.TestError is null ? offers.Test : previous?.Test };
            }
            loaderOffers[tool.Id] = offers;
            status.Text = "저장소 확인 완료 · " + DateTime.Now.ToString("HH:mm");
        });
    }

    private async Task ApplyCadLoader(string channel)
    {
        await Run("AutoCAD 로더 적용", async () =>
        {
            EnvironmentProbe.EnsureHostClosed(Catalog.Cad);
            var status = installer.Status(Catalog.Cad);
            if (status.Modified || status.External) throw new IOException("로컬 변경 또는 외부 설치를 보존하기 위해 중단합니다.");
            var previous = status.Managed ? new CadReload(paths).Desired(status.Path) : null;
            var oldSource = previous is null ? null : new CadReload(paths).Source(status.Path, preferInstalled: true);
            var archive = await sources.DownloadLoader(Catalog.Cad, channel);
            var package = await sources.OfflineCad(archive);
            RequirePackageAccess(Catalog.Cad, package);
            var newRoot = CadReload.WindowsDirectory(package.Path);
            var version = FileVersionInfo.GetVersionInfo(Path.Combine(newRoot, "RoBIM.Cad.Loader.dll")).ProductVersion?.Split('+')[0];
            if (string.IsNullOrWhiteSpace(version)) throw new IOException("동봉 CAD 로더 버전을 확인할 수 없습니다.");
            if (version.Contains('-')) RequireChannelAccess("test");
            if (!string.IsNullOrWhiteSpace(version)) package = package with { Version = version };

            await Task.Run(() => installer.Install(Catalog.Cad, package));
            if (oldSource is not null && previous is not null)
            {
                try { new CadFeaturePackages(paths).Rebase(oldSource, previous.SelectedActions); }
                catch (Exception error) { WriteLog("로더는 설치됐으나 이전 버튼 선택 재준비 실패: " + error.Message); MessageBox.Show(this, "로더는 설치됐지만 기존 버튼 선택을 다시 준비하지 못했습니다. 기존 준비 기록은 보존됩니다.\n" + error.Message, "AutoCAD 로더"); }
            }
            cadEditorLoaded = false;
            await RefreshStatuses(); RefreshLoaderStatus();
            WriteLog("AutoCAD 로더 설치 완료 · AutoCAD를 시작한 뒤 적용 상태를 확인하세요.");
        });
    }

    private async Task ApplyRevitLoader(string channel)
    {
        await Run("Revit 로더 적용", async () =>
        {
            EnvironmentProbe.EnsureHostClosed(Catalog.RevitHost);
            var archive = await sources.DownloadLoader(Catalog.RevitHost, channel);
            EnsureHostRuntime(cad: false);
            await Task.Run(() => NativeCommands().InstallHost(archive,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "Revit", "Addins", "2026")));
            await RefreshStatuses(); RefreshLoaderStatus();
            WriteLog("Revit 로더 설치 완료 · Revit을 시작한 뒤 적용 상태를 확인하세요.");
        });
    }
}
