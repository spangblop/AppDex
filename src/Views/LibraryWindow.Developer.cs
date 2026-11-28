using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace GameShelf
{
    public partial class LibraryWindow
    {
        internal long LastTrayWorkingSet;
        private bool libraryWasHidden;

        private void DeveloperLogoClick(object sender, MouseButtonEventArgs e)
        {
            if (TryOpenDeveloperSettings(e.ClickCount, Keyboard.Modifiers)) e.Handled = true;
        }

        private bool TryOpenDeveloperSettings(int clickCount, ModifierKeys modifiers)
        {
            if (clickCount != 3 || (modifiers & ModifierKeys.Shift) == 0) return false;
            OpenDeveloperSettings();
            return true;
        }

        private void OpenDeveloperSettings()
        {
            var dragTargets = DeveloperToggle("Show drag targets", preferences.ShowDragTargets,
                enabled => preferences.ShowDragTargets = enabled);
            var frameTiming = DeveloperToggle("Drag frame timing", preferences.ShowDragFrameTiming,
                enabled => preferences.ShowDragFrameTiming = enabled);
            var trayMemory = DeveloperToggle("Show tray memory", preferences.ShowTrayMemory,
                enabled => preferences.ShowTrayMemory = enabled);
            var memory = DeveloperNote("");
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += delegate { memory.Text = DescribeTrayMemory(); };
            var status = DeveloperNote("");
            status.Visibility = Visibility.Collapsed;
            AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
            var check = SmallButton("Check for updates (debug)");
            check.HorizontalAlignment = HorizontalAlignment.Left;
            check.Click += async delegate { await RunUpdateDiagnostics(check, status); };
            var toast = SmallButton("Test toast notification");
            toast.HorizontalAlignment = HorizontalAlignment.Left;
            toast.Margin = new Thickness(0, 10, 0, 0);
            toast.Click += delegate { ShowErrorToast(" Working notification that tells you things aren't working"); };
            var history = SmallButton("Update history");
            history.HorizontalAlignment = HorizontalAlignment.Left;
            history.Margin = new Thickness(0, 10, 0, 0);
            history.Click += async delegate { await OpenUpdateHistory(); };
            var feedback = SmallButton("Feature request");
            feedback.HorizontalAlignment = HorizontalAlignment.Left;
            feedback.Margin = new Thickness(0, 10, 0, 0);
            feedback.Click += delegate { OpenFeatureRequest(); };

            var panel = new StackPanel { Children = {
                dragTargets,
                DeveloperNote("Cyan shows reorder slots in Custom order, green shows grouping zones, and amber shows pin targets."),
                frameTiming, trayMemory, memory, check, status, history, toast, feedback
            } };
            Action refreshMemory = delegate
            {
                bool enabled = trayMemory.IsChecked == true;
                memory.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
                if (enabled && panel.IsVisible)
                {
                    memory.Text = DescribeTrayMemory();
                    timer.Start();
                }
                else timer.Stop();
            };
            trayMemory.Click += delegate { refreshMemory(); };
            panel.IsVisibleChanged += delegate { refreshMemory(); };
            panel.Unloaded += delegate { timer.Stop(); };
            refreshMemory();

            ShowPanel("Developer settings", new ScrollViewer {
                Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            }, dragTargets);
        }

        private CheckBox DeveloperToggle(string label, bool value, Action<bool> save)
        {
            var toggle = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 0, 0, 6) };
            toggle.SetResourceReference(ForegroundProperty, "AppText");
            toggle.Click += delegate { save(toggle.IsChecked == true); QueuePreferences(); };
            return toggle;
        }

        private TextBlock DeveloperNote(string text)
        {
            var note = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
            note.SetResourceReference(ForegroundProperty, "AppMuted");
            return note;
        }

        private void OpenFeatureRequest()
        {
            var title = FeatureRequestField("Title", 120);
            var details = FeatureRequestField("What would you like AppDex to do?", 1500);
            details.AcceptsReturn = true;
            details.TextWrapping = TextWrapping.Wrap;
            details.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            details.Height = 112;
            var back = SmallButton("Back");
            back.Margin = new Thickness(0, 0, 7, 5);
            back.Click += delegate { OpenDeveloperSettings(); };
            var review = SmallButton("Review on GitHub");
            review.Margin = new Thickness(0, 0, 0, 5);
            review.SetResourceReference(BackgroundProperty, "AppAccent");
            review.SetResourceReference(ForegroundProperty, "AppAccentInk");
            var status = DeveloperNote("");
            status.SetResourceReference(TextBlock.ForegroundProperty, "AppDanger");
            AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
            var fields = new StackPanel { Children = {
                new Label { Content = "Title", Target = title }, title,
                new Label { Content = "What would you like AppDex to do?", Target = details, Margin = new Thickness(0, 7, 0, 0) }, details,
                new TextBlock { Text = "You’ll need to sign in to GitHub to submit the request. Submitted issues are public.",
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 12) },
                new WrapPanel { Children = { back, review } }, status
            } };
            review.Click += delegate
            {
                try
                {
                    using (Process.Start(new ProcessStartInfo(FeatureRequestUrl(title.Text, details.Text)) { UseShellExecute = true })) { }
                    status.Text = "";
                }
                catch (Exception error) when (error is ArgumentException || error is Win32Exception || error is InvalidOperationException)
                {
                    status.Text = error.Message;
                }
            };
            ShowPanel("Feature request", new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, title);
        }

        private static TextBox FeatureRequestField(string name, int maxLength)
        {
            var field = new TextBox { MaxLength = maxLength, Padding = new Thickness(7, 5, 7, 5) };
            field.SetResourceReference(Control.ForegroundProperty, "AppText");
            field.SetResourceReference(Control.BackgroundProperty, "AppControl");
            field.SetResourceReference(Control.BorderBrushProperty, "AppEdge");
            field.SetResourceReference(TextBox.CaretBrushProperty, "AppText");
            AutomationProperties.SetName(field, name);
            return field;
        }

        private static string FeatureRequestUrl(string title, string details)
        {
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(details))
                throw new ArgumentException("Enter a title and describe your request.");
            string url = "https://github.com/spangblop/AppDex/issues/new?title=" +
                Uri.EscapeDataString(title.Trim()) + "&body=" + Uri.EscapeDataString(details.Trim());
            if (url.Length > 7500) throw new ArgumentException("Shorten the request before opening the GitHub draft.");
            return url;
        }

        private async Task RunUpdateDiagnostics(Button button, TextBlock status)
        {
            button.IsEnabled = false;
            status.Visibility = Visibility.Visible;
            string installed = "Installed build " + AppUpdates.CurrentLabel;
            status.Text = installed + "\nChecking GitHub…";
            try
            {
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25)))
                {
                    var report = await AppUpdates.CheckForDiagnostics(timeout.Token);
                    var newest = report.NewestInstallable?.Version;
                    string result = newest == null ? "No installable release package was found."
                        : newest > AppUpdates.BuildVersion ? "An update is available."
                        : "No newer installable release is available.";
                    status.Text = installed + "\nNewest published release " + (report.NewestPublished?.ToString(3) ?? "none")
                        + "\nNewest installable package " + (newest?.ToString(3) ?? "none") + "\n" + result;
                    if (report.NewestPublished != null && (newest == null || report.NewestPublished > newest))
                        status.Text += "\nThe newest release has no eligible Windows ZIP.";
                }
            }
            catch (Exception error) when (error is HttpRequestException || error is OperationCanceledException ||
                error is IOException || error is SerializationException)
            {
                status.Text = installed + "\nCheck failed. " + (error is OperationCanceledException ? "The request timed out." : error.Message);
            }
            finally { button.IsEnabled = true; }
        }

        private string DescribeTrayMemory()
        {
            using (var process = Process.GetCurrentProcess())
            {
                string result = UpdateTrayCoordinator == null
                    ? "Combined library and tray process  " + FormatWorkingSet(process)
                    : "Library process  " + FormatWorkingSet(process)
                        + "\nTray coordinator  " + FormatWorkingSet(UpdateTrayCoordinator);
                return result + (LastTrayWorkingSet > 0
                    ? "\nLast tray-only reading  " + FormatWorkingSet(LastTrayWorkingSet)
                    : "\nHide AppDex to the tray and reopen it to capture a tray-only reading.");
            }
        }

        private static string FormatWorkingSet(Process process)
        {
            try { process.Refresh(); return process.HasExited ? "stopped" : FormatWorkingSet(process.WorkingSet64); }
            catch (Exception error) when (error is InvalidOperationException || error is Win32Exception) { return "unavailable"; }
        }

        private static string FormatWorkingSet(long bytes) => (bytes / 1048576d).ToString("0.0") + " MiB working set";
    }
}
