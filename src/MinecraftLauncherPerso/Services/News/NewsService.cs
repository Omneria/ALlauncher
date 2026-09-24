using System.Net.Http;
using MinecraftLauncherPerso.Services.Diagnostics;

namespace MinecraftLauncherPerso.Services.News;

/// <summary>
/// Lit news.txt hébergé à côté du zip du modpack sur le VPS (même convention que changelog.txt
/// dans ModSyncService : un fichier texte simple, pas de format structuré à maintenir côté serveur).
/// </summary>
public sealed class NewsService : INewsService
{
    // Petit fichier texte relu toutes les minutes : un VPS qui ne répond pas doit rendre la main
    // bien avant le timeout HttpClient par défaut (100 s), sinon les rafraîchissements s'empilent.
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _httpClient;

    public NewsService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<string?> FetchNewsAsync(string modpackZipUrl, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(modpackZipUrl, UriKind.Absolute, out var zipUri))
        {
            return null;
        }

        var newsUri = new Uri(zipUri, "news.txt");

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(RequestTimeout);

            using var response = await _httpClient.GetAsync(newsUri, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var content = (await response.Content.ReadAsStringAsync(timeoutCts.Token)).Trim();
            return content.Length == 0 ? null : content;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Best-effort : auparavant seule HttpRequestException était rattrapée, un simple timeout
            // (TaskCanceledException) remontait jusqu'au timer de rafraîchissement et faisait
            // planter tout le launcher pour un fichier d'actus optionnel.
            Logger.Warn("NewsService", $"Lecture de news.txt échouée : {ex.Message}");
            return null;
        }
    }
}
