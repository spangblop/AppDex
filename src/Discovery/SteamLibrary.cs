using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace GameShelf
{
    [DataContract]
    internal sealed class SteamTitle : INotifyPropertyChanged
    {
        [DataMember(Name = "appid")] public uint AppId { get; set; }
        [DataMember(Name = "name")] public string Name { get; set; }
        public string Directory { get; set; }
        internal ImageSource CachedArtwork { get; set; }
        internal ImportCandidate Candidate { get; set; }
        private bool selected;
        public event PropertyChangedEventHandler PropertyChanged;
        public bool Selected
        {
            get { return selected; }
            set
            {
                if (selected == value) return;
                selected = value;
                var notify = PropertyChanged;
                if (notify != null) notify(this, new PropertyChangedEventArgs("Selected"));
            }
        }
    }

    internal sealed class SteamScan
    {
        public List<SteamTitle> Titles = new List<SteamTitle>();
        public int Unreadable;

        internal List<SteamTitle> InstalledForAccount(IEnumerable<SteamTitle> owned)
        {
            var ids = new HashSet<uint>(owned.Select(title => title.AppId));
            return Titles.Where(title => ids.Contains(title.AppId)).ToList();
        }
    }

    internal static class SteamLibrary
    {
        public static string Locate()
        {
            return SteamLocation.Read(SteamLocation.PreferencesFile).FindClient();
        }

        private static readonly Regex Tokens = new Regex(@"""(?:\\.|[^""\\])*""|//[^\r\n]*|[{}]|[^\s{}""]+", RegexOptions.Compiled);

        // Both libraryfolders.vdf and app manifests use Valve's text KeyValues format.
        private static Dictionary<string, object> Parse(string text)
        {
            var root = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var stack = new Stack<Dictionary<string, object>>();
            stack.Push(root);
            string key = null;
            foreach (Match match in Tokens.Matches(text))
            {
                string token = match.Value;
                if (token.StartsWith("//", StringComparison.Ordinal)) continue;
                if (token == "{")
                {
                    if (key == null) throw new InvalidDataException("Missing Steam section name.");
                    var child = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    stack.Peek()[key] = child;
                    stack.Push(child);
                    key = null;
                }
                else if (token == "}")
                {
                    if (key != null || stack.Count == 1) throw new InvalidDataException("Invalid Steam section.");
                    stack.Pop();
                }
                else
                {
                    if (token[0] == '"') token = Regex.Replace(token.Substring(1, token.Length - 2), @"\\([""\\])", "$1");
                    if (key == null) key = token;
                    else { stack.Peek()[key] = token; key = null; }
                }
            }
            if (key != null || stack.Count != 1) throw new InvalidDataException("Incomplete Steam file.");
            return root;
        }

        private static T Value<T>(Dictionary<string, object> node, string key) where T : class
        {
            object value;
            return node != null && node.TryGetValue(key, out value) ? value as T : null;
        }

        private static Dictionary<string, object> Section(string file, string name)
        {
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                return Value<Dictionary<string, object>>(Parse(reader.ReadToEnd()), name)
                    ?? throw new InvalidDataException("Missing " + name + " section.");
        }

        private static bool IsFileError(Exception e)
        {
            return e is IOException || e is InvalidDataException || e is UnauthorizedAccessException ||
                e is SecurityException || e is ArgumentException || e is NotSupportedException;
        }

        private static Dictionary<string, object> LibraryFolders(string root)
        {
            foreach (string directory in new[] { "steamapps", "config" })
            {
                try { return Section(Path.Combine(root, directory, "libraryfolders.vdf"), "libraryfolders"); }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
            }
            return new Dictionary<string, object>();
        }

        public static SteamScan Installed(string steamRoot)
        {
            return ScanInstalled(steamRoot, null);
        }

        internal static SteamTitle FindInstalled(uint appId)
        {
            var location = SteamLocation.Read(SteamLocation.PreferencesFile);
            string client = location.FindClient();
            if (client == null && string.IsNullOrWhiteSpace(location.LibraryFolder)) return null;
            return ScanInstalled(client, location.LibraryFolder).Titles.FirstOrDefault(title => title.AppId == appId);
        }

        internal static SteamScan ScanInstalled(string steamRoot, string additionalLibrary)
        {
            steamRoot = steamRoot ?? additionalLibrary;
            if (string.IsNullOrEmpty(steamRoot)) throw new DirectoryNotFoundException("Choose your Steam installation or library folder to find installed apps.");
            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { steamRoot };
            if (!string.IsNullOrEmpty(additionalLibrary)) folders.Add(additionalLibrary);
            var result = new SteamScan();
            try
            {
                foreach (var row in LibraryFolders(steamRoot))
                {
                    uint index;
                    if (!uint.TryParse(row.Key, out index)) continue;
                    string path = row.Value as string ?? Value<string>(row.Value as Dictionary<string, object>, "path");
                    if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
                        throw new InvalidDataException("Invalid Steam library folder.");
                    folders.Add(Path.GetFullPath(path));
                }
            }
            catch (Exception e) when (IsFileError(e)) { result.Unreadable++; }

            var ids = new HashSet<uint>();
            foreach (string folder in folders)
            {
                string apps = Path.Combine(folder, "steamapps");
                string[] manifests;
                try { manifests = Directory.GetFiles(apps, "appmanifest_*.acf"); }
                catch (Exception e) when (IsFileError(e)) { result.Unreadable++; continue; }
                foreach (string manifest in manifests)
                {
                    try
                    {
                        var info = Section(manifest, "AppState");
                        uint id;
                        string name = Value<string>(info, "name"), dir = Value<string>(info, "installdir");
                        if (!uint.TryParse(Value<string>(info, "appid"), out id) || string.IsNullOrWhiteSpace(name) ||
                            id == 0 || !string.Equals(Path.GetFileName(manifest), "appmanifest_" + id + ".acf", StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("Invalid Steam app manifest.");
                        if (id == 228980) continue;
                        uint flags;
                        if (!uint.TryParse(Value<string>(info, "StateFlags"), out flags))
                            throw new InvalidDataException("Invalid Steam install state.");
                        if ((flags & 1) != 0 || (flags & 4) == 0) continue;
                        if (string.IsNullOrWhiteSpace(dir) || dir != Path.GetFileName(dir) || dir == "." || dir == "..")
                            throw new InvalidDataException("Invalid Steam install directory.");
                        string install = Path.Combine(apps, "common", dir);
                        try { if ((File.GetAttributes(install) & FileAttributes.Directory) == 0) continue; }
                        catch (FileNotFoundException) { continue; }
                        catch (DirectoryNotFoundException) { continue; }
                        if (ids.Add(id)) result.Titles.Add(new SteamTitle { AppId = id, Name = name, Directory = install });
                    }
                    catch (Exception e) when (IsFileError(e)) { result.Unreadable++; }
                }
            }
            return result;
        }

        public static ImageSource Artwork(string root, uint id)
        {
            if (root == null) return null;
            try
            {
                string cache = Path.Combine(root, "appcache", "librarycache");
                string number = id.ToString(CultureInfo.InvariantCulture);
                string dir = Path.Combine(cache, number);
                var files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.jpg", SearchOption.AllDirectories) : new string[0];
                foreach (string type in new[] { "library_header", "header", "library_hero", "library_capsule", "library_600x900" })
                {
                    string filename = type + ".jpg";
                    string image = files.FirstOrDefault(p => string.Equals(Path.GetFileName(p), filename, StringComparison.OrdinalIgnoreCase))
                        ?? Path.Combine(cache, number + "_" + filename);
                    if (File.Exists(image)) return GameCatalog.ReadSizedPicture(image, 480);
                }
            }
            catch (Exception e) when (IsFileError(e) || e is FormatException) { }
            return null;
        }

        internal static async Task<BitmapSource> RefreshArtwork(uint id)
        {
            string app = id.ToString(CultureInfo.InvariantCulture);
            string url = "https://cdn.cloudflare.steamstatic.com/steam/apps/" + app + "/header.jpg";
            try
            {
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8), MaxResponseContentBufferSize = 4 * 1024 * 1024 })
                {
                    byte[] bytes = await client.GetByteArrayAsync(url).ConfigureAwait(false);
                    using (var stream = new MemoryStream(bytes)) return GameCatalog.ReadSizedPicture(stream, 1280);
                }
            }
            catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException || e is IOException ||
                e is NotSupportedException || e is FormatException)
            {
                var cached = Artwork(Locate(), id) as BitmapSource;
                if (cached != null) return cached;
                throw new IOException("Steam artwork is unavailable for this app.", e);
            }
        }

        public static string ActiveUser()
        {
            try
            {
                object value = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam\ActiveProcess", "ActiveUser", null);
                if (value is int && (int)value != 0) return (76561197960265728UL + unchecked((uint)(int)value)).ToString(CultureInfo.InvariantCulture);
            }
            catch (Exception e) when (IsFileError(e)) { }
            return "";
        }

        internal static List<RunningGame> Running(IEnumerable<Game> library, int session)
        {
            var tracked = library.Where(g => g.Settings.SteamAppId != 0)
                .ToDictionary(g => g.Settings.SteamAppId.ToString(CultureInfo.InvariantCulture), g => g.Id);
            var result = new List<RunningGame>();
            if (tracked.Count == 0) return result;
            string client = null;
            foreach (var process in Process.GetProcessesByName("steam"))
            using (process)
            {
                try { if (process.SessionId == session) client = "steam/" + process.Id + "/" + process.StartTime.ToUniversalTime().Ticks; }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
            if (client == null) return result;
            try
            {
                using (var steam = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                using (var apps = steam == null ? null : steam.OpenSubKey("Apps"))
                {
                    if (steam == null) return result;
                    var active = new HashSet<string>();
                    object foreground = steam.GetValue("RunningAppID");
                    if (foreground is int) active.Add(unchecked((uint)(int)foreground).ToString(CultureInfo.InvariantCulture));
                    if (apps != null)
                        foreach (string key in apps.GetSubKeyNames().Where(tracked.ContainsKey))
                            using (var app = apps.OpenSubKey(key))
                                if (app != null && object.Equals(app.GetValue("Running"), 1)) active.Add(key);
                    foreach (string id in active.Where(tracked.ContainsKey))
                        result.Add(new RunningGame { GameId = tracked[id], Instance = client + "/" + id });
                }
            }
            catch (Exception e) when (IsFileError(e)) { }
            return result;
        }

        [DataContract] private sealed class Envelope { [DataMember(Name = "response")] public Owned Response { get; set; } }
        [DataContract] private sealed class Owned
        {
            [DataMember(Name = "game_count")] public int? Count { get; set; }
            [DataMember(Name = "games")] public SteamTitle[] Games { get; set; }
        }

        internal static SteamTitle[] ReadOwned(Stream stream)
        {
            var envelope = (Envelope)new DataContractJsonSerializer(typeof(Envelope)).ReadObject(stream);
            var data = envelope == null ? null : envelope.Response;
            if (data == null || !data.Count.HasValue) throw new InvalidDataException("Steam did not share this library. Check the SteamID and use that account’s personal Web API key.");
            if (data.Count > 0 && data.Games == null) throw new InvalidDataException("Steam returned an incomplete app list. Try again.");
            return (data.Games ?? new SteamTitle[0]).Where(g => g != null && g.AppId > 0 && !string.IsNullOrWhiteSpace(g.Name))
                .GroupBy(g => g.AppId).Select(g => g.First()).ToArray();
        }

        public static async Task<SteamTitle[]> OwnedGames(string user, string key, CancellationToken cancel)
        {
            if (!Regex.IsMatch(user, @"\A[0-9]{17}\z")) throw new ArgumentException("Enter your 17-digit SteamID.");
            if (!Regex.IsMatch(key, @"\A[0-9a-fA-F]{32}\z")) throw new ArgumentException("Enter your personal Steam Web API key.");
            string json = "{\"steamid\":\"" + user + "\",\"include_appinfo\":true,\"include_played_free_games\":true,\"skip_unvetted_apps\":false}";
            string endpoint = "https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/?format=json&input_json=" + Uri.EscapeDataString(json);
            using (var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(25) })
            using (var request = new HttpRequestMessage(HttpMethod.Get, endpoint))
            {
                request.Headers.Add("x-webapi-key", key);
                using (var response = await client.SendAsync(request, cancel).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode) throw new HttpRequestException("Steam returned HTTP " + (int)response.StatusCode + ". Check the key and try again.");
                    using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false)) return ReadOwned(stream);
                }
            }
        }
    }
}
