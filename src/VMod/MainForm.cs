using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Reflection;

namespace VMod;

internal sealed class MainForm : Form
{
    private readonly PackageRepository _repository = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly ModEngine _engine;
    private readonly AppSettings _settings;
    private IReadOnlyList<ModPackage> _packages = Array.Empty<ModPackage>();
    private IReadOnlyList<ModView> _views = Array.Empty<ModView>();
    private string _filter = "all";

    private readonly Panel _header = new();
    private readonly Panel _summary = new();
    private readonly Panel _toolbar = new();
    private readonly Panel _footer = new();
    private readonly FlowLayoutPanel _list = new();
    private readonly PictureBox _logo = new();
    private readonly Label _targetPath = new();
    private readonly Label _status = new();
    private readonly StatCard _installed = new("Установлено", VTheme.Green);
    private readonly StatCard _disabled = new("Отключено", VTheme.Amber);
    private readonly StatCard _newMods = new("Новых", VTheme.Purple);
    private readonly DropZone _drop = new();
    private readonly TextBox _search = new();
    private readonly VButton _gameTarget = new() { Text = "ИГРА", Kind = VButtonKind.Secondary };
    private readonly VButton _serverTarget = new() { Text = "СЕРВЕР", Kind = VButtonKind.Secondary };
    private readonly VButton _allFilter = new() { Text = "Все", Kind = VButtonKind.Primary };
    private readonly VButton _installedFilter = new() { Text = "Установлены", Kind = VButtonKind.Ghost };
    private readonly VButton _disabledFilter = new() { Text = "Отключены", Kind = VButtonKind.Ghost };
    private readonly VButton _newFilter = new() { Text = "Новые", Kind = VButtonKind.Ghost };
    private readonly List<VButton> _busyButtons = new();
    private bool _busy;

    public MainForm()
    {
        _engine = new ModEngine(_repository);
        _settings = _settingsStore.Load();
        if (String.IsNullOrWhiteSpace(_settings.GamePath))
            _settings.GamePath = SteamLocator.Detect(InstallTarget.Game);
        if (String.IsNullOrWhiteSpace(_settings.ServerPath))
            _settings.ServerPath = SteamLocator.Detect(InstallTarget.Server);
        _settingsStore.Save(_settings);

        Text = "VMod — менеджер модов Valheim";
        ClientSize = new Size(1220, 720);
        MinimumSize = new Size(1236, 680);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = VTheme.Back;
        ForeColor = VTheme.Text;
        Font = VTheme.Font(10F);
        AllowDrop = true;
        DoubleBuffered = true;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        BuildHeader();
        BuildSummary();
        BuildToolbar();
        BuildList();
        BuildFooter();
        WireDragDrop(this);
        WireDragDrop(_drop);
        _drop.Click += (_, _) => BrowseForMods();
        Shown += (_, _) => RefreshLibrary();
    }

    private InstallTarget Target => _settings.SelectedTarget;
    private string CurrentRoot => Target == InstallTarget.Game ? _settings.GamePath : _settings.ServerPath;

