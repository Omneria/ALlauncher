using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using MinecraftLauncherPerso.Services.Diagnostics;

namespace MinecraftLauncherPerso.Services.Auth;

/// <summary>
/// Authentifie l'utilisateur directement via OAuth Microsoft (MSAL.NET, flux interactif avec le
/// navigateur système + redirection sur boucle locale — même expérience que CurseForge/Paladium :
/// pas de code à recopier, juste se connecter dans l'onglet qui s'ouvre), puis échange le token
/// Microsoft contre une session Minecraft via la chaîne standard Xbox Live -> XSTS -> Minecraft
/// Services. Nécessite une application Azure AD enregistrée par l'utilisateur (voir README) :
/// Minecraft/Xbox Live n'acceptent que des tokens émis pour une application cliente publique
/// explicitement enregistrée, il n'existe pas de client ID générique réutilisable pour un launcher
/// tiers.
///
/// La session Microsoft (refresh token) est mise en cache localement (MSAL "TokenCacheHelper"
/// standard) : une fois connecté une première fois, les lancements suivants renouvellent la
/// session en silence tant que le refresh token Microsoft reste valide (habituellement des mois),
/// sans redemander de connexion interactive.
/// </summary>
public sealed class MicrosoftAuthService : IAuthService
{
    // "v2" : format différent de l'ancien msal-cache.bin (sérialisé à la main, non chiffré) —
    // nom distinct pour ne jamais risquer de lire un fichier dans le mauvais format. L'ancien
    // fichier devient simplement orphelin (inoffensif), et chacun se reconnecte une dernière fois
    // avant que la reconnexion silencieuse reprenne durablement.
    private const string TokenCacheFileName = "msal-cache-v2.bin";

    private static readonly string[] Scopes = { "XboxLive.signin", "offline_access" };

    private readonly string _clientId;
    private readonly string _tokenCacheDirectory;
    private readonly HttpClient _httpClient;

