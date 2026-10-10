using System.Windows;
using System.Windows.Controls;

namespace MinecraftLauncherPerso.Controls;

/// <summary>
/// Sélecteur de modèle de contenu : applique <see cref="StringTemplate"/> (texte à crénage, voir
/// <see cref="TrackedText"/>) quand le contenu d'un bouton est une chaîne, et laisse le rendu par
/// défaut pour tout autre contenu (icône, panneau...).
/// </summary>
public class StringContentSelector : DataTemplateSelector
{
    public DataTemplate? StringTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object? item, DependencyObject container) =>
        item is string ? StringTemplate : null;
}