    private void BuildHeader()
    {
        _header.Dock = DockStyle.Top;
        _header.Height = 86;
        _header.BackColor = Color.FromArgb(18, 24, 28);
        _header.Paint += (_, e) =>
        {
            using LinearGradientBrush gradient = new(_header.ClientRectangle, Color.FromArgb(37, 28, 22), Color.FromArgb(15, 25, 29), LinearGradientMode.Horizontal);
            e.Graphics.FillRectangle(gradient, _header.ClientRectangle);
            using Pen ember = new(Color.FromArgb(120, VTheme.Amber), 1F);
            e.Graphics.DrawLine(ember, 0, _header.Height - 1, _header.Width, _header.Height - 1);
        };
        Controls.Add(_header);

        using Stream logoStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("assets.vmod-logo.png")!;
        using Image logoSource = Image.FromStream(logoStream);
        _logo.Image = new Bitmap(logoSource);
        _logo.SizeMode = PictureBoxSizeMode.Zoom;
        _logo.Location = new Point(22, 8);
        _logo.Size = new Size(70, 70);
        _logo.BackColor = Color.Transparent;
        _header.Controls.Add(_logo);

        Label title = new()
        {
            Text = "VMod",
            Font = VTheme.Font(24F, FontStyle.Bold, "Segoe UI Semibold"),
            ForeColor = VTheme.Text,
            Location = new Point(102, 12),
            AutoSize = true,
            BackColor = Color.Transparent
        };
        Label subtitle = new()
        {
            Text = "VALHEIM MOD MANAGER   •   VERSION 2.0.0",
            Font = VTheme.Font(8.5F, FontStyle.Bold, "Segoe UI Semibold"),
            ForeColor = VTheme.AmberBright,
            Location = new Point(105, 54),
            AutoSize = true,
            BackColor = Color.Transparent
        };
        _header.Controls.Add(title);
        _header.Controls.Add(subtitle);

        _gameTarget.SetBounds(700, 23, 98, 40);
        _serverTarget.SetBounds(804, 23, 108, 40);
        VButton update = new() { Text = "↻  Обновить", Kind = VButtonKind.Teal };
        update.SetBounds(930, 23, 125, 40);
        VButton settings = new() { Text = "⚙  Настройки", Kind = VButtonKind.Secondary };
        settings.SetBounds(1063, 23, 135, 40);
        _gameTarget.Anchor = _serverTarget.Anchor = update.Anchor = settings.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _gameTarget.Click += (_, _) => SetTarget(InstallTarget.Game);
        _serverTarget.Click += (_, _) => SetTarget(InstallTarget.Server);
        update.Click += async (_, _) => await CheckUpdatesAsync();
        settings.Click += (_, _) => ShowSettings();
        _busyButtons.AddRange(new[] { _gameTarget, _serverTarget, update, settings });
        _header.Controls.AddRange(new Control[] { _gameTarget, _serverTarget, update, settings });
    }

    private void BuildSummary()
    {
        _summary.Dock = DockStyle.Top;
        _summary.Height = 118;
        _summary.Padding = new Padding(26, 16, 26, 14);
        _summary.BackColor = Color.Transparent;
        Controls.Add(_summary);
        _summary.BringToFront();

        _installed.Location = new Point(26, 16);
        _disabled.Location = new Point(216, 16);
        _newMods.Location = new Point(406, 16);
        _drop.Location = new Point(606, 16);
        _drop.Size = new Size(588, 88);
        _drop.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _summary.Controls.AddRange(new Control[] { _installed, _disabled, _newMods, _drop });
    }

    private void BuildToolbar()
    {
        _toolbar.Dock = DockStyle.Top;
        _toolbar.Height = 58;
        _toolbar.Padding = new Padding(26, 8, 26, 8);
        _toolbar.BackColor = Color.Transparent;
        Controls.Add(_toolbar);
        _toolbar.BringToFront();

        RunePanel searchShell = new() { Location = new Point(26, 7), Size = new Size(380, 42), Radius = 10, FillColor = VTheme.BackRaised };
        Label glass = new() { Text = "⌕", Font = VTheme.Font(16F, FontStyle.Regular, "Segoe UI Symbol"), ForeColor = VTheme.Muted, Location = new Point(12, 6), Size = new Size(30, 28), BackColor = Color.Transparent };
        _search.BorderStyle = BorderStyle.None;
        _search.BackColor = VTheme.BackRaised;
        _search.ForeColor = VTheme.Text;
        _search.Font = VTheme.Font(10.5F);
        _search.Location = new Point(43, 10);
        _search.Size = new Size(320, 24);
        _search.PlaceholderText = "Поиск по модам…";
        _search.TextChanged += (_, _) => RenderCards();
        searchShell.Controls.Add(glass);
        searchShell.Controls.Add(_search);
        _toolbar.Controls.Add(searchShell);

        _allFilter.SetBounds(700, 8, 72, 40);
        _installedFilter.SetBounds(780, 8, 128, 40);
        _disabledFilter.SetBounds(916, 8, 124, 40);
        _newFilter.SetBounds(1048, 8, 104, 40);
        foreach (VButton button in new[] { _allFilter, _installedFilter, _disabledFilter, _newFilter })
            button.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _allFilter.Click += (_, _) => SetFilter("all");
        _installedFilter.Click += (_, _) => SetFilter("installed");
        _disabledFilter.Click += (_, _) => SetFilter("disabled");
        _newFilter.Click += (_, _) => SetFilter("new");
        _busyButtons.AddRange(new[] { _allFilter, _installedFilter, _disabledFilter, _newFilter });
        _toolbar.Controls.AddRange(new Control[] { _allFilter, _installedFilter, _disabledFilter, _newFilter });
    }

