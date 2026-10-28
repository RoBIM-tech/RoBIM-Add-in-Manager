using System.Diagnostics;
using System.Drawing;
using RoBIM.Manager.Core;

namespace RoBIM.Manager;

public sealed partial class ManagerForm
{
    private readonly CheckBox managerAutoUpdate = new() { Text = "Manager 자동 업데이트", AutoSize = true };
    private readonly Label managerUpdateStatus = new() { Text = "", AutoSize = true, MaximumSize = new Size(660,0) };
    private readonly Button managerUpdateCheck = MakeButton("Manager 업데이트 확인");
    private readonly Button managerUpdateNow = MakeButton("Manager 업데이트", true);
    private ManagerUpdate? manuallyCheckedManagerUpdate;
    private DateTimeOffset managerUpdateChecked;
    private bool checkingManagerUpdate;
    private (ManagerUpdate Offer, string Installer)? pendingManagerUpdate;
    private void BuildManagerUpdateActions(FlowLayoutPanel panel)
    {
        managerUpdateNow.Visible=false;
        var row=Row(); row.Controls.Add(managerUpdateCheck); row.Controls.Add(managerUpdateNow); panel.Controls.Add(row);
        managerUpdateCheck.Click += async (_, _) =>
        {
            if (preview) { managerUpdateStatus.Text="현재 버전 · "+Application.ProductVersion.Split('+')[0]+" · 미리보기"; return; }
            if (teamAccess?.Allowed!=true || checkingManagerUpdate || busy) return;
            managerUpdateCheck.Enabled=false;
            try
            {
                manuallyCheckedManagerUpdate=await ManagerUpdates.Check(Application.ProductVersion.Split('+')[0]);
                managerUpdateNow.Visible=manuallyCheckedManagerUpdate is not null;
                managerUpdateStatus.Text=manuallyCheckedManagerUpdate is { } update ? "Manager "+Application.ProductVersion.Split('+')[0]+" → "+update.Version+" 업데이트 가능" : "현재 Manager "+Application.ProductVersion.Split('+')[0]+" · 최신 버전입니다.";
            }
            catch (Exception error) { managerUpdateStatus.Text="Manager 업데이트 확인 실패 · "+error.Message; }
            finally { if (!IsDisposed) managerUpdateCheck.Enabled=true; }
        };
        managerUpdateNow.Click += async (_, _) =>
        {
            if (preview || manuallyCheckedManagerUpdate is not { } update || teamAccess?.Allowed!=true || busy) return;
            if (!ManagerUpdates.NativeInstallation(Path.GetDirectoryName(Environment.ProcessPath!)!))
            { OpenUrl("https://github.com/RoBIM-tech/RoBIM-Add-in-Manager/releases/tag/v"+update.Version); return; }
            managerUpdateNow.Enabled=false;
            try
            {
                var installer=await ManagerUpdates.Download(paths,update);
                pendingManagerUpdate=(update,installer);
                await CheckManagerUpdate(force:true);
            }
            catch (Exception error) { managerUpdateStatus.Text="Manager 업데이트 준비 실패 · "+error.Message; }
            finally { if (!IsDisposed) managerUpdateNow.Enabled=true; }
        };
    }
    private async Task CheckManagerUpdate(bool force = false)
    {
        if (preview || teamAccess is null || !teamAccess.Allowed || busy || checkingManagerUpdate || IsDisposed ||
            (!force && !AccountPreferences.Read(paths).AutoUpdate)) return;
        checkingManagerUpdate = true;
        try
        {
            var directory = Path.GetDirectoryName(Environment.ProcessPath!)!;
            if (!ManagerUpdates.NativeInstallation(directory)) { managerUpdateStatus.Text = "Manager 자동 업데이트는 Setup으로 설치한 버전에서 사용할 수 있습니다."; return; }
            if (pendingManagerUpdate is null && (force || DateTimeOffset.UtcNow - managerUpdateChecked > TimeSpan.FromHours(6)))
            {
                managerUpdateChecked = DateTimeOffset.UtcNow;
                var offer = await ManagerUpdates.Check(Application.ProductVersion.Split('+')[0]);
                if (offer is null) { managerUpdateStatus.Text = "Manager 최신 버전입니다."; return; }
                var failed=SafeFiles.Child(paths.Data,"ManagerUpdates/failed-digest.txt");
                if (!force && File.Exists(failed) && File.ReadAllText(failed)==offer.Digest)
                { managerUpdateStatus.Text="이 Manager 업데이트의 이전 설치가 실패했습니다. 설치 로그를 확인하고 새 Setup으로 다시 설치하세요."; return; }
                managerUpdateStatus.Text = "Manager " + offer.Version + " 업데이트를 다운로드하고 있습니다…";
                var installer = await ManagerUpdates.Download(paths,offer);
                pendingManagerUpdate = (offer,installer);
            }
            if (pendingManagerUpdate is not { } pending || busy || IsDisposed || !teamAccess.Allowed) return;
            var running = Process.GetProcessesByName("Revit").Concat(Process.GetProcessesByName("acad")).Concat(Process.GetProcessesByName("accoreconsole")).ToArray();
            try
            {
                if (running.Length > 0) { managerUpdateStatus.Text = "Manager " + pending.Offer.Version + " 준비됨 · Revit·AutoCAD를 종료하면 자동 업데이트합니다."; return; }
            }
            finally { foreach (var process in running) process.Dispose(); }
            ManagerUpdates.Launch(paths,pending.Installer,pending.Offer.Digest);
            exitRequested = true; Close();
        }
        catch (Exception error) { if (!IsDisposed) managerUpdateStatus.Text = "Manager 업데이트 확인 실패 · 기존 버전 유지: " + error.Message; }
        finally { checkingManagerUpdate = false; }
    }
}
