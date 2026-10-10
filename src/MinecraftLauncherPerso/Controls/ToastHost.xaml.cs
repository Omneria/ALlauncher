using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace MinecraftLauncherPerso.Controls;

public enum ToastKind
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>Une notification affichée par <see cref="ToastHost"/>.</summary>
public sealed record ToastItem(string Title, string? Message, ToastKind Kind, Action? OnClick = null)
{
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    /// <summary>Icône de la maquette : coche (succès), éclair (avertissement), alerte (erreur), info.</summary>
    public string IconData => Kind switch
    {
        ToastKind.Success => "m5 12 4 4 10-10",
        ToastKind.Warning => "M13 2 4 14h7l-1 8 9-12h-7z",
        ToastKind.Error => "M12 8v4m0 4h.01M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0z",
        _ => "M12 11v5m0-8h.01M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0z",
    };
}

/// <summary>
/// Pile de notifications non bloquantes (v2.0.0), en bas à droite de la fenêtre. À appeler sur le
/// thread UI. Chaque toast disparaît seul après <see cref="DefaultDuration"/> ; au plus
/// <see cref="MaxVisible"/> à la fois (le plus ancien part le premier).
/// </summary>
public partial class ToastHost : UserControl
{
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(6);

    private const int MaxVisible = 4;

    private readonly ObservableCollection<ToastItem> _toasts = [];

    public ToastHost()
    {
        InitializeComponent();
        ToastsControl.ItemsSource = _toasts;
    }

    public void Show(string title, string? message = null, ToastKind kind = ToastKind.Info, TimeSpan? duration = null, Action? onClick = null)
    {
        var toast = new ToastItem(title, message, kind, onClick);
        _toasts.Add(toast);
        while (_toasts.Count > MaxVisible)
        {
            _toasts.RemoveAt(0);
        }

        var timer = new DispatcherTimer { Interval = duration ?? DefaultDuration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _toasts.Remove(toast);
        };
        timer.Start();
    }

    private void Toast_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ToastItem toast })
        {
            _toasts.Remove(toast);
            toast.OnClick?.Invoke();
        }
    }
}