    private void BuildList()
    {
        _list.Dock = DockStyle.Fill;
        _list.Padding = new Padding(26, 8, 18, 14);
        _list.FlowDirection = FlowDirection.TopDown;
        _list.WrapContents = false;
        _list.AutoScroll = true;
        _list.BackColor = Color.Transparent;
        _list.SizeChanged += (_, _) => ResizeCards();
        Controls.Add(_list);
        _list.BringToFront();
    }

    private void BuildFooter()
    {
        _footer.Dock = DockStyle.Bottom;
        _footer.Height = 92;
        _footer.Padding = new Padding(26, 13, 26, 13);
        _footer.BackColor = Color.FromArgb(17, 23, 27);
        _footer.Paint += (_, e) =>
        {
            using Pen line = new(Color.FromArgb(72, 61, 49));
            e.Graphics.DrawLine(line, 0, 0, _footer.Width, 0);
        };
        Controls.Add(_footer);
        _footer.BringToFront();

        VButton installSet = new() { Text = "Установить набор", Kind = VButtonKind.Primary };
        VButton newGame = new() { Text = "Новые → игра", Kind = VButtonKind.Secondary };
        VButton newServer = new() { Text = "Новые → сервер", Kind = VButtonKind.Secondary };
        VButton removeMods = new() { Text = "Удалить…", Kind = VButtonKind.Danger };
        VButton launchGame = new() { Text = "▶  Запустить игру", Kind = VButtonKind.Teal };
        VButton launchServer = new() { Text = "▶  Запустить сервер", Kind = VButtonKind.Teal };
        installSet.SetBounds(26, 16, 190, 50);
        newGame.SetBounds(228, 16, 164, 50);
        newServer.SetBounds(402, 16, 170, 50);
        removeMods.SetBounds(586, 16, 142, 50);
        launchGame.SetBounds(748, 16, 196, 50);
        launchServer.SetBounds(954, 16, 218, 50);
        launchGame.Anchor = launchServer.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        installSet.Click += async (_, _) => await InstallSetAsync();
        newGame.Click += async (_, _) => await InstallNewAsync(InstallTarget.Game);
        newServer.Click += async (_, _) => await InstallNewAsync(InstallTarget.Server);
        removeMods.Click += async (_, _) => await RemoveSelectedAsync();
        launchGame.Click += (_, _) => Launch(InstallTarget.Game);
        launchServer.Click += (_, _) => Launch(InstallTarget.Server);
        _busyButtons.AddRange(new[] { installSet, newGame, newServer, removeMods, launchGame, launchServer });
        _footer.Controls.AddRange(new Control[] { installSet, newGame, newServer, removeMods, launchGame, launchServer });

        _status.Text = "Готово";
        _status.ForeColor = VTheme.Muted;
        _status.Location = new Point(26, 68);
        _status.Size = new Size(1146, 18);
        _status.TextAlign = ContentAlignment.MiddleCenter;
        _status.Anchor = AnchorStyles.Top;
        _footer.Controls.Add(_status);
        _list.BringToFront();
    }

    private void RefreshLibrary()
    {
        _packages = _repository.LoadAll();
        _views = _engine.BuildViews(_packages, CurrentRoot, Target);
        DashboardStats stats = _engine.GetStats(_views);
        _installed.SetValue(stats.Installed);
        _disabled.SetValue(stats.Disabled);
        _newMods.SetValue(stats.New);
        _targetPath.Text = CurrentRoot;
        _gameTarget.Kind = Target == InstallTarget.Game ? VButtonKind.Primary : VButtonKind.Secondary;
        _serverTarget.Kind = Target == InstallTarget.Server ? VButtonKind.Primary : VButtonKind.Secondary;
        _gameTarget.Invalidate();
        _serverTarget.Invalidate();
        RenderCards();
    }

