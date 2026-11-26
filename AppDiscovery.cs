using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security;

namespace GameShelf
{
    internal sealed class ImportCandidate
    {
        internal string Name, Executable, Uri, Arguments;
        internal uint SteamAppId;
        internal string Target { get { return SteamAppId == 0 ? Uri ?? Executable : "steam://rungameid/" + SteamAppId; } }
    }

    internal static class AppDiscovery
    {
        internal static string EpicFolder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic", "EpicGamesLauncher", "Data", "Manifests"); }
        }

        [DataContract]
        private sealed class EpicManifest
        {
            [DataMember] public string DisplayName { get; set; }
            [DataMember] public string AppName { get; set; }
            [DataMember] public string CatalogItemId { get; set; }
            [DataMember] public string CatalogNamespace { get; set; }
            [DataMember] public string InstallLocation { get; set; }
            [DataMember] public string LaunchExecutable { get; set; }
            [DataMember] public string LaunchCommand { get; set; }
            [DataMember(Name = "bIsIncompleteInstall")] public bool Incomplete { get; set; }
        }

        internal static List<ImportCandidate> Epic(string folder)
        {
            return ScanEpic(folder, out _);
        }

        private static bool IsEpicFileError(Exception error)
        {
            return error is IOException || error is UnauthorizedAccessException || error is ArgumentException ||
                error is SerializationException || error is SecurityException || error is NotSupportedException;
        }

        internal static List<ImportCandidate> ScanEpic(string folder, out bool complete)
        {
            var results = new List<ImportCandidate>();
            string[] files;
            try { files = Directory.GetFiles(folder, "*.item"); }
            catch (Exception error) when (IsEpicFileError(error)) { complete = false; return results; }
            complete = true;
            foreach (string file in files)
            {
                try
                {
                    EpicManifest app;
                    using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        app = (EpicManifest)new DataContractJsonSerializer(typeof(EpicManifest)).ReadObject(stream);
                    if (app == null) throw new InvalidDataException("Invalid Epic Games manifest.");
                    if (app.Incomplete || string.IsNullOrWhiteSpace(app.DisplayName) ||
                        string.IsNullOrWhiteSpace(app.AppName) || string.IsNullOrWhiteSpace(app.LaunchExecutable) ||
                        string.IsNullOrWhiteSpace(app.InstallLocation) || !Path.IsPathRooted(app.InstallLocation)) continue;
                    string directory = Path.GetFullPath(app.InstallLocation).TrimEnd('\\') + "\\";
                    string executable = Path.GetFullPath(Path.Combine(directory, app.LaunchExecutable));
                    if (!executable.StartsWith(directory, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(Path.GetExtension(executable), ".exe", StringComparison.OrdinalIgnoreCase)) continue;
                    try { if ((File.GetAttributes(executable) & FileAttributes.Directory) != 0) continue; }
                    catch (FileNotFoundException) { continue; }
                    catch (DirectoryNotFoundException) { continue; }
                    string id = string.IsNullOrWhiteSpace(app.CatalogNamespace) || string.IsNullOrWhiteSpace(app.CatalogItemId)
                        ? app.AppName : app.CatalogNamespace + ":" + app.CatalogItemId + ":" + app.AppName;
                    results.Add(new ImportCandidate { Name = app.DisplayName, Executable = executable, Arguments = app.LaunchCommand,
                        Uri = "com.epicgames.launcher://apps/" + Uri.EscapeDataString(id) + "?action=launch&silent=true" });
                }
                catch (Exception error) when (IsEpicFileError(error)) { complete = false; }
            }
            return results;
        }

        internal static List<ImportCandidate> Running()
        {
            var results = new List<ImportCandidate>();
            string system = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\') + "\\";
            string steam = SteamLibrary.Locate();
            var steamApps = steam == null ? new List<SteamTitle>() : SteamLibrary.Installed(steam).Titles;
            var epicApps = Epic(EpicFolder).ToLookup(app => app.Executable, StringComparer.OrdinalIgnoreCase);
            int session, self;
            using (var current = Process.GetCurrentProcess()) { session = current.SessionId; self = current.Id; }
            foreach (var process in Process.GetProcesses())
            using (process)
            {
                try
                {
                    if (process.Id == self || process.SessionId != session || process.MainWindowHandle == IntPtr.Zero) continue;
                    string executable = GameProcesses.ExecutablePath(process.Id);
                    if (string.IsNullOrWhiteSpace(executable)) continue;
                    if (executable.StartsWith(system, StringComparison.OrdinalIgnoreCase) ||
                        executable.IndexOf("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        process.ProcessName.Equals("AppDex", StringComparison.OrdinalIgnoreCase)) continue;
                    string name = FileVersionInfo.GetVersionInfo(executable).ProductName;
                    if (string.Equals(name, "AppDex", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileNameWithoutExtension(executable);
                    var epicApp = epicApps[executable].FirstOrDefault();
                    if (epicApp != null) { results.Add(epicApp); continue; }
                    var steamApp = steamApps.FirstOrDefault(app => executable.StartsWith(app.Directory.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));
                    results.Add(new ImportCandidate { Name = steamApp == null ? name : steamApp.Name, Executable = executable,
                        SteamAppId = steamApp == null ? 0 : steamApp.AppId });
                }
                catch (Exception e) when (e is InvalidOperationException || e is Win32Exception || e is NotSupportedException || e is IOException) { }
            }
            return results;
        }
    }
}
