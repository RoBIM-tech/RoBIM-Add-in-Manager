using System.Diagnostics;
using RoBIM.Manager.Core;

namespace RoBIM.Manager;

public sealed partial class ManagerBackend
{
    private async Task LoginGitHub()
    {
        var device = new GitHubDeviceLogin();
        using var cancellation = new CancellationTokenSource();
        var code = await device.Begin(cancellation.Token);
        if (ModernDeviceLoginRequested is null)
            throw new IOException("GitHub 로그인 화면을 사용할 수 없습니다.");
        var modernCompleted = false;
        await ModernDeviceLoginRequested(code, cancellation, async () =>
        {
            Process.Start(new ProcessStartInfo(code.VerificationUri.AbsoluteUri) { UseShellExecute = true });
            var token = await device.Complete(code, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            await GitHubSession.UseDeviceToken(token, cancellation.Token);
            modernCompleted = true;
        });
        if (!modernCompleted) throw new IOException("GitHub 로그인이 취소되었습니다.");
    }
}