    private void RenderCards()
    {
        _list.SuspendLayout();
        try
        {
            _list.Controls.Clear();
            string query = _search.Text.Trim();
            IEnumerable<ModView> filtered = _views;
            if (query.Length > 0)
                filtered = filtered.Where(view => view.Package.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase) || view.Package.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
            filtered = _filter switch
            {
                "installed" => filtered.Where(view => view.State == ModState.Installed),
                "disabled" => filtered.Where(view => view.State == ModState.Disabled),
                "new" => filtered.Where(view => view.State == ModState.New),
                _ => filtered
            };

            foreach (ModView view in filtered)
            {
                ModCard card = new(view) { Width = Math.Max(760, _list.ClientSize.Width - 52) };
                card.InstallRequested += async (_, _) => await InstallOneAsync(card.View.Package);
                card.ToggleRequested += async (_, _) => await ToggleAsync(card.View);
                card.RemoveRequested += async (_, _) => await RemoveAsync(card.View.Package);
                _list.Controls.Add(card);
            }
            if (_list.Controls.Count == 0)
            {
                Label empty = new()
                {
                    Text = "Здесь пока пусто",
                    ForeColor = VTheme.Muted,
                    Font = VTheme.Font(13F, FontStyle.Regular, "Segoe UI Semibold"),
                    Size = new Size(420, 60),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Margin = new Padding(12, 25, 0, 0)
                };
                _list.Controls.Add(empty);
            }
        }
        finally
        {
            _list.ResumeLayout();
        }
    }

    private void ResizeCards()
    {
        foreach (Control control in _list.Controls)
            if (control is ModCard)
                control.Width = Math.Max(760, _list.ClientSize.Width - 52);
    }

    private void SetTarget(InstallTarget target)
    {
        if (_busy || Target == target)
            return;
        _settings.SelectedTarget = target;
        if (target == InstallTarget.Game && String.IsNullOrWhiteSpace(_settings.GamePath))
            _settings.GamePath = SteamLocator.Detect(target);
        if (target == InstallTarget.Server && String.IsNullOrWhiteSpace(_settings.ServerPath))
            _settings.ServerPath = SteamLocator.Detect(target);
        _settingsStore.Save(_settings);
        RefreshLibrary();
    }

    private void SetFilter(string filter)
    {
        _filter = filter;
        _allFilter.Kind = filter == "all" ? VButtonKind.Primary : VButtonKind.Ghost;
        _installedFilter.Kind = filter == "installed" ? VButtonKind.Primary : VButtonKind.Ghost;
        _disabledFilter.Kind = filter == "disabled" ? VButtonKind.Primary : VButtonKind.Ghost;
        _newFilter.Kind = filter == "new" ? VButtonKind.Primary : VButtonKind.Ghost;
        foreach (VButton button in new[] { _allFilter, _installedFilter, _disabledFilter, _newFilter }) button.Invalidate();
        RenderCards();
    }

