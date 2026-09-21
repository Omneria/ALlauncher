using System.IO;

namespace MinecraftLauncherPerso.Services.Diagnostics;

/// <summary>
/// Journal fichier minimal (une ligne texte par entrée, %AppData%/MinecraftLauncherPerso/launcher.log) :
/// sert de filet pour les erreurs volontairement avalées ailleurs dans le launcher (elles ne
/// doivent jamais bloquer l'utilisateur — connexion silencieuse en cache, check de mise à jour,
/// détection Java...), qui étaient jusqu'ici invisibles pour de vrai, sans aucun moyen de
/// diagnostiquer à distance un échec répété sans relire le code. Best-effort : une erreur
/// d'écriture du log lui-même est silencieusement ignorée, pour ne jamais devenir une nouvelle
/// source de plantage.
/// </summary>
public static class Logger
{
    private static readonly string LogFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MinecraftLauncherPerso", "launcher.log");

    private static readonly object WriteLock = new();

    // Au-delà, le fichier est repris à zéro plutôt que de grossir indéfiniment sur une machine
    // qu'on ne surveille jamais activement.
    private const long MaxSizeBytes = 5 * 1024 * 1024;

    public static void Warn(string source, string message) => Write("WARN", source, message);

    public static void Error(string source, string message, Exception? exception = null)
    {
        var full = exception is null ? message : $"{message} :: {exception}";
        Write("ERROR", source, full);
    }

    private static void Write(string level, string source, string message)
    {
        try
        {
            var directory = Path.GetDirectoryName(LogFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {source}: {message}{Environment.NewLine}";

            lock (WriteLock)
            {
                if (File.Exists(LogFilePath) && new FileInfo(LogFilePath).Length > MaxSizeBytes)
                {
                    File.Delete(LogFilePath);
                }

                File.AppendAllText(LogFilePath, line);
            }
        }
        catch (Exception)
        {
            // Le logging ne doit jamais devenir lui-même une source d'erreur visible.
        }
    }
}
