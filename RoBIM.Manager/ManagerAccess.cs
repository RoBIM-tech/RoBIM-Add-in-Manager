using RoBIM.Manager.Core;

namespace RoBIM.Manager;

public sealed partial class ManagerForm
{
    private TeamAccess? teamAccess;
    private readonly System.Windows.Forms.Timer accessTimer = new() { Interval = 120000 };
    private NotifyIcon? tray;
    private bool exitRequested, checkingAccess, sessionStarted;
    private readonly SemaphoreSlim accountRefreshGate = new(1, 1);
    private readonly CheckBox automaticLogin = new() { Text = "자동 로그인", AutoSize = true };
    private readonly CheckBox startWithWindows = new() { Text = "Windows 로그인 시 알림 영역에서 자동 시작", AutoSize = true };
    private bool Allowed => teamAccess is null || teamAccess.Allowed;

    private void InitializeAccess()
    {
        teamAccess = new TeamAccess();
        ApplyAccessState();
        accessTimer.Tick += async (_, _) =>
        {
            if (checkingAccess || busy || teamAccess is null || !sessionStarted) return;
            if (GitHubSession.Current().Login.Length == 0) return;
            await RefreshAccount();
        };
        accessTimer.Start();
        var menu = new ContextMenuStrip();
        menu.Items.Add("Manager 열기", null, (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); });
        menu.Items.Add("GitHub 로그인", null, (_, _) => { Show(); WindowState = FormWindowState.Normal; SwitchPage("Account"); Activate(); });
        menu.Items.Add("로그아웃", null, (_, _) => SignOut());
        menu.Items.Add("종료", null, (_, _) => { if (busy) { Show(); Activate(); return; } exitRequested = true; Close(); });
        tray = new NotifyIcon { Icon = Icon, Text = "RoBIM Manager · 로그인 필요", Visible = true, ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); };
        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing && !exitRequested && !busy)
            { e.Cancel = true; Hide(); }
        };
        FormClosed += (_, _) => { accessTimer.Stop(); accessTimer.Dispose(); teamAccess.Dispose(); tray.Visible = false; tray.Dispose(); menu.Dispose(); };
    }
    private async Task RestoreAccountAccess()
    {
        if (teamAccess is null) return;
        if (AccountPreferences.Read(paths).AutoLogin) await RefreshAccount();
        else { teamAccess.Lock("자동 로그인이 꺼져 있습니다. GitHub 연결 확인 또는 로그인을 선택하십시오."); ApplyAccessState(); }
        if (!IsDisposed && !teamAccess.Allowed) SwitchPage("Account");
    }
    private void SignOut()
    {
        teamAccess?.Lock(); sessionStarted = false; GitHubSession.Disconnect(); authenticated = false; connectedLogin = "";
        accountStatus.Text = "로그아웃됨 · GitHub 로그인이 필요합니다."; ApplyAccessState();
        SwitchPage("Account");
    }
    private void ApplyAccessState()
    {
        if (teamAccess is null) return;
        UpdateLoginPage();
        var allowed = teamAccess.Allowed && !busy;
        userPage.Enabled = developerPage.Enabled = extensionsPage.Enabled = productsPage.Enabled = locationsPage.Enabled = ribbonPage.Enabled = allowed;
        toolbar.Enabled = actions.Enabled = grid.Enabled = allowed;
        if (tray is not null) tray.Text = teamAccess.Allowed ? "RoBIM Manager · 팀 인증 완료" : "RoBIM Manager · 로그인/팀 인증 필요";
        if (!teamAccess.Allowed)
        {
            accountStatus.Text = teamAccess.Result.Detail;
            catalogIdentity.Text = teamAccess.Result.Detail;
            footer.Text = teamAccess.Result.Detail;
        }
    }
    private void BuildAccountOptions(FlowLayoutPanel panel)
    {
        try { automaticLogin.Checked = AccountPreferences.Read(paths).AutoLogin; } catch (Exception) { automaticLogin.Checked = true; }
        try { startWithWindows.Checked = StartupRegistration.Enabled(Environment.ProcessPath!); } catch (Exception) { }
        panel.Controls.Add(automaticLogin); panel.Controls.Add(startWithWindows);
        automaticLogin.CheckedChanged += (_, _) => { if (!preview) new AccountPreferences(automaticLogin.Checked).Save(paths); };
        startWithWindows.CheckedChanged += (_, _) =>
        {
            if (preview) return;
            try { StartupRegistration.Set(Environment.ProcessPath!, startWithWindows.Checked); }
            catch (Exception error) { MessageBox.Show(this, error.Message, "Windows 자동 시작"); }
        };
        panel.Controls.Add(Note("RoBIM-tech / robim 팀의 활성 구성원만 도구를 사용할 수 있습니다. 창을 닫으면 알림 영역에서 인증을 유지하며, 종료·로그아웃하면 도구가 잠깁니다."));
    }
    public void VerifyTeamAccessUi(string destination)
    {
        Shown += async (_, _) =>
        {
            var checks = new List<string>();
            try
            {
                void Require(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
                teamAccess = new TeamAccess(false, () => Task.FromResult(new GitHubIdentity("fixture", "Browser")),
                    _ => Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    { Content = new System.Net.Http.StringContent("{\"state\":\"active\"}") }));
                ApplyAccessState();
                Require(!productsPage.Enabled && !ribbonPage.Enabled && !locationsPage.Enabled && accountPage.Enabled, "UnauthenticatedToolsDisabledLoginAvailable");
                Require(loginPage.Visible && mainViews.All(view => !view.Visible), "DedicatedLoginHidesNavigationAndTools");
                var ran = false; await Run("fixture", () => { ran = true; return Task.CompletedTask; });
                Require(!ran, "UnauthorizedActionDoesNotRun");
                SwitchPage("Account"); PerformLayout();
                using (var bitmap = new System.Drawing.Bitmap(Width, Height))
                { DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, Width, Height)); bitmap.Save(destination + ".png"); }
                await teamAccess.Refresh(); ApplyAccessState();
                Require(productsPage.Enabled && ribbonPage.Enabled && locationsPage.Enabled, "ActiveTeamEnablesManagerTools");
                Require(!loginPage.Visible && mainViews.All(view => view.Visible) && currentPage == "Revit", "LoginOpensToolHome");
                teamAccess.Lock(); ApplyAccessState();
                Require(!productsPage.Enabled && !ribbonPage.Enabled, "LogoutDisablesManagerTools");
                Require(loginPage.Visible && mainViews.All(view => !view.Visible), "LogoutReturnsToDedicatedLogin");
                File.WriteAllText(destination, System.Text.Json.JsonSerializer.Serialize(new { Passed = true, Checks = checks }));
            }
            catch (Exception error) { File.WriteAllText(destination, System.Text.Json.JsonSerializer.Serialize(new { Passed = false, Checks = checks, Error = error.ToString() })); }
            finally { teamAccess?.Dispose(); teamAccess = null; Close(); }
        };
    }
}
