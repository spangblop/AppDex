using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace GameShelf
{
    internal enum AppearanceTarget
    {
        None, TileWidth, TileHeight, TileGap, TileCorners, TileOutline, Artwork, ArtworkCorners, SideArtwork, Caption,
        InterfaceText, Headings, TileTitles, TileDetails, Background, Panels, Controls, Edges, MutedText, ArtworkText,
        Warnings, Accent, Scale, SearchWidth, ControlCorners, SidebarWidth, PinHeight, PinArtwork, HoverShift, HoverGlow, Transitions
    }

    internal enum HighlightPart { Frame, Corners, Width, Height, Area, Gap, Halo }

    internal struct HighlightBox
    {
        internal readonly Rect Bounds, Clip;
        internal readonly HighlightPart Part;
        internal readonly double Radius;

        internal HighlightBox(Rect bounds, HighlightPart part, double radius, Rect clip)
        {
            Bounds = bounds;
            Part = part;
            Radius = radius;
            Clip = clip;
        }
    }

    public sealed class AppearanceHighlight : FrameworkElement
    {
        private static readonly DependencyProperty InkProperty = DependencyProperty.Register(
            "Ink", typeof(Brush), typeof(AppearanceHighlight),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
        private HighlightBox[] boxes = new HighlightBox[0];
        private bool pulsing;

        public AppearanceHighlight()
        {
            IsHitTestVisible = false;
            Visibility = Visibility.Collapsed;
            SetResourceReference(InkProperty, "AppAccentText");
        }

        internal void Show(HighlightBox[] targets, bool motion)
        {
            bool visible = targets.Length > 0;
            if (!boxes.SequenceEqual(targets))
            {
                boxes = targets;
                InvalidateVisual();
            }
            Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            bool animate = visible && motion;
            if (animate == pulsing) return;
            pulsing = animate;
            Opacity = .85;
            BeginAnimation(OpacityProperty, animate
                ? new DoubleAnimation(.42, 1, TimeSpan.FromMilliseconds(620)) {
                    AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                }
                : null);
        }

        protected override void OnRender(DrawingContext dc)
        {
            var accent = GetValue(InkProperty) as SolidColorBrush;
            Color color = accent?.Color ?? Colors.White;
            var edge = Pen(color, 225, 2);
            var shadow = Pen(Color.FromRgb(10, 16, 24), 140, 4);
            var halo = Pen(color, 40, 8);
            var wash = new SolidColorBrush(Color.FromArgb(24, color.R, color.G, color.B));
            foreach (var box in boxes)
            {
                if (!box.Clip.IsEmpty) dc.PushClip(new RectangleGeometry(box.Clip));
                var bounds = box.Bounds;
                double radius = Math.Min(box.Radius, Math.Min(bounds.Width, bounds.Height) / 2);
                if (box.Part == HighlightPart.Area || box.Part == HighlightPart.Gap)
                    dc.DrawRoundedRectangle(wash, null, bounds, radius, radius);
                var geometry = Outline(bounds, radius, box.Part);
                dc.DrawGeometry(null, halo, geometry);
                dc.DrawGeometry(null, shadow, geometry);
                dc.DrawGeometry(null, edge, geometry);
                if (!box.Clip.IsEmpty) dc.Pop();
            }
        }

        private static Geometry Outline(Rect bounds, double radius, HighlightPart part)
        {
            if (part == HighlightPart.Halo)
            {
                bounds.Inflate(3, 3);
                radius += 3;
            }
            if (part != HighlightPart.Corners && part != HighlightPart.Width && part != HighlightPart.Height)
                return new RectangleGeometry(bounds, radius, radius);

            var geometry = new StreamGeometry();
            using (var dc = geometry.Open())
            {
                double l = bounds.Left, t = bounds.Top, r = bounds.Right, b = bounds.Bottom;
                if (part == HighlightPart.Corners)
                {
                    double length = Math.Min(Math.Min(bounds.Width, bounds.Height) / 2, Math.Max(12, radius + 8));
                    Corner(dc, new Point(l, t + length), new Point(l, t + radius), new Point(l + radius, t), new Point(l + length, t), radius);
                    Corner(dc, new Point(r - length, t), new Point(r - radius, t), new Point(r, t + radius), new Point(r, t + length), radius);
                    Corner(dc, new Point(r, b - length), new Point(r, b - radius), new Point(r - radius, b), new Point(r - length, b), radius);
                    Corner(dc, new Point(l + length, b), new Point(l + radius, b), new Point(l, b - radius), new Point(l, b - length), radius);
                }
                else if (part == HighlightPart.Width)
                {
                    double y = t + bounds.Height / 2;
                    Line(dc, new Point(l, y), new Point(r, y));
                    Line(dc, new Point(l, y - 6), new Point(l, y + 6));
                    Line(dc, new Point(r, y - 6), new Point(r, y + 6));
                }
                else
                {
                    double x = l + bounds.Width / 2;
                    Line(dc, new Point(x, t), new Point(x, b));
                    Line(dc, new Point(x - 6, t), new Point(x + 6, t));
                    Line(dc, new Point(x - 6, b), new Point(x + 6, b));
                }
            }
            return geometry;
        }

        private static void Corner(StreamGeometryContext dc, Point start, Point beforeArc, Point afterArc, Point end, double radius)
        {
            dc.BeginFigure(start, false, false);
            dc.LineTo(beforeArc, true, false);
            if (radius > 0)
                dc.ArcTo(afterArc, new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
            dc.LineTo(end, true, false);
        }

        private static void Line(StreamGeometryContext dc, Point from, Point to)
        {
            dc.BeginFigure(from, false, false);
            dc.LineTo(to, true, false);
        }

        private static Pen Pen(Color color, byte opacity, double width)
        {
            return new Pen(new SolidColorBrush(Color.FromArgb(opacity, color.R, color.G, color.B)), width) {
                StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round
            };
        }
    }
}
