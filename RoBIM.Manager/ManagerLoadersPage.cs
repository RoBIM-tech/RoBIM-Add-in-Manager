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

    private void BuildLoadersPage()
    {
        var body = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        void Card(string name, string repository, string path, Label status, string action, Func<Task> apply)
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
            var button = MakeButton(action, true); button.AutoSize = true;
            button.Click += async (_, _) => { if (!preview) await apply(); };
            actions.Controls.Add(folder); actions.Controls.Add(button); card.Controls.Add(actions); body.Controls.Add(card);
        }
        Card("RoBIM CAD 로더", "RoBIM-tech/RoBIM-CAD", paths.Target(Catalog.Cad), cadLoaderStatus, "AutoCAD 로더 적용 / 업데이트", ApplyCadLoader);
        Card("RoBIM Revit Host 로더", "RoBIM-tech/RoBIM-Revit-Addin", paths.Target(Catalog.RevitHost), revitLoaderStatus, "Revit 로더 적용 / 업데이트", ApplyRevitLoader);
        body.Controls.Add(new Label { Text = "RoBIM Navisworks 로더 · 준비 중", AutoSize = true, Padding = new Padding(12) });
        loadersPage.Controls.Add(body);
    }

    private void RefreshLoaderStatus()
    {
        try
        {
            var cad = installer.Status(Catalog.Cad);
            var revit = installer.Status(Catalog.RevitHost);
            cadLoaderStatus.Text = DescribeLoader(cad, "DelHiddenNet-AutoCAD2026.zip", "DelHiddenNet.bundle/Contents/Windows/RoBIM.Cad.Loader.dll", "Contents/Windows/RoBIM.Cad.Loader.dll");
            revitLoaderStatus.Text = DescribeLoader(revit, "RoBIM-Host-Revit2026.zip", "RoBIM.Revit.Host.bundle/Contents/2026/RoBIM.Revit.Host.dll", "Contents/2026/RoBIM.Revit.Host.dll");
            cadLoaderBanner.Text = "로더: RoBIM CAD 로더 (RoBIM-tech/RoBIM-CAD) · " + cadLoaderStatus.Text;
        }
        catch (Exception error)
        {
            cadLoaderStatus.Text = revitLoaderStatus.Text = "상태 확인 실패: " + error.Message;
            cadLoaderBanner.Text = "로더 상태 확인 실패: " + error.Message;
        }
    }

    private static string DescribeLoader(ToolStatus status, string asset, string entryName, string dll)
    {
        if (status.Modified) return "로컬 변경 있음 · 보호를 위해 적용 차단";
        if (status.External) return "외부 설치 · 보호를 위해 적용 차단";
        if (!status.Managed) return "미설치";
        var installed = Path.Combine(status.Path, dll);
        var version = File.Exists(installed) ? FileVersionInfo.GetVersionInfo(installed).ProductVersion?.Split('+')[0] : status.Version;
        var archive = Path.Combine(AppContext.BaseDirectory, asset);
        if (!File.Exists(archive) || !File.Exists(archive + ".sha256") || !SafeFiles.Hash(archive).Equals(File.ReadAllText(archive + ".sha256").Trim(), StringComparison.OrdinalIgnoreCase))
            return "설치됨 · " + version + " · 동봉 패키지 검증 실패";
        using var zip = System.IO.Compression.ZipFile.OpenRead(archive);
        var entry = zip.GetEntry(entryName) ?? throw new IOException("동봉 로더 DLL이 없습니다.");
        using var stream = entry.Open();
        var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
        return "설치됨 · " + version + (File.Exists(installed) && SafeFiles.Hash(installed).Equals(expected, StringComparison.OrdinalIgnoreCase) ? " · 동봉본과 일치" : " · 수정사항 있음 (적용 필요)");
    }

    private async Task ApplyCadLoader()
    {
        await Run("AutoCAD 로더 적용", async () =>
        {
            EnvironmentProbe.EnsureHostClosed(Catalog.Cad);
            var status = installer.Status(Catalog.Cad);
            if (status.Modified || status.External) throw new IOException("로컬 변경 또는 외부 설치를 보존하기 위해 중단합니다.");
            var previous = status.Managed ? new CadReload(paths).Desired(status.Path) : null;
            var oldSource = previous is null ? null : new CadReload(paths).Source(status.Path, preferInstalled: true);
            var package = await sources.OfflineCad(Path.Combine(AppContext.BaseDirectory, "DelHiddenNet-AutoCAD2026.zip"));
            RequirePackageAccess(Catalog.Cad, package);
            var newRoot = CadReload.WindowsDirectory(package.Path);
            var version = FileVersionInfo.GetVersionInfo(Path.Combine(newRoot, "RoBIM.Cad.Loader.dll")).ProductVersion?.Split('+')[0];
            if (string.IsNullOrWhiteSpace(version)) throw new IOException("동봉 CAD 로더 버전을 확인할 수 없습니다.");
            if (version.Contains('-')) RequireChannelAccess("test");
            if (!string.IsNullOrWhiteSpace(version)) package = package with { Version = version };
            if (oldSource is not null) CadFiles.Validate(newRoot, oldSource.Catalog);
            await Task.Run(() => installer.Install(Catalog.Cad, package));
            if (oldSource is not null && previous is not null)
            {
                try { new CadReload(paths).Prepare(Catalog.Cad, package.Path, oldSource.Catalog, previous.SelectedActions, null); }
                catch (Exception error) { WriteLog("로더는 설치됐으나 이전 버튼 선택 재준비 실패: " + error.Message); MessageBox.Show(this, "로더는 설치됐지만 기존 버튼 선택을 다시 준비하지 못했습니다. 기존 준비 기록은 보존됩니다.\n" + error.Message, "AutoCAD 로더"); }
            }
            cadEditorLoaded = false;
            await RefreshStatuses(); RefreshLoaderStatus();
            WriteLog("AutoCAD 로더 설치 완료 · AutoCAD를 시작한 뒤 적용 상태를 확인하세요.");
        });
    }

    private async Task ApplyRevitLoader()
    {
        await Run("Revit 로더 적용", async () =>
        {
            EnvironmentProbe.EnsureHostClosed(Catalog.RevitHost);
            await Task.Run(InstallRibbonHost);
            await RefreshStatuses(); RefreshLoaderStatus();
            WriteLog("Revit 로더 설치 완료 · Revit을 시작한 뒤 적용 상태를 확인하세요.");
        });
    }
}
