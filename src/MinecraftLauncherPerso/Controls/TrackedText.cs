using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;

namespace MinecraftLauncherPerso.Controls;

/// <summary>
/// Texte sur une ligne avec crénage réglable (« letter-spacing » CSS, en em), que TextBlock ne sait
/// pas faire. Dessiné glyphe par glyphe : chaque caractère est suivi de <see cref="Tracking"/> × la
/// taille de police, comme en CSS (espace final compris, pour des alignements identiques à la
/// maquette). Police, taille, graisse et couleur sont héritées comme pour un TextBlock.
/// </summary>
public class TrackedText : FrameworkElement
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(TrackedText),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackingProperty = DependencyProperty.Register(
        nameof(Tracking), typeof(double), typeof(TrackedText),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrimProperty = DependencyProperty.Register(
        nameof(Trim), typeof(bool), typeof(TrackedText),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(typeof(TrackedText));
    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(typeof(TrackedText));
    public static readonly DependencyProperty FontWeightProperty = TextElement.FontWeightProperty.AddOwner(typeof(TrackedText));
    public static readonly DependencyProperty FontStyleProperty = TextElement.FontStyleProperty.AddOwner(typeof(TrackedText));
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(TrackedText));

    static TrackedText()
    {
        // Même réaction que TextBlock : un changement de police ou de couleur hérité redessine.
        FontFamilyProperty.OverrideMetadata(typeof(TrackedText), new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
        FontSizeProperty.OverrideMetadata(typeof(TrackedText), new FrameworkPropertyMetadata(SystemFonts.MessageFontSize, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
        FontWeightProperty.OverrideMetadata(typeof(TrackedText), new FrameworkPropertyMetadata(FontWeights.Normal, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
        FontStyleProperty.OverrideMetadata(typeof(TrackedText), new FrameworkPropertyMetadata(FontStyles.Normal, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
        ForegroundProperty.OverrideMetadata(typeof(TrackedText), new FrameworkPropertyMetadata(SystemColors.ControlTextBrush, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value ?? "");
    }

    /// <summary>Espacement ajouté après chaque caractère, en multiples de la taille de police (0,18 = 0,18 em).</summary>
    public double Tracking
    {
        get => (double)GetValue(TrackingProperty);
        set => SetValue(TrackingProperty, value);
    }

    /// <summary>Coupe le texte par « … » quand la place manque (équivalent de TextTrimming).</summary>
    public bool Trim
    {
        get => (bool)GetValue(TrimProperty);
        set => SetValue(TrimProperty, value);
    }

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontWeight FontWeight
    {
        get => (FontWeight)GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    public FontStyle FontStyle
    {
        get => (FontStyle)GetValue(FontStyleProperty);
        set => SetValue(FontStyleProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    private GlyphTypeface? ResolveTypeface()
    {
        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretches.Normal);
        return typeface.TryGetGlyphTypeface(out var glyphTypeface) ? glyphTypeface : null;
    }

    /// <summary>Indices de glyphes et avancées (en unités de police × taille) de la chaîne donnée.</summary>
    private (ushort[] Indices, double[] Advances)? Shape(GlyphTypeface glyphs, string text)
    {
        var indices = new ushort[text.Length];
        var advances = new double[text.Length];
        var step = Tracking * FontSize;
        for (var i = 0; i < text.Length; i++)
        {
            if (!glyphs.CharacterToGlyphMap.TryGetValue(text[i], out var index))
            {
                // Caractère absent de la police (ex. « → ») : glyphe .notdef, largeur de la police.
                index = 0;
            }

            indices[i] = index;
            advances[i] = (glyphs.AdvanceWidths[index] * FontSize) + step;
        }

        return (indices, advances);
    }

    /// <summary>Texte à dessiner : tronqué avec « … » si <see cref="Trim"/> et que la largeur disponible est dépassée.</summary>
    private string Fit(GlyphTypeface glyphs, string text, double maxWidth)
    {
        if (!Trim || double.IsInfinity(maxWidth))
        {
            return text;
        }

        var shaped = Shape(glyphs, text);
        if (shaped is null || shaped.Value.Advances.Sum() <= maxWidth)
        {
            return text;
        }

        var kept = text.Length;
        while (kept > 0 && Shape(glyphs, text[..kept] + "…")!.Value.Advances.Sum() > maxWidth)
        {
            kept--;
        }

        return kept == 0 ? "" : text[..kept] + "…";
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var glyphs = ResolveTypeface();
        if (glyphs is null || string.IsNullOrEmpty(Text))
        {
            return new Size(0, FontSize * 1.3);
        }

        var shown = Fit(glyphs, Text, availableSize.Width);
        var width = Shape(glyphs, shown)!.Value.Advances.Sum();
        var height = glyphs.Height * FontSize;
        return new Size(Math.Min(width, double.IsInfinity(availableSize.Width) ? width : availableSize.Width), height);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var glyphs = ResolveTypeface();
        if (glyphs is null || string.IsNullOrEmpty(Text))
        {
            return;
        }

        var shown = Fit(glyphs, Text, ActualWidth);
        if (shown.Length == 0)
        {
            return;
        }

        var (indices, advances) = Shape(glyphs, shown)!.Value;
        var origin = new Point(0, glyphs.Baseline * FontSize);
        var run = new GlyphRun(
            glyphs,
            0,
            false,
            FontSize,
            (float)VisualTreeHelper.GetDpi(this).PixelsPerDip,
            indices,
            origin,
            advances,
            null,
            shown.Select(c => c).ToArray(),
            null,
            null,
            null,
            XmlLanguage.GetLanguage(CultureInfo.CurrentUICulture.IetfLanguageTag));
        drawingContext.DrawGlyphRun(Foreground, run);
    }
}
