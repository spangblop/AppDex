using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace GameShelf
{
    public partial class LibraryWindow
    {
        private static readonly string[] Sources = { null, "Steam", "Windows", "Epic Games" };
        private readonly List<string> visibleIds = new List<string>();
        private readonly Dictionary<string, Button> tiles = new Dictionary<string, Button>();
        private readonly DispatcherTimer trayArtworkCleanup = new DispatcherTimer(DispatcherPriority.ContextIdle)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        private bool libraryVisible;
        private double hiddenLibraryOffset, hiddenPinnedOffset;
        private string activeGroupId;
        private string libraryQuery = "";
        private double libraryOffset;
        private bool switchingScope;
        private Point? lastGameHoverPosition;

        private AppGroup ActiveGroup { get { return preferences.Groups.FirstOrDefault(g => g.Id == activeGroupId); } }

        private void RenderLibrary()
        {
            if (tileDrag != null) EndTileDrag(false);
            activityOrderDirty = false;
            var all = Games.ToDictionary(g => g.Id);
            preferences.Sync(all.Keys);
            if (!libraryVisible) return;
            if (activeGroupId != null && ActiveGroup == null) RestoreLibraryScope();
            RenderPins(all);
            RenderArrangement(all);
            UpdateTotalPlaytime();
        }

        private void SetLibraryVisible(bool shown)
        {
            if (libraryVisible == shown) return;
            libraryVisible = shown;
            if (shown)
            {
                trayArtworkCleanup.Stop();
                if (libraryWasHidden && UpdateTrayCoordinator == null)
                {
                    using (var process = System.Diagnostics.Process.GetCurrentProcess())
                        LastTrayWorkingSet = process.WorkingSet64;
                }
                AppAppearance.SetWallpaper(preferences.Appearance, true);
                RenderLibrary();
                LibraryScroll.ScrollToVerticalOffset(hiddenLibraryOffset);
                PinnedScroll.ScrollToVerticalOffset(hiddenPinnedOffset);
                return;
            }

            libraryWasHidden = true;
            hiddenLibraryOffset = LibraryScroll.VerticalOffset;
            hiddenPinnedOffset = PinnedScroll.VerticalOffset;
            EndTileDrag(false);
            CloseMenu();
            HighlightAppearance(AppearanceTarget.None);
            panelFocus = null;
            ClearTiles();
            Pinned.Children.Clear();
            foreach (Game game in Games) game.ReleaseArtwork();
            AppAppearance.SetWallpaper(preferences.Appearance, false);
            trayArtworkCleanup.Start();
        }

        private void ClearTiles()
        {
            Covers.Children.Clear();
            tiles.Clear();
            visible.Clear();
            visibleIds.Clear();
        }

        private void RenderArrangement(Dictionary<string, Game> all)
        {
            string[] terms = GameSearch.Tokens(SearchBox.Text);
            string source = Sources[Math.Max(0, SourcePicker.SelectedIndex)];
            var group = ActiveGroup;
            var scope = group == null ? all.Values : group.Apps.Select(id => all[id]);
            var matching = new HashSet<string>(scope.Where(game => (source == null || game.Source == source) && game.Matches(terms))
                .Select(game => game.Id));
            ClearTiles();
            int shown = matching.Count;
            if (group != null)
            {
                foreach (string id in group.Apps.Where(matching.Contains)) AddTile(all[id]);
            }
            else if (preferences.Sort == 2)
            {
                shown = 0;
                var groups = preferences.Groups.ToDictionary(item => item.Id);
                foreach (string id in preferences.Order)
                {
                    AppGroup folder;
                    if (groups.TryGetValue(id, out folder))
                    {
                        bool titleMatch = terms.Length > 0 && terms.All(term =>
                            folder.Name.IndexOf(term, StringComparison.CurrentCultureIgnoreCase) >= 0);
                        int matches = folder.Apps.Count(app => matching.Contains(app) || titleMatch &&
                            (source == null || all[app].Source == source));
                        if (matches == 0) continue;
                        AddGroupTile(folder, all);
                        shown += matches;
                    }
                    else if (matching.Contains(id))
                    {
                        AddTile(all[id]);
                        shown++;
                    }
                }
            }
            else
            {
                var ordered = preferences.Sort == 1 ? Games.OrderByDescending(game => game.PlayTicks).ThenByDescending(game => game.Launches)
                    : Games.OrderByDescending(game => game.Launches).ThenByDescending(game => game.PlayTicks);
                foreach (var game in ordered.Where(game => matching.Contains(game.Id))) AddTile(game);
            }
            if (visible.Count == 0) Covers.Children.Add(new TextBlock {
                Text = all.Count == 0 ? "Add an app or import a library to get started." : "No matching apps",
                FontSize = 19, Margin = new Thickness(4, 19, 0, 0), TextWrapping = TextWrapping.Wrap });
            int count = group == null ? all.Count : group.Apps.Count;
            CountText.Text = shown == count ? AppCount(count) : shown + " of " + AppCount(count);
            ViewTitle.Text = group == null ? "Library" : group.Name;
            BackToLibrary.Visibility = group == null ? Visibility.Collapsed : Visibility.Visible;
            Status.Text = catalog.LoadError ?? (preferences.Sort == 2
                ? group == null ? "Drop in the center to group apps. Drop at an edge to reorder."
                    : "Drag to reorder apps in this group."
                : "Right-click an app to open its options.");
            SearchHint.Text = group == null ? "Search apps" : "Search this group";
            SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            ResizeLibrary();
        }

        private static string AppCount(int count) { return count + (count == 1 ? " app" : " apps"); }

        private void GoHome(object sender, RoutedEventArgs e)
        {
            if (AppBuild.IsPreview && ReferenceEquals(sender, LibraryHome)) ShowPreviewNotice(LibraryHome);
            else NavigateToLibrary();
        }

        private void ShowGroup(AppGroup group)
        {
            if (activeGroupId == null)
            {
                libraryQuery = SearchBox.Text;
                libraryOffset = LibraryScroll.VerticalOffset;
            }
            activeGroupId = group.Id;
            SetLibraryControls("", 0);
            LibraryScroll.ScrollToTop();
            RenderLibrary();
            AnimateView(12);
            BackToLibrary.Focus();
        }

        private void RestoreLibraryScope()
        {
            activeGroupId = null;
            SetLibraryControls(libraryQuery, preferences.Source);
        }

        private void NavigateToLibrary()
        {
            if (activeGroupId == null) return;
            RestoreLibraryScope();
            RenderLibrary();
            LibraryScroll.ScrollToVerticalOffset(libraryOffset);
            AnimateView(-12);
            LibraryHome.Focus();
        }

        private void SetLibraryControls(string query, int source)
        {
            switchingScope = true;
            SearchBox.Text = query;
            SourcePicker.SelectedIndex = source;
            switchingScope = false;
        }

        private void AnimateView(double offset)
        {
            var transform = new TranslateTransform();
            Covers.RenderTransform = transform;
            AnimateOffset(transform, TranslateTransform.YProperty, offset, 0, 220);
            Covers.BeginAnimation(OpacityProperty, Motion(.55, 1, 190));
        }

        private void RenderPins(Dictionary<string, Game> all)
        {
            Pinned.Children.Clear();
            foreach (string id in preferences.Pins)
            {
                Game app = all[id];
                var button = SmallButton(app.Name);
                button.DataContext = app;
                button.Margin = new Thickness(0, 0, 0, 7);
                button.Padding = new Thickness(0);
                button.Height = preferences.Appearance.PinHeight;
                button.RenderTransform = new TranslateTransform();
                button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                button.VerticalContentAlignment = VerticalAlignment.Stretch;
                var artwork = new Image { Stretch = preferences.Appearance.Crop, Opacity = preferences.Appearance.PinArtwork };
                artwork.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding("Artwork") { Source = app });
                var shade = new Border { Background = new LinearGradientBrush(
                    Color.FromArgb(238, 13, 17, 24), Color.FromArgb(40, 13, 17, 24), 344) };
                var label = new TextBlock { Text = app.Name, FontSize = 13, FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0) };
                label.SetResourceReference(TextBlock.FontSizeProperty, "AppBodySize");
                var spinner = new LaunchSpinner {
                    Style = (Style)FindResource("TileLaunchSpinner"), Margin = new Thickness(0, 0, 5, 0)
                };
                var caption = new DockPanel { Children = { spinner, label } };
                DockPanel.SetDock(spinner, Dock.Right);
                var face = new Grid { ClipToBounds = true, Children = { artwork, shade, caption } };
                button.Content = new Grid { Children = { face, new TileLaunchCue() } };
                button.Click += delegate { Launch(app, button); };
                RegisterDragTarget(button, app.Id);
                AttachGameHoverSound(button);
                AttachOptions(button, () => AppOptions(app));
                var launchDescription = new System.Windows.Data.Binding("AccessibleName");
                button.SetBinding(ToolTipProperty, launchDescription);
                button.SetBinding(AutomationProperties.NameProperty, launchDescription);
                Pinned.Children.Add(button);
            }
            if (preferences.Pins.Count == 0)
            {
                var hint = new TextBlock { Text = "Drag an app here or right-click it to pin it.", FontSize = 12, TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(6, 1, 0, 0) };
                hint.SetResourceReference(ForegroundProperty, "AppMuted");
                Pinned.Children.Add(hint);
            }
        }

        private Button SmallButton(string text)
        {
            return new Button { Content = text, Style = (Style)FindResource("ActionButton"), Padding = new Thickness(8, 5, 8, 5) };
        }

        private void AddTile(Game app)
        {
            var appearance = preferences.Appearance;
            var button = new Button { DataContext = app,
                Style = (Style)FindResource(AppAppearance.Card(appearance.Layout)), Tag = AppAppearance.LayoutTag(appearance.Layout) };
            button.SetBinding(ToolTipProperty, new System.Windows.Data.Binding("AccessibleName"));
            button.Click += Play;
            TileHover.Attach(button, appearance);
            AttachGameHoverSound(button);
            AttachOptions(button, () => AppOptions(app));
            RegisterTile(app.Id, button);
        }

        private void AttachGameHoverSound(Button tile)
        {
            bool entered = false;
            tile.MouseLeave += delegate { entered = false; };
            MouseEventHandler hover = delegate(object sender, MouseEventArgs e)
            {
                if (!tile.IsEnabled || tileDrag != null || e.LeftButton == MouseButtonState.Pressed) return;

                Point position = e.GetPosition(Page);
                if (lastGameHoverPosition == position) return;
                lastGameHoverPosition = position;

                if (entered || !new Rect(tile.RenderSize).Contains(e.GetPosition(tile))) return;
                entered = true;
                PlayUiSound(UiCue.Hover);
            };
            tile.MouseEnter += hover;
            tile.MouseMove += hover;
        }

        private void AddGroupTile(AppGroup group, Dictionary<string, Game> all)
        {
            var button = new Button { DataContext = group, Style = (Style)FindResource("GroupCoverButton"),
                Tag = AppAppearance.LayoutTag(preferences.Appearance.Layout) };
            var gallery = new UniformGrid { Rows = 1, Columns = Math.Min(3, group.Apps.Count), ClipToBounds = true };
            foreach (string id in group.Apps.Take(3))
            {
                gallery.Children.Add(new Border { Background = new ImageBrush(all[id].Artwork) {
                    Stretch = preferences.Appearance.Crop, Opacity = preferences.Appearance.ArtworkOpacity },
                    Margin = new Thickness(0, 0, 1, 0) });
            }
            button.Content = gallery;
            AutomationProperties.SetName(button, "Open group " + group.Name + ", " + AppCount(group.Apps.Count));
            button.ToolTip = "Open " + group.Name;
            button.Click += delegate(object sender, RoutedEventArgs e)
            {
                if (ReferenceEquals(sender, e.OriginalSource)) ShowGroup(group);
            };
            TileHover.Attach(button, preferences.Appearance);
            AttachOptions(button, () => GroupOptions(group));
            RegisterTile(group.Id, button);
        }

        private void RegisterTile(string id, Button tile)
        {
            tile.Margin = new Thickness(0, 0, preferences.Appearance.Gap, preferences.Appearance.Gap);
            tile.RenderTransform = new TranslateTransform();
            tiles.Add(id, tile);
            visible.Add(tile);
            visibleIds.Add(id);
            Covers.Children.Add(tile);
            RegisterDragTarget(tile, id);
        }

        private MenuChoice[] AppOptions(Game app)
        {
            var items = new List<MenuChoice> {
                new MenuChoice(app.Playing ? "Resume app" : "Open app", () => Launch(app)),
                new MenuChoice("Edit app…", () => Edit(app)),
                new MenuChoice(preferences.Pins.Contains(app.Id) ? "Unpin app" : "Pin app", () =>
                {
                    if (!preferences.Pins.Remove(app.Id)) preferences.Pins.Add(app.Id);
                    CommitPreferences();
                })
            };
            items.InsertRange(1, app.Settings.LaunchProfiles.Select(profile =>
                new MenuChoice("Launch profile: " + profile.Name, () => LaunchChoice(app, profile))));
            if (preferences.Sort == 2 && preferences.GroupOf(app.Id) != null)
                items.Add(new MenuChoice("Move to library", () => { preferences.Ungroup(app.Id); CommitPreferences(); }));
            items.Add(new MenuChoice("Remove from AppDex…", () => RequestRemoval(app)));
            return items.ToArray();
        }

        private MenuChoice[] GroupOptions(AppGroup group)
        {
            return new[] {
                new MenuChoice("Open group", () => ShowGroup(group)),
                new MenuChoice("Rename group…", () => RenameGroup(group)),
                new MenuChoice("Dissolve group", () => { preferences.Dissolve(group); CommitPreferences(); })
            };
        }

        private void GroupMore(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            var button = (Button)sender;
            ShowMenu(button, null, GroupOptions((AppGroup)button.DataContext));
        }

        private void RenameGroup(AppGroup group)
        {
            var name = new TextBox { Text = group.Name, Margin = new Thickness(0, 6, 0, 12), Padding = new Thickness(8) };
            var save = SmallButton("Save name");
            save.IsDefault = true;
            var layout = new StackPanel { Children = { new TextBlock { Text = "Group name" }, name, save } };
            save.Click += delegate
            {
                if (string.IsNullOrWhiteSpace(name.Text)) return;
                group.Name = name.Text.Trim();
                ClosePanel();
                CommitPreferences();
            };
            ShowPanel("Rename group", layout);
            name.Focus();
            name.SelectAll();
        }

        private void RequestRemoval(Game app)
        {
            var question = new TextBlock { Text = RemovalQuestion(app), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
            var keep = SmallButton("Keep app");
            var remove = SmallButton("Remove app");
            remove.SetResourceReference(ForegroundProperty, "AppDanger");
            keep.Margin = new Thickness(0, 0, 8, 0);
            var actions = new WrapPanel { Children = { keep, remove } };
            var layout = new StackPanel { Children = { question, actions } };
            keep.Click += delegate { ClosePanel(); };
            remove.Click += delegate
            {
                try { catalog.Remove(app); ClosePanel(); AfterRemoval(app); }
                catch (Exception error) { question.Text = "Couldn't remove the app. " + error.Message; }
            };
            ShowPanel("Remove app", layout);
            keep.Focus();
        }

        internal static string RemovalQuestion(Game app) { return "Remove " + app.Name + " from the AppDex library?"; }

        private void AfterRemoval(Game app)
        {
            FinishLaunch(app);
            lastClick.Remove(app);
            CommitPreferences();
            Announce("Removed " + app.Name + " from the library.");
            GameShortcutsChanged?.Invoke();
        }

        private void ResizeLibrary()
        {
            double available = Page.ActualWidth;
            if (available > 0)
            {
                var options = preferences.Appearance;
                bool right = options.Sidebar == 1;
                double rail = options.Sidebar == 2 ? 0 : Math.Min(options.SidebarWidth, Math.Max(86, available * .32));
                SidebarColumn.Width = right ? new GridLength(1, GridUnitType.Star) : new GridLength(rail);
                LibraryColumn.Width = right ? new GridLength(rail) : new GridLength(1, GridUnitType.Star);
                Grid.SetColumn(PinnedRail, right ? 1 : 0);
                Grid.SetColumn(LibraryArea, right ? 0 : 1);
                PinnedRail.Visibility = rail == 0 ? Visibility.Collapsed : Visibility.Visible;
                PinnedRail.BorderThickness = right ? new Thickness(1, 0, 0, 0) : new Thickness(0, 0, 1, 0);
                double workspace = Math.Max(75, available - rail - 32);
                SearchField.Width = Math.Min(options.SearchWidth, workspace);
                SortPicker.MinWidth = Math.Min(141, workspace);
                VersionLabel.Visibility = available >= 460 ? Visibility.Visible : Visibility.Collapsed;
                AppTitle.FontSize = available >= 320 ? options.HeadingSize - 2 : Math.Min(19, options.HeadingSize - 2);
                AppMark.Visibility = available >= 265 ? Visibility.Visible : Visibility.Collapsed;
                TotalPlaytimeLabel.Text = available >= 295 ? "Total playtime" : "Playtime";
                Drawer.Width = Math.Min(490, available);
            }
            ResizeTiles();
        }

        private void ResizeTiles()
        {
            double viewport = LibraryScroll.ViewportWidth > 0 ? LibraryScroll.ViewportWidth : 350;
            double width = Math.Max(40, viewport - Covers.Margin.Left - Covers.Margin.Right - 2);
            var appearance = preferences.Appearance;
            int columns = appearance.Columns == 0 ? Math.Max(1, (int)(width / appearance.Width))
                : Math.Min(appearance.Columns, Math.Max(1, (int)(width / (90 + appearance.Gap))));
            double tileWidth = Math.Floor(Math.Max(32, Math.Min(width / columns - appearance.Gap, appearance.MaximumWidth)));
            double tileHeight = appearance.Height;
            if (tileDrag != null && visible.Count > 0 && Math.Abs(visible[0].Width - tileWidth) > 1) EndTileDrag(false);
            foreach (Button tile in visible)
            {
                tile.Width = tileWidth;
                tile.Height = tileHeight;
            }
        }
    }
}
