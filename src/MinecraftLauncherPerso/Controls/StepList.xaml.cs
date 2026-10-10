using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace MinecraftLauncherPerso.Controls;

/// <summary>
/// Liste des étapes du lancement (maquette « HUB — lancement en cours »). Vue pure : les lignes
/// (<c>StepItem</c>) et leurs états viennent de <c>LaunchViewModel</c> via <see cref="ItemsSource"/>.
/// </summary>
public partial class StepList : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(StepList),
        new PropertyMetadata(null, (d, e) => ((StepList)d).StepsControl.ItemsSource = (IEnumerable?)e.NewValue));

    public StepList()
    {
        InitializeComponent();
        AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((_, _) => RetryRequested?.Invoke(this, EventArgs.Empty)));
    }

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>Clic sur RÉESSAYER, affiché sur l'étape en échec.</summary>
    public event EventHandler? RetryRequested;
}
