namespace MinecraftLauncherPerso.Services.Status;

/// <param name="LatencyMs">Aller-retour mesuré par le ping du protocole (v1.12.0), null si inconnu.</param>
/// <param name="ProtocolVersion">Protocole annoncé par le serveur (763 pour 1.20.1), null si absent.</param>
/// <param name="VersionName">Version affichée par le serveur ("1.20.1", "Forge 1.20.1"...).</param>
/// <param name="Motd">Message du jour du serveur, codes de couleur retirés.</param>
public sealed record ServerStatus(
    bool IsOnline,
    int OnlinePlayers,
    int MaxPlayers,
    string? ErrorDetail = null,
    int? LatencyMs = null,
    int? ProtocolVersion = null,
    string? VersionName = null,
    string? Motd = null,
    IReadOnlyList<string>? PlayerNames = null);

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
