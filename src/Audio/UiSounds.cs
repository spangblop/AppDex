using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Media;

namespace GameShelf
{
    internal enum UiCue { Hover, Drop, Launch, Notice }

    internal sealed class UiSounds : IDisposable
    {
        private const long MinimumGapMilliseconds = 110;
        private readonly HoverAudio hover = new HoverAudio();
        private readonly Dictionary<UiCue, SoundPlayer> players = new Dictionary<UiCue, SoundPlayer>();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private long lastPlayed = -MinimumGapMilliseconds;
        private SoundPlayer current;

        internal void Play(UiCue cue)
        {
            if (cue == UiCue.Hover)
            {
                hover.Play();
                return;
            }
            if (clock.ElapsedMilliseconds - lastPlayed < MinimumGapMilliseconds) return;
            if (!players.TryGetValue(cue, out SoundPlayer player))
            {
                string name = cue.ToString().ToLowerInvariant() + ".wav";
                Stream stream = typeof(UiSounds).Assembly.GetManifestResourceStream("AppDex.Sounds." + name);
                if (stream == null) return;
                player = new SoundPlayer(stream);
                players.Add(cue, player);
            }

            try
            {
                player.Play();
                current = player;
                lastPlayed = clock.ElapsedMilliseconds;
            }
            catch (Exception error) when (error is IOException || error is InvalidOperationException || error is TimeoutException)
            {
                // Audio feedback is optional when the playback service is unavailable.
            }
        }

        internal void Stop()
        {
            current?.Stop();
            current = null;
            lastPlayed = -MinimumGapMilliseconds;
            hover.Stop();
        }

        public void Dispose()
        {
            Stop();
            hover.Dispose();
            foreach (var player in players.Values)
            {
                player.Stream.Dispose();
                player.Dispose();
            }
            players.Clear();
        }
    }
}
