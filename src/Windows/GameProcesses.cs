using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace GameShelf
{
    internal static class GameProcesses
    {
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, int id);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")] private static extern bool GetProcessTimes(IntPtr handle, out long created,
            out long exited, out long kernelTime, out long userTime);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(
            IntPtr handle, uint flags, StringBuilder path, ref int size);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern int GetPackageFamilyName(
            IntPtr handle, ref uint size, StringBuilder family);
        [DllImport("kernel32.dll")] private static extern bool QueryUnbiasedInterruptTime(out long ticks);

        public static long AwakeTicks()
        {
            long ticks;
            if (!QueryUnbiasedInterruptTime(out ticks)) throw new Win32Exception();
            return ticks;
        }

        internal static string ExecutablePath(int id)
        {
            IntPtr handle = OpenProcess(0x1000, false, id);
            if (handle == IntPtr.Zero) return null;
            try { return ReadPath(handle); }
            finally { CloseHandle(handle); }
        }

        private static string ReadPath(IntPtr handle)
        {
            var buffer = new StringBuilder(1024);
            int length = buffer.Capacity;
            return QueryFullProcessImageName(handle, 0, buffer, ref length) ? buffer.ToString() : null;
        }

        internal static string Identify(string name, string family, string path)
        {
            switch (name.ToLowerInvariant())
            {
                case "r5apex": case "r5apex_dx12": return "apex";
                case "beat saber": return "saber";
                case "client-win64-shipping":
                    return path.EndsWith(@"\Wuthering Waves Game\Client\Binaries\Win64\Client-Win64-Shipping.exe",
                        StringComparison.OrdinalIgnoreCase) ? "waves" : null;
                case "minecraft.windows":
                    if (family.Equals("Microsoft.MinecraftWindowsBeta_8wekyb3d8bbwe", StringComparison.OrdinalIgnoreCase)) return "preview";
                    if (family.Equals("Microsoft.MinecraftUWP_8wekyb3d8bbwe", StringComparison.OrdinalIgnoreCase)) return "bedrock";
                    if (family.Length != 0) return null;
                    if (path.EndsWith(@"\Minecraft Preview for Windows\Content\Minecraft.Windows.exe", StringComparison.OrdinalIgnoreCase) ||
                        path.IndexOf(@"\WindowsApps\Microsoft.MinecraftWindowsBeta_", StringComparison.OrdinalIgnoreCase) >= 0) return "preview";
                    if (path.EndsWith(@"\Minecraft for Windows\Content\Minecraft.Windows.exe", StringComparison.OrdinalIgnoreCase) ||
                        path.IndexOf(@"\WindowsApps\Microsoft.MinecraftUWP_", StringComparison.OrdinalIgnoreCase) >= 0) return "bedrock";
                    return null;
                default: return null;
            }
        }

        public static RunningGame[] Read(int session, IDictionary<string, string> custom = null)
        {
            var result = new List<RunningGame>();
            var customNames = new HashSet<string>((custom ?? new Dictionary<string, string>()).Keys.Select(Path.GetFileNameWithoutExtension),
                StringComparer.OrdinalIgnoreCase);
            foreach (var process in Process.GetProcesses())
            using (process)
            {
                try
                {
                    string name = process.ProcessName.ToLowerInvariant();
                    bool needsPath = customNames.Contains(name) || name == "minecraft.windows" || name == "client-win64-shipping";
                    if (!needsPath && Identify(name, "", "") == null) continue;
                    if (process.SessionId != session) continue;
                    IntPtr handle = OpenProcess(0x1000, false, process.Id); // PROCESS_QUERY_LIMITED_INFORMATION
                    long started = 0;
                    string path = "", family = "";
                    if (handle != IntPtr.Zero)
                    {
                        try
                        {
                            long created, exited, kernel, user;
                            if (GetProcessTimes(handle, out created, out exited, out kernel, out user))
                                started = DateTime.FromFileTimeUtc(created).Ticks;
                            if (needsPath)
                            {
                                path = ReadPath(handle) ?? "";
                                if (name == "minecraft.windows")
                                {
                                    var buffer = new StringBuilder(1024);
                                    uint length = (uint)buffer.Capacity;
                                    if (GetPackageFamilyName(handle, ref length, buffer) == 0) family = buffer.ToString();
                                }
                            }
                        }
                        finally { CloseHandle(handle); }
                    }
                    string id = Identify(name, family, path);
                    if (id != null) result.Add(new RunningGame {
                        GameId = id, Instance = process.Id + "/" + started, StartedUtcTicks = started });
                    string customId;
                    if (custom != null && custom.TryGetValue(path, out customId)) result.Add(new RunningGame {
                        GameId = customId, Instance = process.Id + "/" + started, StartedUtcTicks = started });
                }
                catch (InvalidOperationException) { /* The process ended during the snapshot. */ }
                catch (Win32Exception) { /* The process is no longer accessible. */ }
            }
            return result.ToArray();
        }
    }
}
