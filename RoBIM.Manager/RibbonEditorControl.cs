using System.Drawing;
using RoBIM.Manager.Core;

namespace RoBIM.Manager;

internal sealed partial class RibbonEditorControl : UserControl
{
    private readonly ListBox bank = new() { Dock = DockStyle.Fill, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 28 };
    private readonly TextBox bankSearch = new() { Dock = DockStyle.Top, PlaceholderText = "미배치 버튼 검색..." };
    private readonly TreeView tree = new() { Dock = DockStyle.Fill, AllowDrop = true, HideSelection = false, FullRowSelect = true, ItemHeight = 26 };
    private readonly Panel preview = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(250, 250, 250) };
    private readonly Panel tabsBar = new() { Dock = DockStyle.Top, Height = 32, BackColor = Color.FromArgb(241, 243, 246) };
    private readonly FlowLayoutPanel tabsHost = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new(6, 2, 0, 0) };
    private readonly FlowLayoutPanel stripHost = new() { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Color.FromArgb(250, 250, 250), Padding = new(6, 4, 6, 4) };
    private readonly Button zoomCompactBtn = new() { Text = "작게", Width = 48, Height = 24, Margin = new(2, 2, 0, 2) };
    private readonly Button zoomNormalBtn = new() { Text = "보통", Width = 48, Height = 24, Margin = new(2, 2, 4, 2) };
    private readonly TextBox caption = new() { Width = 180 };
    private readonly ComboBox arrangement = new() { Width = 130, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox moveTabBox = new() { Width = 130, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button moveTabBtn = new() { Text = "이동", Width = 52, Height = 25, Margin = new(4, 0, 0, 0) };
    private readonly Label status = new() { AutoSize = true, ForeColor = Color.FromArgb(75, 95, 125), MaximumSize = new(940, 0) };
    private readonly ToolTip tips = new();
    private readonly RibbonLayouts store;
    private readonly bool example;
    private readonly string host;
    private List<RibbonTool> tools = [];
    private bool updating;
    private bool compactPreview = true;
    private sealed record StackNode(string Id, int Rows);
    private sealed record BankTool(RibbonTool Tool) { public override string ToString() => Tool.Title + (Tool.Installed ? "" : "  · 미설치"); }
    internal IEnumerable<string> AvailableIds => bank.Items.Cast<BankTool>().Select(x=>x.Tool.Id);
    internal TreeNodeCollection EditorNodes => tree.Nodes;
    internal object[] ManagementPreviewImages()
    {
        IEnumerable<Control> All(Control parent)=>parent.Controls.Cast<Control>().SelectMany(c=>new[]{c}.Concat(All(c)));
        return All(preview).OfType<PictureBox>().Where(p=>p.Image is not null && p.Tag is TreeNode{Tag:RibbonPlacement placement} && ManagementButtonIcons.IsManagement(placement.Id))
            .Select(p=>(object)new {Id=((RibbonPlacement)((TreeNode)p.Tag!).Tag!).Id,Width=p.Image!.Width,Height=p.Image.Height}).ToArray();
    }

    internal string? PreviewedTab => (SelectedTab() ?? tree.Nodes.Cast<TreeNode>().FirstOrDefault())?.Text;
    internal void SelectPreviewTab(int index) => tree.SelectedNode = tree.Nodes[index];
    internal bool PreviewSpansEditorWidth => preview.Width >= ClientSize.Width - 20 && preview.Top < tree.Parent!.Top;
    internal Func<RibbonLayout, Task<string>>? ApplyRequested;
    internal Func<Task>? RefreshRequested;
    public RibbonEditorControl(RibbonLayouts store, bool example, string host = "Revit")
    {
        this.store = store; this.example = example; this.host = host; Dock = DockStyle.Fill;
        Disposed += (_, _) => tips.Dispose();
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.AutoSize));

        var zoomBar = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Padding = new(4, 2, 4, 0) };
        zoomBar.Controls.Add(zoomCompactBtn); zoomBar.Controls.Add(zoomNormalBtn);
        zoomCompactBtn.Click += (_, _) => { compactPreview = true; Changed(); };
        zoomNormalBtn.Click += (_, _) => { compactPreview = false; Changed(); };
        tabsBar.Controls.Add(tabsHost); tabsBar.Controls.Add(zoomBar);
        preview.Controls.Add(stripHost); preview.Controls.Add(tabsBar);
        stripHost.MouseWheel += (_, e) =>
        {
            if (!stripHost.HorizontalScroll.Visible) return;
            var next = Math.Clamp(stripHost.HorizontalScroll.Value - e.Delta, stripHost.HorizontalScroll.Minimum, Math.Max(0, stripHost.HorizontalScroll.Maximum - stripHost.ClientSize.Width + 1));
            stripHost.HorizontalScroll.Value = next;
            stripHost.PerformLayout();
            if (e is HandledMouseEventArgs h) h.Handled = true;
        };

        var actions = new FlowLayoutPanel { AutoScroll = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
        Button Add(string label, Action action) { var b = new Button { Text = label, Width = 94, Height = 30, Margin = new(0, 0, 4, 5) }; b.Click += (_, _) => Guard(action); actions.Controls.Add(b); return b; }
        Add("＋ 탭", () => { var tab = AddTab("새 탭"); tree.SelectedNode = tab; Changed(); });
        Add("＋ 패널", () => { var tab = SelectedTab() ?? AddTab("RoBIM 도구"); var panel = new TreeNode(Unique("새 패널", tab.Nodes)) { Tag = "Panel" }; tab.Nodes.Add(panel); tree.SelectedNode = panel; tab.Expand(); Changed(); });
        Add("＋ 2단 묶음", () => AddStack(2)); Add("＋ 3단 묶음", () => AddStack(3));
        Add("＋ 구분선", () => AddSeparator());
        Add("↑", () => MoveSelected(-1)); Add("↓", () => MoveSelected(1));
        Add("배치 삭제", () => { tree.SelectedNode?.Remove(); Changed(); });
        Add("기본 배치", () => { if (MessageBox.Show(this, "편집 중인 배치를 설치된 버튼의 기본 위치로 되돌립니까?", "기본 배치", MessageBoxButtons.YesNo) == DialogResult.Yes) LoadLayout(RibbonLayouts.Defaults(host, tools)); });
        var refresh = Add("새로 읽기", () => { }); refresh.Click += async (_, _) => { if (RefreshRequested is not null) await RefreshRequested(); };
        var save = Add("저장", () => { var layout = Snapshot(); if (!example) store.Save(layout); status.Text = example ? "미리보기 예시입니다." : "저장했습니다. 적용 버튼을 눌러 반영하세요."; });
        var apply = Add(host + " 적용", () => { }); apply.BackColor = Color.FromArgb(30, 91, 166); apply.ForeColor = Color.White;
        actions.Controls.SetChildIndex(save, 0); actions.Controls.SetChildIndex(apply, 1);
        apply.Click += async (_, _) => { var saved = false; try { var layout = Snapshot(); if (example) { status.Text = "미리보기 예시입니다."; return; } store.Save(layout); saved = true; if (ApplyRequested is not null) status.Text = await ApplyRequested(layout); } catch (Exception e) { status.Text = saved ? "배치는 저장되었으나 적용 준비에 실패했습니다." : "배치를 저장하지 못했습니다."; MessageBox.Show(this, status.Text + "\n" + e.Message, "리본 적용"); } };
        if (host == "Revit") Add("결과 확인", () => status.Text = store.RevitResult());

        var bankHost = new Panel { Dock = DockStyle.Fill };
        bankHost.Controls.Add(bank); bankHost.Controls.Add(bankSearch);
        bankSearch.TextChanged += (_, _) => RefreshBank();

        var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2 };
        columns.ColumnStyles.Add(new(SizeType.Percent, 36)); columns.ColumnStyles.Add(new(SizeType.Percent, 64)); columns.ColumnStyles.Add(new(SizeType.Absolute, 215)); columns.RowStyles.Add(new(SizeType.Absolute, 26)); columns.RowStyles.Add(new(SizeType.Percent, 100));
        foreach (var (title, col) in new[] { ("배치 가능한 버튼", 0), ("탭 / 패널 / 버튼 묶음", 1), ("편집 / 저장", 2) }) columns.Controls.Add(new Label { Text = title, AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, col, 0);
        columns.Controls.Add(bankHost, 0, 1); columns.Controls.Add(tree, 1, 1); columns.Controls.Add(actions, 2, 1);

        var bottomPane = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        bottomPane.ColumnStyles.Add(new(SizeType.Percent, 100));
        bottomPane.RowStyles.Add(new(SizeType.AutoSize)); bottomPane.RowStyles.Add(new(SizeType.Percent, 100));
        bottomPane.Controls.Add(new Label { AutoSize = true, MaximumSize = new(1000, 0), Padding = new(0, 4, 0, 6), Text = "상단 탭은 스크롤과 무관하게 고정됩니다. 뒤쪽 버튼을 상단 탭으로 드래그하거나 하단 '다른 탭으로 이동'으로 옮기세요. 미리보기 위 마우스 휠로 좌우 스크롤합니다." }, 0, 0);
        bottomPane.Controls.Add(columns, 0, 1);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 6, Panel1MinSize = 115, Panel2MinSize = 160, SplitterDistance = 138 };
        split.Panel1.Controls.Add(preview);
        split.Panel2.Controls.Add(bottomPane);
        root.Controls.Add(split, 0, 0);

        var properties = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new(0, 6, 0, 6) };
        properties.Controls.Add(new Label { Text = "표시 이름", AutoSize = true, Padding = new(0, 5, 4, 0) }); properties.Controls.Add(caption);
        properties.Controls.Add(new Label { Text = "한 칸 구성", AutoSize = true, Padding = new(10, 5, 4, 0) }); arrangement.Items.AddRange(["큰 버튼 1개", "작은 버튼 2개", "작은 버튼 3개"]); arrangement.SelectedIndex = 0; properties.Controls.Add(arrangement);
        properties.Controls.Add(new Label { Text = "다른 탭으로 이동", AutoSize = true, Padding = new(10, 5, 4, 0) }); properties.Controls.Add(moveTabBox); properties.Controls.Add(moveTabBtn);
        moveTabBtn.Click += (_, _) => Guard(MoveSelectedToChosenTab);
        root.Controls.Add(properties, 0, 1); root.Controls.Add(status, 0, 2); Controls.Add(root);
        bank.DrawItem += (_, e) => { if (e.Index < 0) return; e.DrawBackground(); var item = (BankTool)bank.Items[e.Index]; TextRenderer.DrawText(e.Graphics, item.ToString(), Font, e.Bounds, item.Tool.Installed ? ForeColor : Color.Gray, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter); e.DrawFocusRectangle(); };
        bank.MouseDown += (_, e) => { var index = bank.IndexFromPoint(e.Location); if (e.Button == MouseButtons.Left && index >= 0) { bank.SelectedIndex = index; StartPreviewDrag(((BankTool)bank.Items[index]).Tool.Id); } };
        bank.DoubleClick += (_, _) => { if (bank.SelectedItem is BankTool item) Guard(() => AddTool(item.Tool.Id, tree.SelectedNode)); };
        tree.ItemDrag += (_, e) => { if (e.Item is TreeNode node) StartPreviewDrag(node); };
        tree.DragOver += (_, e) => { var target = tree.GetNodeAt(tree.PointToClient(new Point(e.X, e.Y))); if (target is not null) tree.SelectedNode = target; e.Effect = e.Data?.GetDataPresent(typeof(TreeNode)) == true ? DragDropEffects.Move : e.Data?.GetDataPresent(typeof(string)) == true ? DragDropEffects.Copy : DragDropEffects.None; };
        tree.DragDrop += (_, e) => Guard(() => { var target = tree.GetNodeAt(tree.PointToClient(new Point(e.X, e.Y))); if (e.Data?.GetData(typeof(TreeNode)) is TreeNode moved) DropNode(moved, target); else if (e.Data?.GetData(typeof(string)) is string id) AddTool(id, target); });
        tree.AfterSelect += (_, _) => { UpdateProperties(); Changed(); };
        caption.TextChanged += (_, _) => { if (updating || tree.SelectedNode is not { } node || node.Tag is StackNode or RibbonPlacement { Kind: "Separator" }) return; node.Text = caption.Text; if (node.Tag is RibbonPlacement p) node.Tag = p with { Title = caption.Text }; Changed(); };
        arrangement.SelectedIndexChanged += (_, _) => { if (updating) return; Guard(() => SetArrangement(arrangement.SelectedIndex == 0 ? 0 : arrangement.SelectedIndex + 1)); UpdateProperties(); };
        tree.KeyDown += (_, e) => { if (e.Control && e.KeyCode is Keys.Up or Keys.Down) { MoveSelected(e.KeyCode == Keys.Up ? -1 : 1); e.Handled = true; } };
    }
    private void Guard(Action action) { try { action(); } catch (Exception e) { MessageBox.Show(this, e.Message, "리본 편집"); } }
    private static string Unique(string basis, TreeNodeCollection nodes) { var name = basis; for (var i = 2; nodes.Cast<TreeNode>().Any(x => x.Text == name); i++) name = basis + " " + i; return name; }
    private TreeNode AddTab(string name) { var tab = new TreeNode(Unique(name, tree.Nodes)) { Tag = "Tab" }; tree.Nodes.Add(tab); return tab; }
    private TreeNode? SelectedTab() { var node = tree.SelectedNode; while (node?.Parent is not null) node = node.Parent; return node; }
    private static TreeNode? PanelOf(TreeNode? node) { while (node is not null && node.Tag as string != "Panel") node = node.Parent; return node; }
    private TreeNode EnsurePanel(TreeNode? target)
    {
        if (PanelOf(target) is { } panel) return panel;
        var tab = target?.Tag as string == "Tab" ? target : SelectedTab() ?? AddTab("RoBIM 도구");
        var result = tab.Nodes.Cast<TreeNode>().FirstOrDefault() ?? new TreeNode("도구") { Tag = "Panel" }; if (result.Parent is null) tab.Nodes.Add(result); return result;
    }
    private static IEnumerable<TreeNode> Descendants(TreeNodeCollection nodes) { foreach (TreeNode node in nodes) { yield return node; foreach (var child in Descendants(node.Nodes)) yield return child; } }
    internal TreeNode AddStack(int rows, TreeNode? target = null)
    {
        if (rows is not (2 or 3)) throw new IOException("2단 또는 3단 묶음을 선택하세요.");
        var panel = EnsurePanel(target ?? tree.SelectedNode); var node = new TreeNode { Tag = new StackNode("stack-" + Guid.NewGuid().ToString("N"), rows) }; panel.Nodes.Add(node); tree.SelectedNode = node; panel.Expand(); Changed(); return node;
    }
    internal TreeNode AddSeparator(TreeNode? target = null)
    {
        target ??= tree.SelectedNode;
        var panel = EnsurePanel(target);
        var anchor = target;
        while (anchor?.Parent is not null && anchor.Parent != panel) anchor = anchor.Parent;
        var node = new TreeNode("│ 구분선") { Tag = new RibbonPlacement("separator:" + Guid.NewGuid().ToString("N"), "구분선", Kind: "Separator") };
        if (anchor?.Parent == panel) panel.Nodes.Insert(anchor.Index + 1, node); else panel.Nodes.Add(node);
        tree.SelectedNode = node; panel.Expand(); Changed(); return node;
    }
    private static void CheckCapacity(TreeNode container, TreeNode? moving = null)
    {
        if (container.Tag is StackNode group && container.Nodes.Count - (moving?.Parent == container ? 1 : 0) >= group.Rows) throw new IOException($"이 묶음에는 버튼을 {group.Rows}개까지 넣을 수 있습니다.");
    }
    internal void AddTool(string id, TreeNode? target = null)
    {
        var tool = tools.Single(x => x.Id == id); var existing = Descendants(tree.Nodes).FirstOrDefault(n => n.Tag is RibbonPlacement b && b.Id == id);
        var container = target?.Tag is RibbonPlacement ? target.Parent! : target?.Tag is StackNode ? target : EnsurePanel(target);
        CheckCapacity(container, existing);
        var node = existing ?? new TreeNode(tool.Title) { Tag = new RibbonPlacement(id, tool.Title) }; node.Remove(); container.Nodes.Add(node); container.Expand(); container.Parent?.Expand(); tree.SelectedNode = node; Changed();
    }
    internal void DropNode(TreeNode node, TreeNode? target, bool after = false)
    {
        if (node == target) return;
        TreeNodeCollection collection; var index = -1;
        if (node.Tag is RibbonPlacement { Kind: "Separator" })
        {
            var panel = PanelOf(target); if (panel is null || target?.Parent?.Tag is StackNode) throw new IOException("구분선은 묶음 밖의 패널에 놓으세요.");
            collection = panel.Nodes; if (target?.Parent == panel) index = target.Index;
        }
        else if (node.Tag is RibbonPlacement)
        {
            var container = target?.Tag is RibbonPlacement ? target.Parent : target?.Tag is StackNode || target?.Tag as string == "Panel" ? target : null;
            if (container is null) throw new IOException("버튼을 패널 또는 2단·3단 묶음에 놓으세요."); CheckCapacity(container, node); collection = container.Nodes; if (target?.Tag is RibbonPlacement) index = target.Index;
        }
        else if (node.Tag is StackNode)
        {
            var panel = PanelOf(target); if (panel is null) throw new IOException("버튼 묶음을 패널에 놓으세요."); collection = panel.Nodes;
            if (target?.Parent == panel) index = target.Index;
        }
        else if (node.Tag as string == "Panel") { var tab = target?.Tag as string == "Tab" ? target : target?.Tag as string == "Panel" ? target.Parent : null; if (tab is null) throw new IOException("패널을 탭 또는 다른 패널 위로 옮기세요."); collection = tab.Nodes; if (target?.Tag as string == "Panel") index = target.Index; }
        else { if (target is not null && target.Parent is not null) throw new IOException("탭을 다른 탭 위로 옮기세요."); collection = tree.Nodes; index = target?.Index ?? tree.Nodes.Count; }
        if (index >= 0 && after) index++;
        if (node.Parent?.Nodes == collection || node.Parent is null && collection == tree.Nodes) if (index >= 0 && node.Index < index) index--;
        node.Remove(); if (index < 0) collection.Add(node); else collection.Insert(index, node); tree.SelectedNode = node; node.Parent?.Expand(); Changed();
    }
    private void MoveSelected(int delta) { if (tree.SelectedNode is not { } node) return; var nodes = node.Parent?.Nodes ?? tree.Nodes; var index = node.Index + delta; if (index < 0 || index >= nodes.Count) return; node.Remove(); nodes.Insert(index, node); tree.SelectedNode = node; Changed(); }
    private void UpdateProperties()
    {
        updating = true; var node = tree.SelectedNode; caption.Text = node?.Tag is RibbonPlacement b ? b.Title : node?.Text ?? ""; var separator = node?.Tag is RibbonPlacement { Kind: "Separator" }; caption.Enabled = node is not null && node.Tag is not StackNode && !separator;
        arrangement.Enabled = !separator && (node?.Tag is RibbonPlacement or StackNode); var rows = (node?.Tag as StackNode ?? node?.Parent?.Tag as StackNode)?.Rows ?? 0; arrangement.SelectedIndex = rows == 0 ? 0 : rows - 1;
        moveTabBox.Items.Clear();
        foreach (TreeNode t in tree.Nodes) moveTabBox.Items.Add(t.Text);
        var currentTab = SelectedTab();
        if (currentTab is not null && currentTab.Index < moveTabBox.Items.Count) moveTabBox.SelectedIndex = currentTab.Index;
        var canMoveTab = node is not null && node.Tag as string != "Tab" && tree.Nodes.Count > 1;
        moveTabBox.Enabled = canMoveTab; moveTabBtn.Enabled = canMoveTab;
        updating = false;
    }
    private void MoveSelectedToChosenTab()
    {
        if (tree.SelectedNode is not { } node || node.Tag as string == "Tab") return;
        if (moveTabBox.SelectedIndex < 0 || moveTabBox.SelectedIndex >= tree.Nodes.Count) return;
        var targetTab = tree.Nodes[moveTabBox.SelectedIndex];
        if (targetTab == SelectedTab()) return;
        DropNode(node, node.Tag as string == "Panel" ? targetTab : EnsurePanel(targetTab));
    }
    private void RefreshBank()
    {
        var placed = Descendants(tree.Nodes).Where(n => n.Tag is RibbonPlacement { Kind: not "Separator" }).Select(n => ((RibbonPlacement)n.Tag!).Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var query = bankSearch.Text.Trim();
        bank.Items.Clear();
        foreach (var tool in tools.Where(t => !placed.Contains(t.Id) && (query.Length == 0 || t.Title.Contains(query, StringComparison.OrdinalIgnoreCase))))
            bank.Items.Add(new BankTool(tool));
    }
    private void SetArrangement(int rows)
    {
        var node = tree.SelectedNode; if (node is null || node.Tag is not (RibbonPlacement or StackNode) || node.Tag is RibbonPlacement { Kind: "Separator" }) return;
        if (node.Tag is StackNode group)
        {
            if (rows != 0) { if (node.Nodes.Count > rows) throw new IOException("먼저 묶음의 버튼 수를 줄이세요."); node.Tag = group with { Rows = rows }; }
            else { var panel = node.Parent!; var index = node.Index; foreach (var child in node.Nodes.Cast<TreeNode>().ToArray()) { child.Remove(); panel.Nodes.Insert(index++, child); } node.Remove(); tree.SelectedNode = panel; }
        }
        else if (rows == 0) { if (node.Parent?.Tag is StackNode) { var stack = node.Parent; var panel = stack.Parent!; node.Remove(); panel.Nodes.Insert(stack.Index + 1, node); } }
        else if (node.Parent?.Tag is StackNode) { var stack = node.Parent; var old = (StackNode)stack.Tag!; if (stack.Nodes.Count > rows) throw new IOException("먼저 묶음의 버튼 수를 줄이세요."); stack.Tag = old with { Rows = rows }; }
        else { var panel = node.Parent!; var index = node.Index; var stack = AddStack(rows, panel); stack.Remove(); panel.Nodes.Insert(index, stack); node.Remove(); stack.Nodes.Add(node); tree.SelectedNode = node; }
        Changed();
    }
    internal RibbonLayout Snapshot()
    {
        RibbonPlacement[] Buttons(TreeNode panel)
        {
            var result = new List<RibbonPlacement>();
            foreach (TreeNode node in panel.Nodes)
            {
                if (node.Tag is RibbonPlacement b) result.Add(b with { Size = "Large", Stack = null, StackRows = 0 });
                else if (node.Tag is StackNode group)
                {
                    if (node.Nodes.Count != group.Rows) throw new IOException($"{group.Rows}단 묶음에 버튼을 {group.Rows}개 넣으세요. 현재 {node.Nodes.Count}개입니다.");
                    result.AddRange(node.Nodes.Cast<TreeNode>().Select(n => ((RibbonPlacement)n.Tag!) with { Size = "Small", Stack = group.Id, StackRows = group.Rows }));
                }
            }
            return result.ToArray();
        }
        var layout = new RibbonLayout(1, host, tree.Nodes.Cast<TreeNode>().Select(t => new RibbonTabLayout(t.Text.Trim(), t.Nodes.Cast<TreeNode>().Select(p => new RibbonPanelLayout(p.Text.Trim(), Buttons(p))).ToArray())).ToArray()); RibbonLayouts.Validate(layout); return layout;
    }
    public void LoadTools(IEnumerable<RibbonTool> values)
    {
        tools = values.ToList(); bank.Items.Clear(); foreach (var tool in tools) bank.Items.Add(new BankTool(tool));
        var saved = example || !File.Exists(store.FilePath(host)) ? RibbonLayouts.Defaults(host, tools) : store.Read(host);
        LoadLayout(RibbonLayouts.IncludeMissingManagement(saved, tools)); status.Text = tools.Count(x => x.Installed && !x.Id.StartsWith("manager:")) + "개 설치됨 · 관리 버튼도 이동·이름·묶음을 편집할 수 있습니다.";
    }
    internal void LoadLayout(RibbonLayout layout)
    {
        RibbonLayouts.Validate(layout); tree.Nodes.Clear();
        foreach (var tab in layout.Tabs)
        {
            var t = new TreeNode(tab.Name) { Tag = "Tab" }; tree.Nodes.Add(t);
            foreach (var panel in tab.Panels)
            {
                var p = new TreeNode(panel.Name) { Tag = "Panel" }; t.Nodes.Add(p); var stacks = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase);
                foreach (var button in panel.Buttons)
                {
                    var parent = p;
                    if (button.Stack is { } id) { if (!stacks.TryGetValue(id, out parent)) { parent = new TreeNode { Tag = new StackNode(id, button.StackRows) }; stacks[id] = parent; p.Nodes.Add(parent); } }
                    parent.Nodes.Add(new TreeNode(button.Kind == "Separator" ? "│ 구분선" : button.Title) { Tag = button });
                }
            }
        }
        tree.ExpandAll(); Changed();
    }
    private Control PreviewButton(TreeNode node, bool large, int width, int height)
    {
        var placement = (RibbonPlacement)node.Tag!;
        var tool = tools.FirstOrDefault(x => x.Id == placement.Id); var installed = tool?.Installed == true;
        var iconPx = large ? (compactPreview ? 22 : 30) : (compactPreview ? 14 : 16);
        var box = new Panel { Width = width, Height = height, Margin = Padding.Empty, BackColor = installed ? Color.FromArgb(244, 247, 251) : Color.FromArgb(247, 247, 247) };
        var icon = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Size = new(iconPx, iconPx), Location = large ? new((width - iconPx) / 2, compactPreview ? 4 : 5) : new(4, Math.Max(2, (height - iconPx) / 2)) };
        try { if (tool?.Icon is { } path && File.Exists(path)) { SafeFiles.NoLinks(path); if (new FileInfo(path).Length <= 2 * 1024 * 1024) { using var stream = File.OpenRead(path); using var image = Image.FromStream(stream); icon.Image = new Bitmap(image); } } } catch (Exception) { }
        if(icon.Image is null)icon.Image=ManagementButtonIcons.Create(placement.Id,large?32:16);
        if (icon.Image is not null) { icon.Disposed += (_, _) => icon.Image?.Dispose(); box.Controls.Add(icon); }
        else { icon.Dispose(); box.Controls.Add(new Label { Text = "▣", ForeColor = installed ? Color.FromArgb(55, 100, 160) : Color.Gray, Font = new Font(Font.FontFamily, large ? (compactPreview ? 16 : 22) : 11), TextAlign = ContentAlignment.MiddleCenter, Bounds = large ? new((width - 30) / 2, 0, 30, iconPx + 6) : new(2, 0, 20, height) }); }
        var textTop = iconPx + (compactPreview ? 6 : 10);
        var label = new Label { Text = placement.Title, ForeColor = installed ? Color.FromArgb(35, 45, 60) : Color.Gray, TextAlign = large ? ContentAlignment.TopCenter : ContentAlignment.MiddleLeft, AutoEllipsis = true, Font = new Font(Font.FontFamily, compactPreview ? 7.5f : 8.5f), Bounds = large ? new(2, textTop, width - 4, Math.Max(14, height - textTop)) : new(iconPx + 7, 0, Math.Max(20, width - iconPx - 9), height) };
        box.Controls.Add(label); tips.SetToolTip(label, placement.Title + (installed ? "" : " · 미설치") + " · 클릭: 이름 수정 / 드래그: 이동");
        WirePreview(box, node); foreach (Control child in box.Controls) WirePreview(child, node, child == label); return box;
    }
    private void Changed()
    {
        if (suspendPreview) return;
        RefreshBank();
        zoomCompactBtn.BackColor = compactPreview ? Color.FromArgb(30, 91, 166) : SystemColors.Control;
        zoomCompactBtn.ForeColor = compactPreview ? Color.White : ForeColor;
        zoomNormalBtn.BackColor = !compactPreview ? Color.FromArgb(30, 91, 166) : SystemColors.Control;
        zoomNormalBtn.ForeColor = !compactPreview ? Color.White : ForeColor;
        foreach (var child in tabsHost.Controls.Cast<Control>().ToArray()) child.Dispose();
        foreach (var child in stripHost.Controls.Cast<Control>().ToArray()) child.Dispose();
        var selectedTab = SelectedTab() ?? tree.Nodes.Cast<TreeNode>().FirstOrDefault();
        foreach (TreeNode tab in tree.Nodes)
        {
            var header = new Label { Text = tab.Text, AutoSize = true, Padding = new(10, 4, 10, 4), Margin = new(0, 0, 2, 0), BackColor = tab == selectedTab ? Color.White : Color.FromArgb(232, 235, 239), ForeColor = tab == selectedTab ? Color.FromArgb(30, 91, 166) : ForeColor, Font = new Font(Font, tab == selectedTab ? FontStyle.Bold : FontStyle.Regular) };
            WirePreview(header, tab, true); tabsHost.Controls.Add(header);
        }
        var addTab = new Button { Text = "＋", Width = 28, Height = 26, Margin = Padding.Empty, AccessibleName = "미리보기에서 탭 추가" };
        addTab.Click += (_, _) => AddTabFromPreview(); tabsHost.Controls.Add(addTab);
        var largeW = compactPreview ? 74 : 104;
        var largeH = compactPreview ? 58 : 78;
        var stackW = compactPreview ? 112 : 148;
        var smallW = compactPreview ? 108 : 143;
        var panelH = compactPreview ? 88 : 112;
        var sepW = compactPreview ? 10 : 13;
        foreach (TreeNode tab in tree.Nodes)
        {
            if (tab != selectedTab) continue;
            var strip = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty, BackColor = Color.FromArgb(241, 243, 246) };
            foreach (TreeNode panel in tab.Nodes)
            {
                var group = new TableLayoutPanel { Width = Math.Max(compactPreview ? 82 : 110, panel.Nodes.Cast<TreeNode>().Sum(n => n.Tag is StackNode ? stackW : n.Tag is RibbonPlacement { Kind: "Separator" } ? sepW : largeW) + 12), Height = panelH, RowCount = 2, ColumnCount = 1, Margin = new(0, 0, 4, 0), Padding = new(4), BackColor = Color.White, CellBorderStyle = TableLayoutPanelCellBorderStyle.Single };
                group.ColumnStyles.Add(new(SizeType.Percent, 100)); group.RowStyles.Add(new(SizeType.Percent, 100)); group.RowStyles.Add(new(SizeType.Absolute, 19));
                var columns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty };
                WirePreview(group, panel); WirePreview(columns, panel);
                foreach (TreeNode node in panel.Nodes)
                {
                    if (node.Tag is RibbonPlacement { Kind: "Separator" })
                    {
                        var separator = new Panel { Width = sepW, Height = largeH, Margin = Padding.Empty };
                        separator.Controls.Add(new Panel { BackColor = Color.FromArgb(190, 195, 202), Bounds = new(sepW / 2, 4, 1, largeH - 8) }); columns.Controls.Add(separator);
                        WirePreview(separator, node); WirePreview(separator.Controls[0], node);
                    }
                    else if (node.Tag is RibbonPlacement placement) columns.Controls.Add(PreviewButton(node, placement.Size != "Small", placement.Size == "Small" ? smallW : largeW, placement.Size == "Small" ? (compactPreview ? 22 : 26) : largeH));
                    else if (node.Tag is StackNode stack)
                    {
                        node.Text = $"{stack.Rows}단 묶음 ({node.Nodes.Count}/{stack.Rows})";
                        var column = new FlowLayoutPanel { Width = stackW, Height = largeH, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty, Padding = new(4, 0, 0, 0), BackColor = Color.FromArgb(210, 220, 234) };
                        WirePreview(column, node); tips.SetToolTip(column, "왼쪽 여백을 드래그하면 묶음 전체를 옮깁니다.");
                        foreach (TreeNode child in node.Nodes) column.Controls.Add(PreviewButton(child, false, smallW, largeH / stack.Rows));
                        for (var i = node.Nodes.Count; i < stack.Rows; i++) { var slot = new Label { Text = "놓기", Width = smallW, Height = largeH / stack.Rows, ForeColor = Color.Silver, TextAlign = ContentAlignment.MiddleCenter, BorderStyle = BorderStyle.FixedSingle, Margin = Padding.Empty, Font = new Font(Font.FontFamily, compactPreview ? 7.5f : 8.5f) }; WirePreview(slot, node); column.Controls.Add(slot); }
                        columns.Controls.Add(column);
                    }
                }
                var panelName = new Label { Text = panel.Text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(90, 95, 105), Font = new Font(Font.FontFamily, 8), Margin = Padding.Empty }; WirePreview(panelName, panel, true);
                group.Controls.Add(columns, 0, 0); group.Controls.Add(panelName, 0, 1); strip.Controls.Add(group);
            }
            var addPanel = new Button { Text = "＋ 패널", Width = compactPreview ? 64 : 80, Height = panelH - 6, Margin = new(3) }; addPanel.Click += (_, _) => { var panel = new TreeNode(Unique("새 패널", tab.Nodes)) { Tag = "Panel" }; tab.Nodes.Add(panel); tree.SelectedNode = panel; Changed(); }; strip.Controls.Add(addPanel);
            stripHost.Controls.Add(strip);
        }
        UpdateProperties();
        status.Text = "미리보기에서 드래그: 배치 이동 · 상단 고정 탭으로 드롭: 다른 탭 이동 · 저장 후 적용";
    }
}
