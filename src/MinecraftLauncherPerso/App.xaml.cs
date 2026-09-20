using System.Threading;
using System.Windows;

namespace MinecraftLauncherPerso;

public partial class App : Application
{
    // Nom global (préfixe "Global\") : le mutex est visible pour tous les utilisateurs de la
    // machine, pas seulement la session courante, pour vraiment empêcher un double lancement.
    private const string SingleInstanceMutexName = "Global\\AL_Launcher_SingleInstance";

    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "AL Launcher est déjà en cours d'exécution.",
                "AL Launcher",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        new SplashWindow().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
