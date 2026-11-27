using System;
using System.IO;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
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
            var actions = new WrapPanel { Children = { later, install } };
            var panel = new DockPanel { Children = { description, actions, status } };
            DockPanel.SetDock(description, Dock.Top);
            DockPanel.SetDock(actions, Dock.Bottom);
            DockPanel.SetDock(status, Dock.Bottom);
            if (!string.IsNullOrWhiteSpace(update.ReleaseNotes))
            {
                var heading = new TextBlock { Text = "Release notes", FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 6) };
                DockPanel.SetDock(heading, Dock.Top);
                panel.Children.Add(heading);
                var notes = new ScrollViewer {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Margin = new Thickness(0, 0, 0, 12),
                    Content = new TextBlock { Text = update.ReleaseNotes, TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 8, 0) }
                };
                AutomationProperties.SetName(notes, "Release notes");
                panel.Children.Add(notes);
            }
            install.Click += async delegate
            {
                if (installingUpdate || QuitRequested == null) return;
                installingUpdate = true;
                UpdateAvailableButton.IsEnabled = later.IsEnabled = install.IsEnabled = false;
                status.SetResourceReference(ForegroundProperty, "AppMuted");
                status.Text = "Checking for the latest AppDex release…";
                status.Visibility = Visibility.Visible;
                bool restarting = false;
                try
                {
                    AppUpdate latest;
                    using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25)))
                        latest = await AppUpdates.Check(timeout.Token);
                    if (updatesClosed) return;
                    SetAvailableUpdate(latest);
                    if (latest == null)
                    {
                        if (ReferenceEquals(DrawerContent.Content, panel)) ClosePanel();
                        Announce("No newer AppDex release is available.");
                        return;
                    }
                    if (latest.Version != update.Version)
                    {
                        installingUpdate = false;
                        if (ReferenceEquals(DrawerContent.Content, panel)) OpenAvailableUpdate(null, null);
                        return;
                    }

                    status.Text = "Downloading and checking AppDex " + latest.Version.ToString(3) + "…";
                    FlushPreferences();
                    using (var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2)))
                        await AppUpdateInstaller.Prepare(latest, timeout.Token);
                    status.Text = "Restarting AppDex…";
                    QuitRequested?.Invoke();
                    restarting = true;
                }
                catch (Exception error)
                {
                    status.SetResourceReference(ForegroundProperty, "AppDanger");
                    bool accessDenied = AppUpdateInstaller.IsAccessDenied(error);
                    string message = accessDenied ? AppUpdateInstaller.AccessDeniedHelp :
                        error is OperationCanceledException ? "The update timed out. Try again." : "Couldn't install the update. " + error.Message;
                    status.Text = message;
                    if (accessDenied)
                    {
                        var link = AppUpdateInstaller.CreateRepositoryLink();
                        link.SetResourceReference(TextElement.ForegroundProperty, "AppAccentText");
                        status.Inlines.Add(new LineBreak());
                        status.Inlines.Add(link);
                    }
                    if (!ReferenceEquals(DrawerContent.Content, panel)) AnnounceError(message);
                }
                finally
                {
                    if (!restarting)
                    {
                        installingUpdate = false;
                        UpdateAvailableButton.IsEnabled = later.IsEnabled = install.IsEnabled = true;
                    }
                }
            };
            ShowPanel("Update available", panel, later);
        }
    }
}
