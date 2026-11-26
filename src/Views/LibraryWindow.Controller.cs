using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace GameShelf
{
    public partial class LibraryWindow
    {
        internal void HandleController(ControllerCommand command)
        {
            if (command == ControllerCommand.Back)
            {
                if (QuitLayer.IsVisible) DismissQuit();
                else if (panelOpen) ClosePanel();
                else if (MenuLayer.IsVisible) CloseMenu();
                else if (activeGroupId != null) NavigateToLibrary();
                return;
            }

            var focused = Keyboard.FocusedElement as FrameworkElement;
            if (command == ControllerCommand.Options)
            {
                if (!Shell.IsEnabled || MenuLayer.IsVisible || focused == null) return;
                if (focused.DataContext is Game game) ShowMenu(focused, null, AppOptions(game));
                else if (focused.DataContext is AppGroup group) ShowMenu(focused, null, GroupOptions(group));
                return;
            }
            if (command == ControllerCommand.Select)
            {
                if (focused is CheckBox toggle && toggle.IsEnabled) toggle.IsChecked = toggle.IsChecked != true;
                else if (focused is ButtonBase button && button.IsEnabled)
                    button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
                else if (focused is ComboBox picker && picker.IsEnabled) picker.IsDropDownOpen = !picker.IsDropDownOpen;
                else if (Shell.IsEnabled && !MenuLayer.IsVisible) visible.FirstOrDefault()?.Focus();
                return;
            }

            FocusNavigationDirection direction;
            switch (command)
            {
                case ControllerCommand.Up: direction = FocusNavigationDirection.Up; break;
                case ControllerCommand.Down: direction = FocusNavigationDirection.Down; break;
                case ControllerCommand.Left: direction = FocusNavigationDirection.Left; break;
                case ControllerCommand.Right: direction = FocusNavigationDirection.Right; break;
                default: return;
            }
            if (Shell.IsEnabled && !MenuLayer.IsVisible && MoveBetweenCards(direction)) return;
            if (focused != null && focused.MoveFocus(new TraversalRequest(direction)))
            {
                (Keyboard.FocusedElement as FrameworkElement)?.BringIntoView();
                return;
            }
            if (Shell.IsEnabled && !MenuLayer.IsVisible && !visible.Any(tile => tile.IsKeyboardFocusWithin))
                visible.FirstOrDefault()?.Focus();
        }

        private bool MoveBetweenCards(FocusNavigationDirection direction)
        {
            var cards = visible.Concat(Pinned.Children.OfType<Button>()).Where(button => button.IsVisible && button.IsEnabled).ToArray();
            var current = cards.FirstOrDefault(button => button.IsKeyboardFocusWithin);
            if (current == null) return false;
            bool horizontal = direction == FocusNavigationDirection.Left || direction == FocusNavigationDirection.Right;
            int sign = direction == FocusNavigationDirection.Left || direction == FocusNavigationDirection.Up ? -1 : 1;
            Point origin = CardCenter(current);
            var next = cards.Where(button => button != current).Select(button =>
            {
                Vector offset = CardCenter(button) - origin;
                return new { Card = button, Along = sign * (horizontal ? offset.X : offset.Y),
                    Across = Math.Abs(horizontal ? offset.Y : offset.X) };
            }).Where(candidate => candidate.Along > 8)
              .OrderBy(candidate => candidate.Along + candidate.Across * 2)
              .FirstOrDefault();
            if (next == null) return false;
            next.Card.Focus();
            next.Card.BringIntoView();
            return true;
        }

        private Point CardCenter(Button card) => card.TranslatePoint(new Point(card.ActualWidth / 2, card.ActualHeight / 2), Page);
    }
}
