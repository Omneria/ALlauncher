using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using MinecraftLauncherPerso.Models;

namespace MinecraftLauncherPerso.Services.Java;

/// <summary>
/// Vérifie qu'un Java 8 utilisable est disponible et, sinon, télécharge/installe une build
/// Temurin (Eclipse Adoptium) 8 portable dans le dossier de données du launcher.
///
/// Ordre de résolution :
///   1. Java 8 déjà installé par ce launcher (portable, sous %AppData%/MinecraftLauncherPerso/runtime/java8).
///   2. Java 8 déjà présent sur la machine (JAVA_HOME, PATH, dossiers d'installation courants, registre
///      Windows — un installeur Java officiel s'y enregistre toujours, même dans un dossier non standard).
///   3. Téléchargement + extraction d'une build Temurin 8 (JRE) via l'API Adoptium.
///
/// Forge 1.16.5 exige explicitement Java 8 : les JDK 11+ ne sont pas acceptés même s'ils sont
/// installés, d'où la vérification stricte de la version majeure (8) plutôt qu'un simple ">= 8".
/// </summary>
public sealed class JavaManager : IJavaManager
{
    private const int RequiredMajorVersion = 8;
    private static readonly Regex VersionRegex = new(@"version ""(?<version>[^""]+)""", RegexOptions.Compiled);

    private readonly string _runtimeRootDirectory;
    private readonly AdoptiumApiClient _adoptiumClient;

    public JavaManager(string? runtimeRootDirectory = null, HttpClient? httpClient = null)
    {
        _runtimeRootDirectory = runtimeRootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MinecraftLauncherPerso", "runtime");
        _adoptiumClient = new AdoptiumApiClient(httpClient ?? new HttpClient());
    }

    public async Task<string> EnsureJava8Async(IProgress<JavaSetupProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report(new JavaSetupProgress(JavaSetupStage.Checking, 0, "Recherche d'une installation Java 8..."));

        var bundledJava = FindBundledJava8();
        if (bundledJava is not null)
        {
            progress?.Report(new JavaSetupProgress(JavaSetupStage.Ready, 100, $"Java 8 portable déjà installé : {bundledJava}"));
            return bundledJava;
        }

        var systemJava = FindSystemJava8();
        if (systemJava is not null)
        {
            progress?.Report(new JavaSetupProgress(JavaSetupStage.Ready, 100, $"Java 8 détecté sur la machine : {systemJava}"));
            return systemJava;
        }

        progress?.Report(new JavaSetupProgress(JavaSetupStage.Downloading, 0, "Java 8 introuvable, téléchargement de Temurin 8..."));
        var archivePath = await DownloadTemurin8Async(progress, cancellationToken);

        try
        {
            progress?.Report(new JavaSetupProgress(JavaSetupStage.Extracting, 90, "Extraction de Java 8..."));
            var installedPath = ExtractRuntime(archivePath);

            progress?.Report(new JavaSetupProgress(JavaSetupStage.Ready, 100, $"Java 8 installé : {installedPath}"));
            return installedPath;
        }
        finally
        {
            File.Delete(archivePath);
        }
    }

    private string? FindBundledJava8()
    {
        var java8Root = Path.Combine(_runtimeRootDirectory, "java8");
        return FindJavaExecutableUnder(java8Root);
    }

    private static string? FindSystemJava8()
    {
        var candidates = new List<string>();

        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            candidates.Add(GetJavaExecutablePath(javaHome));
        }

        // Laisse le système résoudre "java" via le PATH.
        candidates.Add(OperatingSystem.IsWindows() ? "java.exe" : "java");

        foreach (var installRoot in GetCommonInstallRoots())
        {
            if (!Directory.Exists(installRoot))
            {
                continue;
            }

            foreach (var versionDir in Directory.GetDirectories(installRoot))
            {
                candidates.Add(GetJavaExecutablePath(versionDir));
            }
        }

        foreach (var registryJavaHome in GetRegistryJavaHomes())
        {
            candidates.Add(GetJavaExecutablePath(registryJavaHome));
        }

