using System.Net.Http;

namespace MinecraftLauncherPerso.Services.Maintenance;

/// <summary>
/// Bannière de maintenance (v1.9.0) : un maintenance.txt optionnel hébergé sur le VPS (à côté du
/// modpack) permet de prévenir les joueurs d'une coupure programmée directement dans le launcher,
/// sans dépendre de Discord. Absence de fichier = pas de maintenance en cours, comportement identique
/// à ReportChangelogAsync/VerifyIntegrityAsync (ModSyncService) : un 404 n'est jamais une erreur ici.
/// </summary>
public sealed class MaintenanceService : IMaintenanceService
{
    private readonly HttpClient _httpClient;

    public MaintenanceService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<string?> FetchMaintenanceMessageAsync(string maintenanceUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(maintenanceUrl))
        {
            return null;
        }

        try
        {
            using var response = await _httpClient.GetAsync(maintenanceUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var content = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
            return content.Length == 0 ? null : content;
        }
        catch (HttpRequestException)
        {
            // VPS injoignable : pas de quoi afficher une erreur pour une simple bannière optionnelle.
            return null;
        }
    }
}
