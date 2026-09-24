namespace MinecraftLauncherPerso.Services.Maintenance;

/// <summary>
/// Résultat d'une lecture de maintenance.txt. Trois cas distincts, parce que "je ne sais pas" n'est
/// pas "pas de maintenance" : si le VPS est injoignable, la bannière déjà affichée doit rester
/// telle quelle (c'est précisément pendant une coupure que l'information compte le plus) au lieu
/// de disparaître comme si la maintenance était terminée.
/// </summary>
/// <param name="IsKnown">Faux si le VPS n'a pas pu être interrogé (réseau, timeout) : état inconnu.</param>
/// <param name="Message">Contenu du fichier si une maintenance est en cours, null sinon (404, fichier vide).</param>
public sealed record MaintenanceStatus(bool IsKnown, string? Message)
{
    public static readonly MaintenanceStatus Unknown = new(false, null);

    public static readonly MaintenanceStatus None = new(true, null);
}

public interface IMaintenanceService
{
    /// <summary>
    /// Récupère le message de maintenance courant depuis l'URL configurée (maintenance.txt sur le
    /// VPS, à côté du modpack). <see cref="MaintenanceStatus.None"/> si l'URL n'est pas configurée,
    /// si le fichier n'existe pas (204/404 : pas de maintenance en cours) ou si son contenu est vide ;
    /// <see cref="MaintenanceStatus.Unknown"/> si le VPS n'a pas pu être interrogé. Ne lève jamais.
    /// </summary>
    Task<MaintenanceStatus> FetchMaintenanceMessageAsync(string maintenanceUrl, CancellationToken cancellationToken = default);
}
