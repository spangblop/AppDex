using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace GameShelf
{
    internal sealed class DesktopHost
    {
        private readonly LibraryWindow window = new LibraryWindow(Game.Launch);
        private readonly string file = Path.Combine(AppBuild.DataDirectory, "activity.json");
        private readonly int session;
        private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        private ActivityBook book;
        private long lastSave;
        private bool exiting;
        private string trackingError;

        [STAThread]
        public static void Main(string[] args)
        {
            bool atSignIn = args.Contains(StartupRegistration.Argument, StringComparer.OrdinalIgnoreCase);
            string name = @"Local\" + AppBuild.Name + "-" + WindowsIdentity.GetCurrent().User.Value;
            bool first;
            using (var signal = new EventWaitHandle(false, EventResetMode.AutoReset, name + "-open"))
            using (var instance = new Mutex(true, name, out first))
            {
                if (!first)
                {
                    if (!atSignIn) signal.Set();
                    return;
                }
                var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                new DesktopHost().Run(app, signal, atSignIn);
            }
        }

        private DesktopHost()
        {
            using (var current = Process.GetCurrentProcess()) session = current.SessionId;
        }

        private void Run(Application app, WaitHandle signal, bool atSignIn)
        {
            using (var icon = Icon.ExtractAssociatedIcon(typeof(DesktopHost).Assembly.Location))
            using (var menu = new Forms.ContextMenuStrip())
            using (var tray = new Forms.NotifyIcon { Icon = icon, Text = AppBuild.Name + " · Tracking app activity", ContextMenuStrip = menu })
            {
                Action quit = delegate
                {
                    if (exiting) return;
                    exiting = true;
                    app.Shutdown();
                };
                menu.Items.Add("Open " + AppBuild.Name, null, delegate { ShowLibrary(); });
                menu.Items.Add("Quit " + AppBuild.Name, null, delegate { quit(); });
                window.QuitRequested += quit;
                window.ResetActivityRequested += ResetGameActivity;
                tray.MouseClick += delegate(object sender, Forms.MouseEventArgs e)
                {
                    if (e.Button == Forms.MouseButtons.Left) ShowLibrary();
                };
                tray.Visible = true;
                window.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
                {
                    if (exiting) return;
                    e.Cancel = true;
                    if (window.QuitOnWindowClose) window.ConfirmQuit();
                    else window.Hide();
                };
                app.SessionEnding += delegate { exiting = true; };
                var waiter = ThreadPool.RegisterWaitForSingleObject(signal, delegate
                {
                    window.Dispatcher.BeginInvoke(new Action(ShowLibrary));
                }, null, Timeout.Infinite, false);
                timer.Tick += delegate { Record(false); };
                app.Exit += delegate
                {
                    timer.Stop();
                    window.FlushPreferences();
                    Record(true);
                    waiter.Unregister(null);
                    tray.Visible = false;
                };
                Record(false);
                timer.Start();
                app.MainWindow = window;
                if (!atSignIn || !window.StartupInTray) window.Show();
                app.Run();
            }
        }

        private void ShowLibrary()
        {
            if (exiting) return;
            window.Show();
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Activate();
        }

        private void Record(bool flush)
        {
            try
            {
                long now = SampleActivity(out bool changed);
                if (!exiting) window.ShowActivity(book);
                if (flush || changed || now - lastSave >= TimeSpan.FromSeconds(30).Ticks)
                {
                    book.Save(file);
                    lastSave = now;
                }
                if (trackingError != null) window.Announce("Activity tracking resumed.");
                trackingError = null;
            }
            catch (Exception error)
            {
                if (error.Message != trackingError) window.Announce("Couldn't update app activity. " + error.Message);
                trackingError = error.Message;
            }
        }

        private long SampleActivity(out bool changed)
        {
            if (book == null) book = ActivityBook.Load(file);
            var custom = window.Games.Where(g => g.Settings.Executable != null)
                .GroupBy(g => g.Settings.Executable, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
            var sample = GameProcesses.Read(session, custom).Concat(SteamLibrary.Running(window.Games, session)).ToArray();
            long now = GameProcesses.AwakeTicks();
            changed = book.Observe(sample, now, DateTime.UtcNow.Ticks);
            return now;
        }

        private void ResetGameActivity(Game game)
        {
            // Refresh the active process identities and start the next playtime sample at the reset time.
            SampleActivity(out _);
            book.ResetGame(game.Id, file);
            window.ShowActivity(book);
        }
    }
}
