using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using RoBIM.Cad.Configuration;
using RoBIM.Manager.Core;
using WpfBinding = System.Windows.Data.Binding;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfDataGrid = System.Windows.Controls.DataGrid;
using WpfMessageBox = System.Windows.MessageBox;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace RoBIM.Manager;

internal sealed class CadButtonsWindow : Window
{
    public sealed class CadButtonRow
    {
        public string Key { get; init; } = "";
        public bool Use { get; set; }
        public string Module { get; init; } = "";
        public string Title { get; set; } = "";
        public string Panel { get; set; } = "";
        public int Order { get; set; }
        public string Description { get; init; } = "";
    }

    private sealed record SessionItem(CadSession? Session, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly CadCatalog original;
    private readonly ObservableCollection<CadButtonRow> rows = [];
    private readonly WpfComboBox sessions = new() { Width = 620, Height = 30, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly WpfDataGrid grid = new()
    {
        AutoGenerateColumns = false,
        CanUserAddRows = false,
        CanUserDeleteRows = false,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        HorizontalGridLinesBrush = new SolidColorBrush(WpfColor.FromRgb(226, 232, 240)),
        Background = new SolidColorBrush(WpfColor.FromRgb(255, 255, 255)),
        BorderBrush = new SolidColorBrush(WpfColor.FromRgb(203, 213, 225)),
        BorderThickness = new Thickness(1),
        RowHeight = 34
    };

    public CadSession? Session => (sessions.SelectedItem as SessionItem)?.Session;
    public IReadOnlyList<string> Selection => rows.Where(r => r.Use).Select(r => r.Key).ToArray();
    public CadCatalog Configuration => original with
    {
        Modules = original.Modules.Select(m => m with
        {
            Actions = m.Actions.Select(a =>
            {
                var row = rows.Single(r => r.Key == CadFiles.Key(m, a));
                return a with { Title = row.Title.Trim(), Panel = row.Panel.Trim(), Order = row.Order };
            }).ToList()
        }).ToList()
    };

    public CadButtonsWindow(CadCatalog catalog, IEnumerable<string> selected, IReadOnlyList<CadSession> liveSessions)
    {
        original = catalog;
        var chosen = selected.ToHashSet(StringComparer.Ordinal);
        Title = "RoBIM Add-in Manager — CAD 버튼 선택";
        Width = 980;
        Height = 620;
        MinWidth = 820;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(WpfColor.FromRgb(241, 245, 249));
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI, Malgun Gothic");
        FontSize = 13;

        var root = new DockPanel { LastChildFill = true, Margin = new Thickness(20) };

        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(top, Dock.Top);
        top.Children.Add(new TextBlock
        {
            Text = "필요한 버튼만 선택하세요. 버튼 이름·패널·순서를 수정할 수 있습니다.\n적용 준비 후 AutoCAD → RoBIM → 관리 → Reload를 누르면 반영됩니다.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(WpfColor.FromRgb(51, 65, 85)),
            Margin = new Thickness(0, 0, 0, 10)
        });

        var sessionRow = new StackPanel { Orientation = WpfOrientation.Horizontal };
        sessionRow.Children.Add(new TextBlock
        {
            Text = "적용할 CAD",
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 10, 0)
        });
        if (liveSessions.Count == 0)
        {
            sessions.Items.Add(new SessionItem(null, "CAD 종료됨 — 다음 실행 후 Reload로 적용"));
        }
        else
        {
            sessions.Items.Add(new SessionItem(null, "실행 중인 AutoCAD 전체 — 각 창에서 Reload"));
            foreach (var session in liveSessions)
                sessions.Items.Add(new SessionItem(session, $"PID {session.Pid} · {session.Title}"));
        }
        sessions.SelectedIndex = 0;
        sessionRow.Children.Add(sessions);
        top.Children.Add(sessionRow);
        root.Children.Add(top);

        var bottom = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(bottom, Dock.Bottom);
        bottom.Children.Add(new TextBlock
        {
            Text = "같은 기능의 여러 버튼은 하나의 DLL로 Reload됩니다. 체크를 해제하면 리본과 직접 명령 실행에서도 해제됩니다.\n준비 단계에서는 설치 파일을 교체하지 않습니다. 기존 구성은 CAD에서 적용할 때까지 유지됩니다.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = new SolidColorBrush(WpfColor.FromRgb(100, 116, 139)),
            Margin = new Thickness(0, 0, 0, 12)
        });

        var buttons = new StackPanel { Orientation = WpfOrientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        var cancel = new WpfButton { Content = "취소", IsCancel = true, Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 8, 0) };
        var apply = new WpfButton
        {
            Content = "선택 적용 준비",
            Padding = new Thickness(16, 7, 16, 7),
            Background = new SolidColorBrush(WpfColor.FromRgb(30, 91, 166)),
            Foreground = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(0)
        };
        apply.Click += (_, _) =>
        {
            try
            {
                grid.CommitEdit(DataGridEditingUnit.Row, true);
                var config = Configuration;
                if (config.Modules.SelectMany(m => m.Actions).Any(a => string.IsNullOrWhiteSpace(a.Title) || string.IsNullOrWhiteSpace(a.Panel)))
                    throw new InvalidOperationException("버튼 이름과 패널을 입력하십시오.");
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(this, "버튼 설정을 확인하십시오. " + ex.Message, Title);
            }
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(apply);
        bottom.Children.Add(buttons);
        root.Children.Add(bottom);

        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "등록", Width = 60, Binding = new WpfBinding(nameof(CadButtonRow.Use)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged } });
        grid.Columns.Add(new DataGridTextColumn { Header = "기능", Width = new DataGridLength(1.2, DataGridLengthUnitType.Star), IsReadOnly = true, Binding = new WpfBinding(nameof(CadButtonRow.Module)) });
        grid.Columns.Add(new DataGridTextColumn { Header = "버튼 이름", Width = new DataGridLength(2.0, DataGridLengthUnitType.Star), Binding = new WpfBinding(nameof(CadButtonRow.Title)) });
        grid.Columns.Add(new DataGridTextColumn { Header = "패널", Width = new DataGridLength(1.0, DataGridLengthUnitType.Star), Binding = new WpfBinding(nameof(CadButtonRow.Panel)) });
        grid.Columns.Add(new DataGridTextColumn { Header = "순서", Width = 70, Binding = new WpfBinding(nameof(CadButtonRow.Order)) });

        foreach (var module in catalog.Modules)
        foreach (var action in module.Actions)
        {
            var key = CadFiles.Key(module, action);
            rows.Add(new CadButtonRow
            {
                Key = key,
                Use = chosen.Contains(key),
                Module = module.Name,
                Title = action.Title,
                Panel = action.Panel,
                Order = action.Order,
                Description = action.Description
            });
        }
        grid.ItemsSource = rows;
        root.Children.Add(grid);
        Content = root;
    }
}
