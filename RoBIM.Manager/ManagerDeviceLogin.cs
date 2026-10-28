using System.Diagnostics;
using System.Drawing;
using RoBIM.Manager.Core;

namespace RoBIM.Manager;

public sealed partial class ManagerForm
{
    private async Task LoginGitHub()
    {
        var device = new GitHubDeviceLogin();
        using var cancellation = new CancellationTokenSource();
        var code = await device.Begin(cancellation.Token);
        using var dialog = new Form { Text = "GitHub 로그인", ClientSize = new Size(540, 280), FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterParent };
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(24) };
        panel.Controls.Add(new Label { Text = "브라우저에서 아래 코드를 입력하고\nRoBIM Add-in Manager 로그인을 승인하세요.", AutoSize = true, Margin = new Padding(0, 0, 0, 16) });
        panel.Controls.Add(new TextBox { Text = code.UserCode, ReadOnly = true, Width = 470, TextAlign = HorizontalAlignment.Center, Font = new Font("Segoe UI", 22, FontStyle.Bold) });
        var open = MakeButton("GitHub 인증 페이지 열기", true);
        open.Click += (_, _) => Process.Start(new ProcessStartInfo(code.VerificationUri.AbsoluteUri) { UseShellExecute = true });
        panel.Controls.Add(open);
        panel.Controls.Add(new Label { Text = "인증이 끝나면 자동으로 계속합니다. 만료·철회되면 다시 로그인하세요.", AutoSize = true, MaximumSize = new Size(470, 0), Margin = new Padding(0, 12, 0, 12) });
        var cancel = MakeButton("취소"); cancel.Click += (_, _) => dialog.Close(); panel.Controls.Add(cancel);
        dialog.Controls.Add(panel); dialog.CancelButton = cancel;
        Exception? failure = null; var completed = false;
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dialog.FormClosing += (_, _) => cancellation.Cancel();
        dialog.Shown += async (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(code.VerificationUri.AbsoluteUri) { UseShellExecute = true });
                var token = await device.Complete(code, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                await GitHubSession.UseDeviceToken(token, cancellation.Token);
                completed = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { failure = ex; }
            finally { finished.TrySetResult(); if (!dialog.IsDisposed) dialog.Close(); }
        };
        dialog.ShowDialog(this);
        await finished.Task;
        if (failure is not null) throw failure;
        if (!completed) throw new IOException("GitHub 로그인이 취소되었습니다.");
    }
}
