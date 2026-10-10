using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MinecraftLauncherPerso.Models;
using MinecraftLauncherPerso.Services.Status;
using MinecraftLauncherPerso.ViewModels;

namespace MinecraftLauncherPerso.Controls;

/// <summary>
/// Page SERVEUR (maquette « SERVEUR — page dédiée »). Vue : tout l'affichage se déduit de
/// <see cref="ServerPageViewModel"/> ; la page se contente de charger les têtes des joueurs et de
/// relayer les actions (JOUER, cases de notification) à la fenêtre principale.
/// </summary>
public partial class ServerPage : UserControl
{
    private readonly ServerPageViewModel _vm = new();
    private readonly Dictionary<string, ImageSource> _avatars = new(StringComparer.OrdinalIgnoreCase);

    public ServerPage()
    {
        InitializeComponent();
        DataContext = _vm;
        _vm.NotificationChanged += (_, change) =>
        {
            if (change.Kind == NotificationKind.ServerBack)
            {
                NotificationsToggled?.Invoke(this, change.Enabled);
            }
            else
            {
                FriendNotificationsToggled?.Invoke(this, change.Enabled);
            }
        };
    }

    /// <summary>Case « serveur de nouveau en ligne » cochée ou décochée.</summary>
    public event EventHandler<bool>? NotificationsToggled;

    /// <summary>Case « un ami se connecte » cochée ou décochée.</summary>
    public event EventHandler<bool>? FriendNotificationsToggled;

    /// <summary>Clic sur JOUER : le hub prend le relais pour le lancement.</summary>
    public event EventHandler? PlayRequested;

    /// <summary>Fournisseur d'octets d'image (PNG) pour un pseudo, posé par la fenêtre principale.</summary>
    public Func<string, Task<byte[]?>>? AvatarProvider { get; set; }

    public void Update(LauncherSettings settings, ServerStatus? status, ServerHistory? history, DateTimeOffset now)
    {
        _vm.Update(settings, status, history, now);
        _ = LoadAvatarsAsync(_vm.Players.ToList());
    }

    private async Task LoadAvatarsAsync(IReadOnlyList<PlayerRowViewModel> rows)
    {
        foreach (var row in rows)
        {
            if (_avatars.TryGetValue(row.Name, out var known))
            {
                row.Avatar = known;
                continue;
            }

            if (AvatarProvider is null)
            {
                continue;
            }

            var bytes = await AvatarProvider(row.Name);
            if (bytes is null)
            {
                continue;
            }

            try
            {
                var image = new BitmapImage();
                using var stream = new MemoryStream(bytes);
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                _avatars[row.Name] = image;
                row.Avatar = image;
            }
            catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or IOException)
            {
                // Image illisible (réponse non PNG...) : la case reste vide.
            }
        }
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e) => PlayRequested?.Invoke(this, EventArgs.Empty);
}
