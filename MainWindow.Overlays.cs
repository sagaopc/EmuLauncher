using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using EmuLauncher.Services;

namespace EmuLauncher;

/// <summary>
/// In-window dialogs. They are part of the scaled stage (not separate windows), so they are as
/// large as everything else on a television and work with the controller: one list of choices,
/// A picks, B cancels.
/// </summary>
public partial class MainWindow
{
    private sealed record DialogOption(string Label, string? Detail = null, string? Badge = null);

    private TaskCompletionSource<int>? _dialog;
    private bool _dialogActive;
    private bool _progressActive;
    private IInputElement? _focusBeforeOverlay;

    private bool OverlayOpen => _dialogActive || _progressActive;

    /// <summary>Shows a list of choices and returns the chosen index, or -1 when cancelled.</summary>
    private Task<int> DialogAsync(string title, string? body, IReadOnlyList<DialogOption> options, int focusIndex = 0)
    {
        // Only one dialog at a time: a second request cancels the first.
        var previous = _dialog;
        _dialog = null;
        previous?.TrySetResult(-1);

        var dialog = new TaskCompletionSource<int>();
        _dialog = dialog;

        DialogTitle.Text = title;
        DialogBody.Text = body ?? "";
        DialogBody.Visibility = string.IsNullOrWhiteSpace(body) ? Visibility.Collapsed : Visibility.Visible;

        DialogOptions.Children.Clear();
        for (var i = 0; i < options.Count; i++)
        {
            var button = new Button
            {
                Style = (Style)FindResource("OptionButton"),
                Content = BuildOptionContent(options[i]),
                Tag = i
            };
            button.Click += DialogOption_Click;
            DialogOptions.Children.Add(button);
        }

        _dialogActive = true;
        UpdateOverlay();

        var index = Math.Clamp(focusIndex, 0, Math.Max(0, options.Count - 1));
        if (DialogOptions.Children.Count > 0)
            FocusLater(DialogOptions.Children[index]);

        return dialog.Task;
    }

    private Task MessageAsync(string title, string body)
        => DialogAsync(title, body, new[] { new DialogOption("OK") });

    private async Task<bool> ConfirmAsync(string title, string body, string yes)
    {
        var choice = await DialogAsync(title, body, new[]
        {
            new DialogOption(yes),
            new DialogOption(T.Cancel)
        }, focusIndex: 1);

        return choice == 0;
    }

    private void DialogOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int index }) CloseDialog(index);
    }

    private void CloseDialog(int result)
    {
        var dialog = _dialog;
        if (dialog is null) return;

        _dialog = null;
        _dialogActive = false;
        UpdateOverlay();

        // Whoever awaited the dialog continues here, with the screen already back to normal.
        dialog.TrySetResult(result);
    }

    private void FocusFirstDialogOption()
    {
        if (DialogOptions.Children.Count > 0) FocusElement(DialogOptions.Children[0]);
    }

    private UIElement BuildOptionContent(DialogOption option)
    {
        var label = new TextBlock
        {
            Text = option.Label,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (option.Detail is null && option.Badge is null) return label;

        var panel = new DockPanel();

        if (option.Detail is not null)
        {
            var detail = new TextBlock
            {
                Text = option.Detail,
                FontSize = 15,
                Margin = new Thickness(18, 0, 0, 0),
                Foreground = (Brush)FindResource("TextSecondary"),
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(detail, Dock.Right);
            panel.Children.Add(detail);
        }

        if (option.Badge is not null)
        {
            var badge = new Border
            {
                Background = (Brush)FindResource("Accent"),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(9, 3, 9, 3),
                Margin = new Thickness(14, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = option.Badge,
                    FontSize = 13,
                    FontWeight = FontWeights.Medium,
                    Foreground = (Brush)FindResource("OnAccent")
                }
            };
            DockPanel.SetDock(badge, Dock.Right);
            panel.Children.Add(badge);
        }

        panel.Children.Add(label); // last child fills what is left, and trims
        return panel;
    }

    // -------------------------------------------------------------- progress

    private void ShowProgress(string title, string message)
    {
        ProgressTitle.Text = title;
        ProgressMessage.Text = message;
        InstallProgress.Value = 0;
        ProgressCancel.IsEnabled = true;

        _progressActive = true;
        UpdateOverlay();
    }

    private void ReportProgress(UpdateProgress progress)
    {
        if (!_progressActive) return;
        InstallProgress.Value = Math.Clamp(progress.Percent, 0, 100);
        ProgressMessage.Text = progress.Message;
    }

    private void HideProgress()
    {
        _progressActive = false;
        UpdateOverlay();
    }

    private void ProgressCancel_Click(object sender, RoutedEventArgs e) => CancelUpdate();

    private void CancelUpdate()
    {
        _updateCts?.Cancel();
        ProgressCancel.IsEnabled = false;
        ProgressMessage.Text = T.L("Cancelling…", "Cancelando…");
    }

    // --------------------------------------------------------------- overlay

    /// <summary>
    /// Shows the dialog, else the progress panel, else nothing. While either is up the shell is
    /// disabled, which keeps the focus (and so the controller) inside the overlay.
    /// </summary>
    private void UpdateOverlay()
    {
        DialogPanel.Visibility = _dialogActive ? Visibility.Visible : Visibility.Collapsed;
        ProgressPanel.Visibility = !_dialogActive && _progressActive ? Visibility.Visible : Visibility.Collapsed;

        var open = OverlayOpen;
        var wasOpen = OverlayLayer.Visibility == Visibility.Visible;

        if (open && !wasOpen)
        {
            _focusBeforeOverlay = Keyboard.FocusedElement;
            Shell.IsEnabled = false;
            OverlayLayer.Visibility = Visibility.Visible;
        }
        else if (!open && wasOpen)
        {
            OverlayLayer.Visibility = Visibility.Collapsed;
            Shell.IsEnabled = true;

            // Back to the button that opened the overlay, if it is still there.
            var back = _focusBeforeOverlay as ButtonBase;
            _focusBeforeOverlay = null;
            if (back is { IsVisible: true, IsEnabled: true }) FocusLater(back);
            else FocusDefault();
        }

        // During a download nothing is preselected, so a stray press of A cannot cancel it:
        // B cancels, or a direction moves to the Cancel button first. The window itself takes
        // the focus so the keyboard keeps working.
        if (open && !_dialogActive) Keyboard.Focus(this);

        UpdateHints();
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "";

        var mb = bytes / 1024d / 1024d;
        return mb >= 1
            ? mb.ToString("0.0", CultureInfo.InvariantCulture) + " MB"
            : (bytes / 1024d).ToString("0", CultureInfo.InvariantCulture) + " KB";
    }
}
