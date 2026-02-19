using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;
using SynkrFox.Core;

namespace SynkrFox
{
    public partial class MainWindow : Window
    {
        private SyncEngine _syncEngine = null!;
        private AppSettings _settings = new();
        private bool _syncing = false;
        private WinForms.NotifyIcon? _notifyIcon;
        private System.Drawing.Icon? _normalIcon;
        private System.Drawing.Icon? _syncIcon;
        private DispatcherTimer _statsTimer = new();

        public MainWindow()
        {
            InitializeComponent();
            LoadSettings();
            LoadIcon();
            InitializeTrayIcon();
            _syncEngine = new SyncEngine();
            _syncEngine.LogMessage += OnLogMessage;

            // Setup statistics refresh timer (faster for responsive flashing)
            _statsTimer.Interval = TimeSpan.FromMilliseconds(250);
            _statsTimer.Tick += UpdateStatistics;
            _statsTimer.Start();

            // Subscribe to real-time activity events
            _syncEngine.Statistics.ActivityDetected += OnActivityDetected;

            // Credits and version info (moved to XAML for consistency, but ensuring it's correct here if needed)
            if (TxtCredits != null) TxtCredits.Text = "v1.0.0.0 by codefoxsoft";
            
            // Auto-start sync if folders are configured
            AutoStartSync();
        }

        private void AutoStartSync()
        {
            // Only auto-start if we have both backup path and source folders
            if (!string.IsNullOrEmpty(_settings.BackupPath) && _settings.SourceFolders.Count > 0)
            {
                _syncEngine.SetBackupPath(_settings.BackupPath);
                
                foreach (var folder in _settings.SourceFolders)
                {
                    _syncEngine.AddSourceFolder(folder);
                }
                
                _syncing = true;
                if (BtnToggleSync != null)
                {
                    BtnToggleSync.Content = "Disable Live Sync";
                    BtnToggleSync.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF5555"));
                }
                if (TxtSyncStatus != null) TxtSyncStatus.Text = "● Live Monitoring";
            }
        }

        private void LoadIcon()
        {
            try
            {
                // Load the .ico file directly for the window icon
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
                if (File.Exists(iconPath))
                {
                    // For WPF Window Icon, we can use BitmapFrame.Create
                    var iconUri = new Uri(iconPath, UriKind.Absolute);
                    this.Icon = BitmapFrame.Create(iconUri);
                }
            }
            catch
            {
                // Silently fail - icon is cosmetic
            }
        }

        private void InitializeTrayIcon()
        {
            _notifyIcon = new WinForms.NotifyIcon();
            try
            {
                string appIconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
                string syncIconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "appsync.ico");
                
                if (File.Exists(appIconPath))
                {
                    _normalIcon = new System.Drawing.Icon(appIconPath);
                }
                else
                {
                    _normalIcon = System.Drawing.SystemIcons.Application;
                }

                if (File.Exists(syncIconPath))
                {
                    _syncIcon = new System.Drawing.Icon(syncIconPath);
                }
                else
                {
                    _syncIcon = System.Drawing.SystemIcons.Shield; 
                }

                _notifyIcon.Icon = _normalIcon;
                
                _notifyIcon.Text = "SynkrFox";
                _notifyIcon.Visible = true;

                var contextMenu = new WinForms.ContextMenuStrip();
                contextMenu.Items.Add("Open SynkrFox", null, (s, e) => ShowWindow());
                contextMenu.Items.Add("Exit", null, (s, e) =>
                {
                    _notifyIcon.Visible = false;
                    _statsTimer.Stop();
                    System.Windows.Application.Current.Shutdown();
                });
                _notifyIcon.ContextMenuStrip = contextMenu;

                _notifyIcon.DoubleClick += (s, e) => ShowWindow();
            }
            catch (Exception ex)
            {
                // Fallback to system icon if custom icon fails
                if (_notifyIcon != null)
                {
                    _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
                    _notifyIcon.Text = "SynkrFox";
                    _notifyIcon.Visible = true;
                }
                System.Windows.MessageBox.Show($"Error initializing tray icon: {ex.Message}");
            }
        }

        private void ShowWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void LoadSettings()
        {
            _settings = ConfigManager.Load() ?? new AppSettings();
            if (TxtBackupPath != null) TxtBackupPath.Text = _settings.BackupPath;
            if (ChkStartWithPC != null) ChkStartWithPC.IsChecked = _settings.StartWithPC;
            
            // Sync registry state with settings on load
            SetAutoStart(_settings.StartWithPC);
            
            RefreshSourceList();
        }

