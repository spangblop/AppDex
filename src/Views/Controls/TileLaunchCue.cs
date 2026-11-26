using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace GameShelf
{
    internal enum LaunchAnimation { Ripple, Pulse, Streak, Off }

    public sealed class TileLaunchCue : FrameworkElement
    {
        private static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
            "Progress", typeof(double), typeof(TileLaunchCue),
            new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));

        private LaunchAnimation animation;
        private Color accent;
        private double corners;
        private Brush streak;

        public TileLaunchCue()
        {
            IsHitTestVisible = false;
            Unloaded += delegate { Clear(); };
        }

        private static TileLaunchCue Find(Button tile)
        {
            if (tile == null || !tile.IsLoaded) return null;
            return tile.Template?.FindName("LaunchCue", tile) as TileLaunchCue ??
                (tile.Content as Grid)?.Children.OfType<TileLaunchCue>().FirstOrDefault();
        }

        internal static void Play(Button tile, AppearanceOptions options)
        {
            Find(tile)?.Play(options, tile.Content is Grid ? (double?)options.ControlCorner : null);
        }

        internal static void Clear(Button tile) { Find(tile)?.Clear(); }

        internal void Play(AppearanceOptions options, double? cornerRadius = null)
        {
            Clear();
            if (options.LaunchCue == LaunchAnimation.Off || options.Motion <= 0 || !IsVisible) return;
            animation = options.LaunchCue;
            corners = cornerRadius ?? options.TileCorner;
            accent = TryFindResource("AppHoverGlow") is Color color ? color : Colors.LightGreen;
            if (animation == LaunchAnimation.Streak) streak = StreakBrush(accent);
            double duration = (animation == LaunchAnimation.Ripple ? 380 : animation == LaunchAnimation.Pulse ? 270 : 330) * options.Motion;
            BeginAnimation(ProgressProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(Math.Max(85, duration))) {
                FillBehavior = FillBehavior.Stop
            });
        }

        private void Clear()
        {
            BeginAnimation(ProgressProperty, null);
            streak = null;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double progress = (double)GetValue(ProgressProperty);
            if (progress >= 1 || ActualWidth < 8 || ActualHeight < 8) return;
            var face = new Rect(RenderSize);
            double radius = Math.Min(corners, Math.Min(face.Width, face.Height) / 2);
            switch (animation)
            {
                case LaunchAnimation.Ripple:
                    Ring(dc, face, radius, progress, .9);
                    if (progress > .18) Ring(dc, face, radius, (progress - .18) / .82, .48);
                    dc.DrawRoundedRectangle(Tint(.18 * (1 - progress)), null, face, radius, radius);
                    break;
                case LaunchAnimation.Pulse:
                    double strength = Math.Sin(Math.PI * (.14 + progress * .86));
                    dc.DrawRoundedRectangle(Tint(.28 * strength), new Pen(Tint(.90 * strength), 2), face, radius, radius);
                    break;
                case LaunchAnimation.Streak:
                    double band = face.Width * .58;
                    double x = -band + progress * (face.Width + band);
                    dc.PushClip(new RectangleGeometry(face, radius, radius));
                    dc.DrawRectangle(streak, null, new Rect(x, 0, band, face.Height));
                    dc.Pop();
                    break;
            }
        }

        private void Ring(DrawingContext dc, Rect face, double radius, double progress, double strength)
        {
            double spread = 10 * progress;
            face.Inflate(spread, spread);
            double round = radius == 0 ? 0 : radius + spread;
            dc.DrawRoundedRectangle(null, new Pen(Tint(strength * (1 - progress) * (1 - progress)), 2.5), face, round, round);
        }

        private Brush Tint(double opacity) => new SolidColorBrush(Color.FromArgb(
            (byte)Math.Round(255 * opacity), accent.R, accent.G, accent.B));

        private static Brush StreakBrush(Color color)
        {
            var brush = new LinearGradientBrush { StartPoint = new Point(0, .4), EndPoint = new Point(1, .6),
                GradientStops = new GradientStopCollection {
                    new GradientStop(Colors.Transparent, 0),
                    new GradientStop(Color.FromArgb(78, color.R, color.G, color.B), .37),
                    new GradientStop(Color.FromArgb(128, 255, 255, 255), .53),
                    new GradientStop(Color.FromArgb(60, color.R, color.G, color.B), .71),
                    new GradientStop(Colors.Transparent, 1)
                } };
            brush.Freeze();
            return brush;
        }
    }
}
