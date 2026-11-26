using System.Diagnostics;
using RoBIM.Cad.Configuration;
using RoBIM.Manager.Core;

namespace RoBIM.Manager;

public sealed partial class ManagerBackend
{
    private readonly Dictionary<string, ReleaseChannels> loaderOffers = [];

    private async Task CheckLoaderRepository(ToolSpec tool)
    {
        await Run("로더 저장소 확인", async () =>
        {
            var offers = await sources.CheckLoader(tool, CanUseTest);
            if (offers.StableError is not null || offers.TestError is not null)
            {
                var previous = loaderOffers.GetValueOrDefault(tool.Id);
                offers = offers with
                {
                    Stable = offers.StableError is null ? offers.Stable : previous?.Stable,
                    Test = offers.TestError is null ? offers.Test : previous?.Test
                };
            }
            loaderOffers[tool.Id] = offers;
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
            package = package with { Version = version };

            await Task.Run(() => installer.Install(Catalog.Cad, package));
            if (oldSource is not null && previous is not null)
            {
                try { new CadFeaturePackages(paths).Rebase(oldSource, previous.SelectedActions); }
                catch (Exception error)
                {
                    WriteLog("로더는 설치됐으나 이전 버튼 선택 재준비 실패: " + error.Message);
                    ShowInfo("로더는 설치됐지만 기존 버튼 선택을 다시 준비하지 못했습니다. 기존 준비 기록은 보존됩니다.\n" + error.Message, "AutoCAD 로더");
                }
            }
            await RefreshStatuses();
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
            await Task.Run(() =>
            {
                var baseAddins = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "Revit", "Addins");
                var default2026 = Path.Combine(baseAddins, "2026");
                NativeCommands().InstallHost(archive, default2026);

                if (Directory.Exists(baseAddins))
                {
                    foreach (var yearDir in Directory.GetDirectories(baseAddins))
                    {
                        var name = Path.GetFileName(yearDir);
                        if (int.TryParse(name, out var year) && year > 2026)
                        {
                            try
                            {
                                var addin = SafeFiles.Child(yearDir, "RoBIM.NativeHost.addin");
                                var template = SafeFiles.Child(default2026, "RoBIM.NativeHost.addin");
                                if (File.Exists(template))
                                {
                                    SafeFiles.NoLinks(addin);
                                    if (!File.Exists(addin)) File.Copy(template, addin);
                                }
                            }
                            catch (Exception error) { WriteLog("상위 버전 Addin 복사 건너뜀 (" + name + "): " + error.Message); }
                        }
                    }
                }
            });
            await RefreshStatuses();
            WriteLog("Revit 로더 설치 완료 · Revit을 시작한 뒤 적용 상태를 확인하세요.");
        });
    }
}
