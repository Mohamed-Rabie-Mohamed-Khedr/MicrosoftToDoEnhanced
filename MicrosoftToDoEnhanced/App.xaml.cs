using System.Windows;
using Core.Data;

namespace MicrosoftToDoEnhanced;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var error = await DatabaseProvisioner.EnsureDatabaseAsync();
        if (error is not null)
        {
            MessageBox.Show(
                "The database could not be prepared. The application will now close."
                + Environment.NewLine + Environment.NewLine + "Details: " + error,
                "Microsoft To Do Enhanced",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        new Views.LoginView().Show();
        ShutdownMode = ShutdownMode.OnLastWindowClose;
    }
}
