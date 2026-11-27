using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GameShelf
{
    internal sealed class AppUpdate
    {
        internal Version Version;
        internal Uri Download;
        internal Uri ReleasePage;
        internal string ReleaseNotes;
        internal string ZipHash;
        internal long ZipSize;
    }

    internal static class AppUpdates
    {
        private const string Repository = "spangblop/AppDex";
        private const int MaximumZip = 25 * 1024 * 1024, MaximumExecutable = 64 * 1024 * 1024;
        private static readonly Regex TagPattern = new Regex(@"\Av(?<version>[0-9]+\.[0-9]+\.[0-9]+)(?<channel>-beta|-preview)?\z");
        internal static Version CurrentVersion => typeof(AppUpdates).Assembly.GetName().Version;
        internal static string CurrentLabel => typeof(AppUpdates).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? CurrentVersion.ToString(3);

        [DataContract]
        private sealed class Release
        {
            [DataMember(Name = "tag_name")] public string Tag { get; set; }
            [DataMember(Name = "body")] public string Body { get; set; }
            [DataMember(Name = "draft")] public bool Draft { get; set; }
            [DataMember(Name = "assets")] public Asset[] Assets { get; set; }
        }

        [DataContract]
        private sealed class Asset
        {
            [DataMember(Name = "name")] public string Name { get; set; }
            [DataMember(Name = "state")] public string State { get; set; }
            [DataMember(Name = "size")] public long Size { get; set; }
            [DataMember(Name = "digest")] public string Digest { get; set; }
            [DataMember(Name = "browser_download_url")] public string Url { get; set; }
        }

        internal static async Task<AppUpdate> Check(CancellationToken token)
        {
            using (var client = Client())
            {
                client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true };
                return await CheckReleasePages(client, CurrentVersion, token).ConfigureAwait(false);
            }
        }

        private static async Task<AppUpdate> CheckReleasePages(HttpClient client, Version current, CancellationToken token)
        {
            AppUpdate latest = null;
            const string feedUrl = "https://api.github.com/repos/" + Repository + "/releases?per_page=30&page=";
            for (int page = 1; ; page++)
            {
                using (var response = await client.GetAsync(feedUrl + page.ToString(CultureInfo.InvariantCulture), token)
                    .ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        latest = Latest(stream, latest?.Version ?? current) ?? latest;

                    // Use GitHub's pagination signal while keeping requests pinned to the release endpoint.
                    if (!response.Headers.TryGetValues("Link", out var links) ||
                        !links.Any(link => Regex.IsMatch(link, @">\s*;\s*rel=""next""")))
                        return latest;
                }
            }
        }

        internal static AppUpdate Latest(Stream stream, Version current)
        {
            var feed = (Release[])new DataContractJsonSerializer(typeof(Release[])).ReadObject(stream);
            if (feed == null) throw new InvalidDataException("GitHub returned an empty release feed.");
            var installed = new Version(current.Major, current.Minor, current.Build);
            AppUpdate latest = null;
            foreach (var release in feed.Where(item => item != null && !item.Draft))
            {
                var match = TagPattern.Match(release.Tag ?? "");
                if (!match.Success || !Version.TryParse(match.Groups["version"].Value, out Version version) ||
                    version <= installed || (latest != null && version <= latest.Version)) continue;
                string channel = match.Groups["channel"].Value;
                string prefix = channel == "-beta" ? "Beta-" : channel == "-preview" ? "Preview-" : "";
                string name = "AppDex-" + prefix + version.ToString(3) + "-Windows.zip";
                string url = "https://github.com/" + Repository + "/releases/download/" + release.Tag + "/" + name;
                var asset = (release.Assets ?? Array.Empty<Asset>()).FirstOrDefault(item => item != null && item.Name == name &&
                    item.Url == url && item.State == "uploaded" && item.Size > 0 && item.Size <= MaximumZip &&
                    (item.Digest ?? "").StartsWith("sha256:", StringComparison.Ordinal) && ValidHash(item.Digest.Substring(7)));
                if (asset != null) latest = new AppUpdate { Version = version, Download = new Uri(url),
                    ReleasePage = new Uri("https://github.com/" + Repository + "/releases/tag/" + release.Tag),
                    ReleaseNotes = release.Body?.Trim(), ZipSize = asset.Size, ZipHash = asset.Digest.Substring(7) };
            }
            return latest;
        }

        private static HttpClient Client()
        {
            var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) {
                Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = 2 * 1024 * 1024 };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AppDex/" + CurrentVersion.ToString(3));
            return client;
        }

        internal static async Task Download(AppUpdate update, string archive, CancellationToken token)
        {
            using (var client = Client())
            {
                Uri url = update.Download;
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                    {
                        int code = (int)response.StatusCode;
                        if (code >= 300 && code < 400 && response.Headers.Location != null)
                        {
                            url = new Uri(url, response.Headers.Location);
                            if (url.Scheme != Uri.UriSchemeHttps || !url.IsDefaultPort || url.UserInfo.Length != 0 ||
                                (url.Host != "github.com" && url.Host != "release-assets.githubusercontent.com" &&
                                 url.Host != "objects.githubusercontent.com" && url.Host != "github-releases.githubusercontent.com"))
                                throw new InvalidDataException("GitHub redirected the update to an unexpected address.");
                            continue;
                        }
                        response.EnsureSuccessStatusCode();
                        using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var target = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                        {
                            var buffer = new byte[81920];
                            long size = 0;
                            int read;
                            while ((read = await source.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) != 0)
                            {
                                size += read;
                                if (size > update.ZipSize) throw new InvalidDataException("The update download is larger than expected.");
                                await target.WriteAsync(buffer, 0, read, token).ConfigureAwait(false);
                            }
                            if (size != update.ZipSize) throw new InvalidDataException("The update download is incomplete.");
                        }
                        return;
                    }
                }
                throw new InvalidDataException("GitHub redirected the update too many times.");
            }
        }

        internal static string Unpack(AppUpdate update, string archive, string executable)
        {
            using (var package = File.OpenRead(archive))
            {
                if (package.Length != update.ZipSize || !string.Equals(Hash(package), update.ZipHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The update download did not match GitHub's checksum.");
                package.Position = 0;
                using (var zip = new ZipArchive(package, ZipArchiveMode.Read))
                {
                    var entry = zip.Entries.Count == 1 ? zip.Entries[0] : null;
                    if (entry == null || entry.FullName != "AppDex.exe" || entry.Length <= 0 || entry.Length > MaximumExecutable)
                        throw new InvalidDataException("The update ZIP does not contain the expected AppDex executable.");
                    using (var source = entry.Open())
                    using (var target = new FileStream(executable, FileMode.CreateNew))
                    {
                        var buffer = new byte[81920];
                        long size = 0;
                        int read;
                        while ((read = source.Read(buffer, 0, buffer.Length)) != 0)
                        {
                            size += read;
                            if (size > entry.Length) throw new InvalidDataException("The update executable is larger than expected.");
                            target.Write(buffer, 0, read);
                        }
                        if (size != entry.Length) throw new InvalidDataException("The update executable is incomplete.");
                        target.Flush(true);
                    }
                }
            }
            var version = ExecutableVersion(executable);
            if (version.Major != update.Version.Major || version.Minor != update.Version.Minor || version.Build != update.Version.Build)
                throw new InvalidDataException("The update executable has the wrong version.");
            return FileHash(executable);
        }

        internal static Version ExecutableVersion(string executable)
        {
            var identity = AssemblyName.GetAssemblyName(executable);
            if (identity.Name != "AppDex") throw new InvalidDataException("The update is not an AppDex executable.");
            return identity.Version;
        }

        internal static string FileHash(string path)
        {
            using (var stream = File.OpenRead(path)) return Hash(stream);
        }

        private static string Hash(Stream stream)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }

        internal static bool ValidHash(string value) => value != null && Regex.IsMatch(value, @"\A[0-9a-fA-F]{64}\z");
    }
}
