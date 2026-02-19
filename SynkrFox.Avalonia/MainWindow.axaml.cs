using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.Platform;
using SynkrFox.Core;

namespace SynkrFox.App
{
    public partial class MainWindow : Window
    {
        private SyncEngine _syncEngine = null!;
        private AppSettings _settings = new();
        private bool _syncing = false;
        private DispatcherTimer _statsTimer = new();
        private System.Collections.Concurrent.ConcurrentQueue<string> _logQueue = new();

        private WindowIcon? _defaultIcon;
        private WindowIcon? _syncIcon;
        
        public MainWindow()
        {
            InitializeComponent();
            _syncEngine = new SyncEngine();
            _syncEngine.LogMessage += OnLogMessage;
            
            LoadSettings();

            // Setup statistics refresh timer
            _statsTimer.Interval = TimeSpan.FromMilliseconds(250);
            _statsTimer.Tick += UpdateStatistics;
            _statsTimer.Start();

            // Subscribe to real-time activity events
            _syncEngine.Statistics.ActivityDetected += OnActivityDetected;

            AutoStartSync();
            LoadTrayIcons();
        }

        private void LoadTrayIcons()
        {
            try
            {
                _defaultIcon = new WindowIcon(AssetLoader.Open(new Uri("avares://SynkrFox/Assets/app.ico")));
                _syncIcon = new WindowIcon(AssetLoader.Open(new Uri("avares://SynkrFox/Assets/app_sync.ico")));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading tray icons: {ex.Message}");
            }
        }

        private void AutoStartSync()
        {
            if (!string.IsNullOrEmpty(_settings.BackupPath) && _settings.SourceFolders.Count > 0)
            {
                _syncEngine.SetBackupPath(_settings.BackupPath);
                foreach (var folder in _settings.SourceFolders)
                {
                    _syncEngine.AddSourceFolder(folder);
                }
                
                _syncing = true;
                BtnToggleSync.Content = "Disable Live Sync";
                BtnToggleSync.Background = Brush.Parse("#FF5555");
                TxtSyncStatus.Text = "● Live Monitoring";
            }
        }

        private void LoadSettings()
        {
            _settings = ConfigManager.Load() ?? new AppSettings();
            TxtBackupPath.Text = _settings.BackupPath;
            ChkStartWithPC.IsChecked = _settings.StartWithPC;
            
            // Set auto-start (Platform specific)
            SetAutoStart(_settings.StartWithPC);
            RefreshSourceList();
        }

        private void RefreshSourceList()
        {
            ListSourceFolders.ItemsSource = null;
            ListSourceFolders.ItemsSource = _settings.SourceFolders.ToList();
        }

        private void SaveSettings()
        {
            _settings.BackupPath = TxtBackupPath.Text ?? "";
            ConfigManager.Save(_settings);
        }

        private DateTime _lastActivityUpdate = DateTime.MinValue;

