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
public sealed record ToastItem(string Title, string? Message, ToastKind Kind)
{
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);
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

    public void Show(string title, string? message = null, ToastKind kind = ToastKind.Info, TimeSpan? duration = null)
    {
        var toast = new ToastItem(title, message, kind);
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

    private void CloseToast_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ToastItem toast })
        {
            _toasts.Remove(toast);
        }
    }
}
