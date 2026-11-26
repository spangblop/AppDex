using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameShelf
{
    public partial class AppEditorPane : System.Windows.Controls.UserControl
    {
        private readonly GameCatalog catalog;
        private readonly Game editedApp;
        private readonly GameSettings draft;
        private readonly ImageSource defaultPicture;
        private BitmapSource newPicture;
        private readonly uint steamId;
        private int pictureRequest;
        internal Game Result { get; private set; }
        internal bool Removed { get; private set; }
        internal event Action Completed;
        internal event Action Closed;
        internal event Action ResetActivity;
        internal Func<Game, int, int, string> ValidateShortcut;
        internal Func<Game, GameSettings, string> ValidateShortcuts;
        private bool recordingShortcut;
        private bool switchingProfile;
        private LaunchProfile selectedProfile;

        internal AppEditorPane(GameCatalog catalog, Game game)
        {
            this.catalog = catalog;
            editedApp = game;
            draft = game == null ? new GameSettings() : game.Settings.Copy();
            draft.Profiles = draft.Profiles ?? new List<LaunchProfile>();
            InitializeComponent();
            SaveButton.Content = game == null ? "Add app" : "Save changes";
            GameName.Text = draft.Name ?? "";
            SearchAliases.Text = draft.Aliases ?? "";
            Executable.Text = draft.Executable ?? "";
            SelectProfile(0);
            ExecutableFields.Visibility = game == null || game.Custom ? Visibility.Visible : Visibility.Collapsed;
            defaultPicture = game == null ? Game.CustomBackdrop() : game.DefaultArtwork;
            PicturePreview.ImageSource = game == null ? defaultPicture : game.Artwork;
            ResetPicture.IsEnabled = draft.Picture != null;
            RemoveAppButton.Visibility = game == null ? Visibility.Collapsed : Visibility.Visible;
            PlayStatsFields.DataContext = game;
            PlayStatsFields.Visibility = game == null ? Visibility.Collapsed : Visibility.Visible;
            steamId = game == null ? 0 : game.SteamId;
            SteamPicture.Visibility = steamId != 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Cancel(object sender, RoutedEventArgs e) { if (Closed != null) Closed(); }

        private void RequestRemove(object sender, RoutedEventArgs e)
        {
            if (ResetStatsPrompt.Visibility == Visibility.Visible) KeepStats(sender, e);
            RemoveQuestion.Text = LibraryWindow.RemovalQuestion(editedApp);
            RemovePrompt.Visibility = Visibility.Visible;
            RemoveAppButton.IsEnabled = false;
            SaveButton.IsDefault = false;
            KeepAppButton.Focus();
        }

        private void RequestStatsReset(object sender, RoutedEventArgs e)
        {
            if (RemovePrompt.Visibility == Visibility.Visible) KeepApp(sender, e);
            ResetStatsStatus.Text = "";
            ResetStatsPrompt.Visibility = Visibility.Visible;
            ResetStatsButton.IsEnabled = false;
            SaveButton.IsDefault = false;
            KeepStatsButton.Focus();
            ResetStatsPrompt.BringIntoView();
        }

        private void KeepStats(object sender, RoutedEventArgs e)
        {
            ResetStatsPrompt.Visibility = Visibility.Collapsed;
            ResetStatsButton.IsEnabled = true;
            SaveButton.IsDefault = true;
            ResetStatsButton.Focus();
        }

        private void ConfirmStatsReset(object sender, RoutedEventArgs e)
        {
            try
            {
                ResetActivity?.Invoke();
                KeepStats(sender, e);
                ErrorText.Visibility = Visibility.Collapsed;
                ResetStatsStatus.Text = "Play stats reset.";
            }
            catch (Exception error) { ShowError("Couldn't reset play stats. " + error.Message); }
        }

        private void KeepApp(object sender, RoutedEventArgs e)
        {
            RemovePrompt.Visibility = Visibility.Collapsed;
            RemoveAppButton.IsEnabled = true;
            SaveButton.IsDefault = true;
            RemoveAppButton.Focus();
        }

        private void ConfirmRemove(object sender, RoutedEventArgs e)
        {
            try
            {
                catalog.Remove(editedApp);
                Removed = true;
                if (Completed != null) Completed();
            }
            catch (Exception error) { ShowError(error.Message); }
        }

        private void BrowseGame(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Title = "Choose app executable", Filter = "Windows executables (*.exe)|*.exe" };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            Executable.Text = dialog.FileName;
            if (string.IsNullOrWhiteSpace(GameName.Text)) GameName.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
        }

        private void BrowsePicture(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Title = "Choose app picture",
                Filter = "Pictures (*.png, *.jpg, *.jpeg, *.bmp, *.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif" };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            try
            {
                newPicture = GameCatalog.ReadPicture(dialog.FileName);
                pictureRequest++;
                PicturePreview.ImageSource = newPicture;
                ResetPicture.IsEnabled = true;
                ErrorText.Visibility = Visibility.Collapsed;
            }
            catch (Exception error) { ShowError("Couldn't open that picture. " + error.Message); }
        }

        private void UseDefaultPicture(object sender, RoutedEventArgs e)
        {
            pictureRequest++;
            newPicture = null;
            draft.Picture = null;
            PicturePreview.ImageSource = defaultPicture;
            ResetPicture.IsEnabled = false;
        }

        private async void UpdateSteamPicture(object sender, RoutedEventArgs e)
        {
            int request = ++pictureRequest;
            SteamPicture.IsEnabled = false;
            try
            {
                var image = await SteamLibrary.RefreshArtwork(steamId);
                if (!IsLoaded || request != pictureRequest) return;
                newPicture = image;
                PicturePreview.ImageSource = image;
                ResetPicture.IsEnabled = true;
                ErrorText.Visibility = Visibility.Collapsed;
            }
            catch (Exception error)
            {
                if (IsLoaded && request == pictureRequest) ShowError(error.Message);
            }
            finally { SteamPicture.IsEnabled = true; }
        }

        private void UpdateShortcutLabel()
        {
            int key = selectedProfile?.ShortcutKey ?? draft.ShortcutKey;
            int modifiers = selectedProfile?.ShortcutModifiers ?? draft.ShortcutModifiers;
            RecordShortcut.Content = recordingShortcut ? "Press your shortcut…" :
                key == 0 ? "Record shortcut…" : LaunchHotkeys.Describe(modifiers, key);
            ClearShortcut.IsEnabled = key != 0;
            ShortcutHint.Visibility = recordingShortcut ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BeginShortcutCapture(object sender, RoutedEventArgs e)
        {
            RecordShortcut.Focus();
            recordingShortcut = true;
            ShortcutHint.Text = "Press a key combination or a function key. Esc cancels.";
            UpdateShortcutLabel();
        }

        private void CancelShortcutCapture(object sender, KeyboardFocusChangedEventArgs e)
        {
            recordingShortcut = false;
            UpdateShortcutLabel();
        }

        private void RemoveShortcut(object sender, RoutedEventArgs e)
        {
            SetShortcut(0, 0);
            CancelShortcutCapture(null, null);
        }

        internal bool HandleShortcutKey(KeyEventArgs e)
        {
            if (!recordingShortcut || !RecordShortcut.IsKeyboardFocused) return false;
            e.Handled = true;
            if (e.IsRepeat) return true;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            int modifiers = (int)Keyboard.Modifiers;
            if (key == Key.Escape && modifiers == 0)
            {
                CancelShortcutCapture(null, null);
                return true;
            }
            if (key == Key.LeftAlt || key == Key.RightAlt || key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin) return true;
            return RecordShortcutCombination(modifiers, KeyInterop.VirtualKeyFromKey(key));
        }

        internal bool CaptureRegisteredShortcut(int modifiers, int key)
        {
            return recordingShortcut && RecordShortcut.IsKeyboardFocused && RecordShortcutCombination(modifiers, key);
        }

        private bool RecordShortcutCombination(int modifiers, int key)
        {
            if (!LaunchHotkeys.IsAssignable(modifiers, key))
            {
                ShortcutHint.Text = key == 0x7b ? "Windows reserves F12. Choose another key." :
                    "Use a modifier with this key, or choose a function key.";
                return true;
            }
            SetShortcut(modifiers, key);
            CancelShortcutCapture(null, null);
            return true;
        }

        private void SetShortcut(int modifiers, int key)
        {
            if (selectedProfile == null)
            {
                draft.ShortcutModifiers = modifiers;
                draft.ShortcutKey = key;
            }
            else
            {
                selectedProfile.ShortcutModifiers = modifiers;
                selectedProfile.ShortcutKey = key;
            }
        }

        private void SaveProfileFields()
        {
            if (selectedProfile == null) draft.Arguments = Arguments.Text;
            else
            {
                selectedProfile.Name = ProfileName.Text.Trim();
                selectedProfile.Executable = ProfileExecutable.Text;
                selectedProfile.Arguments = Arguments.Text;
            }
        }

        private void SelectProfile(int index)
        {
            switchingProfile = true;
            ProfilePicker.ItemsSource = new[] { "Default" }.Concat(draft.Profiles.Select(profile =>
                string.IsNullOrWhiteSpace(profile.Name) ? "(Untitled profile)" : profile.Name)).ToArray();
            ProfilePicker.SelectedIndex = index;
            selectedProfile = index > 0 ? draft.Profiles[index - 1] : null;
            ProfileFields.Visibility = selectedProfile != null ? Visibility.Visible : Visibility.Collapsed;
            RemoveProfileButton.IsEnabled = selectedProfile != null;
            ProfileName.Text = selectedProfile?.Name ?? "";
            ProfileExecutable.Text = selectedProfile?.Executable ?? "";
            Arguments.Text = selectedProfile == null ? draft.Arguments ?? "" : selectedProfile.Arguments ?? "";
            CancelShortcutCapture(null, null);
            switchingProfile = false;
        }

        private void ChangeProfile(object sender, RoutedEventArgs e)
        {
            if (switchingProfile || ProfilePicker.SelectedIndex < 0) return;
            SaveProfileFields();
            SelectProfile(ProfilePicker.SelectedIndex);
        }

        private void AddProfile(object sender, RoutedEventArgs e)
        {
            SaveProfileFields();
            int number = 1;
            while (draft.Profiles.Any(profile => string.Equals(profile.Name, "Profile " + number, StringComparison.OrdinalIgnoreCase))) number++;
            draft.Profiles.Add(new LaunchProfile { Name = "Profile " + number, Arguments = draft.Arguments });
            SelectProfile(draft.Profiles.Count);
            ProfileName.Focus();
            ProfileName.SelectAll();
        }

        private void RemoveProfile(object sender, RoutedEventArgs e)
        {
            if (selectedProfile == null) return;
            draft.Profiles.Remove(selectedProfile);
            SelectProfile(0);
        }

        private void BrowseProfileExecutable(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Title = "Choose profile executable", Filter = "Windows executables (*.exe)|*.exe" };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true) ProfileExecutable.Text = dialog.FileName;
        }

        internal void FocusShortcut(LaunchProfile profile)
        {
            if (profile != null)
            {
                int index = draft.Profiles.FindIndex(candidate => candidate.Name == profile.Name);
                if (index >= 0) SelectProfile(index + 1);
            }
            RecordShortcut.BringIntoView();
            RecordShortcut.Focus();
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            draft.Name = GameName.Text;
            draft.Aliases = SearchAliases.Text;
            draft.Executable = Executable.Text;
            SaveProfileFields();
            try
            {
                string shortcutError = ValidateShortcuts != null ? ValidateShortcuts(editedApp, draft) :
                    ValidateShortcut?.Invoke(editedApp, draft.ShortcutModifiers, draft.ShortcutKey);
                if (shortcutError != null) { ShowError(shortcutError); return; }
                Result = catalog.Save(draft, newPicture);
                if (Completed != null) Completed();
            }
            catch (Exception error) { ShowError(error.Message); }
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}
