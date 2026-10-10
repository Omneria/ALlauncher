using System.Text.RegularExpressions;

namespace MinecraftLauncherPerso.ViewModels;

/// <summary>
/// Bannière de maintenance : première ligne de maintenance.txt en titre, ligne optionnelle « FIN: ... »
/// en « fin estimée », le reste en sous-titre atténué (permet un message d'une ligne comme un message
/// détaillé, sans format imposé côté VPS).
/// </summary>
public sealed partial class MaintenanceViewModel : ObservableObject
{
    private bool _isVisible;
    private string _title = "";
    private string _subtitle = "";
    private string _endTime = "";

    public bool IsVisible
    {
        get => _isVisible;
        private set => SetProperty(ref _isVisible, value);
    }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string Subtitle
    {
        get => _subtitle;
        private set
        {
            if (SetProperty(ref _subtitle, value))
            {
                OnPropertyChanged(nameof(HasSubtitle));
            }
        }
    }

    public bool HasSubtitle => Subtitle.Length > 0;

    public string EndTime
    {
        get => _endTime;
        private set
        {
            if (SetProperty(ref _endTime, value))
            {
                OnPropertyChanged(nameof(HasEndTime));
            }
        }
    }

    public bool HasEndTime => EndTime.Length > 0;

    /// <summary>Message du VPS, ou null s'il n'y a pas de maintenance (bannière masquée).</summary>
    public void Apply(string? message)
    {
        if (message is null)
        {
            IsVisible = false;
            return;
        }

        var (title, subtitle, endTime) = Parse(message);
        Title = title;
        Subtitle = subtitle;
        EndTime = endTime;
        IsVisible = true;
    }

    /// <summary>Découpe le message en titre, sous-titre et fin estimée (vide si absente).</summary>
    public static (string Title, string Subtitle, string EndTime) Parse(string message)
    {
        var rawLines = message.Replace("\r\n", "\n").Split('\n');
        string? endTime = null;
        var subtitleLines = new List<string>();
        for (var i = 1; i < rawLines.Length; i++)
        {
            var match = EndTimeLinePattern().Match(rawLines[i]);
            if (match.Success && endTime is null)
            {
                endTime = match.Groups["value"].Value;
            }
            else
            {
                subtitleLines.Add(rawLines[i]);
            }
        }

        return (rawLines[0].Trim(), string.Join("\n", subtitleLines).Trim(), endTime?.Trim() ?? "");
    }

    [GeneratedRegex(@"^\s*FIN\s*:\s*(?<value>.+?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex EndTimeLinePattern();
}
