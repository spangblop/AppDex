using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace GameShelf
{
    internal sealed class TileHover
    {
        private static readonly DependencyProperty StateProperty =
            DependencyProperty.RegisterAttached("State", typeof(TileHover), typeof(TileHover));

        private readonly Button tile;
        private readonly AppearanceOptions options;
        private FrameworkElement surface;
        private Border glow, sweep;
        private TranslateTransform transform;
        private Vector target, current;
        private double targetGlow, currentGlow;
        private bool rendering, dragging, hovered;
        private bool glowInFront;
        private TimeSpan lastFrame;

        private TileHover(Button tile, AppearanceOptions options)
        {
            this.tile = tile;
            this.options = options;
            tile.MouseEnter += Move;
            tile.MouseMove += Move;
            tile.MouseLeave += Leave;
            tile.Unloaded += Unload;
            tile.IsEnabledChanged += delegate { if (!tile.IsEnabled) Reset(); };
        }

        internal static void Attach(Button tile, AppearanceOptions options)
        {
            tile.SetValue(StateProperty, new TileHover(tile, options));
        }

        internal static void SetDragging(Button tile, bool dragging)
        {
            var hover = (TileHover)tile.GetValue(StateProperty);
            if (hover == null) return;
            hover.dragging = dragging;
            if (dragging) hover.Reset();
        }

        private void Move(object sender, MouseEventArgs e) { Aim(e.GetPosition(tile)); }

        private void Aim(Point point)
        {
            Size size = tile.RenderSize;
            if (dragging || !tile.IsEnabled || size.Width <= 0 || size.Height <= 0) return;

            if (surface == null)
            {
                surface = tile.Template?.FindName("TileSurface", tile) as FrameworkElement;
                glow = tile.Template?.FindName("HoverGlow", tile) as Border;
                sweep = tile.Template?.FindName("HoverSweep", tile) as Border;
                if (surface == null || glow == null)
                {
                    surface = null;
                    return;
                }
                transform = new TranslateTransform();
                surface.RenderTransform = transform;
            }

            bool inside = new Rect(size).Contains(point);
            if (inside && !hovered && options.Sweep && options.Motion > 0 && sweep != null) StartSweep();
            hovered = inside;
            double travel = options.HoverShift * 2;
            target = inside ? new Vector((.5 - point.X / size.Width) * travel, (.5 - point.Y / size.Height) * travel)
                : new Vector();
            targetGlow = inside ? options.HoverGlow : 0;
            Start();
        }

        private void StartSweep()
        {
            var offset = new TranslateTransform(-1, 0);
            var brush = new LinearGradientBrush {
                StartPoint = new Point(0, .5), EndPoint = new Point(1, .5), RelativeTransform = offset,
                GradientStops = new GradientStopCollection {
                    new GradientStop(Colors.Transparent, .34),
                    new GradientStop(Color.FromArgb(80, 255, 255, 255), .50),
                    new GradientStop(Colors.Transparent, .66)
                }
            };
            sweep.Background = brush;
            offset.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-1, 1,
                TimeSpan.FromMilliseconds(460 * options.Motion)) { FillBehavior = FillBehavior.Stop });
        }

        private void Leave(object sender, MouseEventArgs e)
        {
            hovered = false;
            target = new Vector();
            targetGlow = 0;
            Start();
        }

        private void Start()
        {
            if (rendering || surface == null || (target == current && targetGlow == currentGlow)) return;
            if (options.Motion == 0)
            {
                current = target;
                currentGlow = targetGlow;
                UpdateSurface();
                return;
            }
            rendering = true;
            lastFrame = TimeSpan.Zero;
            CompositionTarget.Rendering += Render;
        }

        private void Render(object sender, EventArgs e)
        {
            var time = ((RenderingEventArgs)e).RenderingTime;
            double elapsed = lastFrame == TimeSpan.Zero ? 16 : Math.Max(0, Math.Min(40, (time - lastFrame).TotalMilliseconds));
            lastFrame = time;
            double step = 1 - Math.Exp(-elapsed / (42 * Math.Max(.05, options.Motion)));
            current += (target - current) * step;
            currentGlow += (targetGlow - currentGlow) * step;
            if ((target - current).Length < .035 && Math.Abs(targetGlow - currentGlow) < .01)
            {
                current = target;
                currentGlow = targetGlow;
                Stop();
            }

            UpdateSurface();
        }

        private void UpdateSurface()
        {
            transform.X = current.X;
            transform.Y = current.Y;
            glow.Opacity = currentGlow;
            UpdateGlowLayer(currentGlow > 0);
        }

        private void UpdateGlowLayer(bool inFront)
        {
            if (glowInFront == inFront) return;
            glowInFront = inFront;
            Panel.SetZIndex(tile, inFront ? 1 : 0);
        }

        private void Stop()
        {
            if (!rendering) return;
            CompositionTarget.Rendering -= Render;
            rendering = false;
        }

        private void Reset()
        {
            Stop();
            hovered = false;
            current = target = new Vector();
            currentGlow = targetGlow = 0;
            if (transform != null) transform.X = transform.Y = 0;
            if (glow != null) glow.Opacity = 0;
            UpdateGlowLayer(false);
            if (sweep != null) sweep.Background = null;
        }

        private void Unload(object sender, RoutedEventArgs e)
        {
            Reset();
            surface = null;
            glow = null;
            sweep = null;
            transform = null;
        }
    }
}
