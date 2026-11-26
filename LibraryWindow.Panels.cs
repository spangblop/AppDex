using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace GameShelf
{
    public partial class LibraryWindow
    {
        private sealed class MenuChoice
        {
            internal readonly string Label;
            internal readonly Action Select;
            internal MenuChoice(string label, Action select) { Label = label; Select = select; }
        }

        private IInputElement panelFocus;
        private IInputElement menuFocus;
        private IInputElement quitFocus;
        private int panelTransition;
        private int launchNoticeVersion;
        private bool panelOpen;

        private DoubleAnimation Motion(double from, double to, int milliseconds)
        {
            return new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds * preferences.Appearance.Motion)) {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop
            };
        }

        private void ShowErrorToast(string message)
        {
            int version = ++launchNoticeVersion;
            LaunchNotice.BeginAnimation(OpacityProperty, null);
            LaunchNoticeText.Text = message;
            LaunchNotice.Opacity = 1;
            LaunchNotice.Visibility = Visibility.Visible;
            var fade = Motion(1, 0, 420);
            fade.BeginTime = TimeSpan.FromSeconds(4);
            fade.Completed += delegate
            {
                if (version != launchNoticeVersion) return;
                LaunchNotice.Visibility = Visibility.Collapsed;
                LaunchNoticeText.Text = "";
            };
            LaunchNotice.BeginAnimation(OpacityProperty, fade);
            PlayUiSound(UiCue.Notice);
        }

        private void AnimateOffset(TranslateTransform transform, DependencyProperty property, double from, double to, int milliseconds)
        {
            transform.BeginAnimation(property, null);
            transform.SetValue(property, to);
            if (preferences.Appearance.Motion > 0 && Math.Abs(from - to) > .25)
                transform.BeginAnimation(property, Motion(from, to, milliseconds));
        }

        private void ShowPanel(string title, FrameworkElement content, UIElement initialFocus = null)
        {
            HighlightAppearance(AppearanceTarget.None);
            CloseMenu();
            EndTileDrag(false);
            if (!panelOpen) panelFocus = Keyboard.FocusedElement;
            panelTransition++;
            panelOpen = true;
            DrawerTitle.Text = title;
            DrawerContent.Content = content;
            PanelLayer.Visibility = Visibility.Visible;
            UpdateInputAvailability();
            double scrim = content is SettingsPane ? .42 : 1;
            PanelScrim.Opacity = scrim;
            PanelScrim.BeginAnimation(OpacityProperty, Motion(0, scrim, 190));
            AnimateOffset(DrawerMotion, TranslateTransform.XProperty, Math.Min(Drawer.Width, 145), 0, 235);
            Drawer.Opacity = 1;
            Drawer.BeginAnimation(OpacityProperty, Motion(.55, 1, 195));
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(delegate
            {
                if (panelOpen && ReferenceEquals(DrawerContent.Content, content)) (initialFocus ?? CloseDrawerButton).Focus();
            }));
        }

        private void ClosePanel()
        {
            if (!panelOpen) return;
            HighlightAppearance(AppearanceTarget.None);
            var settings = DrawerContent.Content as SettingsPane;
            if (settings != null) settings.CommitAppearance();
            panelOpen = false;
            int transition = ++panelTransition;
            double scrim = PanelScrim.Opacity;
            PanelScrim.Opacity = 0;
            PanelScrim.BeginAnimation(OpacityProperty, Motion(scrim, 0, 155));
            double from = DrawerMotion.X;
            DrawerMotion.BeginAnimation(TranslateTransform.XProperty, null);
            var motion = Motion(from, Math.Min(Drawer.Width, 150), 155);
            motion.Completed += delegate
            {
                if (transition != panelTransition) return;
                PanelLayer.Visibility = Visibility.Collapsed;
                DrawerContent.Content = null;
                DrawerMotion.X = 0;
                UpdateInputAvailability();
                if (!QuitLayer.IsVisible) RestoreFocus(panelFocus);
                panelFocus = null;
            };
            DrawerMotion.BeginAnimation(TranslateTransform.XProperty, motion);
            Drawer.Opacity = 0;
            Drawer.BeginAnimation(OpacityProperty, Motion(1, 0, 155));
        }

        private void RestoreFocus(IInputElement target)
        {
            var element = target as FrameworkElement;
            if (element == null || !element.IsVisible || !element.IsEnabled || !element.Focus()) SearchBox.Focus();
        }

        private void CloseDrawer(object sender, RoutedEventArgs e) { ClosePanel(); }

        private void UpdateInputAvailability()
        {
            bool quitOpen = QuitLayer.Visibility == Visibility.Visible;
            Shell.IsEnabled = !quitOpen && PanelLayer.Visibility != Visibility.Visible;
            PanelLayer.IsEnabled = !quitOpen;
        }

        internal void ConfirmQuit()
        {
            if (QuitLayer.Visibility == Visibility.Visible) return;
            HighlightAppearance(AppearanceTarget.None);
            CloseMenu();
            EndTileDrag(false);
            quitFocus = Keyboard.FocusedElement;
            QuitLayer.Visibility = Visibility.Visible;
            UpdateInputAvailability();
            KeepOpenButton.Focus();
        }

        private void DismissQuit()
        {
            QuitLayer.Visibility = Visibility.Collapsed;
            UpdateInputAvailability();
            RestoreFocus(quitFocus);
            quitFocus = null;
        }

        private void KeepOpen(object sender, RoutedEventArgs e) { DismissQuit(); }

        private void AcceptQuit(object sender, RoutedEventArgs e)
        {
            if (QuitLayer.Visibility == Visibility.Visible) QuitRequested?.Invoke();
        }

        private void DismissPanelOnBackdrop(object sender, MouseButtonEventArgs e)
        {
            ClosePanel();
            e.Handled = true;
        }

        private void AttachOptions(Button button, Func<MenuChoice[]> choices)
        {
            button.PreviewMouseRightButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                ShowMenu(button, e.GetPosition(Page), choices());
                e.Handled = true;
            };
            button.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Apps && (e.Key != Key.F10 || Keyboard.Modifiers != ModifierKeys.Shift)) return;
                ShowMenu(button, null, choices());
                e.Handled = true;
            };
        }

        private void ShowMenu(FrameworkElement anchor, Point? cursor, params MenuChoice[] choices)
        {
            var options = new StackPanel();
            foreach (var choice in choices)
            {
                var line = SmallButton(choice.Label);
                line.HorizontalContentAlignment = HorizontalAlignment.Left;
                line.Margin = new Thickness(0, 0, 0, 3);
                line.Click += delegate { CloseMenu(); choice.Select(); };
                options.Children.Add(line);
            }
            ShowPopover(anchor, cursor, options, options.Children.Count == 0 ? null : options.Children[0]);
        }

        private void PreviewPickerClick(object sender, RoutedEventArgs e) { ShowPreviewNotice((Button)sender); }

        private void ShowPreviewNotice(Button anchor)
        {
            var message = new TextBlock { Text = "This don't work yet", Margin = new Thickness(8, 6, 8, 9),
                TextWrapping = TextWrapping.Wrap, MaxWidth = 190 };
            var ok = SmallButton("OK");
            ok.HorizontalAlignment = HorizontalAlignment.Right;
            ok.Click += delegate { CloseMenu(); };
            ShowPopover(anchor, null, new StackPanel { Children = { message, ok } }, ok);
        }

        private void ShowPopover(FrameworkElement anchor, Point? cursor, FrameworkElement content, UIElement initialFocus)
        {
            CloseMenu();
            menuFocus = Keyboard.FocusedElement;
            var menu = new Border { CornerRadius = new CornerRadius(preferences.Appearance.ControlCorner), Padding = new Thickness(5),
                BorderThickness = new Thickness(1), Child = content, MinWidth = 174 };
            menu.SetResourceReference(Border.BackgroundProperty, "AppPanel");
            menu.SetResourceReference(Border.BorderBrushProperty, "AppEdge");
            menu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Point location = cursor ?? anchor.TranslatePoint(new Point(0, anchor.ActualHeight + 5), Page);
            Canvas.SetLeft(menu, Math.Max(2, Math.Min(location.X, Page.ActualWidth - menu.DesiredSize.Width - 5)));
            Canvas.SetTop(menu, Math.Max(2, Math.Min(location.Y, Page.ActualHeight - menu.DesiredSize.Height - 5)));
            MenuLayer.Children.Add(menu);
            MenuLayer.Visibility = Visibility.Visible;
            menu.BeginAnimation(OpacityProperty, Motion(.2, 1, 150));
            initialFocus?.Focus();
        }

        private void CloseMenu()
        {
            if (MenuLayer.Visibility != Visibility.Visible) return;
            MenuLayer.Children.Clear();
            MenuLayer.Visibility = Visibility.Collapsed;
            RestoreFocus(menuFocus);
            menuFocus = null;
        }

        private void DismissMenuOnBackdrop(object sender, MouseButtonEventArgs e)
        {
            if (!ReferenceEquals(e.OriginalSource, MenuLayer)) return;
            e.Handled = true;
            CloseMenu();
        }

        private void ShowImportMenu(object sender, RoutedEventArgs e)
        {
            ShowMenu((FrameworkElement)sender, null,
                new MenuChoice("Import Steam library", () => OpenImport("Steam")),
                new MenuChoice("Import Epic Games library", () => OpenImport("Epic")));
        }

        private void ShowAddMenu(object sender, RoutedEventArgs e)
        {
            ShowMenu((FrameworkElement)sender, null, new MenuChoice("Add app manually", () => Edit(null)),
                new MenuChoice("Scan open apps", () => OpenImport("Running")));
        }
    }
}
