using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

[assembly: AssemblyTitle("AppDex")]
[assembly: AssemblyProduct("AppDex")]
[assembly: AssemblyVersion("0.3.0.0")]
[assembly: AssemblyFileVersion("0.3.0.0")]
[assembly: AssemblyInformationalVersion("Beta 0.3.0")]

namespace GameShelf
{
    public partial class LibraryWindow : Window
    {
        private readonly GameCatalog catalog;
        private readonly PreferenceWriter preferenceWriter;
        private readonly UiSounds uiSounds = new UiSounds();
        private LibraryPreferences preferences;
        internal IEnumerable<Game> Games { get { return catalog.Games; } }
        internal bool QuitOnWindowClose => preferences.QuitOnWindowClose;
        internal bool StartupInTray => preferences.StartupInTray;
        internal event Action QuitRequested;
        internal event Action<Game> ResetActivityRequested;
        internal event Action GameShortcutsChanged;
        internal event Action<string> HiddenLaunchError;
        internal Func<Game, int, int, string> ValidateGameShortcut;
        internal Func<int, int, string> RegisterQuickShortcut;
        internal int QuickLaunchModifiers => preferences.QuickLaunchModifiers ?? LaunchHotkeys.DefaultModifiers;
        internal int QuickLaunchKey => preferences.QuickLaunchKey ?? LaunchHotkeys.DefaultKey;
        private readonly Action<Game> launch;
        private const long LaunchTimeoutMilliseconds = 15000;
        private readonly Dictionary<Game, PendingLaunch> pending = new Dictionary<Game, PendingLaunch>();
        private readonly DispatcherTimer launchWatch = new DispatcherTimer(DispatcherPriority.Background);
        private readonly Dictionary<Game, long> lastClick = new Dictionary<Game, long>();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly List<Button> visible = new List<Button>();
        private int latestRequest;
        private bool fullscreen;
        private bool activityOrderDirty;
        private WindowState previousState;

        private sealed class PendingLaunch
        {
            internal int Number;
            internal long Deadline;
            internal bool RequestSent;
        }

        internal LibraryWindow(Action<Game> launchGame)
            : this(launchGame, new GameCatalog(AppBuild.DataDirectory)) { }

        internal LibraryWindow(Action<Game> launchGame, GameCatalog catalog)
        {
            this.catalog = catalog;
            launch = launchGame;
            preferences = LibraryPreferences.Load(catalog.PreferencesFile);
            preferenceWriter = new PreferenceWriter(catalog.PreferencesFile, error =>
            {
                if (!Dispatcher.HasShutdownStarted)
                    Dispatcher.BeginInvoke(DispatcherPriority.Background,
                        new Action(() => Announce("Couldn't save AppDex settings. " + error.Message)));
            });
            AppAppearance.Apply(preferences);
            InitializeComponent();
            Title = AppBuild.Name;
            if (AppBuild.IsPreview)
            {
                SortPicker.Visibility = SourcePicker.Visibility = Visibility.Collapsed;
                PreviewSortButton.Visibility = PreviewSourceButton.Visibility = Visibility.Visible;
            }
            Width = Math.Min(Width, SystemParameters.WorkArea.Width);
            Height = Math.Min(Height, SystemParameters.WorkArea.Height);
            SortPicker.SelectedIndex = preferences.Sort;
            SourcePicker.SelectedIndex = preferences.Source;
            SearchBox.TextChanged += delegate { if (!switchingScope) RenderLibrary(); };
            SortPicker.SelectionChanged += delegate
            {
                preferences.Sort = SortPicker.SelectedIndex;
                if (activeGroupId != null && preferences.Sort != 2) RestoreLibraryScope();
                CommitPreferences();
            };
            SourcePicker.SelectionChanged += delegate
            {
                if (switchingScope) return;
                if (activeGroupId != null) RenderLibrary();
                else
                {
                    preferences.Source = SourcePicker.SelectedIndex;
                    CommitPreferences();
                }
            };
            PreviewKeyDown += SearchKeys;
            Body.SizeChanged += delegate { ResizeLibrary(); };
            LibraryScroll.SizeChanged += delegate { ResizeTiles(); };
            PreviewMouseMove += MoveTilePointer;
            PreviewMouseLeftButtonUp += ReleaseTilePointer;
            LostMouseCapture += TileCaptureLost;
            Deactivated += delegate { EndTileDrag(false); };
            LibraryScroll.ScrollChanged += delegate(object sender, ScrollChangedEventArgs e)
            {
                if (e.ViewportWidthChange != 0) ResizeTiles();
            };
            SourceInitialized += delegate { ApplyWindowChrome(); };
            launchWatch.Tick += delegate { ExpireLaunches(clock.ElapsedMilliseconds); };
            Closed += delegate { launchWatch.Stop(); uiSounds.Dispose(); };
            RenderLibrary();
            InitializeLibrarySync();
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, uint attribute, ref int value, int size);

