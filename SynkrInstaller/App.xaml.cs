using System.Configuration;
using System.Data;
using System.Windows;
using System.IO;

namespace SynkrInstaller;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var win = new MainWindow();
            win.Show();
        }
        catch (Exception ex)
        {
            string error = $"Message: {ex.Message}\n\nStackTrace: {ex.StackTrace}";
            if (ex.InnerException != null)
            {
                error += $"\n\nInner Message: {ex.InnerException.Message}\n\nInner StackTrace: {ex.InnerException.StackTrace}";
            }
            MessageBox.Show(error, "Startup Error Details");
            try { File.WriteAllText("installer_crash_details.txt", error); } catch { }
            Shutdown();
        }
    }
}

