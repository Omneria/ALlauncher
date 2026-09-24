using System.Net.Http;
using MinecraftLauncherPerso.Services.Diagnostics;

namespace MinecraftLauncherPerso.Services.Http;

/// <summary>
/// Transition HTTP → HTTPS du VPS (v1.11.0) : les URL par défaut du launcher sont désormais en
/// https:// (voir LauncherSettings), mais tant que le VPS ne sert pas encore TLS (Caddy pas
/// installé, certificat en cours d'obtention, voir scripts/vps/README.md), une requête HTTPS
/// échoue au niveau transport (connexion refusée sur 443, ou TLS impossible). Pour les hôtes
/// listés dans <see cref="FallbackHosts"/> uniquement, ce handler retente alors la même requête en
/// http:// une fois, et mémorise ce repli pour le reste de la session (pas de double tentative à
/// chaque rafraîchissement d'une minute). Chaque repli est tracé dans launcher.log : c'est le
/// signal que le VPS n'est pas encore passé en HTTPS.
///
/// Repli uniquement sur échec de *connexion* (HttpRequestException sans réponse HTTP), jamais sur
/// une réponse HTTP (404, 500...) : un serveur HTTPS qui répond, même mal, est la source de
/// vérité. Ce repli est une mesure de transition à retirer une fois le VPS en HTTPS partout
/// (voir docs/ROADMAP.md) : sans lui, le launcher refuserait tout simplement de parler en clair.
/// </summary>
public sealed class SchemeFallbackHandler : DelegatingHandler
{
    /// <summary>Hôtes pour lesquels un repli HTTPS → HTTP est toléré (le VPS du modpack, rien d'autre :
    /// GitHub, Adoptium, Xbox Live... ne doivent jamais être contactés en clair).</summary>
    public static readonly IReadOnlySet<string> FallbackHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "astralnexusmc.duckdns.org",
    };

    private readonly HashSet<string> _hostsDowngraded = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public SchemeFallbackHandler(HttpMessageHandler innerHandler) : base(innerHandler)
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri;
        if (uri is null || uri.Scheme != Uri.UriSchemeHttps || !FallbackHosts.Contains(uri.Host))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        bool alreadyDowngraded;
        lock (_lock)
        {
            alreadyDowngraded = _hostsDowngraded.Contains(uri.Host);
        }

        if (alreadyDowngraded)
        {
            return await base.SendAsync(CloneAsHttp(request), cancellationToken);
        }

        try
        {
            return await base.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is null)
        {
            // Pas de réponse HTTP du tout (port 443 fermé, TLS refusé) : le VPS ne parle pas encore
            // HTTPS. Un certificat invalide tombe aussi ici — acceptable pendant la transition
            // seulement, puisque l'alternative (HTTP) n'offre de toute façon aucune garantie.
            Logger.Warn("SchemeFallbackHandler", $"{uri.Host} injoignable en HTTPS ({ex.Message}), repli en HTTP pour cette session. Passer le VPS en HTTPS (scripts/vps/README.md).");
            lock (_lock)
            {
                _hostsDowngraded.Add(uri.Host);
            }

            return await base.SendAsync(CloneAsHttp(request), cancellationToken);
        }
    }

    /// <summary>
    /// Même requête, schéma http:// et port par défaut. Les requêtes du launcher vers le VPS
    /// n'ont jamais de corps (GET/HEAD) ; en-têtes (Range, If-Range...) et options recopiés.
    /// </summary>
    private static HttpRequestMessage CloneAsHttp(HttpRequestMessage request)
    {
        var builder = new UriBuilder(request.RequestUri!) { Scheme = Uri.UriSchemeHttp, Port = -1 };
        var clone = new HttpRequestMessage(request.Method, builder.Uri) { Version = request.Version };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var option in request.Options)
        {
            clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
        }

        return clone;
    }
}
