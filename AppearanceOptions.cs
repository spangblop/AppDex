using System;
using System.Runtime.Serialization;
using System.Windows.Media;

namespace GameShelf
{
    [DataContract]
    internal sealed class AppearanceOptions
    {
        [DataMember] public int Layout, Columns, Texture, Sidebar;
        [DataMember] public double Width = 206, Height = 137, MaximumWidth = 295, Gap = 9;
        [DataMember] public double TileCorner = 12, ArtworkCorner = 5, ControlCorner = 7, TileOutline = 1, SideArtworkWidth = 92;
        [DataMember] public string BodyFont = "Segoe UI", HeadingFont = "Segoe UI", TileFont = "Segoe UI";
        [DataMember] public double BodySize = 13, HeadingSize = 26, TileTitleSize = 18, TileDetailSize = 13;
        [DataMember] public double SidebarWidth = 175, PinHeight = 53, PinArtwork = .63, SearchWidth = 180;
        [DataMember] public double TextureOpacity = 1, GradientAngle = 115, ArtworkOpacity = 1, CoverShade = 1;
        [DataMember(EmitDefaultValue = false)] public string BackgroundImage;
        [DataMember] public double BackgroundImageOpacity = .35;
        [DataMember] public Stretch BackgroundImageFit = Stretch.UniformToFill;
        [DataMember] public double HoverShift = 3.5, HoverGlow = 1, GlowBlur = 14, Motion = 1;
        [DataMember(EmitDefaultValue = false)] public LaunchAnimation LaunchCue;
        [DataMember] public bool Gradient, Sweep;
        [DataMember] public bool ShowPlaytime = true, ShowSource = true, ShowStats = true, ShowEdit = true, ShowPlay = true;
        [DataMember] public Stretch Crop = Stretch.UniformToFill;
        [DataMember] public string[] Colors;

        internal AppearanceOptions Copy()
        {
            var copy = (AppearanceOptions)MemberwiseClone();
            copy.Colors = (string[])Colors.Clone();
            return copy;
        }

        internal void Normalize()
        {
            Layout = (int)Within(Layout, 0, 5, 0);
            Columns = (int)Within(Columns, 0, 8, 0);
            Texture = (int)Within(Texture, 0, 3, 0);
            Sidebar = (int)Within(Sidebar, 0, 2, 0);
            Width = Within(Width, 100, 520, 206);
            Height = Within(Height, 64, 480, 137);
            MaximumWidth = Within(MaximumWidth, 100, 720, 295);
            Gap = Within(Gap, 0, 32, 9);
            TileCorner = Within(TileCorner, 0, 48, 12);
            ArtworkCorner = Within(ArtworkCorner, 0, 40, 5);
            ControlCorner = Within(ControlCorner, 0, 24, 7);
            TileOutline = Within(TileOutline, 0, 6, 1);
            SideArtworkWidth = Within(SideArtworkWidth, 42, 175, 92);
            BodySize = Within(BodySize, 10, 22, 13);
            HeadingSize = Within(HeadingSize, 16, 40, 26);
            TileTitleSize = Within(TileTitleSize, 10, 32, 18);
            TileDetailSize = Within(TileDetailSize, 8, 22, 13);
            SidebarWidth = Within(SidebarWidth, 90, 320, 175);
            PinHeight = Within(PinHeight, 32, 118, 53);
            PinArtwork = Within(PinArtwork, 0, 1, .63);
            SearchWidth = Within(SearchWidth, 100, 360, 180);
            TextureOpacity = Within(TextureOpacity, 0, 1, 1);
            GradientAngle = Within(GradientAngle, 0, 360, 115);
            ArtworkOpacity = Within(ArtworkOpacity, 0, 1, 1);
            CoverShade = Within(CoverShade, 0, 1, 1);
            BackgroundImageOpacity = Within(BackgroundImageOpacity, 0, 1, .35);
            HoverShift = Within(HoverShift, 0, 10, 3.5);
            HoverGlow = Within(HoverGlow, 0, 1, 1);
            GlowBlur = Within(GlowBlur, 0, 26, 14);
            Motion = Within(Motion, 0, 2, 1);
            if (!Enum.IsDefined(typeof(LaunchAnimation), LaunchCue)) LaunchCue = LaunchAnimation.Ripple;
            BodyFont = FontOrDefault(BodyFont);
            HeadingFont = FontOrDefault(HeadingFont);
            TileFont = FontOrDefault(TileFont);
            if (Crop != Stretch.UniformToFill && Crop != Stretch.Uniform && Crop != Stretch.Fill) Crop = Stretch.UniformToFill;
            if (BackgroundImageFit != Stretch.UniformToFill && BackgroundImageFit != Stretch.Uniform &&
                BackgroundImageFit != Stretch.Fill) BackgroundImageFit = Stretch.UniformToFill;
            Colors = AppAppearance.NormalizePalette(Colors);
        }

        private static string FontOrDefault(string value) => string.IsNullOrWhiteSpace(value) ? "Segoe UI" : value.Trim();
        private static double Within(double value, double min, double max, double fallback) =>
            value >= min && value <= max ? value : fallback;
    }

    [DataContract]
    internal sealed class AppearancePreset
    {
        internal const int NameLimit = 64;
        [DataMember] public string Name { get; set; }
        [DataMember] public AppearanceOptions Appearance;
        [DataMember] public double Scale;
        [DataMember] public string Accent;

        public override string ToString() => Name;

        internal static AppearancePreset Capture(LibraryPreferences preferences, string name = null)
        {
            return new AppearancePreset {
                Name = name, Appearance = preferences.Appearance.Copy(), Scale = preferences.Scale, Accent = preferences.Accent
            };
        }

        internal void ApplyTo(LibraryPreferences preferences)
        {
            preferences.Appearance = Appearance.Copy();
            preferences.Scale = Scale;
            preferences.Accent = Accent;
        }

        internal AppearancePreset Copy()
        {
            var copy = (AppearancePreset)MemberwiseClone();
            copy.Appearance = Appearance.Copy();
            return copy;
        }

        internal void Normalize()
        {
            Name = Name.Trim();
            Appearance.Normalize();
            if (!(Scale >= .75 && Scale <= 1.5)) Scale = 1;
            Accent = AppAppearance.Hex(AppAppearance.ReadColor(Accent));
        }
    }
}
