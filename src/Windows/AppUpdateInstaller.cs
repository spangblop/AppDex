using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace GameShelf
{
    internal static class AppUpdateInstaller
    {
        private const string ApplyArgument = "--apply-appdex-update";
        private const string CleanupArgument = "--cleanup-appdex-update";
        private const string StagePrefix = "AppDex-update-";
        private const string Helper = "Updater.exe", Candidate = "Next.exe", Backup = "Previous.exe",
            Archive = "Update.zip", Proceed = "Install", Done = "Complete", Failure = "Failure.txt";
        private static string CurrentExecutable => typeof(AppUpdateInstaller).Assembly.Location;
        private static int preparing;

        internal static async Task Prepare(AppUpdate update, CancellationToken token)
        {
            RequireStandardUser();
            string target = CurrentExecutable;
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
                    WaitForHelper(target, directory, id, executableHash);
                    token.ThrowIfCancellationRequested();
                    File.WriteAllText(Path.Combine(directory, Proceed), executableHash);
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

        private static void WaitForHelper(string target, string directory, string id, string hash)
        {
            using (var current = Process.GetCurrentProcess())
            using (var ready = new EventWaitHandle(false, EventResetMode.ManualReset, ReadyName(id)))
            {
                string arguments = string.Format(CultureInfo.InvariantCulture, "{0} {1} {2} \"{3}\" {4}",
                    ApplyArgument, current.Id, current.StartTime.ToUniversalTime().Ticks, Path.GetFileName(target), hash);
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
                    throw new IOException(File.Exists(failure) ? File.ReadAllText(failure) :
                        "The updater couldn't get ready. AppDex is still open.");
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
                if (args.Length != 5 || !int.TryParse(args[1], out int parentId) || !long.TryParse(args[2], out long parentStart) ||
                    Path.GetFileName(args[3]) != args[3] || !args[3].EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                    !AppUpdates.ValidHash(args[4]) || !TryGetStage(out string directory, out string id)) return true;
                RequireStandardUser();
                verifiedStage = directory;
                string target = Path.Combine(Path.GetDirectoryName(directory), args[3]);
                string candidate = Path.Combine(directory, Candidate);
                using (var parent = Process.GetProcessById(parentId))
                {
                    if (!string.Equals(GameProcesses.ExecutablePath(parent.Id), target, StringComparison.OrdinalIgnoreCase) ||
                        parent.StartTime.ToUniversalTime().Ticks != parentStart ||
                        AppUpdates.ExecutableVersion(candidate) <= AppUpdates.CurrentVersion ||
                        !string.Equals(AppUpdates.FileHash(candidate), args[4], StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The updater couldn't verify the running AppDex or the downloaded executable.");
                    using (var ready = EventWaitHandle.OpenExisting(ReadyName(id))) ready.Set();
                    if (!parent.WaitForExit(30000)) return true;
                }
                parentExited = true;
                string permission = Path.Combine(directory, Proceed);
                if (!File.Exists(permission) || File.ReadAllText(permission) != args[4]) return true;
                if (!string.Equals(AppUpdates.FileHash(candidate), args[4], StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The downloaded executable changed before installation.");
                ReplaceAndRestart(target, candidate, directory, id);
            }
            catch (Exception error)
            {
                if (parentExited) MessageBox.Show("Couldn't finish updating AppDex. " + error.Message, "AppDex update");
                else if (verifiedStage != null)
                {
                    try { File.WriteAllText(Path.Combine(verifiedStage, Failure), error.Message); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            return true;
        }

        private static void RequireStandardUser()
        {
            using (var identity = WindowsIdentity.GetCurrent())
            {
                if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                    throw new InvalidOperationException("Reopen AppDex without administrator privileges to install the update.");
            }
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
