using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace GameShelf
{
    public partial class LibraryWindow
    {
        private readonly DispatcherTimer librarySync = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        private readonly HashSet<string> syncingStores = new HashSet<string>();
        private readonly Dictionary<string, string> syncErrors = new Dictionary<string, string>();
        private int syncRevision;

        private void ToggleEmptyLibrarySync(object sender, RoutedEventArgs e)
        {
            if (EmptyLibraryIntro.Visibility != Visibility.Visible) return;
            bool enable = !(preferences.SyncSteam && preferences.SyncEpic);
            preferences.SyncSteam = preferences.SyncEpic = enable;
            QueuePreferences();
            UpdateLibrarySync();
        }

        private void RefreshEmptyLibrarySync()
        {
            bool enabled = preferences.SyncSteam && preferences.SyncEpic;
            EmptyLibrarySync.Content = enabled ? "Turn off library sync" : "Turn on library sync";
            EmptyLibraryStatus.Text = enabled ? "Sync is on. Installed games will appear here when AppDex finds them."
                : "Only games installed on this PC appear in AppDex.";
        }

        private void InitializeLibrarySync()
        {
            librarySync.Tick += delegate { PollLibrarySync(); };
            Closed += delegate { librarySync.Stop(); syncRevision++; };
            UpdateLibrarySync();
        }

        private void UpdateLibrarySync()
        {
            if (AppBuild.IsPreview || catalog.LoadError != null) return;
            RefreshEmptyLibrarySync();

            syncRevision++;
            syncErrors.Clear();
            foreach (var game in Games)
            {
                if ((game.SteamId != 0 && !preferences.SyncSteam) ||
                    (game.Settings.EpicUri != null && !preferences.SyncEpic))
                    game.SetInstallation(null);
            }
            if (preferences.SyncSteam || preferences.SyncEpic)
            {
                librarySync.Start();
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    if (librarySync.IsEnabled) PollLibrarySync();
                }));
            }
            else librarySync.Stop();
        }

        private void PollLibrarySync()
        {
            if (tileDrag != null || pressedTile != null || DrawerContent.Content is ImportPane) return;
            if (preferences.SyncSteam && !syncingStores.Contains("Steam"))
                RefreshStore("Steam", SteamLocation.ScanInstalled, result => result.Scan != null && catalog.SyncSteam(result.Scan, result.Client));
            if (preferences.SyncEpic && !syncingStores.Contains("Epic Games"))
                RefreshStore("Epic Games", AppDiscovery.ScanInstalledEpic, scan => catalog.SyncEpic(scan.Apps, scan.Complete));
        }

        private async void RefreshStore<T>(string source, Func<T> scan, Func<T, bool> apply)
        {
            syncingStores.Add(source);
            int revision = syncRevision;
            try
            {
                var result = await Task.Run(scan);
                if (revision != syncRevision || tileDrag != null || pressedTile != null || DrawerContent.Content is ImportPane) return;
                if (apply(result)) CommitPreferences();
                syncErrors.Remove(source);
            }
            catch (Exception error)
            {
                if (revision == syncRevision && (!syncErrors.TryGetValue(source, out string previous) || previous != error.Message))
                {
                    syncErrors[source] = error.Message;
                    AnnounceError("Couldn't sync " + source + ". " + error.Message);
                }
            }
            finally
            {
                syncingStores.Remove(source);
                if (revision != syncRevision && librarySync.IsEnabled) PollLibrarySync();
            }
        }
    }
}
