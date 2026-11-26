using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace GameShelf
{
    public partial class LibraryWindow
    {
        private AppearanceTarget highlightTarget;

        private void HighlightAppearance(AppearanceTarget target)
        {
            if (highlightTarget == target) return;
            if (highlightTarget != AppearanceTarget.None) Page.LayoutUpdated -= UpdateAppearanceHighlight;
            highlightTarget = target;
            if (target != AppearanceTarget.None) Page.LayoutUpdated += UpdateAppearanceHighlight;
            UpdateAppearanceHighlight(null, EventArgs.Empty);
        }

        private void UpdateAppearanceHighlight(object sender, EventArgs e)
        {
            var settings = DrawerContent.Content as SettingsPane;
            if (highlightTarget == AppearanceTarget.None || settings == null || !PanelLayer.IsVisible || QuitLayer.IsVisible)
            {
                LiveHighlight.Show(Array.Empty<HighlightBox>(), false);
                settings?.ShowAppearanceSample(null, HighlightPart.Frame, 0);
                return;
            }

            var boxes = new List<HighlightBox>();
            FrameworkElement example = null;
            HighlightPart examplePart = HighlightPart.Frame;
            double exampleRadius = 0;
            var look = preferences.Appearance;
            Rect clip = DragViewport();
            void Add(FrameworkElement element, HighlightPart part, double radius = 4, bool inLibrary = false)
            {
                if (element == null || !element.IsVisible || element.ActualWidth < 4 || element.ActualHeight < 4) return;
                var bounds = new Rect(element.TranslatePoint(new Point(), Page), element.RenderSize);
                if (inLibrary && !bounds.IntersectsWith(clip)) return;
                if (example == null)
                {
                    example = element;
                    examplePart = part;
                    exampleRadius = radius;
                }
                boxes.Add(new HighlightBox(bounds, part, radius, inLibrary ? clip : Rect.Empty));
            }

            void Controls(HighlightPart part)
            {
                foreach (var control in new FrameworkElement[] { SearchField, SortPicker, SourcePicker, PreviewSortButton,
                    PreviewSourceButton, ImportButton, AddGameButton, SettingsButton, FullscreenButton, LibraryHome })
                    Add(control, part, look.ControlCorner);
            }

            switch (highlightTarget)
            {
                case AppearanceTarget.Headings:
                    Add(AppTitle, HighlightPart.Frame);
                    Add(ViewTitle, HighlightPart.Frame);
                    break;
                case AppearanceTarget.InterfaceText:
                    Controls(HighlightPart.Frame);
                    Add(Status, HighlightPart.Frame);
                    foreach (var pin in Pinned.Children.OfType<Button>()) Add(PinLabel(pin), HighlightPart.Frame);
                    break;
                case AppearanceTarget.Background: Add(Body, HighlightPart.Area, 0); break;
                case AppearanceTarget.Scale: Add(Shell, HighlightPart.Frame, 0); break;
                case AppearanceTarget.Panels:
                    Add(HeaderBar, HighlightPart.Area, 0);
                    Add(PinnedRail, HighlightPart.Area, 0);
                    break;
                case AppearanceTarget.Controls: Controls(HighlightPart.Area); break;
                case AppearanceTarget.ControlCorners: Controls(HighlightPart.Corners); break;
                case AppearanceTarget.MutedText:
                    Add(Status, HighlightPart.Frame);
                    Add(CountText, HighlightPart.Frame);
                    Add(TotalPlaytimeLabel, HighlightPart.Frame);
                    foreach (var tile in visible) Add(TilePart(tile, "TileDetails"), HighlightPart.Frame, 3, true);
                    break;
                case AppearanceTarget.Accent:
                    Add(AddGameButton, HighlightPart.Area, look.ControlCorner);
                    Add(TotalPlaytime, HighlightPart.Frame);
                    break;
                case AppearanceTarget.Warnings:
                    // Warning colors are also shown in the palette swatch when no warning is on screen.
                    break;
                case AppearanceTarget.SearchWidth: Add(SearchField, HighlightPart.Width); break;
                case AppearanceTarget.SidebarWidth: Add(PinnedRail, HighlightPart.Width, 0); break;
                case AppearanceTarget.PinHeight:
                    foreach (var pin in Pinned.Children.OfType<Button>()) Add(pin, HighlightPart.Height, look.ControlCorner);
                    break;
                case AppearanceTarget.PinArtwork:
                    foreach (var pin in Pinned.Children.OfType<Button>()) Add(PinArtwork(pin), HighlightPart.Area, look.ControlCorner);
                    break;
                default:
                    foreach (var tile in visible)
                    {
                        switch (highlightTarget)
                        {
                            case AppearanceTarget.TileWidth: Add(tile, HighlightPart.Width, 0, true); break;
                            case AppearanceTarget.TileHeight: Add(tile, HighlightPart.Height, 0, true); break;
                            case AppearanceTarget.TileGap:
                                Add(tile, HighlightPart.Frame, look.TileCorner, true);
                                AddTileGaps(boxes, tile, clip, look.Gap);
                                break;
                            case AppearanceTarget.TileCorners: Add(tile, HighlightPart.Corners, look.TileCorner, true); break;
                            case AppearanceTarget.Artwork: Add(TilePart(tile, "ArtworkSurface"), HighlightPart.Area, look.ArtworkCorner, true); break;
                            case AppearanceTarget.ArtworkCorners:
                                if (look.Layout != 0) Add(TilePart(tile, "ArtworkSurface"), HighlightPart.Corners, look.ArtworkCorner, true);
                                break;
                            case AppearanceTarget.SideArtwork:
                                if (look.Layout == 2 || look.Layout == 5) Add(TilePart(tile, "ArtworkSurface"), HighlightPart.Width, 0, true);
                                break;
                            case AppearanceTarget.Caption: Add(TilePart(tile, "Caption"), HighlightPart.Area, 3, true); break;
                            case AppearanceTarget.TileTitles: Add(TilePart(tile, "TileTitle"), HighlightPart.Frame, 3, true); break;
                            case AppearanceTarget.TileDetails: Add(TilePart(tile, "TileDetails"), HighlightPart.Frame, 3, true); break;
                            case AppearanceTarget.ArtworkText:
                                Add(TilePart(tile, "TileTitle"), HighlightPart.Frame, 3, true);
                                Add(TilePart(tile, "TileDetails"), HighlightPart.Frame, 3, true);
                                break;
                            case AppearanceTarget.HoverGlow: Add(tile, HighlightPart.Halo, look.TileCorner, true); break;
                            default: Add(tile, HighlightPart.Frame, look.TileCorner, true); break;
                        }
                    }
                    break;
            }

            var exposed = new Rect(0, 0, Math.Max(0, Drawer.TranslatePoint(new Point(), Page).X), Page.ActualHeight);
            bool hasExposedTarget = boxes.Any(box =>
            {
                Rect bounds = box.Clip.IsEmpty ? box.Bounds : Rect.Intersect(box.Bounds, box.Clip);
                Rect visiblePortion = Rect.Intersect(bounds, exposed);
                return !visiblePortion.IsEmpty && visiblePortion.Width >= Math.Min(bounds.Width, 16);
            });
            LiveHighlight.Show(hasExposedTarget ? boxes.ToArray() : Array.Empty<HighlightBox>(), look.Motion > 0);
            settings.ShowAppearanceSample(hasExposedTarget ? null : example, examplePart, exampleRadius);
        }

        private void AddTileGaps(List<HighlightBox> boxes, Button tile, Rect clip, double gap)
        {
            if (!tile.IsVisible || tile.ActualWidth <= 10 || tile.ActualHeight <= 10) return;
            var bounds = new Rect(tile.TranslatePoint(new Point(), Page), tile.RenderSize);
            if (!bounds.IntersectsWith(clip)) return;
            double space = Math.Max(2, gap);
            boxes.Add(new HighlightBox(new Rect(bounds.Right, bounds.Top + 5, space, bounds.Height - 10), HighlightPart.Gap, 0, clip));
            boxes.Add(new HighlightBox(new Rect(bounds.Left + 5, bounds.Bottom, bounds.Width - 10, space), HighlightPart.Gap, 0, clip));
        }

        private static FrameworkElement TilePart(Button tile, string part) => tile.Template?.FindName(part, tile) as FrameworkElement;

        private static Grid PinFace(Button pin) => (pin.Content as Grid)?.Children.OfType<Grid>().FirstOrDefault();

        private static FrameworkElement PinArtwork(Button pin) => PinFace(pin)?.Children.OfType<Image>().FirstOrDefault();

        private static FrameworkElement PinLabel(Button pin) =>
            PinFace(pin)?.Children.OfType<DockPanel>().FirstOrDefault()?.Children.OfType<TextBlock>().FirstOrDefault();
    }
}
