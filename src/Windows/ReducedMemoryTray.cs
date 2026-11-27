using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;
using System.Threading;
using Forms = System.Windows.Forms;

namespace GameShelf
{
    internal sealed class ReducedMemoryTray
    {
        internal const int SleepExitCode = 10;
        private const string WindowArgument = "--reduced-memory-window";
        private const string ViewSignalPrefix = @"Local\AppDex.ReducedMemory.Window.";
        private const int TrayWorkingSetOffset = sizeof(long);
        private const int SharedStatusSize = TrayWorkingSetOffset + sizeof(long);
        private readonly string activityFile = Path.Combine(AppBuild.DataDirectory, "activity.json");
        private readonly int session;
        private readonly Forms.NotifyIcon tray;
        private readonly Forms.ToolStripItem quickItem;
        private readonly Forms.Timer timer;
        private readonly EventWaitHandle reveal;
        private readonly MemoryMappedViewAccessor activityHandoff;
        private readonly string viewId;
        private LaunchHotkeys hotkeys;
        private GameCatalog catalog;
        private ActivityBook activity;
        private string trackingError;
        private long lastSave;
        private Process window;
        private bool stopping;

        private ReducedMemoryTray(Forms.NotifyIcon tray, Forms.ToolStripItem quickItem, Forms.Timer timer,
            EventWaitHandle reveal, MemoryMappedViewAccessor activityHandoff, string viewId)
        {
            this.tray = tray;
            this.quickItem = quickItem;
            this.timer = timer;
            this.reveal = reveal;
            this.activityHandoff = activityHandoff;
            this.viewId = viewId;
            using (var process = Process.GetCurrentProcess()) session = process.SessionId;
        }

        internal static bool TryRunWindow(string[] args)
        {
            if (args.Length == 0 || args[0] != WindowArgument) return false;
            if (args.Length < 3 || !Guid.TryParseExact(args[1], "N", out _) || !int.TryParse(args[2], out int ownerId))
                return true;
            using (var owner = Process.GetProcessById(ownerId))
            using (var reveal = EventWaitHandle.OpenExisting(ViewSignalPrefix + args[1]))
            using (var shared = MemoryMappedFile.OpenExisting(ViewSignalPrefix + args[1] + ".Activity"))
            using (var sample = shared.CreateViewAccessor(0, SharedStatusSize))
                DesktopHost.RunReducedMemoryWindow(reveal, sample, owner, args.Skip(3).ToArray());
            return true;
        }

        internal static void Run(EventWaitHandle open, bool atSignIn, LibraryPreferences preferences)
        {
            string viewId = Guid.NewGuid().ToString("N");
            using (var reveal = new EventWaitHandle(false, EventResetMode.AutoReset, ViewSignalPrefix + viewId))
            using (var shared = MemoryMappedFile.CreateNew(ViewSignalPrefix + viewId + ".Activity", SharedStatusSize))
            using (var sample = shared.CreateViewAccessor(0, SharedStatusSize))
            using (var dispatch = new Forms.Control())
            using (var menu = new Forms.ContextMenuStrip())
            using (var icon = Icon.ExtractAssociatedIcon(typeof(ReducedMemoryTray).Assembly.Location))
            using (var tray = new Forms.NotifyIcon { Icon = icon, Text = AppBuild.Name, ContextMenuStrip = menu })
            using (var timer = new Forms.Timer())
            {
                menu.Items.Add("Open " + AppBuild.Name);
                var quick = menu.Items.Add("Quick launch");
                var quit = menu.Items.Add("Quit " + AppBuild.Name);
                var host = new ReducedMemoryTray(tray, quick, timer, reveal, sample, viewId);
                menu.Items[0].Click += delegate { host.ShowWindow(); };
                quick.Click += delegate { host.ShowWindow("quick"); };
                quit.Click += delegate { host.Quit(); };
                tray.MouseClick += (sender, e) =>
                {
                    if (e.Button == Forms.MouseButtons.Left) host.ShowWindow();
                };
                timer.Tick += delegate { host.Tick(); };

                dispatch.CreateControl();
                var waiter = ThreadPool.RegisterWaitForSingleObject(open, delegate
                {
                    try { dispatch.BeginInvoke(new Action(() => host.ShowWindow())); }
                    catch (InvalidOperationException) { }
                }, null, Timeout.Infinite, false);
                try
                {
                    if (atSignIn && preferences.StartupInTray) host.ResumeTray();
                    else host.ShowWindow();
                    Forms.Application.Run();
                }
                finally
                {
                    waiter.Unregister(null);
                    host.StopTray();
                    host.window?.Dispose();
                }
            }
        }

