using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GitLFSUnlocker;

public partial class MainWindow : Window
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GitLFSUnlocker", "settings.json");

    private ObservableCollection<LfsLock> _locks = new();
    private ICollectionView _locksView = null!;
    private string? _repoPath;
    private string? _filterUser;

    public MainWindow()
    {
        InitializeComponent();
        Icon = CreateLockIcon();
        _locksView = CollectionViewSource.GetDefaultView(_locks);
        _locksView.Filter = FilterLocks;
        LocksDataGrid.ItemsSource = _locksView;
        LoadSettings();
    }

    private static BitmapSource CreateLockIcon()
    {
        // Draw a simple padlock using WPF vector graphics and render to a bitmap
        const int size = 64;
        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            // Shackle (rounded rectangle arc on top)
            var shackle = new Pen(new SolidColorBrush(Color.FromRgb(0, 120, 212)), 6);
            ctx.DrawRoundedRectangle(null, shackle,
                new Rect(18, 6, 28, 26), 14, 14);

            // Body of the lock
            var bodyBrush = new SolidColorBrush(Color.FromRgb(0, 120, 212));
            ctx.DrawRoundedRectangle(bodyBrush, null,
                new Rect(10, 28, 44, 30), 5, 5);

            // Keyhole circle
            ctx.DrawEllipse(Brushes.White, null, new System.Windows.Point(32, 40), 5, 5);

            // Keyhole slot
            ctx.DrawRectangle(Brushes.White, null, new Rect(29, 42, 6, 8));
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private bool FilterLocks(object item) =>
        _filterUser == null || _filterUser == "(All users)" ||
        (item is LfsLock l && l.LockedBy == _filterUser);

    // ── Settings persistence ──────────────────────────────────────────────────

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json);
            if (settings?.RepoPath is string path && Directory.Exists(Path.Combine(path, ".git")))
            {
                _repoPath = path;
                RepoPathTextBox.Text = path;
                RefreshButton.IsEnabled = true;
                LoadLocks();
            }
        }
        catch { /* ignore corrupt settings */ }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var json = JsonSerializer.Serialize(new AppSettings { RepoPath = _repoPath });
            File.WriteAllText(SettingsPath, json);
        }
        catch { /* ignore write errors */ }
    }

    // ── Repository selection ──────────────────────────────────────────────────

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Git Repository Folder",
        };

        if (dialog.ShowDialog() == true)
        {
            var path = dialog.FolderName;
            if (!Directory.Exists(Path.Combine(path, ".git")))
            {
                ShowStatus("⚠️", "The selected folder does not appear to be a Git repository.", isError: true);
                return;
            }
            _repoPath = path;
            RepoPathTextBox.Text = path;
            RefreshButton.IsEnabled = true;
            SaveSettings();
            LoadLocks();
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => LoadLocks();

    // ── Load locks ────────────────────────────────────────────────────────────

    private async void LoadLocks()
    {
        if (_repoPath == null) return;

        SetBusy(true, "Loading locks…");
        _locks.Clear();
        _filterUser = null;
        UpdateSelectionUi();

        try
        {
            var (output, error, exitCode) = await RunGitAsync("lfs locks", _repoPath);

            if (exitCode != 0)
            {
                ShowStatus("❌", $"git lfs locks failed: {error.Trim()}", isError: true);
                SetEmptyState("Could not load locks. Check that git-lfs is installed.");
                return;
            }

            ParseAndPopulateLocks(output);
            PopulateUserFilter();

            if (_locks.Count == 0)
                SetEmptyState("No LFS locks found in this repository.");
            else
                HideEmptyState();

            ShowStatus("✅", $"Loaded {_locks.Count} lock(s).");
        }
        catch (Exception ex)
        {
            ShowStatus("❌", $"Error: {ex.Message}", isError: true);
            SetEmptyState("An error occurred while loading locks.");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ParseAndPopulateLocks(string output)
    {
        // git lfs locks output format:
        // path/to/file.psd    username    ID:123
        // Columns are separated by tab or multiple spaces
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            // Try tab-delimited first
            var parts = trimmed.Split('\t');
            if (parts.Length >= 2)
            {
                var lockId = parts.Length >= 3 ? parts[2].Replace("ID:", "").Trim() : "";
                _locks.Add(new LfsLock
                {
                    FilePath = parts[0].Trim(),
                    LockedBy = parts[1].Trim(),
                    LockId   = lockId
                });
                continue;
            }

            // Fallback: the last token is "ID:NNN", second-to-last is username
            var tokens = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length >= 3)
            {
                var lockId   = tokens[^1].Replace("ID:", "").Trim();
                var lockedBy = tokens[^2].Trim();
                var filePath = string.Join(" ", tokens[..^2]);
                _locks.Add(new LfsLock
                {
                    FilePath = filePath,
                    LockedBy = lockedBy,
                    LockId   = lockId
                });
            }
        }
    }

    private void PopulateUserFilter()
    {
        var users = _locks.Select(l => l.LockedBy).Distinct().OrderBy(u => u).ToList();

        UserFilterComboBox.ItemsSource = new[] { "(All users)" }.Concat(users).ToList();
        UserFilterComboBox.SelectedIndex = 0;
        UserFilterComboBox.IsEnabled = users.Count > 0;
        _filterUser = null;
    }

    private void UserFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UserFilterComboBox.SelectedItem is string selected)
        {
            _filterUser = selected == "(All users)" ? null : selected;
            _locksView?.Refresh();
            UpdateSelectionUi();
        }
    }

    // ── Unlock ────────────────────────────────────────────────────────────────

    private async void UnlockButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = _locksView.Cast<LfsLock>().Where(l => l.IsSelected).ToList();
        if (selected.Count == 0) return;

        var result = MessageBox.Show(
            $"Unlock {selected.Count} file(s)?",
            "Confirm Unlock",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.OK) return;

        SetBusy(true, $"Unlocking {selected.Count} file(s)…");
        UnlockButton.IsEnabled = false;

        int succeeded = 0, failed = 0;
        var failedFiles = new List<string>();

        foreach (var lfsLock in selected)
        {
            // Prefer unlocking by ID when available, fall back to path
            var args = !string.IsNullOrEmpty(lfsLock.LockId)
                ? $"lfs unlock --id={lfsLock.LockId}"
                : $"lfs unlock \"{lfsLock.FilePath}\"";

            var (_, error, exitCode) = await RunGitAsync(args, _repoPath!);
            if (exitCode == 0)
                succeeded++;
            else
            {
                failed++;
                failedFiles.Add($"{lfsLock.FilePath}: {error.Trim()}");
            }
        }

        SetBusy(false);

        if (failed == 0)
        {
            ShowStatus("✅", $"Successfully unlocked {succeeded} file(s).");
        }
        else
        {
            var msg = $"Unlocked {succeeded} file(s). {failed} failed:\n\n" + string.Join("\n", failedFiles);
            MessageBox.Show(msg, "Unlock Results", MessageBoxButton.OK, MessageBoxImage.Warning);
            ShowStatus("⚠️", $"{succeeded} unlocked, {failed} failed.", isError: true);
        }

        LoadLocks();
    }

    // ── Selection helpers ─────────────────────────────────────────────────────

    private void SelectAllButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var l in _locksView.Cast<LfsLock>()) l.IsSelected = true;
        UpdateSelectionUi();
    }

    private void DeselectAllButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var l in _locksView.Cast<LfsLock>()) l.IsSelected = false;
        UpdateSelectionUi();
    }

    private void RowCheckBox_Click(object sender, RoutedEventArgs e) => UpdateSelectionUi();

    private void HeaderCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb)
        {
            bool check = cb.IsChecked == true;
            foreach (var l in _locksView.Cast<LfsLock>()) l.IsSelected = check;
            UpdateSelectionUi();
        }
    }

    private void UpdateSelectionUi()
    {
        var visible = _locksView.Cast<LfsLock>().ToList();
        int selectedCount = visible.Count(l => l.IsSelected);
        int total = visible.Count;
        int totalAll = _locks.Count;

        SelectedCountLabel.Text = selectedCount > 0
            ? $"{selectedCount} of {total} file(s) selected"
            : "";

        UnlockButton.IsEnabled      = selectedCount > 0;
        SelectAllButton.IsEnabled   = total > 0;
        DeselectAllButton.IsEnabled = total > 0;

        LockCountLabel.Text = totalAll > 0
            ? (total < totalAll ? $"Showing {total} of {totalAll} lock(s)" : $"{totalAll} lock(s) found")
            : "No locks loaded";

        // Update header checkbox state
        UpdateHeaderCheckbox(selectedCount, total);
    }

    private void UpdateHeaderCheckbox(int selectedCount, int total)
    {
        // Walk the visual tree to find the header checkbox
        var header = FindVisualChildren<CheckBox>(LocksDataGrid)
            .FirstOrDefault(c => c.Tag?.ToString() == "HeaderCheckBox");

        if (header == null) return;

        header.Click -= HeaderCheckBox_Click; // avoid re-entrant event
        if (selectedCount == 0)
            header.IsChecked = false;
        else if (selectedCount == total)
            header.IsChecked = true;
        else
            header.IsChecked = null; // indeterminate
        header.Click += HeaderCheckBox_Click;
    }

    // ── UI helpers ────────────────────────────────────────────────────────────

    private void SetBusy(bool busy, string? message = null)
    {
        RefreshButton.IsEnabled     = !busy && _repoPath != null;
        SelectAllButton.IsEnabled   = !busy && _locks.Count > 0;
        DeselectAllButton.IsEnabled = !busy && _locks.Count > 0;

        if (busy && message != null)
            ShowStatus("⏳", message);
    }

    private void SetEmptyState(string message)
    {
        EmptyStatePanel.Visibility  = Visibility.Visible;
        EmptyStateMessage.Text      = message;
    }

    private void HideEmptyState() => EmptyStatePanel.Visibility = Visibility.Collapsed;

    private void ShowStatus(string icon, string message, bool isError = false)
    {
        StatusBorder.Visibility = Visibility.Visible;
        StatusIcon.Text         = icon;
        StatusLabel.Text        = message;

        StatusBorder.Background = isError
            ? new SolidColorBrush(Color.FromRgb(0xFD, 0xED, 0xED))
            : new SolidColorBrush(Color.FromRgb(0xEB, 0xF3, 0xFB));
        StatusLabel.Foreground = isError
            ? new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B))
            : new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4));
    }

    // ── Git process helper ────────────────────────────────────────────────────

    private static Task<(string output, string error, int exitCode)> RunGitAsync(
        string arguments, string workingDirectory)
    {
        return Task.Run(() =>
        {
            var psi = new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory       = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Could not start git process.");

            var output = process.StandardOutput.ReadToEnd();
            var error  = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (output, error, process.ExitCode);
        });
    }

    // ── Visual tree helper ────────────────────────────────────────────────────

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) yield return t;
            foreach (var c in FindVisualChildren<T>(child)) yield return c;
        }
    }
}

// ── Settings model ─────────────────────────────────────────────────────────────

public class AppSettings
{
    public string? RepoPath { get; set; }
}

// ── Lock model ─────────────────────────────────────────────────────────────────

public class LfsLock : INotifyPropertyChanged
{
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
    }

    public string FilePath { get; set; } = "";
    public string LockedBy { get; set; } = "";
    public string LockId   { get; set; } = "";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
