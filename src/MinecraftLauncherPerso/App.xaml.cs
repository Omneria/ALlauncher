using System.Threading;
using System.Windows;
using System.Windows.Threading;
using MinecraftLauncherPerso.Services.Diagnostics;

namespace MinecraftLauncherPerso;

public partial class App : Application
{
    // Nom global (préfixe "Global\") : le mutex est visible pour tous les utilisateurs de la
    // machine, pas seulement la session courante, pour vraiment empêcher un double lancement.
    private const string SingleInstanceMutexName = "Global\\AL_Launcher_SingleInstance";

    private Mutex? _singleInstanceMutex;

    // Une seule boîte de dialogue d'erreur par session : une exception qui se reproduirait à
    // chaque tick de timer ou à chaque passe de layout ne doit pas noyer le joueur sous des
    // MessageBox en boucle — les suivantes ne sont que journalisées.
    private static int _errorDialogShown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Filet de sécurité global : jusqu'ici, une exception non gérée sur le thread UI (dans un
        // "async void" comme MainWindow_Loaded, ou pendant le layout) faisait fermer le launcher
        // instantanément, sans aucun message ni trace dans launcher.log — un joueur ne pouvait que
        // constater "ça se ferme tout seul", impossible à diagnostiquer à distance (vécu en v1.9.2,
        // InvariantGlobalization). Log + message clair au lieu d'une fermeture muette.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

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

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Tant que le tableau de bord n'est pas affiché, une exception est forcément fatale (rien
        // d'autre ne peut se passer) : on ferme proprement avec un message. Une fois le dashboard
        // en place, tout ce qui tourne sur le thread UI est best-effort (rafraîchissements, clics) :
        // on journalise, on prévient une fois, et le launcher continue — fermer d'autorité le
        // launcher pour un rafraîchissement d'actus raté serait pire que le bug lui-même.
        var dashboardIsUp = Current?.MainWindow is MainWindow { IsLoaded: true };
        Logger.Error("App", dashboardIsUp
            ? "Exception non gérée sur le thread UI, le launcher continue."
            : "Exception non gérée sur le thread UI avant l'affichage du tableau de bord, le launcher va se fermer.", e.Exception);
        e.Handled = true;

        if (!dashboardIsUp)
        {
            MessageBox.Show(
                $"AL Launcher a rencontré une erreur inattendue et doit se fermer :\n\n{e.Exception.Message}\n\nDétails dans launcher.log (%AppData%\\MinecraftLauncherPerso).",
                "AL Launcher — Erreur",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Current?.Shutdown();
            return;
        }

        if (Interlocked.Exchange(ref _errorDialogShown, 1) == 0)
        {
            MessageBox.Show(
                $"AL Launcher a rencontré une erreur inattendue mais continue de fonctionner :\n\n{e.Exception.Message}\n\nDétails dans launcher.log (Paramètres → VOIR LES LOGS). Si quelque chose ne répond plus, redémarre le launcher.",
                "AL Launcher — Erreur",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // Ce handler (AppDomain, contrairement à DispatcherUnhandledException) ne peut pas
        // empêcher le crash du processus (IsTerminating est déjà vrai côté runtime dans la plupart
        // des cas) : on se contente de logger avant que le processus ne meure, pour au moins
        // garder une trace exploitable après coup.
        if (e.ExceptionObject is Exception exception)
        {
            Logger.Error("App", "Exception non gérée hors du thread UI, le launcher va se fermer.", exception);
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // Tâche "fire-and-forget" (ex. LoadPlayerAvatarAsync lancée sans await) qui a échoué sans
        // que personne n'observe son exception : ne ferme pas le process depuis .NET 4.5, mais
        // restait totalement invisible. Tracée, et marquée observée pour être explicite.
        Logger.Error("App", "Exception non observée dans une tâche d'arrière-plan.", e.Exception);
        e.SetObserved();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
