using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace GameShelf
{
    public partial class LibraryWindow
    {
        private void ShowShortcutManager()
        {
            var rows = new StackPanel();
            AddShortcutRow(rows, "Quick Launch", QuickLaunchModifiers == 0 ? "None" :
                LaunchHotkeys.Describe(QuickLaunchModifiers, QuickLaunchKey),
                EditQuickShortcut, QuickLaunchModifiers == 0 ? (Action)null : ClearQuickShortcut);
            foreach (var game in Games.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                if (game.Settings.ShortcutKey != 0) AddGameShortcutRow(rows, game, null);
                foreach (var profile in game.Settings.LaunchProfiles.Where(item => item.ShortcutKey != 0))
                    AddGameShortcutRow(rows, game, profile);
            }
            if (rows.Children.Count == 1)
            {
                var empty = new TextBlock { Text = "No app or profile shortcuts assigned.", Margin = new Thickness(0, 6, 0, 0) };
                empty.SetResourceReference(ForegroundProperty, "AppMuted");
                rows.Children.Add(empty);
            }
            ShowPanel("Shortcut Manager", new ScrollViewer { Content = rows, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        }

        private void EditQuickShortcut()
        {
            OpenSettings(null, null);
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
            {
                if (DrawerContent.Content is SettingsPane settings) settings.FocusQuickShortcut();
            }));
        }

        private void AddGameShortcutRow(Panel rows, Game game, LaunchProfile profile)
        {
            string name = profile == null ? game.Name : game.Name + " · " + profile.Name;
            AddShortcutRow(rows, name, LaunchHotkeys.Describe(profile?.ShortcutModifiers ?? game.Settings.ShortcutModifiers,
                profile?.ShortcutKey ?? game.Settings.ShortcutKey), () => EditProfile(game, profile, true),
                () => ClearGameShortcut(game, profile));
        }

        private void AddShortcutRow(Panel rows, string name, string shortcut, Action edit, Action clear)
        {
            var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(6, 0, 0, 0) };
            var editButton = SmallButton("Edit");
            editButton.Click += delegate { edit(); };
            actions.Children.Add(editButton);
            if (clear != null)
            {
                var clearButton = SmallButton("Clear");
                clearButton.Margin = new Thickness(5, 0, 0, 0);
                clearButton.Click += delegate { clear(); };
                actions.Children.Add(clearButton);
            }
            var label = new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            var keys = new TextBlock { Text = shortcut, TextWrapping = TextWrapping.Wrap };
            keys.SetResourceReference(ForegroundProperty, "AppMuted");
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 9) };
            DockPanel.SetDock(actions, Dock.Right);
            row.Children.Add(actions);
            row.Children.Add(new StackPanel { Children = { label, keys } });
            rows.Children.Add(row);
        }

        private void ClearQuickShortcut()
        {
            string error = RegisterQuickShortcut?.Invoke(0, 0);
            if (error != null) { AnnounceError(error); return; }
            preferences.QuickLaunchModifiers = preferences.QuickLaunchKey = 0;
            QueuePreferences();
            ShowShortcutManager();
        }

        private void ClearGameShortcut(Game game, LaunchProfile profile)
        {
            try
            {
                var draft = game.Settings.Copy();
                if (profile == null) draft.ShortcutModifiers = draft.ShortcutKey = 0;
                else
                {
                    var selected = draft.Profiles.First(item => item.Name == profile.Name);
                    selected.ShortcutModifiers = selected.ShortcutKey = 0;
                }
                catalog.Save(draft, null);
                GameShortcutsChanged?.Invoke();
                ShowShortcutManager();
            }
            catch (Exception error) { AnnounceError(error.Message); }
        }
    }
}
