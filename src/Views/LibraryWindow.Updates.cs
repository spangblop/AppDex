using System;
using System.IO;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;

namespace GameShelf
{
    public partial class LibraryWindow
    {
        private readonly DispatcherTimer updateTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMinutes(15)
        };
        private AppUpdate availableUpdate;
        private bool checkingForUpdate, installingUpdate, updatesClosed;

        internal void StartUpdateChecks()
        {
            if (AppBuild.IsPreview) return;
            updateTimer.Tick += delegate { CheckUpdateFeed(); };
            Closed += delegate { updatesClosed = true; updateTimer.Stop(); };
            updateTimer.Start();
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(CheckUpdateFeed));
        }

        private async void CheckUpdateFeed()
        {
            if (checkingForUpdate || installingUpdate || updatesClosed) return;
            checkingForUpdate = true;
            try
            {
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25)))
                {
                    var update = await AppUpdates.Check(timeout.Token);
                    if (!updatesClosed && !installingUpdate) SetAvailableUpdate(update);
                }
            }
            catch (Exception error) when (error is HttpRequestException || error is OperationCanceledException ||
                error is IOException || error is SerializationException)
            {
                // A failed background check will retry on the next tick.
            }
            finally { checkingForUpdate = false; }
        }

        private void SetAvailableUpdate(AppUpdate update)
        {
            availableUpdate = update;
            UpdateAvailableButton.Visibility = update == null ? Visibility.Collapsed : Visibility.Visible;
            UpdateAvailableButton.ToolTip = update == null ? null : "AppDex " + update.Version.ToString(3);
        }

        private void OpenAvailableUpdate(object sender, RoutedEventArgs e)
        {
            var update = availableUpdate;
            if (update == null || installingUpdate) return;
            var description = new TextBlock {
                Text = "AppDex " + update.Version.ToString(3) + " is ready to download. You’re running " + AppUpdates.CurrentLabel + ".",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 0, 0, 12) };
            AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
            status.SetResourceReference(ForegroundProperty, "AppMuted");
            var later = SmallButton("Not now");
            later.Margin = new Thickness(0, 0, 7, 0);
            later.Click += delegate { ClosePanel(); };
            var install = SmallButton("Install and restart");
            install.SetResourceReference(BackgroundProperty, "AppAccent");
            install.SetResourceReference(ForegroundProperty, "AppAccentInk");
            install.SetResourceReference(BorderBrushProperty, "AppAccent");
            install.Click += async delegate
            {
                if (installingUpdate || QuitRequested == null) return;
                installingUpdate = true;
                UpdateAvailableButton.IsEnabled = later.IsEnabled = install.IsEnabled = false;
                status.SetResourceReference(ForegroundProperty, "AppMuted");
                status.Text = "Downloading and checking the update…";
                status.Visibility = Visibility.Visible;
                try
                {
                    FlushPreferences();
                    using (var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2)))
                        await AppUpdateInstaller.Prepare(update, timeout.Token);
                    status.Text = "Restarting AppDex…";
                    QuitRequested?.Invoke();
                    return;
                }
                catch (Exception error)
                {
                    status.SetResourceReference(ForegroundProperty, "AppDanger");
                    status.Text = error is OperationCanceledException ? "The update timed out. Try again." :
                        "Couldn't install the update. " + error.Message;
                    if (!ReferenceEquals(DrawerContent.Content, status.Parent)) AnnounceError(status.Text);
                }
                installingUpdate = false;
                UpdateAvailableButton.IsEnabled = later.IsEnabled = install.IsEnabled = true;
            };
            ShowPanel("Update available", new StackPanel { Children = { description, status, new WrapPanel { Children = { later, install } } } }, later);
        }
    }
}
