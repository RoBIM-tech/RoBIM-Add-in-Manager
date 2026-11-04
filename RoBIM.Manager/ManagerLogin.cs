using System.Drawing;
using RoBIM.Manager.Core;

namespace RoBIM.Manager;

public sealed partial class ManagerForm
{
    private readonly Panel loginPage = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(243, 246, 251), AutoScroll = true, Visible = false };
    private readonly Label loginMessage = new() { AutoSize = true, MaximumSize = new(496, 0), ForeColor = Color.FromArgb(90, 105, 125), Margin = new(0, 14, 0, 4) };
    private readonly Button loginPrimary = MakeButton("GitHub로 로그인", true);
    private readonly Button loginSaved = MakeButton("저장된 계정으로 로그인");
    private readonly CheckBox loginRemember = new() { Text = "다음 실행부터 자동 로그인", AutoSize = true, Margin = new(0, 16, 0, 8) };
    private Control[] mainViews = [];
    private bool showingLogin;

    private void BuildLoginPage()
    {
        mainViews = Controls.Cast<Control>().ToArray();
        var card = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new(32), BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        Label TextLine(string text, float size, bool bold, Color color) => new()
        {
            Text = text, AutoSize = true, MaximumSize = new(496, 0),
            Font = new Font(Font.FontFamily, size, bold ? FontStyle.Bold : FontStyle.Regular),
            ForeColor = color, Margin = new(0, 0, 0, 16)
        };
        card.Controls.Add(TextLine("RoBIM", 27, true, Color.FromArgb(49, 92, 164)));
        card.Controls.Add(TextLine("GitHub 계정으로 시작하세요", 20, true, Color.FromArgb(24, 38, 62)));
        card.Controls.Add(TextLine("RoBIM 팀의 GitHub 계정으로 로그인하세요.\n일반 사용자는 정식, TEST·개발자·관리자는 TEST까지 사용합니다.", 10, false, Color.FromArgb(90, 105, 125)));
        foreach (var button in new[] { loginPrimary, loginSaved })
        {
            button.AutoSize = false; button.Size = new(496, 46); button.Margin = new(0, 6, 0, 4);
            card.Controls.Add(button);
        }
        loginRemember.Checked = automaticLogin.Checked;
        loginRemember.CheckedChanged += (_, _) => automaticLogin.Checked = loginRemember.Checked;
        automaticLogin.CheckedChanged += (_, _) => loginRemember.Checked = automaticLogin.Checked;
        card.Controls.Add(loginRemember); card.Controls.Add(loginMessage);
        var updateCheckLogin = MakeButton("Manager 업데이트 확인");
        var updateNowLogin = MakeButton("Manager 업데이트", true);
        var updateLoginStatus = new Label { AutoSize = true, MaximumSize = new(496, 0) };
        updateCheckLogin.Click += async (_, _) => await CheckManagerUpdateManually();
        updateNowLogin.Click += async (_, _) => await InstallManagerUpdateManually();
        managerUpdateStatus.TextChanged += (_, _) => { updateLoginStatus.Text = managerUpdateStatus.Text; updateNowLogin.Visible = manuallyCheckedManagerUpdate is not null; };
        managerUpdateNow.VisibleChanged += (_, _) => updateNowLogin.Visible = managerUpdateNow.Visible;
        updateNowLogin.Visible = false;
        card.Controls.Add(updateCheckLogin); card.Controls.Add(updateNowLogin); card.Controls.Add(updateLoginStatus);

        card.Controls.Add(TextLine("Reload와 Manager 열기는 로그인하지 않아도 사용할 수 있습니다.", 9, false, Color.FromArgb(90, 105, 125)));
        card.Controls.Add(TextLine("Add-in Manager · " + Application.ProductVersion.Split('+')[0], 9, false, Color.FromArgb(120, 133, 150)));
        loginPrimary.Click += async (_, _) => await Run("GitHub 로그인", async () =>
        {
            teamAccess?.Lock(); await LoginGitHub(); await RefreshAccountAndCatalog();
        }, requiresAccess: false);
        loginSaved.Click += async (_, _) => await Run("저장된 계정 확인", async () =>
        {
            teamAccess?.Lock();
            if (GitHubSession.Current().Login.Length == 0) await GitHubSession.UseBrowser();
            await RefreshAccountAndCatalog();
        }, requiresAccess: false);
        loginPage.Controls.Add(card); Controls.Add(loginPage);
        void Center()
        {
            card.Location = new(Math.Max(20, (loginPage.ClientSize.Width - card.Width) / 2), Math.Max(20, (loginPage.ClientSize.Height - card.Height) / 2));
        }
        loginPage.Resize += (_, _) => Center(); card.SizeChanged += (_, _) => Center();
        Center();
    }

    private void UpdateLoginPage()
    {
        if (teamAccess is null) return;
        var show = !teamAccess.Allowed;
        foreach (var view in mainViews) view.Visible = !show;
        loginPage.Visible = show;
        if (show)
        {
            loginPage.BringToFront();
            loginMessage.Text = busy ? "GitHub 로그인과 팀 권한을 확인하고 있습니다…" : refreshingSnapshot ? "설치 정보를 확인하고 있습니다…" : teamAccess.Result.Detail;
            loginPrimary.Enabled = loginSaved.Enabled = !busy && !checkingAccess && !refreshingSnapshot;
            loginSaved.Text = teamAccess.Result.State == AccountAccessState.RetryPending ? "연결 다시 확인" : "저장된 계정으로 로그인";
            loginPrimary.Text = teamAccess.Result.State == AccountAccessState.ReauthenticationRequired ? "GitHub 재로그인" : "GitHub로 로그인";
        }
        else if (showingLogin) SwitchPage("Revit");
        showingLogin = show;
    }
}
