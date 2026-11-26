using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace GameShelf
{
    public sealed class LaunchSpinner : FrameworkElement
    {
        public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
            nameof(IsActive), typeof(bool), typeof(LaunchSpinner), new PropertyMetadata(false, ActiveChanged));
        public static readonly DependencyProperty AnimatedProperty = DependencyProperty.Register(
            nameof(Animated), typeof(bool), typeof(LaunchSpinner), new PropertyMetadata(true, MotionChanged));

        private static readonly Pen Backdrop = FrozenPen(Color.FromArgb(235, 14, 18, 23), 4);
        private static readonly Pen Track = FrozenPen(Color.FromArgb(105, 235, 242, 248), 2);
        private readonly RotateTransform spin = new RotateTransform();

        public LaunchSpinner()
        {
            IsHitTestVisible = false;
            Visibility = Visibility.Collapsed;
            RenderTransformOrigin = new Point(.5, .5);
            RenderTransform = spin;
            IsVisibleChanged += delegate { SyncAnimation(); };
        }

        public bool IsActive
        {
            get => (bool)GetValue(IsActiveProperty);
            set => SetValue(IsActiveProperty, value);
        }

        public bool Animated
        {
            get => (bool)GetValue(AnimatedProperty);
            set => SetValue(AnimatedProperty, value);
        }

        private static void ActiveChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
        {
            ((LaunchSpinner)element).Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
        }

        private static void MotionChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
        {
            ((LaunchSpinner)element).SyncAnimation();
        }

        private void SyncAnimation()
        {
            spin.BeginAnimation(RotateTransform.AngleProperty, IsVisible && Animated
                ? new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(850)) { RepeatBehavior = RepeatBehavior.Forever }
                : null);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double radius = Math.Min(ActualWidth, ActualHeight) / 2 - 2;
            if (radius <= 0) return;
            var center = new Point(ActualWidth / 2, ActualHeight / 2);
            dc.DrawEllipse(null, Backdrop, center, radius, radius);
            dc.DrawEllipse(null, Track, center, radius, radius);
            var arc = new StreamGeometry();
            using (var context = arc.Open())
            {
                context.BeginFigure(new Point(center.X, center.Y - radius), false, false);
                context.ArcTo(new Point(center.X + radius * .87, center.Y + radius * .5),
                    new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
            }
            var highlight = new Pen(TryFindResource("AppAccentOnDark") as Brush ?? Brushes.White, 2.5) {
                StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round
            };
            dc.DrawGeometry(null, highlight, arc);
        }

        private static Pen FrozenPen(Color color, double thickness)
        {
            var pen = new Pen(new SolidColorBrush(color), thickness);
            pen.Freeze();
            return pen;
        }
    }
}
