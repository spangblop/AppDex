using System;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GameShelf
{
    public sealed class LibraryScrollViewer : ScrollViewer
    {
        private bool scrolling;
        private double target;

        protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
        {
            base.OnPreviewMouseWheel(e);
            if (ScrollableHeight <= 0) return;
            target = Math.Max(0, Math.Min(ScrollableHeight, (scrolling ? target : VerticalOffset) - e.Delta * 0.73));
            if (!scrolling)
            {
                scrolling = true;
                CompositionTarget.Rendering += Animate;
                Unloaded += StopOnUnload;
            }
            e.Handled = true;
        }

        protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            Stop();
            base.OnPreviewMouseDown(e);
        }

        private void Animate(object sender, EventArgs e)
        {
            target = Math.Min(target, ScrollableHeight);
            double difference = target - VerticalOffset;
            if (Math.Abs(difference) < 1)
            {
                ScrollToVerticalOffset(target);
                Stop();
            }
            else ScrollToVerticalOffset(VerticalOffset + difference * 0.24);
        }

        private void StopOnUnload(object sender, System.Windows.RoutedEventArgs e) { Stop(); }

        private void Stop()
        {
            if (!scrolling) return;
            scrolling = false;
            CompositionTarget.Rendering -= Animate;
            Unloaded -= StopOnUnload;
        }
    }
}
