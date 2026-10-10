using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MinecraftLauncherPerso.Controls;

/// <summary>
/// Cadre à coins biseautés (haut gauche et bas droit coupés) de taille libre (v2.0.0). Remplace
/// les polygones à points fixes étirés par Stretch="Fill" : le biseau garde la même taille quel que
/// soit le contenu. Tout passe par OnRender (aucun élément enfant dont la géométrie réinvaliderait la
/// mesure), ce qui évite l'incident de la v1.9.2 sur les bannières.
/// </summary>
public class BevelBorder : Decorator
{
    public static readonly DependencyProperty BevelProperty = DependencyProperty.Register(
        nameof(Bevel), typeof(double), typeof(BevelBorder),
        new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(
        nameof(Background), typeof(Brush), typeof(BevelBorder),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BorderBrushProperty = DependencyProperty.Register(
        nameof(BorderBrush), typeof(Brush), typeof(BevelBorder),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BorderThicknessProperty = DependencyProperty.Register(
        nameof(BorderThickness), typeof(double), typeof(BevelBorder),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PaddingProperty = DependencyProperty.Register(
        nameof(Padding), typeof(Thickness), typeof(BevelBorder),
        new FrameworkPropertyMetadata(new Thickness(0), FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double Bevel
    {
        get => (double)GetValue(BevelProperty);
        set => SetValue(BevelProperty, value);
    }

    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public Brush? BorderBrush
    {
        get => (Brush?)GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    public double BorderThickness
    {
        get => (double)GetValue(BorderThicknessProperty);
        set => SetValue(BorderThicknessProperty, value);
    }

    public Thickness Padding
    {
        get => (Thickness)GetValue(PaddingProperty);
        set => SetValue(PaddingProperty, value);
    }

    private Thickness Inset => new(
        Padding.Left + BorderThickness,
        Padding.Top + BorderThickness,
        Padding.Right + BorderThickness,
        Padding.Bottom + BorderThickness);

    protected override Size MeasureOverride(Size constraint)
    {
        var inset = Inset;
        var horizontal = inset.Left + inset.Right;
        var vertical = inset.Top + inset.Bottom;
        if (Child is null)
        {
            return new Size(horizontal, vertical);
        }

        Child.Measure(new Size(Math.Max(0, constraint.Width - horizontal), Math.Max(0, constraint.Height - vertical)));
        return new Size(Child.DesiredSize.Width + horizontal, Child.DesiredSize.Height + vertical);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var inset = Inset;
        Child?.Arrange(new Rect(
            inset.Left,
            inset.Top,
            Math.Max(0, arrangeSize.Width - inset.Left - inset.Right),
            Math.Max(0, arrangeSize.Height - inset.Top - inset.Bottom)));
        return arrangeSize;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        // Le trait est centré sur le contour : on le rentre d'une demi-épaisseur pour rester dans le cadre.
        var half = BorderThickness / 2;
        var bevel = Math.Min(Bevel, Math.Min(width, height) / 2);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(half + bevel, half), isFilled: true, isClosed: true);
            context.LineTo(new Point(width - half, half), isStroked: true, isSmoothJoin: false);
            context.LineTo(new Point(width - half, height - half - bevel), isStroked: true, isSmoothJoin: false);
            context.LineTo(new Point(width - half - bevel, height - half), isStroked: true, isSmoothJoin: false);
            context.LineTo(new Point(half, height - half), isStroked: true, isSmoothJoin: false);
            context.LineTo(new Point(half, half + bevel), isStroked: true, isSmoothJoin: false);
        }

        geometry.Freeze();
        var pen = BorderBrush is not null && BorderThickness > 0 ? new Pen(BorderBrush, BorderThickness) { LineJoin = PenLineJoin.Miter } : null;
        drawingContext.DrawGeometry(Background, pen, geometry);
    }
}
