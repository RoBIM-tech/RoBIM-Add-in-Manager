using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RoBIM.Manager.Core;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using WpfDragDrop = System.Windows.DragDrop;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfPanel = System.Windows.Controls.Panel;
using WpfPoint = System.Windows.Point;
using WpfTreeView = System.Windows.Controls.TreeView;

namespace RoBIM.Manager;

public partial class Manager2Window : Window
{
    private readonly ManagerForm backend;
    private string currentHost = "revit";
    private string currentNav = "tools";
    private string toolFilter = "all";
    private bool compactRibbon = true;
    private bool syncUi;
    private bool ribbonDirty;

    private List<RibbonTool> ribbonTools = [];
    private readonly List<RibbonTabModel> ribbonTabs = [];
    private int selectedTabIndex;
    private object? selectedRibbonItem;
    private WpfPoint dragStartPoint;
    private object? dragPayload;
    private object? activeDragPayload;
    private object? dragHoverTarget;
    private Window? dragGhost;
    private DispatcherTimer? dragGhostTimer;
    private readonly Dictionary<string, string> loaderSelectedChannel = new(StringComparer.OrdinalIgnoreCase);

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Point { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Win32Point pt);

    private sealed class RibbonTabModel(string name)
    {
        public string Name { get; set; } = name;
        public List<RibbonPanelModel> Panels { get; } = [];
    }

    private sealed class RibbonPanelModel(string name)
    {
        public string Name { get; set; } = name;
        public List<object> Items { get; } = [];
    }

    private sealed class RibbonStackModel(string id, int rows)
    {
        public string Id { get; } = id;
        public int Rows { get; set; } = rows;
        public List<RibbonPlacement> Children { get; } = [];
    }

    internal Manager2Window(ManagerForm backend)
    {
        this.backend = backend;
        InitializeComponent();
        try
        {
            using var stream = typeof(ManagerForm).Assembly.GetManifestResourceStream("RoBIM.Manager.AppIcon.ico");
            if (stream is not null) Icon = BitmapFrame.Create(stream);
        }
        catch (Exception) { }

        backend.BackendChanged += () => Dispatcher.BeginInvoke(RefreshAllViews);
        backend.OpenModernUiRequested = () => Dispatcher.BeginInvoke(() =>
        {
            if (!IsVisible) Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        });
        Loaded += async (_, _) =>
        {
            await backend.StartHiddenBackendAsync();
            LoadRibbonState();
            RefreshAllViews();
        };
        Closing += (_, e) =>
        {
            if (backend.IsBusy)
            {
                e.Cancel = true;
                WpfMessageBox.Show(this, "작업이 끝난 뒤 닫아 주십시오.", Title);
                return;
            }
            if (!backend.IsExitRequested)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            backend.CloseBackend();
        };
    }

    private void RefreshAllViews()
    {
        syncUi = true;
        try
        {
            if (!backend.IsAccessAllowed && currentNav != "account")
                currentNav = "account";
            UpdateRail();
            UpdateHeader();
            if (currentNav == "tools") RenderToolsView();
            else if (currentNav == "ribbon") RenderRibbonView();
            else if (currentNav == "loaders") RenderLoadersView();
            else RenderAccountView();
            BottomStatusText.Text = backend.FooterSummaryText;
        }
        finally { syncUi = false; }
    }

