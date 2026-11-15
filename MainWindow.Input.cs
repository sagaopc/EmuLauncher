using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using EmuLauncher.Services;

namespace EmuLauncher;

/// <summary>
/// Controller and keyboard. Both end in <see cref="OnPad"/>, so every screen behaves the same
/// with either: directions move the focus to the nearest button, A presses it.
/// </summary>
public partial class MainWindow
{
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        _mouseInput = false;

        if (e.Key == Key.F11)
        {
            SetFullscreen(!_fullscreen);
            e.Handled = true;
            return;
        }

        if (Keyboard.FocusedElement is TextBoxBase) return;
        if (Keyboard.Modifiers != ModifierKeys.None) return; // Alt+F4, Alt+Tab and friends stay untouched

        PadButton button;
        switch (e.Key)
        {
            case Key.Up: button = PadButton.Up; break;
            case Key.Down: button = PadButton.Down; break;
            case Key.Left: button = PadButton.Left; break;
            case Key.Right: button = PadButton.Right; break;
            case Key.Enter:
            case Key.Space: button = PadButton.A; break;
            case Key.Escape:
            case Key.Back: button = PadButton.B; break;
            case Key.U:
            case Key.F5: button = PadButton.X; break;
            case Key.M:
            case Key.Apps: button = PadButton.Y; break;
            case Key.PageUp: button = PadButton.LeftShoulder; break;
            case Key.PageDown: button = PadButton.RightShoulder; break;
            default: return;
        }

        e.Handled = true;

        // A held arrow key keeps moving; a held Enter must not press the next button too.
        var direction = button is PadButton.Up or PadButton.Down or PadButton.Left or PadButton.Right;
        if (e.IsRepeat && !direction) return;

        OnPad(button);
    }

    private void OnPad(PadButton button)
    {
        _mouseInput = false;

        switch (button)
        {
            case PadButton.Up:
                Navigate(FocusNavigationDirection.Up);
                break;
            case PadButton.Down:
                Navigate(FocusNavigationDirection.Down);
                break;
            case PadButton.Left:
                Navigate(FocusNavigationDirection.Left);
                break;
            case PadButton.Right:
                Navigate(FocusNavigationDirection.Right);
                break;

            case PadButton.A:
                ActivateFocused();
                break;

            case PadButton.B:
            case PadButton.Back:
                GoBack();
                break;

            case PadButton.X:
                if (!OverlayOpen && _page == Screen.Home && _selected is { } toUpdate && toUpdate.IsInstalled)
                    RunUpdate(toUpdate);
                break;

            case PadButton.Y:
            case PadButton.Start:
                if (!OverlayOpen && _page == Screen.Home) ShowOptions();
                break;

            case PadButton.LeftShoulder:
                if (!OverlayOpen) ShowPage(_page == Screen.Home ? Screen.Settings : _page - 1);
                break;

            case PadButton.RightShoulder:
                if (!OverlayOpen) ShowPage(_page == Screen.Settings ? Screen.Home : _page + 1);
                break;
        }
    }

    /// <summary>Moves the focus to the nearest button in that direction, as the arrow keys would.</summary>
    private void Navigate(FocusNavigationDirection direction)
    {
        if (Keyboard.FocusedElement is ButtonBase { IsVisible: true, IsEnabled: true } focused)
        {
            focused.MoveFocus(new TraversalRequest(direction));
            return;
        }

        // Nothing sensible has the focus (first input, or it sat on something that went away).
        if (_dialogActive) FocusFirstDialogOption();
        else if (_progressActive) FocusElement(ProgressCancel);
        else FocusDefault();
    }

    private void ActivateFocused()
    {
        if (Keyboard.FocusedElement is ButtonBase { IsVisible: true, IsEnabled: true } button)
        {
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
            return;
        }

        Navigate(FocusNavigationDirection.Down);
    }

    private void GoBack()
    {
        if (_dialogActive)
        {
            CloseDialog(-1);
            return;
        }

        if (_progressActive)
        {
            CancelUpdate();
            return;
        }

        if (_page != Screen.Home) ShowPage(Screen.Home);
    }
}
