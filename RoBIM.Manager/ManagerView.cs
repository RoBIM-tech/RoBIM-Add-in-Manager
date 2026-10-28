using System.Drawing;
using System.IO;
using System.Text.Json;
using RoBIM.Manager.Core;

namespace RoBIM.Manager;

public sealed partial class ManagerForm
{
    private readonly Panel content = new() { Dock = DockStyle.Fill, Padding = new(26, 22, 26, 16), BackColor = Color.FromArgb(246, 247, 249) };
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
        var sidebar = new Panel { Dock = DockStyle.Left, Width = 188, BackColor = Color.White, Padding = new(12, 20, 12, 16) };
        var nav = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 590, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        nav.Controls.Add(new Label { Text = "WORKSPACE", AutoSize = true, ForeColor = Color.FromArgb(112, 123, 140), Font = new Font(Font.FontFamily, 8), Margin = new(14, 0, 0, 12) });
        foreach (var (id, title) in new[] { ("All", "전체 도구"), ("Mine", "내 도구"), ("Revit", "Revit 독립 Add-in"), ("PyRevit", "Revit · pyRevit"), ("Cad", "AutoCAD"), ("Navisworks", "Navisworks"), ("Test", "TEST 도구"), ("Ribbon", "리본 편집"), ("Locations", "설치 위치"), ("Extensions", "도구 등록"), ("Developer", "기존 pyRevit TEST"), ("Account", "설정 / GitHub") })
        {
            var button = new Button { Text = title, Tag = id, Width = 162, Height = 44, FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleLeft, Padding = new(14, 0, 0, 0), Margin = new(0, 0, 0, 5), ForeColor = Color.FromArgb(70, 79, 94), BackColor = Color.White };
            button.FlatAppearance.BorderSize = 0;
            button.Click += (_, _) => SwitchPage(id);
            if (!preview && RevitRuntimePolicy.NativeOnly(paths) && id is "PyRevit" or "Developer" or "Locations") button.Visible = false;
            navigation.Add(button); nav.Controls.Add(button);
        }
        sidebar.Controls.Add(nav);
        sidebar.Controls.Add(new Label { Text = "RoBIM Manager\n" + Application.ProductVersion.Split('+')[0], ForeColor = Color.FromArgb(112, 123, 140), AutoSize = true, Dock = DockStyle.Bottom, Padding = new(8) });
        var brand = new Panel { Dock = DockStyle.Top, Height = 62, BackColor = Color.White, Padding = new(24, 12, 24, 12) };
        brand.Controls.Add(new Label { Text = "RoBIM", Font = new Font(Font.FontFamily, 18, FontStyle.Bold), ForeColor = Color.FromArgb(49, 92, 164), AutoSize = true, Location = new(24, 12) });
        brand.Controls.Add(new Label { Text = "Add-in Manager", Font = new Font(Font.FontFamily, 12), ForeColor = Color.FromArgb(55, 65, 81), AutoSize = true, Location = new(127, 20) });
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
        shell.RowStyles.Add(new(SizeType.AutoSize)); shell.RowStyles.Add(new(SizeType.AutoSize));
        shell.RowStyles.Add(new(SizeType.Percent, 100)); shell.RowStyles.Add(new(SizeType.AutoSize));
        var header = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new(0, 0, 0, 12) };
        header.Controls.AddRange([pageTitle, pageSubtitle]); shell.Controls.Add(header, 0, 0);
        profileLabel.Font = new Font(Font.FontFamily, 9);
        shell.Controls.Add(profileLabel, 0, 1);
        var pages = new Panel { Dock = DockStyle.Fill };
        pages.Controls.AddRange([userPage, developerPage, accountPage, extensionsPage, productsPage, ribbonPage, locationsPage]); shell.Controls.Add(pages, 0, 2);
        footer.Font = new Font(Font.FontFamily, 9);
        footer.ForeColor = Color.FromArgb(110, 120, 135); shell.Controls.Add(footer, 0, 3);
        content.Controls.Add(shell);
        var workspace = new Panel { Dock = DockStyle.Fill };
        workspace.Controls.Add(content); workspace.Controls.Add(sidebar);
        Controls.Add(workspace); Controls.Add(brand);
        BuildUserPage(); BuildDeveloperPage(); BuildAccountPage(); BuildExtensionsPage(); BuildProductsPage(); BuildRibbonPage(); BuildLocationsPage();
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
            RequireChannelAccess("test");
            var backup = installer.Backups(Catalog.RevitDeveloper).FirstOrDefault() ?? throw new IOException("복원할 TEST 백업이 없습니다.");
            if (Confirm(backup.Record.Version + "으로 복원합니까?")) await Task.Run(() => installer.Restore(Catalog.RevitDeveloper, backup));
            await RefreshProfileLabel();
        });
    }

    private void BuildAccountPage()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        panel.Controls.Add(accountStatus);
        BuildAccountOptions(panel);
        var accountActions = Row();
        var logout = MakeButton("로그아웃"); logout.Click += (_, _) => SignOut();
        accountActions.Controls.AddRange([accountSwitchButton, logout]); panel.Controls.Add(accountActions);
        panel.Controls.Add(accountSwitchPanel);
        accountSwitchButton.Click += (_, _) =>
        {
            accountSwitchPanel.Visible = !accountSwitchPanel.Visible;
            accountSwitchButton.Text = accountSwitchPanel.Visible ? "계정 변경 닫기" : "계정 변경";
        };
        var selectRow = Row(); var selectAccount = MakeButton("이 계정 사용");
        selectRow.Controls.AddRange([Note("저장 계정"), savedAccounts, selectAccount]); accountSwitchPanel.Controls.Add(selectRow);
        selectAccount.Click += async (_, _) => await Run("GitHub 계정 선택", async () =>
        {
            if (savedAccounts.SelectedItem is not string loginName) throw new IOException("저장된 계정을 선택하십시오.");
            teamAccess?.Lock(); await GitHubSession.UseBrowser(loginName); await RefreshAccountAndCatalog();
        }, requiresAccess: false);
        var login = MakeButton("다른 GitHub 계정으로 로그인", true);
        accountSwitchPanel.Controls.Add(login);
        accountSwitchPanel.Controls.Add(Note("브라우저에서 계정을 선택합니다. 로그인 정보는 Windows 자격 증명 관리자에 보관합니다."));
        panel.Controls.Add(accountAdvancedButton); panel.Controls.Add(accountAdvancedPanel);
        accountAdvancedButton.Click += (_, _) =>
        {
            accountAdvancedPanel.Visible = !accountAdvancedPanel.Visible;
            accountAdvancedButton.Text = accountAdvancedPanel.Visible ? "고급 설정 닫기" : "고급 설정";
            if (!accountAdvancedPanel.Visible) tokenInput.Clear();
        };
        var tokenRow = Row(); var saveToken = MakeButton("토큰 확인 및 저장"); var deleteToken = MakeButton("현재 토큰 삭제");
        accountAdvancedPanel.Controls.Add(Note("브라우저 로그인을 사용할 수 없을 때만 개인 액세스 토큰(PAT)을 입력하세요."));
        tokenRow.Controls.AddRange([Note("토큰(PAT)"), tokenInput, saveToken, deleteToken]); accountAdvancedPanel.Controls.Add(tokenRow);
        accountAdvancedPanel.Controls.Add(Note("토큰에는 저장소 읽기와 조직의 팀 구성원 읽기 권한(read:org 또는 조직 Members 읽기)이 필요합니다. Windows 자격 증명 관리자에만 저장합니다."));
        saveToken.Click += async (_, _) =>
        {
            var entered = tokenInput.Text.Trim(); tokenInput.Clear();
            try { await Run("토큰 확인 및 저장", async () => { teamAccess?.Lock(); await GitHubSession.UseToken(entered); await RefreshAccountAndCatalog(); }, requiresAccess: false); }
            finally { entered = ""; tokenInput.Clear(); }
        };
        deleteToken.Click += async (_, _) => await Run("현재 토큰 삭제", async () =>
        { teamAccess?.Lock(); GitHubSession.RemoveToken(); extensionGrid.Rows.Clear(); await RefreshAccount(); }, requiresAccess: false);
        var diagnostic = MakeButton("작업 기록 보기"); diagnostic.Click += (_, _) =>
        {
            using var dialog = new Form { Text = "작업 기록", Size = new Size(850, 450), StartPosition = FormStartPosition.CenterParent };
            dialog.Controls.Add(new TextBox { Text = log.Text, Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical }); dialog.ShowDialog(this);
        };
        accountAdvancedPanel.Controls.Add(diagnostic); accountPage.Controls.Add(panel);
        var bridge = MakeButton("Revit Reload 연결 설치");
        bridge.Click += async (_, _) => await Run("Revit Reload 연결 설치", async () =>
        {
            _ = await EnvironmentProbe.RevitRoots();
            await Task.Run(() => ReloadBridge.Install(paths, Path.Combine(AppContext.BaseDirectory, "RoBIM-ReloadBridge.zip")));
            MessageBox.Show(this, "Reload 연결을 설치했습니다. Revit에서 pyRevit Reload를 한 번 누르십시오.\nRoBIM TEST → RoBIM 연결 TEST → 적용 및 Reload 버튼이 표시됩니다.", Text);
        });
        var retirePython = MakeButton("기존 RoBIM pyRevit 연결 제거");
        retirePython.Enabled = !preview && !RevitRuntimePolicy.NativeOnly(paths);
        retirePython.Click += async (_, _) =>
        {
            if (!Confirm("기존 RoBIM pyRevit 버튼과 Manager용 pyRevit 연결을 백업 후 제거합니까?\n독립 Add-in과 다른 pyRevit 확장은 유지합니다. Revit을 저장하고 종료한 상태에서 실행하십시오.")) return;
            await Run("RoBIM Native 전용 전환", async () =>
            {
                var backup = await Task.Run(() =>
                {
                    EnvironmentProbe.EnsureHostClosed(Catalog.Revit);
                    var retirement = new PyRevitRetirement(paths, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pyRevit", "pyRevit_config.ini"));
                    var plan = retirement.Prepare();
                    NativeCommands().InstallHost(Path.Combine(AppContext.BaseDirectory, "RoBIM-Host-Revit2026.zip"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "Revit", "Addins", "2026"));
                    retirement.Apply(plan);
                    return Path.Combine(paths.Data, "Backups", "PyRevitRetirement", plan.Id);
                });
                foreach (var navigationItem in navigation.Where(x => x.Tag is string page && page is "PyRevit" or "Developer" or "Locations")) navigationItem.Visible = false;
                retirePython.Enabled = false; bridge.Visible = false;
                WriteLog("RoBIM pyRevit 연결 제거 완료 · 복구 백업: " + backup);
            });
            SwitchPage("Revit");
        };
        retirePython.Visible = preview || !RevitRuntimePolicy.NativeOnly(paths);
        accountAdvancedPanel.Controls.Add(retirePython);
        bridge.Visible = preview || !RevitRuntimePolicy.NativeOnly(paths);
        accountAdvancedPanel.Controls.Add(bridge);
        login.Click += async (_, _) => await Run("GitHub 로그인", async () => { teamAccess?.Lock(); await LoginGitHub(); await RefreshAccountAndCatalog(); }, requiresAccess: false);
    }

    private void SwitchPage(string id)
    {
        if (!CanUseTest && id == "Test") id = "Revit";
        if (!preview && RevitRuntimePolicy.NativeOnly(paths) && id is "PyRevit" or "Developer" or "Locations") id = "Revit";
        if (id == "User") id = SelectedStatus()?.Tool.Kind switch { ToolKind.AutoCAD => "Cad", ToolKind.PyRevit => "PyRevit", _ => "Revit" };
        currentPage = id;
        userPage.Visible = false; developerPage.Visible = id == "Developer"; accountPage.Visible = id == "Account";
        extensionsPage.Visible = id == "Extensions";
        ribbonPage.Visible = id == "Ribbon";
        locationsPage.Visible = id == "Locations";
        if (id == "Locations") ShowLocation();
        if (id == "Ribbon") RefreshRibbonEditor();
        productsPage.Visible = IsProductPage;
        profileLabel.Visible = id is "Revit" or "PyRevit" or "Cad" or "Developer";
        _ = RefreshProfileLabel();
        pageTitle.Text = id == "All" ? "전체 도구" : id == "Mine" ? "내 도구" : id == "Revit" ? "Revit 독립 Add-in" : id == "PyRevit" ? "Revit · pyRevit" : id == "Cad" ? "AutoCAD" : id == "Navisworks" ? "Navisworks" : id == "Test" ? "TEST 도구" : id == "Ribbon" ? "리본 편집" : id == "Locations" ? "설치 위치" : id == "Developer" ? "기존 pyRevit TEST" : id == "Extensions" ? "도구 등록" : "설정 / GitHub";
        pageSubtitle.Text = id == "Navisworks" ? "Navisworks 도구 · 등록된 도구가 없습니다." : id == "Test" ? "검증 중인 독립 도구입니다. 설치 상태와 TEST/실사용 구분은 별도입니다. CAD는 AutoCAD 안에서 Reload하세요." : id == "Locations" ? "RoBIM pyRevit 도구를 설치할 폴더를 선택하세요." : id == "Revit" ? "독립 Revit Add-in의 기능을 설치하고 관리하세요." : id == "PyRevit" ? "pyRevit 확장의 버튼을 설치하고 관리하세요." : id == "Ribbon" ? "Revit 탭·패널·버튼 배치를 드래그해서 구성하세요." : IsProductPage ? "필요한 도구를 찾고 한곳에서 관리하세요." : id == "Developer" ? "개발 브랜치에서 개인 TEST 도구를 골라 설치합니다." : id == "Extensions" ? "다른 도구가 필요할 때 Git URL을 등록하세요." : "GitHub 계정과 로그인·시작 방식을 관리합니다.";
        foreach (var control in navigation)
        {
            var selected = (string?)control.Tag == id;
            control.BackColor = selected ? Color.FromArgb(232, 238, 249) : Color.White;
            control.ForeColor = selected ? Color.FromArgb(49, 92, 164) : Color.FromArgb(70, 79, 94);
        }
        RenderProductCards();
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
        if (mode == "Ribbon" && ribbonEditor is not null)
        {
            ribbonEditor.LoadTools([new("demo:sheet", "시트 생성", "RoBIM 도면", "시트", true), new("demo:join", "AutoJoin", "RoBIM 기본", "요소 수정", true), new("demo:beam", "보 높이 맞춤", "RoBIM 기본", "요소 수정", true), new("demo:copy", "뷰 복제", "RoBIM 기본", "뷰", true), new("demo:rotate", "뷰 회전", "RoBIM 기본", "뷰", true), new("demo:name", "뷰 이름 변경", "RoBIM 기본", "뷰", true)]);
            ribbonEditor.LoadLayout(new(1, "Revit", [new("RoBIM 기본", [new("모델 수정", [new("demo:sheet", "시트 생성"), new("separator:demo", "구분선", Kind: "Separator"), new("demo:join", "AutoJoin", "Small", "demo-two", 2), new("demo:beam", "보 높이 맞춤", "Small", "demo-two", 2)]), new("뷰 관리", [new("demo:copy", "뷰 복제", "Small", "demo-three", 3), new("demo:rotate", "뷰 회전", "Small", "demo-three", 3), new("demo:name", "뷰 이름 변경", "Small", "demo-three", 3)])])]));
        }
    }

    private async Task RefreshAccount()
    {
        await accountRefreshGate.WaitAsync();
        checkingAccess = true;
        sessionStarted = true;
        try
        {
            savedAccounts.Items.Clear();
            try { savedAccounts.Items.AddRange((await GitHubAccount.SavedAccounts()).Cast<object>().ToArray()); } catch (IOException) { }
            var access = teamAccess is null ? null : await teamAccess.Refresh();
            if (access is { Allowed: false }) throw new IOException(access.Detail);
            var identity = access is null ? await GitHubSession.Identify() : new GitHubIdentity(access.Login, GitHubSession.Current().Mode);
            authenticated = true; connectedLogin = identity.Login;
            accountStatus.Text = "연결된 GitHub ID  ·  " + identity.Login + "  /  " + (access?.Role ?? "계정 연결");
            catalogIdentity.Text = accountStatus.Text;
            if (savedAccounts.Items.Contains(identity.Login)) savedAccounts.SelectedItem = identity.Login;
        }
        catch (Exception error) { teamAccess?.Lock(error.Message); authenticated = false; connectedLogin = ""; accountStatus.Text = error.Message; catalogIdentity.Text = error.Message; }
        finally { checkingAccess = false; accountRefreshGate.Release(); if (!IsDisposed) ApplyAccessState(); }
        if (!IsDisposed) RefreshProductAction();
        if (!IsDisposed) _ = CheckManagerUpdate();
    }

    private Task RefreshProfileLabel()
    {
        if (currentPage == "Revit") { profileLabel.Text = "독립 Add-in만 표시 · 설치·제거 준비 후 독립 Add-in Reload로 적용"; return Task.CompletedTask; }
        if (currentPage == "PyRevit") { profileLabel.Text = "pyRevit 버튼만 표시 · 설치·제거 준비 후 pyRevit Reload로 적용"; return Task.CompletedTask; }
        if (currentPage == "Cad") { profileLabel.Text = "버튼별 설치·제거 준비 → AutoCAD의 RoBIM → 관리 → Reload"; return Task.CompletedTask; }
        if (currentPage is "Extensions" or "Account" or "Locations") { profileLabel.Text = "필요할 때만 사용하는 추가 설정입니다."; return Task.CompletedTask; }
        if (preview) { profileLabel.Text = "Revit 적용 프로필  ·  사용자"; return Task.CompletedTask; }
        var developer = RevitProfiles.EnabledFromUserIni("RoBIM-Developer");
        var selected = RevitProfiles.EnabledFromUserIni("RoBIM-Selected");
        profileLabel.Text = "독립 Add-in: Revit Reload · pyRevit 버튼 선택: 적용 및 Reload · " + (selected ? "선택 프로필" : developer ? "개발자 TEST" : "사용자 / 기존 설치");
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
            RequireChannelAccess("test");
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
            if (developer) RequireChannelAccess("test");
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
