namespace MinecraftLauncherPerso.Services.Changelog;

public interface IReleaseChangelogService
{
    /// <summary>
    /// Récupère les <paramref name="count"/> dernières releases GitHub non-draft/pré-release du
    /// launcher, avec leurs notes de version nettoyées. Retourne une liste vide si la requête
    /// échoue (GitHub indisponible, rate-limit non authentifié...) — jamais bloquant.
    /// </summary>
    Task<IReadOnlyList<ReleaseChangelogEntry>> GetRecentReleasesAsync(int count, CancellationToken cancellationToken = default);
}
