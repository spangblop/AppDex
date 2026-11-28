using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace GameShelf
{
    internal sealed class RunningGame
    {
        public int ProcessId;
        public string GameId;
        public string Instance;
        public long StartedUtcTicks;
    }

    [DataContract]
    internal sealed class GameActivity
    {
        [DataMember] public long Launches;
        [DataMember] public long PlayTicks;
        [DataMember] public string[] Active = new string[0];
    }

    [DataContract]
    internal sealed class ActivityBook
    {
        [DataMember] public Dictionary<string, GameActivity> Games = new Dictionary<string, GameActivity>();
        private long? previousSample;
        public bool Dirty { get; private set; }

        internal void ResumeSampling(long sample)
        {
            if (sample > 0 && sample <= GameProcesses.AwakeTicks()) previousSample = sample;
        }

        internal long ObserveLibrary(IEnumerable<Game> games, int session, out bool changed)
        {
            var library = games.ToArray();
            var ids = new HashSet<string>(library.Select(game => game.Id));
            var custom = library.SelectMany(game => new[] { game.Settings.Executable }.Concat(
                    game.Settings.LaunchProfiles.Select(profile => profile.Executable))
                .Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => new { Path = path, game.Id }))
                .GroupBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Id, StringComparer.OrdinalIgnoreCase);
            var sample = GameProcesses.Read(session, custom).Where(game => ids.Contains(game.GameId))
                .Concat(SteamLibrary.Running(library, session)).ToArray();
            long now = GameProcesses.AwakeTicks();
            changed = Observe(sample, now, DateTime.UtcNow.Ticks);
            return now;
        }

        // Keep process identities in the checkpoint so reopening AppDex during a session cannot add a launch.
        public bool Observe(RunningGame[] sample, long awakeTicks, long utcTicks)
        {
            var groups = sample.ToLookup(p => p.GameId);
            long delta = previousSample.HasValue ? Math.Max(0, awakeTicks - previousSample.Value) : 0;
            previousSample = awakeTicks;
            bool transition = false;
            foreach (string id in Games.Keys.Union(groups.Select(group => group.Key)).ToArray())
            {
                GameActivity game;
                if (!Games.TryGetValue(id, out game)) Games[id] = game = new GameActivity();
                var observed = groups[id].ToArray();
                string[] instances = observed.Select(p => p.Instance).Distinct().OrderBy(key => key).ToArray();
                bool previouslyActive = game.Active.Length != 0;
                bool newSession = instances.Length > 0 && !instances.Intersect(game.Active).Any();
                if (newSession) game.Launches++;
                long played = previouslyActive ? delta : 0;
                if (!previouslyActive && observed.Length > 0 && observed.All(p => p.StartedUtcTicks > 0))
                    played = Math.Min(delta, Math.Max(0, utcTicks - observed.Min(p => p.StartedUtcTicks)));
                game.PlayTicks += played;
                transition |= !instances.SequenceEqual(game.Active);
                game.Active = instances;
                Dirty |= played > 0;
            }
            Dirty |= transition;
            return transition;
        }

        private static DataContractJsonSerializer Serializer()
        {
            return new DataContractJsonSerializer(typeof(ActivityBook),
                new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
        }

        public static ActivityBook Load(string file)
        {
            try
            {
                using (var input = File.OpenRead(file))
                {
                    var book = (ActivityBook)Serializer().ReadObject(input);
                    if (book == null || book.Games == null || book.Games.Values.Any(g =>
                        g == null || g.Launches < 0 || g.PlayTicks < 0 || g.Active == null))
                        throw new InvalidDataException("The activity file contains invalid statistics.");
                    return book;
                }
            }
            catch (FileNotFoundException) { return new ActivityBook(); }
            catch (DirectoryNotFoundException) { return new ActivityBook(); }
        }

        public void Save(string file)
        {
            if (!Dirty) return;
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            string temp = file + ".tmp";
            using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                Serializer().WriteObject(output, this);
                output.Flush(true);
            }
            if (File.Exists(file)) File.Replace(temp, file, null);
            else File.Move(temp, file);
            Dirty = false;
        }

        internal void ResetGame(string id, string file)
        {
            if (!Games.TryGetValue(id, out GameActivity previous) ||
                (previous.Launches == 0 && previous.PlayTicks == 0)) return;
            // Keep the running process identities so the existing session does not count as a new launch.
            Games[id] = new GameActivity { Active = previous.Active };
            bool wasDirty = Dirty;
            Dirty = true;
            try { Save(file); }
            catch
            {
                Games[id] = previous;
                Dirty = wasDirty;
                throw;
            }
        }

        internal void RecordWebsiteOpen(string id)
        {
            if (!Games.TryGetValue(id, out GameActivity activity)) Games[id] = activity = new GameActivity();
            activity.Launches++;
            Dirty = true;
        }
    }
}
