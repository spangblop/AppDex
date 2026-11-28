using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameShelf
{
    [DataContract]
    internal sealed class LaunchProfile
    {
        [DataMember] public string Name { get; set; }
        [DataMember(EmitDefaultValue = false)] public string Executable;
        [DataMember(EmitDefaultValue = false)] public string Arguments;
        [DataMember(EmitDefaultValue = false)] public int ShortcutModifiers;
        [DataMember(EmitDefaultValue = false)] public int ShortcutKey;
        internal LaunchProfile Copy() => (LaunchProfile)MemberwiseClone();
    }

    [DataContract]
    internal sealed class GameSettings
    {
        [DataMember] public string Id;
        [DataMember] public string Name;
        [DataMember(EmitDefaultValue = false)] public string Executable;
        [DataMember(EmitDefaultValue = false)] public string Arguments;
        [DataMember(EmitDefaultValue = false)] public string Picture;
        [DataMember(EmitDefaultValue = false)] public uint SteamAppId;
        [DataMember(EmitDefaultValue = false)] public bool Removed;
        [DataMember(EmitDefaultValue = false)] public string EpicUri;
        [DataMember(EmitDefaultValue = false)] public string EpicArguments;
        [DataMember(EmitDefaultValue = false)] public string WebUrl;
        [DataMember(EmitDefaultValue = false)] public int ShortcutModifiers;
        [DataMember(EmitDefaultValue = false)] public int ShortcutKey;
        [DataMember(EmitDefaultValue = false)] public string Aliases;
        [DataMember(EmitDefaultValue = false)] public List<LaunchProfile> Profiles;
        internal IEnumerable<LaunchProfile> LaunchProfiles => Profiles ?? Enumerable.Empty<LaunchProfile>();

        public GameSettings Copy()
        {
            var copy = (GameSettings)MemberwiseClone();
            copy.Profiles = Profiles?.Select(profile => profile.Copy()).ToList();
            return copy;
        }
    }

    internal sealed class GameCatalog
    {
        private readonly string directory;
        private string FileName { get { return Path.Combine(directory, "library.json"); } }
        private string WebsitesFile { get { return Path.Combine(directory, "websites.json"); } }
        private List<GameSettings> saved = new List<GameSettings>();
        private List<GameSettings> websites = new List<GameSettings>();
        public List<Game> Games { get; private set; }
        public string LoadError { get; private set; }
        internal string PreferencesFile { get { return Path.Combine(directory, "preferences.json"); } }

        public GameCatalog(string directory)
        {
            this.directory = directory;
            var stock = Game.Library().ToDictionary(game => game.Id);
            var preferences = LibraryPreferences.Load(PreferencesFile);
            var previous = new HashSet<string>(preferences.Order.Concat(preferences.Pins)
                .Concat(preferences.Groups.Where(group => group?.Apps != null).SelectMany(group => group.Apps)));
            if (!File.Exists(PreferencesFile))
                previous.UnionWith(ActivityBook.Load(Path.Combine(directory, "activity.json")).Games.Keys);
            var legacy = stock.Values.Where(game => previous.Contains(game.Id)).ToList();
            try
            {
                List<GameSettings> entries;
                using (var input = File.OpenRead(FileName))
                    entries = (List<GameSettings>)Serializer().ReadObject(input);
                var ids = new HashSet<string>();
                var loaded = new List<Game>(legacy);
                if (entries == null) throw new InvalidDataException("Invalid app library.");
                string steamRoot = entries.Any(s => s != null && s.SteamAppId > 0) ? SteamLibrary.Locate() : null;
                foreach (var entry in entries)
                {
                    if (entry == null || entry.WebUrl != null || string.IsNullOrWhiteSpace(entry.Name) || !ids.Add(entry.Id))
                        throw new InvalidDataException("Invalid app entry.");
                    stock.TryGetValue(entry.Id ?? "", out Game game);
                    if (entry.Removed) { if (game != null) loaded.Remove(game); continue; }
                    if (entry.SteamAppId != 0)
                    {
                        if (game != null || entry.Id != "steam-" + entry.SteamAppId)
                            throw new InvalidDataException("Invalid Steam app.");
                        loaded.Add(game = Game.SteamGame(entry, steamRoot));
                    }
                    else if (entry.EpicUri != null && game == null)
                    {
                        if (entry.Id == null || !entry.Id.StartsWith("epic-", StringComparison.Ordinal) ||
                            !entry.EpicUri.StartsWith("com.epicgames.launcher://apps/", StringComparison.Ordinal) ||
                            string.IsNullOrEmpty(entry.Executable) || !Path.IsPathRooted(entry.Executable))
                            throw new InvalidDataException("Invalid Epic Games entry.");
                        loaded.Add(game = Game.EpicGame(entry));
                    }
                    else if (game == null)
                    {
                        Guid id;
                        if (entry.Id == null || !entry.Id.StartsWith("user-", StringComparison.Ordinal) ||
                            !Guid.TryParseExact(entry.Id.Substring(5), "N", out id) ||
                            string.IsNullOrWhiteSpace(entry.Executable) || !Path.IsPathRooted(entry.Executable) ||
                            !string.Equals(Path.GetExtension(entry.Executable), ".exe", StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("Invalid custom app.");
                        loaded.Add(game = Game.CustomGame(entry));
                    }
                    else if (!loaded.Contains(game)) loaded.Add(game);
                    ValidatePictureName(entry.Picture);
                    game.Apply(entry, PictureLoader(entry.Picture));
                }
                Games = loaded;
                saved = entries;
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            catch (Exception error) { LoadError = "Couldn't load the saved library. " + error.Message; }
            if (Games == null) Games = legacy;
            LoadWebsites();
        }

        // Separate website records keep the executable library readable by older AppDex builds.
        private void LoadWebsites()
        {
            if (!File.Exists(WebsitesFile)) return;
            try
            {
                List<GameSettings> entries;
                using (var input = File.OpenRead(WebsitesFile))
                    entries = (List<GameSettings>)Serializer().ReadObject(input);
                if (entries == null) throw new InvalidDataException("Invalid website library.");
                var ids = new HashSet<string>();
                var loaded = new List<Game>();
                foreach (var entry in entries)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.Name) || entry.Id == null ||
                        !entry.Id.StartsWith("web-", StringComparison.Ordinal) ||
                        !Guid.TryParseExact(entry.Id.Substring(4), "N", out _) || !ids.Add(entry.Id) ||
                        entry.Executable != null || entry.EpicUri != null || entry.SteamAppId != 0 || entry.LaunchProfiles.Any())
                        throw new InvalidDataException("Invalid website entry.");
                    entry.WebUrl = Game.WebsiteUrl(entry.WebUrl);
                    ValidatePictureName(entry.Picture);
                    var game = Game.WebGame(entry);
                    game.Apply(entry, PictureLoader(entry.Picture));
                    loaded.Add(game);
                }
                websites = entries;
                Games.AddRange(loaded);
            }
            catch (Exception error)
            {
                if (LoadError == null) LoadError = "Couldn't load the saved websites. " + error.Message;
            }
        }

        private static void ValidatePictureName(string name)
        {
            if (name != null && (name != Path.GetFileName(name) || Path.GetExtension(name) != ".png" ||
                !Guid.TryParseExact(Path.GetFileNameWithoutExtension(name), "N", out _)))
                throw new InvalidDataException("Invalid app picture.");
        }

        private static DataContractJsonSerializer Serializer()
        {
            return new DataContractJsonSerializer(typeof(List<GameSettings>));
        }

        private string PicturePath(string name) { return Path.Combine(directory, "Pictures", name); }

        private Func<ImageSource> PictureLoader(string name)
        {
            if (name == null) return null;
            string path = PicturePath(name);
            return () =>
            {
                try { return ReadPicture(path); }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
                    error is NotSupportedException || error is FileFormatException) { return null; }
            };
        }

        public static BitmapSource ReadPicture(string file)
        {
            return ReadSizedPicture(file, 1280);
        }

        internal static BitmapSource ReadSizedPicture(string file, int bound)
        {
            using (var stream = File.OpenRead(file)) return ReadSizedPicture(stream, bound);
        }

        internal static BitmapSource ReadSizedPicture(Stream stream, int bound)
        {
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var frame = decoder.Frames[0];
            int width = frame.PixelWidth, height = frame.PixelHeight;
            stream.Position = 0;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            if (Math.Max(width, height) > bound)
            {
                if (width >= height) image.DecodePixelWidth = bound;
                else image.DecodePixelHeight = bound;
            }
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }

        public Game Save(GameSettings input, BitmapSource newPicture)
        {
            if (LoadError != null) throw new InvalidOperationException(LoadError);
            var draft = input.Copy();
            draft.Name = (draft.Name ?? "").Trim();
            if (draft.Name.Length == 0) throw new ArgumentException("Enter an app name.");
            var existing = Games.FirstOrDefault(g => g.Id == draft.Id);
            bool website = existing?.IsWebsite ?? draft.WebUrl != null;
            bool custom = existing == null || existing.Custom;
            if (website)
            {
                draft.WebUrl = Game.WebsiteUrl(draft.WebUrl);
                draft.Executable = draft.Arguments = null;
                draft.Profiles = null;
                draft.EpicUri = draft.EpicArguments = null;
                draft.SteamAppId = 0;
            }
            else
            {
                draft.WebUrl = null;
                ValidateProfiles(draft, existing);
            }
            if (custom && !website)
            {
                var path = Environment.ExpandEnvironmentVariables((draft.Executable ?? "").Trim().Trim('"'));
                if (!Path.IsPathRooted(path) || !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Choose the app's executable (.exe).");
                draft.Executable = Path.GetFullPath(path);
                bool targetChanged = existing == null || !string.Equals(existing.Target, draft.Executable, StringComparison.OrdinalIgnoreCase);
                if (targetChanged && !File.Exists(draft.Executable)) throw new FileNotFoundException("The selected executable could not be found.");
                if (Games.Any(g => g != existing && (string.Equals(g.Settings.Executable, draft.Executable, StringComparison.OrdinalIgnoreCase) ||
                    g.Settings.LaunchProfiles.Any(profile => string.Equals(profile.Executable, draft.Executable, StringComparison.OrdinalIgnoreCase)))))
                    throw new ArgumentException("This executable is already in your library.");
            }
            else if (draft.EpicUri != null)
            {
                draft.Executable = existing.Settings.Executable;
                draft.EpicArguments = existing.Settings.EpicArguments;
            }
            else draft.Executable = null;
            if (existing == null) draft.Id = (website ? "web-" : "user-") + Guid.NewGuid().ToString("N");
            Game game = existing ?? (website ? Game.WebGame(draft) : Game.CustomGame(draft));
            string oldPicture = game.Settings.Picture;
            // Only saved picture names are retained. Newly chosen files are imported before the catalog is committed.
            draft.Picture = draft.Picture == oldPicture ? oldPicture : null;
            string imported = null;
            Directory.CreateDirectory(directory);
            var source = website ? websites : saved;
            var entries = source.Where(s => s.Id != draft.Id).ToList();
            int previous = source.FindIndex(s => s.Id == draft.Id);
            entries.Insert(previous < 0 ? entries.Count : previous, draft);
            try
            {
                if (newPicture != null)
                {
                    draft.Picture = Guid.NewGuid().ToString("N") + ".png";
                    imported = PicturePath(draft.Picture);
                    Directory.CreateDirectory(Path.GetDirectoryName(imported));
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(newPicture));
                    using (var output = new FileStream(imported, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    { encoder.Save(output); output.Flush(true); }
                }
                WriteEntries(entries, website);
            }
            catch
            {
                if (imported != null) DiscardFile(imported);
                throw;
            }
            game.Apply(draft, PictureLoader(draft.Picture));
            if (existing == null) Games.Add(game);
            DiscardUnusedPicture(oldPicture);
            return game;
        }

        private void ValidateProfiles(GameSettings draft, Game existing)
        {
            var names = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            foreach (var profile in draft.LaunchProfiles)
            {
                profile.Name = (profile.Name ?? "").Trim();
                if (profile.Name.Length == 0 || !names.Add(profile.Name) ||
                    profile.Name.Equals("Default", StringComparison.CurrentCultureIgnoreCase))
                    throw new ArgumentException("Give each launch profile a different name.");
                string path = Environment.ExpandEnvironmentVariables((profile.Executable ?? "").Trim().Trim('"'));
                if (path.Length == 0) { profile.Executable = null; continue; }
                if (!Path.IsPathRooted(path) || !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Choose an executable (.exe) for " + profile.Name + ".");
                profile.Executable = Path.GetFullPath(path);
                bool alreadySaved = existing?.Settings.LaunchProfiles.Any(savedProfile =>
                    string.Equals(savedProfile.Executable, profile.Executable, StringComparison.OrdinalIgnoreCase)) == true;
                if (!alreadySaved && !File.Exists(profile.Executable))
                    throw new FileNotFoundException("The executable for " + profile.Name + " could not be found.");
                if (Games.Any(game => game != existing && game.Id != draft.Id &&
                    (string.Equals(game.Settings.Executable, profile.Executable, StringComparison.OrdinalIgnoreCase) ||
                    game.Settings.LaunchProfiles.Any(other => string.Equals(other.Executable, profile.Executable, StringComparison.OrdinalIgnoreCase)))))
                    throw new ArgumentException("The executable for " + profile.Name + " belongs to another game.");
            }
            if (draft.Profiles?.Count == 0) draft.Profiles = null;
            draft.Aliases = string.IsNullOrWhiteSpace(draft.Aliases) ? null : draft.Aliases.Trim();
        }

        internal SteamTitle[] NewSteamTitles(IEnumerable<SteamTitle> titles)
        {
            var targets = new HashSet<string>(Games.Select(g => g.Target), StringComparer.OrdinalIgnoreCase);
            var custom = Games.Where(g => g.Custom).Select(g => g.Target).ToArray();
            return titles.Where(t => t.AppId != 0 && !string.IsNullOrWhiteSpace(t.Name) &&
                targets.Add("steam://rungameid/" + t.AppId) &&
                (t.Directory == null || !custom.Any(path => path.StartsWith(t.Directory.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))))
                .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }

        internal List<Game> ImportSteam(IEnumerable<SteamTitle> selected, string root)
        {
            return AddSteam(selected, root, automatic: false);
        }

        internal bool SyncSteam(SteamScan scan, string root)
        {
            bool added = AddSteam(scan.Titles, root, automatic: true).Count > 0;
            var installed = new HashSet<uint>(scan.Titles.Select(t => t.AppId));
            foreach (var game in Games.Where(g => g.SteamId != 0))
            {
                if (installed.Contains(game.SteamId)) game.SetInstallation(true);
                else if (scan.Unreadable == 0) game.SetInstallation(false);
            }
            return added;
        }

        private List<Game> AddSteam(IEnumerable<SteamTitle> selected, string root, bool automatic)
        {
            if (LoadError != null) throw new InvalidOperationException(LoadError);
            var additions = new List<Game>();
            if (automatic)
            {
                var ignored = new HashSet<uint>(saved.Where(s => s.Removed && s.SteamAppId != 0).Select(s => s.SteamAppId));
                selected = selected.Where(title => !ignored.Contains(title.AppId));
            }
            var choices = NewSteamTitles(selected);
            if (choices.Length == 0) return additions;
            var entries = new List<GameSettings>(saved);
            var stockApps = Game.Library().Where(g => g.SteamId != 0).ToDictionary(g => g.SteamId);
            var removed = new HashSet<string>(saved.Where(s => s.Removed).Select(s => s.Id));
            foreach (var title in choices)
            {
                Game stock;
                stockApps.TryGetValue(title.AppId, out stock);
                var settings = stock == null ? new GameSettings { Id = "steam-" + title.AppId, Name = title.Name, SteamAppId = title.AppId } : stock.Settings;
                if (automatic && removed.Contains(settings.Id)) continue;
                var game = stock ?? Game.SteamGame(settings, root);
                game.Apply(settings, null);
                additions.Add(game);
                entries.RemoveAll(s => s.Id == settings.Id);
                entries.Add(settings);
            }
            if (additions.Count > 0)
            {
                WriteEntries(entries);
                Games.AddRange(additions);
            }
            return additions;
        }

        internal ImportCandidate[] NewApps(IEnumerable<ImportCandidate> candidates)
        {
            var targets = new HashSet<string>(Games.Select(g => g.Target), StringComparer.OrdinalIgnoreCase);
            var executables = new HashSet<string>(Games.Select(g => g.Settings.Executable).Where(p => p != null), StringComparer.OrdinalIgnoreCase);
            return candidates.Where(c => !targets.Contains(c.Target) && executables.Add(c.Executable) && targets.Add(c.Target))
                .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }

        internal List<Game> ImportApps(IEnumerable<ImportCandidate> choices)
        {
            return AddApps(choices, automatic: false);
        }

        internal bool SyncEpic(List<ImportCandidate> choices, bool complete)
        {
            bool added = AddApps(choices, automatic: true).Count > 0;
            var installed = new HashSet<string>(choices.Select(item => item.Uri), StringComparer.OrdinalIgnoreCase);
            foreach (var game in Games.Where(g => g.Settings.EpicUri != null))
            {
                if (installed.Contains(game.Settings.EpicUri)) game.SetInstallation(true);
                else if (complete) game.SetInstallation(false);
            }
            return added;
        }

        private List<Game> AddApps(IEnumerable<ImportCandidate> choices, bool automatic)
        {
            if (LoadError != null) throw new InvalidOperationException(LoadError);
            var additions = new List<Game>();
            var candidates = NewApps(choices);
            var moved = automatic ? FindMovedEpicApps(choices) : new Dictionary<Game, GameSettings>();
            if (candidates.Length == 0 && moved.Count == 0) return additions;
            var entries = new List<GameSettings>(saved);
            var removedEpic = saved.Where(s => s.Removed && s.EpicUri != null)
                .ToLookup(s => s.EpicUri, StringComparer.OrdinalIgnoreCase);
            var stockApps = candidates.Any(c => c.SteamAppId != 0)
                ? Game.Library().Where(g => g.SteamId != 0).ToDictionary(g => g.SteamId) : null;
            string steamRoot = null;
            foreach (var item in candidates)
            {
                var removed = item.Uri == null ? null : removedEpic[item.Uri].FirstOrDefault();
                if (automatic && removed != null) continue;
                var settings = new GameSettings { Id = (item.Uri == null ? "user-" : "epic-") + Guid.NewGuid().ToString("N"),
                    Name = item.Name, Executable = item.Executable, EpicUri = item.Uri, EpicArguments = item.Arguments };
                if (removed != null)
                {
                    settings.Id = removed.Id;
                    entries.RemoveAll(s => s.Id == settings.Id);
                }
                Game game;
                if (item.SteamAppId != 0)
                {
                    if (stockApps.TryGetValue(item.SteamAppId, out game)) settings = game.Settings;
                    else
                    {
                        settings.SteamAppId = item.SteamAppId;
                        settings.Id = "steam-" + item.SteamAppId;
                        settings.Executable = null;
                        game = Game.SteamGame(settings, steamRoot ?? (steamRoot = SteamLibrary.Locate()));
                    }
                    entries.RemoveAll(s => s.Id == settings.Id);
                }
                else game = item.Uri == null ? Game.CustomGame(settings) : Game.EpicGame(settings);
                game.Apply(settings, null);
                entries.Add(settings);
                additions.Add(game);
            }
            foreach (var settings in moved.Values)
                entries[entries.FindIndex(s => s.Id == settings.Id)] = settings;
            if (additions.Count > 0 || moved.Count > 0)
            {
                WriteEntries(entries);
                Games.AddRange(additions);
                foreach (var update in moved) update.Key.Apply(update.Value, PictureLoader(update.Value.Picture));
            }
            return additions;
        }

        private Dictionary<Game, GameSettings> FindMovedEpicApps(IEnumerable<ImportCandidate> choices)
        {
            var current = choices.ToLookup(item => item.Uri, StringComparer.OrdinalIgnoreCase);
            var updates = new Dictionary<Game, GameSettings>();
            foreach (var game in Games.Where(g => g.Settings.EpicUri != null))
            {
                var previous = game.Settings;
                var matches = current[previous.EpicUri];
                var item = matches.FirstOrDefault(c => string.Equals(c.Executable, previous.Executable, StringComparison.OrdinalIgnoreCase))
                    ?? matches.FirstOrDefault();
                if (item == null || (string.Equals(previous.Executable, item.Executable, StringComparison.OrdinalIgnoreCase) &&
                    previous.EpicArguments == item.Arguments)) continue;
                var settings = previous.Copy();
                settings.Executable = item.Executable;
                settings.EpicArguments = item.Arguments;
                updates.Add(game, settings);
            }
            return updates;
        }

        internal void Remove(Game game)
        {
            if (LoadError != null) throw new InvalidOperationException(LoadError);
            var entries = (game.IsWebsite ? websites : saved).Where(s => s.Id != game.Id).ToList();
            if (!game.IsWebsite && (game.SteamId != 0 || game.Settings.EpicUri != null || Game.Library().Any(g => g.Id == game.Id)))
                entries.Add(new GameSettings { Id = game.Id, Name = game.Name, Removed = true,
                    SteamAppId = game.SteamId, EpicUri = game.Settings.EpicUri });
            WriteEntries(entries, game.IsWebsite);
            Games.Remove(game);
            DiscardUnusedPicture(game.Settings.Picture);
        }

        private void DiscardUnusedPicture(string picture)
        {
            if (picture != null && !saved.Concat(websites).Any(entry => entry.Picture == picture)) DiscardFile(PicturePath(picture));
        }

        private void WriteEntries(List<GameSettings> entries, bool website = false)
        {
            Directory.CreateDirectory(directory);
            string target = website ? WebsitesFile : FileName;
            string temp = target + ".tmp";
            try
            {
                using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                { Serializer().WriteObject(output, entries); output.Flush(true); }
                if (File.Exists(target)) File.Replace(temp, target, null);
                else File.Move(temp, target);
                if (website) websites = entries;
                else saved = entries;
            }
            finally { DiscardFile(temp); }
        }

        private static void DiscardFile(string path)
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
