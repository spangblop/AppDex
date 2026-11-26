using System;
using System.Windows;
using System.Windows.Controls;

namespace GameShelf
{
    public partial class SettingsPane : UserControl
    {
        private readonly StartupRegistration startup;
        private readonly LibraryPreferences preferences;
        private readonly Action save;
        private readonly AppearanceEditor editor;
        private bool refreshing;
        internal event Action Closed;
        internal event Action<AppearanceTarget> HighlightChanged;
        internal event Action LibrarySyncChanged;
        internal event Action UiSoundsChanged;
        internal Func<int, int, string> RegisterQuickShortcut;

        internal SettingsPane(StartupRegistration startup, LibraryPreferences preferences, Action save, Action apply, string dataDirectory)
        {
            this.startup = startup;
            this.preferences = preferences;
            this.save = save;
            refreshing = true;
            InitializeComponent();
            ConfirmLaunchToggle.IsChecked = preferences.ConfirmLaunch;
            AfterLaunchPicker.SelectedIndex = preferences.QuitAfterLaunch ? 2 : preferences.MinimizeAfterLaunch ? 1 : 0;
            QuickModifiers.ItemsSource = LaunchHotkeys.ModifierLabels;
            QuickKey.ItemsSource = LaunchHotkeys.Keys;
            QuickKey.SelectedIndex = Math.Max(0, Array.IndexOf(LaunchHotkeys.Keys, (char)(preferences.QuickLaunchKey ?? LaunchHotkeys.DefaultKey)));
            QuickModifiers.SelectedIndex = Math.Max(0, Array.IndexOf(LaunchHotkeys.ModifierValues,
                preferences.QuickLaunchModifiers ?? LaunchHotkeys.DefaultModifiers));
            OnClosePicker.SelectedIndex = preferences.QuitOnWindowClose ? 1 : 0;
            StartupModePicker.SelectedIndex = preferences.StartupInTray ? 1 : 0;
            UiSoundsToggle.IsChecked = preferences.UiSoundsEnabled;
            SteamSyncToggle.IsChecked = preferences.SyncSteam;
            EpicSyncToggle.IsChecked = preferences.SyncEpic;
            if (AppBuild.IsPreview) LibrarySyncCard.Visibility = Visibility.Collapsed;
            editor = new AppearanceEditor(preferences, apply, save, dataDirectory);
            editor.HighlightChanged += target => HighlightChanged?.Invoke(target);
            AppearanceHost.Content = editor;
            RefreshStartup();
            UpdateCloseWarnings();
        }

        internal void CommitAppearance() { editor.Commit(); }

        private void ChangeQuickModifiers(object sender, RoutedEventArgs e)
        {
            QuickKey.IsEnabled = QuickModifiers.SelectedIndex > 0;
        }

        private void SaveQuickShortcut(object sender, RoutedEventArgs e)
        {
            int modifiers = LaunchHotkeys.ModifierValues[Math.Max(0, QuickModifiers.SelectedIndex)];
            int key = modifiers == 0 ? 0 : (char)QuickKey.SelectedItem;
            string error = RegisterQuickShortcut?.Invoke(modifiers, key);
            QuickShortcutStatus.SetResourceReference(TextBlock.ForegroundProperty, error == null ? "AppMuted" : "AppDanger");
            QuickShortcutStatus.Text = error ?? (modifiers == 0 ? "Quick Launch shortcut turned off." : "Shortcut saved.");
            QuickShortcutStatus.Visibility = Visibility.Visible;
            if (error != null) return;
            preferences.QuickLaunchModifiers = modifiers;
            preferences.QuickLaunchKey = key;
            save();
        }

        internal void ShowAppearanceSample(FrameworkElement source, HighlightPart part, double radius) =>
            editor.ShowAppearanceSample(source, part, radius);

        private void RefreshStartup()
        {
            refreshing = true;
            try
            {
                StartupToggle.IsChecked = startup.IsRegistered();
                StartupToggle.IsEnabled = true;
                Feedback.Visibility = Visibility.Collapsed;
            }
            catch (Exception error)
            {
                StartupToggle.IsEnabled = false;
                ShowError(error);
            }
            finally
            {
                UpdateStartupModeVisibility();
                refreshing = false;
            }
        }

        private void ChangeStartup(object sender, RoutedEventArgs e)
        {
            if (refreshing) return;
            try
            {
                startup.SetRegistered(StartupToggle.IsChecked == true);
                UpdateStartupModeVisibility();
                Feedback.Visibility = Visibility.Collapsed;
            }
            catch (Exception error)
            {
                RefreshStartup();
                ShowError(error);
            }
        }

        private void ChangeBehavior(object sender, RoutedEventArgs e)
        {
            if (refreshing) return;
            preferences.ConfirmLaunch = ConfirmLaunchToggle.IsChecked == true;
            preferences.QuitAfterLaunch = AfterLaunchPicker.SelectedIndex == 2;
            preferences.MinimizeAfterLaunch = AfterLaunchPicker.SelectedIndex == 1;
            preferences.QuitOnWindowClose = OnClosePicker.SelectedIndex == 1;
            preferences.StartupInTray = StartupModePicker.SelectedIndex == 1;
            UpdateCloseWarnings();
            save();
        }

        private void UpdateCloseWarnings()
        {
            AfterLaunchWarning.Visibility = preferences.QuitAfterLaunch ? Visibility.Visible : Visibility.Collapsed;
            OnCloseWarning.Visibility = preferences.QuitOnWindowClose ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ChangeLibrarySync(object sender, RoutedEventArgs e)
        {
            if (refreshing) return;
            preferences.SyncSteam = SteamSyncToggle.IsChecked == true;
            preferences.SyncEpic = EpicSyncToggle.IsChecked == true;
            save();
            LibrarySyncChanged?.Invoke();
        }

        private void ChangeUiSounds(object sender, RoutedEventArgs e)
        {
            if (refreshing) return;
            preferences.UiSoundsEnabled = UiSoundsToggle.IsChecked == true;
            save();
            UiSoundsChanged?.Invoke();
        }

        private void UpdateStartupModeVisibility()
        {
            StartupModeRow.Visibility = StartupToggle.IsEnabled && StartupToggle.IsChecked == true
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowError(Exception error)
        {
            Feedback.Text = "Couldn't update startup. " + error.Message;
            Feedback.Visibility = Visibility.Visible;
        }

        private void Cancel(object sender, RoutedEventArgs e) { if (Closed != null) Closed(); }
    }
}
