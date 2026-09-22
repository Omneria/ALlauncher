namespace MinecraftLauncherPerso.Services.Maintenance;

public interface IMaintenanceService
{
    /// <summary>
    /// Récupère le message de maintenance courant depuis l'URL configurée (maintenance.txt sur le
    /// VPS, à côté du modpack). Null si l'URL n'est pas configurée, si le fichier n'existe pas
    /// (204/404 : pas de maintenance en cours) ou si son contenu est vide — dans tous ces cas,
    /// aucune bannière ne doit s'afficher.
    /// </summary>
    Task<string?> FetchMaintenanceMessageAsync(string maintenanceUrl, CancellationToken cancellationToken = default);
}
