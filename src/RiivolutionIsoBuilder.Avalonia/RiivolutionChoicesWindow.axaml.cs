using Avalonia.Controls;
using Avalonia.Interactivity;
using RiivolutionIsoBuilder.Riivolution;

namespace RiivolutionIsoBuilder.Avalonia;

public partial class RiivolutionChoicesWindow : Window
{
    private readonly List<ComboBox> combos = [];

    public RiivolutionChoicesWindow()
    {
        InitializeComponent();
    }

    public RiivolutionChoicesWindow(RiivolutionDocument document)
    {
        InitializeComponent();

        Title = $"Riivolution Options - {document.DisplayName}";
        foreach (var option in document.Sections.SelectMany(section => section.Options))
        {
            var label = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(option.Name) ? option.Id : option.Name,
                FontWeight = global::Avalonia.Media.FontWeight.SemiBold
            };
            var combo = new ComboBox
            {
                MinHeight = 34,
                ItemsSource = new[] { "Disabled" }.Concat(option.Choices.Select(choice => choice.Name)).ToList()
            };
            var defaultIndex = option.DefaultChoice;
            combo.SelectedIndex = defaultIndex >= 0 && defaultIndex < option.Choices.Count + 1 ? defaultIndex : 0;

            var panel = new StackPanel { Spacing = 6 };
            panel.Children.Add(label);
            panel.Children.Add(combo);
            OptionsPanel.Children.Add(panel);
            combos.Add(combo);
        }
    }

    public IReadOnlyList<int?>? SelectedChoices { get; private set; }

    private void OkButton_OnClick(object? sender, RoutedEventArgs e)
    {
        SelectedChoices = combos.Select(combo => (int?)(combo.SelectedIndex - 1)).ToList();
        Close(true);
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
