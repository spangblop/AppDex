using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace GameShelf
{
    internal sealed class HoverAudio : IDisposable
    {
        private const int InQueue = 0x10;
        private const uint DefaultDevice = uint.MaxValue;
        private static readonly int HeaderSize = Marshal.SizeOf<WaveHeader>();
        private static readonly int FlagsOffset = Marshal.OffsetOf<WaveHeader>(nameof(WaveHeader.Flags)).ToInt32();

        // A short queue absorbs quick crossings without playing a trail of ticks after the pointer stops.
        private readonly IntPtr[] headers = new IntPtr[4];
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private long nextOpenAttempt;
        private IntPtr output;
        private IntPtr samples;

        internal bool Play()
        {
            if (output == IntPtr.Zero && !Open()) return false;
            foreach (IntPtr header in headers)
            {
                if (header == IntPtr.Zero || (Marshal.ReadInt32(header, FlagsOffset) & InQueue) != 0) continue;
                if (waveOutWrite(output, header, HeaderSize) == 0) return true;
                Dispose();
                break;
            }
            return false;
        }

        private bool Open()
        {
            if (clock.ElapsedMilliseconds < nextOpenAttempt) return false;
            nextOpenAttempt = clock.ElapsedMilliseconds + 1000;

            byte[] clip;
            WaveFormat format;
            using (var stream = typeof(HoverAudio).Assembly.GetManifestResourceStream("AppDex.Sounds.hover.wav"))
            {
                if (stream == null || stream.Length < 44) return false;
                using (var reader = new BinaryReader(stream))
                    clip = ReadClip(reader, out format);
            }
            if (clip == null || waveOutOpen(out IntPtr device, DefaultDevice, ref format, IntPtr.Zero, IntPtr.Zero, 0) != 0)
                return false;

            output = device;
            samples = Marshal.AllocHGlobal(clip.Length);
            Marshal.Copy(clip, 0, samples, clip.Length);
            for (int i = 0; i < headers.Length; i++)
            {
                headers[i] = Marshal.AllocHGlobal(HeaderSize);
                Marshal.StructureToPtr(new WaveHeader { Data = samples, Length = clip.Length }, headers[i], false);
                if (waveOutPrepareHeader(output, headers[i], HeaderSize) == 0) continue;
                Dispose();
                return false;
            }
            return true;
        }

        private static byte[] ReadClip(BinaryReader reader, out WaveFormat format)
        {
            format = default;
            var stream = reader.BaseStream;
            if (new string(reader.ReadChars(4)) != "RIFF") return null;
            stream.Position = 8;
            if (new string(reader.ReadChars(4)) != "WAVE") return null;
            while (stream.Length - stream.Position >= 8)
            {
                string chunk = new string(reader.ReadChars(4));
                int length = reader.ReadInt32();
                if (length < 0 || length > stream.Length - stream.Position) return null;
                long next = stream.Position + length + (length & 1);
                if (chunk == "fmt " && length >= 16)
                    format = new WaveFormat { Tag = reader.ReadUInt16(), Channels = reader.ReadUInt16(),
                        SamplesPerSecond = reader.ReadUInt32(), AverageBytesPerSecond = reader.ReadUInt32(),
                        BlockAlign = reader.ReadUInt16(), BitsPerSample = reader.ReadUInt16() };
                else if (chunk == "data")
                    return format.Tag == 1 && format.BlockAlign > 0 && length > 0 && length % format.BlockAlign == 0
                        ? reader.ReadBytes(length) : null;
                stream.Position = next;
            }
            return null;
        }

        internal void Stop()
        {
            if (output != IntPtr.Zero) waveOutReset(output);
        }

        public void Dispose()
        {
            if (output == IntPtr.Zero) return;
            Stop();
            foreach (IntPtr header in headers)
                if (header != IntPtr.Zero) waveOutUnprepareHeader(output, header, HeaderSize);
            if (waveOutClose(output) != 0) return;

            output = IntPtr.Zero;
            foreach (IntPtr header in headers)
                if (header != IntPtr.Zero) Marshal.FreeHGlobal(header);
            Array.Clear(headers, 0, headers.Length);
            Marshal.FreeHGlobal(samples);
            samples = IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        private struct WaveFormat
        {
            internal ushort Tag, Channels;
            internal uint SamplesPerSecond, AverageBytesPerSecond;
            internal ushort BlockAlign, BitsPerSample, ExtraSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WaveHeader
        {
            internal IntPtr Data;
            internal int Length, BytesRecorded;
            internal UIntPtr User;
            internal int Flags, Loops;
            internal IntPtr Next;
            internal UIntPtr Reserved;
        }

        [DllImport("winmm.dll")]
        private static extern uint waveOutOpen(out IntPtr output, uint device, ref WaveFormat format, IntPtr callback,
            IntPtr instance, uint flags);
        [DllImport("winmm.dll")]
        private static extern uint waveOutPrepareHeader(IntPtr output, IntPtr header, int size);
        [DllImport("winmm.dll")]
        private static extern uint waveOutWrite(IntPtr output, IntPtr header, int size);
        [DllImport("winmm.dll")]
        private static extern uint waveOutReset(IntPtr output);
        [DllImport("winmm.dll")]
        private static extern uint waveOutUnprepareHeader(IntPtr output, IntPtr header, int size);
        [DllImport("winmm.dll")]
        private static extern uint waveOutClose(IntPtr output);
    }
}
