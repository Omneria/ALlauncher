using System.Net.Http;

namespace MinecraftLauncherPerso.Services.News;

/// <summary>
/// Lit news.txt hébergé à côté du zip du modpack sur le VPS (même convention que changelog.txt
/// dans ModSyncService : un fichier texte simple, pas de format structuré à maintenir côté serveur).
/// </summary>
public sealed class NewsService : INewsService
{
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
            using var response = await _httpClient.GetAsync(newsUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var content = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
            return content.Length == 0 ? null : content;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }
}
