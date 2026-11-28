using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace GameShelf
{
    public partial class LibraryWindow
    {
        private async Task OpenUpdateHistory()
        {
            if (installingUpdate) return;
            var back = SmallButton("Back to Developer settings");
            back.Click += delegate { OpenDeveloperSettings(); };
            back.Margin = new Thickness(0, 0, 7, 4);
            var refresh = SmallButton("Refresh");
            refresh.Margin = new Thickness(0, 0, 0, 4);
            var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8), Children = { back, refresh } };
            var status = DeveloperNote("");
            AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
            var rows = new StackPanel();
            var scroll = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var panel = new DockPanel { Children = { toolbar, status, scroll } };
            DockPanel.SetDock(toolbar, Dock.Top);
            DockPanel.SetDock(status, Dock.Top);
            Action reopen = () => ShowPanel("Update history", panel, back);
            refresh.Click += async delegate { await LoadUpdateHistory(rows, status, refresh, reopen); };
            reopen();
            await LoadUpdateHistory(rows, status, refresh, reopen);
        }

        private async Task LoadUpdateHistory(StackPanel rows, TextBlock status, Button refresh, Action reopen)
        {
            refresh.IsEnabled = false;
            status.Visibility = Visibility.Visible;
            status.Text = "Loading releases from GitHub…";
            rows.Children.Clear();
            try
            {
                AppRelease[] releases;
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25)))
                    releases = await AppUpdates.GetHistory(timeout.Token);
                status.Text = releases.Length == 0 ? "No published releases were found." : "";
                if (releases.Length != 0) status.Visibility = Visibility.Collapsed;
                foreach (var release in releases)
                {
                    var title = new TextBlock { Text = release.Tag, FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 2, 8, 4), TextWrapping = TextWrapping.Wrap };
                    var labels = new WrapPanel { Children = { title } };
                    foreach (string warning in UpdateFeatureWarnings(release))
                        labels.Children.Add(UpdateWarningTag(warning));

                    var row = new StackPanel { Children = { labels } };
                    bool installed = release.Version == AppUpdates.BuildVersion;
                    if (installed || release.Package == null)
                    {
                        var note = DeveloperNote(installed ? "Current version" : "Windows ZIP unavailable");
                        note.Margin = new Thickness(0);
                        row.Children.Add(note);
                    }
                    else
                    {
                        var install = SmallButton(release.Version < AppUpdates.BuildVersion ? "Downgrade…" : "Install…");
                        AutomationProperties.SetName(install, (string)install.Content + " " + release.Tag);
                        install.HorizontalAlignment = HorizontalAlignment.Left;
                        install.Margin = new Thickness(0, 3, 0, 0);
                        install.Click += delegate { ShowUpdatePanel(release.Package, release, reopen); };
                        row.Children.Add(install);
                    }

                    var card = new Border { Padding = new Thickness(10), Margin = new Thickness(0, 0, 6, 7),
                        BorderThickness = new Thickness(1), Child = row };
                    card.SetResourceReference(BorderBrushProperty, "AppEdge");
                    card.SetResourceReference(Border.CornerRadiusProperty, "AppControlCorner");
                    rows.Children.Add(card);
                }
            }
            catch (Exception error) when (error is HttpRequestException || error is OperationCanceledException ||
                error is IOException || error is SerializationException)
            {
                status.Text = "Couldn't load update history. " +
                    (error is OperationCanceledException ? "The request timed out." : error.Message);
            }
            finally { refresh.IsEnabled = true; }
        }

        private static IEnumerable<string> UpdateFeatureWarnings(AppRelease release)
        {
            if (release.Version == null) yield break;
            if (!release.HasDeveloperSettings) yield return "Loses Developer settings";
            if (!release.HasInAppUpdates) yield return "Loses in-app updates";
        }

        private static Border UpdateWarningTag(string warning)
        {
            return new Border {
                Background = new SolidColorBrush(Color.FromRgb(77, 27, 32)), CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 0, 6, 4),
                Child = new TextBlock { Text = warning, Foreground = new SolidColorBrush(Color.FromRgb(255, 181, 177)),
                    FontSize = 11, FontWeight = FontWeights.SemiBold }
            };
        }
    }
}