        private void ResumeTray()
        {
            catalog = new GameCatalog(AppBuild.DataDirectory);
            var preferences = LibraryPreferences.Load(catalog.PreferencesFile);
            hotkeys = new LaunchHotkeys(() => ShowWindow("quick"), game => OpenGame(game, null),
                preferences.QuickLaunchModifiers ?? LaunchHotkeys.DefaultModifiers,
                preferences.QuickLaunchKey ?? LaunchHotkeys.DefaultKey, useNativeWindow: true);
            hotkeys.LaunchProfile = OpenGame;
            quickItem.Text = hotkeys.QuickLaunchRegistered ? "Quick launch (" + hotkeys.QuickLaunchLabel + ")" : "Quick launch";
            tray.Visible = true;
            string unavailable = hotkeys.Refresh(catalog.Games);
            if (unavailable != null) Notify(unavailable);
            Record(false);
            timer.Interval = 3000;
            timer.Start();
        }

        private void StopTray()
        {
            if (hotkeys == null) return;
            timer.Stop();
            Record(true);
            hotkeys.Dispose();
            hotkeys = null;
            catalog = null;
            activity = null;
            tray.Visible = false;
        }

        private void OpenGame(Game game, LaunchProfile profile)
        {
            int index = profile == null ? -1 : game.Settings.LaunchProfiles.ToList().IndexOf(profile);
            string gameId = Convert.ToBase64String(Encoding.UTF8.GetBytes(game.Id));
            ShowWindow("game " + gameId + " " + index.ToString(CultureInfo.InvariantCulture));
        }

        private void ShowWindow(string request = "")
        {
            if (stopping) return;
            if (window != null)
            {
                if (!window.HasExited) { reveal.Set(); return; }
                WindowExited();
                if (stopping) return;
            }

            if (hotkeys != null)
            {
                using (var process = Process.GetCurrentProcess())
                    activityHandoff.Write(TrayWorkingSetOffset, process.WorkingSet64);
            }
            StopTray();
            reveal.Reset();
            try
            {
                string executable = typeof(ReducedMemoryTray).Assembly.Location;
                using (var owner = Process.GetCurrentProcess())
                window = Process.Start(new ProcessStartInfo(executable, WindowArgument + " " + viewId +
                    " " + owner.Id.ToString(CultureInfo.InvariantCulture) + (request.Length == 0 ? "" : " " + request)) {
                    WorkingDirectory = Path.GetDirectoryName(executable), UseShellExecute = false
                }) ?? throw new IOException("No AppDex window process started.");
                timer.Interval = 250;
                timer.Start();
            }
            catch (Exception error) when (error is IOException || error is System.ComponentModel.Win32Exception ||
                error is InvalidOperationException)
            {
                ResumeTray();
                Notify("Couldn't open the library. " + error.Message);
            }
        }

        private void Tick()
        {
            if (window == null) Record(false);
            else if (window.HasExited) WindowExited();
        }

        private void WindowExited()
        {
            timer.Stop();
            int exitCode = window.ExitCode;
            window.Dispose();
            window = null;
            if (exitCode == 0) { Quit(); return; }
            ResumeTray();
            if (exitCode != SleepExitCode) Notify("The library closed unexpectedly. Open AppDex from the tray to try again.");
        }

        private void Quit()
        {
            stopping = true;
            Forms.Application.ExitThread();
        }

        private void Record(bool flush)
        {
            try
            {
                if (activity == null)
                {
                    activity = ActivityBook.Load(activityFile);
                    activity.ResumeSampling(TakeActivityHandoff(activityHandoff));
                }
                long now = activity.ObserveLibrary(catalog.Games, session, out bool changed);
                if (flush || changed || now - lastSave >= TimeSpan.FromSeconds(30).Ticks)
                {
                    activity.Save(activityFile);
                    lastSave = now;
                }
                if (flush) activityHandoff.Write(0, now);
                trackingError = null;
            }
            catch (Exception error)
            {
                if (trackingError != error.Message) Notify("Couldn't update app activity. " + error.Message);
                trackingError = error.Message;
            }
        }

        internal static long TakeActivityHandoff(MemoryMappedViewAccessor handoff)
        {
            long sample = handoff.ReadInt64(0);
            handoff.Write(0, 0L);
            return sample;
        }

        internal static long ReadLastTrayWorkingSet(MemoryMappedViewAccessor handoff) => handoff.ReadInt64(TrayWorkingSetOffset);

        private void Notify(string message) => tray.ShowBalloonTip(4500, AppBuild.Name, message, Forms.ToolTipIcon.Warning);
    }
}
