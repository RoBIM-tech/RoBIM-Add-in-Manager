using System.Windows.Threading;
using RoBIM.Manager.Core;

namespace RoBIM.Manager;

public sealed partial class ManagerBackend
{
    private TeamAccess? teamAccess;
    private readonly DispatcherTimer accessTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private System.Windows.Forms.NotifyIcon? tray;
    private System.Windows.Forms.ContextMenuStrip? trayMenu;
    private System.Drawing.Icon? trayIcon;
    private bool exitRequested, checkingAccess, sessionStarted;
    private DateTimeOffset lastAccountRefresh;
    private readonly SemaphoreSlim accountRefreshGate = new(1, 1);
    private bool allowPrereleaseChecked;
    private bool automaticLoginChecked = true;
    private bool startWithWindowsChecked;
    private readonly List<string> savedAccountLogins = [];
    private bool Allowed => teamAccess is null || teamAccess.Allowed;

    private void InitializeAccess()
    {
        LoadSavedAccountOptions();
        teamAccess = new TeamAccess();
        teamAccess.ToolsChanged += () => BackendChanged?.Invoke();
        ApplyAccessState();
        accessTimer.Tick += async (_, _) =>
        {
            if (checkingAccess || teamAccess is null || !sessionStarted) return;
            if (teamAccess.Allowed && DateTimeOffset.UtcNow - lastAccountRefresh < TimeSpan.FromMinutes(2)) return;
            if (teamAccess.Result.State == AccountAccessState.ReauthenticationRequired) return;
            if (GitHubSession.Current().Login.Length == 0) return;
            await RefreshAccount();
        };
        accessTimer.Start();

        trayMenu = new System.Windows.Forms.ContextMenuStrip();
        trayMenu.Items.Add("Manager 열기", null, (_, _) => OpenModernUiRequested?.Invoke());
        trayMenu.Items.Add("GitHub 로그인", null, (_, _) => OpenModernUiRequested?.Invoke());
        trayMenu.Items.Add("로그아웃", null, (_, _) => SignOut());
        trayMenu.Items.Add("종료", null, (_, _) =>
        {
            if (busy) { OpenModernUiRequested?.Invoke(); return; }
            CloseBackend();
        });
        using (var stream = typeof(ManagerBackend).Assembly.GetManifestResourceStream("RoBIM.Manager.AppIcon.ico"))
        {
            if (stream is not null) trayIcon = new System.Drawing.Icon(stream);
        }
        tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = trayIcon,
            Text = "RoBIM Manager · 로그인 필요",
            Visible = true,
            ContextMenuStrip = trayMenu
        };
        tray.DoubleClick += (_, _) => OpenModernUiRequested?.Invoke();
    }

    private void LoadSavedAccountOptions()
    {
        try { automaticLoginChecked = AccountPreferences.Read(paths).AutoLogin; } catch (Exception) { automaticLoginChecked = true; }
        try { startWithWindowsChecked = StartupRegistration.Enabled(Environment.ProcessPath!); } catch (Exception) { }
        try { managerAutoUpdateChecked = AccountPreferences.Read(paths).AutoUpdate; } catch (Exception) { managerAutoUpdateChecked = true; }
        try { allowPrereleaseChecked = AccountPreferences.Read(paths).AllowPrerelease; } catch (Exception) { allowPrereleaseChecked = false; }
    }

    private void DisposeAccess()
    {
        accessTimer.Stop();
        teamAccess?.Dispose();
        if (tray is not null)
        {
            tray.Visible = false;
            tray.Dispose();
            tray = null;
        }
        trayMenu?.Dispose();
        trayMenu = null;
        trayIcon?.Dispose();
        trayIcon = null;
    }

    private async Task RestoreAccountAccess()
    {
        if (teamAccess is null) return;
        if (AccountPreferences.Read(paths).AutoLogin) await RefreshAccount();
        else { teamAccess.Lock("자동 로그인이 꺼져 있습니다. GitHub 연결 확인 또는 로그인을 선택하십시오."); ApplyAccessState(); }
    }

    private void SignOut()
    {
        var previousLogin = connectedLogin;
        teamAccess?.Lock();
        sessionStarted = false;
        GitHubSession.Disconnect(removeSaved: true);
        authenticated = false;
        connectedLogin = "";
        if (!string.IsNullOrWhiteSpace(previousLogin))
        {
            lock (savedAccountLogins) savedAccountLogins.Remove(previousLogin);
        }
        accountStatusText = "로그아웃됨 · GitHub 로그인이 필요합니다.";
        ApplyAccessState();
    }

    private void ApplyAccessState()
    {
        if (teamAccess is null) return;
        BackendChanged?.Invoke();
        if (tray is not null) tray.Text = teamAccess.Allowed ? "RoBIM Manager · 팀 인증 완료" : "RoBIM Manager · 로그인/팀 인증 필요";
        if (!teamAccess.Allowed)
        {
            accountStatusText = teamAccess.Result.Detail;
            footerText = teamAccess.Result.Detail;
        }
    }

    private async Task RefreshAccount()
    {
        await accountRefreshGate.WaitAsync();
        checkingAccess = true;
        lastAccountRefresh = DateTimeOffset.UtcNow;
        sessionStarted = true;
        try
        {
            var accounts = new List<string>();
            try { accounts.AddRange(await GitHubAccount.SavedAccounts()); } catch (IOException) { }
            lock (savedAccountLogins)
            {
                savedAccountLogins.Clear();
                savedAccountLogins.AddRange(accounts);
            }
            var access = teamAccess is null ? null : await teamAccess.Refresh();
            if (access is { Allowed: false })
            {
                authenticated = false;
                connectedLogin = access.Login;
                accountStatusText = access.Detail;
                return;
            }
            var identity = access is null ? await GitHubSession.Identify() : new GitHubIdentity(access.Login, GitHubSession.Current().Mode);
            authenticated = true;
            connectedLogin = identity.Login;
            accountStatusText = "연결된 GitHub ID  ·  " + identity.Login + "  /  " + (access?.Role ?? "계정 연결");
        }
        catch (Exception error)
        {
            teamAccess?.Lock(error.Message);
            authenticated = false;
            connectedLogin = "";
            accountStatusText = error.Message;
        }
        finally
        {
            checkingAccess = false;
            accountRefreshGate.Release();
            if (!disposed) ApplyAccessState();
        }
        if (!disposed) _ = CheckManagerUpdate();
    }
}