    private async Task InstallSetAsync()
    {
        string? root = EnsurePath(Target);
        if (root == null) return;
        string label = Target == InstallTarget.Game ? "игру" : "сервер";
        if (MessageBox.Show(this, "Установить полный совместимый набор модов в " + label + "?", "VMod", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        await RunOperationAsync(() => _engine.InstallCoreSet(_packages, root, Target, new Progress<string>(SetStatus)), "Набор установлен");
    }

    private async Task InstallNewAsync(InstallTarget target)
    {
        string? root = EnsurePath(target);
        if (root == null) return;
        IReadOnlyList<ModPackage> packages = _repository.LoadAll();
        int count = packages.Count(package => package.IsCustom && _engine.GetState(package, root) == ModState.New);
        if (count == 0)
        {
            MessageBox.Show(this, "Новых модов для установки нет. Перетащите ZIP в верхнее поле.", "VMod", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        await RunOperationAsync(() => _engine.InstallNew(packages, root, target, new Progress<string>(SetStatus)), "Новые моды установлены");
    }

    private async Task InstallOneAsync(ModPackage package)
    {
        string? root = EnsurePath(Target);
        if (root == null) return;
        await RunOperationAsync(() => _engine.InstallOne(package, _packages, root, Target, new Progress<string>(SetStatus)), package.DisplayName + " установлен");
    }

    private async Task ToggleAsync(ModView view)
    {
        string? root = EnsurePath(Target);
        if (root == null) return;
        bool enable = view.State == ModState.Disabled;
        if (!enable && view.Package.Name.Equals("EquipmentAndQuickSlots", StringComparison.OrdinalIgnoreCase))
        {
            if (MessageBox.Show(this, "Перед отключением переложите предметы из дополнительных слотов в обычный инвентарь. Продолжить?", "Важное предупреждение", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
        }
        await RunOperationAsync(() => _engine.SetEnabled(view.Package, root, Target, enable), enable ? "Мод включён" : "Мод отключён");
    }

    private async Task RemoveAsync(ModPackage package)
    {
        string? root = EnsurePath(Target);
        if (root == null) return;
        string warning = "Удалить «" + package.DisplayName + "»? Файлы будут сохранены в резервную копию.";
        if (package.Name.Equals("EquipmentAndQuickSlots", StringComparison.OrdinalIgnoreCase))
            warning += "\r\n\r\nСначала переложите предметы из дополнительных слотов.";
        if (MessageBox.Show(this, warning, "Удаление мода", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        await RunOperationAsync(() => _engine.Remove(package, root, Target), "Мод удалён");
    }

    private async Task RemoveSelectedAsync()
    {
        string? root = EnsurePath(Target);
        if (root == null) return;
        IReadOnlyList<ModPackage> installed = _packages
            .Where(package => (Target == InstallTarget.Game || package.InstallOnServer) && _engine.GetState(package, root) is ModState.Installed or ModState.Disabled)
            .ToArray();
        if (installed.Count == 0)
        {
            MessageBox.Show(this, "В выбранной папке нет модов, установленных через VMod.", "VMod", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using RemoveModsDialog dialog = new(installed);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Selected.Count == 0)
            return;
        if (dialog.Selected.Any(package => package.Name.Equals("EquipmentAndQuickSlots", StringComparison.OrdinalIgnoreCase)) &&
            MessageBox.Show(this, "Перед удалением «Снаряжение и быстрые слоты» переложите предметы в обычный инвентарь. Продолжить?", "Важное предупреждение", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        await RunOperationAsync(() =>
        {
            OperationResult total = new();
            foreach (ModPackage package in dialog.Selected)
            {
                OperationResult result = _engine.Remove(package, root, Target);
                total.PackageCount += result.PackageCount;
                total.FileCount += result.FileCount;
                total.BackupPath ??= result.BackupPath;
            }
            return total;
        }, "Выбранные моды удалены");
    }

    private async Task RunOperationAsync(Func<OperationResult> operation, string success)
    {
        SetBusy(true);
        try
        {
            OperationResult result = await Task.Run(operation);
            SetStatus(success + "  •  файлов: " + result.FileCount);
            RefreshLibrary();
        }
        catch (Exception ex)
        {
            SetStatus("Ошибка");
            MessageBox.Show(this, ex.Message, "VMod", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetBusy(false); }
    }

    private void BrowseForMods()
    {
        using OpenFileDialog dialog = new() { Title = "Добавить ZIP-моды в VMod", Filter = "Thunderstore ZIP (*.zip)|*.zip", Multiselect = true };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            ImportMods(dialog.FileNames);
    }

    private void ImportMods(IEnumerable<string> files)
    {
        try
        {
            IReadOnlyList<ModPackage> imported = _repository.Import(files);
            if (imported.Count == 0)
                throw new InvalidDataException("Не найдено подходящих ZIP-файлов.");
            _filter = "new";
            _search.Clear();
            RefreshLibrary();
            SetFilter("new");
            MessageBox.Show(this, "Сохранено в библиотеке VMod: " + imported.Count + ".\r\nТеперь можно установить новые моды в игру или на сервер.", "Моды добавлены", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Не удалось добавить мод", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void WireDragDrop(Control control)
    {
        control.DragEnter += (_, e) =>
        {
            bool ok = e.Data?.GetDataPresent(DataFormats.FileDrop) == true && ((string[])e.Data.GetData(DataFormats.FileDrop)!).Any(path => Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase));
            e.Effect = ok ? DragDropEffects.Copy : DragDropEffects.None;
            _drop.Active = ok;
        };
        control.DragLeave += (_, _) => _drop.Active = false;
        control.DragDrop += (_, e) =>
        {
            _drop.Active = false;
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files)
                ImportMods(files);
        };
    }

    private string? EnsurePath(InstallTarget target)
    {
        string current = target == InstallTarget.Game ? _settings.GamePath : _settings.ServerPath;
        if (SteamLocator.IsValid(current, target))
            return current;
        using FolderBrowserDialog dialog = new()
        {
            Description = target == InstallTarget.Game ? "Выберите папку Valheim" : "Выберите папку выделенного сервера Valheim",
            ShowNewFolderButton = false,
            SelectedPath = Directory.Exists(current) ? current : ""
        };
        if (dialog.ShowDialog(this) != DialogResult.OK || !SteamLocator.IsValid(dialog.SelectedPath, target))
        {
            MessageBox.Show(this, "В выбранной папке не найден Valheim.", "Неверная папка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
        if (target == InstallTarget.Game) _settings.GamePath = dialog.SelectedPath; else _settings.ServerPath = dialog.SelectedPath;
        _settingsStore.Save(_settings);
        RefreshLibrary();
        return dialog.SelectedPath;
    }

    private void ShowSettings()
    {
        using SettingsDialog dialog = new(_settings);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        _settings.GamePath = dialog.GamePath;
        _settings.ServerPath = dialog.ServerPath;
        _settingsStore.Save(_settings);
        RefreshLibrary();
    }

    private void Launch(InstallTarget target)
    {
        string? root = EnsurePath(target);
        if (root == null) return;
        try
        {
            if (target == InstallTarget.Game) _engine.LaunchGame(root); else _engine.LaunchServer(root);
            SetStatus(target == InstallTarget.Game ? "Valheim запускается…" : "Сервер запускается…");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Не удалось запустить", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private async Task CheckUpdatesAsync()
    {
        SetBusy(true);
        try
        {
            SetStatus("Проверка обновлений…");
            UpdateManifest manifest = await UpdateService.FetchAsync();
            Version current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
            if (manifest.Version <= current)
            {
                SetStatus("Установлена последняя версия");
                MessageBox.Show(this, "Установлена последняя версия VMod " + current + ".", "VMod", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string message = "Доступна VMod " + manifest.Version + ".\r\n\r\n" + manifest.Notes.Replace("\\n", "\r\n") + "\r\n\r\nСкачать и установить?";
            if (MessageBox.Show(this, message, "Обновление VMod", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            SetStatus("Загрузка обновления…");
            string downloaded = await UpdateService.DownloadAndVerifyAsync(manifest);
            UpdateService.StartReplacement(downloaded);
            Application.Exit();
        }
        catch (Exception ex)
        {
            SetStatus("Ошибка обновления");
            MessageBox.Show(this, ex.Message, "Обновление VMod", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetBusy(false); }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        foreach (VButton button in _busyButtons) button.Enabled = !busy;
        _drop.Enabled = !busy;
        UseWaitCursor = busy;
    }

    private void SetStatus(string text)
    {
        if (InvokeRequired) { BeginInvoke(new Action(() => SetStatus(text))); return; }
        _status.Text = text;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using LinearGradientBrush gradient = new(ClientRectangle, Color.FromArgb(17, 22, 25), VTheme.Back, LinearGradientMode.Vertical);
        e.Graphics.FillRectangle(gradient, ClientRectangle);
        using Pen grain = new(Color.FromArgb(15, VTheme.Amber));
        for (int y = 92; y < Height; y += 37)
            e.Graphics.DrawBezier(grain, 0, y, Width / 3, y - 7, Width * 2 / 3, y + 8, Width, y - 2);
    }
}

internal sealed class RemoveModsDialog : Form
{
    private readonly CheckedListBox _list = new();
    private readonly IReadOnlyList<ModPackage> _packages;
    public IReadOnlyList<ModPackage> Selected => _list.CheckedIndices.Cast<int>().Select(index => _packages[index]).ToArray();

    public RemoveModsDialog(IReadOnlyList<ModPackage> packages)
    {
        _packages = packages;
        Text = "Удаление модов — VMod";
        ClientSize = new Size(620, 540);
        MinimumSize = new Size(560, 480);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = VTheme.Back;
        ForeColor = VTheme.Text;
        Font = VTheme.Font(10F);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        Controls.Add(new Label { Text = "Какие моды удалить?", Font = VTheme.Font(18F, FontStyle.Bold, "Segoe UI Semibold"), ForeColor = VTheme.AmberBright, Location = new Point(24, 18), AutoSize = true });
        Controls.Add(new Label { Text = "Файлы будут перенесены в резервную копию внутри BepInEx\\VMod.", ForeColor = VTheme.Muted, Location = new Point(27, 58), AutoSize = true });
        _list.SetBounds(27, 92, 566, 362);
        _list.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _list.BackColor = VTheme.BackRaised;
        _list.ForeColor = VTheme.Text;
        _list.BorderStyle = BorderStyle.FixedSingle;
        _list.CheckOnClick = true;
        foreach (ModPackage package in packages)
            _list.Items.Add(package.DisplayName + "  ·  " + package.VersionLabel, !package.IsComponent);
        Controls.Add(_list);

        VButton all = new() { Text = "Выбрать все", Kind = VButtonKind.Ghost };
        VButton remove = new() { Text = "Удалить выбранные", Kind = VButtonKind.Danger, DialogResult = DialogResult.OK };
        VButton cancel = new() { Text = "Отмена", Kind = VButtonKind.Secondary, DialogResult = DialogResult.Cancel };
        all.SetBounds(27, 475, 135, 42);
        cancel.SetBounds(300, 475, 125, 42);
        remove.SetBounds(435, 475, 158, 42);
        all.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        cancel.Anchor = remove.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        all.Click += (_, _) => { for (int index = 0; index < _list.Items.Count; index++) _list.SetItemChecked(index, true); };
        Controls.AddRange(new Control[] { all, cancel, remove });
        AcceptButton = remove;
        CancelButton = cancel;
    }
}

internal sealed class SettingsDialog : Form
{
    private readonly TextBox _game = new();
    private readonly TextBox _server = new();
    public string GamePath => _game.Text.Trim();
    public string ServerPath => _server.Text.Trim();

    public SettingsDialog(AppSettings settings)
    {
        Text = "Настройки VMod";
        ClientSize = new Size(680, 310);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = VTheme.Back;
        ForeColor = VTheme.Text;
        Font = VTheme.Font(10F);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        Controls.Add(new Label { Text = "Пути установки", Font = VTheme.Font(18F, FontStyle.Bold, "Segoe UI Semibold"), ForeColor = VTheme.AmberBright, Location = new Point(25, 20), AutoSize = true });
        AddPathRow("Папка игры", settings.GamePath, 80, _game, InstallTarget.Game);
        AddPathRow("Папка сервера", settings.ServerPath, 157, _server, InstallTarget.Server);
        VButton save = new() { Text = "Сохранить", Kind = VButtonKind.Primary, DialogResult = DialogResult.OK };
        VButton cancel = new() { Text = "Отмена", Kind = VButtonKind.Ghost, DialogResult = DialogResult.Cancel };
        save.SetBounds(475, 247, 175, 42);
        cancel.SetBounds(335, 247, 130, 42);
        Controls.Add(save);
        Controls.Add(cancel);
        AcceptButton = save;
        CancelButton = cancel;
    }

    private void AddPathRow(string caption, string value, int y, TextBox box, InstallTarget target)
    {
        Controls.Add(new Label { Text = caption, Location = new Point(27, y), Size = new Size(180, 23), ForeColor = VTheme.Muted });
        box.Text = value;
        box.SetBounds(27, y + 27, 520, 29);
        box.BackColor = VTheme.BackRaised;
        box.ForeColor = VTheme.Text;
        box.BorderStyle = BorderStyle.FixedSingle;
        VButton browse = new() { Text = "Обзор…", Kind = VButtonKind.Secondary };
        browse.SetBounds(557, y + 25, 93, 33);
        browse.Click += (_, _) =>
        {
            using FolderBrowserDialog dialog = new() { Description = target == InstallTarget.Game ? "Выберите папку Valheim" : "Выберите папку сервера", SelectedPath = Directory.Exists(box.Text) ? box.Text : "" };
            if (dialog.ShowDialog(this) == DialogResult.OK) box.Text = dialog.SelectedPath;
        };
        Controls.Add(box);
        Controls.Add(browse);
    }
}