        private void ApplyWindowChrome()
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;
            int dark = AppAppearance.DarkTitleBar(preferences) ? 1 : 0;
            DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        }

        private void OpenSettings(object sender, RoutedEventArgs e)
        {
            try
            {
                var startup = new StartupRegistration(Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                    typeof(DesktopHost).Assembly.Location);
                var settings = new SettingsPane(startup, preferences, QueuePreferences, delegate
                {
                    AppAppearance.Apply(preferences);
                    ApplyWindowChrome();
                    RenderLibrary();
                }, Path.GetDirectoryName(catalog.PreferencesFile));
                settings.Closed += ClosePanel;
                settings.HighlightChanged += HighlightAppearance;
                settings.LibrarySyncChanged += UpdateLibrarySync;
                settings.RegisterQuickShortcut = RegisterQuickShortcut;
                settings.UiSoundsChanged += delegate
                {
                    if (!preferences.UiSoundsEnabled) uiSounds.Stop();
                };
                ShowPanel("Settings", settings);
            }
            catch (Exception error) { Announce("Couldn't open AppDex settings. " + error.Message); }
        }

        private void FullscreenClick(object sender, RoutedEventArgs e) { ToggleFullscreen(); }

        private void ToggleFullscreen()
        {
            if (AppBuild.IsPreview)
            {
                ShowPreviewNotice(FullscreenButton);
                return;
            }
            EndTileDrag(false);
            if (!fullscreen) previousState = WindowState;
            WindowState = WindowState.Normal;
            fullscreen = !fullscreen;
            WindowStyle = fullscreen ? WindowStyle.None : WindowStyle.SingleBorderWindow;
            ResizeMode = fullscreen ? ResizeMode.NoResize : ResizeMode.CanResize;
            WindowState = fullscreen ? WindowState.Maximized : previousState;
        }

        private void OpenImport(string source)
        {
            if (catalog.LoadError != null) { Announce(catalog.LoadError); return; }
            var import = new ImportPane(catalog, source);
            import.Closed += ClosePanel;
            import.Completed += delegate
            {
                ClosePanel();
                if (activeGroupId != null) RestoreLibraryScope();
                CommitPreferences();
                Announce("Imported " + AppCount(import.Added.Count) + ".");
            };
            ShowPanel(source == "Steam" ? "Import Steam library" : source == "Epic" ? "Import Epic Games library" : "Scan open apps", import);
        }

        private void EditCover(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            Edit((Game)((Button)sender).DataContext);
        }

        private void Edit(Game game)
        {
            if (catalog.LoadError != null) { Announce(catalog.LoadError); return; }
            var editor = new AppEditorPane(catalog, game);
            editor.ValidateShortcut = ValidateGameShortcut;
            editor.Closed += ClosePanel;
            if (game != null) editor.ResetActivity += () => ResetActivityRequested?.Invoke(game);
            editor.Completed += delegate
            {
                ClosePanel();
                if (editor.Removed)
                {
                    AfterRemoval(game);
                    return;
                }
                if (game == null)
                {
                    if (activeGroupId != null) preferences.Move(editor.Result.Id, activeGroupId, true, false);
                    SearchBox.Clear();
                }
                CommitPreferences();
                Announce((game == null ? "Added " : "Saved changes to ") + editor.Result.Name + ".");
                GameShortcutsChanged?.Invoke();
            };
            ShowPanel(game == null ? "Add app" : "Edit app", editor);
        }