        private void RefreshSourceList()
        {
            if (ListSourceFolders != null)
            {
                ListSourceFolders.ItemsSource = null;
                ListSourceFolders.ItemsSource = _settings.SourceFolders;
            }
        }

        private void SaveSettings()
        {
            if (TxtBackupPath != null)
            {
                _settings.BackupPath = TxtBackupPath.Text;
                ConfigManager.Save(_settings);
            }
        }

        private DateTime _lastActivityUpdate = DateTime.MinValue;

        private void OnActivityDetected()
        {
            // Rate limit UI updates to prevent spamming the dispatcher (max 20fps equivalent)
            var now = DateTime.Now;
            if ((now - _lastActivityUpdate).TotalMilliseconds < 50) return;
            _lastActivityUpdate = now;

            // Force immediate update on UI thread
            System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (_syncing)
                {
                    // Force the "Lit" state immediately
                    if (_notifyIcon != null && _syncIcon != null)
                    {
                        _notifyIcon.Icon = _syncIcon;
                        _isIconFlashState = true;
                    }
                    
                    // Also update text if needed (optional, heavier)
                    if (TxtSyncStatus != null) TxtSyncStatus.Text = "● Syncing...";
                    if (SyncProgressBar != null && SyncProgressBar.Visibility != Visibility.Visible)
                    {
                        SyncProgressBar.Visibility = Visibility.Visible;
                        SyncProgressBar.IsIndeterminate = true;
                    }
                }
            });
        }

        private void UpdateStatistics(object? sender, EventArgs e)
        {
            if (TxtStats == null || TxtLastSync == null) return;
            
            TxtStats.Text = _syncEngine.Statistics.GetFormattedStats();
            TxtLastSync.Text = "Last sync: " + _syncEngine.Statistics.GetLastSyncFormatted();
            
            // Update sync status indicator
            if (_syncing && TxtSyncStatus != null)
            {
                // Is active check
                bool isActive = _syncEngine.Statistics.IsSyncing;

                if (isActive)
                {
                    TxtSyncStatus.Text = "● Syncing...";
                    if (SyncProgressBar != null)
                    {
                        SyncProgressBar.Visibility = Visibility.Visible;
                        SyncProgressBar.IsIndeterminate = true;
                    }
                    FlashTrayIcon(true);
                }
                else
                {
                    TxtSyncStatus.Text = "● Live Monitoring";
                    if (SyncProgressBar != null)
                    {
                        SyncProgressBar.Visibility = Visibility.Collapsed;
                        SyncProgressBar.IsIndeterminate = false;
                    }
                    FlashTrayIcon(false);
                }
            }
        }

        private bool _isIconFlashState = false;
        private DateTime _lastFlashTime = DateTime.MinValue;

        private void FlashTrayIcon(bool flashing)
        {
            if (_notifyIcon == null || _normalIcon == null || _syncIcon == null) return;

            if (!flashing)
            {
                _notifyIcon.Icon = _normalIcon;
                _isIconFlashState = false;
                return;
            }
            
            // If actively moving data, keep it LIT
            if (_syncEngine.Statistics.IsMovingData)
            {
                 // Ensure it's syncing icon
                 if (!_isIconFlashState)
                 {
                    _notifyIcon.Icon = _syncIcon;
                    _isIconFlashState = true;
                 }
                 return;
            }

            // Otherwise, flash it (strobing effect for "recent activity" window)
            // Flash every ~250ms
            if ((DateTime.Now - _lastFlashTime).TotalMilliseconds > 250)
            {
                _isIconFlashState = !_isIconFlashState;
                _lastFlashTime = DateTime.Now;
                _notifyIcon.Icon = _isIconFlashState ? _syncIcon : _normalIcon;
            }
        }



        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Minimize to tray instead of closing
            e.Cancel = true;
            this.Hide();
            if (_notifyIcon != null)
            {
                _notifyIcon.ShowBalloonTip(3000, "SynkrFox", "Application minimized to tray.", WinForms.ToolTipIcon.Info);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
            if (_notifyIcon != null)
            {
                _notifyIcon.ShowBalloonTip(3000, "SynkrFox", "Application minimized to tray.", WinForms.ToolTipIcon.Info);
            }
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
        }

        private void BrowseBackup_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog();
            dialog.Title = "Select Backup Destination";
            if (dialog.ShowDialog() == true)
            {
                if (TxtBackupPath != null) TxtBackupPath.Text = dialog.FolderName;
                SaveSettings();
            }
        }

        private void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog();
            dialog.Title = "Select Source Folder to Sync";
            if (dialog.ShowDialog() == true)
            {
                if (!_settings.SourceFolders.Contains(dialog.FolderName))
                {
                    _settings.SourceFolders.Add(dialog.FolderName);
                    RefreshSourceList();
                    SaveSettings();

                    if (_syncing)
                    {
                        _syncEngine.AddSourceFolder(dialog.FolderName);
                    }
                }
            }
        }

        private void RemoveFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string path)
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

        private void ToggleSync_Click(object sender, RoutedEventArgs e)
        {
            if (_syncing)
            {
                // Disable live sync
                foreach (var folder in _settings.SourceFolders)
                {
                    _syncEngine.RemoveSourceFolder(folder);
                }

                if (BtnToggleSync != null)
                {
                    BtnToggleSync.Content = "Enable Live Sync";
                    BtnToggleSync.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#007ACC"));
                }
                if (TxtSyncStatus != null) TxtSyncStatus.Text = "Stopped";
                if (SyncProgressBar != null) SyncProgressBar.Visibility = Visibility.Collapsed;
                _syncing = false;
            }
            else
            {
                // Enable live sync
                if (TxtBackupPath != null && string.IsNullOrEmpty(TxtBackupPath.Text))
                {
                    System.Windows.MessageBox.Show("Please select a backup destination first.");
                    return;
                }

                if (TxtBackupPath != null) _syncEngine.SetBackupPath(TxtBackupPath.Text);

                foreach (var folder in _settings.SourceFolders)
                {
                    _syncEngine.AddSourceFolder(folder);
                }

                if (BtnToggleSync != null)
                {
                    BtnToggleSync.Content = "Disable Live Sync";
                    BtnToggleSync.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF5555"));
                }
                if (TxtSyncStatus != null) TxtSyncStatus.Text = "● Live Monitoring";
                _syncing = true;
            }
        }

        private void ChkStartWithPC_Click(object sender, RoutedEventArgs e)
        {
            bool isChecked = ChkStartWithPC?.IsChecked ?? false;
            _settings.StartWithPC = isChecked;
            SetAutoStart(isChecked);
            SaveSettings();
        }

        private void SetAutoStart(bool enable)
        {
            try
            {
                string path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
                RegistryKey? key = Registry.CurrentUser.OpenSubKey(path, true);
                if (key != null)
                {
                    if (enable)
                    {
                        string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
                        if (!string.IsNullOrEmpty(exePath))
                        {
                            key.SetValue("SynkrFox", $"\"{exePath}\"");
                        }
                    }
                    else
                    {
                        key.DeleteValue("SynkrFox", false);
                    }
                }
            }
            catch (Exception ex)
            {
                // Silently log or handle? We'll log to our internal log if possible.
                OnLogMessage(new LogEntry($"Error setting auto-start: {ex.Message}", LogLevel.Error));
            }
        }

        private void ForceSync_Click(object sender, RoutedEventArgs e)
        {
            if (!_syncing)
            {
                System.Windows.MessageBox.Show("Live sync must be enabled first. Click 'Enable Live Sync' to start monitoring.", "Sync Not Active", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (TxtSyncStatus != null) TxtSyncStatus.Text = "⟳ Full Rescan...";
            
            // Force a full rescan of all source folders
            _syncEngine.ForceFullRescan();
        }

        private void OnLogMessage(LogEntry entry)
        {
            Dispatcher.Invoke(() =>
            {
                // Color code based on log level
                string icon = entry.Level switch
                {
                    LogLevel.Success => "✓",
                    LogLevel.Warning => "⚠",
                    LogLevel.Error => "✗",
                    _ => "•"
                };

                string timestamp = entry.Timestamp.ToString("HH:mm:ss");
                string formattedMessage = $"[{timestamp}] {icon} {entry.Message}\n";

                if (TxtLog != null)
                {
                    TxtLog.AppendText(formattedMessage);
                    TxtLog.ScrollToEnd();
                }
            });
        }
    }
}