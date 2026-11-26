using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace GameShelf
{
    internal sealed class LaunchHotkeys : IDisposable
    {
        private const int HotkeyMessage = 0x0312;
        private const uint NoRepeat = 0x4000;
        private const int FirstQuickId = 1, SecondQuickId = 2, ProbeId = 0, FirstGameId = 3;
        internal const int DefaultModifiers = 1, DefaultKey = 'X';
        internal static readonly int[] ModifierValues = { 0, 1, 2, 3, 5, 6, 7 };
        internal static readonly string[] ModifierLabels = { "None", "Alt", "Ctrl", "Ctrl+Alt", "Alt+Shift", "Ctrl+Shift", "Ctrl+Alt+Shift" };
        internal static readonly char[] Keys = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".ToCharArray();
        private readonly HwndSource source;
        private readonly Action open;
        private readonly Action<Game> launch;
        private readonly Dictionary<int, (Game Game, LaunchProfile Profile, int Modifiers, int Key)> games =
            new Dictionary<int, (Game, LaunchProfile, int, int)>();
        private int quickId = FirstQuickId;
        private int quickModifiers, quickKey;
        internal Func<int, int, bool> Capture;
        internal Action<Game, LaunchProfile> LaunchProfile;

        private static IEnumerable<(LaunchProfile Profile, int Modifiers, int Key)> Bindings(GameSettings settings)
        {
            if (settings.ShortcutKey != 0) yield return (null, settings.ShortcutModifiers, settings.ShortcutKey);
            foreach (var profile in settings.LaunchProfiles.Where(profile => profile.ShortcutKey != 0))
                yield return (profile, profile.ShortcutModifiers, profile.ShortcutKey);
        }

        internal LaunchHotkeys(Action open, Action<Game> launch) : this(open, launch, DefaultModifiers, DefaultKey) { }

        internal LaunchHotkeys(Action open, Action<Game> launch, int modifiers, int key)
        {
            this.open = open;
            this.launch = launch;
            source = new HwndSource(new HwndSourceParameters("AppDex shortcuts") { ParentWindow = new IntPtr(-3) });
            source.AddHook(HandleMessage);
            quickModifiers = modifiers;
            quickKey = key;
            QuickLaunchRegistered = Register(quickId, modifiers, key);
        }

        internal bool QuickLaunchRegistered { get; private set; }
        internal string QuickLaunchLabel => Describe(quickModifiers, quickKey);

        internal static string Describe(int modifiers, int key)
        {
            if (key == 0) return "None";
            var parts = new List<string>();
            if ((modifiers & 2) != 0) parts.Add("Ctrl");
            if ((modifiers & 1) != 0) parts.Add("Alt");
            if ((modifiers & 4) != 0) parts.Add("Shift");
            if ((modifiers & 8) != 0) parts.Add("Win");
            parts.Add(new KeyConverter().ConvertToString(KeyInterop.KeyFromVirtualKey(key)));
            return string.Join("+", parts);
        }

        internal static bool IsAssignable(int modifiers, int key)
        {
            if (key <= 0 || key >= 256 || key == 0x7b || KeyInterop.KeyFromVirtualKey(key) == Key.None) return false;
            if ((key >= 0x10 && key <= 0x12) || (key >= 0xa0 && key <= 0xa5) || key == 0x5b || key == 0x5c) return false;
            return modifiers >= 0 && modifiers <= 15 && (modifiers != 0 || (key >= 0x70 && key <= 0x87));
        }

        internal string SetQuickLaunch(IEnumerable<Game> library, int modifiers, int key)
        {
            if (QuickLaunchRegistered && quickModifiers == modifiers && quickKey == key) return null;
            if (modifiers != 0 && library.Any(game => Bindings(game.Settings).Any(binding =>
                binding.Modifiers == modifiers && binding.Key == key))) return "That shortcut is assigned to a game or profile.";

            int next = quickId == FirstQuickId ? SecondQuickId : FirstQuickId;
            if (modifiers != 0 && !Register(next, modifiers, key))
                return "That shortcut is unavailable in Windows. Try a different one.";

            if (QuickLaunchRegistered) UnregisterHotKey(source.Handle, quickId);
            quickId = next;
            quickModifiers = modifiers;
            quickKey = key;
            QuickLaunchRegistered = modifiers != 0;
            return null;
        }

        internal string Validate(IEnumerable<Game> library, Game current, int modifiers, int key)
        {
            if (key == 0) return null;
            if (quickModifiers != 0 && modifiers == quickModifiers && key == quickKey)
                return QuickLaunchLabel + " opens Quick Launch. Choose another shortcut.";
            if (library.Any(game => game != current && Bindings(game.Settings).Any(binding =>
                binding.Modifiers == modifiers && binding.Key == key))) return "That shortcut is assigned to another game or profile.";
            if (current != null && games.Values.Any(binding => binding.Game == current && binding.Modifiers == modifiers && binding.Key == key))
                return null;
            if (!Register(ProbeId, modifiers, key)) return "That shortcut is unavailable in Windows. Try a different one.";
            UnregisterHotKey(source.Handle, ProbeId);
            return null;
        }

        internal string ValidateDraft(IEnumerable<Game> library, Game current, GameSettings draft)
        {
            var used = new HashSet<(int, int)>();
            foreach (var binding in Bindings(draft))
            {
                if (!used.Add((binding.Modifiers, binding.Key))) return "Give the default launch and each profile a different shortcut.";
                string error = Validate(library, current, binding.Modifiers, binding.Key);
                if (error != null) return binding.Profile == null ? error : binding.Profile.Name + " — " + error;
            }
            return null;
        }

        internal string Refresh(IEnumerable<Game> library)
        {
            ClearGames();
            var unavailable = new List<string>();
            int id = FirstGameId;
            foreach (var game in library)
            foreach (var binding in Bindings(game.Settings))
            {
                if ((quickModifiers != 0 && binding.Modifiers == quickModifiers && binding.Key == quickKey) ||
                    !Register(id, binding.Modifiers, binding.Key))
                    unavailable.Add(binding.Profile == null ? game.Name : game.Name + " (" + binding.Profile.Name + ")");
                else games.Add(id++, (game, binding.Profile, binding.Modifiers, binding.Key));
            }
            return unavailable.Count == 0 ? null : "Couldn't register launch shortcuts for " + string.Join(", ", unavailable) + ".";
        }

        private bool Register(int id, int modifiers, int key)
        {
            return IsAssignable(modifiers, key) &&
                RegisterHotKey(source.Handle, id, (uint)modifiers | NoRepeat, (uint)key);
        }

        private IntPtr HandleMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message != HotkeyMessage) return IntPtr.Zero;
            int id = wParam.ToInt32();
            if (id == quickId && QuickLaunchRegistered && Matches(lParam, quickModifiers, quickKey))
            {
                handled = true;
                if (Capture?.Invoke(quickModifiers, quickKey) != true) open();
            }
            else if (games.TryGetValue(id, out var binding))
            {
                if (!Matches(lParam, binding.Modifiers, binding.Key)) return IntPtr.Zero;
                handled = true;
                if (Capture?.Invoke(binding.Modifiers, binding.Key) != true)
                {
                    if (binding.Profile == null) launch(binding.Game);
                    else LaunchProfile?.Invoke(binding.Game, binding.Profile);
                }
            }
            return IntPtr.Zero;
        }

        private static bool Matches(IntPtr value, int modifiers, int key)
        {
            long shortcut = value.ToInt64();
            return (shortcut & 15) == modifiers && ((shortcut >> 16) & 0xffff) == key;
        }

        private void ClearGames()
        {
            foreach (int id in games.Keys) UnregisterHotKey(source.Handle, id);
            games.Clear();
        }

        public void Dispose()
        {
            ClearGames();
            if (QuickLaunchRegistered) UnregisterHotKey(source.Handle, quickId);
            source.Dispose();
        }

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr window, int id);
    }
}
