using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using System.Diagnostics;
using System.Collections.Generic;

namespace SynkrInstaller
{
    public partial class MainWindow : Window
    {
        private enum InstallMode { Install, Uninstall }
        private InstallMode _mode = InstallMode.Install;
        private string _installPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SynkrFox");
        private string _appPath = "";

        public MainWindow()
        {
            try
            {
                InitializeComponent();
                _appPath = AppDomain.CurrentDomain.BaseDirectory;
                
                SetIcon(); // Call local method safely

                // Check for uninstall flag
                if (Environment.GetCommandLineArgs().Contains("--uninstall"))
                {
                    _mode = InstallMode.Uninstall;
                    ShowUninstall();
                }
            }
            catch (Exception ex)
            {
                string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "installer_debug.txt");
                File.AppendAllText(logPath, $"Startup Error: {ex.Message}\n{ex.StackTrace}\n");
                MessageBox.Show($"Installer Error: {ex.Message}", "Setup Failed");
                Close();
            }
        }

        private void SetIcon()
        {
            try
            {
                var iconUri = new Uri("pack://application:,,,/Resources/app.ico", UriKind.RelativeOrAbsolute);
                this.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(iconUri);
            }
            catch (Exception ex)
            {
                // Silently fail icon if it crashes, better than crashing the whole app
                System.Diagnostics.Debug.WriteLine($"Icon load failed: {ex.Message}");
            }
        }

        private void Header_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void ShowUninstall()
        {
            ScreenWelcome.Visibility = Visibility.Collapsed;
            ScreenUninstall.Visibility = Visibility.Visible;
            BtnNext.Content = "Uninstall";
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            if (_mode == InstallMode.Install)
            {
                if (ScreenWelcome.Visibility == Visibility.Visible)
                {
                    StartInstallation();
                }
            }
            else
            {
                if (ScreenUninstall.Visibility == Visibility.Visible)
                {
                    StartUninstallation();
                }
            }
        }

        private async void StartInstallation()
        {
            ScreenWelcome.Visibility = Visibility.Collapsed;
            ScreenProgress.Visibility = Visibility.Visible;
            BtnNext.IsEnabled = false;

            try
            {
                // 1. Prepare directory
                SetStatus("Creating directory...");
                if (!Directory.Exists(_installPath)) Directory.CreateDirectory(_installPath);
                UpdateProgress(10);

                // 2. Extract files from embedded resource
                SetStatus("Extracting files...");
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using (Stream? stream = assembly.GetManifestResourceStream("app_files.zip"))
                {
                    if (stream == null) throw new Exception("Embedded resource 'app_files.zip' not found.");
                    
                    string tempZip = Path.Combine(Path.GetTempPath(), "SynkrFox_Setup.zip");
                    using (FileStream fs = new FileStream(tempZip, FileMode.Create))
                    {
                        await stream.CopyToAsync(fs);
                    }
                    
                    UpdateProgress(40);
                    SetStatus("Installing files...");
                    
                    // Extract to Program Files
                    System.IO.Compression.ZipFile.ExtractToDirectory(tempZip, _installPath, true);
                    
                    try { File.Delete(tempZip); } catch { }
                }
                UpdateProgress(80);

                // 3. Shortcuts
                SetStatus("Creating shortcuts...");
                string exePath = Path.Combine(_installPath, "SynkrFox.exe");
                
                if (ChkDesktopIcon.IsChecked == true)
                    CreateShortcut(exePath, "SynkrFox", Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
                
                if (ChkStartMenuIcon.IsChecked == true)
                {
                    string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "SynkrFox");
                    if (!Directory.Exists(startMenu)) Directory.CreateDirectory(startMenu);
                    CreateShortcut(exePath, "SynkrFox", startMenu);
                }
                UpdateProgress(90);

                // 4. Registry Registration (Uninstaller)
                SetStatus("Registering uninstaller...");
                RegisterUninstaller();
                UpdateProgress(100);

                await Task.Delay(500);
                ShowFinished();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Installation failed: {ex.Message}");
                Close();
            }
        }

        private async void StartUninstallation()
        {
            ScreenUninstall.Visibility = Visibility.Collapsed;
            ScreenProgress.Visibility = Visibility.Visible;
            BtnNext.IsEnabled = false;

            try
            {
                SetStatus("Removing shortcuts...");
                // Remove shortcuts
                string desktopShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "SynkrFox.lnk");
                if (File.Exists(desktopShortcut)) File.Delete(desktopShortcut);
                
                string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "SynkrFox");
                if (Directory.Exists(startMenu)) Directory.Delete(startMenu, true);
                UpdateProgress(30);

                // We can't delete the app itself easily while running from it (if uninstaller is the app)
                // But if the installer is separate, we can.
                SetStatus("Stopping SynkrFox...");
                foreach (var process in Process.GetProcessesByName("SynkrFox"))
                {
                    process.Kill();
                }
                UpdateProgress(50);

                SetStatus("Removing files...");
                if (Directory.Exists(_installPath))
                {
                    // Delay slightly to ensure file locks are released
                    await Task.Delay(1000);
                    try { Directory.Delete(_installPath, true); } catch { }
                }
                UpdateProgress(80);

                SetStatus("Cleaning registry...");
                UnregisterUninstaller();
                UpdateProgress(100);

                MessageBox.Show("SynkrFox has been successfully removed.", "Uninstalled");
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Uninstallation failed: {ex.Message}");
                Close();
            }
        }

        private void ShowFinished()
        {
            ScreenProgress.Visibility = Visibility.Collapsed;
            ScreenFinished.Visibility = Visibility.Visible;
            BtnNext.Visibility = Visibility.Collapsed;
            BtnFinish.Visibility = Visibility.Visible;
        }

        private void SetStatus(string msg) => TxtProgressStatus.Text = msg;

        private void UpdateProgress(int val)
        {
            InstallProgressBar.Value = val;
            TxtPercent.Text = $"{val}%";
        }

        private void Finish_Click(object sender, RoutedEventArgs e)
        {
            if (ChkLaunchNow.IsChecked == true)
            {
                try { Process.Start(Path.Combine(_installPath, "SynkrFox.exe")); } catch { }
            }
            Close();
        }

        private void Back_Click(object sender, RoutedEventArgs e) { }

        private void CreateShortcut(string targetPath, string name, string folder)
        {
            try
            {
                // PowerShell method for shortcut creation to avoid external dependencies
                string script = $"$s=(New-Object -COM WScript.Shell).CreateShortcut('{Path.Combine(folder, name + ".lnk")}');$s.TargetPath='{targetPath}';$s.IconLocation='{targetPath},0';$s.Save()";
                Process.Start(new ProcessStartInfo("powershell", $"-Command \"{script}\"") { CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
            }
            catch { }
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private void RegisterUninstaller()
        {
            try
            {
                string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\SynkrFox";
                using (RegistryKey key = Registry.LocalMachine.CreateSubKey(keyPath))
                {
                    string installerPath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                    key.SetValue("DisplayName", "SynkrFox");
                    key.SetValue("UninstallString", $"\"{installerPath}\" --uninstall");
                    key.SetValue("DisplayIcon", Path.Combine(_installPath, "SynkrFox.exe"));
                    key.SetValue("DisplayVersion", "1.0.0.0");
                    key.SetValue("Publisher", "codefoxsoft");
                    key.SetValue("InstallLocation", _installPath);
                }
            }
            catch { }
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private void UnregisterUninstaller()
        {
            try
            {
                Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\SynkrFox", false);
            }
            catch { }
        }
    }
}