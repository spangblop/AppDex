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
            string[] terms = query.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            var matches = library.Where(game => game.Matches(terms))
                .OrderByDescending(game => string.Equals(game.Name, query, StringComparison.CurrentCultureIgnoreCase))
                .ThenByDescending(game => game.Name.StartsWith(query, StringComparison.CurrentCultureIgnoreCase))
                .ThenByDescending(game => game.Launches)
                .ThenBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
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
                    if (Results.Items.Count > 0)
                    {
                        int next = Results.SelectedIndex + (e.Key == Key.Down ? 1 : -1);
                        Results.SelectedIndex = Math.Max(0, Math.Min(next, Results.Items.Count - 1));
                        Results.ScrollIntoView(Results.SelectedItem);
                    }
                    break;
                default: return;
            }
            e.Handled = true;
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
