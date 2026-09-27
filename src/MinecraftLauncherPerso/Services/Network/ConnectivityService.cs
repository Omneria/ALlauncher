using System.Net.Http;
using System.Net.NetworkInformation;

namespace MinecraftLauncherPerso.Services.Network;

/// <summary>
/// État "pas de connexion internet" (v1.14.0). Les services du tableau de bord sont tous
/// best-effort : sans internet, chaque carte échouait en silence de son côté (statut serveur
/// "hors ligne", actus vides, pas de mise à jour) sans que rien ne dise au joueur que le problème
/// vient de sa connexion, pas du serveur. Ce service tranche : hors ligne quand Windows ne voit
/// aucun réseau, ou quand aucune des adresses sondées (VPS, GitHub) ne répond.
/// </summary>
public sealed class ConnectivityService : IConnectivityService
{
    // Court : le bandeau doit apparaître vite, et une sonde lente compte comme une coupure.
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(8);

    private readonly HttpClient _httpClient;
    private readonly Func<bool> _isNetworkAvailable;

    /// <param name="isNetworkAvailable">Remplaçable pour les tests ; par défaut
    /// NetworkInterface.GetIsNetworkAvailable (câble débranché, Wi-Fi coupé...).</param>
    public ConnectivityService(HttpClient httpClient, Func<bool>? isNetworkAvailable = null)
    {
        _httpClient = httpClient;
        _isNetworkAvailable = isNetworkAvailable ?? NetworkInterface.GetIsNetworkAvailable;
    }

    public async Task<bool> IsOnlineAsync(IEnumerable<string> probeUrls, CancellationToken cancellationToken = default)
    {
        if (!_isNetworkAvailable())
        {
            return false;
        }

        var probes = probeUrls
            .Where(url => Uri.TryCreate(url, UriKind.Absolute, out _))
            .Select(url => ProbeAsync(new Uri(url), cancellationToken))
            .ToList();

        if (probes.Count == 0)
        {
            // Rien à sonder : impossible de conclure à une coupure, on ne crie pas au loup.
            return true;
        }

        // En ligne dès la première réponse, sans attendre le délai des autres sondes.
        while (probes.Count > 0)
        {
            var finished = await Task.WhenAny(probes);
            if (await finished)
            {
                return true;
            }

            probes.Remove(finished);
        }

        return false;
    }

    /// <summary>Vrai si l'adresse répond quoi que ce soit en HTTP (même 404 ou 405) : c'est la
    /// connexion qui est testée, pas la présence d'un fichier.</summary>
    private async Task<bool> ProbeAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, uri);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Réseau injoignable, DNS en échec, délai dépassé : cette adresse ne répond pas.
            return false;
        }
    }
}
