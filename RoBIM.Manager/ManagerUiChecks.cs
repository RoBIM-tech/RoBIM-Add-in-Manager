using System.Drawing;
using System.Text.Json;
using RoBIM.Manager.Core;

namespace RoBIM.Manager;

public sealed partial class ManagerForm
{
    internal void InspectInstalledCards(string destination, string? snapshot = null, string page = "All")
    {
        ShowInTaskbar = false; Opacity = 0; StartPosition = FormStartPosition.Manual; Location = new Point(-20000, -20000);
        Shown += async (_, _) =>
        {
            try
            {
                await StartLocalDisplay(); SwitchPage(page); RefreshCardActions();
                if (snapshot is not null)
                {
                    using var bitmap = new Bitmap(Width, Height); DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
                    bitmap.Save(snapshot, System.Drawing.Imaging.ImageFormat.Png);
                }
                File.WriteAllText(destination, JsonSerializer.Serialize(new
                {
                    Products = productRows.Select(x => new { x.Status.Tool.Id, x.Status.Path, x.Status.Managed, x.Status.External, x.Status.Modified, x.Status.Detail }),
                    Cards = cardBindings.Values.Select(x => new { x.Row.Id, x.Row.Title, x.Row.Selected, x.Row.Pending, x.Row.ModuleId, x.Row.Channel, x.Row.ReleaseLabel, Source = x.Row.Source,
                        Enabled = x.Action.Enabled, Action = x.Action.Text, State = x.State.Text, Instruction = x.Instruction.Text }),
                    Log = log.Text
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception error) { File.WriteAllText(destination, JsonSerializer.Serialize(new { Error = error.ToString() })); }
            finally { Close(); }
        };
    }
    internal void VerifyCardUi(string destination)
    {
        if (!preview) throw new InvalidOperationException("UI checks require preview mode.");
        var checks = new List<string>();
        void Require(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks.Add(name); }
        ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; Location = new Point(-20000, -20000); Show(); Application.DoEvents();
        Require(currentPage == "Revit" && cardBindings.Count == 1 && cardBindings.Values.All(x => x.Row.Kind == ButtonKind.Native), "Default Revit shows independent Add-ins only");
        Require(navigation.Any(x => (string?)x.Tag == "PyRevit"), "Separate pyRevit navigation");
        Require(nativeReload.Text == "독립 Add-in Reload", "Independent Add-in Reload label");
        Navigate("PyRevit"); Require(cardBindings.Count == 3 && cardBindings.Values.All(x => x.Row.Kind == ButtonKind.PyRevit), "pyRevit page excludes independent Add-ins and CAD");
        Require(nativeReload.Visible && nativeReload.Text == "pyRevit Reload", "pyRevit Reload label");
        Require(cardBindings.Values.Count(x => x.Row.Kind == ButtonKind.PyRevit) == 3, "Multiple buttons share one pyRevit package");
        void Navigate(string id) { ((Button)navigation.Single(x => (string?)x.Tag == id)).PerformClick(); Application.DoEvents(); }
        Navigate("Test"); Require(cardBindings.Count == 1 && cardBindings.Values.All(x => x.Row.Channel == "test"), "TEST has separate collection");
        Require(cardBindings.Values.Single().Row.ReleaseLabel.StartsWith("TEST"), "TEST badge includes version");
        Navigate("All"); Require(cardBindings.Count == 6, "All lists buttons from both hosts");
        Require(productCards.ClientSize.Height > 200, "Full content height without profile note");
        Navigate("Mine"); Require(cardBindings.Count == 3 && cardBindings.Values.All(x => x.Row.Selected), "My tools is per-button selection");
        Navigate("Cad"); Require(cardBindings.Count == 2 && cardBindings.Values.All(x => x.Row.Kind == ButtonKind.Cad), "AutoCAD shows multiple actions from one loader");
        Require(!nativeReload.Visible, "Native Revit Reload hidden on AutoCAD page");
        Navigate("Revit"); Require(cardBindings.Values.All(x => x.Row.Kind == ButtonKind.Native), "Independent Revit excludes pyRevit and CAD actions");
        Require(nativeReload.Visible, "Native Revit Reload available on Revit page");
        productSearch.Text = "OCR-no-match"; Require(cardBindings.Count == 0, "Empty search state");
        productSearch.Clear(); Navigate("PyRevit"); productSearch.Text = "슬래브"; Require(cardBindings.Count == 1 && cardBindings.Values.Single().Row.Title == "보 슬래브 높이 맞춤", "Search button title and description");
        productSearch.Clear(); Navigate("Revit");
        productSearch.Text = "독립"; Require(cardBindings.Count == 1 && cardBindings.Values.Single().Row.Kind == ButtonKind.Native, "Native versus pyRevit search");
        productSearch.Clear(); Navigate("All"); productSort.SelectedIndex = 2;
        Require(cardBindings.Values.Take(3).All(x => x.Row.Selected), "Selected-first sorting");
        productSort.SelectedIndex = 1; var names = cardBindings.Values.Select(x => x.Row.Title).ToArray();
        Require(names.SequenceEqual(names.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)), "Button name sorting");
        foreach (var binding in cardBindings.Values)
        {
            var card = binding.Row;
            Require(binding.Action.Text == (card.Selected ? "제거" : "설치하기"), "Per-button action for " + card.Id);
            using var menu = BuildButtonMenu(card);
            Require(menu.Items.Cast<ToolStripItem>().Any(x => x.Text == (card.Selected ? "이 버튼 제거" : "이 버튼 설치")), "Individual button menu for " + card.Id);
            Require(menu.Items.Cast<ToolStripItem>().Any(x => x.Text == "공용 패키지 관리") == (card.Kind != ButtonKind.Native), "Single native button has no separate package action " + card.Id);
            SelectProduct(card.Owner.Status.Tool.Id); Require(SelectedProduct()?.Status.Tool.Id == card.Owner.Status.Tool.Id, "Package dispatch for " + card.Id);
        }
        busy = true; RefreshCardActions(); Require(cardBindings.Values.All(x => !x.Action.Enabled), "Busy disables all feature mutations"); busy = false; RefreshCardActions();
        refreshingSnapshot = true; RefreshCardActions(); Require(cardBindings.Values.All(x => !x.Action.Enabled && !x.More.Enabled), "Snapshot verification blocks stale card actions and menus"); refreshingSnapshot = false; RefreshCardActions();
        var scans = inventoryScanCount;
        Navigate("PyRevit"); Navigate("Revit"); Navigate("All"); productSearch.Text = "AutoJoin"; productSearch.Clear();
        Require(inventoryScanCount == scans, "Navigation and search never rescan installed packages");
        Require(CardColumnCount(380, 96) == 1 && CardColumnCount(900, 96) == 2 && CardColumnCount(1400, 96) == 3, "Responsive one/two/three column breakpoints");
        Require(CardColumnCount(2100, 144) == 3 && CardColumnCount(900, 144) == 1, "DPI-aware column breakpoints");
        foreach (var formWidth in new[] { 1180, 1320, 1760, 1320 })
        {
            Size = new Size(formWidth, 850); PerformLayout(); Application.DoEvents(); SizeProductCards(); Application.DoEvents();
            var cards = productCards.Controls.OfType<ToolCardPanel>().ToArray();
            var available = Math.Max(100, productCards.ClientSize.Width - productCards.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 2);
            var expected = Math.Min(cards.Length, CardColumnCount(available, DeviceDpi));
            Require(cards.Count(x => x.Top == cards[0].Top) == expected, "Responsive first row at width " + formWidth);
            Require(cards.All(x => x.Height <= 89 * DeviceDpi / 96), "Compact card height at width " + formWidth);
            Require(!productCards.HorizontalScroll.Visible, "No horizontal scrollbar at width " + formWidth);
            foreach (var binding in cardBindings.Values)
            {
                var action = binding.Action;
                var header = action.Parent!;
                var more = header.Controls.OfType<Button>().Single(x => x != action);
                Require(action.Top <= 5 * DeviceDpi / 96 && more.Top == action.Top && more.Left >= action.Right,
                    "Actions aligned at upper right " + formWidth + ":" + binding.Row.Id);
            }
            for (var i = 0; i < cards.Length; i++)
                for (var k = i + 1; k < cards.Length; k++) Require(!cards[i].Bounds.IntersectsWith(cards[k].Bounds), "Cards do not overlap " + formWidth + ":" + i + "/" + k);
        }
        Size = MinimumSize; PerformLayout(); Application.DoEvents(); SizeProductCards();
        Require(productSearch.Width >= 180, "Search remains usable at minimum size");
        foreach (Control card in productCards.Controls)
        {
            Require(card.Right <= productCards.ClientSize.Width, "Card fits width " + card.AccessibleName);
            void CheckBounds(Control container)
            {
                foreach (Control child in container.Controls)
                {
                    if (child is Button) Require(child.Right <= container.ClientSize.Width && child.Bottom <= container.ClientSize.Height, "Action fits card " + child.Text);
                    CheckBounds(child);
                }
            }
            CheckBounds(card);
        }
        Navigate("Ribbon"); Require(ribbonPage.Visible && !productsPage.Visible, "Ribbon editor navigation");
        Require(ribbonEditor!.Snapshot().Tabs.SelectMany(t => t.Panels).SelectMany(p => p.Buttons).Count() == 2, "Ribbon editor defaults contain installed tools only");
        ribbonEditor.AddTool("native:sheets:create-sheets");
        Require(ribbonEditor.Snapshot().Tabs.SelectMany(t => t.Panels).SelectMany(p => p.Buttons).Count() == 3, "Uninstalled tool can be placed without installation");
        var editable = ribbonEditor.Snapshot();
        var moved = editable.Tabs.SelectMany(t => t.Panels).SelectMany(p => p.Buttons).First();
        var secondId = CommandInventory().First(x => x.Kind is ButtonKind.PyRevit or ButtonKind.Native && x.Id != moved.Id).Id;
        ribbonEditor.LoadLayout(new(1, "Revit", [new("사용자 탭", [new("사용자 패널", [moved with { Title = "사용자 버튼", Size = "Small", Stack = "two", StackRows = 2 }, new(secondId, "두 번째 버튼", "Small", "two", 2)])])]));
        Require(ribbonEditor.Snapshot().Tabs[0].Panels[0].Buttons[0].Size == "Small", "Ribbon size and custom title round trip");
        Require(ribbonEditor.EditorNodes[0].Nodes[0].Nodes.Count == 1 && ribbonEditor.EditorNodes[0].Nodes[0].Nodes[0].Nodes.Count == 2, "Two small buttons share one ribbon column");
        var thirdId = CommandInventory().First(x => x.Kind is ButtonKind.PyRevit or ButtonKind.Native && x.Id != moved.Id && x.Id != secondId).Id;
        var rejected = false;
        try { ribbonEditor.AddTool(thirdId, ribbonEditor.EditorNodes[0].Nodes[0].Nodes[0]); } catch (IOException) { rejected = true; }
        Require(rejected && ribbonEditor.Snapshot().Tabs[0].Panels[0].Buttons.Length == 2, "Full two-row column rejects a third button without losing layout");
        ribbonEditor.LoadLayout(new(1, "Revit", [new("세 줄 탭", [new("세 줄 패널", [new(moved.Id, "첫째", "Small", "three", 3), new(secondId, "둘째", "Small", "three", 3), new(thirdId, "셋째", "Small", "three", 3)])])]));
        Require(ribbonEditor.Snapshot().Tabs[0].Panels[0].Buttons.All(b => b.StackRows == 3), "Three small buttons share one ribbon column");
        ribbonEditor.LoadLayout(new(1, "Revit", [new("첫 탭", [new("첫 패널", [moved])]), new("둘째 탭", [new("둘째 패널", [])])]));
        ribbonEditor.DropNode(ribbonEditor.EditorNodes[0].Nodes[0].Nodes[0], ribbonEditor.EditorNodes[1].Nodes[0]);
        Require(ribbonEditor.Snapshot().Tabs[0].Panels[0].Buttons.Length == 0 && ribbonEditor.Snapshot().Tabs[1].Panels[0].Buttons[0].Id == moved.Id, "Drag button across tabs preserves identity");
        ribbonEditor.DropNode(ribbonEditor.EditorNodes[1], ribbonEditor.EditorNodes[0]);
        Require(ribbonEditor.Snapshot().Tabs[0].Name == "둘째 탭", "Drag tab reorders root tree");
        ribbonEditor.SelectPreviewTab(0); Application.DoEvents();
        Require(ribbonEditor.PreviewedTab == "둘째 탭", "Preview shows selected tab only");
        ribbonEditor.SelectPreviewTab(1); Application.DoEvents();
        Require(ribbonEditor.PreviewedTab == "첫 탭", "Preview follows another selected tab");
        Require(ribbonEditor.PreviewSpansEditorWidth, "Selected-tab preview spans full width above editor");
        var separator = ribbonEditor.AddSeparator(ribbonEditor.EditorNodes[1].Nodes[0]);
        Require(ribbonEditor.Snapshot().Tabs[1].Panels[0].Buttons.Single().Kind == "Separator", "Insert a separator in a panel");
        ribbonEditor.DropNode(separator, ribbonEditor.EditorNodes[0].Nodes[0].Nodes[0]);
        Require(ribbonEditor.Snapshot().Tabs[0].Panels[0].Buttons[0].Kind == "Separator", "Drag separator before another button");
        var previewButton = ribbonEditor.EditorNodes[0].Nodes[0].Nodes[1];
        ribbonEditor.SelectPreviewTab(0);
        var nameLabel = ribbonEditor.PreviewControl(previewButton, true)!;
        typeof(Control).GetMethod("OnMouseUp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(nameLabel, [new MouseEventArgs(MouseButtons.Left, 1, 5, 5, 0)]);
        var inlineName = ribbonEditor.PreviewNameInput!; inlineName.Text = "직접 편집한 버튼";
        typeof(Control).GetMethod("OnKeyDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(inlineName, [new KeyEventArgs(Keys.Enter)]);
        Require(ribbonEditor.Snapshot().Tabs[0].Panels[0].Buttons[1].Title == "직접 편집한 버튼" && ribbonEditor.Snapshot().Tabs[0].Panels[0].Buttons[1].Id == moved.Id, "Preview name click and Enter update title while preserving command identity");
        ribbonEditor.DropOnPreview(previewButton, ribbonEditor.EditorNodes[1].Nodes[0]);
        Require(ribbonEditor.Snapshot().Tabs[1].Panels[0].Buttons.Single().Id == moved.Id, "Preview drop moves button across panels");
        ribbonEditor.SelectPreviewTab(1);
        var panelLabel = ribbonEditor.PreviewControl(ribbonEditor.EditorNodes[1].Nodes[0], true)!;
        Require(panelLabel.AllowDrop, "Preview panel caption accepts drag and drop");
        var panelNameBounds = ribbonEditor.RectangleToClient(panelLabel.RectangleToScreen(panelLabel.ClientRectangle));
        typeof(Control).GetMethod("OnMouseUp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(panelLabel, [new MouseEventArgs(MouseButtons.Left, 1, 5, 5, 0)]);
        var panelInput = ribbonEditor.PreviewNameInput!;
        Require(panelInput.Parent == ribbonEditor && panelInput.Top < panelNameBounds.Bottom && panelInput.Bottom > panelNameBounds.Top && panelInput.Bottom <= ribbonEditor.ClientSize.Height, "Panel rename overlays the visible caption outside automatic panel layout");
        panelInput.Text = "취소할 이름";
        typeof(Control).GetMethod("OnKeyDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(panelInput, [new KeyEventArgs(Keys.Escape)]);
        Require(ribbonEditor.Snapshot().Tabs[1].Panels[0].Name == "첫 패널", "Escape cancels panel inline rename");
        var oldTabCount = ribbonEditor.EditorNodes.Count; ribbonEditor.AddTabFromPreview();
        Require(ribbonEditor.EditorNodes.Count == oldTabCount + 1 && ribbonEditor.PreviewedTab == "새 탭", "Preview plus creates and selects a real editable tab");
        ribbonEditor.RenamePreviewNode(ribbonEditor.EditorNodes[2], "새 관리 탭");
        Require(ribbonEditor.Snapshot().Tabs[2].Name == "새 관리 탭", "Rename tab directly from preview");
        ribbonEditor.LoadLayout(new(1, "Revit", [new("드래그 시작", [new("시작 패널", [new(moved.Id, "A"), new(secondId, "B"), new(thirdId, "C")])]), new("드래그 목적", [new("목적 패널", [])])]));
        ribbonEditor.SelectPreviewTab(0); Application.DoEvents();
        var firstNode = ribbonEditor.EditorNodes[0].Nodes[0].Nodes[0];
        var lastNode = ribbonEditor.EditorNodes[0].Nodes[0].Nodes[2];
        var lastVisual = ribbonEditor.PreviewControl(lastNode)!;
        var lastPoint = lastVisual.PointToScreen(new Point(lastVisual.Width - 1, lastVisual.Height / 2));
        var dragData = new DataObject(); dragData.SetData(typeof(TreeNode), firstNode);
        typeof(Control).GetMethod("OnDragDrop", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(lastVisual, [new DragEventArgs(dragData, 0, lastPoint.X, lastPoint.Y, DragDropEffects.Move, DragDropEffects.Move)]);
        Require(ribbonEditor.Snapshot().Tabs[0].Panels[0].Buttons.Select(b => b.Title).SequenceEqual(new[] { "B", "C", "A" }), "Right half drop places an earlier button after the last button");
        var firstVisual = ribbonEditor.PreviewControl(ribbonEditor.EditorNodes[0].Nodes[0].Nodes[0])!;
        var firstPoint = firstVisual.PointToScreen(new Point(0, firstVisual.Height / 2));
        typeof(Control).GetMethod("OnDragDrop", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(firstVisual, [new DragEventArgs(dragData, 0, firstPoint.X, firstPoint.Y, DragDropEffects.Move, DragDropEffects.Move)]);
        Require(ribbonEditor.Snapshot().Tabs[0].Panels[0].Buttons.Select(b => b.Title).SequenceEqual(new[] { "A", "B", "C" }), "Left half drop places a later button before the first button");
        var otherHeader = ribbonEditor.PreviewControl(ribbonEditor.EditorNodes[1], true)!;
        var hoverPoint = otherHeader.PointToScreen(new Point(5, 5));
        typeof(Control).GetMethod("OnDragEnter", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(otherHeader, [new DragEventArgs(dragData, 0, hoverPoint.X, hoverPoint.Y, DragDropEffects.Move, DragDropEffects.Move)]);
        var hoverClock = System.Diagnostics.Stopwatch.StartNew();
        while (ribbonEditor.PreviewedTab != "드래그 목적" && hoverClock.ElapsedMilliseconds < 1500) { Application.DoEvents(); Thread.Sleep(10); }
        Require(ribbonEditor.PreviewedTab == "드래그 목적" && firstNode.Parent == ribbonEditor.EditorNodes[0].Nodes[0], "Timed drag hover switches tab without moving or disposing the source node");
        var dropControl = ribbonEditor.PreviewControl(ribbonEditor.EditorNodes[1].Nodes[0])!;
        var dropPoint = dropControl.PointToScreen(new Point(12, 12));
        typeof(Control).GetMethod("OnDragDrop", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(dropControl, [new DragEventArgs(dragData, 0, dropPoint.X, dropPoint.Y, DragDropEffects.Move, DragDropEffects.Move)]);
        Require(ribbonEditor.Snapshot().Tabs[1].Panels[0].Buttons.Single().Id == moved.Id, "Drop continues into the newly hovered tab");
        ribbonEditor.CreateDragImage(firstNode, false); ribbonEditor.MoveDragImage(new Point(210, 310));
        Require(ribbonEditor.DragImageBounds is { X: 224, Y: 328, Width: > 0, Height: > 0 }, "Dragged button bitmap follows the pointer with an offset");
        ribbonEditor.EndPreviewDrag(); Require(ribbonEditor.DragImageBounds is null, "Drag end disposes the floating button image");
        ribbonEditor.LoadLayout(new(1, "Revit", [new("묶음", [new("패널", [new(moved.Id, "A", "Small", "g", 2), new(secondId, "B", "Small", "g", 2)])])]));
        var group = ribbonEditor.EditorNodes[0].Nodes[0].Nodes[0]; ribbonEditor.DropOnPreview(group.Nodes[0], group.Nodes[1], true);
        Require(ribbonEditor.Snapshot().Tabs[0].Panels[0].Buttons.Select(b => b.Title).SequenceEqual(new[] { "B", "A" }), "Stacked button can move below its sibling without exceeding capacity");
        ribbonEditor.LoadTools([new("test:uninstalled", "미설치", "RoBIM", "도구", false)]);
        Require(ribbonEditor.Snapshot().Tabs.Length == 0, "Zero installed tools produce no default tabs");
        Navigate("Account"); Require(accountPage.Visible && !productsPage.Visible && accountPage.Height > 350, "GitHub settings retains content height");
        Navigate("Developer"); Require(developerPage.Visible, "Developer screen preserved");
        Navigate("Extensions"); Require(extensionsPage.Visible, "Registration screen preserved");
        Navigate("Locations"); Require(locationsPage.Visible && !productsPage.Visible && pyRevitRootInput.Text.Contains("RoBIM"), "Dedicated pyRevit installation location page");
        Require(!applyLocation.Enabled, "Preview cannot migrate actual installations");
        foreach (var repository in BundledCatalog.Read().Repositories.Where(r => r.Packages.SingleOrDefault()?.Host == "revit"))
        {
            var package = repository.Packages.Single(); var tool = ExtensionCatalog.Tool(repository, package);
            var row = new ProductRow(new(tool, "unused", "", false, false, false, "설치 안 됨"), new(repository, package));
            var direct = RegisteredNativeCard(row);
            Require(direct.Kind == ButtonKind.Native && direct.Source is null && direct.CommandId.Length > 0 && direct.ModuleId.Length > 0, "Registered button available before package download " + direct.Id);
            Require(!direct.Title.StartsWith("RoBIM", StringComparison.OrdinalIgnoreCase), "Display name omits product prefix " + direct.Id);
            using var menu = BuildButtonMenu(direct);
            Require(!menu.Items.Cast<ToolStripItem>().Any(x => (x.Text ?? "").Contains("기능 목록") || x.Text == "공용 패키지 관리"), "No native discovery or separate package menu " + direct.Id);
            using var card = CreateCommandCard(direct); snapshotVerified = true; refreshingSnapshot = false; busy = false;
            RefreshCardActions();
            Require(cardBindings[direct.Id].Action.Text == "설치하기", "Uncached native card installs directly " + direct.Id);
        }
        File.WriteAllText(destination, JsonSerializer.Serialize(new { Passed = checks.Count, Checks = checks }, new JsonSerializerOptions { WriteIndented = true })); Close();
    }
}