    private void UpdateRail()
    {
        var isProgramScoped = currentNav is "tools" or "ribbon";
        RailHostSection.Visibility = isProgramScoped ? Visibility.Visible : Visibility.Collapsed;

        void StyleHostBtn(WpfButton btn, bool active)
        {
            btn.Background = active ? new SolidColorBrush(WpfColor.FromRgb(37, 99, 235)) : WpfBrushes.Transparent;
            btn.BorderBrush = active ? new SolidColorBrush(WpfColor.FromRgb(59, 130, 246)) : new SolidColorBrush(WpfColor.FromRgb(51, 65, 85));
            btn.Foreground = active ? WpfBrushes.White : new SolidColorBrush(WpfColor.FromRgb(203, 213, 225));
        }
        StyleHostBtn(HostRevitBtn, currentHost == "revit");
        StyleHostBtn(HostCadBtn, currentHost == "autocad");
        StyleHostBtn(HostNavisBtn, currentHost == "navisworks");

        void StyleNavBtn(WpfButton btn, bool active)
        {
            btn.Background = active ? new SolidColorBrush(WpfColor.FromRgb(51, 65, 85)) : WpfBrushes.Transparent;
            btn.Foreground = active ? WpfBrushes.White : new SolidColorBrush(WpfColor.FromRgb(148, 163, 184));
            btn.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        }
        StyleNavBtn(NavToolsBtn, currentNav == "tools");
        StyleNavBtn(NavRibbonBtn, currentNav == "ribbon");
        StyleNavBtn(NavLoadersBtn, currentNav == "loaders");
        RailAccountSettingsBtn.Background = currentNav == "account"
            ? new SolidColorBrush(WpfColor.FromRgb(51, 65, 85))
            : WpfBrushes.Transparent;
        RailAccountSettingsBtn.Foreground = currentNav == "account"
            ? WpfBrushes.White
            : new SolidColorBrush(WpfColor.FromRgb(148, 163, 184));

        NavToolsBtn.IsEnabled = backend.IsAccessAllowed;
        NavRibbonBtn.IsEnabled = backend.IsAccessAllowed;
        NavLoadersBtn.IsEnabled = backend.IsAccessAllowed;

        RailUserText.Text = backend.IsSignedIn ? backend.ConnectedLogin : "로그인 필요";
        RailRoleText.Text = backend.IsTestAllowed
            ? (backend.AllowPrereleaseChecked ? "TEST 수신 켜짐" : "TEST 권한 보유")
            : (backend.IsAccessAllowed ? "정식(Stable) 사용자" : "권한 확인 필요");

        ToolsView.Visibility = currentNav == "tools" ? Visibility.Visible : Visibility.Collapsed;
        RibbonView.Visibility = currentNav == "ribbon" ? Visibility.Visible : Visibility.Collapsed;
        LoadersView.Visibility = currentNav == "loaders" ? Visibility.Visible : Visibility.Collapsed;
        AccountView.Visibility = currentNav == "account" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateHeader()
    {
        var isProgramScoped = currentNav is "tools" or "ribbon";
        var hostTitle = currentHost == "autocad" ? "AutoCAD" : currentHost == "navisworks" ? "Navisworks" : "Revit";
        var navTitle = currentNav switch { "tools" => "도구 카탈로그", "ribbon" => "리본 편집기", "loaders" => "호스트 로더", _ => "계정 · 설정" };
        HeaderTitle.Text = isProgramScoped ? $"{hostTitle} · {navTitle}" : navTitle;

        HeaderBadge.Visibility = isProgramScoped ? Visibility.Visible : Visibility.Collapsed;
        if (isProgramScoped)
        {
            if (currentHost == "revit")
            {
                var count = backend.RevitSessionCount;
                HeaderBadgeText.Text = count > 0 ? $"Revit 연결됨 ({count}개 세션)" : "Revit 미실행";
                HeaderBadge.Background = new SolidColorBrush(count > 0 ? WpfColor.FromRgb(220, 252, 231) : WpfColor.FromRgb(241, 245, 249));
                HeaderBadgeText.Foreground = new SolidColorBrush(count > 0 ? WpfColor.FromRgb(22, 101, 52) : WpfColor.FromRgb(71, 85, 105));
            }
            else if (currentHost == "autocad")
            {
                var cad = backend.GetLoaderStatus(Catalog.Cad);
                HeaderBadgeText.Text = cad.Managed ? $"CAD 로더 v{cad.Version}" : "CAD 로더 미설치";
                HeaderBadge.Background = new SolidColorBrush(cad.Managed ? WpfColor.FromRgb(220, 252, 231) : WpfColor.FromRgb(241, 245, 249));
                HeaderBadgeText.Foreground = new SolidColorBrush(cad.Managed ? WpfColor.FromRgb(22, 101, 52) : WpfColor.FromRgb(71, 85, 105));
            }
            else
            {
                HeaderBadgeText.Text = "확장 예정";
                HeaderBadge.Background = new SolidColorBrush(WpfColor.FromRgb(241, 245, 249));
                HeaderBadgeText.Foreground = new SolidColorBrush(WpfColor.FromRgb(71, 85, 105));
            }
        }

        var showBatch = currentNav == "tools" && currentHost is "revit" or "autocad" && backend.IsAccessAllowed;
        HeaderBatchUpdateBtn.Visibility = showBatch ? Visibility.Visible : Visibility.Collapsed;
        HeaderRefreshBtn.Visibility = currentNav is "tools" or "ribbon" or "loaders" ? Visibility.Visible : Visibility.Collapsed;
        HeaderRefreshBtn.IsEnabled = !backend.IsBusy;
        HeaderLogsBtn.IsEnabled = !backend.IsBusy;
        HeaderManualReloadBtn.Visibility = isProgramScoped && currentHost == "revit" && backend.IsAccessAllowed ? Visibility.Visible : Visibility.Collapsed;
        HeaderManualReloadBtn.IsEnabled = !backend.IsBusy;
        if (showBatch)
        {
            var batchCount = backend.GetBatchUpdateCount(currentHost);
            HeaderBatchUpdateBtn.Content = batchCount > 0 ? $"일괄 업데이트 ({batchCount})" : "일괄 업데이트";
            HeaderBatchUpdateBtn.IsEnabled = !backend.IsBusy && batchCount > 0;
        }
    }

    private void RenderToolsView()
    {
        ToolSearchHint.Visibility = string.IsNullOrEmpty(ToolSearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        FilterTestBtn.Visibility = backend.IsTestAllowed ? Visibility.Visible : Visibility.Collapsed;
        ToolsCadConfigBtn.Visibility = currentHost == "autocad" ? Visibility.Visible : Visibility.Collapsed;
        ToolsCadConfigBtn.IsEnabled = !backend.IsBusy && backend.IsCadHostConfigurable;
        ToolsImportZipBtn.Visibility = currentHost is "revit" or "autocad" ? Visibility.Visible : Visibility.Collapsed;
        ToolsImportZipBtn.IsEnabled = !backend.IsBusy;

        void StyleFilter(WpfButton b, bool active)
        {
            b.Background = active ? new SolidColorBrush(WpfColor.FromRgb(30, 91, 166)) : WpfBrushes.White;
            b.Foreground = active ? WpfBrushes.White : new SolidColorBrush(WpfColor.FromRgb(30, 41, 59));
            b.BorderBrush = active ? new SolidColorBrush(WpfColor.FromRgb(30, 91, 166)) : new SolidColorBrush(WpfColor.FromRgb(203, 213, 225));
        }
        StyleFilter(FilterAllBtn, toolFilter == "all");
        StyleFilter(FilterInstalledBtn, toolFilter == "installed");
        StyleFilter(FilterUninstalledBtn, toolFilter == "uninstalled");
        StyleFilter(FilterUpdateBtn, toolFilter == "update");
        StyleFilter(FilterTestBtn, toolFilter == "test");

        ToolsCardsPanel.Children.Clear();
        if (currentHost == "navisworks")
        {
            ToolsEmptyNotice.Text = "Navisworks 도구 카탈로그는 향후 호스트 연동 시 여기에 표시됩니다.";
            ToolsEmptyNotice.Visibility = Visibility.Visible;
            return;
        }

        var query = ToolSearchBox.Text.Trim();
        var filtered = backend.GetCardsForHost(currentHost).Where(c =>
        {
            var installed = c.Selected || c.Pending;
            var appearance = backend.GetCardAppearance(c);
            if (toolFilter == "installed" && !installed) return false;
            if (toolFilter == "uninstalled" && installed) return false;
            if (toolFilter == "update" && !appearance.Update) return false;
            if (toolFilter == "test" && c.Channel != "test" && !backend.HasTestOfferForCard(c)) return false;
            return MatchesKoreanSearch(c.Title + " " + c.Description, query);
        });

        var cards = ToolSortBox.SelectedIndex switch
        {
            1 => filtered.OrderBy(c => c.Title, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            2 => filtered.OrderByDescending(c => c.Selected || c.Pending).ThenBy(c => c.Title, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            3 => filtered.OrderByDescending(c => backend.GetCardAppearance(c).Update).ThenBy(c => c.Title, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            _ => filtered.ToArray()
        };

        ToolsEmptyNotice.Text = "조건에 맞는 도구가 없습니다.";
        ToolsEmptyNotice.Visibility = cards.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var card in cards)
            ToolsCardsPanel.Children.Add(BuildToolCard(card));
    }

    private Border BuildToolCard(ManagerForm.ButtonCard card)
    {
        var border = new Border
        {
            Width = 330,
            MinHeight = 156,
            Margin = new Thickness(0, 0, 14, 14),
            Padding = new Thickness(16),
            Background = WpfBrushes.White,
            BorderBrush = new SolidColorBrush(WpfColor.FromRgb(226, 232, 240)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };

        var root = new DockPanel { LastChildFill = true };

        // Bottom status + action bar
        var bottom = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(bottom, Dock.Bottom);

        var permitted = backend.IsRepositoryPermitted(card);
        var appearance = backend.GetCardAppearance(card);
        var isLegacy = backend.IsLegacyNativeCard(card);
        var hasNewTest = backend.HasTestOfferForCard(card);
        var updateOffer = backend.GetUpdateOfferForCard(card);
        var hasUpdate = appearance.Update || updateOffer is not null;

        var badgeBg = !permitted
            ? WpfColor.FromRgb(254, 226, 226)
            : isLegacy
                ? WpfColor.FromRgb(254, 243, 199)
                : card.Selected
                    ? (hasUpdate ? WpfColor.FromRgb(254, 243, 199) : WpfColor.FromRgb(220, 252, 231))
                    : (card.Channel == "test" ? WpfColor.FromRgb(237, 233, 254) : WpfColor.FromRgb(241, 245, 249));
        var badgeFg = !permitted
            ? WpfColor.FromRgb(185, 28, 28)
            : isLegacy
                ? WpfColor.FromRgb(146, 64, 14)
                : card.Selected
                    ? (hasUpdate ? WpfColor.FromRgb(146, 64, 14) : WpfColor.FromRgb(22, 101, 52))
                    : (card.Channel == "test" ? WpfColor.FromRgb(109, 40, 217) : WpfColor.FromRgb(71, 85, 105));
        var badgeLabel = !permitted
            ? (card.Selected ? $"● 설치됨 (권한 제한)" : "권한 필요")
            : isLegacy
                ? "▲ 전환 필요"
                : card.Selected
                    ? (hasUpdate ? $"▲ {card.SelectedVersion} → {updateOffer?.Version ?? "업데이트"}" : $"● 설치됨 · {card.SelectedVersion}")
                    : (card.Channel == "test" ? "◆ TEST" : card.Pending ? "◷ 준비됨" : "○ 미설치");

        var badge = new Border { Background = new SolidColorBrush(badgeBg), CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 3, 8, 3), VerticalAlignment = VerticalAlignment.Center };
        badge.Child = new TextBlock { Text = badgeLabel, FontSize = 11, FontWeight = FontWeights.Medium, Foreground = new SolidColorBrush(badgeFg) };
        DockPanel.SetDock(badge, Dock.Left);
        bottom.Children.Add(badge);

        var btnStack = new StackPanel { Orientation = WpfOrientation.Horizontal, HorizontalAlignment = WpfHorizontalAlignment.Right };
        if (!permitted)
        {
            if (card.Selected)
            {
                var remBtn = new WpfButton { Content = "제거", Style = (Style)FindResource("SecondaryButton"), Padding = new Thickness(10, 4, 10, 4), FontSize = 12, IsEnabled = !backend.IsBusy };
                remBtn.Click += async (_, _) => await RunBusyActionAsync(() => backend.ToggleCardFromModernAsync(card, false), "도구 제거");
                btnStack.Children.Add(remBtn);
            }
            else
            {
                btnStack.Children.Add(new WpfButton { Content = "권한 필요", Style = (Style)FindResource("SecondaryButton"), Padding = new Thickness(10, 4, 10, 4), FontSize = 12, IsEnabled = false });
            }
        }
        else if (isLegacy)
        {
            var convBtn = new WpfButton { Content = "전환하기", Style = (Style)FindResource("PrimaryButton"), Padding = new Thickness(10, 4, 10, 4), FontSize = 12, IsEnabled = !backend.IsBusy };
            convBtn.Click += async (_, _) => await RunBusyActionAsync(() => backend.ToggleCardFromModernAsync(card, true), "Reload 방식 전환");
            btnStack.Children.Add(convBtn);
        }
        else if (card.Kind == ManagerForm.ButtonKind.Discover)
        {
            var loadBtn = new WpfButton { Content = "목록 확인", Style = (Style)FindResource("SecondaryButton"), Padding = new Thickness(10, 4, 10, 4), FontSize = 12, IsEnabled = !backend.IsBusy };
            loadBtn.Click += async (_, _) => await RunBusyActionAsync(() => backend.ToggleCardFromModernAsync(card), "목록 확인");
            btnStack.Children.Add(loadBtn);
        }
        else
        {
            if (hasUpdate)
            {
                var updBtn = new WpfButton { Content = "업데이트", Style = (Style)FindResource("PrimaryButton"), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0), FontSize = 12, IsEnabled = !backend.IsBusy };
                updBtn.Click += async (_, _) => await RunBusyActionAsync(() => backend.ToggleCardFromModernAsync(card, true, card.SelectedChannel == "test" ? "test" : "stable"), "도구 업데이트");
                btnStack.Children.Add(updBtn);
            }
            if (!card.Selected)
            {
                var instBtn = new WpfButton { Content = card.Channel == "test" ? "TEST 설치" : "설치하기", Style = (Style)FindResource("PrimaryButton"), Padding = new Thickness(10, 4, 10, 4), FontSize = 12, IsEnabled = !backend.IsBusy };
                instBtn.Click += async (_, _) => await RunBusyActionAsync(() => backend.ToggleCardFromModernAsync(card, true, card.Channel == "test" ? "test" : "stable"), "도구 설치");
                btnStack.Children.Add(instBtn);
            }
            else
            {
                if (hasNewTest && card.SelectedChannel != "test")
                {
                    var testBtn = new WpfButton { Content = "TEST 설치", Style = (Style)FindResource("SecondaryButton"), Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 6, 0), FontSize = 12, IsEnabled = !backend.IsBusy };
                    testBtn.Click += async (_, _) => await RunBusyActionAsync(() => backend.ToggleCardFromModernAsync(card, true, "test"), "TEST 설치");
                    btnStack.Children.Add(testBtn);
                }
                var remBtn = new WpfButton { Content = "제거", Style = (Style)FindResource("SecondaryButton"), Padding = new Thickness(10, 4, 10, 4), FontSize = 12, IsEnabled = !backend.IsBusy };
                remBtn.Click += async (_, _) => await RunBusyActionAsync(() => backend.ToggleCardFromModernAsync(card, false), "도구 제거");
                btnStack.Children.Add(remBtn);
            }
        }

        var moreBtn = new WpfButton { Content = "···", Style = (Style)FindResource("SecondaryButton"), Padding = new Thickness(7, 4, 7, 4), Margin = new Thickness(6, 0, 0, 0), FontSize = 12 };
        moreBtn.Click += (_, _) => OpenCardContextMenu(moreBtn, card, permitted);
        btnStack.Children.Add(moreBtn);
        bottom.Children.Add(btnStack);
        root.Children.Add(bottom);

        // Top header + description
        var body = new StackPanel();
        var topRow = new StackPanel { Orientation = WpfOrientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var iconBox = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(WpfColor.FromRgb(241, 245, 249)), Margin = new Thickness(0, 0, 10, 0) };
        var bmp = LoadIconBitmap(card.Icon);
        if (bmp is not null)
            iconBox.Child = new System.Windows.Controls.Image { Source = bmp, Width = 24, Height = 24, Stretch = Stretch.Uniform };
        else
            iconBox.Child = new TextBlock { Text = card.Title.Length > 0 ? card.Title[..1] : "R", FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(WpfColor.FromRgb(30, 91, 166)), HorizontalAlignment = WpfHorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        topRow.Children.Add(iconBox);

        var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titleStack.Children.Add(new TextBlock { Text = card.Title, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(WpfColor.FromRgb(15, 23, 42)), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 240 });
        titleStack.Children.Add(new TextBlock { Text = card.Group, FontSize = 11, Foreground = new SolidColorBrush(WpfColor.FromRgb(100, 116, 139)), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 240 });
        topRow.Children.Add(titleStack);
        body.Children.Add(topRow);

        body.Children.Add(new TextBlock
        {
            Text = card.Description,
            FontSize = 12,
            Foreground = new SolidColorBrush(WpfColor.FromRgb(71, 85, 105)),
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 36,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        root.Children.Add(body);
        border.Child = root;
        return border;
    }

    private static void PopulatePanelFromLayout(RibbonPanelModel pm, RibbonPanelLayout panel)
    {
        var stacks = new Dictionary<string, RibbonStackModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var btn in panel.Buttons)
        {
            if (btn.Stack is { } sid)
            {
                if (!stacks.TryGetValue(sid, out var st))
                {
                    st = new RibbonStackModel(sid, btn.StackRows > 0 ? btn.StackRows : 2);
                    stacks[sid] = st;
                    pm.Items.Add(st);
                }
                st.Children.Add(btn);
            }
            else
            {
                pm.Items.Add(btn);
            }
        }
    }

    private void LoadRibbonState()
    {
        ribbonTools = backend.GetRibbonToolsForHost(currentHost).ToList();
        var layout = backend.LoadRibbonLayoutForHost(currentHost);
        ribbonTabs.Clear();
        foreach (var tab in layout.Tabs)
        {
            var tm = new RibbonTabModel(tab.Name);
            foreach (var panel in tab.Panels)
            {
                var pm = new RibbonPanelModel(panel.Name);
                PopulatePanelFromLayout(pm, panel);
                tm.Panels.Add(pm);
            }
            ribbonTabs.Add(tm);
        }
        if (ribbonTabs.Count == 0) ribbonTabs.Add(new RibbonTabModel("RoBIM 도구"));
        selectedTabIndex = Math.Clamp(selectedTabIndex, 0, ribbonTabs.Count - 1);
        ribbonDirty = false;
    }

    private RibbonLayout SnapshotRibbonLayout()
    {
        var hostName = currentHost == "autocad" ? "AutoCAD" : currentHost == "navisworks" ? "Navisworks" : "Revit";
        var tabs = ribbonTabs.Select(t => new RibbonTabLayout(t.Name.Trim(), t.Panels.Select(p =>
        {
            var list = new List<RibbonPlacement>();
            foreach (var item in p.Items)
            {
                if (item is RibbonPlacement rp) list.Add(rp with { Size = "Large", Stack = null, StackRows = 0 });
                else if (item is RibbonStackModel st)
                {
                    if (st.Children.Count != st.Rows) throw new IOException($"{st.Rows}단 묶음({p.Name})에 버튼을 {st.Rows}개 넣으세요.");
                    list.AddRange(st.Children.Select(c => c with { Size = "Small", Stack = st.Id, StackRows = st.Rows }));
                }
            }
            return new RibbonPanelLayout(p.Name.Trim(), list.ToArray());
        }).ToArray())).ToArray();
        var layout = new RibbonLayout(1, hostName, tabs);
        RibbonLayouts.Validate(layout);
        return layout;
    }

    private void RenderRibbonView()
    {
        var editableHost = currentHost is "revit" or "autocad";
        var canAct = !backend.IsBusy && editableHost;
        RibbonApplyBtn.Content = currentHost == "autocad" ? "AutoCAD 적용" : currentHost == "navisworks" ? "Navisworks 저장" : "Revit 적용";
        RibbonApplyBtn.IsEnabled = !backend.IsBusy;
        RibbonSaveBtn.IsEnabled = !backend.IsBusy;
        RibbonReloadBtn.IsEnabled = !backend.IsBusy;
        RibbonDefaultsBtn.IsEnabled = !backend.IsBusy;
        RibbonRecoverBtn.Visibility = currentHost == "revit" ? Visibility.Visible : Visibility.Collapsed;
        RibbonRecoverBtn.IsEnabled = canAct;
        RibbonCheckResultBtn.Visibility = currentHost == "revit" ? Visibility.Visible : Visibility.Collapsed;
        RibbonCheckResultBtn.IsEnabled = canAct;

        RibbonScaleTransform.ScaleX = compactRibbon ? 0.78 : 1.0;
        RibbonScaleTransform.ScaleY = compactRibbon ? 0.78 : 1.0;
        RibbonZoomCompactBtn.Style = (Style)FindResource(compactRibbon ? "PrimaryButton" : "SecondaryButton");
        RibbonZoomNormalBtn.Style = (Style)FindResource(!compactRibbon ? "PrimaryButton" : "SecondaryButton");

        RenderRibbonTabsAndStrip();
        RefreshRibbonBank();
        RefreshRibbonTree();
        RefreshRibbonInspector();
    }

    private void RenderRibbonTabsAndStrip()
    {
        RibbonTabsHost.Children.Clear();
        for (var i = 0; i < ribbonTabs.Count; i++)
        {
            var idx = i;
            var tab = ribbonTabs[i];
            var active = i == selectedTabIndex;
            var isDragHover = ReferenceEquals(dragHoverTarget, tab);
            var btn = new WpfButton
            {
                Content = tab.Name,
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 0, 3, 0),
                Background = isDragHover ? new SolidColorBrush(WpfColor.FromRgb(219, 234, 254)) : active ? WpfBrushes.White : new SolidColorBrush(WpfColor.FromRgb(226, 232, 240)),
                Foreground = active || isDragHover ? new SolidColorBrush(WpfColor.FromRgb(30, 91, 166)) : new SolidColorBrush(WpfColor.FromRgb(71, 85, 105)),
                FontWeight = active || isDragHover ? FontWeights.SemiBold : FontWeights.Normal,
                BorderThickness = new Thickness(isDragHover ? 2 : 1, isDragHover ? 2 : 1, isDragHover ? 2 : 1, 0),
                BorderBrush = new SolidColorBrush(isDragHover ? WpfColor.FromRgb(37, 99, 235) : WpfColor.FromRgb(203, 213, 225)),
                AllowDrop = true,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            btn.Click += (_, _) => { selectedTabIndex = idx; selectedRibbonItem = tab; RenderRibbonView(); };
            btn.DragOver += (_, e) =>
            {
                if (!e.Data.GetDataPresent("RoBIM.RibbonItem")) { e.Effects = System.Windows.DragDropEffects.None; e.Handled = true; return; }
                e.Effects = System.Windows.DragDropEffects.Move;
                SetDragHoverTarget(tab);
                e.Handled = true;
            };
            btn.Drop += (_, e) =>
            {
                if (e.Data.GetData("RoBIM.RibbonItem") is { } payload)
                {
                    MoveRibbonPayloadToTab(payload, tab);
                    selectedTabIndex = idx;
                    ribbonDirty = true;
                    RenderRibbonView();
                }
            };
            RibbonTabsHost.Children.Add(btn);
        }
        var addTabBtn = new WpfButton { Content = "＋ 새 탭", Padding = new Thickness(8, 5, 8, 5), Style = (Style)FindResource("SecondaryButton"), FontSize = 11, Margin = new Thickness(4, 0, 0, 3) };
        addTabBtn.Click += (_, _) =>
        {
            var newTab = new RibbonTabModel(UniqueTabName("새 탭"));
            newTab.Panels.Add(new RibbonPanelModel("도구"));
            ribbonTabs.Add(newTab);
            selectedTabIndex = ribbonTabs.Count - 1;
            selectedRibbonItem = newTab;
            ribbonDirty = true;
            RenderRibbonView();
        };
        RibbonTabsHost.Children.Add(addTabBtn);

        RibbonStripHost.Children.Clear();
        if (selectedTabIndex >= 0 && selectedTabIndex < ribbonTabs.Count)
        {
            var currentTab = ribbonTabs[selectedTabIndex];
            foreach (var panel in currentTab.Panels)
                RibbonStripHost.Children.Add(BuildPreviewPanel(panel));
        }
    }

    private Border BuildPreviewPanel(RibbonPanelModel panel)
    {
        var card = new Border
        {
            Background = WpfBrushes.White,
            BorderBrush = new SolidColorBrush(selectedRibbonItem == panel ? WpfColor.FromRgb(30, 91, 166) : WpfColor.FromRgb(203, 213, 225)),
            BorderThickness = new Thickness(selectedRibbonItem == panel ? 2 : 1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6),
            Margin = new Thickness(0, 0, 8, 0),
            AllowDrop = true
        };
        card.DragOver += (_, e) =>
        {
            if (!e.Data.GetDataPresent("RoBIM.RibbonItem")) { e.Effects = System.Windows.DragDropEffects.None; e.Handled = true; return; }
            e.Effects = System.Windows.DragDropEffects.Move;
            SetDragHoverTarget(panel);
            e.Handled = true;
        };
        card.Drop += (_, e) =>
        {
            if (e.Data.GetData("RoBIM.RibbonItem") is { } payload)
            {
                MoveRibbonPayloadToPanel(payload, panel);
                ribbonDirty = true;
                dragHoverTarget = null;
                RenderRibbonView();
                e.Handled = true;
            }
        };

        var stack = new StackPanel();
        var itemsRow = new StackPanel { Orientation = WpfOrientation.Horizontal, MinHeight = 76 };
        foreach (var item in panel.Items)
        {
            if (ReferenceEquals(dragHoverTarget, item) && !ReferenceEquals(activeDragPayload, item))
                itemsRow.Children.Add(BuildDragPreviewTile(small: false));

            if (item is RibbonPlacement rp)
            {
                if (rp.Kind == "Separator")
                {
                    var isSel = ReferenceEquals(selectedRibbonItem, rp);
                    var sep = new Border
                    {
                        Width = 11,
                        Height = 68,
                        Background = new SolidColorBrush(isSel ? WpfColor.FromRgb(219, 234, 254) : WpfColor.FromArgb(0, 0, 0, 0)),
                        BorderBrush = new SolidColorBrush(isSel ? WpfColor.FromRgb(30, 91, 166) : WpfColor.FromArgb(0, 0, 0, 0)),
                        BorderThickness = new Thickness(isSel ? 1 : 0),
                        CornerRadius = new CornerRadius(3),
                        AllowDrop = true,
                        Tag = rp,
                        Cursor = System.Windows.Input.Cursors.Hand
                    };
                    sep.Child = new Border { Width = 2, Background = new SolidColorBrush(isSel ? WpfColor.FromRgb(30, 91, 166) : WpfColor.FromRgb(203, 213, 225)), Margin = new Thickness(4, 4, 4, 4) };
                    WireRibbonDraggable(sep, rp);
                    sep.DragOver += (_, e) =>
                    {
                        if (!e.Data.GetDataPresent("RoBIM.RibbonItem")) { e.Effects = System.Windows.DragDropEffects.None; e.Handled = true; return; }
                        e.Effects = System.Windows.DragDropEffects.Move;
                        if (!ReferenceEquals(activeDragPayload, rp)) SetDragHoverTarget(rp);
                        e.Handled = true;
                    };
                    sep.Drop += (_, e) =>
                    {
                        if (e.Data.GetData("RoBIM.RibbonItem") is { } payload && !ReferenceEquals(payload, rp))
                        {
                            MoveRibbonPayloadToPanel(payload, panel, rp);
                            ribbonDirty = true;
                            dragHoverTarget = null;
                            RenderRibbonView();
                            e.Handled = true;
                        }
                    };
                    itemsRow.Children.Add(sep);
                }
                else
                {
                    itemsRow.Children.Add(BuildPreviewLargeButton(rp, panel));
                }
            }
            else if (item is RibbonStackModel st)
            {
                var isStackHover = ReferenceEquals(dragHoverTarget, st);
                var col = new Border
                {
                    Width = 144,
                    Height = 76,
                    Background = new SolidColorBrush(isStackHover || selectedRibbonItem == st ? WpfColor.FromRgb(219, 234, 254) : WpfColor.FromRgb(241, 245, 249)),
                    BorderBrush = new SolidColorBrush(isStackHover ? WpfColor.FromRgb(37, 99, 235) : selectedRibbonItem == st ? WpfColor.FromRgb(30, 91, 166) : WpfColor.FromRgb(226, 232, 240)),
                    BorderThickness = new Thickness(isStackHover ? 2 : 1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(4, 5, 4, 3),
                    Margin = new Thickness(2, 0, 2, 0),
                    AllowDrop = true
                };
                WireRibbonDraggable(col, st);
                col.DragOver += (_, e) =>
                {
                    if (activeDragPayload is RibbonPlacement cp && cp.Kind != "Separator" && st.Children.Count < st.Rows)
                    {
                        e.Effects = System.Windows.DragDropEffects.Move;
                        SetDragHoverTarget(st);
                    }
                    else
                    {
                        e.Effects = System.Windows.DragDropEffects.None;
                    }
                    e.Handled = true;
                };
                col.Drop += (_, e) =>
                {
                    if (e.Data.GetData("RoBIM.RibbonItem") is RibbonPlacement child && child.Kind != "Separator")
                    {
                        if (st.Children.Count < st.Rows)
                        {
                            RemoveRibbonPayload(child);
                            st.Children.Add(child);
                            ribbonDirty = true;
                            dragHoverTarget = null;
                            RenderRibbonView();
                        }
                        e.Handled = true;
                    }
                };
                var colStack = new StackPanel();
                foreach (var child in st.Children)
                {
                    var rowBtn = new Border
                    {
                        Height = Math.Max(19, 64 / Math.Max(1, st.Rows)),
                        Background = new SolidColorBrush(ReferenceEquals(selectedRibbonItem, child) ? WpfColor.FromRgb(191, 219, 254) : WpfColor.FromRgb(255, 255, 255)),
                        CornerRadius = new CornerRadius(3),
                        Margin = new Thickness(0, 0, 0, 2),
                        Padding = new Thickness(4, 0, 4, 0),
                        Cursor = System.Windows.Input.Cursors.Hand
                    };
                    rowBtn.Child = new TextBlock { Text = child.Title, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                    WireRibbonDraggable(rowBtn, child);
                    colStack.Children.Add(rowBtn);
                }
                if (isStackHover && activeDragPayload is RibbonPlacement && st.Children.Count < st.Rows)
                    colStack.Children.Add(BuildDragPreviewTile(small: true, stackRows: st.Rows));
                col.Child = colStack;
                itemsRow.Children.Add(col);
            }
        }
        if (ReferenceEquals(dragHoverTarget, panel) && activeDragPayload is not null)
            itemsRow.Children.Add(BuildDragPreviewTile(small: false));
        stack.Children.Add(itemsRow);
        var headerLbl = new TextBlock
        {
            Text = panel.Name,
            FontSize = 11,
            Foreground = new SolidColorBrush(WpfColor.FromRgb(100, 116, 139)),
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        WireRibbonDraggable(headerLbl, panel);
        stack.Children.Add(headerLbl);
        card.Child = stack;
        return card;
    }

    private Border BuildPreviewLargeButton(RibbonPlacement rp, RibbonPanelModel panel)
    {
        var tool = ribbonTools.FirstOrDefault(x => x.Id == rp.Id);
        var btn = new Border
        {
            Width = 86,
            Height = 76,
            Margin = new Thickness(2, 0, 2, 0),
            Padding = new Thickness(4),
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(ReferenceEquals(selectedRibbonItem, rp) ? WpfColor.FromRgb(219, 234, 254) : WpfColor.FromRgb(248, 250, 252)),
            BorderBrush = new SolidColorBrush(ReferenceEquals(selectedRibbonItem, rp) ? WpfColor.FromRgb(30, 91, 166) : WpfColor.FromRgb(226, 232, 240)),
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            AllowDrop = true
        };
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var bmp = LoadIconBitmap(tool?.Icon);
        if (bmp is not null)
            sp.Children.Add(new System.Windows.Controls.Image { Source = bmp, Width = 28, Height = 28, Margin = new Thickness(0, 0, 0, 4) });
        else
            sp.Children.Add(new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(WpfColor.FromRgb(226, 232, 240)), Margin = new Thickness(0, 0, 0, 4), Child = new TextBlock { Text = rp.Title.Length > 0 ? rp.Title[..1] : "R", HorizontalAlignment = WpfHorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Bold } });
        sp.Children.Add(new TextBlock { Text = rp.Title, FontSize = 11, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxHeight = 30, TextTrimming = TextTrimming.CharacterEllipsis });
        btn.Child = sp;

        WireRibbonDraggable(btn, rp);
        btn.DragOver += (_, e) =>
        {
            if (!e.Data.GetDataPresent("RoBIM.RibbonItem")) { e.Effects = System.Windows.DragDropEffects.None; e.Handled = true; return; }
            e.Effects = System.Windows.DragDropEffects.Move;
            if (!ReferenceEquals(activeDragPayload, rp)) SetDragHoverTarget(rp);
            e.Handled = true;
        };
        btn.Drop += (_, e) =>
        {
            if (e.Data.GetData("RoBIM.RibbonItem") is { } payload && !ReferenceEquals(payload, rp))
            {
                MoveRibbonPayloadToPanel(payload, panel, rp);
                ribbonDirty = true;
                dragHoverTarget = null;
                RenderRibbonView();
                e.Handled = true;
            }
        };
        return btn;
    }

    private void WireRibbonDraggable(FrameworkElement element, object model)
    {
        element.PreviewMouseLeftButtonDown += (_, e) =>
        {
            dragStartPoint = e.GetPosition(null);
            dragPayload = model;
        };
        element.MouseLeftButtonDown += (_, e) =>
        {
            selectedRibbonItem = model;
            e.Handled = true;
            syncUi = true;
            try
            {
                SelectTreeItemByTag(RibbonTree.Items, model);
                RenderRibbonTabsAndStrip();
                RefreshRibbonInspector();
            }
            finally { syncUi = false; }
        };
        element.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || dragPayload is null) return;
            var diff = e.GetPosition(null) - dragStartPoint;
            if (Math.Abs(diff.X) > 6 || Math.Abs(diff.Y) > 6)
            {
                var payload = dragPayload;
                dragPayload = null;
                StartRibbonDrag(element, payload);
            }
        };
    }

    private string DescribeRibbonPayload(object payload) => payload switch
    {
        RibbonTabModel t => "탭: " + t.Name,
        RibbonPanelModel p => "패널: " + p.Name,
        RibbonStackModel s => $"{s.Rows}단 묶음",
        RibbonPlacement rp => rp.Kind == "Separator" ? "구분선" : rp.Title,
        _ => "항목"
    };

    private void SetDragHoverTarget(object? target)
    {
        if (ReferenceEquals(dragHoverTarget, target)) return;
        dragHoverTarget = target;
        RenderRibbonTabsAndStrip();
    }

    private Border BuildDragPreviewTile(bool small, int stackRows = 2)
    {
        var label = activeDragPayload is null ? "여기에 놓기" : DescribeRibbonPayload(activeDragPayload);
        if (small)
        {
            return new Border
            {
                Height = Math.Max(19, 64 / Math.Max(1, stackRows)),
                Background = new SolidColorBrush(WpfColor.FromArgb(220, 219, 234, 254)),
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(37, 99, 235)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 0, 0, 2),
                Padding = new Thickness(4, 0, 4, 0),
                IsHitTestVisible = false,
                Child = new TextBlock { Text = label, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(WpfColor.FromRgb(30, 64, 175)), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }
            };
        }

        if (activeDragPayload is RibbonPlacement { Kind: "Separator" })
        {
            return new Border
            {
                Width = 11,
                Height = 68,
                Background = new SolidColorBrush(WpfColor.FromArgb(200, 219, 234, 254)),
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(37, 99, 235)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(3),
                IsHitTestVisible = false,
                Child = new Border { Width = 2, Background = new SolidColorBrush(WpfColor.FromRgb(37, 99, 235)), Margin = new Thickness(4) }
            };
        }

        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (activeDragPayload is RibbonPlacement rp)
        {
            var tool = ribbonTools.FirstOrDefault(x => x.Id == rp.Id);
            var bmp = LoadIconBitmap(tool?.Icon);
            if (bmp is not null)
                sp.Children.Add(new System.Windows.Controls.Image { Source = bmp, Width = 28, Height = 28, Margin = new Thickness(0, 0, 0, 4), Opacity = 0.85 });
            else
                sp.Children.Add(new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(WpfColor.FromRgb(191, 219, 254)), Margin = new Thickness(0, 0, 0, 4), Child = new TextBlock { Text = rp.Title.Length > 0 ? rp.Title[..1] : "R", HorizontalAlignment = WpfHorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(WpfColor.FromRgb(30, 64, 175)) } });
        }
        sp.Children.Add(new TextBlock { Text = label, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(WpfColor.FromRgb(30, 64, 175)), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxHeight = 30, TextTrimming = TextTrimming.CharacterEllipsis });
        return new Border
        {
            Width = 86,
            Height = 76,
            Margin = new Thickness(2, 0, 2, 0),
            Padding = new Thickness(4),
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(WpfColor.FromArgb(220, 219, 234, 254)),
            BorderBrush = new SolidColorBrush(WpfColor.FromRgb(37, 99, 235)),
            BorderThickness = new Thickness(1.5),
            IsHitTestVisible = false,
            Child = sp
        };
    }

    private void StartRibbonDrag(FrameworkElement source, object payload)
    {
        activeDragPayload = payload;
        dragHoverTarget = null;
        ShowDragGhost(DescribeRibbonPayload(payload));
        try
        {
            var dataObj = new System.Windows.DataObject("RoBIM.RibbonItem", payload);
            WpfDragDrop.DoDragDrop(source, dataObj, System.Windows.DragDropEffects.Move);
        }
        finally
        {
            activeDragPayload = null;
            dragHoverTarget = null;
            CloseDragGhost();
            RenderRibbonTabsAndStrip();
        }
    }

    private void ShowDragGhost(string text)
    {
        CloseDragGhost();
        var badge = new Border
        {
            Background = new SolidColorBrush(WpfColor.FromArgb(235, 30, 91, 166)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 5, 10, 5),
            Child = new TextBlock { Text = text, Foreground = WpfBrushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold }
        };
        dragGhost = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = WpfBrushes.Transparent,
            SizeToContent = SizeToContent.WidthAndHeight,
            ShowInTaskbar = false,
            ShowActivated = false,
            IsHitTestVisible = false,
            Topmost = true,
            Content = badge
        };
        MoveDragGhost();
        dragGhost.Show();
        dragGhostTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Input, (_, _) => MoveDragGhost(), Dispatcher);
        dragGhostTimer.Start();
    }