        private void SearchKeys(object sender, KeyEventArgs e)
        {
            if (QuitLayer.Visibility == Visibility.Visible)
            {
                if (e.Key == Key.Escape) { DismissQuit(); e.Handled = true; }
                return;
            }
            if (panelOpen && DrawerContent.Content is AppEditorPane editor && editor.HandleShortcutKey(e)) return;
            if (e.Key == Key.Escape && PanelLayer.Visibility == Visibility.Visible) ClosePanel();
            else if (e.Key == Key.Escape && MenuLayer.Visibility == Visibility.Visible) CloseMenu();
            else if (PanelLayer.Visibility == Visibility.Visible || MenuLayer.Visibility == Visibility.Visible) return;
            else if (e.Key == Key.Escape && tileDrag != null) EndTileDrag(false);
            else if (e.Key == Key.F11 || (e.Key == Key.Escape && fullscreen)) ToggleFullscreen();
            else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
            }
            else if (e.Key == Key.Escape && SearchBox.Text.Length > 0) SearchBox.Clear();
            else if (e.Key == Key.Escape && activeGroupId != null) NavigateToLibrary();
            else if (SearchBox.IsKeyboardFocused && visible.Count > 0 && e.Key == Key.Down) visible[0].Focus();
            else if (SearchBox.IsKeyboardFocused && visible.Count > 0 && e.Key == Key.Enter)
                visible[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent, visible[0]));
            else return;
            e.Handled = true;
        }

        internal bool CaptureRegisteredShortcut(int modifiers, int key)
        {
            return IsActive && panelOpen && DrawerContent.Content is AppEditorPane editor &&
                editor.CaptureRegisteredShortcut(modifiers, key);
        }

        internal void ShowActivity(ActivityBook book)
        {
            bool resort = false;
            foreach (Game game in Games)
            {
                GameActivity activity;
                if (!book.Games.TryGetValue(game.Id, out activity)) continue;
                resort |= (preferences.Sort == 0 && game.Launches != activity.Launches) ||
                    (preferences.Sort == 1 && game.PlayTicks / TimeSpan.TicksPerMinute != activity.PlayTicks / TimeSpan.TicksPerMinute);
                game.UpdateStatistics(activity.Launches, activity.PlayTicks, activity.Active.Length > 0);
            }
            activityOrderDirty |= resort;
            if (activityOrderDirty && tileDrag == null) RenderLibrary();
            else UpdateTotalPlaytime();
            if (pending.Count == 0) return;
            foreach (var entry in pending.Where(entry => entry.Key.Launching && entry.Key.Playing).ToArray())
                FinishLaunch(entry.Key, entry.Key.Name + " is running.", keepDispatch: true);
        }

        private void UpdateTotalPlaytime()
        {
            TotalPlaytime.Text = Game.FormatPlaytime(Games.Sum(game => (decimal)game.PlayTicks));
        }

        private void CommitPreferences()
        {
            preferences.Sync(Games.Select(g => g.Id));
            QueuePreferences();
            RenderLibrary();
        }

        private void QueuePreferences() { preferenceWriter.Queue(preferences); }

        internal void FlushPreferences()
        {
            var settings = DrawerContent.Content as SettingsPane;
            if (settings != null) settings.CommitAppearance();
            preferenceWriter.Flush();
        }

        private void Play(object sender, RoutedEventArgs e)
        {
            if (!ReferenceEquals(sender, e.OriginalSource)) return;
            var tile = (Button)sender;
            Launch((Game)tile.DataContext, tile);
        }

        internal bool ShortcutNeedsLibrary(Game game)
        {
            return preferences.ConfirmLaunch || (game.IsInstalled == false && !game.Playing);
        }

        internal void LaunchFromShortcut(Game game)
        {
            if (QuitLayer.Visibility == Visibility.Visible || !Games.Contains(game)) return;
            if (preferences.ConfirmLaunch && panelOpen)
            {
                AnnounceError("Close the current panel before launching with a shortcut.");
                return;
            }
            Launch(game);
        }

        private void Launch(Game game, Button tile = null)
        {
            if (pending.ContainsKey(game)) return;
            if (!preferences.ConfirmLaunch)
            {
                LaunchNow(game, tile);
                return;
            }
            if (!CanLaunchInstalledGame(game)) return;

            TileLaunchCue.Play(tile, preferences.Appearance);
            var question = new TextBlock { Text = "Launch " + game.Name + "?", TextWrapping = TextWrapping.Wrap,
                FontSize = 18, Margin = new Thickness(0, 0, 0, 14) };
            var cancel = SmallButton("Cancel");
            cancel.Margin = new Thickness(0, 0, 8, 0);
            var confirm = SmallButton("Launch");
            confirm.SetResourceReference(BackgroundProperty, "AppAccent");
            confirm.SetResourceReference(ForegroundProperty, "AppAccentInk");
            confirm.SetResourceReference(BorderBrushProperty, "AppAccentText");
            var actions = new WrapPanel { Children = { cancel, confirm } };
            var content = new StackPanel { Children = { question, actions } };
            cancel.Click += delegate { ClosePanel(); };
            confirm.Click += delegate
            {
                if (!panelOpen || !ReferenceEquals(DrawerContent.Content, content)) return;
                ClosePanel();
                LaunchNow(game);
            };
            ShowPanel("Confirm launch", content, cancel);
        }

        private void LaunchNow(Game game, Button tile = null)
        {
            if (!CanLaunchInstalledGame(game)) return;
            long previous;
            long now = clock.ElapsedMilliseconds;
            if (pending.ContainsKey(game) || (lastClick.TryGetValue(game, out previous) && now - previous < 2000))
                return;
            lastClick[game] = now;
            var attempt = new PendingLaunch { Number = ++latestRequest, Deadline = now + LaunchTimeoutMilliseconds };
            pending.Add(game, attempt);
            game.SetLaunching(true);
            UpdateLaunchWatch();
            TileLaunchCue.Play(tile, preferences.Appearance);
            Announce("Opening " + game.Name + "\u2026");
            PlayUiSound(UiCue.Launch);

            // A storefront may take time to start. Keep input responsive while Windows handles the request.
            var thread = new Thread(delegate()
            {
                Exception failure = null;
                try { launch(game); }
                catch (Exception error)
                {
                    failure = error;
                }
                Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(delegate
                {
                    if (!pending.TryGetValue(game, out PendingLaunch current) || !ReferenceEquals(current, attempt)) return;
                    if (failure != null)
                    {
                        FinishLaunch(game, "Couldn't open " + game.Name + ". " + failure.Message, error: true);
                        return;
                    }
                    attempt.RequestSent = true;
                    if (game.Playing) FinishLaunch(game, game.Name + " is running.");
                    else if (!game.Launching) FinishLaunch(game);
                    if (preferences.QuitAfterLaunch) QuitRequested?.Invoke();
                    else if (preferences.MinimizeAfterLaunch)
                    {
                        if (QuitLayer.Visibility == Visibility.Visible) DismissQuit();
                        Hide();
                    }
                }));
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
        }

        private bool CanLaunchInstalledGame(Game game)
        {
            if (game.IsInstalled != false || game.Playing) return true;
            AnnounceError(game.Name + " isn't installed. Install it through " + game.Source + " to play.");
            return false;
        }

        private void ExpireLaunches(long now)
        {
            foreach (var entry in pending.Where(entry => entry.Key.Launching && now >= entry.Value.Deadline).ToArray())
                FinishLaunch(entry.Key, "Couldn't detect " + entry.Key.Name + " running.", error: true, keepDispatch: true);
            UpdateLaunchWatch();
        }

        private void UpdateLaunchWatch()
        {
            launchWatch.Stop();
            long next = pending.Where(entry => entry.Key.Launching).Select(entry => entry.Value.Deadline)
                .DefaultIfEmpty(long.MaxValue).Min();
            if (next == long.MaxValue) return;
            launchWatch.Interval = TimeSpan.FromMilliseconds(Math.Max(1, next - clock.ElapsedMilliseconds));
            launchWatch.Start();
        }

        private void FinishLaunch(Game game, string message = null, bool error = false, bool keepDispatch = false)
        {
            if (!pending.TryGetValue(game, out PendingLaunch attempt)) return;
            if (!keepDispatch || attempt.RequestSent) pending.Remove(game);
            game.SetLaunching(false);
            UpdateLaunchWatch();
            if (message != null && attempt.Number == latestRequest) Announce(message);
            if (error)
            {
                if (!IsVisible || WindowState == WindowState.Minimized) HiddenLaunchError?.Invoke(message);
                ShowErrorToast(message);
            }
        }

        internal void Announce(string message)
        {
            Status.Text = message;
            var peer = UIElementAutomationPeer.FromElement(Status);
            if (peer != null) peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        private void AnnounceError(string message)
        {
            Announce(message);
            ShowErrorToast(message);
        }

        private void PlayUiSound(UiCue cue)
        {
            if (!preferences.UiSoundsEnabled || !IsActive) return;
            var priority = cue == UiCue.Hover ? DispatcherPriority.Input : DispatcherPriority.Background;
            Dispatcher.BeginInvoke(priority, new Action(() =>
            {
                if (preferences.UiSoundsEnabled && IsActive) uiSounds.Play(cue);
            }));
        }
    }
}
