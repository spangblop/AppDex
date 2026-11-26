using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace GameShelf
{
    internal enum ControllerCommand { Up = 1, Down = 2, Left = 4, Right = 8, Select = 0x1000, Back = 0x2000, Options = 0x4000, Search = 0x8000 }

    internal sealed class ControllerInput : IDisposable
    {
        private readonly DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(32) };
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Func<Window> foreground;
        private readonly Action<Window, ControllerCommand> dispatch;
        private Window active;
        private int previous;
        private long repeatAt;

        internal ControllerInput(Func<Window> foreground, Action<Window, ControllerCommand> dispatch)
        {
            this.foreground = foreground;
            this.dispatch = dispatch;
            timer.Tick += Poll;
            timer.Start();
        }

        internal static int ButtonMask(int buttons, int x, int y)
        {
            if ((buttons & 15) != 0 || Math.Max(Math.Abs(x), Math.Abs(y)) < 16000) return buttons;
            if (Math.Abs(x) > Math.Abs(y))
                return buttons | (x > 0 ? (int)ControllerCommand.Right : (int)ControllerCommand.Left);
            return buttons | (y > 0 ? (int)ControllerCommand.Up : (int)ControllerCommand.Down);
        }

        private void Poll(object sender, EventArgs e)
        {
            Window window = foreground();
            if (window == null)
            {
                active = null;
                return;
            }
            int buttons = ReadButtons();
            long now = clock.ElapsedMilliseconds;
            if (active != window)
            {
                active = window;
                previous = buttons;
                repeatAt = now + 330;
                return;
            }
            int pressed = buttons & ~previous;
            int direction = buttons & 15;
            if (direction != (previous & 15)) repeatAt = now + 330;
            else if (direction != 0 && now >= repeatAt)
            {
                pressed |= direction;
                repeatAt = now + 105;
            }
            previous = buttons;
            foreach (var command in Commands)
            {
                if ((pressed & (int)command) == 0) continue;
                dispatch(window, command);
                break;
            }
        }

        private static readonly ControllerCommand[] Commands = {
            ControllerCommand.Back, ControllerCommand.Select, ControllerCommand.Options, ControllerCommand.Search,
            ControllerCommand.Up, ControllerCommand.Down, ControllerCommand.Left, ControllerCommand.Right
        };

        private uint controller;
        private long scanAgainAt;

        private int ReadButtons()
        {
            try
            {
                long now = clock.ElapsedMilliseconds;
                if (now < scanAgainAt) return 0;
                if (XInputGetState(controller, out ControllerState state) == 0)
                    return ButtonMask(state.Buttons, state.LeftX, state.LeftY);
                for (uint slot = 0; slot < 4; slot++)
                {
                    if (slot == controller || XInputGetState(slot, out state) != 0) continue;
                    controller = slot;
                    return ButtonMask(state.Buttons, state.LeftX, state.LeftY);
                }
                scanAgainAt = now + 500;
            }
            catch (DllNotFoundException) { timer.Stop(); }
            catch (EntryPointNotFoundException) { timer.Stop(); }
            return 0;
        }

        public void Dispose() => timer.Stop();

        [StructLayout(LayoutKind.Sequential)]
        private struct ControllerState
        {
            internal uint Packet;
            internal ushort Buttons;
            internal byte LeftTrigger, RightTrigger;
            internal short LeftX, LeftY, RightX, RightY;
        }

        [DllImport("xinput1_4.dll")]
        private static extern uint XInputGetState(uint index, out ControllerState state);
    }
}
