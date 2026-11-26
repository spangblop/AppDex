using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
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

        internal AppEditorPane(GameCatalog catalog, Game game)
        {
            this.catalog = catalog;
            editedApp = game;
            draft = game == null ? new GameSettings() : game.Settings.Copy();
            InitializeComponent();
            SaveButton.Content = game == null ? "Add app" : "Save changes";
            GameName.Text = draft.Name ?? "";
            Executable.Text = draft.Executable ?? "";
            Arguments.Text = draft.Arguments ?? "";
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

        private void OnSave(object sender, RoutedEventArgs e)
        {
            draft.Name = GameName.Text;
            draft.Executable = Executable.Text;
            draft.Arguments = Arguments.Text;
            try
            {
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
