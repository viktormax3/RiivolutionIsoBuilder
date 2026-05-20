using System.Drawing;

namespace RiivolutionIsoBuilder;

public sealed class MainForm : Form
{
    private readonly PatcherPaths paths;
    private readonly PatcherEngine engine;
    private readonly ComboBox gamesCombo = new();
    private readonly ComboBox modsCombo = new();
    private readonly ComboBox extensionCombo = new();
    private readonly TextBox outputIdBox = new();
    private readonly CheckBox bannerCheck = new();
    private readonly Label statusLabel = new();
    private readonly ProgressBar progressBar = new();
    private readonly Button scanButton = new();
    private readonly Button browseButton = new();
    private readonly Button xmlButton = new();
    private readonly Button buildButton = new();
    private readonly TextBox logBox = new();
    private CancellationTokenSource? buildCts;

    public MainForm()
    {
        paths = PatcherPaths.Discover();
        engine = new PatcherEngine(paths, AppendLog);
        InitializeUi();
    }

    private void InitializeUi()
    {
        Text = "Riivolution ISO Builder";
        MinimumSize = new Size(1120, 740);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10F);
        BackColor = Color.FromArgb(238, 241, 245);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(20),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 88,
            BackColor = Color.FromArgb(31, 42, 56),
            Padding = new Padding(18, 16, 18, 16),
            Margin = new Padding(0, 0, 0, 14)
        };
        root.Controls.Add(header, 0, 0);

        var title = new Label
        {
            Text = "Riivolution ISO Builder",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 21F, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(18, 14),
            Margin = new Padding(0)
        };
        header.Controls.Add(title);

        statusLabel.AutoSize = true;
        statusLabel.ForeColor = Color.FromArgb(196, 207, 220);
        statusLabel.Text = $"Listo - {paths.RootDirectory}";
        statusLabel.Location = new Point(21, 54);
        header.Controls.Add(statusLabel);

        var controls = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 4,
            RowCount = 2,
            BackColor = Color.White,
            Padding = new Padding(16),
            Margin = new Padding(0, 0, 0, 12)
        };
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        controls.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        controls.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(controls, 0, 1);

        gamesCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        gamesCombo.DisplayMember = nameof(GameImage.DisplayName);
        gamesCombo.SelectedIndexChanged += (_, _) => RefreshMods();
        AddField(controls, "Juego detectado", gamesCombo, 0, 0);
        controls.SetColumnSpan(controls.GetControlFromPosition(0, 0)!, 2);

        modsCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        modsCombo.DisplayMember = "DisplayName";
        modsCombo.SelectedIndexChanged += (_, _) => RefreshOutputIdSuggestion();
        AddField(controls, "Mod", modsCombo, 2, 0);
        controls.SetColumnSpan(controls.GetControlFromPosition(2, 0)!, 2);

        outputIdBox.CharacterCasing = CharacterCasing.Upper;
        outputIdBox.MaxLength = 6;
        AddField(controls, "ID6", outputIdBox, 0, 1);

        extensionCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        extensionCombo.Items.AddRange(["wbfs", "iso", "ciso", "wdf", "wia"]);
        extensionCombo.SelectedIndex = 0;
        AddField(controls, "Salida", extensionCombo, 1, 1);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 22, 0, 0)
        };
        controls.Controls.Add(buttonPanel, 2, 1);
        controls.SetColumnSpan(buttonPanel, 2);

        scanButton.Text = "Buscar";
        StyleButton(scanButton, Color.FromArgb(65, 78, 96), Color.White);
        scanButton.Click += async (_, _) => await ScanAsync();
        buttonPanel.Controls.Add(scanButton);

        browseButton.Text = "Elegir ISO";
        StyleButton(browseButton, Color.FromArgb(65, 78, 96), Color.White);
        browseButton.Click += async (_, _) => await BrowseAsync();
        buttonPanel.Controls.Add(browseButton);

        xmlButton.Text = "Elegir XML";
        StyleButton(xmlButton, Color.FromArgb(65, 78, 96), Color.White);
        xmlButton.Click += async (_, _) => await BrowseRiivolutionXmlAsync();
        buttonPanel.Controls.Add(xmlButton);

        buildButton.Text = "Crear mod";
        StyleButton(buildButton, Color.FromArgb(23, 132, 92), Color.White);
        buildButton.Click += async (_, _) => await BuildAsync();
        buttonPanel.Controls.Add(buildButton);

        var options = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            BackColor = Color.White,
            Padding = new Padding(14, 10, 14, 10),
            Margin = new Padding(0, 0, 0, 12)
        };
        bannerCheck.Text = "Usar banner personalizado";
        bannerCheck.Checked = true;
        bannerCheck.AutoSize = true;
        bannerCheck.Margin = new Padding(4, 4, 22, 4);
        options.Controls.Add(bannerCheck);
        progressBar.Style = ProgressBarStyle.Marquee;
        progressBar.MarqueeAnimationSpeed = 0;
        progressBar.Width = 220;
        progressBar.Height = 18;
        progressBar.Visible = false;
        progressBar.Margin = new Padding(8, 5, 0, 0);
        options.Controls.Add(progressBar);
        root.Controls.Add(options, 0, 2);

        logBox.Multiline = true;
        logBox.ReadOnly = true;
        logBox.ScrollBars = ScrollBars.Vertical;
        logBox.Dock = DockStyle.Fill;
        logBox.Font = new Font("Consolas", 9.5F);
        logBox.BackColor = Color.FromArgb(20, 26, 35);
        logBox.ForeColor = Color.FromArgb(226, 234, 242);
        logBox.Margin = new Padding(0, 0, 0, 0);
        logBox.BorderStyle = BorderStyle.None;
        root.Controls.Add(logBox, 0, 3);

        AppendLog($"Proyecto detectado: {paths.RootDirectory}");
        AppendLog($"Catalogo: {paths.ResolveCatalogFile()}");
        AppendLog($"Mods: {paths.ResolveRiivDirectory()}");
        _ = ScanAsync();
    }

    private static void AddField(TableLayoutPanel parent, string labelText, Control control, int column, int row)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            RowCount = 2,
            Margin = new Padding(0, 0, 12, 0)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var label = new Label
        {
            Text = labelText,
            AutoSize = true,
            ForeColor = Color.FromArgb(70, 82, 97),
            Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 6)
        };
        control.Dock = DockStyle.Top;
        control.Height = 32;
        control.Font = new Font("Segoe UI", 9.8F);
        panel.Controls.Add(label, 0, 0);
        panel.Controls.Add(control, 0, 1);
        parent.Controls.Add(panel, column, row);
    }

    private static void StyleButton(Button button, Color backColor, Color foreColor)
    {
        button.Width = 126;
        button.Height = 32;
        button.Margin = new Padding(0, 0, 0, 7);
        button.BackColor = backColor;
        button.ForeColor = foreColor;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
    }

    private async Task ScanAsync()
    {
        await RunUiTaskAsync(async token =>
        {
            AppendLog("Buscando imagenes compatibles...");
            var images = await engine.ScanAsync(token);
            gamesCombo.Items.Clear();
            foreach (var image in images)
            {
                gamesCombo.Items.Add(image);
            }

            if (gamesCombo.Items.Count > 0)
            {
                gamesCombo.SelectedIndex = 0;
            }
            else
            {
                AppendLog("No se encontraron imagenes compatibles. Puedes usar Elegir ISO.");
            }
        });
    }

    private async Task BrowseAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Seleccionar backup de Wii",
            Filter = "Imagenes Wii|*.iso;*.wbfs;*.ciso;*.wdf;*.wia|Todos los archivos|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        await RunUiTaskAsync(async token =>
        {
            var image = await engine.InspectImageAsync(dialog.FileName, token);
            if (image is null)
            {
                MessageBox.Show(this, "La imagen seleccionada no coincide con ningun juego del catalogo actual.", "Imagen no compatible", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            gamesCombo.Items.Add(image);
            gamesCombo.SelectedItem = image;
            AppendLog($"Imagen seleccionada: {image.GameId}");
        });
    }

    private async Task BuildAsync()
    {
        if (gamesCombo.SelectedItem is not GameImage game)
        {
            MessageBox.Show(this, "Selecciona un juego primero.", "Faltan datos", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (modsCombo.SelectedItem is null)
        {
            MessageBox.Show(this, "Selecciona un mod o un XML Riivolution primero.", "Faltan datos", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var options = new BuildOptions((string)extensionCombo.SelectedItem!, bannerCheck.Checked);
        await RunUiTaskAsync(async token =>
        {
            if (modsCombo.SelectedItem is NativeRiivolutionMod nativeMod)
            {
                var nativePlan = engine.CreateNativePlan(game, nativeMod, outputIdBox.Text, options);
                AppendLog($"Preparando XML Riivolution {nativeMod.DisplayName} para {nativePlan.OutputId}...");
                await engine.BuildNativeAsync(nativePlan, token);
            }
            else if (modsCombo.SelectedItem is ModDefinition mod)
            {
                var plan = engine.CreatePlan(game, mod, options);
                outputIdBox.Text = plan.OutputId;
                AppendLog($"Preparando {mod.DisplayName} para {plan.OutputId}...");
                await engine.BuildAsync(plan, options, token);
            }

            MessageBox.Show(this, "Imagen modificada creada correctamente.", "Listo", MessageBoxButtons.OK, MessageBoxIcon.Information);
        });
    }

    private async Task BrowseRiivolutionXmlAsync()
    {
        if (gamesCombo.SelectedItem is not GameImage game)
        {
            MessageBox.Show(this, "Selecciona un juego antes de cargar un XML.", "Faltan datos", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = "Seleccionar XML Riivolution",
            Filter = "Riivolution XML|*.xml|Todos los archivos|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        await RunUiTaskAsync(token =>
        {
            var mod = engine.LoadNativeRiivolutionMod(dialog.FileName, game);
            modsCombo.Items.Add(mod);
            modsCombo.SelectedItem = mod;
            outputIdBox.Text = engine.SuggestNativeOutputId(mod, game);
            AppendLog($"XML cargado: {mod.ShortName} - {dialog.FileName}");
            AppendLog($"Patches activos: {string.Join(", ", mod.Plan.ActivePatches.Select(patch => patch.Id))}");
            return Task.CompletedTask;
        });
    }

    private void RefreshMods()
    {
        modsCombo.Items.Clear();
        if (gamesCombo.SelectedItem is not GameImage game)
        {
            return;
        }

        foreach (var mod in engine.GetAvailableMods(game.Game))
        {
            modsCombo.Items.Add(mod);
        }

        if (modsCombo.Items.Count > 0)
        {
            modsCombo.SelectedIndex = 0;
            RefreshOutputIdSuggestion();
        }
        else
        {
            outputIdBox.Clear();
            AppendLog("No hay mods locales para el juego seleccionado. Puedes cargar un XML Riivolution.");
        }
    }

    private void RefreshOutputIdSuggestion()
    {
        if (gamesCombo.SelectedItem is not GameImage game)
        {
            return;
        }

        if (modsCombo.SelectedItem is NativeRiivolutionMod nativeMod)
        {
            outputIdBox.Text = engine.SuggestNativeOutputId(nativeMod, game);
            return;
        }

        if (modsCombo.SelectedItem is ModDefinition mod)
        {
            outputIdBox.Text = $"{mod.OutputIdPrefix ?? mod.Id}{game.GameId[3..6]}".ToUpperInvariant();
        }
    }

    private async Task RunUiTaskAsync(Func<CancellationToken, Task> task)
    {
        SetBusy(true);
        buildCts = new CancellationTokenSource();
        try
        {
            await task(buildCts.Token);
        }
        catch (OperationCanceledException)
        {
            AppendLog("Operacion cancelada.");
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            buildCts.Dispose();
            buildCts = null;
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        scanButton.Enabled = !busy;
        browseButton.Enabled = !busy;
        xmlButton.Enabled = !busy;
        buildButton.Enabled = !busy;
        gamesCombo.Enabled = !busy;
        modsCombo.Enabled = !busy;
        extensionCombo.Enabled = !busy;
        outputIdBox.Enabled = !busy;
        bannerCheck.Enabled = !busy;
        progressBar.Visible = busy;
        progressBar.MarqueeAnimationSpeed = busy ? 35 : 0;
        statusLabel.Text = busy ? "Trabajando..." : $"Listo - {paths.RootDirectory}";
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }

    private void AppendLog(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(AppendLog), message);
            return;
        }

        logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }
}