    public MicrosoftAuthService(string clientId, string? tokenCacheDirectory = null, HttpClient? httpClient = null)
    {
        _clientId = clientId;
        _tokenCacheDirectory = tokenCacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MinecraftLauncherPerso");
        _httpClient = httpClient ?? new HttpClient();

        // Certains points de terminaison Xbox Live renvoient 403 aux requêtes sans User-Agent
        // (traitées comme du trafic automatisé suspect) : on en fournit toujours un.
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("MinecraftLauncherPerso/1.0");
        }
    }

    public async Task<MinecraftSession> GetActiveSessionAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var app = await BuildAppAsync();

        progress?.Report("Connexion à Microsoft...");
        var microsoftAccessToken = await AcquireMicrosoftTokenAsync(app, progress, cancellationToken);

        return await CompleteMinecraftLoginAsync(microsoftAccessToken, progress, cancellationToken);
    }

    public async Task<MinecraftSession?> TryGetCachedSessionAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_clientId))
        {
            return null;
        }

        var app = await BuildAppAsync();
        var accounts = await app.GetAccountsAsync();
        var account = accounts.FirstOrDefault();
        if (account is null)
        {
            return null;
        }

        string microsoftAccessToken;
        try
        {
            var silentResult = await app.AcquireTokenSilent(Scopes, account).ExecuteAsync(cancellationToken);
            microsoftAccessToken = silentResult.AccessToken;
        }
        catch (MsalUiRequiredException)
        {
            // Session Microsoft en cache expirée/révoquée : jamais de navigateur ici, on laisse
            // l'appelant proposer une reconnexion interactive explicite (bouton SE CONNECTER).
            return null;
        }

        try
        {
            return await CompleteMinecraftLoginAsync(microsoftAccessToken, progress: null, cancellationToken);
        }
        catch (Exception ex)
        {
            // Best-effort : un profil Minecraft/Xbox Live indisponible ponctuellement (réseau,
            // service en maintenance) ne doit pas empêcher le launcher de démarrer normalement,
            // l'utilisateur pourra toujours se reconnecter via le bouton SE CONNECTER. L'échec
            // reste tracé dans le journal (auparavant totalement invisible) pour diagnostiquer un
            // échec qui se répéterait anormalement à chaque démarrage.
            Logger.Warn("MicrosoftAuthService", $"Reconnexion silencieuse échouée : {ex.Message}");
            return null;
        }
    }

    private async Task<IPublicClientApplication> BuildAppAsync()
    {
        if (string.IsNullOrWhiteSpace(_clientId))
        {
            throw new InvalidOperationException(
                "MicrosoftClientId n'est pas configuré (settings.json). Créez une application Azure AD " +
                "(voir README) et renseignez son \"Application (client) ID\".");
        }

        var app = PublicClientApplicationBuilder.Create(_clientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, "consumers")
            .WithRedirectUri("http://localhost")
            .Build();

        await EnableTokenCacheSerializationAsync(app.UserTokenCache);
        return app;
    }

    private async Task<MinecraftSession> CompleteMinecraftLoginAsync(
        string microsoftAccessToken,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report("Authentification Xbox Live...");
        var xbl = await AuthenticateWithXboxLiveAsync(microsoftAccessToken, cancellationToken);

        progress?.Report("Autorisation XSTS...");
        var xsts = await AuthorizeWithXstsAsync(xbl.Token, cancellationToken);

        progress?.Report("Connexion à Minecraft...");
        var minecraftAccessToken = await LoginWithXboxAsync(xsts.UserHash, xsts.Token, cancellationToken);

        progress?.Report("Récupération du profil Minecraft...");
        var profile = await GetMinecraftProfileAsync(minecraftAccessToken, cancellationToken);

        return new MinecraftSession(profile.Username, profile.Uuid, minecraftAccessToken);
    }

    private async Task<string> AcquireMicrosoftTokenAsync(
        IPublicClientApplication app,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var accounts = await app.GetAccountsAsync();
        var account = accounts.FirstOrDefault();

        if (account is not null)
        {
            try
            {
                var silentResult = await app.AcquireTokenSilent(Scopes, account).ExecuteAsync(cancellationToken);
                return silentResult.AccessToken;
            }
            catch (MsalUiRequiredException)
            {
                // Session Microsoft en cache expirée/révoquée : on retombe sur la connexion interactive ci-dessous.
            }
        }

        progress?.Report("Ouverture du navigateur pour la connexion Microsoft...");

        // Navigateur système + redirection sur boucle locale (http://localhost, port choisi
        // automatiquement par MSAL) : MSAL ouvre le navigateur par défaut et intercepte la
        // redirection lui-même, sans WebView2 ni code à recopier.
        var result = await app.AcquireTokenInteractive(Scopes)
            .WithUseEmbeddedWebView(false)
            .ExecuteAsync(cancellationToken);

        return result.AccessToken;
    }

    /// <summary>
    /// Utilise Microsoft.Identity.Client.Extensions.Msal (package officiel MSAL) plutôt qu'une
    /// sérialisation manuelle du cache : chiffrement DPAPI natif sur Windows, verrouillage de
    /// fichier correct entre process concurrents, et c'est l'implémentation que Microsoft
    /// recommande et maintient pour ce cas d'usage précis (reconnexion silencieuse fiable d'un
    /// lancement à l'autre, tant que le refresh token Microsoft reste valide).
    /// </summary>
    private async Task EnableTokenCacheSerializationAsync(ITokenCache tokenCache)
    {
        Directory.CreateDirectory(_tokenCacheDirectory);

        var storageProperties = new StorageCreationPropertiesBuilder(TokenCacheFileName, _tokenCacheDirectory)
            .Build();

        var cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties);
        cacheHelper.RegisterCache(tokenCache);
    }

    private async Task<(string Token, string UserHash)> AuthenticateWithXboxLiveAsync(string microsoftAccessToken, CancellationToken cancellationToken)
    {
        var payload = new XblAuthRequest
        {
            Properties = new XblAuthProperties
            {
                AuthMethod = "RPS",
                SiteName = "user.auth.xboxlive.com",
                RpsTicket = $"d={microsoftAccessToken}",
            },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT",
        };

        using var response = await PostJsonAsync("https://user.auth.xboxlive.com/user/authenticate", payload, cancellationToken);
        await EnsureSuccessAsync(response, "Xbox Live (user/authenticate)", cancellationToken);

        var result = await DeserializeAsync<XboxTokenResponse>(response, cancellationToken)
            ?? throw new InvalidOperationException("Réponse Xbox Live invalide.");

        var userHash = result.DisplayClaims?.Xui?.FirstOrDefault()?.Uhs
            ?? throw new InvalidOperationException("Réponse Xbox Live invalide : hash utilisateur manquant.");

        return (result.Token, userHash);
    }

    private async Task<(string Token, string UserHash)> AuthorizeWithXstsAsync(string xblToken, CancellationToken cancellationToken)
    {
        var payload = new XstsAuthRequest
        {
            Properties = new XstsAuthProperties
            {
                SandboxId = "RETAIL",
                UserTokens = new List<string> { xblToken },
            },
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType = "JWT",
        };

        using var response = await PostJsonAsync("https://xsts.auth.xboxlive.com/xsts/authorize", payload, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var error = JsonSerializer.Deserialize<XstsErrorResponse>(errorBody);
            throw BuildXstsError(error?.XErr);
        }

        await EnsureSuccessAsync(response, "XSTS (xsts/authorize)", cancellationToken);

        var result = await DeserializeAsync<XboxTokenResponse>(response, cancellationToken)
            ?? throw new InvalidOperationException("Réponse XSTS invalide.");

        var userHash = result.DisplayClaims?.Xui?.FirstOrDefault()?.Uhs
            ?? throw new InvalidOperationException("Réponse XSTS invalide : hash utilisateur manquant.");

        return (result.Token, userHash);
    }

    private async Task<string> LoginWithXboxAsync(string userHash, string xstsToken, CancellationToken cancellationToken)
    {
        var payload = new MinecraftLoginRequest { IdentityToken = $"XBL3.0 x={userHash};{xstsToken}" };

        using var response = await PostJsonAsync("https://api.minecraftservices.com/authentication/login_with_xbox", payload, cancellationToken);
        await EnsureSuccessAsync(response, "Minecraft (login_with_xbox)", cancellationToken);

        var result = await DeserializeAsync<MinecraftLoginResponse>(response, cancellationToken)
            ?? throw new InvalidOperationException("Réponse de connexion Minecraft invalide.");

        return result.AccessToken;
    }

    private async Task<(string Username, string Uuid)> GetMinecraftProfileAsync(string minecraftAccessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", minecraftAccessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                "Ce compte Microsoft ne possède pas Minecraft (Java Edition). Connectez-vous avec le bon compte.");
        }

        await EnsureSuccessAsync(response, "Minecraft (profile)", cancellationToken);

        var profile = await DeserializeAsync<MinecraftProfileResponse>(response, cancellationToken)
            ?? throw new InvalidOperationException("Réponse de profil Minecraft invalide.");

        return (profile.Name, FormatUuid(profile.Id));
    }

    private async Task<HttpResponseMessage> PostJsonAsync(string url, object payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("x-xbl-contract-version", "1");

        return await _httpClient.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// Remplace EnsureSuccessStatusCode() par une erreur qui nomme l'étape et inclut le corps de la
    /// réponse : "403 (Forbidden)" seul ne dit pas quel appel (Xbox Live/XSTS/Minecraft) a échoué
    /// ni pourquoi, ce qui rend le diagnostic très difficile côté utilisateur final.
    /// </summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string stepName, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException(
            $"Échec de l'étape \"{stepName}\" : HTTP {(int)response.StatusCode} {response.StatusCode}. Réponse : {body}");
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: cancellationToken);
    }

    private static Exception BuildXstsError(long? xErr)
    {
        var message = xErr switch
        {
            2148916233 => "Ce compte Microsoft n'a pas de compte Xbox associé. Créez-en un sur https://www.xbox.com puis réessayez.",
            2148916235 => "Le Xbox Live n'est pas disponible dans votre pays/région.",
            2148916236 or 2148916237 => "Ce compte nécessite une vérification d'âge (Xbox Live) sur https://account.microsoft.com/family/.",
            2148916238 => "Ce compte enfant doit d'abord être ajouté à une famille Microsoft (par un adulte) avant de pouvoir jouer.",
            _ => $"Échec de l'autorisation Xbox Live (XErr={xErr}).",
        };

        return new InvalidOperationException(message);
    }

    private static string FormatUuid(string rawId)
    {
        var clean = rawId.Replace("-", "");
        if (clean.Length != 32)
        {
            return rawId; // Format inattendu : on le laisse tel quel plutôt que de planter ici.
        }

        return $"{clean[..8]}-{clean[8..12]}-{clean[12..16]}-{clean[16..20]}-{clean[20..]}";
    }

    private sealed class XblAuthRequest
    {
        [JsonPropertyName("Properties")]
        public XblAuthProperties Properties { get; set; } = null!;

        [JsonPropertyName("RelyingParty")]
        public string RelyingParty { get; set; } = "";

        [JsonPropertyName("TokenType")]
        public string TokenType { get; set; } = "";
    }

    private sealed class XblAuthProperties
    {
        [JsonPropertyName("AuthMethod")]
        public string AuthMethod { get; set; } = "";

        [JsonPropertyName("SiteName")]
        public string SiteName { get; set; } = "";

        [JsonPropertyName("RpsTicket")]
        public string RpsTicket { get; set; } = "";
    }

    private sealed class XstsAuthRequest
    {
        [JsonPropertyName("Properties")]
        public XstsAuthProperties Properties { get; set; } = null!;

        [JsonPropertyName("RelyingParty")]
        public string RelyingParty { get; set; } = "";

        [JsonPropertyName("TokenType")]
        public string TokenType { get; set; } = "";
    }

    private sealed class XstsAuthProperties
    {
        [JsonPropertyName("SandboxId")]
        public string SandboxId { get; set; } = "RETAIL";

        [JsonPropertyName("UserTokens")]
        public List<string> UserTokens { get; set; } = new();
    }

    private sealed class XboxTokenResponse
    {
        [JsonPropertyName("Token")]
        public string Token { get; set; } = "";

        [JsonPropertyName("DisplayClaims")]
        public XboxDisplayClaims? DisplayClaims { get; set; }
    }

    private sealed class XboxDisplayClaims
    {
        [JsonPropertyName("xui")]
        public List<XboxUserInfo>? Xui { get; set; }
    }

    private sealed class XboxUserInfo
    {
        [JsonPropertyName("uhs")]
        public string Uhs { get; set; } = "";
    }

    private sealed class XstsErrorResponse
    {
        [JsonPropertyName("XErr")]
        public long? XErr { get; set; }
    }

    private sealed class MinecraftLoginRequest
    {
        [JsonPropertyName("identityToken")]
        public string IdentityToken { get; set; } = "";
    }

    private sealed class MinecraftLoginResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = "";
    }

    private sealed class MinecraftProfileResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
    }
}
