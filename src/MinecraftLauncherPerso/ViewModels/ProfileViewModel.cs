using System.Windows.Media;
using MinecraftLauncherPerso.Services.Formatting;

namespace MinecraftLauncherPerso.ViewModels;

/// <summary>Bloc profil de la barre latérale : connexion, pseudo, tête, version et dernière synchro.</summary>
public sealed class ProfileViewModel : ObservableObject
{
    private bool _isConnected;
    private string _playerName = "";
    private ImageSource? _avatar;
    private string? _avatarError;
    private string _versionLine = "";

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (SetProperty(ref _isConnected, value))
            {
                OnPropertyChanged(nameof(IsDisconnected));
            }
        }
    }

    public bool IsDisconnected => !IsConnected;

    public string PlayerName
    {
        get => _playerName;
        private set => SetProperty(ref _playerName, value);
    }

    public ImageSource? Avatar
    {
        get => _avatar;
        private set => SetProperty(ref _avatar, value);
    }

    /// <summary>Détail de l'échec de chargement de la tête (infobulle du ⚠), null s'il n'y en a pas.</summary>
    public string? AvatarError
    {
        get => _avatarError;
        private set
        {
            if (SetProperty(ref _avatarError, value))
            {
                OnPropertyChanged(nameof(HasAvatarError));
            }
        }
    }

    public bool HasAvatarError => AvatarError is not null;

    /// <summary>« v2.0.1 · SYNCHRO IL Y A 2 H ».</summary>
    public string VersionLine
    {
        get => _versionLine;
        private set => SetProperty(ref _versionLine, value);
    }

    public void SetConnected(string playerName)
    {
        PlayerName = playerName;
        IsConnected = true;
    }

    public void SetAvatar(ImageSource image)
    {
        Avatar = image;
        AvatarError = null;
    }

    public void SetAvatarError(string detail) => AvatarError = detail;

    public void UpdateSync(DateTimeOffset? lastSyncedAt, string versionDisplay, DateTimeOffset now) =>
        VersionLine = lastSyncedAt is null
            ? $"v{versionDisplay} · JAMAIS SYNCHRONISÉ"
            : $"v{versionDisplay} · SYNCHRO {RelativeTime.Format(lastSyncedAt.Value, now).ToUpperInvariant()}";
}