    private void MoveDragGhost()
    {
        if (dragGhost is null || !GetCursorPos(out var pt)) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        dragGhost.Left = pt.X / dpi.DpiScaleX + 14;
        dragGhost.Top = pt.Y / dpi.DpiScaleY + 18;
    }

    private void CloseDragGhost()
    {
        dragGhostTimer?.Stop();
        dragGhostTimer = null;
        if (dragGhost is not null)
        {
            dragGhost.Close();
            dragGhost = null;
        }
    }

    private void RefreshRibbonBank()
    {
        RibbonBankSearchHint.Visibility = string.IsNullOrEmpty(RibbonBankSearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        var placedIds = ribbonTabs.SelectMany(t => t.Panels).SelectMany(p => p.Items)
            .SelectMany(i => i is RibbonPlacement rp ? [rp.Id] : i is RibbonStackModel st ? st.Children.Select(c => c.Id) : Array.Empty<string>())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var query = RibbonBankSearchBox.Text.Trim();
        RibbonBankList.Items.Clear();
        foreach (var tool in ribbonTools.Where(t => !placedIds.Contains(t.Id) && MatchesKoreanSearch(t.Title, query)))
            RibbonBankList.Items.Add(new System.Windows.Controls.ListBoxItem { Content = tool.Title + (tool.Installed ? "" : " · 미설치"), Tag = tool });
    }

    private static bool SelectTreeItemByTag(System.Windows.Controls.ItemCollection items, object? target)
    {
        if (target is null) return false;
        foreach (var obj in items)
        {
            if (obj is TreeViewItem tvi)
            {
                if (ReferenceEquals(tvi.Tag, target)) { tvi.IsSelected = true; return true; }
                if (SelectTreeItemByTag(tvi.Items, target)) return true;
            }
        }
        return false;
    }

    private void RefreshRibbonTree()
    {
        RibbonTree.Items.Clear();
        for (var i = 0; i < ribbonTabs.Count; i++)
        {
            var tab = ribbonTabs[i];
            var tItem = new TreeViewItem { Header = tab.Name, Tag = tab, IsExpanded = true, IsSelected = selectedRibbonItem == tab };
            foreach (var panel in tab.Panels)
            {
                var pItem = new TreeViewItem { Header = panel.Name, Tag = panel, IsExpanded = true, IsSelected = selectedRibbonItem == panel };
                foreach (var item in panel.Items)
                {
                    if (item is RibbonPlacement rp)
                    {
                        pItem.Items.Add(new TreeViewItem { Header = rp.Kind == "Separator" ? "│ 구분선" : rp.Title, Tag = rp, IsSelected = ReferenceEquals(selectedRibbonItem, rp) });
                    }
                    else if (item is RibbonStackModel st)
                    {
                        var sItem = new TreeViewItem { Header = $"{st.Rows}단 묶음 ({st.Children.Count}/{st.Rows})", Tag = st, IsExpanded = true, IsSelected = ReferenceEquals(selectedRibbonItem, st) };
                        foreach (var c in st.Children)
                            sItem.Items.Add(new TreeViewItem { Header = c.Title, Tag = c, IsSelected = ReferenceEquals(selectedRibbonItem, c) });
                        pItem.Items.Add(sItem);
                    }
                }
                tItem.Items.Add(pItem);
            }
            RibbonTree.Items.Add(tItem);
        }
    }

    private void RefreshRibbonInspector()
    {
        syncUi = true;
        try
        {
            RibbonCaptionBox.Text = selectedRibbonItem switch
            {
                RibbonTabModel t => t.Name,
                RibbonPanelModel p => p.Name,
                RibbonPlacement rp when rp.Kind != "Separator" => rp.Title,
                _ => ""
            };
            RibbonCaptionBox.IsEnabled = selectedRibbonItem is RibbonTabModel or RibbonPanelModel or RibbonPlacement { Kind: not "Separator" };

            var parentStack = FindParentStack(selectedRibbonItem);
            var rows = selectedRibbonItem is RibbonStackModel st ? st.Rows : parentStack?.Rows ?? 0;
            RibbonArrangementBox.SelectedIndex = rows == 0 ? 0 : rows - 1;
            RibbonArrangementBox.IsEnabled = selectedRibbonItem is RibbonStackModel or RibbonPlacement { Kind: not "Separator" };

            RibbonMoveTabBox.Items.Clear();
            foreach (var t in ribbonTabs) RibbonMoveTabBox.Items.Add(t.Name);
            RibbonMoveTabBox.SelectedIndex = selectedTabIndex;
            var canMove = selectedRibbonItem is RibbonPanelModel or RibbonStackModel or RibbonPlacement && ribbonTabs.Count > 1;
            RibbonMoveTabBox.IsEnabled = canMove;
            RibbonMoveTabBtn.IsEnabled = canMove;
        }
        finally { syncUi = false; }
    }

    private void RenderLoadersView()
    {
        LoadersPanel.Children.Clear();
        void AddCard(string title, string repo, ToolSpec? spec, string hostKey)
        {
            var card = new Border { Background = WpfBrushes.White, BorderBrush = new SolidColorBrush(WpfColor.FromRgb(226, 232, 240)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(18), Margin = new Thickness(0, 0, 0, 12) };
            var dock = new DockPanel();

            if (spec is null)
            {
                var btn = new WpfButton { Content = "지원 예정", Style = (Style)FindResource("SecondaryButton"), IsEnabled = false };
                DockPanel.SetDock(btn, Dock.Right);
                dock.Children.Add(btn);
                var info = new StackPanel();
                info.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold });
                info.Children.Add(new TextBlock { Text = repo, FontSize = 11, Foreground = new SolidColorBrush(WpfColor.FromRgb(100, 116, 139)), Margin = new Thickness(0, 2, 0, 6) });
                info.Children.Add(new TextBlock { Text = "Navisworks 호스트 로더는 향후 추가될 예정입니다.", FontSize = 12, Foreground = new SolidColorBrush(WpfColor.FromRgb(51, 65, 85)) });
                dock.Children.Add(info);
                card.Child = dock;
                LoadersPanel.Children.Add(card);
                return;
            }

            var status = backend.GetLoaderStatus(spec);
            var offers = backend.GetLoaderOffers(spec);
            var chosenChannel = loaderSelectedChannel.GetValueOrDefault(hostKey, "stable");
            if (chosenChannel == "test" && !backend.IsTestAllowed) chosenChannel = "stable";
            var actionState = LoaderActions.Evaluate(status, offers, chosenChannel, backend.IsTestAllowed);

            var rightControls = new StackPanel { Orientation = WpfOrientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(rightControls, Dock.Right);

            var checkBtn = new WpfButton { Content = "저장소 확인", Style = (Style)FindResource("SecondaryButton"), Margin = new Thickness(0, 0, 6, 0), IsEnabled = !backend.IsBusy };
            checkBtn.Click += async (_, _) => await RunBusyActionAsync(async () => { await backend.CheckLoaderForHostAsync(hostKey); RenderLoadersView(); }, "저장소 확인");
            rightControls.Children.Add(checkBtn);

            var folderBtn = new WpfButton { Content = "폴더 열기", Style = (Style)FindResource("SecondaryButton"), Margin = new Thickness(0, 0, 6, 0) };
            folderBtn.Click += (_, _) => backend.OpenLoaderFolderFromModern(spec);
            rightControls.Children.Add(folderBtn);

            var repoBtn = new WpfButton { Content = "GitHub", Style = (Style)FindResource("SecondaryButton"), Margin = new Thickness(0, 0, 6, 0) };
            repoBtn.Click += (_, _) => backend.OpenLoaderRepoFromModern(spec);
            rightControls.Children.Add(repoBtn);

            if (backend.IsTestAllowed)
            {
                var channelBox = new System.Windows.Controls.ComboBox { Width = 84, Margin = new Thickness(0, 0, 6, 0) };
                channelBox.Items.Add("정식");
                channelBox.Items.Add("TEST");
                channelBox.SelectedIndex = chosenChannel == "test" ? 1 : 0;
                channelBox.SelectionChanged += (_, _) =>
                {
                    loaderSelectedChannel[hostKey] = channelBox.SelectedIndex == 1 ? "test" : "stable";
                    RenderLoadersView();
                };
                rightControls.Children.Add(channelBox);
            }

            var applyBtn = new WpfButton
            {
                Content = actionState.Text,
                Style = (Style)FindResource(actionState.Enabled ? "PrimaryButton" : "SecondaryButton"),
                IsEnabled = actionState.Enabled && !backend.IsBusy
            };
            applyBtn.Click += async (_, _) => await RunBusyActionAsync(async () => { await backend.ApplyLoaderForHostAsync(hostKey, chosenChannel); RenderLoadersView(); }, "로더 적용");
            rightControls.Children.Add(applyBtn);
            dock.Children.Add(rightControls);

            var leftInfo = new StackPanel();
            var titleRow = new StackPanel { Orientation = WpfOrientation.Horizontal };
            titleRow.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            var isInstalledTest = status.Managed && status.Version.Contains('-');
            var chText = !status.Managed ? "미설치" : isInstalledTest ? "TEST 설치됨" : "정식 설치됨";
            var chBg = !status.Managed ? WpfColor.FromRgb(241, 245, 249) : isInstalledTest ? WpfColor.FromRgb(237, 233, 254) : WpfColor.FromRgb(220, 252, 231);
            var chFg = !status.Managed ? WpfColor.FromRgb(71, 85, 105) : isInstalledTest ? WpfColor.FromRgb(109, 40, 217) : WpfColor.FromRgb(22, 101, 52);
            titleRow.Children.Add(new Border
            {
                Background = new SolidColorBrush(chBg),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(8, 0, 0, 0),
                Child = new TextBlock { Text = chText, FontSize = 11, FontWeight = FontWeights.Medium, Foreground = new SolidColorBrush(chFg) }
            });
            leftInfo.Children.Add(titleRow);
            leftInfo.Children.Add(new TextBlock { Text = repo, FontSize = 11, Foreground = new SolidColorBrush(WpfColor.FromRgb(100, 116, 139)), Margin = new Thickness(0, 2, 0, 6) });
            var offerSummary = offers is null
                ? "원격 버전 미확인 ('저장소 확인'을 누르면 최신 정식/TEST 버전을 비교합니다)"
                : $"원격 정식: {offers.Stable?.Version ?? "없음"}" + (backend.IsTestAllowed ? $" · 원격 TEST: {offers.Test?.Version ?? "없음"}" : "");
            var errorText = offers is null ? "" : string.Join(" / ", new[] { offers.StableError is null ? null : "정식 확인 실패: " + offers.StableError, backend.IsTestAllowed && offers.TestError is not null ? "TEST 확인 실패: " + offers.TestError : null }.Where(x => x is not null));
            var detailBody = $"{FormatLoaderStatus(status)}\n{offerSummary}\n{actionState.Detail}" + (string.IsNullOrEmpty(errorText) ? "" : $"\n오류: {errorText}");
            leftInfo.Children.Add(new TextBlock { Text = detailBody, FontSize = 12, Foreground = new SolidColorBrush(WpfColor.FromRgb(51, 65, 85)), TextWrapping = TextWrapping.Wrap });
            dock.Children.Add(leftInfo);
            card.Child = dock;
            LoadersPanel.Children.Add(card);
        }

        AddCard("Revit Native Host 로더", "RoBIM-tech/RoBIM-Revit-Addin", Catalog.RevitHost, "revit");
        AddCard("AutoCAD 로더", "RoBIM-tech/RoBIM-CAD-Loader", Catalog.Cad, "autocad");
        AddCard("Navisworks 로더", "RoBIM-tech/RoBIM-Navisworks-Addin (예정)", null, "navisworks");
    }

    private void RenderAccountView()
    {
        AccountSummaryLabel.Text = backend.AccountSummaryText;
        AccountTestCheck.IsChecked = backend.AllowPrereleaseChecked;
        AccountTestCheck.IsEnabled = backend.IsTestAllowed;
        AccountAutoLoginCheck.IsChecked = backend.AutoLoginChecked;
        AccountAutoUpdateCheck.IsChecked = backend.ManagerAutoUpdateChecked;
        AccountStartupCheck.IsChecked = backend.StartWithWindowsChecked;

        var busy = backend.IsBusy;
        AccountSignInBtn.IsEnabled = !busy;
        AccountOtherLoginBtn.IsEnabled = !busy;
        AccountSignOutBtn.IsEnabled = !busy && backend.IsSignedIn;
        CheckManagerUpdateBtn.IsEnabled = !busy;
        InstallManagerUpdateBtn.Visibility = backend.HasPendingManagerUpdate ? Visibility.Visible : Visibility.Collapsed;
        InstallManagerUpdateBtn.IsEnabled = !busy;

        var logins = backend.SavedAccountLogins;
        SavedAccountsBox.Items.Clear();
        foreach (var login in logins) SavedAccountsBox.Items.Add(login);
        if (SavedAccountsBox.Items.Count > 0) SavedAccountsBox.SelectedIndex = 0;
        SavedAccountsRow.Visibility = logins.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UseSavedAccountBtn.IsEnabled = !busy && logins.Count > 0;

        ManagerVersionLabel.Text = $"현재 버전: v{backend.ManagerProductVersion} (테스트 업데이트 수신 시 프리릴리즈 포함 확인)";
        ManagerUpdateStatusLabel.Text = backend.ManagerUpdateStatusText;
    }

    private static string FormatLoaderStatus(ToolStatus status) =>
        status.Managed ? $"설치됨 · 버전 {status.Version} ({status.Path})" : "미설치 또는 외부 설치";

    private static BitmapImage? LoadIconBitmap(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception) { return null; }
    }

