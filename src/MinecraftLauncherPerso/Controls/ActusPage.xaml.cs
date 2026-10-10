using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MinecraftLauncherPerso.Controls;

/// <summary>Une actu (news.txt) ou une mise à jour du pack (changelog.json), horodatée.</summary>
public sealed record ActusEntry(string DateLabel, string Content);

/// <summary>Une release GitHub du launcher. Visibilité du badge DERNIÈRE précalculée pour le binding.</summary>
public sealed record ReleaseEntry(string VersionTag, string Url, string DateLabel, Visibility LatestBadgeVisibility, IReadOnlyList<string> Notes);

public enum ActusSection
{
    News,
    Modpack,
    Launcher,
}

/// <summary>Page ACTUS : historique complet des actus, du modpack et des notes du launcher.</summary>
public partial class ActusPage : UserControl
{
    public ActusPage()
    {
        InitializeComponent();
    }

    /// <summary>Clic sur un numéro de version : le parent ouvre la release dans le navigateur.</summary>
    public event EventHandler<string>? ReleaseOpenRequested;

    public void SetNews(IReadOnlyList<ActusEntry> entries)
    {
        NewsList.ItemsSource = entries;
        NewsEmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetModpack(IReadOnlyList<ActusEntry> entries)
    {
        ModpackList.ItemsSource = entries;
        ModpackEmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetReleases(IReadOnlyList<ReleaseEntry> entries)
    {
        ReleasesList.ItemsSource = entries;
        ReleasesEmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ScrollTo(ActusSection section)
    {
        // Après le layout : une page tout juste rendue visible n'a pas encore de position.
        Dispatcher.BeginInvoke(() =>
        {
            FrameworkElement target = section switch
            {
                ActusSection.Modpack => ModpackSection,
                ActusSection.Launcher => LauncherSection,
                _ => NewsSection,
            };
            target.BringIntoView();
            if (section == ActusSection.News)
            {
                Scroller.ScrollToTop();
            }
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void ReleaseVersion_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url } && !string.IsNullOrEmpty(url))
        {
            ReleaseOpenRequested?.Invoke(this, url);
        }
    }
}
