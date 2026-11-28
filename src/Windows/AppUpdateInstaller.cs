using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace GameShelf
{
    internal static class AppUpdateInstaller
    {
        private const string ApplyArgument = "--apply-appdex-update";
        private const string DowngradeArgument = "--downgrade=";
        private const string CleanupArgument = "--cleanup-appdex-update";
        private const string StagePrefix = "AppDex-update-";
        private const string AccessDeniedMarker = "ACCESS_DENIED";
        private const string RepositoryUrl = "https://github.com/spangblop/AppDex";
        internal const string AccessDeniedHelp = "Windows denied access while updating AppDex. Close AppDex and relaunch it as administrator to retry the automatic update. To update manually, download the Windows ZIP from the AppDex repository.";
        private const string Helper = "Updater.exe", Candidate = "Next.exe", Backup = "Previous.exe",
            Archive = "Update.zip", Proceed = "Install", Done = "Complete", Failure = "Failure.txt";
        private static string CurrentExecutable => typeof(AppUpdateInstaller).Assembly.Location;
        private static int preparing;

        internal static async Task Prepare(AppUpdate update, CancellationToken token, Process trayCoordinator = null, bool allowDowngrade = false)
        {
            bool downgrade = update.Version < AppUpdates.BuildVersion;
            if (update.Version == AppUpdates.BuildVersion || (downgrade && !allowDowngrade))
                throw new InvalidDataException("This build was not selected for installation.");
            string approvedVersion = downgrade ? update.Version.ToString(3) : null;
            string target = CurrentExecutable;
            EnsureWritableExecutable(target);
            string id = Guid.NewGuid().ToString("N");
            string directory = Path.Combine(Path.GetDirectoryName(target), StagePrefix + id);
            if (Interlocked.CompareExchange(ref preparing, 1, 0) != 0)
                throw new InvalidOperationException("An AppDex update is already in progress.");
            bool ready = false;
            try
            {
                Directory.CreateDirectory(directory);
                string archive = Path.Combine(directory, Archive);
                await AppUpdates.Download(update, archive, token).ConfigureAwait(false);
                await Task.Run(() =>
                {
                    string executableHash = AppUpdates.Unpack(update, archive, Path.Combine(directory, Candidate));
                    token.ThrowIfCancellationRequested();
                    File.Delete(archive);
                    File.Copy(target, Path.Combine(directory, Helper));
                    WaitForHelper(target, directory, id, executableHash, trayCoordinator, approvedVersion);
                    token.ThrowIfCancellationRequested();
                    File.WriteAllText(Path.Combine(directory, Proceed), Approval(executableHash, approvedVersion));
                }, token).ConfigureAwait(false);
                ready = true;
            }
            finally
            {
                if (!ready)
                {
                    RemoveStage(directory);
                    Interlocked.Exchange(ref preparing, 0);
                }
            }
        }

        private static void EnsureWritableExecutable(string target)
        {
            if ((File.GetAttributes(target) & FileAttributes.ReadOnly) != 0)
                throw new IOException("AppDex is running from a read-only file. Extract AppDex.exe from the ZIP to a folder you can write to, then open the extracted copy.");
        }

        private static string Approval(string hash, string version) => version == null ? hash : hash + "|" + version;

        private static void WaitForHelper(string target, string directory, string id, string hash, Process trayCoordinator, string approvedVersion)
        {
            using (var current = Process.GetCurrentProcess())
            using (var ready = new EventWaitHandle(false, EventResetMode.ManualReset, ReadyName(id)))
            {
                string arguments = string.Format(CultureInfo.InvariantCulture, "{0} {1} {2} \"{3}\" {4}",
                    ApplyArgument, current.Id, current.StartTime.ToUniversalTime().Ticks, Path.GetFileName(target), hash);
                if (trayCoordinator != null)
                    arguments += string.Format(CultureInfo.InvariantCulture, " {0} {1}", trayCoordinator.Id, trayCoordinator.StartTime.ToUniversalTime().Ticks);
                if (approvedVersion != null) arguments += " " + DowngradeArgument + approvedVersion;
                using (var helper = Process.Start(new ProcessStartInfo(Path.Combine(directory, Helper), arguments) { UseShellExecute = false }))
                {
                    if (helper == null) throw new IOException("Couldn't start the AppDex updater.");
                    var wait = Stopwatch.StartNew();
                    while (wait.Elapsed < TimeSpan.FromSeconds(10))
                    {
                        if (ready.WaitOne(100)) return;
                        if (helper.HasExited) break;
                    }
                    string failure = Path.Combine(directory, Failure);
                    string detail = File.Exists(failure) ? File.ReadAllText(failure) :
                        "The updater couldn't get ready. AppDex is still open.";
                    if (detail == AccessDeniedMarker) throw new UnauthorizedAccessException();
                    throw new IOException(detail);
                }
            }
        }

        internal static bool HandleArguments(string[] args)
        {
            if (args.Length == 0) return false;
            if (args[0] == CleanupArgument && args.Length == 2 && Guid.TryParseExact(args[1], "N", out _))
            {
                string path = Path.Combine(Path.GetDirectoryName(CurrentExecutable), StagePrefix + args[1]);
                Task.Run(() => CleanCompletedStage(path));
                return false;
            }
            if (args[0] != ApplyArgument) return false;

            bool parentExited = false;
            string verifiedStage = null;
            try
            {
                string approvedVersion = args.Length > 5 && args[args.Length - 1].StartsWith(DowngradeArgument, StringComparison.Ordinal)
                    ? args[args.Length - 1].Substring(DowngradeArgument.Length) : null;
                int argumentCount = args.Length - (approvedVersion == null ? 0 : 1);
                if ((argumentCount != 5 && argumentCount != 7) || !int.TryParse(args[1], out int parentId) || !long.TryParse(args[2], out long parentStart) ||
                    Path.GetFileName(args[3]) != args[3] || !args[3].EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                    !AppUpdates.ValidHash(args[4]) || !TryGetStage(out string directory, out string id)) return true;
                verifiedStage = directory;
                string target = Path.Combine(Path.GetDirectoryName(directory), args[3]);
                string candidate = Path.Combine(directory, Candidate);
                using (var coordinator = OpenTrayCoordinator(args, argumentCount, parentId, target))
                using (var parent = Process.GetProcessById(parentId))
                {
                    if (!string.Equals(GameProcesses.ExecutablePath(parent.Id), target, StringComparison.OrdinalIgnoreCase) ||
                        parent.StartTime.ToUniversalTime().Ticks != parentStart ||
                        !CanInstallVersion(AppUpdates.ExecutableVersion(candidate), approvedVersion) ||
                        !string.Equals(AppUpdates.FileHash(candidate), args[4], StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The updater couldn't verify the running AppDex or the downloaded executable.");
                    using (var ready = EventWaitHandle.OpenExisting(ReadyName(id))) ready.Set();
                    if (!parent.WaitForExit(30000)) return true;
                    parentExited = true;
                    if (coordinator != null && !coordinator.WaitForExit(10000))
                        throw new IOException("AppDex's tray process did not exit. Fully quit AppDex and try the update again.");
                }
                string permission = Path.Combine(directory, Proceed);
                if (!File.Exists(permission) || File.ReadAllText(permission) != Approval(args[4], approvedVersion)) return true;
                if (!string.Equals(AppUpdates.FileHash(candidate), args[4], StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The downloaded executable changed before installation.");
                if (approvedVersion != null) SaveVersionBackup(AppBuild.DataDirectory, target, id);
                ReplaceAndRestart(target, candidate, directory, id);
            }
            catch (Exception error)
            {
                if (parentExited)
                {
                    if (IsAccessDenied(error)) CreateAccessDeniedDialog().ShowDialog();
                    else MessageBox.Show("Couldn't finish updating AppDex. " + error.Message, "AppDex update");
                }
                else if (verifiedStage != null)
                {
                    try { File.WriteAllText(Path.Combine(verifiedStage, Failure), IsAccessDenied(error) ? AccessDeniedMarker : error.Message); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            return true;
        }

        private static bool CanInstallVersion(Version candidate, string approvedVersion)
        {
            var build = AppUpdates.ToBuildVersion(candidate);
            return approvedVersion == null ? build > AppUpdates.BuildVersion :
                build < AppUpdates.BuildVersion && approvedVersion == build.ToString(3);
        }

        private static void SaveVersionBackup(string dataDirectory, string target, string id)
        {
            string folder = Path.Combine(dataDirectory, "Version backups",
                DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + id);
            Directory.CreateDirectory(folder);
            File.Copy(target, Path.Combine(folder, "AppDex.exe"));
            foreach (string name in new[] { "library.json", "websites.json", "preferences.json", "activity.json", "steam-location.json" })
            {
                string path = Path.Combine(dataDirectory, name);
                if (File.Exists(path)) File.Copy(path, Path.Combine(folder, name));
            }
        }

        private static Process OpenTrayCoordinator(string[] args, int argumentCount, int parentId, string target)
        {
            if (argumentCount == 5) return null;
            if (!int.TryParse(args[5], out int id) || id == parentId || !long.TryParse(args[6], out long start))
                throw new InvalidDataException("The updater couldn't verify the AppDex tray process.");
            var process = Process.GetProcessById(id);
            try
            {
                if (!string.Equals(GameProcesses.ExecutablePath(id), target, StringComparison.OrdinalIgnoreCase) ||
                    process.StartTime.ToUniversalTime().Ticks != start)
                    throw new InvalidDataException("The updater couldn't verify the AppDex tray process.");
                return process;
            }
            catch
            {
                process.Dispose();
                throw;
            }
        }

        internal static bool IsAccessDenied(Exception error) =>
            error is UnauthorizedAccessException || error.HResult == unchecked((int)0x80070005) ||
            (error is Win32Exception win32 && win32.NativeErrorCode == 5);

        internal static Hyperlink CreateRepositoryLink()
        {
            var link = new Hyperlink(new Run("github.com/spangblop/AppDex")) { NavigateUri = new Uri(RepositoryUrl) };
            link.RequestNavigate += (sender, e) =>
            {
                e.Handled = true;
                try { using (Process.Start(new ProcessStartInfo(RepositoryUrl) { UseShellExecute = true })) { } }
                catch (Win32Exception) { MessageBox.Show("Couldn't open your browser. Visit " + RepositoryUrl, "AppDex update"); }
            };
            return link;
        }

        private static Window CreateAccessDeniedDialog()
        {
            var instructions = new TextBlock { TextWrapping = TextWrapping.Wrap };
            instructions.Inlines.Add(new Run(AccessDeniedHelp));
            instructions.Inlines.Add(new LineBreak());
            instructions.Inlines.Add(CreateRepositoryLink());
            var close = new Button { Content = "OK", IsCancel = true, IsDefault = true,
                MinWidth = 75, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var dialog = new Window { Title = "AppDex update", Width = 430, SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Content = new StackPanel { Margin = new Thickness(16), Children = { instructions, close } } };
            close.Click += delegate { dialog.Close(); };
            return dialog;
        }

        private static bool TryGetStage(out string directory, out string id)
        {
            directory = Path.GetDirectoryName(CurrentExecutable);
            id = "";
            string name = Path.GetFileName(directory);
            if (Path.GetFileName(CurrentExecutable) != Helper || !name.StartsWith(StagePrefix, StringComparison.Ordinal) ||
                (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return false;
            id = name.Substring(StagePrefix.Length);
            return Guid.TryParseExact(id, "N", out _);
        }

        private static void ReplaceAndRestart(string target, string candidate, string directory, string id)
        {
            string backup = Path.Combine(directory, Backup);
            Process updated = null;
            bool replaced = false;
            try
            {
                File.Replace(candidate, target, backup);
                replaced = true;
                updated = Start(target, CleanupArgument + " " + id);
                var wait = Stopwatch.StartNew();
                while (wait.Elapsed < TimeSpan.FromSeconds(12))
                {
                    updated.Refresh();
                    if (updated.HasExited) throw new IOException("The new build exited during startup.");
                    if (updated.MainWindowHandle != IntPtr.Zero)
                    {
                        File.WriteAllText(Path.Combine(directory, Done), "");
                        return;
                    }
                    Thread.Sleep(150);
                }
                throw new IOException("The new build is running, but its window did not open. The previous executable is at " + backup);
            }
            catch
            {
                if (updated == null || updated.HasExited)
                {
                    if (replaced) File.Replace(backup, target, null);
                    using (Start(target, "")) { }
                }
                throw;
            }
            finally { updated?.Dispose(); }
        }

        private static Process Start(string target, string args)
        {
            return Process.Start(new ProcessStartInfo(target, args) {
                WorkingDirectory = Path.GetDirectoryName(target), UseShellExecute = false
            }) ?? throw new IOException("Couldn't reopen AppDex.");
        }

        private static string ReadyName(string id) => @"Local\AppDex.Update." + id;

        private static void CleanCompletedStage(string path)
        {
            var wait = Stopwatch.StartNew();
            while (wait.Elapsed < TimeSpan.FromSeconds(20))
            {
                if (File.Exists(Path.Combine(path, Done)) && RemoveStage(path)) return;
                Thread.Sleep(200);
            }
        }

        private static bool RemoveStage(string path)
        {
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return false;
                foreach (string name in new[] { Helper, Candidate, Archive, Backup, Proceed, Done, Failure }) File.Delete(Path.Combine(path, name));
                Directory.Delete(path);
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }
}
