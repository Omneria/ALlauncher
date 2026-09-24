using System.Net.Http;
using MinecraftLauncherPerso.Services.Diagnostics;

namespace MinecraftLauncherPerso.Services.Maintenance;

/// <summary>
/// Bannière de maintenance (v1.9.0) : un maintenance.txt optionnel hébergé sur le VPS (à côté du
/// modpack) permet de prévenir les joueurs d'une coupure programmée directement dans le launcher,
/// sans dépendre de Discord. Absence de fichier = pas de maintenance en cours, comportement identique
/// à ReportChangelogAsync/VerifyIntegrityAsync (ModSyncService) : un 404 n'est jamais une erreur ici.
/// </summary>
public sealed class MaintenanceService : IMaintenanceService
{
    // Voir NewsService : fichier minuscule relu toutes les minutes, pas de raison d'attendre 100 s.
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _httpClient;

    public MaintenanceService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<MaintenanceStatus> FetchMaintenanceMessageAsync(string maintenanceUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(maintenanceUrl))
        {
            return MaintenanceStatus.None;
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(RequestTimeout);

            using var response = await _httpClient.GetAsync(maintenanceUrl, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                // 404 (fichier supprimé = maintenance terminée) est la réponse normale. Une erreur
                // serveur (5xx) est traitée pareil : le fichier est servi statiquement, un 5xx
                // signifie que le serveur web tourne mais ne sert plus ce fichier.
                return MaintenanceStatus.None;
            }

            var content = (await response.Content.ReadAsStringAsync(timeoutCts.Token)).Trim();
            return content.Length == 0 ? MaintenanceStatus.None : new MaintenanceStatus(true, content);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // VPS injoignable : on ne sait pas, et on ne doit surtout pas faire disparaître une
            // bannière déjà affichée (le VPS injoignable est souvent LA maintenance annoncée).
            Logger.Warn("MaintenanceService", $"Lecture de maintenance.txt impossible, état inchangé : {ex.Message}");
            return MaintenanceStatus.Unknown;
        }
    }
}
