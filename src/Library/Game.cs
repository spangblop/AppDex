using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace GameShelf
{
    internal sealed class Game : INotifyPropertyChanged
    {
        public string Id { get; private set; }
        public string Name { get { return Settings.Name; } }
        public string Platform { get; private set; }
        public string Target { get { return Custom ? Settings.Executable : defaultTarget; } }
        public string Source { get { return SteamId != 0 ? "Steam" : Settings.EpicUri != null ? "Epic Games" : "Windows"; } }
        public string SourceStatus { get { return Launching ? Source + " · Opening" : Playing ? Source + " · Playing" :
            IsInstalled == false ? "Not installed" : Source; } }
        public uint SteamId { get { uint id; return defaultTarget != null && defaultTarget.StartsWith("steam://", StringComparison.Ordinal) &&
            uint.TryParse(defaultTarget.Split('/').Last(), out id) ? id : 0; } }
        public bool Custom { get; private set; }
        public GameSettings Settings { get; private set; }
        public ImageSource DefaultArtwork { get; private set; }
        public ImageSource Artwork { get; private set; }
        public long Launches { get; private set; }
        public long PlayTicks { get; private set; }
        public bool Playing { get; private set; }
        public bool Launching { get; private set; }
        public bool? IsInstalled { get; private set; }
        public string Statistics { get; private set; }
        public string PlatformStatus { get; private set; }
        public string AccessibleName { get { return Launching ? "Opening " + Name :
            IsInstalled == false && !Playing ? Name + ", not installed" : "Play " + Name; } }
        public string EditName { get { return "Edit " + Name; } }
        public event PropertyChangedEventHandler PropertyChanged;
        private readonly string defaultTarget;
        private readonly string aliases;

        private Game(string name, string platform, string target, string art, string aliases = "", bool custom = false, ImageSource steamCover = null)
        {
            Id = art;
            Platform = platform;
            defaultTarget = target;
            Custom = custom;
            this.aliases = custom ? "" : name + " " + aliases;
            Settings = new GameSettings { Id = Id, Name = name, Executable = custom ? target : null, Arguments = "" };
            UpdateStatistics(0, 0, false);
            DefaultArtwork = steamCover ?? (custom || art.StartsWith("steam-", StringComparison.Ordinal) || art.StartsWith("epic-", StringComparison.Ordinal) ? (ImageSource)CustomBackdrop() :
                new BitmapImage(new Uri("pack://application:,,,/AppDex;component/Assets/" + art + ".jpg")));
            DefaultArtwork.Freeze();
            Artwork = DefaultArtwork;
        }

        internal static Game CustomGame(GameSettings settings)
        {
            return new Game(settings.Name, "WINDOWS", settings.Executable, settings.Id, "", true);
        }

        internal static Game SteamGame(GameSettings settings, string root, ImageSource artwork = null)
        {
            return new Game(settings.Name, "STEAM", "steam://rungameid/" + settings.SteamAppId,
                settings.Id, steamCover: artwork ?? SteamLibrary.Artwork(root, settings.SteamAppId));
        }

        internal static Game EpicGame(GameSettings settings)
        {
            return new Game(settings.Name, "EPIC GAMES", settings.EpicUri, settings.Id);
        }

        internal static DrawingImage CustomBackdrop()
        {
            var group = new DrawingGroup();
            using (var dc = group.Open())
            {
                dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(56, 89, 78), Color.FromRgb(25, 38, 48), 35),
                    null, new Rect(0, 0, 400, 225));
                var ink = new SolidColorBrush(Color.FromArgb(46, 220, 245, 232));
                dc.DrawRectangle(ink, null, new Rect(161, 42, 34, 34));
                dc.DrawRectangle(ink, null, new Rect(201, 42, 34, 34));
                dc.DrawRectangle(ink, null, new Rect(161, 82, 34, 34));
            }
            return new DrawingImage(group);
        }

        internal void Apply(GameSettings settings, ImageSource picture)
        {
            Settings = settings;
            Artwork = picture ?? DefaultArtwork;
            Changed("Name");
            Changed("AccessibleName");
            Changed("EditName");
            Changed("Target");
            Changed("Artwork");
        }

        public static Game[] Library()
        {
            return new[] {
                new Game("Apex Legends", "STEAM", "steam://rungameid/1172470", "apex"),
                new Game("Wuthering Waves", "STEAM", "steam://rungameid/3513350", "waves", "WuWa"),
                new Game("Minecraft Bedrock", "WINDOWS", "Microsoft.MinecraftUWP_8wekyb3d8bbwe!Game", "bedrock", "for Windows"),
                new Game("Beat Saber", "STEAM VR", "steam://rungameid/620980", "saber"),
                new Game("Minecraft Preview", "WINDOWS", "Microsoft.MinecraftWindowsBeta_8wekyb3d8bbwe!Game", "preview", "Bedrock beta for Windows")
            };
        }

        public bool Matches(string[] terms)
        {
            string searchTerms = Name + " " + Platform + " " + aliases;
            return terms.All(term => searchTerms.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void Changed(string property)
        {
            var notify = PropertyChanged;
            if (notify != null) notify(this, new PropertyChangedEventArgs(property));
        }

        internal void SetLaunching(bool launching)
        {
            if (Launching == launching) return;
            Launching = launching;
            Changed("Launching");
            Changed("SourceStatus");
            Changed("AccessibleName");
        }

        internal void SetInstallation(bool? installed)
        {
            if (IsInstalled == installed) return;
            IsInstalled = installed;
            Changed("SourceStatus");
            Changed("AccessibleName");
        }

        public void UpdateStatistics(long launches, long ticks, bool active)
        {
            Launches = launches;
            PlayTicks = ticks;
            if (Playing != active)
            {
                Playing = active;
                Changed("SourceStatus");
                Changed("AccessibleName");
            }
            string summary = string.Format("{0:N0} {1}  ·  {2}", launches, launches == 1 ? "launch" : "launches", FormatPlaytime(ticks));
            if (Statistics != summary) { Statistics = summary; Changed("Statistics"); }
            string platform = active ? Platform + "  ·  PLAYING" : Platform;
            if (PlatformStatus != platform) { PlatformStatus = platform; Changed("PlatformStatus"); }
        }

        internal static string FormatPlaytime(decimal ticks)
        {
            decimal minutes = decimal.Floor(ticks / TimeSpan.TicksPerMinute);
            return ticks > 0 && minutes == 0 ? "<1m" : string.Format("{0:N0}h {1:00}m", decimal.Floor(minutes / 60), minutes % 60);
        }

        internal static ProcessStartInfo StartInfo(Game game, GameSettings settings)
        {
            if (game.Custom)
                return new ProcessStartInfo(settings.Executable, settings.Arguments ?? "") {
                    WorkingDirectory = Path.GetDirectoryName(settings.Executable), UseShellExecute = true };
            if (settings.EpicUri != null)
            {
                if (string.IsNullOrWhiteSpace(settings.Arguments))
                    return new ProcessStartInfo(settings.EpicUri) { UseShellExecute = true };
                return new ProcessStartInfo(settings.Executable, (settings.EpicArguments + " " + settings.Arguments).Trim())
                    { WorkingDirectory = Path.GetDirectoryName(settings.Executable), UseShellExecute = true };
            }
            if (game.Target.StartsWith("steam://", StringComparison.Ordinal))
            {
                var location = SteamLocation.Read(SteamLocation.PreferencesFile);
                bool selectedClient = !string.IsNullOrWhiteSpace(location.ClientFolder) && File.Exists(Path.Combine(location.ClientFolder, "steam.exe"));
                if (!selectedClient && string.IsNullOrWhiteSpace(settings.Arguments) && HasSteamProtocol())
                    return new ProcessStartInfo(game.Target) { UseShellExecute = true };
                string appId = new Uri(game.Target).Segments.Last();
                string client = location.FindClient();
                if (client == null) throw new FileNotFoundException("Choose your Steam installation under Import library, or install Steam to launch this app.");
                return new ProcessStartInfo(Path.Combine(client, "steam.exe"), "-applaunch " + appId +
                    (string.IsNullOrWhiteSpace(settings.Arguments) ? "" : " " + settings.Arguments))
                    { UseShellExecute = true };
            }
            return null;
        }

        private static bool HasSteamProtocol()
        {
            try
            {
                using (var key = Registry.ClassesRoot.OpenSubKey(@"steam\shell\open\command"))
                    return key != null && !string.IsNullOrWhiteSpace(key.GetValue(null) as string);
            }
            catch (SecurityException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        public static void Launch(Game game)
        {
            GameSettings settings = game.Settings;
            var info = StartInfo(game, settings);
            if (info != null)
            {
                using (Process.Start(info)) { }
                return;
            }

            // Activate the registered edition by identity so game updates can move its executable.
            var manager = (IApplicationActivationManager)new ApplicationActivationManager();
            try
            {
                uint processId;
                int result = manager.ActivateApplication(game.Target,
                    string.IsNullOrWhiteSpace(settings.Arguments) ? null : settings.Arguments, 0, out processId);
                Marshal.ThrowExceptionForHR(result);
            }
            finally
            {
                Marshal.ReleaseComObject(manager);
            }
        }

        [ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
        private class ApplicationActivationManager { }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D")]
        private interface IApplicationActivationManager
        {
            [PreserveSig]
            int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appId,
                [MarshalAs(UnmanagedType.LPWStr)] string arguments, uint options, out uint processId);
        }
    }
}
