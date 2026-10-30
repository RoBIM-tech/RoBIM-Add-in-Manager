using System.Drawing;
using System.Text.Json;
using RoBIM.Manager.Core;

namespace RoBIM.Manager;

public sealed partial class ManagerForm
{
    internal void InspectInstalledCards(string destination, string? snapshot = null, string page = "All", bool refreshOffers = false)
    {
        ShowInTaskbar = false; Opacity = 0; StartPosition = FormStartPosition.Manual; Location = new Point(-20000, -20000);
        Shown += async (_, _) =>
        {
            try
            {
                var protectedFiles = new[] { "installed.json", "ribbon-layout-revit.json", "NativeRevit/active.json", "NativeRevit/pending.json" };
                string[] SelectionProof() => protectedFiles.Select(f => Path.Combine(paths.Data,f)).Select(f => File.Exists(f) ? SafeFiles.Hash(f) : "ABSENT").ToArray();
                var before = SelectionProof();
                await StartLocalDisplay();
                if (refreshOffers) await RefreshWithUpdates();
                SwitchPage(page); RefreshCardActions();
                if (snapshot is not null)
                {
                    using var bitmap = new Bitmap(Width, Height); DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
                    bitmap.Save(snapshot, System.Drawing.Imaging.ImageFormat.Png);
                }
                File.WriteAllText(destination, JsonSerializer.Serialize(new
                {
                    Products = productRows.Select(x => new { x.Status.Tool.Id, x.Status.Path, x.Status.Managed, x.Status.External, x.Status.Modified, x.Status.Detail }),
                    Cards = cardBindings.Values.Select(x => new { x.Row.Id, x.Row.Title, x.Row.Selected, x.Row.Pending, x.Row.ModuleId, x.Row.Channel, x.Row.ReleaseLabel, x.Row.SelectedChannel, x.Row.SelectedVersion, x.Row.CollectionChannel, UpdateChannel = OfferFor(x.Row)?.Channel, Source = x.Row.Source,
                        Enabled = x.Action.Enabled, Action = x.Action.Text, State = x.State.Text, Instruction = x.Instruction.Text }),
                    Log = log.Text,
                    UpdateOffers = updateOffers, TestOffers = testOffers, StableOffers = stableOffers, TestErrors = testOfferErrors, StableErrors = stableOfferErrors,
                    TestCards = ProductCommands("Test").Where(c => c.CollectionChannel == "test").Select(c => new { c.Id, c.TestView, c.ReleaseLabel, c.SelectedChannel, c.SelectedVersion, Action = PrimaryText(c,AppearanceFor(c)) }),
                    SelectionUnchanged = before.SequenceEqual(SelectionProof())
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
        Require(currentPage == "Revit" && cardBindings.Count == 5 && cardBindings.Values.All(x => x.Row.Kind == ButtonKind.Native), "Revit includes stable and TEST independent Add-ins");
        Require(!navigation.Any(x => (string?)x.Tag is "PyRevit" or "Developer" or "Locations"), "Retired pyRevit pages are absent");
        Require(!nativeReload.Visible, "Product page has no duplicate Reload");
        Require(!navigation.Any(x => (string?)x.Tag is "All" or "Mine" or "Test"), "Aggregate pages removed");
        Require(UpdateTargets().All(p => p.Package.Host != "pyrevit"), "Retired pyRevit is excluded from update queries");
        SwitchPage("PyRevit"); Require(currentPage == "Revit", "Historical pyRevit page routes safely to Revit");
        void Navigate(string id) { ((Button)navigation.Single(x => (string?)x.Tag == id)).PerformClick(); Application.DoEvents(); }
        foreach(var oldPage in new[]{"All","Mine","Test"}) { SwitchPage(oldPage); Require(currentPage=="Revit","Old page maps to program "+oldPage); }
        Navigate("Cad"); Require(cardBindings.Count == 3 && cardBindings.Values.All(x => x.Row.Kind == ButtonKind.Cad), "AutoCAD includes stable and TEST commands");
        Require(!nativeReload.Visible, "Native Revit Reload hidden on AutoCAD page");
        Navigate("CadRibbon");
        Require(cadRibbonPage.Visible && !ribbonPage.Visible && pageTitle.Text=="AutoCAD 리본 편집", "AutoCAD ribbon editor is separate from Revit");
        Navigate("NavisworksRibbon");
        Require(navisworksRibbonPage.Visible && !ribbonPage.Visible && !cadRibbonPage.Visible && pageTitle.Text=="Navisworks 리본 편집", "Navisworks ribbon editor has its own empty state");
        Navigate("Loaders");
        Require(loadersPage.Visible && !ribbonPage.Visible && !cadRibbonPage.Visible && pageTitle.Text == "프로그램 로더 관리", "Loader page is separate");
        Require(navigation.Any(x => (string?)x.Tag == "Loaders"), "Loader navigation visible");
        var loaderCards = loadersPage.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<FlowLayoutPanel>().ToArray();
        Require(loaderCards.Length == 2, "CAD and Revit loader cards visible");
        Require(loaderCards.All(c => c.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<Button>().Count(b => b.Text == "저장소 확인") == 1), "One metadata check per loader");
        Require(!loaderCards.SelectMany(c => c.Controls.OfType<FlowLayoutPanel>()).SelectMany(c => c.Controls.OfType<Button>()).Any(b => b.Text is "TEST 확인" or "TEST 로더 적용"), "Loader channels share the primary action");
        Require(loaderCards.SelectMany(card => card.Controls.OfType<LinkLabel>()).Select(link => link.Text).Order().SequenceEqual(new[] { "RoBIM-tech/RoBIM-CAD-Loader", "RoBIM-tech/RoBIM-Revit-Addin" }.Order()), "Loader repository links identify actual repositories");
        var savedCadStatus=statuses.GetValueOrDefault(Catalog.Cad.Id);
        var savedCadOffer=loaderOffers.GetValueOrDefault(Catalog.Cad.Id);
        var cadActions=loaderCards[0].Controls.OfType<FlowLayoutPanel>().Single();
        var cadChannel=cadActions.Controls.OfType<ComboBox>().Single();
        var cadPrimary=cadActions.Controls.OfType<Button>().Single(b=>b.BackColor!=Color.White);
        statuses[Catalog.Cad.Id]=new(Catalog.Cad,paths.Target(Catalog.Cad),"1.0.2-test.2",true,false,false,"");
        loaderOffers[Catalog.Cad.Id]=new(new("1.0.3","stable"),new("1.1.0-test.3","test"));
        cadChannel.SelectedIndex=1; RefreshLoaderStatus();
        Require(cadPrimary.Enabled && cadPrimary.Text=="업데이트", "Screenshot TEST version pair enables loader update");
        statuses[Catalog.Cad.Id]=statuses[Catalog.Cad.Id] with{Modified=true}; RefreshLoaderStatus();
        Require(!cadPrimary.Enabled && cadPrimary.Text=="적용 차단" && cadLoaderStatus.Text.Contains("변경"), "Modified loader explains disabled action");
        using (var bitmap=new Bitmap(Width,Height)) { DrawToBitmap(bitmap,new Rectangle(Point.Empty,Size));bitmap.Save(destination+".loader-protected.png"); }
        statuses[Catalog.Cad.Id]=statuses[Catalog.Cad.Id] with{Version="1.0.3",Modified=false};cadChannel.SelectedIndex=0;RefreshLoaderStatus();
        Require(!cadPrimary.Enabled && cadLoaderStatus.Text.Contains("TEST를 선택"), "Latest stable loader points to optional TEST choice");
        if(savedCadStatus is null) statuses.Remove(Catalog.Cad.Id);else statuses[Catalog.Cad.Id]=savedCadStatus;
        if(savedCadOffer is null) loaderOffers.Remove(Catalog.Cad.Id);else loaderOffers[Catalog.Cad.Id]=savedCadOffer;
        RefreshLoaderStatus();
        Navigate("Revit");
        Require(pageTitle.Text=="Revit" && navigation.Single(x=>(string?)x.Tag=="Revit").Text=="Revit", "Revit menu and page use simple display name");
        Require(navigation.Single(x=>(string?)x.Tag=="Ribbon").AccessibleName=="Revit 리본 편집" && navigation.Single(x=>(string?)x.Tag=="CadRibbon").AccessibleName=="AutoCAD 리본 편집", "Ribbon navigation identifies the owning program");
        Require(navigation.First().Parent is ScrollableControl nav && !nav.HorizontalScroll.Visible, "Program ribbon navigation fits without horizontal scrolling");
        Navigate("Revit"); Require(cardBindings.Values.All(x => x.Row.Kind == ButtonKind.Native), "Independent Revit excludes pyRevit and CAD actions");
        Require(!nativeReload.Visible, "Reload belongs to ribbon editor");
        productSearch.Text = "OCR-no-match"; Require(cardBindings.Count == 0, "Empty search state");
        productSearch.Clear(); Navigate("Revit"); productSearch.Text = "파라미터"; Require(cardBindings.Count == 1 && cardBindings.Values.Single().Row.Title == "파라미터 복사", "Search button title and description");
        productSearch.Clear(); Navigate("Revit");
        productSearch.Text = "도구"; Require(cardBindings.Count == 5 && cardBindings.Values.All(x => x.Row.Kind == ButtonKind.Native), "Native versus pyRevit search");
        productSearch.Clear(); Navigate("Revit"); productSort.SelectedIndex = 2;
        Require(cardBindings.Values.Take(2).All(x => x.Row.Selected), "Selected-first sorting");
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
        Navigate("Cad"); Navigate("Revit"); Navigate("Revit"); productSearch.Text = "자동 결합"; productSearch.Clear();
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
            Require(cards.All(x => x.Height <= 97 * DeviceDpi / 96), "Compact card height at width " + formWidth);
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
                    if (child is Button && child.Visible) Require(child.Right <= container.ClientSize.Width && child.Bottom <= container.ClientSize.Height, "Action fits card " + child.Text);
                    CheckBounds(child);
                }
            }
            CheckBounds(card);
        }
        Navigate("Ribbon"); Require(ribbonPage.Visible && !productsPage.Visible, "Ribbon editor navigation");
        var ribbonConnection = ribbonPage.Controls.OfType<FlowLayoutPanel>().SelectMany(x => x.Controls.OfType<Button>()).Single(x => x.AccessibleName == "Revit 리본 연결 설치 및 업데이트");
        Require(ribbonConnection.Visible && ribbonConnection.Parent!.Bottom <= ribbonEditor!.Top, "Native ribbon connection recovery is visible without covering the editor");
        var managementCount = ribbonEditor!.Snapshot().Tabs.SelectMany(t => t.Panels).SelectMany(p => p.Buttons).Count(b => b.Id.StartsWith("manager:"));
        Require(managementCount > 0, "Installed tools expose editable management controls");
        Require(ribbonEditor.Snapshot().Tabs.SelectMany(t => t.Panels).SelectMany(p => p.Buttons).Count() == 2 + managementCount, "Ribbon editor defaults contain installed tools and management only");
        ribbonEditor.AddTool("native:sheets:create-sheets");
        Require(ribbonEditor.Snapshot().Tabs.SelectMany(t => t.Panels).SelectMany(p => p.Buttons).Count() == 3 + managementCount, "Uninstalled tool can be placed without installation");
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
        SwitchPage("Developer"); Require(currentPage == "Revit" && !developerPage.Visible, "Historical developer page cannot reopen retired tools");
        Navigate("Extensions"); Require(extensionsPage.Visible, "Registration screen preserved");
        SwitchPage("Locations"); Require(currentPage == "Revit" && !locationsPage.Visible, "Historical pyRevit location page stays retired");
        foreach (var repository in BundledCatalog.Read().Repositories.Where(r => r.Packages.SingleOrDefault()?.Host == "revit"))
        {
            var package = repository.Packages.Single(); var tool = ExtensionCatalog.Tool(repository, package);
            var row = new ProductRow(new(tool, "unused", "", false, false, false, "설치 안 됨"), new(repository, package));
            var direct = RegisteredNativeCard(row);
            Require(direct.Kind == ButtonKind.Native && direct.Source is null && direct.CommandId.Length > 0 && direct.ModuleId.Length > 0, "Registered button available before package download " + direct.Id);
            Require(!direct.Title.StartsWith("RoBIM", StringComparison.OrdinalIgnoreCase), "Display name omits product prefix " + direct.Id);
            using var menu = BuildButtonMenu(direct);
            Require(menu.Items.Cast<ToolStripItem>().Any(x => x.Text == "저장소 TEST 버전 설치…") &&
                menu.Items.Cast<ToolStripItem>().Any(x => x.Text == "저장소 정식 버전 설치…"), "Native release channels are directly selectable before download " + direct.Id);
            Require(!menu.Items.Cast<ToolStripItem>().Any(x => (x.Text ?? "").Contains("기능 목록") || x.Text == "공용 패키지 관리"), "No native discovery or separate package menu " + direct.Id);
            using var card = CreateCommandCard(direct); snapshotVerified = true; refreshingSnapshot = false; busy = false;
            RefreshCardActions();
            Require(cardBindings[direct.Id].Action.Text == "설치하기", "Uncached native card installs directly " + direct.Id);
        }
        var sheetsRepository = BundledCatalog.Read().Repositories.Single(r => r.Repository.EndsWith("/RoBIM-Revit-Sheets"));
        var sheetsPackage = sheetsRepository.Packages.Single();
        var sheetsTool = ExtensionCatalog.Tool(sheetsRepository, sheetsPackage);
        var promoted = RegisteredNativeCard(new(new(sheetsTool, "unused", "", false, false, false, ""), new(sheetsRepository, sheetsPackage)))
            with { Selected = true, SelectedChannel = "test", SelectedVersion = "0.1.0-test.2" };
        Require(promoted.Channel == "stable" && promoted.CollectionChannel == "test" && promoted.SelectedChannel == "test",
            "Installed TEST controls collection despite stable catalog default");
        updateOffers[promoted.Owner.Status.Tool.Id] = new("0.2.0", "stable");
        Require(NativeReleaseState(promoted).Update?.Channel == "stable", "Newer stable promotion selects update");
        using var statusCard = CreateCommandCard(promoted with { SelectedVersion = "0.1.0-test.2" });
        var statusBinding = cardBindings[promoted.Id];
        appearanceSnapshot[promoted.Id] = new(true, true, false, "업데이트 가능 · 0.1.0-test.2 → 0.2.0", "업데이트 후 Reload");
        RefreshCardActions(); var updateColor = statusCard.BackColor;
        Require(statusBinding.Action.Text == "정식 업데이트" && statusBinding.State.Text.Contains("0.1.0-test.2 → 0.2.0"), "Update action and installed/latest versions are visible");
        updateOffers[promoted.Owner.Status.Tool.Id] = new("0.0.1", "stable");
        appearanceSnapshot[promoted.Id] = new(true, false, false, "설치됨", ""); RefreshCardActions();
        Require(statusCard.BackColor != updateColor && statusBinding.State.Text.StartsWith("설치 ·"), "Installed card uses separate color and text");
        using var missingCard = CreateCommandCard(promoted with { Id = "missing", Selected = false }); RefreshCardActions();
        Require(missingCard.BackColor != statusCard.BackColor && missingCard.BackColor != updateColor && cardBindings["missing"].State.Text.StartsWith("미설치 ·"), "Uninstalled card uses distinct neutral color and text");
        updateOffers[promoted.Owner.Status.Tool.Id] = new("0.3.0", "stable");
        Require(RemoteUpdate(promoted with { SelectedVersion = "0.2.0", SelectedChannel = "stable" }), "Refresh offer marks newer same-channel version");
        Require(!RemoteUpdate(promoted with { SelectedVersion = "0.3.0", SelectedChannel = "stable" }) && !RemoteUpdate(promoted with { Selected = false, SelectedVersion = "0.2.0" }), "Applied update and uninstalled tool do not remain outdated");
        updateErrors[promoted.Owner.Status.Tool.Id] = "offline";
        var failedAppearance = BuildAppearances([promoted with { SelectedVersion = "0.2.0" }])[promoted.Id];
        Require(failedAppearance.Update && failedAppearance.Message.Contains("최신 확인 실패"), "Refresh failure retains last verified update offer");
        var stableCard = promoted with { SelectedChannel="stable",SelectedVersion="0.2.0" };
        var stableId=stableCard.Owner.Status.Tool.Id;
        updateErrors.Clear(); updateOffers[stableId]=new("v0.2.0","stable"); testOffers[stableId]=new("v0.3.0-test.1","test");
        var projected=ProductCommands("Test",[stableCard]).Single();
        Require(projected.TestView && projected.Id==stableCard.Id && projected.CommandId==stableCard.CommandId && stableCard.Channel=="stable", "TEST view preserves canonical button identity and default channel");
        Require(!SelectedInView(projected) && PrimaryText(projected,AppearanceFor(projected))=="TEST 설치", "Installed stable offers explicit TEST install instead of removal");
        Require(AppearanceFor(stableCard).Message.Contains("새 TEST 제공") && !RemoteUpdate(stableCard), "New TEST is an optional notice rather than a stable update");
        using var testCard=CreateCommandCard(projected); RefreshCardActions();
        Require(cardBindings[projected.Id].Action.Text=="TEST 설치" && cardBindings[projected.Id].State.Text.Contains("정식 0.2.0"),"TEST card explains currently installed stable version");
        var selectedTest=projected with { SelectedChannel="test",SelectedVersion="0.2.5-test.1" };
        Require(PrimaryText(selectedTest,AppearanceFor(selectedTest))=="TEST 업데이트","Installed TEST can update within TEST channel");
        var currentTest = projected with { SelectedChannel="test", SelectedVersion="v0.3.0-test.1", CachedOffer=new("0.2.0","stable") };
        var mine = ProductCommands("Mine",[currentTest]).Single();
        Require(mine.ReleaseLabel.StartsWith("TEST · v0.3.0-test.1") && mine.CollectionChannel=="test", "My tools labels the actual installed TEST version");
        appearanceSnapshot[mine.Id]=new(true,true,false,"새 기능 패키지 · 업데이트 가능","");
        Require(!AppearanceFor(mine).Update && PrimaryText(mine,AppearanceFor(mine))=="제거" && !AppearanceFor(mine).Message.Contains("새 기능 패키지"), "Old stable cache and stale UI flags do not mark current TEST outdated");
        testOffers[stableId]=new("v0.3.0-test.2","test");
        Require(AppearanceFor(mine).Update && PrimaryText(mine,AppearanceFor(mine))=="TEST 업데이트", "My tools offers later TEST updates");
        stableOffers[stableId]=new("v0.3.0","stable");
        Require(PrimaryText(mine,AppearanceFor(mine))=="정식 업데이트" && OfferFor(mine)?.Channel=="stable", "Installed TEST offers stable promotion from My tools");
        var testCatalog = mine with { Channel="test" };
        Require(OfferFor(testCatalog)?.Channel=="stable", "Stable promotion is found even while repository default remains TEST");
        var promotedStable=testCatalog with { SelectedChannel="stable",SelectedVersion="0.3.0",ViewChannel="" };
        Require(promotedStable.CollectionChannel=="stable" && !AppearanceFor(promotedStable).Update && ProductCommands("Mine",[promotedStable]).Single().ReleaseLabel.StartsWith("정식"), "Applied promotion becomes stable without repeating update");
        stableOffers.Clear(); testOffers[stableId]=new("v0.3.0-test.1","test");
        testOffers[stableId]=new("0.2.0-test.1","test");
        Require(!ProductCommands("Test",[stableCard]).Any() && ProductCommands("Test",[selectedTest]).Any(),"Covered TEST is hidden while installed TEST remains manageable");
        testOffers[stableId]=new("v0.3.0-test.1","test"); testOfferErrors[stableId]="offline";
        Require(AppearanceFor(stableCard).Message.Contains("TEST 확인 실패") && NewTestAvailable(stableCard),"Failed TEST check retains prior offer and current installation");
        var cacheRoundtrip=JsonSerializer.Deserialize<DisplaySnapshot>(JsonSerializer.Serialize(new DisplaySnapshot(6,[],[],[],[],DateTimeOffset.UtcNow,updateOffers,testOffers,testOfferErrors)))!;
        Require(cacheRoundtrip.Tests![stableId].Version=="v0.3.0-test.1" && cacheRoundtrip.TestErrors![stableId]=="offline","TEST offers and check failures survive display-cache roundtrip");
        stableOffers[stableId]=new("0.3.0","stable"); stableOfferErrors[stableId]="offline";
        var fullCache=JsonSerializer.Deserialize<DisplaySnapshot>(JsonSerializer.Serialize(new DisplaySnapshot(7,[],[],[],[],DateTimeOffset.UtcNow,updateOffers,testOffers,testOfferErrors,stableOffers,stableOfferErrors)))!;
        Require(fullCache.Stables![stableId].Version=="0.3.0" && fullCache.StableErrors![stableId]=="offline", "Stable promotion offers and errors survive display cache");
        stableOffers.Clear(); stableOfferErrors.Clear();
        // Exercise navigation from the actual link against the preview fixture's stable card.
        var sample=PreviewCommands().First(c=>c.Kind==ButtonKind.Native && c.Channel=="stable" && c.Selected);
        updateOffers[sample.Owner.Status.Tool.Id]=new("0.2.0","stable"); testOffers[sample.Owner.Status.Tool.Id]=new("0.3.0-test.1","test");
        productSearch.Clear(); Navigate("Revit");
        var optionalBinding=cardBindings[sample.Id];
        Require(optionalBinding.TestInstall.Visible && optionalBinding.TestInstall.Enabled,"Prominent TEST install is visible alongside stable update");
        Require(optionalBinding.Action.Text=="업데이트" && optionalBinding.TestInstall.Text=="TEST 설치","Stable and TEST actions are both available");
        updateOffers[sample.Owner.Status.Tool.Id]=new(sample.SelectedVersion,"stable");RenderProductCards();
        optionalBinding=cardBindings[sample.Id];
        Require(optionalBinding.Card.BackColor==Color.FromArgb(255,251,222),"Sixth color identifies installed stable with newer optional TEST");
        Require(optionalBinding.TestInstall.Visible && optionalBinding.State.Text.Contains("새 TEST 제공"),"Optional TEST version has a visible install button and label");
        busy=true;RefreshCardActions();Require(!optionalBinding.TestInstall.Enabled,"Busy prevents TEST mutation");busy=false;RefreshCardActions();
        using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,new Rectangle(Point.Empty,Size));bitmap.Save(destination+".optional-test.png");}
        using (var owners = new RepositoryPublishersDialog(new(1,true,[new("alice","user"),new("team","organization")])))
        {
            owners.ShowInTaskbar=false;owners.StartPosition=FormStartPosition.Manual;owners.Location=new(-20000,-20000);owners.Show();Application.DoEvents();
            IEnumerable<Control> Descendants(Control root) => root.Controls.Cast<Control>().SelectMany(c => new[]{c}.Concat(Descendants(c)));
            var controls=Descendants(owners).ToArray();var table=controls.OfType<DataGridView>().Single();
            var input=controls.OfType<TextBox>().Single();input.Text="another";
            controls.OfType<Button>().Single(b=>b.Text=="추가").PerformClick();
            Require(table.Rows.Count==3,"Publisher dialog adds a third owner");
            table.ClearSelection();table.Rows[0].Selected=true;
            controls.OfType<Button>().Single(b=>b.Text=="선택 삭제").PerformClick();
            Require(table.Rows.Count==2,"Publisher dialog removes only selected owner");
            table.Rows[0].Cells["Enabled"].Value=false;
            controls.OfType<CheckBox>().Single().Checked=false;
            using(var bitmap=new Bitmap(owners.Width,owners.Height)){owners.DrawToBitmap(bitmap,new Rectangle(Point.Empty,owners.Size));bitmap.Save(destination+".publishers.png");}
            controls.OfType<Button>().Single(b=>b.Text=="저장").PerformClick();
            Require(owners.Result is { IncludeConnectedAccount:false, Publishers.Length:2 } && !owners.Result.Publishers[0].Enabled,"Publisher dialog persists enable toggle and account scope");
        }
        SwitchPage("Revit"); productSearch.Clear(); productFilter.SelectedIndex=1;
        Require(cardBindings.Values.All(c=>c.Row.Selected||c.Row.Pending),"Installed filter replaces My tools");
        productFilter.SelectedIndex=3;
        Require(cardBindings.Values.All(c=>!c.Row.Selected&&!c.Row.Pending),"Uninstalled filter");
        productFilter.SelectedIndex=0; channelFilter.SelectedIndex=2;
        Require(cardBindings.Values.All(c=>c.Row.CollectionChannel=="test"||NewTestAvailable(c.Row)),"Program TEST filter");
        channelFilter.SelectedIndex=0;
        using(var editor=new RibbonEditorControl(new RibbonLayouts(paths),true)) {
            editor.LoadTools([new("bank:a","A","Tab","Panel",true),new("bank:b","B","Tab","Panel",true)]);
            editor.LoadLayout(new(1,"Revit",[new("Tab",[new("Panel",[new("bank:a","A")])])]));
            Require(editor.AvailableIds.SequenceEqual(new[]{"bank:b"}),"Placed button excluded from bank");
            editor.LoadLayout(new(1,"Revit",[]));
            Require(editor.AvailableIds.Count()==2,"Deleting a tab restores its buttons to bank");
        }
        File.WriteAllText(destination, JsonSerializer.Serialize(new { Passed = checks.Count, Checks = checks }, new JsonSerializerOptions { WriteIndented = true })); Close();
    }
}
