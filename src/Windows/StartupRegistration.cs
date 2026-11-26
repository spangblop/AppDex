using System;
using System.IO;
using System.Runtime.InteropServices;

namespace GameShelf
{
    internal sealed class StartupRegistration
    {
        internal const string Argument = "--startup";
        private const string Description = "Launch " + AppBuild.Name + " at Windows sign-in";
        private const string PreviousDescription = "Start AppDex in the system tray at sign-in";
        private readonly string shortcut;
        private readonly string executable;

        internal StartupRegistration(string folder, string executable)
        {
            if (string.IsNullOrEmpty(folder)) throw new DirectoryNotFoundException("Windows could not locate your Startup folder.");
            shortcut = Path.Combine(folder, AppBuild.Name + ".lnk");
            this.executable = Path.GetFullPath(executable);
        }

        internal bool IsRegistered()
        {
            return File.Exists(shortcut) && WithLink(shortcut, link =>
                string.Equals((string)link.TargetPath, executable, StringComparison.OrdinalIgnoreCase));
        }

        internal void SetRegistered(bool enable)
        {
            bool exists = File.Exists(shortcut);
            if (exists && !WithLink(shortcut, link =>
                string.Equals((string)link.TargetPath, executable, StringComparison.OrdinalIgnoreCase) ||
                (string)link.Description == Description || (!AppBuild.IsPreview && (string)link.Description == PreviousDescription)))
                throw new IOException("Another shortcut named " + AppBuild.Name + " already uses this Startup folder.");
            if (!enable)
            {
                if (exists) File.Delete(shortcut);
                return;
            }

            string folder = Path.GetDirectoryName(shortcut);
            Directory.CreateDirectory(folder);
            string staging = Path.Combine(folder, AppBuild.Name + "-" + Guid.NewGuid().ToString("N") + ".lnk");
            try
            {
                WithLink(staging, link =>
                {
                    link.TargetPath = executable;
                    link.Arguments = Argument;
                    link.WorkingDirectory = Path.GetDirectoryName(executable);
                    link.Description = Description;
                    link.Save();
                    return true;
                });
                if (exists) File.Replace(staging, shortcut, null);
                else File.Move(staging, shortcut);
            }
            finally { if (File.Exists(staging)) File.Delete(staging); }
        }

        private static bool WithLink(string path, Func<dynamic, bool> use)
        {
            object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
            object link = null;
            try
            {
                link = ((dynamic)shell).CreateShortcut(path);
                return use(link);
            }
            finally
            {
                if (link != null) Marshal.ReleaseComObject(link);
                Marshal.ReleaseComObject(shell);
            }
        }
    }
}
