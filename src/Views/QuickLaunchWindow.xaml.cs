using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GameShelf
{
    public partial class QuickLaunchWindow : Window
    {
        private readonly IEnumerable<Game> library;
        private readonly Action<Game> launch;
        private bool closing;

        internal QuickLaunchWindow(IEnumerable<Game> library, Action<Game> launch)
        {
            this.library = library;
            this.launch = launch;
            InitializeComponent();
            Search(null, null);
            Loaded += delegate { Query.Focus(); };
            Closing += delegate { closing = true; };
        }

        private void Search(object sender, TextChangedEventArgs e)
        {
            if (Results == null || LaunchButton == null) return;
            string query = Query.Text.Trim();
            string[] terms = GameSearch.Tokens(query);
            var matches = library.Select(game => new { Game = game, Score = game.SearchScore(terms) }).Where(item => item.Score >= 0)
                .OrderByDescending(item => string.Equals(item.Game.Name, query, StringComparison.CurrentCultureIgnoreCase))
                .ThenByDescending(item => item.Score)
                .ThenByDescending(item => item.Game.Launches)
                .ThenBy(item => item.Game.Name, StringComparer.CurrentCultureIgnoreCase).Select(item => item.Game).ToArray();
            Results.ItemsSource = matches;
            Results.SelectedIndex = matches.Length == 0 ? -1 : 0;
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
            if (ReferenceEquals(sender, Results) && (e.OriginalSource is DependencyObject element
                && ItemsControl.ContainerFromElement(Results, element) == null)) return;
            LaunchSelected();
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
