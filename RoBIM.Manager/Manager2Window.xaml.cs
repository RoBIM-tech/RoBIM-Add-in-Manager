using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

    private List<RibbonTool> ribbonTools = [];
    private readonly List<RibbonTabModel> ribbonTabs = [];
    private int selectedTabIndex;
    private object? selectedRibbonItem;
    private WpfPoint dragStartPoint;
    private object? dragPayload;

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
            backend.CloseBackend();
        };
    }

    private void RefreshAllViews()
    {
        syncUi = true;
        try
        {
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
        StyleNavBtn(NavAccountBtn, currentNav == "account");

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
        var hostTitle = currentHost == "autocad" ? "AutoCAD 2026" : currentHost == "navisworks" ? "Navisworks 2026" : "Revit 2026";
        var navTitle = currentNav switch { "tools" => "도구 카탈로그", "ribbon" => "리본 편집기", "loaders" => "호스트 로더", _ => "계정 · 설정" };
        HeaderTitle.Text = $"{hostTitle} · {navTitle}";

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
        }
        else
        {
            HeaderBadgeText.Text = "확장 예정";
        }

        var showBatch = currentNav == "tools" && currentHost is "revit" or "autocad";
        HeaderBatchUpdateBtn.Visibility = showBatch ? Visibility.Visible : Visibility.Collapsed;
        HeaderRefreshBtn.Visibility = currentNav is "tools" or "loaders" ? Visibility.Visible : Visibility.Collapsed;
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

        void StyleFilter(WpfButton b, bool active)
        {
            b.Background = active ? new SolidColorBrush(WpfColor.FromRgb(30, 91, 166)) : WpfBrushes.White;
            b.Foreground = active ? WpfBrushes.White : new SolidColorBrush(WpfColor.FromRgb(30, 41, 59));
            b.BorderBrush = active ? new SolidColorBrush(WpfColor.FromRgb(30, 91, 166)) : new SolidColorBrush(WpfColor.FromRgb(203, 213, 225));
        }
        StyleFilter(FilterAllBtn, toolFilter == "all");
        StyleFilter(FilterInstalledBtn, toolFilter == "installed");
        StyleFilter(FilterUpdateBtn, toolFilter == "update");
        StyleFilter(FilterTestBtn, toolFilter == "test");

        ToolsCardsPanel.Children.Clear();
        if (currentHost == "navisworks")
        {
            ToolsEmptyNotice.Text = "Navisworks 2026 도구 카탈로그는 향후 호스트 연동 시 여기에 표시됩니다.";
            ToolsEmptyNotice.Visibility = Visibility.Visible;
            return;
        }

        var query = ToolSearchBox.Text.Trim();
        var cards = backend.GetCardsForHost(currentHost).Where(c =>
        {
            if (toolFilter == "installed" && !c.Selected) return false;
            if (toolFilter == "update" && !backend.HasUpdateForCard(c)) return false;
            if (toolFilter == "test" && c.Channel != "test" && !backend.HasTestOfferForCard(c)) return false;
            return MatchesKoreanSearch(c.Title + " " + c.Description, query);
        }).ToArray();

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

        var hasUpdate = backend.HasUpdateForCard(card);
        var badgeBg = card.Selected
            ? (hasUpdate ? WpfColor.FromRgb(254, 243, 199) : WpfColor.FromRgb(220, 252, 231))
            : (card.Channel == "test" ? WpfColor.FromRgb(237, 233, 254) : WpfColor.FromRgb(241, 245, 249));
        var badgeFg = card.Selected
            ? (hasUpdate ? WpfColor.FromRgb(146, 64, 14) : WpfColor.FromRgb(22, 101, 52))
            : (card.Channel == "test" ? WpfColor.FromRgb(109, 40, 217) : WpfColor.FromRgb(71, 85, 105));
        var badgeLabel = card.Selected
            ? (hasUpdate ? $"▲ 업데이트 · {card.SelectedVersion}" : $"● 설치됨 · {card.SelectedVersion}")
            : (card.Channel == "test" ? "◆ TEST" : "○ 미설치");

        var badge = new Border { Background = new SolidColorBrush(badgeBg), CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 3, 8, 3), VerticalAlignment = VerticalAlignment.Center };
        badge.Child = new TextBlock { Text = badgeLabel, FontSize = 11, FontWeight = FontWeights.Medium, Foreground = new SolidColorBrush(badgeFg) };
        DockPanel.SetDock(badge, Dock.Left);
        bottom.Children.Add(badge);

        var btnStack = new StackPanel { Orientation = WpfOrientation.Horizontal, HorizontalAlignment = WpfHorizontalAlignment.Right };
        if (card.Kind == ManagerForm.ButtonKind.Discover)
        {
            var loadBtn = new WpfButton { Content = "목록 확인", Style = (Style)FindResource("SecondaryButton"), Padding = new Thickness(10, 4, 10, 4), FontSize = 12 };
            loadBtn.Click += async (_, _) => await backend.ToggleCardFromModernAsync(card);
            btnStack.Children.Add(loadBtn);
        }
        else
        {
            if (hasUpdate)
            {
                var updBtn = new WpfButton { Content = "업데이트", Style = (Style)FindResource("PrimaryButton"), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0), FontSize = 12 };
                updBtn.Click += async (_, _) => await backend.ToggleCardFromModernAsync(card, true, card.SelectedChannel == "test" ? "test" : "stable");
                btnStack.Children.Add(updBtn);
            }
            if (!card.Selected)
            {
                var instBtn = new WpfButton { Content = "설치하기", Style = (Style)FindResource("PrimaryButton"), Padding = new Thickness(10, 4, 10, 4), FontSize = 12 };
                instBtn.Click += async (_, _) => await backend.ToggleCardFromModernAsync(card, true, card.Channel == "test" ? "test" : "stable");
                btnStack.Children.Add(instBtn);
            }
            else
            {
                var remBtn = new WpfButton { Content = "제거", Style = (Style)FindResource("SecondaryButton"), Padding = new Thickness(10, 4, 10, 4), FontSize = 12 };
                remBtn.Click += async (_, _) => await backend.ToggleCardFromModernAsync(card, false);
                btnStack.Children.Add(remBtn);
            }
        }
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
                var stacks = new Dictionary<string, RibbonStackModel>(StringComparer.OrdinalIgnoreCase);
                foreach (var btn in panel.Buttons)
                {
                    if (btn.Stack is { } sid)
                    {
                        if (!stacks.TryGetValue(sid, out var st))
                        {
                            st = new RibbonStackModel(sid, btn.StackRows);
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
                tm.Panels.Add(pm);
            }
            ribbonTabs.Add(tm);
        }
        if (ribbonTabs.Count == 0) ribbonTabs.Add(new RibbonTabModel("RoBIM 도구"));
        selectedTabIndex = Math.Clamp(selectedTabIndex, 0, ribbonTabs.Count - 1);
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
        RibbonApplyBtn.Content = currentHost == "autocad" ? "AutoCAD 적용" : currentHost == "navisworks" ? "Navisworks 저장" : "Revit 적용";
        RibbonScaleTransform.ScaleX = compactRibbon ? 0.78 : 1.0;
        RibbonScaleTransform.ScaleY = compactRibbon ? 0.78 : 1.0;
        RibbonZoomCompactBtn.Style = (Style)FindResource(compactRibbon ? "PrimaryButton" : "SecondaryButton");
        RibbonZoomNormalBtn.Style = (Style)FindResource(!compactRibbon ? "PrimaryButton" : "SecondaryButton");

        // Sticky Tabs
        RibbonTabsHost.Children.Clear();
        for (var i = 0; i < ribbonTabs.Count; i++)
        {
            var idx = i;
            var tab = ribbonTabs[i];
            var active = i == selectedTabIndex;
            var btn = new WpfButton
            {
                Content = tab.Name,
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 0, 3, 0),
                Background = active ? WpfBrushes.White : new SolidColorBrush(WpfColor.FromRgb(226, 232, 240)),
                Foreground = active ? new SolidColorBrush(WpfColor.FromRgb(30, 91, 166)) : new SolidColorBrush(WpfColor.FromRgb(71, 85, 105)),
                FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
                BorderThickness = new Thickness(1, 1, 1, 0),
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(203, 213, 225)),
                AllowDrop = true,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            btn.Click += (_, _) => { selectedTabIndex = idx; selectedRibbonItem = tab; RenderRibbonView(); };
            btn.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(typeof(object)) ? System.Windows.DragDropEffects.Move : System.Windows.DragDropEffects.None; e.Handled = true; };
            btn.Drop += (_, e) =>
            {
                if (e.Data.GetData("RoBIM.RibbonItem") is { } payload)
                {
                    MoveRibbonPayloadToTab(payload, tab);
                    selectedTabIndex = idx;
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
            RenderRibbonView();
        };
        RibbonTabsHost.Children.Add(addTabBtn);

        // Strip
        RibbonStripHost.Children.Clear();
        if (selectedTabIndex >= 0 && selectedTabIndex < ribbonTabs.Count)
        {
            var currentTab = ribbonTabs[selectedTabIndex];
            foreach (var panel in currentTab.Panels)
                RibbonStripHost.Children.Add(BuildPreviewPanel(panel));
        }

        RefreshRibbonBank();
        RefreshRibbonTree();
        RefreshRibbonInspector();
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
        card.Drop += (_, e) =>
        {
            if (e.Data.GetData("RoBIM.RibbonItem") is { } payload)
            {
                MoveRibbonPayloadToPanel(payload, panel);
                RenderRibbonView();
                e.Handled = true;
            }
        };

        var stack = new StackPanel();
        var itemsRow = new StackPanel { Orientation = WpfOrientation.Horizontal, MinHeight = 76 };
        foreach (var item in panel.Items)
        {
            if (item is RibbonPlacement rp)
            {
                if (rp.Kind == "Separator")
                {
                    var sep = new Border { Width = 9, Height = 68, Background = WpfBrushes.Transparent, Tag = rp, Cursor = System.Windows.Input.Cursors.Hand };
                    sep.Child = new Border { Width = 1, Background = new SolidColorBrush(WpfColor.FromRgb(203, 213, 225)), Margin = new Thickness(4, 4, 4, 4) };
                    WireRibbonDraggable(sep, rp);
                    itemsRow.Children.Add(sep);
                }
                else
                {
                    itemsRow.Children.Add(BuildPreviewLargeButton(rp, panel));
                }
            }
            else if (item is RibbonStackModel st)
            {
                var col = new Border
                {
                    Width = 140,
                    Height = 76,
                    Background = new SolidColorBrush(selectedRibbonItem == st ? WpfColor.FromRgb(219, 234, 254) : WpfColor.FromRgb(241, 245, 249)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(3),
                    Margin = new Thickness(2, 0, 2, 0),
                    AllowDrop = true
                };
                WireRibbonDraggable(col, st);
                col.Drop += (_, e) =>
                {
                    if (e.Data.GetData("RoBIM.RibbonItem") is RibbonPlacement child && child.Kind != "Separator" && st.Children.Count < st.Rows)
                    {
                        RemoveRibbonPayload(child);
                        st.Children.Add(child);
                        RenderRibbonView();
                        e.Handled = true;
                    }
                };
                var colStack = new StackPanel();
                foreach (var child in st.Children)
                {
                    var rowBtn = new Border
                    {
                        Height = Math.Max(20, 68 / st.Rows),
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
                col.Child = colStack;
                itemsRow.Children.Add(col);
            }
        }
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
        btn.Drop += (_, e) =>
        {
            if (e.Data.GetData("RoBIM.RibbonItem") is { } payload && !ReferenceEquals(payload, rp))
            {
                MoveRibbonPayloadToPanel(payload, panel, rp);
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
            selectedRibbonItem = model;
            RefreshRibbonInspector();
        };
        element.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || dragPayload is null) return;
            var diff = e.GetPosition(null) - dragStartPoint;
            if (Math.Abs(diff.X) > 6 || Math.Abs(diff.Y) > 6)
            {
                var dataObj = new System.Windows.DataObject("RoBIM.RibbonItem", dragPayload);
                dragPayload = null;
                WpfDragDrop.DoDragDrop(element, dataObj, System.Windows.DragDropEffects.Move);
            }
        };
    }

    private void RefreshRibbonBank()
    {
        var placedIds = ribbonTabs.SelectMany(t => t.Panels).SelectMany(p => p.Items)
            .SelectMany(i => i is RibbonPlacement rp ? [rp.Id] : i is RibbonStackModel st ? st.Children.Select(c => c.Id) : Array.Empty<string>())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var query = RibbonBankSearchBox.Text.Trim();
        RibbonBankList.Items.Clear();
        foreach (var tool in ribbonTools.Where(t => !placedIds.Contains(t.Id) && MatchesKoreanSearch(t.Title, query)))
            RibbonBankList.Items.Add(new System.Windows.Controls.ListBoxItem { Content = tool.Title + (tool.Installed ? "" : " · 미설치"), Tag = tool });
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
            var btn = new WpfButton { Content = spec is null ? "지원 예정" : "최신 정식 로더 적용 / 업데이트", Style = (Style)FindResource("PrimaryButton"), IsEnabled = spec is not null && !backend.IsBusy };
            DockPanel.SetDock(btn, Dock.Right);
            if (spec is not null) btn.Click += async (_, _) => await backend.ApplyLoaderForHostAsync(hostKey);
            dock.Children.Add(btn);

            var info = new StackPanel();
            info.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold });
            info.Children.Add(new TextBlock { Text = repo, FontSize = 11, Foreground = new SolidColorBrush(WpfColor.FromRgb(100, 116, 139)), Margin = new Thickness(0, 2, 0, 6) });
            var statusText = spec is null ? "Navisworks 호스트 로더는 향후 추가될 예정입니다." : FormatLoaderStatus(backend.GetLoaderStatus(spec));
            info.Children.Add(new TextBlock { Text = statusText, FontSize = 12, Foreground = new SolidColorBrush(WpfColor.FromRgb(51, 65, 85)), TextWrapping = TextWrapping.Wrap });
            dock.Children.Add(info);
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
        AccountStartupCheck.IsChecked = backend.StartWithWindowsChecked;
        AccountSignOutBtn.IsEnabled = backend.IsSignedIn;
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
                foreach (var st in p.Items.OfType<RibbonStackModel>())
                    if (payload is RibbonPlacement rp) st.Children.Remove(rp);
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

    private void OnSelectHostRevit(object sender, RoutedEventArgs e) { currentHost = "revit"; LoadRibbonState(); RefreshAllViews(); }
    private void OnSelectHostCad(object sender, RoutedEventArgs e) { currentHost = "autocad"; LoadRibbonState(); RefreshAllViews(); }
    private void OnSelectHostNavis(object sender, RoutedEventArgs e) { currentHost = "navisworks"; LoadRibbonState(); RefreshAllViews(); }
    private void OnSelectNavTools(object sender, RoutedEventArgs e) { currentNav = "tools"; RefreshAllViews(); }
    private void OnSelectNavRibbon(object sender, RoutedEventArgs e) { currentNav = "ribbon"; LoadRibbonState(); RefreshAllViews(); }
    private void OnSelectNavLoaders(object sender, RoutedEventArgs e) { currentNav = "loaders"; RefreshAllViews(); }
    private void OnSelectNavAccount(object sender, RoutedEventArgs e) { currentNav = "account"; RefreshAllViews(); }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e) { await backend.RefreshAllAsync(); RefreshAllViews(); }
    private async void OnBatchUpdateClicked(object sender, RoutedEventArgs e) { await backend.RunBatchUpdateForHostAsync(currentHost); RefreshAllViews(); }

    private void OnToolFilterChanged(object sender, TextChangedEventArgs e) { if (!syncUi) RenderToolsView(); }
    private void OnFilterAll(object sender, RoutedEventArgs e) { toolFilter = "all"; RenderToolsView(); }
    private void OnFilterInstalled(object sender, RoutedEventArgs e) { toolFilter = "installed"; RenderToolsView(); }
    private void OnFilterUpdate(object sender, RoutedEventArgs e) { toolFilter = "update"; RenderToolsView(); }
    private void OnFilterTest(object sender, RoutedEventArgs e) { toolFilter = "test"; RenderToolsView(); }

    private void OnZoomCompact(object sender, RoutedEventArgs e) { compactRibbon = true; RenderRibbonView(); }
    private void OnZoomNormal(object sender, RoutedEventArgs e) { compactRibbon = false; RenderRibbonView(); }
    private void OnRibbonStripMouseWheel(object sender, MouseWheelEventArgs e)
    {
        RibbonStripScroller.ScrollToHorizontalOffset(RibbonStripScroller.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    private void OnRibbonBankSearchChanged(object sender, TextChangedEventArgs e) { if (!syncUi) RefreshRibbonBank(); }
    private void OnRibbonBankMouseDown(object sender, MouseButtonEventArgs e) => dragStartPoint = e.GetPosition(null);
    private void OnRibbonBankMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || RibbonBankList.SelectedItem is not ListBoxItem { Tag: RibbonTool tool }) return;
        var diff = e.GetPosition(null) - dragStartPoint;
        if (Math.Abs(diff.X) > 6 || Math.Abs(diff.Y) > 6)
        {
            var rp = new RibbonPlacement(tool.Id, tool.Title);
            WpfDragDrop.DoDragDrop(RibbonBankList, new System.Windows.DataObject("RoBIM.RibbonItem", rp), System.Windows.DragDropEffects.Move);
        }
    }

    private void OnRibbonBankDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RibbonBankList.SelectedItem is not ListBoxItem { Tag: RibbonTool tool }) return;
        var tab = ribbonTabs[selectedTabIndex];
        if (tab.Panels.Count == 0) tab.Panels.Add(new RibbonPanelModel("도구"));
        var targetPanel = FindParentPanel(selectedRibbonItem) ?? tab.Panels[0];
        var rp = new RibbonPlacement(tool.Id, tool.Title);
        targetPanel.Items.Add(rp);
        selectedRibbonItem = rp;
        RenderRibbonView();
    }

    private void OnRibbonTreeSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (syncUi || e.NewValue is not TreeViewItem tvi) return;
        selectedRibbonItem = tvi.Tag;
        if (FindParentTab(selectedRibbonItem) is { } tab) selectedTabIndex = ribbonTabs.IndexOf(tab);
        RefreshRibbonInspector();
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
        RenderRibbonView();
    }

    private void OnArrangementChanged(object sender, SelectionChangedEventArgs e)
    {
        if (syncUi || RibbonArrangementBox.SelectedIndex < 0) return;
        var rows = RibbonArrangementBox.SelectedIndex == 0 ? 0 : RibbonArrangementBox.SelectedIndex + 1;
        if (selectedRibbonItem is RibbonStackModel st)
        {
            if (rows > 0) st.Rows = rows;
            RenderRibbonView();
        }
        else if (selectedRibbonItem is RibbonPlacement rp && rp.Kind != "Separator" && rows > 0 && FindParentStack(rp) is null)
        {
            var panel = FindParentPanel(rp);
            if (panel is null) return;
            var idx = panel.Items.IndexOf(rp);
            var newStack = new RibbonStackModel("stack-" + Guid.NewGuid().ToString("N"), rows);
            newStack.Children.Add(rp);
            panel.Items[idx] = newStack;
            RenderRibbonView();
        }
    }

    private void OnQuickMoveTabClicked(object sender, RoutedEventArgs e)
    {
        if (selectedRibbonItem is null || RibbonMoveTabBox.SelectedIndex < 0 || RibbonMoveTabBox.SelectedIndex >= ribbonTabs.Count) return;
        MoveRibbonPayloadToTab(selectedRibbonItem, ribbonTabs[RibbonMoveTabBox.SelectedIndex]);
        selectedTabIndex = RibbonMoveTabBox.SelectedIndex;
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
        panel.Items.Add(st);
        selectedRibbonItem = st;
        RenderRibbonView();
    }

    private void OnAddPanel(object sender, RoutedEventArgs e)
    {
        var tab = ribbonTabs[selectedTabIndex];
        var panel = new RibbonPanelModel("새 패널 " + (tab.Panels.Count + 1));
        tab.Panels.Add(panel);
        selectedRibbonItem = panel;
        RenderRibbonView();
    }

    private void OnAddSeparator(object sender, RoutedEventArgs e)
    {
        var tab = ribbonTabs[selectedTabIndex];
        var panel = FindParentPanel(selectedRibbonItem) ?? tab.Panels.FirstOrDefault();
        if (panel is null) return;
        var sep = new RibbonPlacement("separator:" + Guid.NewGuid().ToString("N"), "구분선", Kind: "Separator");
        panel.Items.Add(sep);
        selectedRibbonItem = sep;
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
            if (n >= 0 && n < st.Children.Count) { st.Children.RemoveAt(i); st.Children.Insert(n, rp); RenderRibbonView(); }
            return;
        }
        var panel = FindParentPanel(selectedRibbonItem);
        if (panel is not null && selectedRibbonItem != panel)
        {
            var i = panel.Items.IndexOf(selectedRibbonItem);
            var n = i + delta;
            if (n >= 0 && n < panel.Items.Count) { panel.Items.RemoveAt(i); panel.Items.Insert(n, selectedRibbonItem); RenderRibbonView(); }
        }
    }

    private void OnDeleteNode(object sender, RoutedEventArgs e)
    {
        if (selectedRibbonItem is null) return;
        if (selectedRibbonItem is RibbonTabModel tm && ribbonTabs.Count > 1) ribbonTabs.Remove(tm);
        else RemoveRibbonPayload(selectedRibbonItem);
        selectedRibbonItem = null;
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
            foreach (var p in t.Panels) { var pm = new RibbonPanelModel(p.Name); pm.Items.AddRange(p.Buttons); tm.Panels.Add(pm); }
            ribbonTabs.Add(tm);
        }
        selectedTabIndex = 0;
        RenderRibbonView();
    }

    private async void OnRibbonSave(object sender, RoutedEventArgs e)
    {
        try
        {
            var layout = SnapshotRibbonLayout();
            new RibbonLayouts(backend.CorePaths).Save(layout);
            BottomStatusText.Text = "리본 배치를 저장했습니다.";
        }
        catch (Exception ex) { WpfMessageBox.Show(this, ex.Message, "리본 저장"); }
    }

    private async void OnRibbonApply(object sender, RoutedEventArgs e)
    {
        try
        {
            var layout = SnapshotRibbonLayout();
            BottomStatusText.Text = await backend.SaveAndApplyRibbonForHostAsync(currentHost, layout);
        }
        catch (Exception ex) { WpfMessageBox.Show(this, ex.Message, "리본 적용"); }
    }

    private async void OnAccountSignIn(object sender, RoutedEventArgs e) { await backend.SignInOrRefreshFromModernAsync(); RefreshAllViews(); }
    private void OnAccountSignOut(object sender, RoutedEventArgs e) { backend.SignOutFromModern(); RefreshAllViews(); }
    private void OnOptionChanged(object sender, RoutedEventArgs e)
    {
        if (syncUi) return;
        backend.SetAllowPrereleaseOption(AccountTestCheck.IsChecked == true);
        backend.SetAutoLoginOption(AccountAutoLoginCheck.IsChecked == true);
        backend.SetStartWithWindowsOption(AccountStartupCheck.IsChecked == true);
        RefreshAllViews();
    }
}
