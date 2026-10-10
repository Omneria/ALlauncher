namespace MinecraftLauncherPerso.Services.Status;

/// <summary>Détecte l'arrivée d'amis sur le serveur entre deux sondes (v2.0.0, page SERVEUR).</summary>
public static class FriendWatcher
{
    /// <summary>
    /// Amis présents maintenant et absents de la sonde précédente. Sans sonde précédente (premier
    /// passage), personne n'est « nouveau » : on ne notifie pas ceux déjà connectés au démarrage.
    /// </summary>
    public static IReadOnlyList<string> NewlyConnected(IReadOnlyCollection<string>? previous, IReadOnlyCollection<string>? current, IEnumerable<string> friends)
    {
        if (previous is null || current is null)
        {
            return [];
        }

        var friendSet = new HashSet<string>(friends.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim()), StringComparer.OrdinalIgnoreCase);
        return current
            .Where(name => friendSet.Contains(name) && !previous.Contains(name, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>« Alex, sam ,, » → [Alex, sam] : saisie libre des Paramètres.</summary>
    public static List<string> ParseNames(string? text) =>
        (text ?? "")
            .Split([',', ';', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