        private void OnActivityDetected()
        {
            var now = DateTime.Now;
            if ((now - _lastActivityUpdate).TotalMilliseconds < 50) return;
            _lastActivityUpdate = now;

            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_syncing)
                {
                    TxtSyncStatus.Text = "● Syncing...";
                    if (!SyncProgressBar.IsVisible)
                    {
                        SyncProgressBar.IsVisible = true;
                        SyncProgressBar.IsIndeterminate = true;
                    }
                }
            });
        }

        private void UpdateStatistics(object? sender, EventArgs e)
        {
            TxtStats.Text = _syncEngine.Statistics.GetFormattedStats();
            TxtLastSync.Text = "Last sync: " + _syncEngine.Statistics.GetLastSyncFormatted();
            
            if (_syncing)
            {
                bool isActive = _syncEngine.Statistics.IsSyncing;
                if (isActive)
                {
                    TxtSyncStatus.Text = "● Syncing...";
                    SyncProgressBar.IsVisible = true;
                    SyncProgressBar.IsIndeterminate = true;
                }
                else
                {
                    TxtSyncStatus.Text = "● Live Monitoring";
                    SyncProgressBar.IsVisible = false;
                    SyncProgressBar.IsIndeterminate = false;
                }
            }
            
            UpdateTrayIcon(_syncEngine?.Statistics?.IsSyncing == true && _syncing);
            ProcessLogQueue();
        }

        private void ProcessLogQueue()
        {
            if (_logQueue.IsEmpty) return;
            
            var sb = new System.Text.StringBuilder();
            int count = 0;
            while (_logQueue.TryDequeue(out var msg) && count < 50) // Limit batch size
            {
                sb.Append(msg);
                count++;
            }
            
            TxtLog.Text += sb.ToString();
            if (TxtLog.Text.Length > 20000) // Prevent memory bloat
            {
                TxtLog.Text = TxtLog.Text.Substring(TxtLog.Text.Length - 10000);
            }
            TxtLog.CaretIndex = TxtLog.Text.Length;
        }

        private void UpdateTrayIcon(bool isSyncing)
        {
            try
            {
                if (Application.Current == null) return;
                var trayIcons = TrayIcon.GetIcons(Application.Current);
                var trayIcon = trayIcons?.FirstOrDefault();
                
                if (trayIcon != null && _defaultIcon != null && _syncIcon != null)
                {
                    trayIcon.Icon = isSyncing ? _syncIcon : _defaultIcon;
                }
            }
            catch { }
        }

        public void Close_Click(object? sender, RoutedEventArgs e) => Close();
        public void Minimize_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        public void TitleBar_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e) => BeginMoveDrag(e);

        private async void BrowseBackup_Click(object? sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions()
            {
                Title = "Select Backup Destination",
                AllowMultiple = false
            });

            if (folders.Any())
            {
                TxtBackupPath.Text = folders[0].Path.LocalPath;
                SaveSettings();
            }
        }

        private async void AddFolder_Click(object? sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions()
            {
                Title = "Select Source Folder to Sync",
                AllowMultiple = false
            });

            if (folders.Any())
            {
                string path = folders[0].Path.LocalPath;
                if (!_settings.SourceFolders.Contains(path))
                {
                    _settings.SourceFolders.Add(path);
                    RefreshSourceList();
                    SaveSettings();

                    if (_syncing)
                    {
                        _syncEngine.AddSourceFolder(path);
                    }
                }
            }
        }

        private void RemoveFolder_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string path)
            {
                _settings.SourceFolders.Remove(path);
                RefreshSourceList();
                SaveSettings();

                if (_syncing)
                {
                    _syncEngine.RemoveSourceFolder(path);
                }
            }
        }

        private void ToggleSync_Click(object? sender, RoutedEventArgs e)
        {
            if (_syncing)
            {
                foreach (var folder in _settings.SourceFolders) _syncEngine.RemoveSourceFolder(folder);
                BtnToggleSync.Content = "Enable Live Sync";
                BtnToggleSync.Background = Brush.Parse("#007ACC");
                TxtSyncStatus.Text = "Stopped";
                SyncProgressBar.IsVisible = false;
                _syncing = false;
            }
            else
            {
                if (string.IsNullOrEmpty(TxtBackupPath.Text)) return;
                _syncEngine.SetBackupPath(TxtBackupPath.Text);
                foreach (var folder in _settings.SourceFolders) _syncEngine.AddSourceFolder(folder);
                BtnToggleSync.Content = "Disable Live Sync";
                BtnToggleSync.Background = Brush.Parse("#FF5555");
                TxtSyncStatus.Text = "● Live Monitoring";
                _syncing = true;
            }
        }

        private void ChkStartWithPC_Click(object? sender, RoutedEventArgs e)
        {
            bool isChecked = ChkStartWithPC.IsChecked ?? false;
            _settings.StartWithPC = isChecked;
            SetAutoStart(isChecked);
            SaveSettings();
        }

        private void SetAutoStart(bool enable)
        {
            // Platform specific logic
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                try
                {
                    using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                    if (key != null)
                    {
                        if (enable) key.SetValue("SynkrFox", $"\"{System.Environment.ProcessPath}\"");
                        else key.DeleteValue("SynkrFox", false);
                    }
                } catch { }
            }
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
            {
                // Linux autostart logic (.desktop file)
                try
                {
                    string autostartDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "autostart");
                    if (!Directory.Exists(autostartDir)) Directory.CreateDirectory(autostartDir);
                    string desktopPath = Path.Combine(autostartDir, "synkrfox.desktop");
                    
                    if (enable)
                    {
                        string content = $@" [Desktop Entry]
Type=Application
Exec={Environment.ProcessPath}
Hidden=false
NoDisplay=false
X-GNOME-Autostart-enabled=true
Name=SynkrFox
Comment=Bi-directional sync tool";
                        File.WriteAllText(desktopPath, content);
                    }
                    else if (File.Exists(desktopPath)) File.Delete(desktopPath);
                } catch { }
            }
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
            {
                // macOS LaunchAgent logic (.plist)
                try
                {
                    string libraryDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents");
                    if (!Directory.Exists(libraryDir)) Directory.CreateDirectory(libraryDir);
                    string plistPath = Path.Combine(libraryDir, "com.codefoxsoft.synkrfox.plist");

                    if (enable)
                    {
                        string content = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE plist PUBLIC ""-//Apple//DTD PLIST 1.0//EN"" ""http://www.apple.com/DTDs/PropertyList-1.0.dtd"">
<plist version=""1.0"">
<dict>
    <key>Label</key>
    <string>com.codefoxsoft.synkrfox</string>
    <key>ProgramArguments</key>
    <array>
        <string>{Environment.ProcessPath}</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
</dict>
</plist>";
                        File.WriteAllText(plistPath, content);
                    }
                    else if (File.Exists(plistPath)) File.Delete(plistPath);
                } catch { }
            }
        }

        private void ForceSync_Click(object? sender, RoutedEventArgs e)
        {
            if (!_syncing) return;
            TxtSyncStatus.Text = "⟳ Full Rescan...";
            _syncEngine.ForceFullRescan();
        }

        private void OnLogMessage(LogEntry entry)
        {
            string icon = entry.Level switch
            {
                LogLevel.Success => "✓",
                LogLevel.Warning => "⚠",
                LogLevel.Error => "✗",
                _ => "•"
            };
            string formattedMessage = $"[{entry.Timestamp:HH:mm:ss}] {icon} {entry.Message}\n";
            _logQueue.Enqueue(formattedMessage);
        }
    }
}