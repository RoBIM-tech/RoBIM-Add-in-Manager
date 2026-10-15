using System.Drawing;
using System.IO;
using System.Text.Json;
using RoBIM.Manager.Core;

namespace RoBIM.Manager;

public sealed partial class ManagerForm
{
    private readonly Panel content = new() { Dock = DockStyle.Fill, Padding = new(28, 22, 28, 20), BackColor = Color.FromArgb(248, 250, 253) };
    private readonly Panel userPage = new() { Dock = DockStyle.Fill };
    private readonly Panel developerPage = new() { Dock = DockStyle.Fill };
    private readonly Panel accountPage = new() { Dock = DockStyle.Fill };
    private readonly Panel extensionsPage = new() { Dock = DockStyle.Fill };
    private readonly Label pageTitle = new() { AutoSize = true, Font = new Font("맑은 고딕", 21, FontStyle.Bold), ForeColor = Color.FromArgb(24, 38, 62) };
    private readonly Label pageSubtitle = new() { AutoSize = true, ForeColor = Color.FromArgb(106, 117, 136), MaximumSize = new(850, 0) };
    private readonly Label profileLabel = new() { AutoSize = true, ForeColor = Color.FromArgb(42, 96, 178), Padding = new(0, 10, 0, 8) };
    private readonly Label accountStatus = new() { AutoSize = true, Font = new Font("맑은 고딕", 13, FontStyle.Bold), Padding = new(0, 14, 0, 14) };
    private readonly ComboBox branchBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 530, IntegralHeight = false, DropDownHeight = 240 };
    private readonly TextBox developerId = new() { Text = "jm", Width = 125 };
    private readonly CheckedListBox panelList = new() { Dock = DockStyle.Fill, CheckOnClick = true, BorderStyle = BorderStyle.None, BackColor = Color.White, IntegralHeight = false };
    private readonly Label devInfo = new() { AutoSize = true, MaximumSize = new(850, 0), ForeColor = Color.FromArgb(85, 100, 121) };
    private readonly Button devInstall = MakeButton("선택한 TEST 설치", true);
    private readonly Button devApply = MakeButton("개발자 프로필 적용");
    private readonly Button userApply = MakeButton("사용자 프로필 적용");
    private readonly List<Control> navigation = [];
    private SourcePackage? developerSource;
    private string? loadedBranch;
    private string currentPage = "Revit";
    private UiPreferences preferences = new();
    private sealed record UiPreferences(string DeveloperId = "jm", string Branch = "", string[]? Panels = null);

    private void BuildView()
    {
        var sidebar = new Panel { Dock = DockStyle.Left, Width = 208, BackColor = Color.FromArgb(23, 36, 58), Padding = new(16, 24, 16, 16) };
        sidebar.Controls.Add(new Label { Text = "RoBIM\nADD-IN\nMANAGER", ForeColor = Color.White, Font = new Font(Font.FontFamily, 19, FontStyle.Bold), AutoSize = true, Location = new(22, 20) });
        var nav = new FlowLayoutPanel { Location = new(12, 140), Size = new(184, 360), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        foreach (var (id, title) in new[] { ("Revit", "Revit"), ("Cad", "AutoCAD"), ("Extensions", "도구 추가"), ("Developer", "개발자 TEST"), ("Account", "설정 / GitHub") })
        {
            var button = new Button { Text = title, Tag = id, Width = 180, Height = 48, FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleLeft, Padding = new(12, 0, 0, 0), Margin = new(0, 0, 0, 8), ForeColor = Color.White, BackColor = sidebar.BackColor };
            button.FlatAppearance.BorderSize = 0;
            button.Click += (_, _) => SwitchPage(id);
            navigation.Add(button); nav.Controls.Add(button);
        }
        sidebar.Controls.Add(nav);
        sidebar.Controls.Add(new Label { Text = "Add-in Manager\nPreview 5", ForeColor = Color.FromArgb(148, 165, 190), AutoSize = true, Dock = DockStyle.Bottom, Padding = new(8) });
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
        shell.RowStyles.Add(new(SizeType.AutoSize)); shell.RowStyles.Add(new(SizeType.AutoSize));
        shell.RowStyles.Add(new(SizeType.Percent, 100)); shell.RowStyles.Add(new(SizeType.AutoSize));
        var header = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new(0, 0, 0, 12) };
        header.Controls.AddRange([pageTitle, pageSubtitle]); shell.Controls.Add(header);
        shell.Controls.Add(profileLabel);
        var pages = new Panel { Dock = DockStyle.Fill };
        pages.Controls.AddRange([userPage, developerPage, accountPage, extensionsPage, productsPage]); shell.Controls.Add(pages);
        footer.Font = new Font(Font.FontFamily, 9);
        footer.ForeColor = Color.FromArgb(110, 120, 135); shell.Controls.Add(footer);
        content.Controls.Add(shell);
        Controls.Add(content); Controls.Add(sidebar);
        BuildUserPage(); BuildDeveloperPage(); BuildAccountPage(); BuildExtensionsPage(); BuildProductsPage();
        try
        {
            var file = Path.Combine(paths.Data, "ui-preferences.json");
            SafeFiles.NoLinks(file);
            if (File.Exists(file)) preferences = JsonSerializer.Deserialize<UiPreferences>(File.ReadAllText(file)) ?? new();
            developerId.Text = preferences.DeveloperId;
        }
        catch (Exception) { preferences = new(); }
        SwitchPage("Revit");
    }

    private static FlowLayoutPanel Row() => new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new(0, 0, 0, 10) };
    private static Label Note(string text) => new() { Text = text, AutoSize = true, MaximumSize = new(850, 0), ForeColor = Color.FromArgb(101, 115, 136), Padding = new(0, 8, 0, 12) };
    private void BuildUserPage()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.Absolute, 102)); layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.AutoSize));
        var refresh = MakeButton("새로 고침"); var check = MakeButton("업데이트 확인");
        var prepare = MakeButton("Revit Reload용 준비");
        prepare.Click += async (_, _) => await PrepareUserReload();
        toolbar.Padding = new(0); toolbar.Controls.AddRange([refresh, check, userApply, prepare]); layout.Controls.Add(toolbar);
        AddColumn("Host", "제품", 65); AddColumn("Name", "도구", 120); AddColumn("Version", "설치 버전", 155); AddColumn("Latest", "최신 버전", 140); AddColumn("Status", "상태", 180);
        grid.RowTemplate.Height = 56; grid.ColumnHeadersHeight = 42;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(239, 243, 249);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(80, 96, 117);
        grid.DefaultCellStyle.Padding = new(8, 0, 2, 0); grid.GridColor = Color.FromArgb(235, 239, 245);
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(230, 240, 254); grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(25, 70, 130);
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.SelectionChanged += (_, _) => ShowSelected(); layout.Controls.Add(grid);
        details.Margin = new(0, 12, 0, 0); details.Font = new Font(Font.FontFamily, 9); details.BackColor = content.BackColor; layout.Controls.Add(details);
        actions.Padding = new(0, 8, 0, 8);
        var more = MakeButton("추가 작업 ▾");
        var menu = new ContextMenuStrip();
        menu.Items.Add("이전 버전 복원", null, async (_, _) => await RestoreSelected());
        menu.Items.Add("도구 제거", null, async (_, _) => await RemoveSelected());
        menu.Items.Add("설치 폴더 열기", null, (_, _) => { if (SelectedStatus() is { } row) OpenFolder(row.Path); });
        menu.Items.Add("GitHub 저장소", null, (_, _) => { if (SelectedStatus() is { } row) OpenUrl("https://github.com/" + row.Tool.Repository); });
        more.Click += (_, _) => menu.Show(more, new Point(0, more.Height));
        actions.Controls.AddRange([install, offline, more]); layout.Controls.Add(actions);
        layout.Controls.Add(Note("Revit의 새 사용자 설치는 운영 탭만 표시합니다. 기존 연결 설치의 코드와 브랜치는 그대로 유지합니다."));
        userPage.Controls.Add(layout);
        refresh.Click += async (_, _) => await Run("설치 상태 확인", RefreshStatuses);
        check.Click += async (_, _) => await Run("업데이트 확인", CheckLatest);
        install.Click += async (_, _) => await InstallSelected(false); offline.Click += async (_, _) => await InstallSelected(true);
        userApply.Click += async (_, _) => await ApplyProfile(false);
    }

    private void BuildDeveloperPage()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
        for (var i = 0; i < 6; i++) layout.RowStyles.Add(new(i == 3 ? SizeType.Percent : SizeType.AutoSize, i == 3 ? 100 : 0));
        var identity = Row(); identity.Controls.AddRange([Note("개발자 ID"), developerId, Note("GitHub 계정명과 별개입니다.")]); layout.Controls.Add(identity);
        var branchRow = Row(); var fetch = MakeButton("브랜치 조회"); var scan = MakeButton("TEST 조회"); var local = MakeButton("로컬 Git 선택");
        branchRow.Controls.AddRange([branchBox, fetch, scan]); layout.Controls.Add(branchRow);
        devInfo.Text = "브랜치를 조회하고 TEST 패널을 선택하십시오. 설치해도 프로필을 적용하기 전에는 활성화되지 않습니다."; layout.Controls.Add(devInfo);
        var card = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new(14), Margin = new(0, 12, 0, 12) };
        card.Controls.Add(panelList); layout.Controls.Add(card);
        var buttons = Row(); var devRemove = MakeButton("TEST 제거"); var devRestore = MakeButton("이전 TEST 복원");
        var prepare = MakeButton("Reload용 TEST 준비");
        prepare.Click += async (_, _) => await InstallDeveloper(true);
        buttons.Controls.AddRange([devInstall, prepare, devApply, devRestore, devRemove, local]); layout.Controls.Add(buttons);
        layout.Controls.Add(Note("개발 설치는 원본 브랜치를 별도 내려받아 선택한 TEST 패널을 표시합니다. 운영본과 동시에 활성화하지 않습니다. 수정·커밋·Push는 원래 개발 저장소에서 진행합니다."));
        developerPage.Controls.Add(layout);
        fetch.Click += async (_, _) => await Run("개발 브랜치 확인", async () =>
        {
            var branches = await sources.RevitBranches(); branchBox.Items.Clear(); branchBox.Items.AddRange(branches.Cast<object>().ToArray());
            var saved = branches.Contains(preferences.Branch) ? preferences.Branch : branches.FirstOrDefault(x => x.Contains("/" + developerId.Text.Trim() + "/")) ?? branches.FirstOrDefault();
            if (saved is not null) branchBox.SelectedItem = saved;
        });
        branchBox.SelectedIndexChanged += (_, _) => { developerSource = null; loadedBranch = null; panelList.Items.Clear(); devInfo.Text = "TEST 조회를 눌러 선택한 브랜치의 패널을 확인하십시오."; };
        scan.Click += async (_, _) => await Run("TEST 도구 확인", async () =>
        {
            if (branchBox.SelectedItem is not string branch) throw new IOException("브랜치를 먼저 선택하십시오.");
            developerSource = await sources.DownloadRevit(branch); loadedBranch = branch;
            panelList.Items.Clear();
            foreach (var panel in RevitPackages.Panels(developerSource.Path))
                panelList.Items.Add(panel, panel.Commands > 0 && (panel.Id.Equals("TEST_" + developerId.Text.Trim(), StringComparison.OrdinalIgnoreCase) || (preferences.Branch == branch && preferences.Panels?.Contains(panel.Name) == true)));
            devInfo.Text = developerSource.Version + "\n" + panelList.Items.Count + "개 패널 · 도구가 0개인 패널은 아직 설치할 TEST가 없습니다.";
            SavePreferences();
        });
        local.Click += async (_, _) =>
        {
            using var picker = new FolderBrowserDialog { Description = "RoBIM-pyrevit Git 작업 폴더 선택", UseDescriptionForTitle = true };
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            await Run("로컬 TEST 확인", async () =>
            {
                await EnvironmentProbe.ValidateExternal(picker.SelectedPath);
                var branch = await ProcessRunner.GitRequired(picker.SelectedPath, "branch", "--show-current");
                RevitPackages.ValidateBranch(branch);
                var version = await EnvironmentProbe.ExternalVersion(picker.SelectedPath);
                var dirty = (await ProcessRunner.GitRequired(picker.SelectedPath, "status", "--porcelain", "--untracked-files=normal")).Length > 0;
                if (!branchBox.Items.Contains(branch)) branchBox.Items.Add(branch);
                branchBox.SelectedItem = branch;
                developerSource = new(picker.SelectedPath, version + (dirty ? " · 로컬 수정 포함" : " · 로컬")); loadedBranch = branch;
                panelList.Items.Clear();
                foreach (var panel in RevitPackages.Panels(picker.SelectedPath)) panelList.Items.Add(panel, panel.Commands > 0 && panel.Id.Equals("TEST_" + developerId.Text.Trim(), StringComparison.OrdinalIgnoreCase));
                devInfo.Text = developerSource.Version + "\n원본은 읽기만 합니다. Commit / Push는 자동 실행하지 않습니다.";
                SavePreferences();
            });
        };
        panelList.ItemCheck += (_, e) => { if (panelList.Items[e.Index] is TestPanel { Commands: 0 }) e.NewValue = CheckState.Unchecked; };
        devInstall.Click += async (_, _) => await InstallDeveloper(); devApply.Click += async (_, _) => await ApplyProfile(true);
        devRemove.Click += async (_, _) =>
        {
            if (!Confirm("관리자가 설치한 TEST 사본을 제거합니까?\n활성 개발자 프로필이라면 먼저 사용자 프로필을 적용하십시오.")) return;
            await Run("TEST 제거", async () =>
            {
                if (RevitProfiles.EnabledFromUserIni("RoBIM-Developer")) throw new IOException("사용자 프로필을 먼저 적용하십시오.");
                await Task.Run(() => installer.Remove(Catalog.RevitDeveloper)); await RefreshProfileLabel();
            });
        };
        devRestore.Click += async (_, _) => await Run("TEST 복원", async () =>
        {
            var backup = installer.Backups(Catalog.RevitDeveloper).FirstOrDefault() ?? throw new IOException("복원할 TEST 백업이 없습니다.");
            if (Confirm(backup.Record.Version + "으로 복원합니까?")) await Task.Run(() => installer.Restore(Catalog.RevitDeveloper, backup));
            await RefreshProfileLabel();
        });
    }

    private void BuildAccountPage()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        panel.Controls.Add(accountStatus);
        var selectRow = Row(); var selectAccount = MakeButton("선택 계정 연결");
        selectRow.Controls.AddRange([Note("저장 계정"), savedAccounts, selectAccount]); panel.Controls.Add(selectRow);
        selectAccount.Click += async (_, _) => await Run("GitHub 계정 선택", async () =>
        {
            if (savedAccounts.SelectedItem is not string loginName) throw new IOException("저장된 계정을 선택하십시오.");
            await GitHubSession.UseBrowser(loginName); await RefreshAccountAndCatalog();
        });
        panel.Controls.Add(Note("GitHub 로그인은 브라우저에서 진행합니다. 로그인 정보는 Windows 자격 증명 관리자에 저장되어 다음 실행에도 사용됩니다."));
        var buttons = Row(); var login = MakeButton("GitHub 로그인 / 저장", true); var verify = MakeButton("저장된 연결 확인");
        buttons.Controls.AddRange([login, verify]); panel.Controls.Add(buttons);
        var tokenRow = Row(); var saveToken = MakeButton("토큰 확인 및 저장"); var deleteToken = MakeButton("현재 토큰 삭제");
        tokenRow.Controls.AddRange([Note("토큰(PAT)"), tokenInput, saveToken, deleteToken]); panel.Controls.Add(tokenRow);
        panel.Controls.Add(Note("토큰은 확인 후 Windows 자격 증명 관리자에 저장합니다. 토큰이 속한 실제 GitHub ID를 표시하며 선택한 저장소의 Metadata / Contents 읽기 권한이 필요합니다."));
        saveToken.Click += async (_, _) =>
        {
            var entered = tokenInput.Text.Trim(); tokenInput.Clear();
            try { await Run("토큰 확인 및 저장", async () => { await GitHubSession.UseToken(entered); await RefreshAccountAndCatalog(); }); }
            finally { entered = ""; tokenInput.Clear(); }
        };
        deleteToken.Click += async (_, _) => await Run("현재 토큰 삭제", async () =>
        { GitHubSession.RemoveToken(); extensionGrid.Rows.Clear(); await RefreshAccount(); });
        panel.Controls.Add(Note("사용자 설정이나 pyRevit INI에 토큰을 기록하지 않습니다. 계정 삭제는 Windows 자격 증명 관리자에서 처리할 수 있습니다."));
        var diagnostic = MakeButton("작업 기록 보기"); diagnostic.Click += (_, _) =>
        {
            using var dialog = new Form { Text = "작업 기록", Size = new Size(850, 450), StartPosition = FormStartPosition.CenterParent };
            dialog.Controls.Add(new TextBox { Text = log.Text, Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical }); dialog.ShowDialog(this);
        };
        panel.Controls.Add(diagnostic); accountPage.Controls.Add(panel);
        var bridge = MakeButton("Revit Reload 연결 설치");
        bridge.Click += async (_, _) => await Run("Revit Reload 연결 설치", async () =>
        {
            _ = await EnvironmentProbe.RevitRoots();
            await Task.Run(() => ReloadBridge.Install(paths, Path.Combine(AppContext.BaseDirectory, "RoBIM-ReloadBridge.zip")));
            MessageBox.Show(this, "Reload 연결을 설치했습니다. Revit에서 pyRevit Reload를 한 번 누르십시오.\nRoBIM TEST → RoBIM 연결 TEST → 적용 및 Reload 버튼이 표시됩니다.", Text);
        });
        panel.Controls.Add(bridge);
        panel.Controls.Add(Note("Revit을 켜 둔 채 파일을 준비한 뒤, Revit 안의 적용 및 Reload 버튼에서 반영합니다. DLL 변경은 재시작이 필요합니다."));
        login.Click += async (_, _) => await Run("GitHub 로그인", async () => { await GitHubAccount.LoginAndSave(); await RefreshAccountAndCatalog(); });
        verify.Click += async (_, _) => await Run("저장된 연결 확인", RefreshAccountAndCatalog);
    }

    private void SwitchPage(string id)
    {
        if (id == "User") id = SelectedStatus()?.Tool.Kind == ToolKind.AutoCAD ? "Cad" : "Revit";
        currentPage = id;
        userPage.Visible = false; developerPage.Visible = id == "Developer"; accountPage.Visible = id == "Account";
        extensionsPage.Visible = id == "Extensions";
        productsPage.Visible = id is "Revit" or "Cad";
        profileLabel.Visible = true;
        _ = RefreshProfileLabel();
        pageTitle.Text = id == "Revit" ? "Revit" : id == "Cad" ? "AutoCAD" : id == "Developer" ? "개발자 TEST" : id == "Extensions" ? "도구 추가" : "설정 / GitHub";
        pageSubtitle.Text = id is "Revit" or "Cad" ? "도구를 선택하고 아래 버튼을 누르세요." : id == "Developer" ? "개발 브랜치에서 개인 TEST 도구를 골라 설치합니다." : id == "Extensions" ? "다른 도구가 필요할 때 Git URL을 등록하세요." : "처음 한 번 계정을 연결하면 설치할 수 있습니다.";
        foreach (var control in navigation) control.BackColor = (string?)control.Tag == id ? Color.FromArgb(42, 67, 105) : Color.FromArgb(23, 36, 58);
        RefreshProductList();
    }

    public void PreviewPage(string mode)
    {
        if (!preview) return;
        if (mode == "Developer")
        {
            branchBox.Items.Add("codex/jm/test-tool"); branchBox.SelectedIndex = 0;
            panelList.Items.Add(new TestPanel("TEST_jm.panel", 3), true);
            panelList.Items.Add(new TestPanel("TEST_JH.panel", 2));
            panelList.Items.Add(new TestPanel("TEST_gagaggan.panel", 1));
            devInfo.Text = "화면 확인용 예시 · 개발 브랜치와 개인 패널을 선택해 설치합니다.";
        }
        if (mode == "Account") accountStatus.Text = "저장된 계정  ·  예시 사용자";
        if (mode == "Extensions") ShowExtensionsPreview();
        SwitchPage(mode);
    }

    private async Task RefreshAccount()
    {
        try
        {
            savedAccounts.Items.Clear();
            try { savedAccounts.Items.AddRange((await GitHubAccount.SavedAccounts()).Cast<object>().ToArray()); } catch (IOException) { }
            var identity = await GitHubSession.Identify();
            authenticated = true; connectedLogin = identity.Login;
            accountStatus.Text = "연결된 GitHub ID  ·  " + identity.Login + "  /  " + (identity.Mode == "Token" ? "토큰 인증" : "브라우저 계정");
            catalogIdentity.Text = accountStatus.Text;
            if (savedAccounts.Items.Contains(identity.Login)) savedAccounts.SelectedItem = identity.Login;
        }
        catch (Exception) { authenticated = false; connectedLogin = ""; accountStatus.Text = "GitHub 연결 확인 필요"; catalogIdentity.Text = "설정 / GitHub에서 계정 또는 토큰을 연결하십시오."; }
        RefreshProductAction();
    }

    private Task RefreshProfileLabel()
    {
        if (currentPage == "Cad") { profileLabel.Text = "AutoCAD 2026 · 설치 후 DHNET 실행"; return Task.CompletedTask; }
        if (currentPage is "Extensions" or "Account") { profileLabel.Text = "필요할 때만 사용하는 추가 설정입니다."; return Task.CompletedTask; }
        if (preview) { profileLabel.Text = "Revit 적용 프로필  ·  사용자"; return Task.CompletedTask; }
        var developer = RevitProfiles.EnabledFromUserIni("RoBIM-Developer");
        profileLabel.Text = developer ? "Revit 적용 프로필  ·  개발자 TEST — Reload 또는 재시작 후 반영" : "Revit 적용 프로필  ·  사용자 / 기존 설치";
        return Task.CompletedTask;
    }

    private void SavePreferences()
    {
        var id = developerId.Text.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[A-Za-z0-9_-]{1,30}$")) throw new IOException("개발자 ID에는 영문·숫자·밑줄·하이픈을 사용하십시오.");
        preferences = new(id, loadedBranch ?? "", panelList.CheckedItems.Cast<TestPanel>().Select(x => x.Name).ToArray());
        SafeFiles.NoLinks(paths.Data); Directory.CreateDirectory(paths.Data);
        var file = Path.Combine(paths.Data, "ui-preferences.json"); SafeFiles.NoLinks(file);
        File.WriteAllText(file, JsonSerializer.Serialize(preferences));
    }

    private async Task InstallDeveloper(bool prepareReload = false)
    {
        if (developerSource is null || branchBox.SelectedItem as string != loadedBranch) { MessageBox.Show(this, "선택한 브랜치의 TEST를 먼저 조회하십시오.", Text); return; }
        var selected = panelList.CheckedItems.Cast<TestPanel>().Select(x => x.Name).ToArray();
        if (selected.Length == 0) { MessageBox.Show(this, "설치할 TEST 패널을 선택하십시오.", Text); return; }
        if (!Confirm((prepareReload ? "Revit 안에서 적용할 TEST 사본을 준비합니다." : "개발 TEST 사본을 설치합니다.") + "\n브랜치: " + loadedBranch + "\n패널: " + string.Join(", ", selected) + "\n운영 설치와 개발 원본은 유지합니다. 프로필 적용은 별도입니다.")) return;
        await Run("TEST 설치", async () =>
        {
            SavePreferences(); _ = await EnvironmentProbe.RevitRoots();
            if (!prepareReload) EnvironmentProbe.EnsureHostClosed(Catalog.RevitDeveloper);
            var output = SafeFiles.Child(paths.Data, "Prepared/" + Guid.NewGuid().ToString("N"));
            var package = await Task.Run(() => RevitPackages.Prepare(developerSource, output, true, selected));
            if (prepareReload)
            {
                await Task.Run(() => new RevitReload(paths).Prepare(Catalog.RevitDeveloper, package));
                ShowReloadReady();
            }
            else await Task.Run(() => installer.Install(Catalog.RevitDeveloper, package));
            WriteLog((prepareReload ? "TEST 준비 완료: " : "TEST 설치 완료: ") + package.Version);
            await RefreshProfileLabel();
        });
    }

    private async Task ApplyProfile(bool developer)
    {
        if (!Confirm((developer ? "개발자 TEST" : "사용자") + " 프로필을 적용합니까?\nRevit 실행 중에는 적용 요청만 준비하며 Revit 안의 적용 및 Reload 버튼에서 전환합니다. 다른 RoBIM 프로필 파일은 보존합니다.")) return;
        await Run("Revit 프로필 적용", async () =>
        {
            _ = await EnvironmentProbe.RevitRoots();
            if (ReloadBridge.RevitRunning())
            {
                new RevitReload(paths).PrepareProfile(developer); ShowReloadReady(); return;
            }
            if (developer)
            {
                var status = installer.Status(Catalog.RevitDeveloper);
                if (!status.Managed || status.Modified) throw new IOException("정상적인 관리 TEST 설치가 필요합니다.");
            }
            else if (!Directory.Exists(paths.Target(Catalog.Revit)) && await EnvironmentProbe.ExternalRevit(paths.Target(Catalog.Revit)) is null)
                throw new IOException("사용자용 RoBIM 설치를 먼저 연결하거나 설치하십시오.");
            await new RevitProfiles(paths).Apply(developer); await RefreshProfileLabel();
            WriteLog("프로필 적용 완료. Revit을 다시 시작하십시오.");
        });
    }
}
