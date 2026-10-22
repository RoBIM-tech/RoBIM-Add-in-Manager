using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using RoBIM.Manager.Core;

namespace RoBIM.Manager;

public sealed partial class ManagerForm
{
    private bool IsProductPage => currentPage is "All" or "Mine" or "Revit" or "PyRevit" or "Cad" or "Test";
    private readonly TextBox productSearch = new() { PlaceholderText = "도구 이름 또는 기능 검색", Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
    private readonly ComboBox productSort = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly FlowLayoutPanel productCards = new() { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Padding = new(0, 0, 5, 0) };
    private readonly Label productCount = new() { AutoSize = true, ForeColor = Color.FromArgb(106, 117, 136), Padding = new(0, 8, 0, 0) };
    private readonly Dictionary<string, CardBinding> cardBindings = [];
    private readonly Dictionary<string, (Control Control, CardBinding Binding)> cachedCards = [];
    private sealed record CardBinding(ButtonCard Row, Button Action, Label Instruction, Label State, ToolTip Tips, Button More, ToolCardPanel Card);

    private void BuildProductsPage()
    {
        // Keep the existing selection adapters and handlers; cards dispatch to the same install services.
        InitializeProductSelection();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.AutoSize));
        var searchRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 5, Padding = new(0, 4, 0, 16), Margin = Padding.Empty };
        searchRow.ColumnStyles.Add(new(SizeType.Percent, 100)); searchRow.ColumnStyles.Add(new(SizeType.Absolute, 150));
        searchRow.ColumnStyles.Add(new(SizeType.AutoSize)); searchRow.ColumnStyles.Add(new(SizeType.AutoSize)); searchRow.ColumnStyles.Add(new(SizeType.AutoSize));
        productSearch.Margin = new(0, 4, 10, 0); productSort.Margin = new(0, 4, 10, 0);
        productSort.Items.AddRange(["기본 순서", "이름순", "설치된 도구 먼저", "업데이트 먼저"]); productSort.SelectedIndex = 0;
        var refresh = MakeButton("새로 고침"); var add = MakeButton("＋ 도구 등록"); add.Margin = Padding.Empty;
        searchRow.Controls.Add(productSearch, 0, 0); searchRow.Controls.Add(productSort, 1, 0);
        searchRow.Controls.Add(refresh, 2, 0); searchRow.Controls.Add(nativeReload, 3, 0); searchRow.Controls.Add(add, 4, 0);
        nativeReload.Click += async (_, _) =>
        {
            var page = currentPage;
            await Run("Revit Reload 적용 요청", async () =>
            {
                if (page != "PyRevit") new NativeRevitCommands(paths).RequestReload();
                if (page is not ("Revit" or "Test"))
                {
                    if (ReloadBridge.RevitRunning()) WriteLog(new RibbonLayouts(paths).RequestRevit(true));
                    else
                    {
                        var applied = await new RevitReload(paths).ApplyPendingSelectionWhenClosed(() => new RevitProfiles(paths).ApplySelection());
                        WriteLog(applied ? "준비된 버튼 선택을 설치했습니다. 다음 Revit 시작 시 로드합니다." : "적용할 pyRevit 버튼 선택이 없습니다.");
                        if (applied) await RefreshStatuses();
                    }
                }
                else WriteLog("독립 Add-in Reload 요청 완료");
            });
        };
        layout.Controls.Add(searchRow, 0, 0); layout.Controls.Add(productCards, 0, 1); layout.Controls.Add(productCount, 0, 2);
        productsPage.Controls.Add(layout); layout.BringToFront();
        var searchDelay = new System.Windows.Forms.Timer { Interval = 180 };
        searchDelay.Tick += (_, _) => { searchDelay.Stop(); RenderProductCards(); };
        productSearch.TextChanged += (_, _) => { if (preview) RenderProductCards(); else { searchDelay.Stop(); searchDelay.Start(); } };
        FormClosed += (_, _) => searchDelay.Dispose();
        FormClosed += (_, _) => { foreach (var item in cachedCards.Values) item.Control.Dispose(); cachedCards.Clear(); };
        productSort.SelectedIndexChanged += (_, _) => RenderProductCards();
        productCards.ClientSizeChanged += (_, _) => SizeProductCards();
        refresh.Click += async (_, _) => await RefreshWithUpdates();
        add.Click += (_, _) => SwitchPage("Extensions");
    }

    private void RenderProductCards()
    {
        if (!IsProductPage) return;
        var scroll = productCards.VerticalScroll.Value;
        IEnumerable<ButtonCard> rows = ProductCommands(currentPage);
        rows = rows.Where(x => currentPage switch
        {
            "Test" => x.CollectionChannel == "test",
            "Revit" => x.CollectionChannel != "test" && x.Kind == ButtonKind.Native,
            "PyRevit" => x.Kind == ButtonKind.PyRevit,
            "Cad" => x.CollectionChannel != "test" && x.Owner.Status.Tool.Kind == ToolKind.AutoCAD,
            "Mine" => x.Selected || x.Pending,
            _ => x.CollectionChannel != "test"
        });
        var search = productSearch.Text.Trim();
        if (search.Length > 0) rows = rows.Where(x => string.Join(" ", x.Title, x.Description, x.Group, Platform(x.Owner.Status.Tool.Kind), KindLabel(x.Owner.Status.Tool.Kind)).Contains(search, StringComparison.CurrentCultureIgnoreCase));
        rows = productSort.SelectedIndex switch
        {
            1 => rows.OrderBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase),
            2 => rows.OrderByDescending(SelectedInView).ThenBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase),
            3 => rows.OrderByDescending(x => AppearanceFor(x).Update).ThenBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase),
            _ => rows.OrderByDescending(x => x.Kind == ButtonKind.Native)
        };
        var visible = rows.ToList();
        productCards.SuspendLayout();
        try
        {
        // Dispose replaced cards, including their context menus and drawing resources.
        foreach (var control in productCards.Controls.Cast<Control>().ToArray())
            if (preview || control is not ToolCardPanel) control.Dispose();
        productCards.Controls.Clear();
        cardBindings.Clear();
        foreach (var row in visible)
        {
            if (!preview && cachedCards.TryGetValue(row.Id, out var cached))
            {
                if (cached.Binding.Row == row)
                { cardBindings[row.Id] = cached.Binding; productCards.Controls.Add(cached.Control); continue; }
                cached.Control.Dispose(); cachedCards.Remove(row.Id);
            }
            var card = CreateCommandCard(row); productCards.Controls.Add(card);
            if (!preview) cachedCards[row.Id] = (card, cardBindings[row.Id]);
        }
        if (!preview)
        {
            var known = commandSnapshot.Select(x => x.Id).ToHashSet();
            foreach (var key in cachedCards.Keys.Where(x => !known.Contains(x)).ToArray()) { cachedCards[key].Control.Dispose(); cachedCards.Remove(key); }
        }
        if (visible.Count == 0) productCards.Controls.Add(new Label { Text = search.Length > 0 ? "조건에 맞는 도구가 없습니다." : currentPage == "Mine" ? "아직 설치되거나 연결된 도구가 없습니다." : "등록된 도구를 확인하고 있습니다. 새로 고침 또는 도구 등록을 이용하십시오.", AutoSize = false, Height = 80, ForeColor = Color.FromArgb(106, 117, 136), TextAlign = ContentAlignment.MiddleCenter });
        productCount.Text = $"{visible.Count}개 버튼  ·  업데이트 {visible.Count(x => AppearanceFor(x).Update)}  ·  설치 {visible.Count(SelectedInView)}  ·  미설치 {visible.Count(x => !SelectedInView(x) && !x.Pending)}  |  주황: 업데이트 · 녹색: 설치 · 회색: 미설치 · 연보라: 새 TEST 있음" + (preview ? "  ·  화면 확인용 예시 상태" : "");
        }
        finally { productCards.ResumeLayout(true); }
        SizeProductCards();
        productCards.AutoScrollPosition = new Point(0, scroll);
        RefreshCardActions();
    }

    private static string Platform(ToolKind kind) => kind == ToolKind.AutoCAD ? "AutoCAD" : "Revit";
    private static string KindLabel(ToolKind kind) => kind == ToolKind.PyRevit ? "pyRevit" : "독립 Add-in";
    private static string StatusLabel(ToolStatus status) => status.Modified ? "확인 필요" : status.External ? "기존 설치 연결" : status.Managed ? "설치됨" : "설치 안 됨";

    private Control CreateCommandCard(ButtonCard feature)
    {
        var status = feature.Owner.Status; var scale = DeviceDpi / 96f;
        var card = new ToolCardPanel { Height = (int)(88 * scale), Margin = new(0, 0, (int)(8 * scale), (int)(8 * scale)), Padding = new((int)(8 * scale)), BackColor = Color.FromArgb(246, 247, 249), AccessibleName = feature.Title };
        var tips = new ToolTip { AutoPopDelay = 15000, InitialDelay = 450, ReshowDelay = 100 };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty, BackColor = Color.Transparent };
        layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.Absolute, 32 * scale)); layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.Absolute, 22 * scale));
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty, BackColor = Color.Transparent };
        header.ColumnStyles.Add(new(SizeType.Absolute, 36 * scale)); header.ColumnStyles.Add(new(SizeType.Percent, 100));
        header.ColumnStyles.Add(new(SizeType.Absolute, 96 * scale)); header.ColumnStyles.Add(new(SizeType.Absolute, 30 * scale));
        header.RowStyles.Add(new(SizeType.Percent, 100));
        Control icon = new ToolGlyph(status.Tool.Kind) { Size = new((int)(28 * scale), (int)(28 * scale)), Anchor = AnchorStyles.Left, Margin = Padding.Empty };
        if (feature.Icon is not null && File.Exists(feature.Icon))
        {
            try
            {
                SafeFiles.NoLinks(feature.Icon);
                if (new FileInfo(feature.Icon).Length > 2 * 1024 * 1024) throw new IOException("아이콘 크기 초과");
                using var stream = File.OpenRead(feature.Icon); using var image = Image.FromStream(stream);
                var picture = new PictureBox { Size = icon.Size, Anchor = icon.Anchor, Margin = Padding.Empty, SizeMode = PictureBoxSizeMode.Zoom, Image = new Bitmap(image) };
                picture.Disposed += (_, _) => picture.Image?.Dispose(); icon.Dispose(); icon = picture;
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or OutOfMemoryException) { }
        }
        header.Controls.Add(icon, 0, 0);
        var text = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, BackColor = Color.Transparent };
        text.ColumnStyles.Add(new(SizeType.Percent, 100)); text.RowStyles.Add(new(SizeType.Absolute, 12 * scale)); text.RowStyles.Add(new(SizeType.Percent, 100));
        var category = new Label { Text = feature.ReleaseLabel + "  ·  " + Platform(status.Tool.Kind) + "  ·  " + KindLabel(status.Tool.Kind) + "  ·  " + feature.Group, Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = Color.FromArgb(49, 92, 164), Font = new Font(Font.FontFamily, 7.5f), Margin = Padding.Empty };
        var title = new Label { Text = feature.Title, Dock = DockStyle.Fill, AutoEllipsis = true, Font = new Font(Font.FontFamily, 10.5f, FontStyle.Bold), Margin = Padding.Empty };
        text.Controls.Add(category, 0, 0); text.Controls.Add(title, 0, 1); header.Controls.Add(text, 1, 0); layout.Controls.Add(header, 0, 0);
        var description = new Label { Text = feature.Description, Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = Color.FromArgb(102, 112, 131), Font = new Font(Font.FontFamily, 8.5f), Margin = Padding.Empty };
        layout.Controls.Add(description, 0, 1);
        var stateLabel = new LinkLabel { Text = feature.Message, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Font = new Font(Font.FontFamily, 8), ForeColor = Color.FromArgb(102, 112, 131), Margin = new(0, 0, (int)(6 * scale), 0) };
        var more = MakeButton("···"); more.MinimumSize = new((int)(30 * scale), (int)(22 * scale)); more.AutoSize = false; more.Dock = DockStyle.Fill; more.Margin = Padding.Empty; more.Padding = Padding.Empty; more.AccessibleName = feature.Title + " 관리 메뉴";
        var primary = MakeButton("설치하기", true); primary.Font = new Font(Font.FontFamily, 8.5f); primary.MinimumSize = new((int)(80 * scale), (int)(22 * scale)); primary.AutoSize = false; primary.Dock = DockStyle.Fill; primary.Padding = Padding.Empty; primary.Margin = new(0, 0, (int)(6 * scale), 0);
        primary.Dock = more.Dock = DockStyle.None;
        primary.Anchor = more.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        primary.Size = new((int)(90 * scale), (int)(22 * scale)); more.Size = new((int)(30 * scale), (int)(22 * scale));
        primary.Margin = new(0, (int)(4 * scale), (int)(6 * scale), 0); more.Margin = new(0, (int)(4 * scale), 0, 0);
        primary.Click += async (_, _) => { if (busy || preview) return; await PrimaryCardAction(feature); };
        stateLabel.LinkClicked += (_, _) => { if (!busy) ShowTestOffer(feature); };
        var menu = BuildButtonMenu(feature); more.Click += (_, _) => menu.Show(more, new Point(0, more.Height));
        var instruction = new Label { Visible = false };
        card.Disposed += (_, _) => { menu.Dispose(); tips.Dispose(); instruction.Dispose(); };
        header.Controls.Add(primary, 2, 0); header.Controls.Add(more, 3, 0); layout.Controls.Add(stateLabel, 0, 2);
        tips.SetToolTip(category, category.Text); tips.SetToolTip(title, feature.Title); tips.SetToolTip(description, feature.Description);
        card.Controls.Add(layout); cardBindings[feature.Id] = new(feature, primary, instruction, stateLabel, tips, more, card);
        return card;
    }

    private ContextMenuStrip BuildCardMenu(ProductRow row)
    {
        var menu = new ContextMenuStrip(); var status = row.Status;
        menu.Items.Add("도구 상세 정보", null, (_, _) => ShowProductDetails(row));
        menu.Items.Add("설치 폴더 열기", null, (_, _) => { if (!preview) OpenFolder(status.Path); });
        menu.Items.Add("GitHub 저장소", null, (_, _) => { if (!preview) OpenUrl("https://github.com/" + status.Tool.Repository); });
        menu.Items.Add("공용 패키지 설치 / 업데이트", null, async (_, _) => { if (!busy && !preview) { SelectProduct(status.Tool.Id); await InstallProduct(); } });
        menu.Items.Add(new ToolStripSeparator());
        var restoreItem = menu.Items.Add("이전 버전 복원", null, async (_, _) => { if (!busy && !preview) { SelectProduct(status.Tool.Id); await ManageProduct(true); } });
        restoreItem.Enabled = !status.External && !status.Modified;
        var removeItem = menu.Items.Add("도구 제거", null, async (_, _) => { if (!busy && !preview) { SelectProduct(status.Tool.Id); await ManageProduct(false); } });
        removeItem.Enabled = status.Managed && !status.Modified;
        if (status.Tool.Id == Catalog.Revit.Id)
        {
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Revit 사용자 프로필 적용", null, async (_, _) => { if (!busy && !preview) await ApplyProfile(false); });
            menu.Items.Add("Revit Reload용 업데이트 준비", null, async (_, _) => { if (!busy && !preview) { SelectLegacy(status.Tool.Id); await PrepareUserReload(); } });
        }
        if (status.Tool.Id == Catalog.Cad.Id)
        {
            menu.Items.Add(new ToolStripSeparator());
            var offlineItem = menu.Items.Add("CAD 동봉 버전 설치", null, async (_, _) => { if (!busy && !preview) { SelectLegacy(status.Tool.Id); await InstallSelected(true); } });
            offlineItem.Enabled = !status.External && !status.Modified;
            var download = menu.Items.Add("최신 기능 다운로드 / Reload 준비", null, async (_, _) => { if (!busy && !preview) { SelectProduct(status.Tool.Id); await ConfigureCadButtons(true); } });
            var zip = menu.Items.Add("패키지 ZIP에서 Reload 준비", null, async (_, _) => { if (!busy && !preview) { SelectProduct(status.Tool.Id); await PrepareCadArchive(); } });
            download.Enabled = zip.Enabled = CadConfigurable(row);
        }
        return menu;
    }

    private void ShowProductDetails(ProductRow row)
    {
        using var dialog = new Form { Text = row.Status.Tool.Name, Size = new(720, 400), MinimumSize = new(560, 320), StartPosition = FormStartPosition.CenterParent, Font = Font };
        dialog.Controls.Add(new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, BackColor = Color.White, Text = string.Join(Environment.NewLine + Environment.NewLine, row.Status.Tool.Description, row.Package?.Package.Description, "유형: " + Platform(row.Status.Tool.Kind) + " · " + KindLabel(row.Status.Tool.Kind), "현재 상태: " + StatusLabel(row.Status) + " · " + row.Status.Detail, "설치 버전: " + row.Status.Version, "설치 경로: " + row.Status.Path, "저장소: " + row.Status.Tool.Repository) });
        dialog.ShowDialog(this);
    }

    private void SelectProduct(string id)
    {
        if (productRows.Any(x => x.Status.Tool.Id == id)) { selectedProductId = id; return; }
        throw new IOException("설치 상태를 새로 고친 뒤 다시 시도하십시오.");
    }

    private void RefreshCardActions()
    {
        nativeReload.Visible = IsProductPage && currentPage != "Cad";
        nativeReload.Text = currentPage is "Revit" or "Test" ? "독립 Add-in Reload" : currentPage == "PyRevit" ? "pyRevit Reload" : "Revit 전체 Reload";
        var ready = !busy && !refreshingSnapshot && (preview || snapshotVerified);
        nativeReload.Enabled = ready && !preview;
        foreach (var binding in cardBindings.Values)
        {
            var feature = binding.Row;
            var appearance = AppearanceFor(feature);
            binding.Action.Text = PrimaryText(feature, appearance);
            binding.Action.Enabled = ready && appearance.Enabled;
            binding.More.Enabled = ready;
            var updateState = appearance.Update;
            var selected = SelectedInView(feature);
            var testAvailable = selected && feature.SelectedChannel == "stable" && NewTestAvailable(feature);
            binding.Card.BackColor = updateState ? Color.FromArgb(255, 246, 232) : testAvailable ? Color.FromArgb(246, 243, 250) : selected ? Color.FromArgb(240, 249, 243) : Color.FromArgb(246, 247, 249);
            binding.Card.BorderColor = updateState ? Color.FromArgb(223, 164, 78) : testAvailable ? Color.FromArgb(190, 179, 207) : selected ? Color.FromArgb(155, 195, 169) : Color.FromArgb(214, 220, 229);
            binding.State.ForeColor = updateState ? Color.FromArgb(143, 79, 14) : testAvailable ? Color.FromArgb(102, 81, 131) : selected ? Color.FromArgb(39, 105, 67) : Color.FromArgb(102, 112, 131);
            binding.Card.Invalidate();
            binding.State.Text = (updateState ? "업데이트 · " : selected ? "설치 · " : feature.Pending ? "변경 준비 · " : "미설치 · ") + appearance.Message;
            if (binding.State is LinkLabel link)
            {
                link.Links.Clear();
                var at = link.Text.LastIndexOf("TEST 보기", StringComparison.Ordinal);
                if (at >= 0)
                {
                    link.LinkColor = link.VisitedLinkColor = Color.FromArgb(102, 81, 131);
                    link.ActiveLinkColor = Color.FromArgb(79, 59, 108);
                    link.Links.Add(at, "TEST 보기".Length);
                }
            }
            binding.Instruction.Text = appearance.Instruction;
            var detail = appearance.Message + "\n" + appearance.Instruction;
            if (binding.Tips.GetToolTip(binding.State) != detail) binding.Tips.SetToolTip(binding.State, detail);
            if (binding.Tips.GetToolTip(binding.Action) != appearance.Instruction) binding.Tips.SetToolTip(binding.Action, appearance.Instruction);
        }
    }
    private bool sizingProductCards;
    private static int CardColumnCount(int availableWidth, int dpi) => Math.Clamp((availableWidth + 8 * dpi / 96) / (400 * dpi / 96 + 8 * dpi / 96), 1, 3);
    private void SizeProductCards()
    {
        if (sizingProductCards) return;
        sizingProductCards = true;
        try
        {
            var available = Math.Max(100, productCards.ClientSize.Width - productCards.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 2);
            var columns = CardColumnCount(available, DeviceDpi); var gap = 8 * DeviceDpi / 96;
            var width = (available - gap * (columns - 1)) / columns;
            productCards.SuspendLayout(); var index = 0;
            foreach (Control card in productCards.Controls)
            {
                var isCard = card is ToolCardPanel;
                card.Width = isCard ? width : available;
                card.Margin = isCard ? new Padding(0, 0, ++index % columns == 0 ? 0 : gap, gap) : Padding.Empty;
            }
            productCards.ResumeLayout(true);
        }
        finally { sizingProductCards = false; }
    }

    private sealed class ToolCardPanel : Panel
    {
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Color BorderColor { get; set; } = Color.FromArgb(224, 229, 237);
        public ToolCardPanel() { DoubleBuffered = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = new GraphicsPath(); var radius = 9f * DeviceDpi / 96f; var rect = new RectangleF(.5f, .5f, Width - 1.5f, Height - 1.5f);
            path.AddRoundedRectangle(rect, new SizeF(radius, radius));
            using var pen = new Pen(BorderColor); e.Graphics.DrawPath(pen, path);
        }
    }

    private sealed class ToolGlyph(ToolKind kind) : Control
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var s = Width / 58f; e.Graphics.ScaleTransform(s, s);
            using var bg = new SolidBrush(Color.FromArgb(237, 242, 250)); using var pen = new Pen(Color.FromArgb(49, 92, 164), 1.8f) { LineJoin = LineJoin.Round };
            using var shape = new GraphicsPath(); shape.AddRoundedRectangle(new RectangleF(0, 0, 57, 57), new SizeF(10, 10)); e.Graphics.FillPath(bg, shape);
            if (kind == ToolKind.AutoCAD)
            {
                e.Graphics.DrawLines(pen, [new(17, 42), new(29, 15), new(41, 42)]); e.Graphics.DrawLine(pen, 22, 32, 36, 32); e.Graphics.DrawEllipse(pen, 25, 12, 8, 8);
            }
            else if (kind == ToolKind.NativeRevit)
            {
                e.Graphics.DrawRectangle(pen, 17, 13, 23, 29); e.Graphics.DrawLines(pen, [new(13, 18), new(13, 46), new(35, 46)]);
                e.Graphics.DrawLine(pen, 22, 22, 35, 22); e.Graphics.DrawLine(pen, 22, 28, 35, 28); e.Graphics.DrawLine(pen, 22, 34, 31, 34);
            }
            else
            {
                e.Graphics.DrawRectangle(pen, 14, 24, 13, 18); e.Graphics.DrawRectangle(pen, 30, 15, 13, 27); e.Graphics.DrawLine(pen, 20, 29, 20, 35); e.Graphics.DrawLine(pen, 36, 21, 36, 35);
            }
        }
    }
}
