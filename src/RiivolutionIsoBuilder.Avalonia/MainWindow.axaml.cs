using Avalonia.Controls;
using Avalonia.Interactivity;

namespace RiivolutionIsoBuilder.Avalonia;

public partial class MainWindow : Window
{
    private readonly PatcherPaths paths;
    private readonly PatcherEngine engine;

    public MainWindow()
    {
        InitializeComponent();

        paths = PatcherPaths.Discover();
        engine = new PatcherEngine(paths, AppendLog);
        StatusText.Text = $"Ready - {paths.RootDirectory}";
        ExtensionCombo.ItemsSource = BuilderDefaults.OutputExtensions;
        ExtensionCombo.SelectedIndex = 0;

        AppendLog($"Project: {paths.RootDirectory}");
        AppendLog($"Catalog: {paths.ResolveCatalogFile()}");
        AppendLog($"Mods: {paths.ResolveRiivDirectory()}");
    }

    private async void ScanButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await ScanAsync();
    }

    private void GamesCombo_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        RefreshMods();
    }

    private async Task ScanAsync()
    {
        SetBusy(true);
        try
        {
            AppendLog("Scanning compatible images...");
            var images = await engine.ScanAsync(CancellationToken.None);
            GamesCombo.ItemsSource = images;
            GamesCombo.SelectedIndex = images.Count > 0 ? 0 : -1;
            if (images.Count == 0)
            {
                AppendLog("No compatible images found.");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void RefreshMods()
    {
        if (GamesCombo.SelectedItem is not GameImage game)
        {
            ModsCombo.ItemsSource = null;
            OutputIdBox.Text = "";
            return;
        }

        var mods = engine.GetAvailableMods(game.Game);
        ModsCombo.ItemsSource = mods;
        ModsCombo.SelectedIndex = mods.Count > 0 ? 0 : -1;
        OutputIdBox.Text = ModsCombo.SelectedItem is ModDefinition mod
            ? OutputIdSuggester.ForCatalogMod(mod, game)
            : "";

        if (mods.Count == 0)
        {
            AppendLog("No local catalog mods found for the selected game.");
        }
    }

    private void SetBusy(bool busy)
    {
        ScanButton.IsEnabled = !busy;
        GamesCombo.IsEnabled = !busy;
        ModsCombo.IsEnabled = !busy;
        ExtensionCombo.IsEnabled = !busy;
        OutputIdBox.IsEnabled = !busy;
        StatusText.Text = busy ? "Working..." : $"Ready - {paths.RootDirectory}";
    }

    private void AppendLog(string message)
    {
        LogBox.Text += $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
        LogBox.CaretIndex = LogBox.Text?.Length ?? 0;
    }
}
