using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace RiivolutionIsoBuilder.Avalonia;

public partial class MainWindow : Window
{
    private readonly PatcherPaths paths;
    private readonly PatcherEngine engine;
    private readonly List<GameImage> gameImages = [];
    private CancellationTokenSource? operationCts;

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

    private async void BrowseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await BrowseIsoAsync();
    }

    private async void BuildButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await BuildAsync();
    }

    private void GamesCombo_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        RefreshMods();
    }

    private void ModsCombo_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        RefreshOutputIdSuggestion();
    }

    private async Task ScanAsync()
    {
        SetBusy(true);
        operationCts = new CancellationTokenSource();
        try
        {
            AppendLog("Scanning compatible images...");
            var images = await engine.ScanAsync(operationCts.Token);
            gameImages.Clear();
            gameImages.AddRange(images);
            RefreshGameList();
            if (gameImages.Count == 0)
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
            operationCts.Dispose();
            operationCts = null;
            SetBusy(false);
        }
    }

    private async Task BrowseIsoAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            AppendLog("ERROR: File picker is not available.");
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Wii backup",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Wii images")
                {
                    Patterns = BuilderDefaults.InputImageExtensions.Select(extension => $"*.{extension}").ToList()
                },
                FilePickerFileTypes.All
            ]
        });
        var file = files.FirstOrDefault();
        if (file is null)
        {
            return;
        }

        SetBusy(true);
        operationCts = new CancellationTokenSource();
        try
        {
            var imagePath = file.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                AppendLog("ERROR: The selected file does not expose a local path.");
                return;
            }

            AppendLog($"Inspecting image: {imagePath}");
            var image = await engine.InspectImageAsync(imagePath, operationCts.Token);
            if (image is null)
            {
                AppendLog("The selected image is not supported by the current catalog.");
                return;
            }

            AddOrSelectGame(image);
            AppendLog($"Image selected: {image.DisplayName}");
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: {ex.Message}");
        }
        finally
        {
            operationCts.Dispose();
            operationCts = null;
            SetBusy(false);
        }
    }

    private async Task BuildAsync()
    {
        if (GamesCombo.SelectedItem is not GameImage game)
        {
            AppendLog("Select a game first.");
            return;
        }

        if (ModsCombo.SelectedItem is not ModDefinition mod)
        {
            AppendLog("Select a catalog mod first.");
            return;
        }

        var extension = ExtensionCombo.SelectedItem as string ?? BuilderDefaults.OutputExtensions[0];
        var options = new BuildOptions(extension, UseCustomBannerCheck.IsChecked == true);
        SetBusy(true);
        operationCts = new CancellationTokenSource();
        try
        {
            var plan = engine.CreatePlan(game, mod, options);
            OutputIdBox.Text = plan.OutputId;
            AppendLog($"Building {mod.DisplayName} for {plan.OutputId}...");
            await engine.BuildAsync(plan, options, operationCts.Token);
            AppendLog("Build finished.");
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: {ex.Message}");
        }
        finally
        {
            operationCts.Dispose();
            operationCts = null;
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
        RefreshOutputIdSuggestion();

        if (mods.Count == 0)
        {
            AppendLog("No local catalog mods found for the selected game.");
        }
    }

    private void RefreshOutputIdSuggestion()
    {
        if (GamesCombo.SelectedItem is not GameImage game || ModsCombo.SelectedItem is not ModDefinition mod)
        {
            OutputIdBox.Text = "";
            return;
        }

        OutputIdBox.Text = OutputIdSuggester.ForCatalogMod(mod, game);
    }

    private void RefreshGameList()
    {
        GamesCombo.ItemsSource = null;
        GamesCombo.ItemsSource = gameImages;
        GamesCombo.SelectedIndex = gameImages.Count > 0 ? 0 : -1;
    }

    private void AddOrSelectGame(GameImage image)
    {
        var existingIndex = gameImages.FindIndex(current =>
            Path.GetFullPath(current.Path).Equals(Path.GetFullPath(image.Path), StringComparison.OrdinalIgnoreCase));
        if (existingIndex < 0)
        {
            gameImages.Add(image);
            existingIndex = gameImages.Count - 1;
        }

        RefreshGameList();
        GamesCombo.SelectedIndex = existingIndex;
    }

    private void SetBusy(bool busy)
    {
        ScanButton.IsEnabled = !busy;
        BrowseButton.IsEnabled = !busy;
        BuildButton.IsEnabled = !busy;
        GamesCombo.IsEnabled = !busy;
        ModsCombo.IsEnabled = !busy;
        ExtensionCombo.IsEnabled = !busy;
        OutputIdBox.IsEnabled = !busy;
        UseCustomBannerCheck.IsEnabled = !busy;
        Progress.IsVisible = busy;
        StatusText.Text = busy ? "Working..." : $"Ready - {paths.RootDirectory}";
    }

    private void AppendLog(string message)
    {
        LogBox.Text += $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
        LogBox.CaretIndex = LogBox.Text?.Length ?? 0;
    }
}
