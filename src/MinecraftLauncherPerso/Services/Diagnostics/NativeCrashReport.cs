using System.IO;
using System.Text.RegularExpressions;

namespace MinecraftLauncherPerso.Services.Diagnostics;

/// <summary>
/// Rapport de crash natif de la JVM (<c>hs_err_pid&lt;n&gt;.log</c>). Quand le jeu se ferme sans
/// <c>crash-reports/</c> ni exception dans <c>latest.log</c> (pilote graphique, bug du compilateur JIT,
/// mémoire native...), ce fichier est la seule trace : le launcher le retrouve et en tire la cause
/// probable au lieu de demander au joueur de fouiller son disque. La JVM l'écrit dans le dossier de
/// travail du process sauf si <c>-XX:ErrorFile</c> le redirige, ce que GameLauncher fait depuis
/// la v2.2.0 (<see cref="ErrorFileArgument"/>).
/// </summary>
public sealed partial record NativeCrashReport(
    string Path,
    DateTime WrittenAt,
    string? ExceptionLine,
    string? ProblematicFrame,
    string? JavaVersion)
{
    /// <summary>Nom des rapports natifs de la JVM.</summary>
    public const string FileNamePattern = "hs_err_pid*.log";

    /// <summary>Lignes lues en tête de fichier : l'en-tête (cause, cadre fautif, version) tient dans les 40 premières.</summary>
    private const int HeaderLineCount = 60;

    /// <summary>Dossier (sous le dossier de jeu) où la JVM est priée d'écrire ses rapports.</summary>
    public static string ReportsDirectory(string gameDirectory) => System.IO.Path.Combine(gameDirectory, "crash-reports");

    /// <summary>Argument JVM qui range les rapports natifs à côté des crash-reports de Minecraft.</summary>
    public static string ErrorFileArgument(string gameDirectory) =>
        "-XX:ErrorFile=" + System.IO.Path.Combine(ReportsDirectory(gameDirectory), "hs_err_pid%p.log");

    /// <summary>
    /// Rapport natif le plus récent écrit à partir de <paramref name="notBefore"/> dans l'un des
    /// dossiers, ou null. Les erreurs d'accès (dossier absent, droits) sont ignorées : c'est un
    /// diagnostic de confort, il ne doit jamais empêcher d'afficher le reste.
    /// </summary>
    public static NativeCrashReport? FindLatest(IEnumerable<string> directories, DateTime notBefore)
    {
        NativeCrashReport? best = null;
        foreach (var directory in directories.Where(d => !string.IsNullOrWhiteSpace(d)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(directory, FileNamePattern, SearchOption.TopDirectoryOnly))
                {
                    var writtenAt = File.GetLastWriteTime(file);
                    if (writtenAt < notBefore || (best is not null && writtenAt <= best.WrittenAt))
                    {
                        continue;
                    }

                    if (Parse(file, writtenAt) is { } report)
                    {
                        best = report;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logger.Warn("NativeCrashReport", $"Dossier illisible ({directory}) : {ex.Message}");
            }
        }

        return best;
    }

    /// <summary>Lit l'en-tête d'un rapport natif ; null si le fichier est illisible.</summary>
    internal static NativeCrashReport? Parse(string path, DateTime writtenAt)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var header = new List<string>(HeaderLineCount);
            while (header.Count < HeaderLineCount && reader.ReadLine() is { } line)
            {
                header.Add(line);
            }

            return ParseHeader(path, writtenAt, header);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Warn("NativeCrashReport", $"Rapport illisible ({path}) : {ex.Message}");
            return null;
        }
    }

    internal static NativeCrashReport ParseHeader(string path, DateTime writtenAt, IReadOnlyList<string> header)
    {
        string? exception = null;
        string? frame = null;
        string? javaVersion = null;

        for (var i = 0; i < header.Count; i++)
        {
            var line = header[i];
            if (exception is null && ExceptionPattern().Match(line) is { Success: true } match)
            {
                exception = match.Groups["text"].Value.Trim();
            }
            else if (frame is null && line.StartsWith("# Problematic frame:", StringComparison.Ordinal) && i + 1 < header.Count)
            {
                frame = header[i + 1].TrimStart('#', ' ').Trim();
            }
            else if (javaVersion is null && line.StartsWith("# JRE version:", StringComparison.Ordinal))
            {
                javaVersion = line["# JRE version:".Length..].Trim();
            }
        }

        return new NativeCrashReport(path, writtenAt, exception, string.IsNullOrEmpty(frame) ? null : frame, javaVersion);
    }

    /// <summary>
    /// Explication en une ou deux phrases, pour le joueur. Ne conclut que ce que le rapport permet
    /// d'affirmer : la nature de la zone fautive (code compilé par le JIT, bibliothèque native, JVM),
    /// jamais un coupable certain.
    /// </summary>
    public string Describe()
    {
        var cause = ExceptionLine is null ? "Crash natif de la machine virtuelle Java" : $"Crash natif de la machine virtuelle Java ({ExceptionLine})";
        var frame = ProblematicFrame;

        if (frame is null)
        {
            return $"{cause}. Le rapport {System.IO.Path.GetFileName(Path)} ne nomme pas de zone fautive.";
        }

        if (JitFramePattern().Match(frame) is { Success: true } jit)
        {
            return $"{cause}, dans du code Java compilé à la volée ({jit.Groups["method"].Value}). " +
                "C'est le plus souvent un bug de la machine virtuelle : mettre Java à jour (le launcher en " +
                "installe un récent) et, si ça persiste, vérifier que le mod concerné est à jour.";
        }

        if (NativeLibraryPattern().Match(frame) is { Success: true } native)
        {
            var library = native.Groups["lib"].Value;
            return $"{cause}, dans la bibliothèque native « {library} ». " +
                (LooksLikeGraphicsDriver(library)
                    ? "Cela ressemble à un problème de pilote graphique : mettre à jour le pilote, et choisir la carte graphique principale pour javaw.exe dans les paramètres graphiques de Windows."
                    : "Une bibliothèque du système ou d'un pilote en est la cause probable : la mettre à jour ou la désinstaller si elle est facultative.");
        }

        return $"{cause}. Zone fautive : {frame}.";
    }

    private static bool LooksLikeGraphicsDriver(string library) =>
        library.StartsWith("ig", StringComparison.OrdinalIgnoreCase)
        || library.StartsWith("atio", StringComparison.OrdinalIgnoreCase)
        || library.StartsWith("amd", StringComparison.OrdinalIgnoreCase)
        || library.StartsWith("nv", StringComparison.OrdinalIgnoreCase)
        || library.Contains("opengl", StringComparison.OrdinalIgnoreCase)
        || library.Contains("glfw", StringComparison.OrdinalIgnoreCase);

    // "#  EXCEPTION_ACCESS_VIOLATION (0xc0000005) at pc=..., pid=..., tid=..." ou "#  SIGSEGV (0xb) at ..."
    // ou "#  Internal Error (...)". On garde le nom et le code, pas l'adresse (propre à chaque lancement).
    [GeneratedRegex(@"^#\s+(?<text>(?:EXCEPTION_[A-Z_]+|SIG[A-Z]+|Internal Error)[^,]*?)(?:\s+at\s|,|$)")]
    private static partial Regex ExceptionPattern();

    // "J 29020 c2 net.mezzdev.config.schema.ConfigSchema.loadIfNeededWithoutNotifying()Lnet/...; mezz_config@0.6.3 (614 bytes) @ 0x..."
    [GeneratedRegex(@"^J\s+\d+\s+c[12]\s+(?<method>[\w$.<>]+)")]
    private static partial Regex JitFramePattern();

    // "C  [atio6axx.dll+0x1234]" ou "C  [jvm.dll+0x...]"
    [GeneratedRegex(@"^[CV]\s+\[(?<lib>[^+\]]+)")]
    private static partial Regex NativeLibraryPattern();
}
