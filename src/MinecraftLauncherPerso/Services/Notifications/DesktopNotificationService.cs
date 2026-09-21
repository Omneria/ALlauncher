using System.Windows;
using System.Windows.Threading;
using MinecraftLauncherPerso.Services.Diagnostics;
using Forms = System.Windows.Forms;

namespace MinecraftLauncherPerso.Services.Notifications;

/// <summary>
/// Notification desktop native Windows (bulle/toast dans la zone de notification), via
/// System.Windows.Forms.NotifyIcon plutôt qu'une dépendance dédiée (ex. toolkit UWP toast) qui
/// suppose généralement une identité de paquet (MSIX) que ce launcher, publié en .exe autonome,
/// n'a pas. Sert par exemple à signaler que le serveur Astral Nexus est redevenu joignable pendant
/// que le launcher tourne en arrière-plan, sans avoir à garder la fenêtre au premier plan.
/// </summary>
public static class DesktopNotificationService
{
    public static void Show(string title, string message)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        // NotifyIcon repose sur une fenêtre native cachée : toute manipulation doit rester sur le
        // thread UI, d'où le passage explicite par le Dispatcher plutôt qu'un simple appel direct
        // (Show peut être invoqué depuis un contexte async quelconque).
        dispatcher.Invoke(() => ShowOnUiThread(title, message));
    }

    private static void ShowOnUiThread(string title, string message)
    {
        try
        {
            var icon = LoadAppIcon();
            var notifyIcon = new Forms.NotifyIcon
            {
                Icon = icon,
                Visible = true,
                BalloonTipTitle = title,
                BalloonTipText = message,
            };
            notifyIcon.ShowBalloonTip(5000);

            // Retire l'icône de la zone de notification après un délai plutôt que de la laisser en
            // permanence : ce n'est pas une icône d'état persistante, juste le porteur de cette
            // notification ponctuelle. DispatcherTimer (thread UI) plutôt que System.Timers.Timer,
            // pour ne pas disposer un NotifyIcon depuis un thread d'arrière-plan.
            var cleanupTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            cleanupTimer.Tick += (_, _) =>
            {
                cleanupTimer.Stop();
                notifyIcon.Visible = false;
                notifyIcon.Dispose();
                icon.Dispose();
            };
            cleanupTimer.Start();
        }
        catch (Exception ex)
        {
            // Best-effort : jamais bloquant pour le reste du launcher.
            Logger.Warn("DesktopNotificationService", $"Échec de l'affichage d'une notification : {ex.Message}");
        }
    }

    private static System.Drawing.Icon LoadAppIcon()
    {
        var uri = new Uri("pack://application:,,,/Assets/Images/omneria-mark.ico");
        var streamInfo = Application.GetResourceStream(uri)
            ?? throw new InvalidOperationException("Icône introuvable (Assets/Images/omneria-mark.ico).");
        return new System.Drawing.Icon(streamInfo.Stream);
    }
}
