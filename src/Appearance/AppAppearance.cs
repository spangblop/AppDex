using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace GameShelf
{
    internal sealed class AppStyle
    {
        internal readonly string Name, Card, DisplayFont;
        internal readonly double ColumnWidth, TileHeight, MaxTileWidth;
        internal readonly string[] Palette;

        internal AppStyle(string name, string card, double columnWidth, double tileHeight, double maxTileWidth,
            string displayFont, params string[] palette)
        {
            Name = name;
            Card = card;
            ColumnWidth = columnWidth;
            TileHeight = tileHeight;
            MaxTileWidth = maxTileWidth;
            DisplayFont = displayFont;
            Palette = palette;
        }
    }

    internal static class AppAppearance
    {
        private static string wallpaperPath;
        private static ImageSource wallpaper;
        internal const string DefaultAccent = "#5094FF";
        internal const string DefaultHeadingFont = "Arial";
        private const string SerifHeading = "Baskervville, Georgia";
        private static readonly AppStyle[] LegacyLooks =
        {
            new AppStyle("Classic", "CoverButton", 206, 137, 295, DefaultHeadingFont,
                "#111414", "#1C211F", "#29332E", "#526057", "#C1CBC2", "#F4F6F0", "#FFAD9D"),
            new AppStyle("Gallery", "AlternateCoverButton", 206, 163, 295, SerifHeading,
                "#141724", "#202638", "#2A3349", "#4D5C78", "#B6C3DF", "#F4F6F0", "#FFAD9D"),
            new AppStyle("Compact", "AlternateCoverButton", 272, 70, 490, SerifHeading,
                "#1B1918", "#272321", "#342E2A", "#665951", "#C9BDB4", "#F4F6F0", "#FFAD9D"),
            new AppStyle("Arcade", "ArcadeCoverButton", 225, 155, 310, "Bahnschrift, Segoe UI",
                "#110F22", "#19172C", "#25203B", "#56507E", "#BDB3D8", "#FAF6FF", "#FF9DAA"),
            new AppStyle("Marquee", "MarqueeCoverButton", 178, 232, 235, SerifHeading,
                "#EFE4D0", "#FCF4E7", "#EEE1C8", "#AE9677", "#594A3B", "#241B17", "#922A30"),
            new AppStyle("Flight Deck", "FlightDeckCoverButton", 315, 94, 465, "Cascadia Code, Consolas",
                "#0D1B24", "#142833", "#1A3641", "#3B6571", "#A8C6C9", "#EAF9F5", "#FFA298")
        };

        internal static readonly string[] LayoutNames = { "Full cover", "Artwork above", "Artwork beside", "Cabinet", "Framed print", "Detail rail" };
        internal static readonly string[] PresetNames = { "Style 1", "Style 2", "Style 3", "Style 4", "People that like being blinded", "Style 6" };
        internal static readonly string[] ColorNames = { "Background", "Panels", "Controls", "Borders", "Secondary text",
            "Primary text", "Warnings", "Gradient end", "Tile surface", "Text on artwork" };
        private static readonly string[] ColorKeys = { "AppBackground", "AppPanel", "AppControl", "AppEdge", "AppMuted",
            "AppText", "AppDanger", "AppGradientEnd", "AppTileSurface", "AppCoverInk" };

        internal static string Card(int layout) => LegacyLooks[layout].Card;
        internal static string LayoutTag(int layout) => LegacyLooks[layout].Name;

        internal static AppearancePreset BuiltInPreset(int index)
        {
            return new AppearancePreset {
                Name = PresetNames[index], Appearance = Defaults(index), Scale = 1, Accent = DefaultAccent
            };
        }

        internal static AppearanceOptions Defaults(int previousStyle = 0)
        {
            int index = previousStyle >= 0 && previousStyle < LegacyLooks.Length ? previousStyle : 0;
            var look = LegacyLooks[index];
            string[] colors = new string[ColorNames.Length];
            Array.Copy(look.Palette, colors, look.Palette.Length);
            colors[7] = index == 3 ? "#241529" : index == 4 ? "#F9F0E1" : index == 5 ? "#172D35" : "#29332E";
            colors[8] = index == 3 ? "#211A38" : index == 0 ? "#252B2B" : colors[1];
            colors[9] = "#FFFFFF";
            return new AppearanceOptions
            {
                Layout = index, Width = look.ColumnWidth, Height = look.TileHeight, MaximumWidth = look.MaxTileWidth,
                HeadingFont = look.DisplayFont, TileFont = index >= 3 ? look.DisplayFont : "Segoe UI",
                TileCorner = index == 0 ? 12 : index < 3 ? 10 : index == 3 ? 2 : 3,
                TileTitleSize = index == 0 ? 18 : 16, TileDetailSize = index < 3 ? 13 : 11,
                SideArtworkWidth = index == 2 ? 83 : 104,
                Gradient = index >= 3, Texture = Math.Max(0, index - 2), Sweep = index == 3,
                Colors = colors
            };
        }

        internal static string[] NormalizePalette(string[] colors)
        {
            string[] normalized = Defaults().Colors;
            if (colors == null) return normalized;
            for (int i = 0; i < Math.Min(colors.Length, normalized.Length); i++)
                if (TryReadColor(colors[i], out Color color)) normalized[i] = Hex(color);
            return normalized;
        }

        internal static void Apply(LibraryPreferences prefs)
        {
            var resources = Application.Current.Resources;
            if (resources.MergedDictionaries.Count == 0)
                resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/AppDex;component/src/Views/Controls/Controls.xaml") });
            var look = prefs.Appearance;
            resources["AppBodyFont"] = new FontFamily(look.BodyFont);
            resources["AppDisplayFont"] = new FontFamily(look.HeadingFont);
            resources["AppTileFont"] = new FontFamily(look.TileFont);
            resources["AppBodySize"] = look.BodySize;
            resources["AppHeadingSize"] = look.HeadingSize;
            resources["AppTileTitleSize"] = look.TileTitleSize;
            resources["AppTileDetailSize"] = look.TileDetailSize;
            resources["AppTileCorner"] = new CornerRadius(look.TileCorner);
            resources["AppGlowCorner"] = new CornerRadius(look.TileCorner == 0 ? 0 : look.TileCorner + 3);
            resources["AppArtworkCorner"] = new CornerRadius(look.ArtworkCorner);
            resources["AppControlCorner"] = new CornerRadius(look.ControlCorner);
            resources["AppTileOutline"] = new Thickness(look.TileOutline);
            resources["AppSideArtworkMax"] = look.SideArtworkWidth;
            resources["AppCrop"] = look.Crop;
            resources["AppArtworkOpacity"] = look.ArtworkOpacity;
            resources["AppCoverShade"] = look.CoverShade;
            resources["AppGlowBlur"] = look.GlowBlur;
            resources["AppSpinnerAnimation"] = look.Motion > 0;
            resources["AppSourceVisibility"] = Visible(look.ShowSource);
            resources["AppStatsVisibility"] = Visible(look.ShowStats);
            resources["AppEditVisibility"] = Visible(look.ShowEdit);
            resources["AppPlayVisibility"] = Visible(look.ShowPlay);
            resources["AppPlaytimeVisibility"] = Visible(look.ShowPlaytime);
            resources["AppTexture"] = Texture(look.Texture, look.TextureOpacity);
            resources["AppWallpaper"] = Wallpaper(look);
            var scale = new ScaleTransform(prefs.Scale, prefs.Scale);
            scale.Freeze();
            resources["AppScale"] = scale;
            ApplyColors(prefs);
        }

        private static Visibility Visible(bool show) => show ? Visibility.Visible : Visibility.Collapsed;

        internal static Color ReadColor(string value)
        {
            Color color;
            return TryReadColor(value, out color) ? color : Color.FromRgb(199, 248, 118);
        }

        internal static bool TryReadColor(string value, out Color color)
        {
            uint hex;
            value = (value ?? "").Trim();
            if (value.StartsWith("#", StringComparison.Ordinal)) value = value.Substring(1);
            if (value.Length == 6 && uint.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out hex))
            {
                color = Color.FromRgb((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex);
                return true;
            }
            color = default(Color);
            return false;
        }

        internal static string Hex(Color color) { return color.ToString().Remove(1, 2); }

        internal static void ApplyColors(LibraryPreferences prefs)
        {
            var resources = Application.Current.Resources;
            var look = prefs.Appearance;
            for (int i = 0; i < ColorKeys.Length; i++) resources[ColorKeys[i]] = Brush(ReadColor(look.Colors[i]));
            Color panel = ReadColor(look.Colors[1]);
            Color canvas = ReadColor(look.Colors[0]);
            resources["AppCanvas"] = Canvas(look, canvas);
            Color accent = ReadColor(prefs.Accent);
            resources["AppAccent"] = Brush(accent);
            resources["AppAccentInk"] = Contrast(accent, Colors.Black) >= Contrast(accent, Colors.White) ? Brushes.Black : Brushes.White;
            Color readable = ReadableAccent(accent, panel, canvas);
            resources["AppAccentText"] = Brush(readable);
            resources["AppHoverGlow"] = readable;
            var dark = Color.FromRgb(24, 26, 40);
            resources["AppAccentOnDark"] = Brush(ReadableAccent(accent, dark, dark));
        }

        internal static bool DarkTitleBar(LibraryPreferences prefs) =>
            Contrast(ReadColor(prefs.Appearance.Colors[1]), Colors.White) >= Contrast(ReadColor(prefs.Appearance.Colors[1]), Colors.Black);

        private static Color ReadableAccent(Color accent, Color a, Color b)
        {
            Color endpoint = Contrast(a, Colors.White) >= Contrast(a, Colors.Black) ? Colors.White : Colors.Black;
            for (int step = 0; step <= 12; step++)
            {
                double amount = step / 12.0;
                Color candidate = Color.FromRgb(Blend(accent.R, endpoint.R, amount),
                    Blend(accent.G, endpoint.G, amount), Blend(accent.B, endpoint.B, amount));
                if (Contrast(candidate, a) >= 4.5 && Contrast(candidate, b) >= 4.5) return candidate;
            }
            return endpoint;
        }

        private static byte Blend(byte from, byte to, double amount) { return (byte)Math.Round(from + (to - from) * amount); }

        private static double Contrast(Color a, Color b)
        {
            double first = a.ScR * .2126 + a.ScG * .7152 + a.ScB * .0722;
            double second = b.ScR * .2126 + b.ScG * .7152 + b.ScB * .0722;
            return (Math.Max(first, second) + .05) / (Math.Min(first, second) + .05);
        }

        private static Brush Canvas(AppearanceOptions look, Color color)
        {
            if (!look.Gradient) return Brush(color);
            var brush = new LinearGradientBrush(color, ReadColor(look.Colors[7]), look.GradientAngle);
            brush.Freeze();
            return brush;
        }

        internal static void LoadWallpaper(string path)
        {
            ImageSource loaded = GameCatalog.ReadSizedPicture(path, 1920);
            wallpaper = loaded;
            wallpaperPath = path;
        }

        private static Brush Wallpaper(AppearanceOptions look)
        {
            if (string.IsNullOrWhiteSpace(look.BackgroundImage) || look.BackgroundImageOpacity == 0) return Brushes.Transparent;
            try
            {
                if (wallpaperPath != look.BackgroundImage) LoadWallpaper(look.BackgroundImage);
                var brush = new ImageBrush(wallpaper) { Stretch = look.BackgroundImageFit, Opacity = look.BackgroundImageOpacity };
                brush.Freeze();
                return brush;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
                error is ArgumentException || error is NotSupportedException || error is FileFormatException)
            {
                return Brushes.Transparent;
            }
        }

        private static Brush Texture(int texture, double opacity)
        {
            if (texture == 0 || opacity == 0) return Brushes.Transparent;
            int size = texture == 1 ? 32 : texture == 2 ? 8 : 24;
            var drawing = new DrawingGroup();
            using (DrawingContext dc = drawing.Open())
            {
                dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, size, size));
                if (texture == 2)
                {
                    dc.DrawEllipse(Brush(Color.FromArgb(25, 112, 72, 39)), null, new Point(1.5, 1.5), .52, .52);
                    dc.DrawEllipse(Brush(Color.FromArgb(32, 255, 255, 255)), null, new Point(5, 6), .65, .65);
                }
                else
                {
                    Color line = texture == 1 ? Color.FromArgb(35, 172, 119, 233) : Color.FromArgb(38, 123, 183, 201);
                    var pen = new Pen(Brush(line), 1);
                    dc.DrawLine(pen, new Point(.5, 0), new Point(.5, size));
                    dc.DrawLine(pen, new Point(0, .5), new Point(size, .5));
                }
            }
            var brush = new DrawingBrush(drawing) {
                Viewport = new Rect(0, 0, size, size), ViewportUnits = BrushMappingMode.Absolute, TileMode = TileMode.Tile, Opacity = opacity
            };
            brush.Freeze();
            return brush;
        }

        private static SolidColorBrush Brush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