        return candidates.Distinct().Select(TryGetJavaVersion)
            .FirstOrDefault(info => info?.MajorVersion == RequiredMajorVersion)
            ?.ExecutablePath;
    }

    /// <summary>
    /// Lit les emplacements d'installation Java enregistrés dans le registre Windows par les
    /// installeurs officiels (Oracle, Eclipse Adoptium/Foundation) — plus fiable qu'un simple scan
    /// de dossiers puisque ça rattrape une installation faite dans un chemin non standard. Les deux
    /// vues du registre (64/32 bits) sont vérifiées, un installeur 32 bits n'apparaissant que sous
    /// WOW6432Node vu depuis un process 64 bits.
    /// </summary>
    private static IEnumerable<string> GetRegistryJavaHomes()
    {
        if (!OperatingSystem.IsWindows())
        {
            yield break;
        }

        string[] registryRoots =
        {
            @"SOFTWARE\JavaSoft\Java Runtime Environment",
            @"SOFTWARE\JavaSoft\JRE",
            @"SOFTWARE\JavaSoft\JDK",
            @"SOFTWARE\Eclipse Adoptium\JRE",
            @"SOFTWARE\Eclipse Adoptium\JDK",
            @"SOFTWARE\Eclipse Foundation\JDK",
        };

        foreach (var registryRoot in registryRoots)
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = baseKey.OpenSubKey(registryRoot);
                if (key is null)
                {
                    continue;
                }

                foreach (var versionName in key.GetSubKeyNames())
                {
                    using var versionKey = key.OpenSubKey(versionName);
                    if (versionKey?.GetValue("JavaHome") is string javaHome && !string.IsNullOrWhiteSpace(javaHome))
                    {
                        yield return javaHome;
                        continue;
                    }

                    // Eclipse Adoptium/Foundation stockent le chemin sous une sous-clé "hotspot\MSI".
                    using var msiKey = versionKey?.OpenSubKey(@"hotspot\MSI");
                    if (msiKey?.GetValue("Path") is string msiPath && !string.IsNullOrWhiteSpace(msiPath))
                    {
                        yield return msiPath;
                    }
                }
            }
        }
    }

    private static IEnumerable<string> GetCommonInstallRoots()
    {
        if (!OperatingSystem.IsWindows())
        {
            yield break;
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Path.Combine(programFiles, "Java");
        yield return Path.Combine(programFiles, "Eclipse Adoptium");
        yield return Path.Combine(programFiles, "AdoptOpenJDK");
    }

    private static string GetJavaExecutablePath(string root)
        => Path.Combine(root, "bin", OperatingSystem.IsWindows() ? "java.exe" : "java");

    /// <summary>Cherche récursivement un exécutable java valide (version 8) sous <paramref name="root"/>,
    /// sans supposer le nom exact du dossier extrait par l'archive Temurin (il varie selon la version).</summary>
    private static string? FindJavaExecutableUnder(string root)
    {
        if (!Directory.Exists(root))
        {
            return null;
        }

        var executableName = OperatingSystem.IsWindows() ? "java.exe" : "java";

        return Directory.EnumerateFiles(root, executableName, SearchOption.AllDirectories)
            .Select(TryGetJavaVersion)
            .FirstOrDefault(info => info?.MajorVersion == RequiredMajorVersion)
            ?.ExecutablePath;
    }

    public static JavaVersionInfo? TryGetJavaVersion(string javaPath)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = javaPath,
                Arguments = "-version",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            // "java -version" écrit sur stderr historiquement ; certaines distributions écrivent sur stdout.
            var output = process.StandardError.ReadToEnd();
            if (string.IsNullOrWhiteSpace(output))
            {
                output = process.StandardOutput.ReadToEnd();
            }

            if (!process.WaitForExit(5000))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            return ParseVersionOutput(javaPath, output);
        }
        catch
        {
            // Exécutable absent, non exécutable, ou pas un binaire Java valide : traité comme "non trouvé".
            return null;
        }
    }

    private static JavaVersionInfo? ParseVersionOutput(string javaPath, string output)
    {
        var match = VersionRegex.Match(output);
        if (!match.Success)
        {
            return null;
        }

        var versionString = match.Groups["version"].Value; // ex: "1.8.0_392" ou "17.0.9"
        return new JavaVersionInfo(javaPath, versionString, ParseMajorVersion(versionString));
    }

    private static int ParseMajorVersion(string versionString)
    {
        var parts = versionString.Split('.', '_', '-');
        if (parts.Length == 0)
        {
            return 0;
        }

        // Ancien schéma de version ("1.8.0_392") : la version majeure réelle est le 2e segment.
        if (parts[0] == "1" && parts.Length > 1 && int.TryParse(parts[1], out var legacyMajor))
        {
            return legacyMajor;
        }

        // Nouveau schéma de version (9+, ex. "17.0.9") : le 1er segment est la version majeure.
        return int.TryParse(parts[0], out var major) ? major : 0;
    }

    private async Task<string> DownloadTemurin8Async(IProgress<JavaSetupProgress>? progress, CancellationToken cancellationToken)
    {
        var downloadInfo = await _adoptiumClient.GetLatestJre8Async(cancellationToken);

        Directory.CreateDirectory(_runtimeRootDirectory);
        var archivePath = Path.Combine(_runtimeRootDirectory, downloadInfo.FileName);

        await _adoptiumClient.DownloadAsync(
            downloadInfo.DownloadUrl,
            archivePath,
            onProgress: fraction => progress?.Report(new JavaSetupProgress(
                JavaSetupStage.Downloading,
                fraction * 90,
                $"Téléchargement de Java 8... {fraction:P0}")),
            cancellationToken);

        return archivePath;
    }

    private string ExtractRuntime(string archivePath)
    {
        var extractRoot = Path.Combine(_runtimeRootDirectory, "java8");
        if (Directory.Exists(extractRoot))
        {
            Directory.Delete(extractRoot, recursive: true);
        }
        Directory.CreateDirectory(extractRoot);

        // Le launcher cible Windows (WPF) : Adoptium fournit un .zip pour cet OS.
        // Le support Linux/macOS nécessiterait de décompresser le .tar.gz retourné par l'API à la place.
        ZipFile.ExtractToDirectory(archivePath, extractRoot);

        return FindJavaExecutableUnder(extractRoot)
            ?? throw new InvalidOperationException(
                $"Extraction de Java 8 invalide : aucun exécutable java trouvé sous {extractRoot}.");
    }
}
