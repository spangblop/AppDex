using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security;
using Microsoft.Win32;

namespace GameShelf
{
    [DataContract]
    internal sealed class SteamLocation
    {
        [DataMember(EmitDefaultValue = false)] public string ClientFolder;
        [DataMember(EmitDefaultValue = false)] public string LibraryFolder;

        internal static string PreferencesFile
        {
            get { return Path.Combine(AppBuild.DataDirectory, "steam-location.json"); }
        }

        internal static SteamLocation Read(string file)
        {
            try
            {
                using (var input = File.OpenRead(file))
                    return (SteamLocation)new DataContractJsonSerializer(typeof(SteamLocation)).ReadObject(input) ?? new SteamLocation();
            }
            catch (Exception e) when (e is IOException || e is SerializationException || e is UnauthorizedAccessException)
            { return new SteamLocation(); }
        }

        internal void Save(string file)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            string temp = file + ".tmp";
            try
            {
                using (var output = File.Create(temp))
                { new DataContractJsonSerializer(typeof(SteamLocation)).WriteObject(output, this); output.Flush(true); }
                if (File.Exists(file)) File.Replace(temp, file, null);
                else File.Move(temp, file);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        internal SteamLocation WithFolder(string path)
        {
            var dir = new DirectoryInfo(path);
            if (dir.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase) && dir.Parent != null) dir = dir.Parent;
            if (!Directory.Exists(Path.Combine(dir.FullName, "steamapps")))
                throw new ArgumentException("Choose a Steam folder that contains steamapps, or choose the steamapps folder itself.");
            return new SteamLocation { LibraryFolder = dir.FullName,
                ClientFolder = File.Exists(Path.Combine(dir.FullName, "steam.exe")) ? dir.FullName : ClientFolder };
        }

        private static string Registered(RegistryHive hive, RegistryView view)
        {
            try
            {
                using (var registry = RegistryKey.OpenBaseKey(hive, view))
                using (var key = registry.OpenSubKey(@"Software\Valve\Steam"))
                {
                    if (key == null) return null;
                    return (key.GetValue("SteamPath") ?? key.GetValue("InstallPath")) as string;
                }
            }
            catch (Exception e) when (e is SecurityException || e is UnauthorizedAccessException || e is IOException) { return null; }
        }

        private static IEnumerable<string> Candidates()
        {
            yield return Registered(RegistryHive.CurrentUser, RegistryView.Default);
            yield return Registered(RegistryHive.LocalMachine, RegistryView.Registry32);
            if (Environment.Is64BitOperatingSystem) yield return Registered(RegistryHive.LocalMachine, RegistryView.Registry64);
            foreach (var folder in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles })
            {
                string programs = Environment.GetFolderPath(folder);
                if (!string.IsNullOrEmpty(programs)) yield return Path.Combine(programs, "Steam");
            }
        }

        internal string FindClient() { return ResolveClient(Candidates()); }

        internal string ResolveClient(IEnumerable<string> candidates)
        {
            return new[] { ClientFolder }.Concat(candidates).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)
                && Path.IsPathRooted(p) && File.Exists(Path.Combine(p, "steam.exe")));
        }

        internal string ScanFolder(string client)
        {
            return !string.IsNullOrWhiteSpace(LibraryFolder) && Directory.Exists(Path.Combine(LibraryFolder, "steamapps"))
                ? LibraryFolder : client;
        }
    }
}
