using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace GameShelf
{
    public partial class QuickLaunchWindow : Window
    {
        private readonly IEnumerable<Game> library;
        private readonly Action<Game> launch;
        private readonly Func<Game, Task<bool>> quit;
        private readonly Game[] observed;
        private bool closing;

        internal QuickLaunchWindow(IEnumerable<Game> library, Action<Game> launch, Func<Game, Task<bool>> quit = null)
        {
            this.library = library;
            this.launch = launch;
            this.quit = quit;
            InitializeComponent();
            Search(null, null);
            Loaded += delegate { Query.Focus(); };
            Closing += delegate { closing = true; };
            observed = library.ToArray();
            foreach (var game in observed) game.PropertyChanged += PlayingChanged;
            Closed += delegate { foreach (var game in observed) game.PropertyChanged -= PlayingChanged; };
        }

        private void Search(object sender, TextChangedEventArgs e)
        {
            RefreshResults(false);
        }

        private void PlayingChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Game.Playing)) RefreshResults(true);
        }

        private void RefreshResults(bool keepSelection)
        {
            if (closing || Results == null || LaunchButton == null) return;
            var selected = keepSelection ? Results.SelectedItem as Game : null;
            string query = Query.Text.Trim();
            string[] terms = GameSearch.Tokens(query);
            var matches = library.Select(game => new { Game = game, Score = game.SearchScore(terms) }).Where(item => item.Score >= 0)
                .OrderByDescending(item => item.Game.Playing)
                .ThenByDescending(item => string.Equals(item.Game.Name, query, StringComparison.CurrentCultureIgnoreCase))
                .ThenByDescending(item => item.Score)
                .ThenByDescending(item => item.Game.Launches)
                .ThenBy(item => item.Game.Name, StringComparer.CurrentCultureIgnoreCase).Select(item => item.Game).ToArray();
            Results.ItemsSource = matches;
            Results.SelectedIndex = matches.Length == 0 ? -1 : Math.Max(0, Array.IndexOf(matches, selected));
            if (Results.SelectedItem != null) Results.ScrollIntoView(Results.SelectedItem);
            NoResults.Visibility = matches.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            LaunchButton.IsEnabled = matches.Length > 0;
        }

        private void HandleKeys(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape: Close(); break;
                case Key.Enter: LaunchSelected(); break;
                case Key.Up:
                case Key.Down:
                    MoveSelection(e.Key == Key.Down ? 1 : -1);
                    break;
                default: return;
            }
            e.Handled = true;
        }

        internal void HandleController(ControllerCommand command)
        {
            if (command == ControllerCommand.Up) MoveSelection(-1);
            else if (command == ControllerCommand.Down) MoveSelection(1);
            else if (command == ControllerCommand.Select) LaunchSelected();
            else if (command == ControllerCommand.Back || command == ControllerCommand.Search) Close();
        }

        private void MoveSelection(int step)
        {
            if (Results.Items.Count == 0) return;
            Results.SelectedIndex = Math.Max(0, Math.Min(Results.SelectedIndex + step, Results.Items.Count - 1));
            Results.ScrollIntoView(Results.SelectedItem);
        }

        private void LaunchResult(object sender, RoutedEventArgs e)
        {
            if (ReferenceEquals(sender, Results) && e.OriginalSource is DependencyObject element)
            {
                var row = ItemsControl.ContainerFromElement(Results, element);
                if (row == null) return;
                for (var target = element; target is Visual && target != row; target = VisualTreeHelper.GetParent(target))
                    if (target is ButtonBase) return;
            }
            LaunchSelected();
        }

        private async void QuitResult(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            var button = (Button)sender;
            if (closing || quit == null || !(button.DataContext is Game game) || !game.Playing) return;
            button.IsEnabled = false;
            try
            {
                bool requested = await quit(game);
                ShowQuitStatus(requested ? "Asked " + game.Name + " to close." : "Couldn't find a running window for " + game.Name + ".", !requested);
            }
            catch (Exception error) { ShowQuitStatus("Couldn't close " + game.Name + ". " + error.Message, true); }
            finally { if (!closing) button.IsEnabled = true; }
        }

        private void ShowQuitStatus(string message, bool error)
        {
            if (closing) return;
            QuitStatus.SetResourceReference(TextBlock.ForegroundProperty, error ? "AppDanger" : "AppMuted");
            QuitStatus.Text = message;
            QuitStatus.Visibility = Visibility.Visible;
        }
        private void LaunchSelected()
        {
            if (!(Results.SelectedItem is Game game) || closing) return;
            Close();
            launch(game);
        }

        private void Dismiss(object sender, EventArgs e)
        {
            if (!closing) Close();
        }
    }
}
