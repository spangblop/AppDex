using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameShelf
{
    public partial class LibraryWindow
    {
        private enum DropKind { Reorder, Group, Pin }

        private sealed class DropPosition
        {
            internal string Target, Label;
            internal DropKind Kind;
            internal bool Before;
            internal int Slot;
        }

        private sealed class TileDrag
        {
            internal string Id;
            internal Button Source;
            internal bool Ready;
            internal Point Anchor, Pointer, LastPosition;
            internal Size Size;
            internal double LastScroll = double.NaN;
            internal string[] Original, Projected;
            internal Dictionary<string, int> BaseIndices;
            internal Rect[] Slots;
            internal DropPosition Destination;
            internal DropPosition Insertion;
            internal DragAdorner Preview;
            internal AdornerLayer Layer;
            internal TimeSpan FrameTime;
        }

        private string pressedTileId;
        private Button pressedTile;
        private Point pressPoint;
        private TileDrag tileDrag;

        private void RegisterDragTarget(Button tile, string id)
        {
            tile.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                var origin = e.OriginalSource as DependencyObject;
                while (origin is Visual && origin != tile && !(origin is ButtonBase))
                    origin = VisualTreeHelper.GetParent(origin);
                if (origin is ButtonBase && origin != tile) return;
                pressedTile = tile;
                pressedTileId = id;
                pressPoint = e.GetPosition(Page);
            };
        }

        private void MoveTilePointer(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            Point pointer = e.GetPosition(Page);
            if (tileDrag == null)
            {
                if (pressedTile == null || !pressedTile.IsVisible) return;
                Vector delta = pointer - pressPoint;
                if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                if (!BeginTileDrag(pressedTile, pressedTileId, pressPoint)) return;
            }
            if (tileDrag != null) tileDrag.Pointer = pointer;
            e.Handled = true;
        }

        private bool BeginTileDrag(Button tile, string id, Point start)
        {
            if (tileDrag != null) return false;
            pressedTile = null;
            var layer = AdornerLayer.GetAdornerLayer(Page);
            if (layer == null || tile.ActualWidth <= 0 || tile.ActualHeight <= 0) return false;
            TileHover.SetDragging(tile, true);
            TileLaunchCue.Clear(tile);
            Covers.RenderTransform = Transform.Identity;
            string[] order = visibleIds.ToArray();
            var slots = visible.Select(card => new Rect(LayoutInformation.GetLayoutSlot(card).TopLeft, card.RenderSize)).ToArray();
            var size = tile.RenderSize;
            double dpi = VisualTreeHelper.GetDpi(tile).PixelsPerInchX * preferences.Scale;
            var image = new RenderTargetBitmap((int)Math.Ceiling(size.Width * dpi / 96),
                (int)Math.Ceiling(size.Height * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
            var snapshot = new DrawingVisual();
            using (DrawingContext dc = snapshot.RenderOpen())
                dc.DrawRectangle(new VisualBrush(tile) { Stretch = Stretch.Fill }, null, new Rect(size));
            image.Render(snapshot);
            image.Freeze();

            var drag = new TileDrag {
                Id = id, Source = tile, Anchor = Page.TranslatePoint(start, tile), Pointer = start, Size = size,
                Original = order, Projected = order, Slots = slots, Layer = layer,
                BaseIndices = order.Select((key, index) => new { key, index }).ToDictionary(pair => pair.key, pair => pair.index),
                Preview = new DragAdorner(Page, image, (Brush)FindResource("AppAccentText"), preferences.Appearance.TileCorner)
            };
            // Taking mouse capture can dispatch input before it returns. Reserve the preview first.
            tileDrag = drag;
            try
            {
                layer.Add(drag.Preview);
                if (!Mouse.Capture(this) || Mouse.Captured != this || tileDrag != drag) return false;
                drag.Ready = true;
                tile.Opacity = .36;
                CompositionTarget.Rendering += RenderDragFrame;
                return true;
            }
            finally
            {
                if (!drag.Ready && tileDrag == drag) EndTileDrag(false);
            }
        }

        private Rect DragViewport()
        {
            return new Rect(LibraryScroll.TranslatePoint(new Point(), Page),
                new Size(Math.Max(0, LibraryScroll.ViewportWidth), Math.Max(0, LibraryScroll.ViewportHeight)));
        }

        private Rect PinViewport()
        {
            if (!PinnedRail.IsVisible) return Rect.Empty;
            Rect zone = new Rect(PinnedRail.TranslatePoint(new Point(), Page), PinnedRail.RenderSize);
            if (zone.Width > 8 && zone.Height > 8) zone.Inflate(-4, -4);
            return zone;
        }

        private static int FindSlot(Rect[] slots, Point point)
        {
            int found = 0;
            double smallest = double.MaxValue;
            for (int i = 0; i < slots.Length; i++)
            {
                Rect rect = slots[i];
                if (rect.Contains(point)) return i;
                double dx = Math.Max(0, Math.Max(rect.Left - point.X, point.X - rect.Right));
                double dy = Math.Max(0, Math.Max(rect.Top - point.Y, point.Y - rect.Bottom));
                double distance = dx * dx + dy * dy * 3;
                if (distance >= smallest) continue;
                found = i;
                smallest = distance;
            }
            return found;
        }

        private DropPosition FindDestination(TileDrag drag, Point pointer, Rect viewport)
        {
            if (drag.Source.DataContext is Game && PinViewport().Contains(pointer))
                return new DropPosition { Kind = DropKind.Pin, Label = preferences.Pins.Contains(drag.Id) ? "Already pinned" : "Pin to sidebar" };
            if (preferences.Sort != 2 || !viewport.Contains(pointer) || drag.Slots.Length < 2) return null;
            Point point = Page.TranslatePoint(pointer, Covers);
            int index = FindSlot(drag.Slots, point);
            string target = drag.Projected[index];
            if (target == drag.Id) return drag.Insertion;
            Rect card = drag.Slots[index];
            double x = (point.X - card.Left) / Math.Max(1, card.Width);
            double y = (point.Y - card.Top) / Math.Max(1, card.Height);
            bool side = x < .23 || x > .77;
            bool end = y < .23 || y > .77;
            string name = TileName(tiles[target]);
            if (!side && !end && activeGroupId == null && drag.Source.DataContext is Game)
            {
                return new DropPosition { Kind = DropKind.Group, Target = target, Slot = index,
                    Label = tiles[target].DataContext is AppGroup ? "Add to " + name : "Group with " + name };
            }
            bool singleColumn = drag.Slots.Length < 2 || drag.Slots[0].Top != drag.Slots[1].Top;
            bool before = singleColumn || end && !side ? y < .5 : x < .5;
            return new DropPosition { Kind = DropKind.Reorder, Target = target, Before = before,
                Label = (before ? "Before " : "After ") + name };
        }

        private static string TileName(Button tile)
        {
            var app = tile.DataContext as Game;
            return app == null ? ((AppGroup)tile.DataContext).Name : app.Name;
        }

        private void UpdateTileDrag(Point pointer)
        {
            TileDrag drag = tileDrag;
            if (drag == null || !drag.Ready) return;
            if (drag.LastPosition == pointer && Math.Abs(drag.LastScroll - LibraryScroll.VerticalOffset) < .1) return;
            drag.LastPosition = pointer;
            drag.LastScroll = LibraryScroll.VerticalOffset;
            Rect viewport = DragViewport();
            DropPosition target = FindDestination(drag, pointer, viewport);
            if (target != null && target.Kind == DropKind.Reorder && drag.Insertion != null &&
                target.Target == drag.Insertion.Target && target.Before == drag.Insertion.Before)
                target = drag.Insertion;
            drag.Destination = target;
            if (target != null && target.Kind == DropKind.Reorder && !ReferenceEquals(drag.Insertion, target))
            {
                var order = drag.Original.Where(id => id != drag.Id).ToList();
                int index = order.IndexOf(target.Target) + (target.Before ? 0 : 1);
                order.Insert(index, drag.Id);
                target.Slot = index;
                drag.Insertion = target;
                ProjectOrder(drag, order.ToArray(), SameCoordinateViewport(viewport));
            }
            else if ((target == null || target.Kind == DropKind.Pin) && drag.Insertion != null)
            {
                drag.Insertion = null;
                ProjectOrder(drag, drag.Original, SameCoordinateViewport(viewport));
            }

            Rect guide = Rect.Empty;
            Rect clip = viewport;
            bool emphasize = false;
            drag.Source.Opacity = drag.Insertion == null ? .36 : 0;
            if (target != null)
            {
                if (target.Kind == DropKind.Pin)
                {
                    clip = new Rect(new Point(), Page.RenderSize);
                    guide = PinViewport();
                    emphasize = true;
                }
                else
                {
                    Rect slot = drag.Slots[target.Slot];
                    guide = new Rect(Covers.TranslatePoint(slot.TopLeft, Page), slot.Size);
                    if (target.Kind == DropKind.Group) { emphasize = true; guide.Inflate(3, 3); }
                }
            }
            Point origin = pointer - (Vector)drag.Anchor;
            origin.X = Math.Max(1, Math.Min(origin.X, Page.ActualWidth - drag.Size.Width - 1));
            origin.Y = Math.Max(1, Math.Min(origin.Y, Page.ActualHeight - drag.Size.Height - 1));
            drag.Preview.Update(new Rect(origin, drag.Size), clip, guide, emphasize, target == null ? null : target.Label);
        }

        private Rect SameCoordinateViewport(Rect viewport)
        {
            return new Rect(Page.TranslatePoint(viewport.TopLeft, Covers), viewport.Size);
        }

        private void ProjectOrder(TileDrag drag, string[] order, Rect viewport)
        {
            if (drag.Projected.SequenceEqual(order)) return;
            for (int i = 0; i < order.Length; i++)
            {
                string id = order[i];
                if (drag.Projected[i] == id || id == drag.Id) continue;
                int original = drag.BaseIndices[id];
                var tile = tiles[id];
                var translate = (TranslateTransform)tile.RenderTransform;
                Vector offset = drag.Slots[i].TopLeft - drag.Slots[original].TopLeft;
                var from = drag.Slots[original];
                from.Offset(translate.X, translate.Y);
                int milliseconds = viewport.IntersectsWith(from) || viewport.IntersectsWith(drag.Slots[i]) ? 150 : 0;
                MoveTile(translate, offset, milliseconds);
            }
            drag.Projected = order;
        }

        private void MoveTile(TranslateTransform transform, Vector offset, int milliseconds)
        {
            if (milliseconds <= 0)
            {
                transform.BeginAnimation(TranslateTransform.XProperty, null);
                transform.BeginAnimation(TranslateTransform.YProperty, null);
                transform.X = offset.X;
                transform.Y = offset.Y;
                return;
            }
            AnimateOffset(transform, TranslateTransform.XProperty, transform.X, offset.X, milliseconds);
            AnimateOffset(transform, TranslateTransform.YProperty, transform.Y, offset.Y, milliseconds);
        }

        private void RenderDragFrame(object sender, EventArgs e)
        {
            TileDrag drag = tileDrag;
            if (drag == null || !drag.Ready) return;
            if (Mouse.Captured != this || Mouse.LeftButton != MouseButtonState.Pressed)
            {
                EndTileDrag(false);
                return;
            }
            TimeSpan time = ((RenderingEventArgs)e).RenderingTime;
            double elapsed = drag.FrameTime == TimeSpan.Zero ? 16 : Math.Min(40, (time - drag.FrameTime).TotalMilliseconds);
            drag.FrameTime = time;
            Point pointer = drag.Pointer;
            Rect viewport = DragViewport();
            if (viewport.Contains(pointer))
            {
                double top = pointer.Y - viewport.Top, bottom = viewport.Bottom - pointer.Y;
                if (top < 35) LibraryScroll.ScrollToVerticalOffset(LibraryScroll.VerticalOffset - (35 - top) * elapsed * .021);
                else if (bottom < 35) LibraryScroll.ScrollToVerticalOffset(LibraryScroll.VerticalOffset + (35 - bottom) * elapsed * .021);
            }
            UpdateTileDrag(pointer);
        }

        private void ReleaseTilePointer(object sender, MouseButtonEventArgs e)
        {
            pressedTile = null;
            if (tileDrag == null) return;
            e.Handled = true;
            if (tileDrag.Ready) UpdateTileDrag(e.GetPosition(Page));
            EndTileDrag(tileDrag != null && tileDrag.Ready);
        }

        private void TileCaptureLost(object sender, MouseEventArgs e)
        {
            if (e.OriginalSource == this && Mouse.Captured != this) EndTileDrag(false);
        }

        private void EndTileDrag(bool drop)
        {
            pressedTile = null;
            TileDrag drag = tileDrag;
            if (drag == null) return;
            tileDrag = null;
            CompositionTarget.Rendering -= RenderDragFrame;
            drag.Layer.Remove(drag.Preview);
            if (Mouse.Captured == this) ReleaseMouseCapture();
            drag.Source.Opacity = 1;
            TileHover.SetDragging(drag.Source, false);

            DropPosition target = drop ? drag.Destination : null;
            if (target == null)
            {
                ProjectOrder(drag, drag.Original, SameCoordinateViewport(DragViewport()));
                return;
            }
            if (target.Kind == DropKind.Pin)
            {
                PinFromDrag(drag.Id);
                return;
            }

            if (target.Kind == DropKind.Reorder)
            {
                int original = drag.BaseIndices[drag.Id];
                if (target.Slot == original) return;
                preferences.Move(drag.Id, target.Target, false, target.Before);
                PlaceReorderedTile(drag, original, target.Slot);
            }
            else
            {
                preferences.Move(drag.Id, target.Target, true, target.Before);
                RenderLibrary();
            }
            QueuePreferences();
            PlayUiSound(UiCue.Drop);
        }

        private void PlaceReorderedTile(TileDrag drag, int from, int to)
        {
            // The preview already moved the neighbors. Keep their visuals and place the dragged card in the gap.
            Rect viewport = SameCoordinateViewport(DragViewport());
            for (int i = Math.Min(from, to); i <= Math.Max(from, to); i++)
            {
                string id = drag.Projected[i];
                var translate = (TranslateTransform)tiles[id].RenderTransform;
                int previous = drag.BaseIndices[id];
                Vector offset = drag.Slots[previous].TopLeft + new Vector(translate.X, translate.Y) - drag.Slots[i].TopLeft;
                MoveTile(translate, new Vector(), 0);
                if (id == drag.Id || !viewport.IntersectsWith(drag.Slots[i]) || offset.Length < 1) continue;
                AnimateOffset(translate, TranslateTransform.XProperty, offset.X, 0, 85);
                AnimateOffset(translate, TranslateTransform.YProperty, offset.Y, 0, 85);
            }
            Covers.Children.RemoveAt(from);
            Covers.Children.Insert(to, drag.Source);
            visible.RemoveAt(from);
            visible.Insert(to, drag.Source);
            visibleIds.RemoveAt(from);
            visibleIds.Insert(to, drag.Id);
        }

        private void PinFromDrag(string id)
        {
            if (preferences.Pins.Contains(id)) return;
            var apps = Games.ToDictionary(app => app.Id);
            if (!apps.ContainsKey(id)) return;
            preferences.Pins.Add(id);
            RenderPins(apps);
            QueuePreferences();
            Announce("Pinned " + apps[id].Name + ".");
            PlayUiSound(UiCue.Drop);
        }

        private sealed class DragAdorner : Adorner
        {
            private readonly ImageSource image;
            private readonly Pen outline;
            private readonly Brush accent;
            private readonly double radius;
            private Rect clip, guide;
            private bool grouping;
            private string label;
            internal Rect Avatar { get; private set; }

            internal DragAdorner(UIElement element, ImageSource image, Brush accent, double radius) : base(element)
            {
                this.image = image;
                this.accent = accent;
                this.radius = radius;
                outline = new Pen(accent, 2);
                IsHitTestVisible = false;
            }

            internal void Update(Rect avatar, Rect viewport, Rect marker, bool group, string text)
            {
                if (Avatar == avatar && clip == viewport && guide == marker && grouping == group && label == text) return;
                Avatar = avatar;
                clip = viewport;
                guide = marker;
                grouping = group;
                label = text;
                InvalidateVisual();
            }

            protected override void OnRender(DrawingContext dc)
            {
                if (Avatar.Width <= 0 || Avatar.Height <= 0) return;
                if (!guide.IsEmpty && !clip.IsEmpty)
                {
                    dc.PushClip(new RectangleGeometry(clip));
                    if (grouping)
                    {
                        dc.PushOpacity(0.23);
                        dc.DrawRoundedRectangle(accent, null, guide, radius, radius);
                        dc.Pop();
                    }
                    dc.DrawRoundedRectangle(null, outline, guide, radius, radius);
                    dc.Pop();
                }
                var shadow = Avatar;
                shadow.Offset(0, 4);
                dc.PushOpacity(0.65);
                dc.DrawRoundedRectangle(Brushes.Black, null, shadow, radius, radius);
                dc.Pop();
                dc.PushOpacity(0.93);
                dc.DrawImage(image, Avatar);
                dc.Pop();
                dc.DrawRoundedRectangle(null, outline, Avatar, radius, radius);
                if (label == null) return;
                var text = new FormattedText(label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI Semibold"), 13, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip)
                    { MaxTextWidth = Math.Max(70, Math.Min(240, RenderSize.Width - 24)), Trimming = TextTrimming.CharacterEllipsis };
                double width = text.Width + 18;
                double left = Math.Max(0, Math.Min(Avatar.X, RenderSize.Width - width));
                double top = Avatar.Top > text.Height + 12 ? Avatar.Top - text.Height - 13 : Avatar.Bottom + 5;
                var badge = new Rect(left, top, width, text.Height + 8);
                dc.DrawRoundedRectangle(Brushes.Black, outline, badge, 5, 5);
                dc.DrawText(text, new Point(left + 9, top + 4));
            }
        }
    }
}
