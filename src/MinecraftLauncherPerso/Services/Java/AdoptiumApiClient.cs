using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MinecraftLauncherPerso.Services.Java;

/// <summary>Résultat de résolution : URL de téléchargement + nom de fichier + empreinte SHA-256
/// (fournie par l'API Adoptium elle-même) de l'archive Temurin.</summary>
public sealed record JavaDownloadInfo(string DownloadUrl, string FileName, string? Sha256Checksum);

/// <summary>Client minimal pour l'API Adoptium (https://api.adoptium.net), utilisé pour récupérer
/// la dernière build Temurin (JRE) correspondant à l'OS/architecture de la machine, pour la version
/// majeure Java demandée (17 pour Minecraft/Forge 1.20.1+).</summary>
public sealed class AdoptiumApiClient
{
    private const string ApiBaseUrl = "https://api.adoptium.net/v3";
    private readonly HttpClient _httpClient;

    public AdoptiumApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<JavaDownloadInfo> GetLatestJreAsync(int majorVersion, CancellationToken cancellationToken = default)
    {
        var (os, architecture) = GetCurrentPlatform();
        var requestUrl = $"{ApiBaseUrl}/assets/latest/{majorVersion}/hotspot" +
                          $"?architecture={architecture}&image_type=jre&os={os}&vendor=eclipse";

        using var response = await _httpClient.GetAsync(requestUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var assets = await JsonSerializer.DeserializeAsync<List<AdoptiumAsset>>(stream, cancellationToken: cancellationToken);

        var asset = assets?.FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"Aucune build Temurin {majorVersion} disponible pour os={os} architecture={architecture}.");

        return new JavaDownloadInfo(asset.Binary.Package.Link, asset.Binary.Package.Name, asset.Binary.Package.Checksum);
    }

    /// <summary>
    /// Télécharge l'archive puis vérifie son empreinte SHA-256 contre <paramref name="expectedSha256"/>
    /// (fournie par GetLatestJreAsync, elle-même issue de l'API Adoptium) avant de rendre la main :
    /// sans ce contrôle, un contenu altéré en transit (MITM, miroir CDN corrompu) aurait été extrait
    /// et exécuté sans qu'aucun signal ne le distingue d'un téléchargement normal.
    /// </summary>
    public async Task DownloadAsync(
        string url,
        string destinationPath,
        string? expectedSha256,
        Action<double>? onProgress,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);

        using var sha256 = SHA256.Create();
        await using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
                totalRead += bytesRead;

                if (totalBytes > 0)
                {
                    onProgress?.Invoke((double)totalRead / totalBytes);
                }
            }

            sha256.TransformFinalBlock([], 0, 0);
        }

        if (string.IsNullOrWhiteSpace(expectedSha256))
        {
            // L'API n'a exceptionnellement pas fourni d'empreinte pour cette build : pas de quoi
            // bloquer l'installation de Java, mais rien à vérifier non plus.
            return;
        }

        var actualSha256 = Convert.ToHexString(sha256.Hash!).ToLowerInvariant();
        if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(destinationPath);
            throw new InvalidOperationException(
                $"Empreinte SHA-256 invalide pour {Path.GetFileName(destinationPath)} : attendu {expectedSha256}, obtenu {actualSha256}. " +
                "Fichier supprimé, le téléchargement a peut-être été altéré en transit.");
        }
    }

    private static (string os, string architecture) GetCurrentPlatform()
    {
        var os = Environment.OSVersion.Platform switch
        {
            PlatformID.Win32NT => "windows",
            PlatformID.Unix when OperatingSystem.IsMacOS() => "mac",
            PlatformID.Unix => "linux",
            _ => throw new PlatformNotSupportedException("Plateforme non supportée pour le téléchargement de Java."),
        };

        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86-32",
            Architecture.Arm64 => "aarch64",
            _ => throw new PlatformNotSupportedException("Architecture processeur non supportée."),
        };

        return (os, architecture);
    }

    private sealed class AdoptiumAsset
    {
        [JsonPropertyName("binary")]
        public AdoptiumBinary Binary { get; set; } = null!;
    }

    private sealed class AdoptiumBinary
    {
        [JsonPropertyName("package")]
        public AdoptiumPackage Package { get; set; } = null!;
    }

    private sealed class AdoptiumPackage
    {
        [JsonPropertyName("link")]
        public string Link { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("checksum")]
        public string? Checksum { get; set; }
    }
}
