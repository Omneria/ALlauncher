using System.IO;

namespace MinecraftLauncherPerso.Services.Diagnostics;

/// <summary>
/// Vérification d'espace disque avant un téléchargement volumineux (modpack, mise à jour du
/// launcher) : auparavant, un disque plein ne se révélait qu'en pleine extraction/écriture, avec
/// une exception peu claire (IOException générique) plutôt qu'un message compréhensible en amont.
/// </summary>
public static class DiskSpaceChecker
{
    /// <summary>
    /// Vrai si le disque contenant <paramref name="path"/> a au moins <paramref name="requiredBytes"/>
    /// d'espace libre. Best-effort : si le lecteur ne peut pas être résolu (chemin invalide, lecteur
    /// réseau non standard...), retourne true plutôt que de bloquer un téléchargement sur une
    /// vérification qui n'a pas pu avoir lieu.
    /// </summary>
    public static bool HasEnoughFreeSpace(string path, long requiredBytes)
    {
        var freeBytes = GetAvailableFreeBytes(path);
        return freeBytes is null || freeBytes.Value >= requiredBytes;
    }

    public static long? GetAvailableFreeBytes(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }

            var drive = new DriveInfo(root);
            return drive.IsReady ? drive.AvailableFreeSpace : null;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static string FormatBytes(long bytes)
    {
        double value = bytes;
        string[] units = ["o", "Ko", "Mo", "Go"];
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.#} {units[unitIndex]}";
    }
}
