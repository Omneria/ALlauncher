using System.Windows;
using MinecraftLauncherPerso.Controls;
using MinecraftLauncherPerso.Services.Status;
using MinecraftLauncherPerso.ViewModels;

namespace MinecraftLauncherPerso;

// Navigation entre le hub et les pages (v2.0.0) : toutes vivent dans la même fenêtre.
public partial class MainWindow
{
    private bool _pagesWired;
    private bool _serverPageWired;
    private ServerHistory? _serverHistory;
    private readonly Services.Skins.PlayerAvatarService _playerAvatars = new(
        Services.Http.SharedHttpClient.Instance,
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MinecraftLauncherPerso", "avatar-cache", "players"));

    private readonly ServerHistoryService _serverHistoryService = new(Services.Http.SharedHttpClient.Instance);

    private void HubNavItem_Click(object sender, RoutedEventArgs e) => ShowPage(AppPage.Hub);

    private void ActusNavItem_Click(object sender, RoutedEventArgs e) => OpenActus(ActusSection.News);

    private void ServeurNavItem_Click(object sender, RoutedEventArgs e) => OpenServerPage();

    private void ParametresNavItem_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void TileActusLink_Click(object sender, RoutedEventArgs e) => OpenActus(ActusSection.News);

    private void TileModpackLink_Click(object sender, RoutedEventArgs e) => OpenActus(ActusSection.Modpack);

    private void TileLauncherLink_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Tiles.LauncherUrl is { Length: > 0 } url)
        {
            OpenExternalUrl(url);
        }
        else
        {
            OpenActus(ActusSection.Launcher);
        }
    }

    /// <summary>
    /// Pages : elles remplacent le tableau de bord dans la même fenêtre. Visibilités, libellé de la
    /// barre de titre et item actif de la barre latérale se déduisent de la page courante du view-model.
    /// </summary>
    private void ShowPage(AppPage page) => _vm.CurrentPage = page;

    private void WirePages()
    {
        if (_pagesWired)
        {
            return;
        }

        _pagesWired = true;
        SettingsPageView.Closed += (_, saved) =>
        {
            if (saved)
            {
                _settingsManager.Save(_settings);
            }

            ShowPage(AppPage.Hub);
        };
        SettingsPageView.ViewLogsRequested += (_, _) =>
        {
            LogsPageView.Initialize(_settings.GameDirectory);
            ShowPage(AppPage.Logs);
        };
        SettingsPageView.LegalRequested += (_, _) => ShowPage(AppPage.Legal);
        LogsPageView.BackRequested += (_, _) => ShowPage(AppPage.Settings);
        LegalPageView.BackRequested += (_, _) => ShowPage(AppPage.Settings);
        ActusPageView.ReleaseOpenRequested += (_, url) => OpenExternalUrl(url);
    }

    private void OpenActus(ActusSection section)
    {
        WirePages();
        ShowPage(AppPage.Actus);
        ActusPageView.ScrollTo(section);
    }

    private void OpenServerPage()
    {
        if (!_serverPageWired)
        {
            _serverPageWired = true;
            ServerPageView.AvatarProvider = name => _playerAvatars.GetAvatarAsync(name);
            ServerPageView.NotificationsToggled += (_, enabled) =>
            {
                _settings.DesktopNotificationsEnabled = enabled;
                _settingsManager.Save(_settings);
            };
            ServerPageView.FriendNotificationsToggled += (_, enabled) =>
            {
                _settings.NotifyFriendConnected = enabled;
                _settingsManager.Save(_settings);
                if (enabled && _settings.FriendNames.Count == 0)
                {
                    Toasts.Show("Aucun ami à surveiller", "Ajoute leurs pseudos dans Paramètres > Launcher.", ToastKind.Info);
                }
            };
            ServerPageView.PlayRequested += (_, _) =>
            {
                ShowPage(AppPage.Hub);
                if (Launch.PlayEnabled)
                {
                    PlayButton_Click(PlayButton, new RoutedEventArgs());
                }
            };
        }

        ServerPageView.Update(_settings, _lastServerStatus, _serverHistory, DateTimeOffset.UtcNow);
        ShowPage(AppPage.Server);
        _ = RunSafeAsync("Page serveur", RefreshServerPageAsync);
    }

    private async Task RefreshServerPageAsync()
    {
        _serverHistory = await _serverHistoryService.FetchAsync(_settings.ModpackManifestUrl) ?? _serverHistory;
        ServerPageView.Update(_settings, _lastServerStatus, _serverHistory, DateTimeOffset.UtcNow);
    }

    private void OpenSettings(SettingsSection section = SettingsSection.Game)
    {
        WirePages();
        SettingsPageView.Initialize(_settings, _modSyncService, _packSettings, _modCount, section);
        ShowPage(AppPage.Settings);
    }
}
