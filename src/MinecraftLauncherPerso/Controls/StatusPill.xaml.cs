using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MinecraftLauncherPerso.Services.Status;

namespace MinecraftLauncherPerso.Controls;

public partial class StatusPill : UserControl
{
    public static readonly DependencyProperty CompactProperty = DependencyProperty.Register(
        nameof(Compact), typeof(bool), typeof(StatusPill), new PropertyMetadata(false, (d, _) => ((StatusPill)d).ApplySize()));

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(StatusKind), typeof(StatusPill), new PropertyMetadata(StatusKind.Neutral, (d, _) => ((StatusPill)d).Refresh()));

    public static readonly DependencyProperty LabelTextProperty = DependencyProperty.Register(
        nameof(LabelText), typeof(string), typeof(StatusPill), new PropertyMetadata("Vérification...", (d, _) => ((StatusPill)d).Refresh()));

    public static readonly DependencyProperty HintProperty = DependencyProperty.Register(
        nameof(Hint), typeof(string), typeof(StatusPill), new PropertyMetadata(null, (d, _) => ((StatusPill)d).Refresh()));

    public StatusPill()
    {
        InitializeComponent();
        Set(StatusKind.Neutral, "Vérification...");
    }

    /// <summary>Variante 9 px sans point (pastilles CONNECTÉ et DERNIÈRE de la maquette).</summary>
    public bool Compact
    {
        get => (bool)GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    /// <summary>État affiché (liaison de données) : couleur du point, du texte et du filet.</summary>
    public StatusKind Kind
    {
        get => (StatusKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>Libellé affiché (liaison de données).</summary>
    public string LabelText
    {
        get => (string)GetValue(LabelTextProperty);
        set => SetValue(LabelTextProperty, value);
    }

    /// <summary>Infobulle (liaison de données), null pour aucune.</summary>
    public string? Hint
    {
        get => (string?)GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    private void Refresh() => Set(Kind, LabelText, Hint);

    /// <summary>Couleur d'accent de l'état affiché (aussi utilisée pour les valeurs voisines).</summary>
    public Brush Accent { get; private set; } = Brushes.Gray;

    public void Set(StatusKind kind, string text, string? tooltip = null)
    {
        Accent = (Brush)FindResource(kind switch
        {
            StatusKind.Online => "CyanBrush",
            StatusKind.Warning => "AmberBrush",
            StatusKind.Offline => "MagentaBrush",
            _ => "InkDimBrush",
        });
        Dot.Fill = Accent;
        Label.Foreground = Accent;
        Label.Text = text;

        // Filet : couleur de l'état à 50 % d'opacité ; état neutre : filet de la charte.
        if (kind == StatusKind.Neutral)
        {
            Frame.BorderBrush = (Brush)FindResource("LineBrush");
        }
        else
        {
            var color = ((SolidColorBrush)Accent).Color;
            Frame.BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, color.R, color.G, color.B));
        }

        ToolTip = tooltip;
        Cursor = tooltip is null ? null : Cursors.Help;
        ApplySize();
    }

    public void Set(ServerStatusPresentation presentation) => Set(presentation.Kind, presentation.Text, presentation.Tooltip);

    private void ApplySize()
    {
        if (Frame is null)
        {
            return;
        }

        Frame.Padding = Compact ? new Thickness(6, 2, 6, 2) : new Thickness(10, 5, 10, 5);
        Label.FontSize = Compact ? 9 : 11;
        Dot.Visibility = Compact ? Visibility.Collapsed : Visibility.Visible;
    }
}
