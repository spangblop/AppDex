using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace GameShelf
{
    internal static class InstallFolders
    {
        private const int InsufficientBuffer = 122;
        private const uint MainPackage = 0x10;

        internal static string Resolve(Game game, string executable, string profileOverride)
        {
            string path = string.IsNullOrWhiteSpace(profileOverride) ? executable : profileOverride;
            if (!string.IsNullOrWhiteSpace(path))
            {
                path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
                if (!Path.IsPathRooted(path)) return null;
                string directory = Path.GetDirectoryName(Path.GetFullPath(path));
                return Directory.Exists(directory) ? directory : null;
            }
            if (game.SteamId != 0) return SteamLibrary.FindInstalled(game.SteamId)?.Directory;
            int separator = game.Target.IndexOf('!');
            return !game.Custom && game.Settings.EpicUri == null && separator > 0
                ? PackageFolder(game.Target.Substring(0, separator)) : null;
        }

        private static string PackageFolder(string family)
        {
            uint count = 0, capacity = 0;
            if (FindPackagesByPackageFamily(family, MainPackage, ref count, null, ref capacity,
                IntPtr.Zero, IntPtr.Zero) != InsufficientBuffer || count == 0 || capacity == 0) return null;

            var names = new IntPtr[count];
            IntPtr buffer = Marshal.AllocHGlobal(checked((int)capacity * sizeof(char)));
            try
            {
                if (FindPackagesByPackageFamily(family, MainPackage, ref count, names, ref capacity, buffer, IntPtr.Zero) != 0)
                    return null;
                for (int i = 0; i < count; i++)
                {
                    string name = Marshal.PtrToStringUni(names[i]);
                    uint length = 0;
                    if (GetPackagePathByFullName(name, ref length, null) != InsufficientBuffer) continue;
                    var path = new StringBuilder(checked((int)length));
                    if (GetPackagePathByFullName(name, ref length, path) == 0) return path.ToString();
                }
                return null;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int FindPackagesByPackageFamily(string family, uint filters, ref uint count,
            [Out] IntPtr[] names, ref uint capacity, IntPtr buffer, IntPtr properties);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int GetPackagePathByFullName(string name, ref uint length, StringBuilder path);
    }
}
