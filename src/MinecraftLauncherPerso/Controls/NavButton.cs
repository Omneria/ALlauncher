using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MinecraftLauncherPerso.Controls;

/// <summary>Item de la barre latérale : bouton à icône vectorielle, avec un état « page courante ».</summary>
public class NavButton : Button
{
    public static readonly DependencyProperty IconDataProperty = DependencyProperty.RegisterAttached(
        "IconData", typeof(Geometry), typeof(NavButton), new FrameworkPropertyMetadata(Geometry.Empty));

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(NavButton), new FrameworkPropertyMetadata(false));

    public Geometry IconData
    {
        get => (Geometry)GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public static Geometry GetIconData(DependencyObject element) => (Geometry)element.GetValue(IconDataProperty);

    public static void SetIconData(DependencyObject element, Geometry value) => element.SetValue(IconDataProperty, value);
}
