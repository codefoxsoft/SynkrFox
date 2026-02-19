using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace SynkrFox.App
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow();
                
                // Keep the app running even if window is closed (minimize to tray)
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            }

            base.OnFrameworkInitializationCompleted();
        }

        public void TrayIcon_Click(object? sender, EventArgs e)
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow?.Show();
                desktop.MainWindow?.Activate();
                if (desktop.MainWindow != null)
                {
                    desktop.MainWindow.WindowState = WindowState.Normal;
                }
            }
        }

        public void Exit_Click(object? sender, EventArgs e)
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }
    }
}