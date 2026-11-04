using RoBIM.Manager.Core;

namespace RoBIM.Manager;

internal sealed partial class RibbonEditorControl
{
    private sealed class RenameClickFilter(Action finish, TextBox input) : IMessageFilter
    {
        public bool PreFilterMessage(ref Message message)
        {
            if (message.Msg is 0x0201 or 0x0204 or 0x0207 or 0x00A1 or 0x00A4 &&
                !input.IsDisposed && !input.RectangleToScreen(input.ClientRectangle).Contains(Cursor.Position)) finish();
            return false;
        }
    }
    private bool suspendPreview;
    internal TextBox? PreviewNameInput => Controls.OfType<TextBox>().FirstOrDefault();
    internal Control? PreviewControl(TreeNode node, bool label = false)
    {
        IEnumerable<Control> Children(Control parent) { foreach (Control child in parent.Controls) { yield return child; foreach (var nested in Children(child)) yield return nested; } }
        return Children(preview).FirstOrDefault(c => ReferenceEquals(c.Tag, node) && (!label || c.AccessibleName == "리본 이름"));
    }
    internal void AddTabFromPreview()
    {
        var tab = AddTab("새 탭"); tree.SelectedNode = tab; Changed();
    }
    private void SelectWithoutRepaint(TreeNode node)
    {
        suspendPreview = true;
        try { tree.SelectedNode = node; UpdateProperties(); }
        finally { suspendPreview = false; }
    }
    internal void RenamePreviewNode(TreeNode node, string value)
    {
        value = value.Trim();
        if (node.Tag is StackNode or RibbonPlacement { Kind: "Separator" }) throw new IOException("묶음과 구분선의 이름은 변경하지 않습니다.");
        var limit = node.Tag is RibbonPlacement ? 120 : 80;
        if (string.IsNullOrWhiteSpace(value) || value.Length > limit || value.Any(char.IsControl)) throw new IOException($"이름은 1~{limit}자로 입력하세요.");
        if (node.Tag is not RibbonPlacement && (node.Parent?.Nodes ?? tree.Nodes).Cast<TreeNode>().Any(n => n != node && n.Text.Equals(value, StringComparison.OrdinalIgnoreCase))) throw new IOException("같은 위치에 같은 이름이 있습니다.");
        node.Text = value; if (node.Tag is RibbonPlacement button) node.Tag = button with { Title = value };
        SelectWithoutRepaint(node); Changed();
    }
    private void BeginPreviewRename(Control label, TreeNode node)
    {
        if (label.IsDisposed) return;
        var position = PointToClient(label.PointToScreen(Point.Empty));
        var width = Math.Min(Math.Max(label.Width, 130), ClientSize.Width);
        var bounds = new Rectangle(Math.Clamp(position.X, 0, Math.Max(0, ClientSize.Width - width)), Math.Clamp(position.Y - 2, 0, Math.Max(0, ClientSize.Height - 28)), width, 28);
        var input = new TextBox { Bounds = bounds, Text = node.Tag is RibbonPlacement button ? button.Title : node.Text, BorderStyle = BorderStyle.FixedSingle, Font = label.Font, AutoSize = false };
        var done = false;
        void Finish(bool save)
        {
            if (done) return; done = true;
            var text = input.Text; input.Dispose();
            if (save) Guard(() => RenamePreviewNode(node, text));
        }
        input.KeyDown += (_, e) => { if (e.KeyCode is Keys.Enter or Keys.Escape) { e.SuppressKeyPress = true; Finish(e.KeyCode == Keys.Enter); } };
        input.LostFocus += (_, _) => Finish(true);
        // Labels and blank preview areas do not take keyboard focus, so LostFocus alone misses these clicks.
        var clickFilter = new RenameClickFilter(() => Finish(true), input);
        Application.AddMessageFilter(clickFilter);
        input.Disposed += (_, _) => Application.RemoveMessageFilter(clickFilter);
        Controls.Add(input); input.BringToFront(); input.Focus(); input.SelectAll();
    }
    internal void DropOnPreview(object source, TreeNode target, bool after = false)
    {
        if (source is TreeNode { Tag: StackNode or RibbonPlacement { Kind: "Separator" } } && target.Parent?.Tag is StackNode) target = target.Parent;
        if (source is TreeNode { Tag: "Panel" } && target.Tag as string is not ("Tab" or "Panel")) target = PanelOf(target) ?? target;
        if (target.Tag as string == "Tab" && source is not TreeNode { Tag: "Tab" or "Panel" }) target = EnsurePanel(target);
        if (source is TreeNode node) DropNode(node, target, after);
        else if (source is string id)
        {
            AddTool(id, target);
            var added = tree.SelectedNode!;
            if (target.Tag is RibbonPlacement && added != target) DropNode(added, target, after);
        }
        else throw new IOException("버튼 또는 배치 항목을 놓으세요.");
    }
    private void WirePreview(Control control, TreeNode node, bool rename = false)
    {
        control.Tag = node; control.AllowDrop = true; control.Cursor = Cursors.Hand;
        if (rename) control.AccessibleName = "리본 이름";
        Point start = default; var pressed = false; var dragged = false;
        control.MouseDown += (_, e) => { pressed = e.Button == MouseButtons.Left; start = e.Location; dragged = false; };
        control.MouseMove += (_, e) =>
        {
            if (!pressed || e.Button != MouseButtons.Left || Math.Abs(e.X - start.X) < SystemInformation.DragSize.Width / 2 && Math.Abs(e.Y - start.Y) < SystemInformation.DragSize.Height / 2) return;
            pressed = false; dragged = true; SelectWithoutRepaint(node); StartPreviewDrag(node);
        };
        control.MouseUp += (_, e) =>
        {
            pressed = false; if (e.Button != MouseButtons.Left || dragged) return;
            if (node.Tag as string == "Tab" && node != SelectedTab()) { tree.SelectedNode = node; return; }
            SelectWithoutRepaint(node);
            if (rename) BeginPreviewRename(control, node);
        };
        control.DragEnter += (_, e) => PreviewDragOver(control, node, e);
        control.DragOver += (_, e) => PreviewDragOver(control, node, e);
        control.DragLeave += (_, _) => ClearPreviewDropHint();
        control.DragDrop += (_, e) => Guard(() =>
        {
            var after = PreviewDropAfter(node, new(e.X, e.Y)); ClearPreviewDropHint();
            if (e.Data?.GetData(typeof(TreeNode)) is TreeNode moved) DropOnPreview(moved, node, after);
            else if (e.Data?.GetData(typeof(string)) is string id) DropOnPreview(id, node, after);
        });
    }
}
