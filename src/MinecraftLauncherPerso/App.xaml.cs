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
        Logger.Error("App", "Exception non gérée sur le thread UI, le launcher va se fermer.", e.Exception);
        MessageBox.Show(
            $"AL Launcher a rencontré une erreur inattendue et doit se fermer :\n\n{e.Exception.Message}\n\nDétails dans launcher.log (Paramètres → VOIR LES LOGS).",
            "AL Launcher — Erreur",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
        Current.Shutdown();
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

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
