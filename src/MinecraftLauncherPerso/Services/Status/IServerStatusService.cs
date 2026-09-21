namespace MinecraftLauncherPerso.Services.Status;

public sealed record ServerStatus(bool IsOnline, int OnlinePlayers, int MaxPlayers, string? ErrorDetail = null);

public interface IServerStatusService
{
    /// <summary>
    /// Interroge le serveur via le protocole Server List Ping de Minecraft (le même que l'écran
    /// multijoueur du jeu utilise pour afficher joueurs connectés/latence) et retourne son statut.
    /// Ne lève jamais : un serveur injoignable (hors ligne, timeout, port fermé) retourne
    /// simplement <see cref="ServerStatus.IsOnline"/> à false.
    /// </summary>
    Task<ServerStatus> PingAsync(string host, int port, CancellationToken cancellationToken = default);
}