    private static bool MatchesKoreanSearch(string text, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        if (text.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
        var chosung = GetChosung(text);
        return chosung.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetChosung(string input)
    {
        const string initials = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ";
        var chars = new char[input.Length];
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            chars[i] = c is >= '\uAC00' and <= '\uD7A3' ? initials[(c - 0xAC00) / (21 * 28)] : c;
        }
        return new string(chars);
    }

    private string UniqueTabName(string basis)
    {
        var name = basis;
        for (var i = 2; ribbonTabs.Any(t => t.Name == name); i++) name = $"{basis} {i}";
        return name;
    }

    private RibbonStackModel? FindParentStack(object? item) =>
        item is RibbonPlacement rp
            ? ribbonTabs.SelectMany(t => t.Panels).SelectMany(p => p.Items).OfType<RibbonStackModel>().FirstOrDefault(s => s.Children.Contains(rp))
            : null;

    private RibbonPanelModel? FindParentPanel(object? item)
    {
        foreach (var t in ribbonTabs)
            foreach (var p in t.Panels)
                if (p.Items.Contains(item!) || p.Items.OfType<RibbonStackModel>().Any(s => item is RibbonPlacement rp && s.Children.Contains(rp)))
                    return p;
        return null;
    }

    private RibbonTabModel? FindParentTab(object? item) =>
        item is RibbonTabModel tm ? tm : ribbonTabs.FirstOrDefault(t => item is RibbonPanelModel pm ? t.Panels.Contains(pm) : t.Panels.Contains(FindParentPanel(item)!));

    private void RemoveRibbonPayload(object payload)
    {
        if (payload is RibbonPanelModel pm)
        {
            foreach (var t in ribbonTabs) t.Panels.Remove(pm);
            return;
        }
        foreach (var t in ribbonTabs)
            foreach (var p in t.Panels)
            {
                p.Items.Remove(payload);
                foreach (var st in p.Items.OfType<RibbonStackModel>().ToArray())
                {
                    if (payload is RibbonPlacement rp && st.Children.Remove(rp) && st.Children.Count == 0)
                        p.Items.Remove(st);
                }
            }
    }

    private void MoveRibbonPayloadToTab(object payload, RibbonTabModel targetTab)
    {
        if (payload is RibbonTabModel) return;
        RemoveRibbonPayload(payload);
        if (payload is RibbonPanelModel pm) { targetTab.Panels.Add(pm); return; }
        if (targetTab.Panels.Count == 0) targetTab.Panels.Add(new RibbonPanelModel("도구"));
        targetTab.Panels[0].Items.Add(payload);
    }

    private void MoveRibbonPayloadToPanel(object payload, RibbonPanelModel targetPanel, RibbonPlacement? before = null)
    {
        if (payload is RibbonTabModel) return;
        if (payload is RibbonPanelModel pm)
        {
            var destTab = FindParentTab(targetPanel);
            if (destTab is null || pm == targetPanel) return;
            RemoveRibbonPayload(pm);
            var idx = destTab.Panels.IndexOf(targetPanel);
            destTab.Panels.Insert(Math.Max(0, idx), pm);
            return;
        }
        RemoveRibbonPayload(payload);
        var insertAt = before is not null ? targetPanel.Items.IndexOf(before) : -1;
        if (insertAt >= 0) targetPanel.Items.Insert(insertAt, payload);
        else targetPanel.Items.Add(payload);
    }

    private async Task RunBusyActionAsync(Func<Task> action, string title)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(this, ex.Message, title);
        }
        finally
        {
            RefreshAllViews();
        }
    }

    private bool ConfirmDiscardRibbonChanges()
    {
        if (!ribbonDirty || currentNav != "ribbon") return true;
        var res = WpfMessageBox.Show(this, "저장하지 않은 리본 편집 내용이 있습니다. 저장하지 않고 이동합니까?", "리본 편집기", MessageBoxButton.YesNo);
        if (res != MessageBoxResult.Yes) return false;
        ribbonDirty = false;
        return true;
    }

    private void OpenCardContextMenu(WpfButton anchor, ManagerForm.ButtonCard card, bool permitted)
    {
        var menu = new System.Windows.Controls.ContextMenu();
        var detailsItem = new System.Windows.Controls.MenuItem { Header = "기능 설명 / 상세 정보" };
        detailsItem.Click += (_, _) => backend.ShowCardDetailsFromModern(card);
        menu.Items.Add(detailsItem);

        var checkItem = new System.Windows.Controls.MenuItem { Header = "업데이트 확인" };
        checkItem.Click += async (_, _) => await RunBusyActionAsync(() => backend.RefreshCardCatalogFromModernAsync(card), "업데이트 확인");
        menu.Items.Add(checkItem);

        if (permitted && card.Kind == ManagerForm.ButtonKind.Native && card.Owner.Package is not null)
        {
            var stableItem = new System.Windows.Controls.MenuItem { Header = "저장소 정식 버전 설치…" };
            stableItem.Click += async (_, _) => await RunBusyActionAsync(() => backend.ToggleCardFromModernAsync(card, true, "stable"), "정식 설치");
            menu.Items.Add(stableItem);

            if (backend.IsTestAllowed)
            {
                var testItem = new System.Windows.Controls.MenuItem { Header = "저장소 TEST 버전 설치…" };
                testItem.Click += async (_, _) => await RunBusyActionAsync(() => backend.ToggleCardFromModernAsync(card, true, "test"), "TEST 설치");
                menu.Items.Add(testItem);
            }
        }

        if (permitted && card.Kind == ManagerForm.ButtonKind.Native)
        {
            var zipItem = new System.Windows.Controls.MenuItem { Header = "새 버전 ZIP 가져오기" };
            zipItem.Click += async (_, _) => await RunBusyActionAsync(() => backend.ImportNativeZipFromModernAsync(), "ZIP 가져오기");
            menu.Items.Add(zipItem);
        }

        if (permitted && card.Kind == ManagerForm.ButtonKind.Cad)
        {
            var cadCfgItem = new System.Windows.Controls.MenuItem { Header = "CAD 버튼 이름·패널·순서 구성" };
            cadCfgItem.Click += async (_, _) => await RunBusyActionAsync(() => backend.ConfigureCadButtonsFromModernAsync(card), "CAD 버튼 구성");
            menu.Items.Add(cadCfgItem);
        }

        menu.Items.Add(new System.Windows.Controls.Separator());

        if (permitted && card.Kind != ManagerForm.ButtonKind.Native)
        {
            var restoreItem = new System.Windows.Controls.MenuItem { Header = "이전 버전 복원" };
            restoreItem.Click += async (_, _) => await RunBusyActionAsync(() => backend.RestoreBackupFromModernAsync(card), "이전 버전 복원");
            menu.Items.Add(restoreItem);
        }

        var folderItem = new System.Windows.Controls.MenuItem { Header = "설치 폴더 열기" };
        folderItem.Click += (_, _) => backend.OpenCardFolderFromModern(card);
        menu.Items.Add(folderItem);

        var ghItem = new System.Windows.Controls.MenuItem { Header = "GitHub 저장소 열기" };
        ghItem.Click += (_, _) => backend.OpenCardGitHubFromModern(card);
        menu.Items.Add(ghItem);

        menu.PlacementTarget = anchor;
        menu.IsOpen = true;
    }

    private void ShowLogsDialog()
    {
        var win = new Window
        {
            Title = "작업 기록",
            Width = 820,
            Height = 460,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = WpfBrushes.White
        };
        win.Content = new System.Windows.Controls.TextBox
        {
            Text = string.IsNullOrWhiteSpace(backend.LogsText) ? "기록된 작업 로그가 없습니다." : backend.LogsText,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new System.Windows.Media.FontFamily("Consolas, Malgun Gothic"),
            FontSize = 12,
            Padding = new Thickness(12),
            BorderThickness = new Thickness(0)
        };
        win.ShowDialog();
    }

    private void OnSelectHostRevit(object sender, RoutedEventArgs e) { if (!ConfirmDiscardRibbonChanges()) return; currentHost = "revit"; LoadRibbonState(); RefreshAllViews(); }
    private void OnSelectHostCad(object sender, RoutedEventArgs e) { if (!ConfirmDiscardRibbonChanges()) return; currentHost = "autocad"; LoadRibbonState(); RefreshAllViews(); }
    private void OnSelectHostNavis(object sender, RoutedEventArgs e) { if (!ConfirmDiscardRibbonChanges()) return; currentHost = "navisworks"; LoadRibbonState(); RefreshAllViews(); }
    private void OnSelectNavTools(object sender, RoutedEventArgs e) { if (!ConfirmDiscardRibbonChanges()) return; currentNav = "tools"; RefreshAllViews(); }
    private void OnSelectNavRibbon(object sender, RoutedEventArgs e) { currentNav = "ribbon"; LoadRibbonState(); RefreshAllViews(); }
    private void OnSelectNavLoaders(object sender, RoutedEventArgs e) { if (!ConfirmDiscardRibbonChanges()) return; currentNav = "loaders"; RefreshAllViews(); }
    private void OnSelectNavAccount(object sender, RoutedEventArgs e) { if (!ConfirmDiscardRibbonChanges()) return; currentNav = "account"; RefreshAllViews(); }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        await RunBusyActionAsync(async () =>
        {
            if (currentNav == "loaders")
            {
                await backend.CheckLoaderForHostAsync("revit");
                await backend.CheckLoaderForHostAsync("autocad");
            }
            else
            {
                await backend.RefreshAllAsync();
                if (currentNav == "ribbon" && !ribbonDirty) LoadRibbonState();
            }
        }, "새로고침");
    }
    private async void OnBatchUpdateClicked(object sender, RoutedEventArgs e) => await RunBusyActionAsync(() => backend.RunBatchUpdateForHostAsync(currentHost), "일괄 업데이트");
    private void OnShowLogsClicked(object sender, RoutedEventArgs e) => ShowLogsDialog();
    private void OnManualReloadClicked(object sender, RoutedEventArgs e) => backend.RequestManualRevitReloadFromModern();
    private async void OnConfigureCadButtonsClicked(object sender, RoutedEventArgs e) => await RunBusyActionAsync(() => backend.ConfigureCadButtonsFromModernAsync(), "CAD 버튼 구성");
    private async void OnImportZipClicked(object sender, RoutedEventArgs e)
    {
        await RunBusyActionAsync(() => currentHost == "autocad" ? backend.ImportCadZipFromModernAsync() : backend.ImportNativeZipFromModernAsync(), "ZIP 가져오기");
    }

    private void OnToolFilterChanged(object sender, TextChangedEventArgs e) { if (IsLoaded && !syncUi) RenderToolsView(); }
    private void OnToolSortChanged(object sender, SelectionChangedEventArgs e) { if (IsLoaded && !syncUi) RenderToolsView(); }
    private void OnFilterAll(object sender, RoutedEventArgs e) { toolFilter = "all"; RenderToolsView(); }
    private void OnFilterInstalled(object sender, RoutedEventArgs e) { toolFilter = "installed"; RenderToolsView(); }
    private void OnFilterUninstalled(object sender, RoutedEventArgs e) { toolFilter = "uninstalled"; RenderToolsView(); }
    private void OnFilterUpdate(object sender, RoutedEventArgs e) { toolFilter = "update"; RenderToolsView(); }
    private void OnFilterTest(object sender, RoutedEventArgs e) { toolFilter = "test"; RenderToolsView(); }

    private void OnZoomCompact(object sender, RoutedEventArgs e) { compactRibbon = true; RenderRibbonView(); }
    private void OnZoomNormal(object sender, RoutedEventArgs e) { compactRibbon = false; RenderRibbonView(); }
    private void OnRibbonStripMouseWheel(object sender, MouseWheelEventArgs e)
    {
        RibbonStripScroller.ScrollToHorizontalOffset(RibbonStripScroller.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    private void OnRibbonBankSearchChanged(object sender, TextChangedEventArgs e) { if (IsLoaded && !syncUi) RefreshRibbonBank(); }
    private void OnRibbonBankMouseDown(object sender, MouseButtonEventArgs e) => dragStartPoint = e.GetPosition(null);
    private void OnRibbonBankMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || RibbonBankList.SelectedItem is not ListBoxItem { Tag: RibbonTool tool }) return;
        var diff = e.GetPosition(null) - dragStartPoint;
        if (Math.Abs(diff.X) > 6 || Math.Abs(diff.Y) > 6)
        {
            var rp = new RibbonPlacement(tool.Id, tool.Title);
            StartRibbonDrag(RibbonBankList, rp);
        }
    }

    private static TreeViewItem? FindTreeViewItemUnder(DependencyObject? source)
    {
        while (source is not null && source is not TreeViewItem)
            source = VisualTreeHelper.GetParent(source);
        return source as TreeViewItem;
    }

    private void OnRibbonTreeMouseDown(object sender, MouseButtonEventArgs e)
    {
        dragStartPoint = e.GetPosition(null);
        dragPayload = FindTreeViewItemUnder(e.OriginalSource as DependencyObject)?.Tag;
    }

    private void OnRibbonTreeMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || dragPayload is null) return;
        var diff = e.GetPosition(null) - dragStartPoint;
        if (Math.Abs(diff.X) > 6 || Math.Abs(diff.Y) > 6)
        {
            var payload = dragPayload;
            dragPayload = null;
            StartRibbonDrag(RibbonTree, payload);
        }
    }

    private void OnRibbonTreeDragOver(object sender, WpfDragEventArgs e)
    {
        if (!e.Data.GetDataPresent("RoBIM.RibbonItem")) { e.Effects = System.Windows.DragDropEffects.None; e.Handled = true; return; }
        e.Effects = System.Windows.DragDropEffects.Move;
        var targetItem = FindTreeViewItemUnder(e.OriginalSource as DependencyObject)?.Tag;
        if (targetItem is not null && !ReferenceEquals(targetItem, activeDragPayload))
            SetDragHoverTarget(targetItem);
        e.Handled = true;
    }

    private void OnRibbonTreeDrop(object sender, WpfDragEventArgs e)
    {
        if (e.Data.GetData("RoBIM.RibbonItem") is not { } payload) return;
        var targetItem = FindTreeViewItemUnder(e.OriginalSource as DependencyObject)?.Tag;
        if (targetItem is null || ReferenceEquals(targetItem, payload)) return;
        if (targetItem is RibbonTabModel targetTab)
        {
            MoveRibbonPayloadToTab(payload, targetTab);
            selectedTabIndex = ribbonTabs.IndexOf(targetTab);
        }
        else if (targetItem is RibbonPanelModel targetPanel)
        {
            MoveRibbonPayloadToPanel(payload, targetPanel);
        }
        else if (targetItem is RibbonStackModel targetStack && payload is RibbonPlacement child && child.Kind != "Separator" && targetStack.Children.Count < targetStack.Rows)
        {
            RemoveRibbonPayload(child);
            targetStack.Children.Add(child);
        }
        else if (targetItem is RibbonPlacement targetPlacement && FindParentPanel(targetPlacement) is { } parentPanel)
        {
            MoveRibbonPayloadToPanel(payload, parentPanel, targetPlacement);
        }
        ribbonDirty = true;
        RenderRibbonView();
        e.Handled = true;
    }

    private void InsertNearSelection(RibbonPanelModel panel, object item)
    {
        var anchor = FindParentStack(selectedRibbonItem) as object ?? selectedRibbonItem;
        var idx = anchor is not null ? panel.Items.IndexOf(anchor) : -1;
        if (idx >= 0) panel.Items.Insert(idx + 1, item);
        else panel.Items.Add(item);
    }

    private void OnRibbonBankDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RibbonBankList.SelectedItem is not ListBoxItem { Tag: RibbonTool tool }) return;
        var tab = ribbonTabs[selectedTabIndex];
        if (tab.Panels.Count == 0) tab.Panels.Add(new RibbonPanelModel("도구"));
        var targetPanel = FindParentPanel(selectedRibbonItem) ?? tab.Panels[0];
        var rp = new RibbonPlacement(tool.Id, tool.Title);
        InsertNearSelection(targetPanel, rp);
        selectedRibbonItem = rp;
        ribbonDirty = true;
        RenderRibbonView();
    }

    private void OnRibbonTreeSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (syncUi || e.NewValue is not TreeViewItem tvi) return;
        selectedRibbonItem = tvi.Tag;
        if (FindParentTab(selectedRibbonItem) is { } tab) selectedTabIndex = ribbonTabs.IndexOf(tab);
        syncUi = true;
        try
        {
            RenderRibbonTabsAndStrip();
            RefreshRibbonInspector();
        }
        finally { syncUi = false; }
    }

    private void OnRibbonTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RibbonCaptionBox.IsEnabled)
        {
            RibbonCaptionBox.Focus();
            RibbonCaptionBox.SelectAll();
        }
    }

    private void OnRenameNodeClicked(object sender, RoutedEventArgs e)
    {
        var text = RibbonCaptionBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;
        if (selectedRibbonItem is RibbonTabModel t) t.Name = text;
        else if (selectedRibbonItem is RibbonPanelModel p) p.Name = text;
        else if (selectedRibbonItem is RibbonPlacement rp && rp.Kind != "Separator")
        {
            var updated = rp with { Title = text };
            var panel = FindParentPanel(rp);
            if (panel is not null)
            {
                var idx = panel.Items.IndexOf(rp);
                if (idx >= 0) panel.Items[idx] = updated;
                else
                {
                    var st = FindParentStack(rp);
                    if (st is not null) { var ci = st.Children.IndexOf(rp); if (ci >= 0) st.Children[ci] = updated; }
                }
            }
            selectedRibbonItem = updated;
        }
        ribbonDirty = true;
        RenderRibbonView();
    }

    private void OnArrangementChanged(object sender, SelectionChangedEventArgs e)
    {
        if (syncUi || RibbonArrangementBox.SelectedIndex < 0) return;
        var rows = RibbonArrangementBox.SelectedIndex == 0 ? 0 : RibbonArrangementBox.SelectedIndex + 1;
        if (selectedRibbonItem is RibbonStackModel st)
        {
            var panel = FindParentPanel(st);
            if (panel is null) return;
            if (rows == 0)
            {
                var idx = panel.Items.IndexOf(st);
                panel.Items.RemoveAt(idx);
                for (var i = 0; i < st.Children.Count; i++)
                    panel.Items.Insert(idx + i, st.Children[i]);
                selectedRibbonItem = st.Children.FirstOrDefault() ?? (object)panel;
            }
            else
            {
                st.Rows = rows;
            }
            ribbonDirty = true;
            RenderRibbonView();
        }
        else if (selectedRibbonItem is RibbonPlacement rp && rp.Kind != "Separator")
        {
            var panel = FindParentPanel(rp);
            if (panel is null) return;
            var parentSt = FindParentStack(rp);
            if (parentSt is not null)
            {
                if (rows == 0)
                {
                    var stIdx = panel.Items.IndexOf(parentSt);
                    parentSt.Children.Remove(rp);
                    if (parentSt.Children.Count == 0) panel.Items.RemoveAt(stIdx);
                    else stIdx++;
                    panel.Items.Insert(Math.Clamp(stIdx, 0, panel.Items.Count), rp);
                }
                else
                {
                    parentSt.Rows = rows;
                }
                ribbonDirty = true;
                RenderRibbonView();
            }
            else if (rows > 0)
            {
                var idx = panel.Items.IndexOf(rp);
                var newStack = new RibbonStackModel("stack-" + Guid.NewGuid().ToString("N"), rows);
                newStack.Children.Add(rp);
                panel.Items[idx] = newStack;
                ribbonDirty = true;
                RenderRibbonView();
            }
        }
    }

    private void OnQuickMoveTabClicked(object sender, RoutedEventArgs e)
    {
        if (selectedRibbonItem is null || RibbonMoveTabBox.SelectedIndex < 0 || RibbonMoveTabBox.SelectedIndex >= ribbonTabs.Count) return;
        MoveRibbonPayloadToTab(selectedRibbonItem, ribbonTabs[RibbonMoveTabBox.SelectedIndex]);
        selectedTabIndex = RibbonMoveTabBox.SelectedIndex;
        ribbonDirty = true;
        RenderRibbonView();
    }

    private void OnAddStack2(object sender, RoutedEventArgs e) => AddStackToCurrentPanel(2);
    private void OnAddStack3(object sender, RoutedEventArgs e) => AddStackToCurrentPanel(3);
    private void AddStackToCurrentPanel(int rows)
    {
        var tab = ribbonTabs[selectedTabIndex];
        var panel = FindParentPanel(selectedRibbonItem) ?? tab.Panels.FirstOrDefault() ?? new RibbonPanelModel("도구");
        if (!tab.Panels.Contains(panel)) tab.Panels.Add(panel);
        var st = new RibbonStackModel("stack-" + Guid.NewGuid().ToString("N"), rows);
        InsertNearSelection(panel, st);
        selectedRibbonItem = st;
        ribbonDirty = true;
        RenderRibbonView();
    }

    private void OnAddPanel(object sender, RoutedEventArgs e)
    {
        var tab = ribbonTabs[selectedTabIndex];
        var panel = new RibbonPanelModel("새 패널 " + (tab.Panels.Count + 1));
        tab.Panels.Add(panel);
        selectedRibbonItem = panel;
        ribbonDirty = true;
        RenderRibbonView();
    }

    private void OnAddSeparator(object sender, RoutedEventArgs e)
    {
        var tab = ribbonTabs[selectedTabIndex];
        var panel = FindParentPanel(selectedRibbonItem) ?? tab.Panels.FirstOrDefault();
        if (panel is null) return;
        var sep = new RibbonPlacement("separator:" + Guid.NewGuid().ToString("N"), "구분선", Kind: "Separator");
        InsertNearSelection(panel, sep);
        selectedRibbonItem = sep;
        ribbonDirty = true;
        RenderRibbonView();
    }

    private void OnMoveUp(object sender, RoutedEventArgs e) => MoveSelectedRibbonItem(-1);
    private void OnMoveDown(object sender, RoutedEventArgs e) => MoveSelectedRibbonItem(1);
    private void MoveSelectedRibbonItem(int delta)
    {
        if (selectedRibbonItem is null) return;
        if (FindParentStack(selectedRibbonItem) is { } st && selectedRibbonItem is RibbonPlacement rp)
        {
            var i = st.Children.IndexOf(rp);
            var n = i + delta;
            if (n >= 0 && n < st.Children.Count) { st.Children.RemoveAt(i); st.Children.Insert(n, rp); ribbonDirty = true; RenderRibbonView(); }
            return;
        }
        var panel = FindParentPanel(selectedRibbonItem);
        if (panel is not null && selectedRibbonItem != panel)
        {
            var i = panel.Items.IndexOf(selectedRibbonItem);
            var n = i + delta;
            if (n >= 0 && n < panel.Items.Count) { panel.Items.RemoveAt(i); panel.Items.Insert(n, selectedRibbonItem); ribbonDirty = true; RenderRibbonView(); }
        }
    }

    private void OnDeleteNode(object sender, RoutedEventArgs e)
    {
        if (selectedRibbonItem is null) return;
        if (selectedRibbonItem is RibbonTabModel tm)
        {
            if (ribbonTabs.Count <= 1) return;
            ribbonTabs.Remove(tm);
            selectedTabIndex = Math.Clamp(selectedTabIndex, 0, ribbonTabs.Count - 1);
            selectedRibbonItem = ribbonTabs[selectedTabIndex];
        }
        else
        {
            RemoveRibbonPayload(selectedRibbonItem);
            selectedRibbonItem = null;
        }
        ribbonDirty = true;
        RenderRibbonView();
    }

    private void OnRibbonReloadFromDisk(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscardRibbonChanges()) return;
        LoadRibbonState();
        RenderRibbonView();
    }

    private void OnRibbonDefaults(object sender, RoutedEventArgs e)
    {
        if (WpfMessageBox.Show(this, "편집 중인 배치를 기본 위치로 되돌립니까?", "기본 배치", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        var hostName = currentHost == "autocad" ? "AutoCAD" : currentHost == "navisworks" ? "Navisworks" : "Revit";
        var defaults = RibbonLayouts.Defaults(hostName, ribbonTools);
        ribbonTabs.Clear();
        foreach (var t in defaults.Tabs)
        {
            var tm = new RibbonTabModel(t.Name);
            foreach (var p in t.Panels)
            {
                var pm = new RibbonPanelModel(p.Name);
                PopulatePanelFromLayout(pm, p);
                tm.Panels.Add(pm);
            }
            ribbonTabs.Add(tm);
        }
        if (ribbonTabs.Count == 0) ribbonTabs.Add(new RibbonTabModel("RoBIM 도구"));
        selectedTabIndex = 0;
        selectedRibbonItem = ribbonTabs[0];
        ribbonDirty = true;
        RenderRibbonView();
    }

    private async void OnRibbonRecoverSessions(object sender, RoutedEventArgs e) => await RunBusyActionAsync(() => backend.ReprepareRevitSessionsFromModernAsync(), "현재 Revit에 다시 준비");

    private void OnRibbonCheckResult(object sender, RoutedEventArgs e)
    {
        var result = new RibbonLayouts(backend.CorePaths).RevitResult();
        WpfMessageBox.Show(this, result, "리본 적용 결과 확인");
    }

    private async void OnRibbonSave(object sender, RoutedEventArgs e)
    {
        try
        {
            var layout = SnapshotRibbonLayout();
            new RibbonLayouts(backend.CorePaths).Save(layout);
            ribbonDirty = false;
            BottomStatusText.Text = "리본 배치를 저장했습니다.";
            await Task.CompletedTask;
        }
        catch (Exception ex) { WpfMessageBox.Show(this, ex.Message, "리본 저장"); }
    }

    private async void OnRibbonApply(object sender, RoutedEventArgs e)
    {
        try
        {
            var layout = SnapshotRibbonLayout();
            BottomStatusText.Text = await backend.SaveAndApplyRibbonForHostAsync(currentHost, layout);
            ribbonDirty = false;
        }
        catch (Exception ex) { WpfMessageBox.Show(this, ex.Message, "리본 적용"); }
    }

    private async void OnAccountSignIn(object sender, RoutedEventArgs e) => await RunBusyActionAsync(() => backend.SignInOrRefreshFromModernAsync(), "GitHub 연결");
    private async void OnAccountOtherLogin(object sender, RoutedEventArgs e) => await RunBusyActionAsync(() => backend.LoginOtherGitHubFromModernAsync(), "GitHub 로그인");
    private async void OnUseSavedAccount(object sender, RoutedEventArgs e)
    {
        var loginName = SavedAccountsBox.SelectedItem as string ?? "";
        await RunBusyActionAsync(() => backend.UseSavedAccountFromModernAsync(loginName), "계정 전환");
    }
    private void OnAccountSignOut(object sender, RoutedEventArgs e) { backend.SignOutFromModern(); RefreshAllViews(); }
    private async void OnCheckManagerUpdate(object sender, RoutedEventArgs e) => await RunBusyActionAsync(() => backend.CheckManagerUpdateFromModernAsync(), "Manager 업데이트 확인");
    private async void OnInstallManagerUpdate(object sender, RoutedEventArgs e) => await RunBusyActionAsync(() => backend.InstallManagerUpdateFromModernAsync(), "Manager 업데이트");
    private void OnOptionChanged(object sender, RoutedEventArgs e)
    {
        if (syncUi) return;
        backend.SetAllowPrereleaseOption(AccountTestCheck.IsChecked == true);
        backend.SetAutoLoginOption(AccountAutoLoginCheck.IsChecked == true);
        backend.SetManagerAutoUpdateOption(AccountAutoUpdateCheck.IsChecked == true);
        backend.SetStartWithWindowsOption(AccountStartupCheck.IsChecked == true);
        RefreshAllViews();
    }
}
