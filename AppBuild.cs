using System;
using System.IO;

namespace GameShelf
{
    internal static class AppBuild
    {
#if APPDEX_PREVIEW
        internal const string Name = "AppDex Preview";
#else
        internal const string Name = "AppDex";
#endif

        internal static bool IsPreview => Name == "AppDex Preview";

        internal static string DataDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Name);
    }
}
