using System.Reflection;
using System.Text.RegularExpressions;
using MinecraftLauncherPerso.ViewModels;
using Xunit;

namespace MinecraftLauncherPerso.Tests;

/// <summary>
/// Garde-fou des liaisons XAML : un chemin mal orthographié dans « {Binding ...} » n'échoue pas à la
/// compilation, il n'affiche simplement rien. Ce test relit MainWindow.xaml et vérifie que chaque
/// chemin existe bien sur le view-model (ou sur l'élément de liste correspondant).
/// </summary>
public sealed partial class BindingPathTests
{
    private static string XamlPath(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "MinecraftLauncherPerso", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"{fileName} introuvable depuis {AppContext.BaseDirectory}.");
    }

    [GeneratedRegex(@"\{Binding\s+(?:Path=)?(?<path>[A-Za-z_][A-Za-z0-9_.]*)")]
    private static partial Regex BindingPattern();

    private static bool Resolves(Type type, string path)
    {
        foreach (var segment in path.Split('.'))
        {
            var property = type.GetProperty(segment, BindingFlags.Public | BindingFlags.Instance);
            if (property is null)
            {
                return false;
            }

            type = property.PropertyType;
        }

        return true;
    }

    [Fact]
    public void Chaque_liaison_de_la_fenetre_principale_pointe_vers_une_propriete_existante()
    {
        var xaml = File.ReadAllText(XamlPath("MainWindow.xaml"));
        var unresolved = new List<string>();

        foreach (Match match in BindingPattern().Matches(xaml))
        {
            var path = match.Groups["path"].Value;

            // Éléments de liste : TileLine (tuile MODPACK) et StepItem (liste d'étapes, dans StepList.xaml).
            if (Resolves(typeof(MainViewModel), path) || Resolves(typeof(TileLine), path))
            {
                continue;
            }

            unresolved.Add(path);
        }

        Assert.True(unresolved.Count == 0, "Liaisons sans propriété correspondante : " + string.Join(", ", unresolved.Distinct()));
    }

    [Fact]
    public void Les_liaisons_de_la_liste_detapes_pointent_vers_StepItem()
    {
        var xaml = File.ReadAllText(XamlPath(Path.Combine("Controls", "StepList.xaml")));
        var unresolved = BindingPattern().Matches(xaml)
            .Select(match => match.Groups["path"].Value)
            .Where(path => !Resolves(typeof(StepItem), path))
            .Distinct()
            .ToList();

        Assert.True(unresolved.Count == 0, "Liaisons sans propriété correspondante : " + string.Join(", ", unresolved));
    }

    [Fact]
    public void Le_nombre_de_liaisons_verifiees_nest_pas_nul()
    {
        // Évite qu'une regex cassée rende les tests ci-dessus vides de sens.
        var xaml = File.ReadAllText(XamlPath("MainWindow.xaml"));

        Assert.True(BindingPattern().Matches(xaml).Count > 40);
    }
}
