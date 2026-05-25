using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using RiivolutionIsoBuilder.Riivolution;

namespace RiivolutionIsoBuilder.UI;

public partial class MainView : UserControl
{
    private readonly PatcherPaths paths;
    private readonly PatcherEngine engine;
    private readonly List<GameImage> gameImages = [];
    private readonly List<object> modChoices = [];
    private readonly List<ComboBox> xmlOptionCombos = [];
    private CancellationTokenSource? operationCts;
    private string? currentXmlFile;
    private RiivolutionDocument? currentXmlDocument;
    private int? currentXmlModIndex;
    private bool updatingXmlOptions;
    private bool compactLayout;

    public MainView()
    {
        InitializeComponent();

        paths = PatcherPaths.Discover();
        engine = new PatcherEngine(paths, AppendLog);
        StatusText.Text = "Ready";
        ProjectRootText.Text = paths.RootDirectory;
        GamesFolderText.Text = paths.GamesDirectory;
        ToolsFolderText.Text = paths.ResolveToolsDirectory();
        ThemeCombo.ItemsSource = new[] { "System", "Light", "Dark" };
        ThemeCombo.SelectedIndex = 0;
        ExtensionCombo.ItemsSource = BuilderDefaults.OutputExtensions;
        ExtensionCombo.SelectedIndex = 0;

        AppendLog($"Project: {paths.RootDirectory}");
        AppendLog($"Catalog: {paths.ResolveCatalogFile()}");
        AppendLog($"Mods: {paths.ResolveRiivDirectory()}");
        AppendLog($"Games folder: {paths.GamesDirectory}");
        AppendLog($"Imports folder: {paths.ImportDirectory}");
        AppendLog($"Tools folder: {paths.ResolveToolsDirectory()}");

        AttachedToVisualTree += async (_, _) => await ScanAsync();
        SizeChanged += (_, _) => UpdateResponsiveLayout(Bounds.Width, Bounds.Height);
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

    private async void XmlButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await BrowseRiivolutionXmlAsync();
    }

