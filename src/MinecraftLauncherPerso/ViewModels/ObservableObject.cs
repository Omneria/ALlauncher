using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MinecraftLauncherPerso.ViewModels;

/// <summary>Base des view-models : notification de changement de propriété (INotifyPropertyChanged).</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Affecte le champ et notifie si la valeur change. Renvoie vrai si elle a changé.</summary>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>Notifie les propriétés calculées qui dépendent d'une autre.</summary>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
