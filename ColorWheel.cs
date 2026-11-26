using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameShelf
{
    public sealed class ColorWheel : FrameworkElement
    {
        private static readonly Lazy<BitmapSource> WheelImage = new Lazy<BitmapSource>(DrawWheel);
        private static readonly Pen DarkRing = new Pen(Brushes.Black, 3);
        private static readonly Pen LightRing = new Pen(Brushes.White, 1.5);
        private double hue, saturation, brightness = 1;
        internal event Action<Color> Changed;
        internal Color Color { get { return FromHsv(hue, saturation, brightness); } }

        public ColorWheel()
        {
            Focusable = true;
            Cursor = Cursors.Cross;
        }

        internal double Brightness
        {
            get { return brightness; }
            set { SetHsv(hue, saturation, value); }
        }

        internal void SelectColor(Color color)
        {
            double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), difference = max - min;
            double angle = hue;
            if (difference > 0)
            {
                if (max == r) angle = (g - b) / difference;
                else if (max == g) angle = (b - r) / difference + 2;
                else angle = (r - g) / difference + 4;
                angle /= 6;
            }
            SetHsv(angle, max > 0 ? difference / max : saturation, max);
        }

        protected override void OnRender(DrawingContext dc)
        {
            Point center = Center();
            double radius = Radius();
            if (radius <= 0) return;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
            dc.DrawImage(WheelImage.Value, new Rect(center.X - radius, center.Y - radius, radius * 2, radius * 2));
            if (IsKeyboardFocused) dc.DrawEllipse(null, LightRing, center, radius + 3, radius + 3);
            double angle = hue * 2 * Math.PI;
            var marker = center + new Vector(Math.Cos(angle), -Math.Sin(angle)) * saturation * radius;
            dc.DrawEllipse(null, DarkRing, marker, 5.25, 5.25);
            dc.DrawEllipse(null, LightRing, marker, 5.25, 5.25);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            Point point = e.GetPosition(this);
            if ((point - Center()).Length > Radius() + 5) return;
            Focus();
            if (CaptureMouse()) SelectPoint(point);
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!IsMouseCaptured) return;
            if (e.LeftButton == MouseButtonState.Pressed) SelectPoint(e.GetPosition(this));
            else ReleaseMouseCapture();
            e.Handled = true;
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (!IsMouseCaptured) return;
            SelectPoint(e.GetPosition(this));
            ReleaseMouseCapture();
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.Key)
            {
                case Key.Left: SetHsv(hue - 1.0 / 72, saturation, brightness); break;
                case Key.Right: SetHsv(hue + 1.0 / 72, saturation, brightness); break;
                case Key.Up: SetHsv(hue, saturation + .04, brightness); break;
                case Key.Down: SetHsv(hue, saturation - .04, brightness); break;
                default: return;
            }
            e.Handled = true;
        }

        protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); InvalidateVisual(); }
        protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); InvalidateVisual(); }

        private Point Center() { return new Point(RenderSize.Width / 2, RenderSize.Height / 2); }
        private double Radius() { return Math.Max(0, Math.Min(RenderSize.Width, RenderSize.Height) / 2 - 8); }

        private void SelectPoint(Point point)
        {
            double radius = Radius();
            if (radius <= 0) return;
            Vector delta = point - Center();
            double angle = delta.Length > .1 ? Math.Atan2(-delta.Y, delta.X) / (2 * Math.PI) : hue;
            SetHsv(angle, delta.Length / radius, brightness);
        }

        private void SetHsv(double angle, double amount, double level)
        {
            var before = Color;
            hue = angle - Math.Floor(angle);
            saturation = Math.Max(0, Math.Min(1, amount));
            brightness = Math.Max(0, Math.Min(1, level));
            InvalidateVisual();
            var changed = Changed;
            if (changed != null && before != Color) changed(Color);
        }

        private static Color FromHsv(double hue, double saturation, double value)
        {
            double sector = (hue - Math.Floor(hue)) * 6;
            double chroma = value * saturation;
            double secondary = chroma * (1 - Math.Abs(sector % 2 - 1));
            double offset = value - chroma;
            byte high = Channel(chroma + offset), middle = Channel(secondary + offset), low = Channel(offset);
            switch ((int)sector)
            {
                case 0: return Color.FromRgb(high, middle, low);
                case 1: return Color.FromRgb(middle, high, low);
                case 2: return Color.FromRgb(low, high, middle);
                case 3: return Color.FromRgb(low, middle, high);
                case 4: return Color.FromRgb(middle, low, high);
                default: return Color.FromRgb(high, low, middle);
            }
        }

        private static byte Channel(double value) { return (byte)Math.Round(value * 255); }

        private static BitmapSource DrawWheel()
        {
            const int size = 320;
            const double radius = size / 2.0;
            var pixels = new byte[size * size * 4];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                double dx = x + .5 - radius, dy = radius - y - .5;
                double distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance > radius + .5) continue;
                Color color = FromHsv(Math.Atan2(dy, dx) / (2 * Math.PI), Math.Min(1, distance / radius), 1);
                int offset = (y * size + x) * 4;
                pixels[offset] = color.B;
                pixels[offset + 1] = color.G;
                pixels[offset + 2] = color.R;
                pixels[offset + 3] = Channel(Math.Min(1, radius + .5 - distance));
            }
            var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
            bitmap.Freeze();
            return bitmap;
        }
    }
}