    private async void GctButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await BrowseGctAsync();
    }

    private void ClearLogButton_OnClick(object? sender, RoutedEventArgs e)
    {
        LogBox.Text = "";
        AppendLog("Log cleared.");
    }

    private void LogToggleButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var showLog = LogToggleButton.IsChecked == true;
        LogBox.IsVisible = showLog;
        LogToggleButton.Content = showLog ? "Hide" : "Show";
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e)
    {
        operationCts?.Cancel();
        AppendLog("Cancellation requested.");
    }

    private void ThemeCombo_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.RequestedThemeVariant = ThemeCombo.SelectedItem switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
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
                EmptyStateText.Text = OperatingSystem.IsAndroid()
                    ? "No compatible images found in the app workspace. Use Browse ISO to import one."
                    : $"No compatible images found in {paths.GamesDirectory}. Use Browse ISO to select one manually.";
                AppendLog("No compatible images found.");
            }
            else
            {
                EmptyStateText.Text = $"{gameImages.Count} compatible image(s) found. Select one, then choose a mod or load XML/GCT.";
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
        var imagePath = await PickFileForToolAsync(
            "Select Wii backup",
            new FilePickerFileType("Wii images")
            {
                Patterns = BuilderDefaults.InputImageExtensions.Select(extension => $"*.{extension}").ToList()
            },
            "games");
        if (imagePath is null)
        {
            return;
        }

        SetBusy(true);
        operationCts = new CancellationTokenSource();
        try
        {
            AppendLog($"Inspecting image: {imagePath}");
            var image = await engine.InspectImageAsync(imagePath, operationCts.Token);
            if (image is null)
            {
                AppendLog("The selected image is not supported by the current catalog.");
                return;
            }

            AddOrSelectGame(image);
            AppendLog($"Image selected: {image.DisplayName}");
            EmptyStateText.Text = "Image selected. Choose a catalog mod, or load a Riivolution XML/GCT patch.";
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

    private async Task BrowseRiivolutionXmlAsync()
    {
        if (GamesCombo.SelectedItem is not GameImage game)
        {
            AppendLog("Select a game before loading a Riivolution XML.");
            return;
        }

        var xmlFile = await PickFileForToolAsync(
            "Select Riivolution XML",
            new FilePickerFileType("Riivolution XML") { Patterns = ["*.xml"] },
            "xml");
        if (xmlFile is null)
        {
            return;
        }

        try
        {
            var document = RiivolutionPatchReader.ReadDocument(xmlFile, game.Region.Name);
            currentXmlFile = xmlFile;
            currentXmlDocument = document;
            BuildXmlOptionControls(document);
            RefreshLoadedXmlMod();
            AppendLog($"XML loaded: {document.DisplayName} - {xmlFile}");
            EmptyStateText.Text = "Riivolution XML loaded. Review the output ID and build when ready.";
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: {ex.Message}");
        }
    }

    private async Task BrowseGctAsync()
    {
        if (GamesCombo.SelectedItem is not GameImage game)
        {
            AppendLog("Select a game before loading a GCT.");
            return;
        }

        var gctFile = await PickFileForToolAsync(
            "Select GCT patch",
            new FilePickerFileType("Ocarina GCT") { Patterns = ["*.gct"] },
            "gct");
        if (gctFile is null)
        {
            return;
        }

        var displayName = Path.GetFileNameWithoutExtension(gctFile);
        var patch = new ManualGctPatch(gctFile, displayName);
        AddOrSelectMod(patch);
        OutputIdBox.Text = OutputIdSuggester.ForManualPatch(displayName, game);
        AppendLog($"GCT loaded: {displayName} - {gctFile}");
        EmptyStateText.Text = "GCT patch loaded. Review the output ID and build when ready.";
    }

    private async Task BuildAsync()
    {
        if (GamesCombo.SelectedItem is not GameImage game)
        {
            AppendLog("Select a game first.");
            return;
        }

        if (ModsCombo.SelectedItem is null)
        {
            AppendLog("Select a mod, Riivolution XML, or GCT first.");
            return;
        }

        var extension = ExtensionCombo.SelectedItem as string ?? BuilderDefaults.OutputExtensions[0];
        var options = new BuildOptions(extension, UseCustomBannerCheck.IsChecked == true);
        SetBusy(true);
        operationCts = new CancellationTokenSource();
        try
        {
            if (ModsCombo.SelectedItem is NativeRiivolutionMod nativeMod)
            {
                var nativePlan = engine.CreateNativePlan(game, nativeMod, OutputIdBox.Text ?? "", options);
                AppendLog($"Building XML {nativeMod.DisplayName} for {nativePlan.OutputId}...");
                await engine.BuildNativeAsync(nativePlan, operationCts.Token);
            }
            else if (ModsCombo.SelectedItem is ManualGctPatch gctPatch)
            {
                var gctPlan = engine.CreateGctPlan(game, gctPatch, OutputIdBox.Text ?? "", options);
                AppendLog($"Building GCT {gctPatch.DisplayName} for {gctPlan.OutputId}...");
                await engine.BuildGctAsync(gctPlan, operationCts.Token);
            }
            else if (ModsCombo.SelectedItem is ModDefinition mod)
            {
                var plan = engine.CreatePlan(game, mod, options);
                OutputIdBox.Text = plan.OutputId;
                AppendLog($"Building {mod.DisplayName} for {plan.OutputId}...");
                await engine.BuildAsync(plan, options, operationCts.Token);
            }

            AppendLog("Build finished.");
            EmptyStateText.Text = "Build finished. Check the output folder for the generated image.";
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
            modChoices.Clear();
            ClearXmlOptions();
            RefreshModList();
            OutputIdBox.Text = "";
            SelectedGameText.Text = "No game selected.";
            return;
        }

        SelectedGameText.Text = $"{game.GameId} - {game.Game.DisplayName} ({game.Region.Name})";
        modChoices.Clear();
        ClearXmlOptions();
        modChoices.AddRange(engine.GetAvailableMods(game.Game));
        RefreshModList();
        RefreshOutputIdSuggestion();

        if (modChoices.Count == 0)
        {
            AppendLog("No local catalog mods found for the selected game.");
            EmptyStateText.Text = "No catalog mods found for this game. You can still load a Riivolution XML or GCT patch.";
        }
    }

    private void RefreshOutputIdSuggestion()
    {
        if (GamesCombo.SelectedItem is not GameImage game)
        {
            OutputIdBox.Text = "";
            SelectedModText.Text = "No mod selected.";
            return;
        }

        OutputIdBox.Text = ModsCombo.SelectedItem switch
        {
            NativeRiivolutionMod nativeMod => engine.SuggestNativeOutputId(nativeMod, game),
            ManualGctPatch gctPatch => OutputIdSuggester.ForManualPatch(gctPatch.DisplayName, game),
            ModDefinition mod => OutputIdSuggester.ForCatalogMod(mod, game),
            _ => ""
        };
        SelectedModText.Text = ModsCombo.SelectedItem switch
        {
            NativeRiivolutionMod nativeMod => $"Riivolution XML: {nativeMod.DisplayName}",
            ManualGctPatch gctPatch => $"GCT patch: {gctPatch.DisplayName}",
            ModDefinition mod => $"Catalog mod: {mod.DisplayName}",
            _ => "No mod selected."
        };
    }

    private void RefreshModList()
    {
        ModsCombo.ItemsSource = null;
        ModsCombo.ItemsSource = modChoices;
        ModsCombo.SelectedIndex = modChoices.Count > 0 ? 0 : -1;
    }

    private void AddOrSelectMod(object mod)
    {
        modChoices.Add(mod);
        RefreshModList();
        ModsCombo.SelectedIndex = modChoices.Count - 1;
    }

    private void BuildXmlOptionControls(RiivolutionDocument document)
    {
        updatingXmlOptions = true;
        xmlOptionCombos.Clear();
        XmlOptionsPanel.Children.Clear();

        foreach (var option in document.Sections.SelectMany(section => section.Options))
        {
            var panel = new StackPanel { Spacing = 6 };
            panel.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(option.Name) ? option.Id : option.Name,
                Classes = { "label" }
            });

            var combo = new ComboBox
            {
                ItemsSource = new[] { "Disabled" }.Concat(option.Choices.Select(choice => choice.Name)).ToList()
            };
            var defaultIndex = option.DefaultChoice;
            combo.SelectedIndex = defaultIndex >= 0 && defaultIndex < option.Choices.Count + 1 ? defaultIndex : 0;
            combo.SelectionChanged += XmlOptionCombo_OnSelectionChanged;

            panel.Children.Add(combo);
            XmlOptionsPanel.Children.Add(panel);
            xmlOptionCombos.Add(combo);
        }

        XmlOptionsPanelHost.IsVisible = xmlOptionCombos.Count > 0;
        updatingXmlOptions = false;
    }

    private void XmlOptionCombo_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!updatingXmlOptions)
        {
            RefreshLoadedXmlMod();
        }
    }

    private void RefreshLoadedXmlMod()
    {
        if (GamesCombo.SelectedItem is not GameImage game || currentXmlFile is null || currentXmlDocument is null)
        {
            return;
        }

        var choices = xmlOptionCombos.Select(combo => (int?)(combo.SelectedIndex - 1)).ToList();
        var mod = engine.LoadNativeRiivolutionMod(currentXmlFile, game, choices, currentXmlDocument);
        if (currentXmlModIndex is { } index && index >= 0 && index < modChoices.Count)
        {
            modChoices[index] = mod;
        }
        else
        {
            modChoices.Add(mod);
            currentXmlModIndex = modChoices.Count - 1;
        }

        RefreshModList();
        ModsCombo.SelectedIndex = currentXmlModIndex.Value;
        OutputIdBox.Text = engine.SuggestNativeOutputId(mod, game);

        if (!string.IsNullOrWhiteSpace(mod.ChoiceSummary))
        {
            AppendLog($"XML choices: {mod.ChoiceSummary}");
        }

        AppendLog($"Active patches: {string.Join(", ", mod.Plan.ActivePatches.Select(patch => patch.Id))}");
    }

    private void ClearXmlOptions()
    {
        currentXmlFile = null;
        currentXmlDocument = null;
        currentXmlModIndex = null;
        xmlOptionCombos.Clear();
        XmlOptionsPanel.Children.Clear();
        XmlOptionsPanelHost.IsVisible = false;
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
        XmlButton.IsEnabled = !busy;
        GctButton.IsEnabled = !busy;
        ClearLogButton.IsEnabled = !busy;
        CancelButton.IsEnabled = busy;
        GamesCombo.IsEnabled = !busy;
        ModsCombo.IsEnabled = !busy;
        ExtensionCombo.IsEnabled = !busy;
        OutputIdBox.IsEnabled = !busy;
        UseCustomBannerCheck.IsEnabled = !busy;
        Progress.IsVisible = busy;
        StatusText.Text = busy ? "Working..." : "Ready";
    }

    private async Task<string?> PickFileForToolAsync(string title, FilePickerFileType fileType, string importKind)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            AppendLog("ERROR: File picker is not available.");
            return null;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [fileType, FilePickerFileTypes.All]
        });

        var file = files.FirstOrDefault();
        if (file is null)
        {
            return null;
        }

        var localPath = file.TryGetLocalPath();
        if (!OperatingSystem.IsAndroid() && !string.IsNullOrWhiteSpace(localPath))
        {
            return localPath;
        }

        if (!string.IsNullOrWhiteSpace(localPath) && IsInsideDirectory(localPath, paths.RootDirectory))
        {
            return localPath;
        }

        return await ImportPickedFileAsync(file, importKind);
    }

    private async Task<string?> ImportPickedFileAsync(IStorageFile file, string importKind)
    {
        var destinationDirectory = Path.Combine(paths.ImportDirectory, importKind);
        Directory.CreateDirectory(destinationDirectory);

        var fileName = SanitizeFileName(file.Name);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = $"selected-{DateTime.UtcNow:yyyyMMddHHmmss}";
        }

        var destination = CreateUniqueImportPath(destinationDirectory, fileName);
        AppendLog($"Importing selected file to workspace: {destination}");
        var wasBusy = Progress.IsVisible;
        if (!wasBusy)
        {
            SetBusy(true);
            StatusText.Text = "Importing...";
        }

        try
        {
            await using var source = await file.OpenReadAsync();
            await using var target = File.Create(destination);
            await source.CopyToAsync(target);
            AppendLog($"Import finished: {Path.GetFileName(destination)}");
            return destination;
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: Could not import selected file. {ex.Message}");
            return null;
        }
        finally
        {
            if (!wasBusy)
            {
                SetBusy(false);
            }
        }
    }

    private static bool IsInsideDirectory(string path, string directory)
    {
        var fullPath = Path.GetFullPath(path);
        var fullDirectory = Path.GetFullPath(directory);
        if (!fullDirectory.EndsWith(Path.DirectorySeparatorChar))
        {
            fullDirectory += Path.DirectorySeparatorChar;
        }

        return fullPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeFileName(string name)
    {
        var cleaned = new string(name.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character).ToArray());
        return Path.GetFileName(cleaned);
    }

    private static string CreateUniqueImportPath(string directory, string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var candidate = Path.Combine(directory, fileName);
        var index = 1;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{name}-{index}{extension}");
            index++;
        }

        return candidate;
    }

    private void UpdateResponsiveLayout(double width, double height)
    {
        var shouldUseCompactLayout = width < 900 || height < 700;
        if (shouldUseCompactLayout == compactLayout)
        {
            return;
        }

        compactLayout = shouldUseCompactLayout;
        if (compactLayout)
        {
            RootGrid.Margin = new Thickness(10);
            WorkflowGrid.ColumnDefinitions = new ColumnDefinitions("*");
            WorkflowGrid.RowDefinitions = new RowDefinitions("Auto,Auto");
            PathsGrid.ColumnDefinitions = new ColumnDefinitions("*");
            PathsGrid.RowDefinitions = new RowDefinitions("Auto,Auto,Auto");
            Grid.SetColumn(ProjectPathPanel, 0);
            Grid.SetRow(ProjectPathPanel, 0);
            Grid.SetColumn(GamesPathPanel, 0);
            Grid.SetRow(GamesPathPanel, 1);
            Grid.SetColumn(ToolsPathPanel, 0);
            Grid.SetRow(ToolsPathPanel, 2);
            Grid.SetColumn(WorkflowRight, 0);
            Grid.SetRow(WorkflowRight, 1);
            LogBox.Height = Math.Max(180, Math.Min(320, height * 0.45));
            return;
        }

        RootGrid.Margin = new Thickness(16);
        WorkflowGrid.ColumnDefinitions = new ColumnDefinitions("430,*");
        WorkflowGrid.RowDefinitions = new RowDefinitions("*");
        PathsGrid.ColumnDefinitions = new ColumnDefinitions("*,*,*");
        PathsGrid.RowDefinitions = new RowDefinitions("Auto");
        Grid.SetColumn(ProjectPathPanel, 0);
        Grid.SetRow(ProjectPathPanel, 0);
        Grid.SetColumn(GamesPathPanel, 1);
        Grid.SetRow(GamesPathPanel, 0);
        Grid.SetColumn(ToolsPathPanel, 2);
        Grid.SetRow(ToolsPathPanel, 0);
        Grid.SetColumn(WorkflowRight, 1);
        Grid.SetRow(WorkflowRight, 0);
        LogBox.Height = 220;
    }

    private void AppendLog(string message)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => AppendLog(message));
            return;
        }

        LogBox.Text += $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
        LogBox.CaretIndex = LogBox.Text?.Length ?? 0;
    }
}
