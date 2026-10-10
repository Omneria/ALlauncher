using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace MinecraftLauncherPerso.Controls;

public enum DialogButtonStyle
{
    /// <summary>Action principale (bouton cyan plein).</summary>
    Primary,

    /// <summary>Action secondaire (bouton à filet).</summary>
    Secondary,

    /// <summary>Action destructive ou renoncement (bouton à filet magenta).</summary>
    Danger,
}

/// <summary>Un bouton du dialogue : son libellé, l'identifiant renvoyé à l'appelant et son style.</summary>
public sealed record DialogButton(string Label, string Id, DialogButtonStyle Style = DialogButtonStyle.Secondary);

/// <summary>Contenu d'un dialogue. <paramref name="CancelId"/> est renvoyé par la touche Échap.</summary>
public sealed record DialogRequest(string Title, string Message, IReadOnlyList<DialogButton> Buttons, string CancelId);

/// <summary>
/// Dialogue modal intégré au launcher (v2.0.0). À utiliser sur le thread UI ; si un dialogue est
/// déjà affiché, le suivant attend son tour. Échap renvoie <see cref="DialogRequest.CancelId"/>,
/// Tab reste dans la carte.
/// </summary>
public partial class DialogHost : UserControl
{
    public const string ConfirmId = "confirm";
    public const string CancelId = "cancel";
    public const string OkId = "ok";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private TaskCompletionSource<string>? _pending;
    private string _cancelId = CancelId;

    public DialogHost()
    {
        InitializeComponent();
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetControlTabNavigation(this, KeyboardNavigationMode.Cycle);
    }

    /// <summary>Affiche le dialogue et renvoie l'identifiant du bouton choisi.</summary>
    public async Task<string> ShowAsync(DialogRequest request)
    {
        await _gate.WaitAsync();
        var previousFocus = Keyboard.FocusedElement;
        try
        {
            var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending = pending;
            _cancelId = request.CancelId;

            TitleText.Text = request.Title;
            MessageText.Text = request.Message;
            AutomationProperties.SetName(Card, request.Title);
            var focused = BuildButtons(request.Buttons, pending);

            Visibility = Visibility.Visible;
            if (focused is not null)
            {
                _ = Dispatcher.InvokeAsync(() => focused.Focus(), DispatcherPriority.Input);
            }

            return await pending.Task;
        }
        finally
        {
            Visibility = Visibility.Collapsed;
            ButtonsPanel.Children.Clear();
            _pending = null;
            _gate.Release();
            if (previousFocus is UIElement { IsVisible: true } element)
            {
                element.Focus();
            }
        }
    }

    /// <summary>Question à deux issues : vrai si le joueur confirme, faux s'il renonce (ou Échap).</summary>
    public async Task<bool> ConfirmAsync(string title, string message, string confirmLabel, string cancelLabel = "ANNULER")
    {
        var choice = await ShowAsync(new DialogRequest(
            title,
            message,
            [
                new DialogButton(confirmLabel, ConfirmId, DialogButtonStyle.Primary),
                new DialogButton(cancelLabel, CancelId, DialogButtonStyle.Secondary),
            ],
            CancelId));
        return choice == ConfirmId;
    }

    /// <summary>Message à acquitter : un seul bouton.</summary>
    public async Task AlertAsync(string title, string message, string okLabel = "OK") =>
        _ = await ShowAsync(new DialogRequest(
            title,
            message,
            [new DialogButton(okLabel, OkId, DialogButtonStyle.Primary)],
            OkId));

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _pending is not null)
        {
            e.Handled = true;
            _pending.TrySetResult(_cancelId);
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    /// <summary>
    /// Crée les boutons, le principal en dernier (à droite, là où l'œil attend la validation).
    /// Renvoie celui qui reçoit le focus et répond à Entrée : le principal, sinon le premier.
    /// </summary>
    private Button? BuildButtons(IReadOnlyList<DialogButton> buttons, TaskCompletionSource<string> pending)
    {
        ButtonsPanel.Children.Clear();
        Button? focused = null;

        foreach (var definition in buttons.OrderBy(button => button.Style == DialogButtonStyle.Primary ? 1 : 0))
        {
            var button = new Button
            {
                Content = definition.Label,
                Margin = new Thickness(12, 0, 0, 0),
                Style = (Style)FindResource(definition.Style switch
                {
                    DialogButtonStyle.Primary => "PrimaryButtonStyle",
                    DialogButtonStyle.Danger => "DangerButtonStyle",
                    _ => "OutlineButtonStyle",
                }),
            };
            button.Click += (_, _) => pending.TrySetResult(definition.Id);
            ButtonsPanel.Children.Add(button);

            if (definition.Style == DialogButtonStyle.Primary || focused is null)
            {
                focused = button;
            }
        }

        if (focused is not null)
        {
            focused.IsDefault = true;
        }

        return focused;
    }
}
