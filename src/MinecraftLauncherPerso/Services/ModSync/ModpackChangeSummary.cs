namespace MinecraftLauncherPerso.Services.ModSync;

/// <summary>
/// Lecture des lignes d'une entrée de changelog.json (« Ajouté : X », « Retiré : Y », « Mis à
/// jour : A → B ») en trois listes de noms, pour la tuile MODPACK du hub.
/// </summary>
public sealed record ModpackChangeSummary(IReadOnlyList<string> Added, IReadOnlyList<string> Removed, IReadOnlyList<string> Updated)
{
    public static ModpackChangeSummary From(IEnumerable<string> lines)
    {
        var added = new List<string>();
        var removed = new List<string>();
        var updated = new List<string>();
        foreach (var line in lines)
        {
            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var kind = line[..separator].Trim();
            var name = line[(separator + 1)..].Trim();
            if (name.Length == 0)
            {
                continue;
            }

            // « A → B » : le mod est renommé/remplacé, on garde son nouveau nom.
            if (kind.Equals("Mis à jour", StringComparison.OrdinalIgnoreCase))
            {
                updated.Add(name.Contains('→') ? name[(name.LastIndexOf('→') + 1)..].Trim() : name);
            }
            else if (kind.Equals("Ajouté", StringComparison.OrdinalIgnoreCase))
            {
                added.Add(name);
            }
            else if (kind.Equals("Retiré", StringComparison.OrdinalIgnoreCase))
            {
                removed.Add(name);
            }
        }

        return new ModpackChangeSummary(added, removed, updated);
    }
}
