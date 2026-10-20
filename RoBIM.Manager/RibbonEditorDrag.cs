using RoBIM.Manager.Core;

namespace RoBIM.Manager;

internal sealed partial class RibbonEditorControl
{
    private readonly System.Windows.Forms.Timer dragTimer = new() { Interval = 30 };
    private TreeNode? hoveredPreviewTab;
    private long hoverStarted;
    private Panel? insertionHint;
    private DragImageWindow? dragImage;
    private bool dragToolsReady;

    private sealed class DragImageWindow : Form
    {
        public DragImageWindow(Bitmap bitmap)
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            TopMost = true; Opacity = 0.78; ClientSize = bitmap.Size; BackgroundImage = bitmap;
        }
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get { var value = base.CreateParams; value.ExStyle |= 0x08000000 | 0x00000020 | 0x00000080; return value; }
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0084) { message.Result = new IntPtr(-1); return; } // HTTRANSPARENT: never intercept the drop.
            base.WndProc(ref message);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { var image = BackgroundImage; BackgroundImage = null; image?.Dispose(); }
            base.Dispose(disposing);
        }
    }
    private void EnsureDragTools()
    {
        if (dragToolsReady) return; dragToolsReady = true;
        GiveFeedback += (_, e) => { e.UseDefaultCursors = true; MoveDragImage(Cursor.Position); };
        dragTimer.Tick += (_, _) =>
        {
            MoveDragImage(Cursor.Position);
            if (hoveredPreviewTab is not null && Environment.TickCount64 - hoverStarted >= 450) SwitchHoveredPreviewTab();
            if (dragImage is null && hoveredPreviewTab is null) dragTimer.Stop();
        };
        Disposed += (_, _) => { dragTimer.Dispose(); dragImage?.Dispose(); insertionHint?.Dispose(); };
    }
    internal Bitmap PreviewDragBitmap(object source)
    {
        if (source is TreeNode node && PreviewControl(node) is { } visual && visual.Width is > 0 and <= 480 && visual.Height is > 0 and <= 160)
        {
            var bitmap = new Bitmap(visual.Width, visual.Height); visual.DrawToBitmap(bitmap, new(Point.Empty, visual.Size)); return bitmap;
        }
        var title = source is TreeNode treeNode ? treeNode.Text : tools.FirstOrDefault(t => t.Id == source as string)?.Title ?? "버튼";
        var fallback = new Bitmap(168, 42);
        using var graphics = Graphics.FromImage(fallback); graphics.Clear(Color.FromArgb(234, 241, 252));
        graphics.DrawRectangle(Pens.SteelBlue, 0, 0, 167, 41); TextRenderer.DrawText(graphics, title, Font, new Rectangle(8, 4, 152, 34), ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        return fallback;
    }
    internal Rectangle? DragImageBounds => dragImage?.Bounds;
    internal void CreateDragImage(object source, bool show = true)
    {
        dragImage?.Dispose(); dragImage = new DragImageWindow(PreviewDragBitmap(source)); MoveDragImage(Cursor.Position);
        if (show) dragImage.Show();
    }
    internal void MoveDragImage(Point screenPoint) { if (dragImage is not null) dragImage.Location = new(screenPoint.X + 14, screenPoint.Y + 18); }
    internal void EndPreviewDrag()
    {
        dragTimer.Stop(); hoveredPreviewTab = null; insertionHint?.Hide(); dragImage?.Dispose(); dragImage = null;
    }
    private void StartPreviewDrag(object source)
    {
        EnsureDragTools(); CreateDragImage(source); dragTimer.Start();
        try
        {
            // This UserControl survives rebuilding the preview when hovering another tab.
            DoDragDrop(source, source is string ? DragDropEffects.Copy : DragDropEffects.Move);
        }
        finally { EndPreviewDrag(); }
    }
    internal bool PreviewDropAfter(TreeNode node, Point screenPoint)
    {
        var visual = PreviewControl(node); if (visual is null) return false;
        var point = visual.PointToClient(screenPoint);
        return node.Parent?.Tag is StackNode ? point.Y >= visual.Height / 2 : point.X >= visual.Width / 2;
    }
    internal void SwitchHoveredPreviewTab()
    {
        var tab = hoveredPreviewTab; hoveredPreviewTab = null;
        if (tab is null || tab.TreeView != tree || tab == SelectedTab()) return;
        insertionHint?.Hide(); SelectWithoutRepaint(tab); Changed();
    }
    private void PreviewDragOver(Control control, TreeNode node, DragEventArgs e)
    {
        EnsureDragTools();
        e.Effect = e.Data?.GetDataPresent(typeof(TreeNode)) == true ? DragDropEffects.Move : e.Data?.GetDataPresent(typeof(string)) == true ? DragDropEffects.Copy : DragDropEffects.None;
        if (e.Effect == DragDropEffects.None) { ClearPreviewDropHint(); return; }
        if (node.Tag as string == "Tab" && node != SelectedTab())
        {
            if (hoveredPreviewTab != node) { hoveredPreviewTab = node; hoverStarted = Environment.TickCount64; }
        }
        else hoveredPreviewTab = null;
        dragTimer.Start(); MoveDragImage(new(e.X, e.Y));
        var visual = PreviewControl(node) ?? control;
        var rectangle = RectangleToClient(visual.RectangleToScreen(visual.ClientRectangle));
        var after = PreviewDropAfter(node, new(e.X, e.Y));
        insertionHint ??= new Panel { BackColor = Color.FromArgb(0, 110, 230), Enabled = false };
        if (insertionHint.Parent is null) Controls.Add(insertionHint);
        insertionHint.Bounds = node.Parent?.Tag is StackNode
            ? new(rectangle.Left, after ? rectangle.Bottom - 2 : rectangle.Top, rectangle.Width, 3)
            : new(after ? rectangle.Right - 2 : rectangle.Left, rectangle.Top, 3, rectangle.Height);
        insertionHint.Show(); insertionHint.BringToFront();
    }
    private void ClearPreviewDropHint()
    {
        insertionHint?.Hide(); hoveredPreviewTab = null;
        if (dragImage is null) dragTimer.Stop();
    }
}
