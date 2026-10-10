using System.Net.Http;
using System.Text.Json;
using MinecraftLauncherPerso.Services.Diagnostics;

namespace MinecraftLauncherPerso.Services.Status;

/// <summary>Lit server-history.json à côté du manifest du modpack. Best-effort : un VPS qui ne
/// publie pas (encore) l'historique donne simplement une page SERVEUR sans courbe.</summary>
public sealed class ServerHistoryService(HttpClient httpClient)
{
    private const string FileName = "server-history.json";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public async Task<ServerHistory?> FetchAsync(string? manifestUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(manifestUrl) || !Uri.TryCreate(manifestUrl, UriKind.Absolute, out var manifestUri))
        {
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            using var response = await httpClient.GetAsync(new Uri(manifestUri, FileName), timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            return await JsonSerializer.DeserializeAsync<ServerHistory>(stream, cancellationToken: timeout.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            Logger.Warn("ServerHistory", $"Historique du serveur indisponible : {ex.Message}");
            return null;
        }
    }
}
