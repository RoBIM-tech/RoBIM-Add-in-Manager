using RoBIM.Manager.Core;

namespace RoBIM.Manager;

public sealed partial class ManagerBackend
{
    private bool managerAutoUpdateChecked = true;
    private ManagerUpdate? manuallyCheckedManagerUpdate;
    private DateTimeOffset managerUpdateChecked;
    private bool checkingManagerUpdate;
    private (ManagerUpdate Offer, string Installer)? pendingManagerUpdate;

    private async Task CheckManagerUpdateManually()
    {
        if (preview) { managerUpdateStatusText = "현재 버전 · " + ManagerProductVersion + " · 미리보기"; return; }
        if (checkingManagerUpdate || busy) return;
        checkingManagerUpdate = true;
        try
        {
            manuallyCheckedManagerUpdate = await ManagerUpdates.Check(ManagerProductVersion, AccountPreferences.Read(paths).AllowPrerelease);
            managerUpdateStatusText = manuallyCheckedManagerUpdate is { } update
                ? "Manager " + ManagerProductVersion + " → " + update.Version + " 업데이트 가능"
                : "현재 Manager " + ManagerProductVersion + " · 최신 버전입니다.";
        }
        catch (Exception error) { managerUpdateStatusText = "Manager 업데이트 확인 실패 · " + error.Message; }
        finally { checkingManagerUpdate = false; }
    }

    private async Task InstallManagerUpdateManually()
    {
        if (preview || manuallyCheckedManagerUpdate is not { } update || busy || checkingManagerUpdate) return;
        if (!ManagerUpdates.NativeInstallation(Path.GetDirectoryName(Environment.ProcessPath!)!))
        {
            OpenUrl("https://github.com/RoBIM-tech/RoBIM-Add-in-Manager/releases/tag/v" + update.Version);
            return;
        }
        checkingManagerUpdate = true;
        try
        {
            managerUpdateStatusText = $"Manager {update.Version} 다운로드 준비 중…";
            ManagerUpdateProgressChanged?.Invoke(0, managerUpdateStatusText);
            var progress = new Progress<(long Downloaded, long? Total)>(p =>
            {
                if (p.Total is > 0)
                {
                    var pct = Math.Clamp(p.Downloaded * 100.0 / p.Total.Value, 0, 100);
                    var text = $"다운로드 중… {pct:0}% ({p.Downloaded / 1048576.0:0.0} / {p.Total.Value / 1048576.0:0.0} MB)";
                    managerUpdateStatusText = text;
                    ManagerUpdateProgressChanged?.Invoke(pct, text);
                }
                else
                {
                    var text = $"다운로드 중… ({p.Downloaded / 1048576.0:0.0} MB)";
                    managerUpdateStatusText = text;
                    ManagerUpdateProgressChanged?.Invoke(null, text);
                }
            });
            var installer = await ManagerUpdates.Download(paths, update, progress);
            pendingManagerUpdate = (update, installer);
            managerUpdateStatusText = $"Manager {update.Version} 다운로드 완료 · '지금 재시작'을 누르면 업데이트를 적용합니다.";
            ManagerUpdateProgressChanged?.Invoke(100, managerUpdateStatusText);
        }
        catch (Exception error)
        {
            managerUpdateStatusText = "Manager 업데이트 준비 실패 · " + error.Message;
            ManagerUpdateProgressChanged?.Invoke(null, managerUpdateStatusText);
        }
        finally { checkingManagerUpdate = false; }
    }

    private async Task CheckManagerUpdate(bool force = false)
    {
        if (preview || busy || checkingManagerUpdate || disposed ||
            (!force && !AccountPreferences.Read(paths).AutoUpdate)) return;
        checkingManagerUpdate = true;
        try
        {
            var directory = Path.GetDirectoryName(Environment.ProcessPath!)!;
            if (!ManagerUpdates.NativeInstallation(directory))
            {
                managerUpdateStatusText = "Manager 자동 업데이트는 Setup으로 설치한 버전에서 사용할 수 있습니다.";
                return;
            }
            if (pendingManagerUpdate is null && (force || DateTimeOffset.UtcNow - managerUpdateChecked > TimeSpan.FromHours(6)))
            {
                managerUpdateChecked = DateTimeOffset.UtcNow;
                var offer = await ManagerUpdates.Check(ManagerProductVersion, AccountPreferences.Read(paths).AllowPrerelease);
                if (offer is null) { managerUpdateStatusText = "Manager 최신 버전입니다."; return; }
                var failed = SafeFiles.Child(paths.Data, "ManagerUpdates/failed-digest.txt");
                if (!force && File.Exists(failed) && File.ReadAllText(failed) == offer.Digest)
                {
                    managerUpdateStatusText = "이 Manager 업데이트의 이전 설치가 실패했습니다. 설치 로그를 확인하고 새 Setup으로 다시 설치하세요.";
                    return;
                }
                managerUpdateStatusText = "Manager " + offer.Version + " 업데이트를 다운로드하고 있습니다…";
                var installer = await ManagerUpdates.Download(paths, offer);
                pendingManagerUpdate = (offer, installer);
            }
            if (pendingManagerUpdate is not { } pending || busy || disposed) return;
            ManagerUpdates.Launch(paths, pending.Installer, pending.Offer.Digest);
            CloseBackend();
        }
        catch (Exception error)
        {
            if (!disposed) managerUpdateStatusText = "Manager 업데이트 확인 실패 · 기존 버전 유지: " + error.Message;
        }
        finally { checkingManagerUpdate = false; }
    }
}
