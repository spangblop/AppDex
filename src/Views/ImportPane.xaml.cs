using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace GameShelf
{
    public partial class ImportPane : System.Windows.Controls.UserControl
    {
        private readonly GameCatalog catalog;
        private SteamLocation location;
        private string clientRoot;
        private string libraryRoot;
        private SteamTitle[] available = new SteamTitle[0];
        private CancellationTokenSource pending;
        private bool updatingSelection;
        private readonly string source;
        internal List<Game> Added { get; private set; }
        internal event Action Completed;
        internal event Action Closed;

        internal ImportPane(GameCatalog catalog) : this(catalog, "Steam") { }

        internal ImportPane(GameCatalog catalog, string source)
        {
            this.catalog = catalog;
            this.source = source;
            InitializeComponent();
            location = SteamLocation.Read(SteamLocation.PreferencesFile);
            UpdateLocation();
            SteamId.Text = SteamLibrary.ActiveUser();
            if (source != "Steam")
            {
                Sources.Visibility = FolderFields.Visibility = Visibility.Collapsed;
            }
            if (source == "Running") InstalledNotice.Visibility = Visibility.Collapsed;
            Loaded += delegate { if (source == "Steam") Preview(false); else PreviewOther(); };
            Unloaded += delegate { Reset(); Key.Clear(); };
        }

        private async void PreviewOther()
        {
            Summary.Text = source == "Epic" ? "Checking Epic Games Launcher’s installed apps…" : "Looking for open apps…";
            try
            {
                var found = await Task.Run(() => source == "Epic" ? AppDiscovery.Epic(AppDiscovery.EpicFolder) : AppDiscovery.Running());
                if (!IsLoaded) return;
                var titles = catalog.NewApps(found).Select(item => new SteamTitle { Name = item.Name, Candidate = item }).ToArray();
                Present(titles, found.Select(item => item.Target).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            }
            catch (Exception error) { Summary.Text = "Couldn't detect apps. " + error.Message; }
        }

        private void UpdateLocation()
        {
            clientRoot = location.FindClient();
            libraryRoot = location.ScanFolder(clientRoot);
            FolderPath.Text = libraryRoot ?? "No Steam folder detected";
            AutoFolder.Visibility = location.LibraryFolder == null ? Visibility.Collapsed : Visibility.Visible;
        }

        private sealed class FolderOwner : Forms.IWin32Window
        {
            public IntPtr Handle { get; set; }
        }

        private void ChooseFolder(object sender, RoutedEventArgs e)
        {
            using (var picker = new Forms.FolderBrowserDialog {
                Description = "Choose your Steam installation, a Steam library, or its steamapps folder.",
                ShowNewFolderButton = false, SelectedPath = libraryRoot ?? "" })
            {
                if (picker.ShowDialog(new FolderOwner { Handle = new WindowInteropHelper(Window.GetWindow(this)).Handle }) != Forms.DialogResult.OK) return;
                try { SetLocation(location.WithFolder(picker.SelectedPath)); }
                catch (Exception error) { Summary.Text = error.Message; }
            }
        }

        private void AutoDetect(object sender, RoutedEventArgs e)
        {
            try { SetLocation(new SteamLocation()); }
            catch (Exception error) { Summary.Text = error.Message; }
        }

        private void SetLocation(SteamLocation next)
        {
            next.Save(SteamLocation.PreferencesFile);
            location = next;
            UpdateLocation();
            Preview(false);
        }

        private void Reset()
        {
            if (pending != null) pending.Cancel();
            pending = null;
            available = new SteamTitle[0];
            Titles.ItemsSource = available;
            LoadAccount.IsEnabled = true;
            UpdateSelection();
        }

        private void SourceChanged(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            Reset();
            AccountFields.Visibility = Account.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            FolderFields.Visibility = Local.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            if (Local.IsChecked == true) Preview(false);
            else Summary.Text = "Load installed apps owned by this Steam account.";
        }

        private void CredentialsChanged(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded || Account.IsChecked != true) return;
            Reset();
            Summary.Text = "Load installed apps owned by this Steam account.";
        }

        private void OpenKeyPage(object sender, RoutedEventArgs e)
        {
            try { using (Process.Start(new ProcessStartInfo("https://steamcommunity.com/dev/apikey") { UseShellExecute = true })) { } }
            catch (Exception error) { Summary.Text = error.Message; }
        }

        private void LoadOwned(object sender, RoutedEventArgs e) { Preview(true); }

        private async void Preview(bool account)
        {
            Reset();
            var request = new CancellationTokenSource();
            pending = request;
            LoadAccount.IsEnabled = false;
            Summary.Text = "Scanning Steam library folders…";
            try
            {
                string scanFolder = libraryRoot;
                var local = account && scanFolder == null ? new SteamScan() : await Task.Run(() => SteamLibrary.Installed(scanFolder));
                if (request.IsCancellationRequested) return;
                List<SteamTitle> found = local.Titles;
                if (account && found.Count > 0)
                {
                    Summary.Text = "Checking which installed apps this Steam account owns…";
                    SteamTitle[] owned = await SteamLibrary.OwnedGames(SteamId.Text.Trim(), Key.Password.Trim(), request.Token);
                    if (request.IsCancellationRequested) return;
                    found = local.InstalledForAccount(owned);
                }
                Present(catalog.NewSteamTitles(found), found.Count);
                if (local.Unreadable > 0) Summary.Text += " Some Steam files or library folders could not be read.";
            }
            catch (Exception error)
            {
                if (!request.IsCancellationRequested)
                    Summary.Text = error is OperationCanceledException ? "Steam took too long to respond. Try again." : error.Message;
            }
            finally
            {
                if (pending == request) { pending = null; LoadAccount.IsEnabled = true; }
                request.Dispose();
            }
        }

        private void Present(SteamTitle[] titles, int found)
        {
            available = titles;
            foreach (var title in available)
            {
                title.Selected = true;
                title.PropertyChanged += delegate { if (!updatingSelection) UpdateSelection(); };
            }
            Titles.ItemsSource = available;
            Summary.Text = found == 0 ? (source == "Running" ? "No apps found." : "No installed apps found.") :
                string.Format("{0} available to import · {1} already in AppDex", available.Length, found - available.Length);
            UpdateSelection();
        }

        private void ToggleAll(object sender, RoutedEventArgs e)
        {
            if (updatingSelection || !IsLoaded) return;
            updatingSelection = true;
            bool select = SelectAll.IsChecked == true;
            foreach (var title in available) title.Selected = select;
            updatingSelection = false;
            UpdateSelection();
        }

        private void UpdateSelection()
        {
            int count = available.Count(t => t.Selected);
            updatingSelection = true;
            SelectAll.IsEnabled = available.Length > 0;
            SelectAll.IsChecked = count == 0 ? false : count == available.Length ? true : (bool?)null;
            updatingSelection = false;
            ImportButton.IsEnabled = count > 0;
            ImportButton.Content = count == 0 ? "Import apps" : "Import " + count + (count == 1 ? " app" : " apps");
        }

        private void Import(object sender, RoutedEventArgs e)
        {
            try
            {
                var selected = available.Where(t => t.Selected);
                Added = source == "Steam" ? catalog.ImportSteam(selected, clientRoot ?? libraryRoot) :
                    catalog.ImportApps(selected.Select(t => t.Candidate));
                if (Completed != null) Completed();
            }
            catch (Exception error) { Summary.Text = "Couldn't import apps. " + error.Message; }
        }

        private void Cancel(object sender, RoutedEventArgs e) { if (Closed != null) Closed(); }
    }
}
