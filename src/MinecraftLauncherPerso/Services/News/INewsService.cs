namespace MinecraftLauncherPerso.Services.News;

public interface INewsService
{
    /// <summary>
    /// Récupère le contenu d'un éventuel news.txt hébergé à côté du zip du modpack (annonces,
    /// événements, maintenance prévue...). Optionnel : retourne null si le fichier n'existe pas
    /// (404) ou si le VPS est injoignable, sans jamais lever d'exception.
    /// </summary>
    Task<string?> FetchNewsAsync(string modpackZipUrl, CancellationToken cancellationToken = default);
}
