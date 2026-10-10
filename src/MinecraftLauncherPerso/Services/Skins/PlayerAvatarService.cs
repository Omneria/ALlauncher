using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using MinecraftLauncherPerso.Services.Diagnostics;

namespace MinecraftLauncherPerso.Services.Skins;

/// <summary>
/// Têtes des joueurs affichées sur la page SERVEUR (v2.1.0). Le ping du serveur ne donne que des
/// pseudos : l'image vient d'un service public de skins (mc-heads.net, minotar.net en secours), puis
/// reste en cache sur le disque quelques heures. Best-effort : sans image, la case reste vide.
/// </summary>
public sealed partial class PlayerAvatarService(HttpClient httpClient, string cacheDirectory)
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(12);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);

    private static readonly Func<string, string>[] UrlBuilders =
    [
        name => $"https://mc-heads.net/avatar/{name}/52",
        name => $"https://minotar.net/avatar/{name}/52.png",
    ];

    /// <summary>Pseudo Minecraft valide (3 à 16 caractères : lettres, chiffres, tiret bas) : refuse
    /// tout le reste avant de bâtir une URL ou un nom de fichier.</summary>
    public static bool IsValidName(string? name) => name is not null && ValidNamePattern().IsMatch(name);

    public string CachePathFor(string name) => Path.Combine(cacheDirectory, name.ToLowerInvariant() + ".png");

    /// <summary>Octets PNG de la tête du joueur, ou null si indisponible.</summary>
    public async Task<byte[]?> GetAvatarAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!IsValidName(name))
        {
            return null;
        }

        var cachePath = CachePathFor(name);
        byte[]? cached = null;
        try
        {
            if (File.Exists(cachePath))
            {
                cached = await File.ReadAllBytesAsync(cachePath, cancellationToken);
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath) < CacheLifetime)
                {
                    return cached;
                }
            }
        }
        catch (IOException)
        {
            // Cache illisible : on retente le réseau.
        }

        foreach (var buildUrl in UrlBuilders)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(RequestTimeout);
                var bytes = await httpClient.GetByteArrayAsync(buildUrl(name), timeout.Token);
                if (bytes.Length == 0)
                {
                    continue;
                }

                try
                {
                    Directory.CreateDirectory(cacheDirectory);
                    await File.WriteAllBytesAsync(cachePath, bytes, cancellationToken);
                }
                catch (IOException)
                {
                    // Cache non écrit (disque plein...) : sans conséquence, l'image est déjà en main.
                }

                return bytes;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                Logger.Warn("Avatars", $"Tête de {name} indisponible ({ex.Message}).");
            }
        }

        // Réseau en échec : une tête périmée vaut mieux qu'une case vide.
        return cached;
    }

    [GeneratedRegex("^[A-Za-z0-9_]{3,16}$")]
    private static partial Regex ValidNamePattern();
}
