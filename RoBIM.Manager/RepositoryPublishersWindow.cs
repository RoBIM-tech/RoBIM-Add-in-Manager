using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using RoBIM.Manager.Core;
using WpfBinding = System.Windows.Data.Binding;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfColor = System.Windows.Media.Color;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfDataGrid = System.Windows.Controls.DataGrid;
using WpfMessageBox = System.Windows.MessageBox;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace RoBIM.Manager;

internal sealed class RepositoryPublishersWindow : Window
{
    public sealed class PublisherRow
    {
        public bool Enabled { get; set; }
        public string Name { get; init; } = "";
        public string Kind { get; set; } = "자동 확인";
    }

    private readonly ObservableCollection<PublisherRow> rows = [];
    private readonly WpfDataGrid table = new()
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
        RowHeight = 32,
        SelectionMode = DataGridSelectionMode.Extended
    };
    private readonly WpfCheckBox account = new() { Content = "연결 계정이 접근할 수 있는 RoBIM 저장소도 검색", Margin = new Thickness(0, 8, 0, 8) };
    private readonly WpfTextBox nameInput = new() { Width = 280, Height = 30, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
    private readonly WpfComboBox kindInput = new() { Width = 110, Height = 30, Margin = new Thickness(0, 0, 8, 0) };

    public RepositorySearchSettings? Result { get; private set; }

    public RepositoryPublishersWindow(RepositorySearchSettings current)
    {
        Title = "제작자·조직 검색 대상";
        Width = 760;
        Height = 480;
        MinWidth = 640;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(WpfColor.FromRgb(241, 245, 249));
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI, Malgun Gothic");
        FontSize = 13;

        var root = new DockPanel { LastChildFill = true, Margin = new Thickness(20) };

        var header = new TextBlock
        {
            Text = "여러 개인 제작자와 조직을 등록할 수 있습니다. 저장 후 도구 목록의 새로고침에서 검색합니다.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(WpfColor.FromRgb(51, 65, 85)),
            Margin = new Thickness(0, 0, 0, 12)
        };
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var bottom = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(bottom, Dock.Bottom);

        var addRow = new StackPanel { Orientation = WpfOrientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        kindInput.Items.Add("자동 확인");
        kindInput.Items.Add("개인");
        kindInput.Items.Add("조직");
        kindInput.SelectedIndex = 0;
        var addBtn = new WpfButton { Content = "추가", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 6, 0) };
        var removeBtn = new WpfButton { Content = "선택 삭제", Padding = new Thickness(12, 5, 12, 5) };
        addRow.Children.Add(nameInput);
        addRow.Children.Add(kindInput);
        addRow.Children.Add(addBtn);
        addRow.Children.Add(removeBtn);
        bottom.Children.Add(addRow);

        account.IsChecked = current.IncludeConnectedAccount;
        bottom.Children.Add(account);

        var actionRow = new StackPanel { Orientation = WpfOrientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var cancel = new WpfButton { Content = "취소", IsCancel = true, Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 8, 0) };
        var save = new WpfButton
        {
            Content = "저장",
            Padding = new Thickness(16, 7, 16, 7),
            Background = new SolidColorBrush(WpfColor.FromRgb(36, 122, 113)),
            Foreground = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(0)
        };
        actionRow.Children.Add(cancel);
        actionRow.Children.Add(save);
        bottom.Children.Add(actionRow);
        root.Children.Add(bottom);

        table.Columns.Add(new DataGridCheckBoxColumn { Header = "검색", Width = 60, Binding = new WpfBinding(nameof(PublisherRow.Enabled)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged } });
        table.Columns.Add(new DataGridTextColumn { Header = "제작자·조직", Width = new DataGridLength(1.8, DataGridLengthUnitType.Star), IsReadOnly = true, Binding = new WpfBinding(nameof(PublisherRow.Name)) });
        table.Columns.Add(new DataGridComboBoxColumn
        {
            Header = "종류",
            Width = 110,
            ItemsSource = new[] { "자동 확인", "개인", "조직" },
            SelectedItemBinding = new WpfBinding(nameof(PublisherRow.Kind))
        });

        foreach (var item in current.Publishers)
            rows.Add(new PublisherRow { Enabled = item.Enabled, Name = item.Name, Kind = KindLabel(item.Kind) });
        table.ItemsSource = rows;
        root.Children.Add(table);
        Content = root;

        addBtn.Click += (_, _) =>
        {
            try
            {
                var owner = RepositoryDiscovery.PublisherName(nameInput.Text);
                if (rows.Any(r => string.Equals(r.Name, owner, StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException("이미 등록된 검색 대상입니다.");
                rows.Add(new PublisherRow { Enabled = true, Name = owner, Kind = kindInput.SelectedItem as string ?? "자동 확인" });
                nameInput.Clear();
            }
            catch (ArgumentException error)
            {
                WpfMessageBox.Show(this, error.Message, Title);
            }
        };

        removeBtn.Click += (_, _) =>
        {
            foreach (var row in table.SelectedItems.Cast<PublisherRow>().ToArray())
                rows.Remove(row);
        };

        save.Click += (_, _) =>
        {
            table.CommitEdit(DataGridEditingUnit.Row, true);
            Result = new RepositorySearchSettings(
                1,
                account.IsChecked == true,
                rows.Select(r => new RepositoryPublisher(r.Name, KindValue(r.Kind), r.Enabled)).ToArray());
            DialogResult = true;
            Close();
        };
    }

    private static string KindLabel(string kind) => kind == "organization" ? "조직" : kind == "user" ? "개인" : "자동 확인";
    private static string KindValue(string? kind) => kind == "조직" ? "organization" : kind == "개인" ? "user" : "auto";
}
