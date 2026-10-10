using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MinecraftLauncherPerso.Services.Status;

namespace MinecraftLauncherPerso.Controls;

public partial class StatusPill : UserControl
{
    public StatusPill()
    {
        InitializeComponent();
        Set(StatusKind.Neutral, "Vérification...");
    }

    /// <summary>Couleur d'accent de l'état affiché (aussi utilisée pour les valeurs voisines, ex. « — »).</summary>
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
        Label.Text = text;
        ToolTip = tooltip;
        Cursor = tooltip is null ? null : Cursors.Help;
    }

    public void Set(ServerStatusPresentation presentation) => Set(presentation.Kind, presentation.Text, presentation.Tooltip);
}
