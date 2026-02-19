using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace SynkrFox;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            LogFatal((Exception)e.ExceptionObject, "AppDomain.UnhandledException");

        DispatcherUnhandledException += (s, e) =>
        {
            LogFatal(e.Exception, "DispatcherUnhandledException");
            e.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            LogFatal(e.Exception, "TaskScheduler.UnobservedTaskException");
            e.SetObserved();
        };
    }

    private void LogFatal(Exception ex, string source)
    {
        // Build a full exception chain
        string fullMessage = "";
        Exception? current = ex;
        int depth = 0;
        while (current != null)
        {
            fullMessage += $"[Depth {depth}] {current.GetType().Name}: {current.Message}\nStack: {current.StackTrace}\n\n";
            current = current.InnerException;
            depth++;
        }

        try
        {
            string logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup_error.log");
            string message = $"[{DateTime.Now}] FATAL ERROR ({source}):\n{fullMessage}\n";
            File.AppendAllText(logFile, message);
        }
        catch { }

        try
        {
            System.Windows.MessageBox.Show(
                $"SynkrFox Error:\n{fullMessage}",
                "SynkrFox Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch { }
    }
}
