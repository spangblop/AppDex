using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace GameShelf
{
    internal sealed class AppearanceEditor : DockPanel
    {
        private static readonly string[] Sections = { "Presets", "Tiles & artwork", "Typography", "Colors", "Library layout", "Motion & effects" };
        private static readonly string[] FontsOnPc = Fonts.SystemFontFamilies.Select(font => font.Source)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToArray();

        private readonly LibraryPreferences preferences;
        private readonly string dataDirectory;
        private readonly Action apply, save;
        private readonly DispatcherTimer delay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
        private readonly StackPanel fields = new StackPanel();
        private readonly ComboBox category;
        private readonly Button undo;
        private ComboBox builtInPicker;
        private AppearancePreset previousAppearance;
        private FrameworkElement highlightedRow;
        private readonly VisualBrush sampleBrush = new VisualBrush { Stretch = Stretch.Uniform };
        private readonly Grid sample = new Grid { Width = 86, Height = 42, Visibility = Visibility.Hidden, IsHitTestVisible = false };
        private readonly AppearanceHighlight sampleHighlight = new AppearanceHighlight();
        private bool dirty;
        internal event Action<AppearanceTarget> HighlightChanged;

        internal AppearanceEditor(LibraryPreferences preferences, Action apply, Action save, string dataDirectory)
        {
            this.preferences = preferences;
            this.dataDirectory = dataDirectory;
            this.apply = apply;
            this.save = save;
            undo = ActionButton("Undo", delegate
            {
                if (previousAppearance == null) return;
                var previous = previousAppearance;
                previousAppearance = null;
                undo.IsEnabled = false;
                ApplyAppearance(previous);
            });
            undo.ToolTip = "Restore the appearance before the last preset or reset.";
            undo.IsEnabled = false;
            var actions = new WrapPanel { Children = {
                ActionButton("Reset appearance", () => StartFrom(AppAppearance.BuiltInPreset(0))), undo
            } };
            sample.Children.Add(new Border { Background = sampleBrush, Margin = new Thickness(4) });
            sample.Children.Add(sampleHighlight);
            SetDock(sample, Dock.Right);
            var footer = new DockPanel { Margin = new Thickness(0, 8, 0, 0), Children = { sample, actions } };
            SetDock(footer, Dock.Bottom);
            Children.Add(footer);

            category = Picker("Appearance category", Sections, 0);
            category.Margin = new Thickness(0, 0, 0, 9);
            SetDock(category, Dock.Top);
            Children.Add(category);
            Children.Add(new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            category.SelectionChanged += delegate { LoadSection(); };
            delay.Tick += delegate { Commit(); };
            Unloaded += delegate { ClearHighlight(); Commit(); };
            LoadSection();
        }

        private void LoadSection()
        {
            ClearHighlight();
            fields.Children.Clear();
            var options = preferences.Appearance;
            switch (category.SelectedIndex)
            {
                case 0: PresetFields(); break;
                case 1: TileFields(options); break;
                case 2: TypographyFields(options); break;
                case 3: ColorFields(options); break;
                case 4: LayoutFields(options); break;
                case 5: MotionFields(options); break;
            }
        }

        internal void ShowAppearanceSample(FrameworkElement source, HighlightPart part, double radius)
        {
            if (!ReferenceEquals(sampleBrush.Visual, source)) sampleBrush.Visual = source;
            sample.Visibility = source == null ? Visibility.Hidden : Visibility.Visible;
            if (source == null)
            {
                sampleHighlight.Show(Array.Empty<HighlightBox>(), false);
                return;
            }
            double scale = Math.Min((sample.Width - 8) / source.ActualWidth, (sample.Height - 8) / source.ActualHeight);
            double width = source.ActualWidth * scale, height = source.ActualHeight * scale;
            var bounds = new Rect((sample.Width - width) / 2, (sample.Height - height) / 2, width, height);
            sampleHighlight.Show(new[] { new HighlightBox(bounds, part, radius * scale, Rect.Empty) },
                preferences.Appearance.Motion > 0);
        }

        private void ClearHighlight()
        {
            if (highlightedRow == null) return;
            highlightedRow = null;
            HighlightChanged?.Invoke(AppearanceTarget.None);
        }

        private StackPanel Card(string heading)
        {
            var panel = new StackPanel();
            if (heading != null)
            {
                var label = new TextBlock { Text = heading };
                label.SetResourceReference(StyleProperty, "SettingsHeading");
                panel.Children.Add(label);
            }
            var border = new Border { Child = panel };
            border.SetResourceReference(StyleProperty, "SettingsCard");
            fields.Children.Add(border);
            return panel;
        }

        private void PresetFields()
        {
            var builtIns = Card("Built-in presets");
            var choices = AppBuild.IsPreview ? AppAppearance.PresetNames.Take(1).ToArray() : AppAppearance.PresetNames;
            builtInPicker = Picker("Built-in preset", new[] { "Choose a preset" }.Concat(choices).ToArray(), 0);
            builtIns.Children.Add(builtInPicker);
            var message = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 6) };
            message.SetResourceReference(TextBlock.ForegroundProperty, "AppMuted");
            AutomationProperties.SetLiveSetting(message, AutomationLiveSetting.Polite);
            builtInPicker.SelectionChanged += delegate
            {
                if (builtInPicker.SelectedIndex <= 0) return;
                StartFrom(AppAppearance.BuiltInPreset(builtInPicker.SelectedIndex - 1), true);
                message.Text = "";
            };

            var personal = Card("My presets");
            var saved = new ComboBox { Height = 32, MaxDropDownHeight = 240 };
            AutomationProperties.SetName(saved, "Saved preset");
            var empty = new TextBlock { Text = "No saved presets yet.", Margin = new Thickness(0, 2, 0, 4) };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "AppMuted");
            personal.Children.Add(saved);
            personal.Children.Add(empty);

            var actions = new WrapPanel();
            var confirmation = new WrapPanel { Visibility = Visibility.Collapsed };
            Action dismissConfirmation = delegate
            {
                confirmation.Visibility = Visibility.Collapsed;
                actions.Visibility = saved.SelectedItem == null ? Visibility.Collapsed : Visibility.Visible;
            };
            Action<AppearancePreset> refresh = selected =>
            {
                var ordered = preferences.Presets.OrderBy(preset => preset.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
                saved.ItemsSource = ordered;
                saved.SelectedItem = selected ?? ordered.FirstOrDefault();
                saved.Visibility = ordered.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
                empty.Visibility = ordered.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                dismissConfirmation();
            };
            saved.SelectionChanged += delegate { dismissConfirmation(); message.Text = ""; };
            actions.Children.Add(ActionButton("Apply saved preset", delegate
            {
                if (!(saved.SelectedItem is AppearancePreset preset)) return;
                StartFrom(preset);
                message.Text = "Applied " + preset.Name + ".";
            }));
            actions.Children.Add(ActionButton("Update from current", delegate
            {
                if (!(saved.SelectedItem is AppearancePreset preset)) return;
                var updated = AppearancePreset.Capture(preferences, preset.Name);
                preferences.Presets[preferences.Presets.IndexOf(preset)] = updated;
                save();
                refresh(updated);
                message.Text = "Updated " + updated.Name + ".";
            }));
            actions.Children.Add(ActionButton("Delete…", delegate
            {
                if (!(saved.SelectedItem is AppearancePreset preset)) return;
                actions.Visibility = Visibility.Collapsed;
                confirmation.Visibility = Visibility.Visible;
                message.Text = "Delete " + preset.Name + "?";
                ((Button)confirmation.Children[0]).Focus();
            }));
            confirmation.Children.Add(ActionButton("Cancel", delegate
            {
                dismissConfirmation();
                message.Text = "";
                saved.Focus();
            }));
            confirmation.Children.Add(ActionButton("Confirm delete", delegate
            {
                if (!(saved.SelectedItem is AppearancePreset preset)) return;
                preferences.Presets.Remove(preset);
                save();
                refresh(null);
                message.Text = "Deleted " + preset.Name + ".";
            }));
            personal.Children.Add(actions);
            personal.Children.Add(confirmation);

            var name = new TextBox { MaxLength = AppearancePreset.NameLimit, MinHeight = 32, Padding = new Thickness(6, 4, 6, 4) };
            name.SetResourceReference(Control.BackgroundProperty, "AppControl");
            name.SetResourceReference(Control.ForegroundProperty, "AppText");
            name.SetResourceReference(Control.BorderBrushProperty, "AppEdge");
            name.SetResourceReference(TextBox.CaretBrushProperty, "AppText");
            AutomationProperties.SetName(name, "Preset name");
            LabelField(personal, "Preset name", name, name);
            personal.Children.Add(ActionButton("Save current appearance", delegate
            {
                string title = name.Text.Trim();
                if (title.Length == 0)
                {
                    message.Text = "Enter a preset name.";
                    name.Focus();
                    return;
                }
                if (PresetNameExists(title))
                {
                    message.Text = "A preset with that name already exists.";
                    name.Focus();
                    return;
                }
                var preset = AppearancePreset.Capture(preferences, title);
                preferences.Presets.Add(preset);
                save();
                refresh(preset);
                name.Clear();
                message.Text = "Saved " + preset.Name + ".";
            }));
            SharingFields(saved, refresh, message);
            fields.Children.Add(message);
            refresh(null);
        }

        private void SharingFields(ComboBox saved, Action<AppearancePreset> refresh, TextBlock message)
        {
            var panel = Card("Share presets");
            var includeImage = new CheckBox { Content = "Include background image", IsChecked = true, Margin = new Thickness(0, 4, 0, 0),
                ToolTip = "Including the image makes the code longer." };
            includeImage.SetResourceReference(StyleProperty, "SettingSwitch");
            panel.Children.Add(includeImage);
            Button copy = null;
            bool copying = false;
            Action refreshCopy = delegate
            {
                var preset = saved.SelectedItem as AppearancePreset;
                copy.IsEnabled = preset != null && !copying;
                includeImage.Visibility = string.IsNullOrWhiteSpace(preset?.Appearance.BackgroundImage)
                    ? Visibility.Collapsed : Visibility.Visible;
            };
            copy = ActionButton("Copy share code", async delegate
            {
                if (copying || !(saved.SelectedItem is AppearancePreset source)) return;
                var snapshot = source.Copy();
                bool withImage = includeImage.IsChecked == true;
                copying = true;
                refreshCopy();
                message.Text = "Creating share code…";
                try
                {
                    string code = await Task.Run(() => PresetShareCode.Export(snapshot, withImage));
                    if (!copy.IsLoaded) return;
                    Clipboard.SetText(code);
                    message.Text = "Copied the share code for " + snapshot.Name + ".";
                }
                catch (Exception error) { if (copy.IsLoaded) message.Text = "Couldn't copy the share code. " + error.Message; }
                finally { copying = false; refreshCopy(); }
            });
            panel.Children.Add(copy);
            saved.SelectionChanged += delegate { refreshCopy(); };
            refreshCopy();

            var codeBox = new TextBox { MaxLength = PresetShareCode.MaxCodeLength, MinHeight = 32, Padding = new Thickness(6, 4, 6, 4) };
            codeBox.SetResourceReference(Control.BackgroundProperty, "AppControl");
            codeBox.SetResourceReference(Control.ForegroundProperty, "AppText");
            codeBox.SetResourceReference(Control.BorderBrushProperty, "AppEdge");
            codeBox.SetResourceReference(TextBox.CaretBrushProperty, "AppText");
            AutomationProperties.SetName(codeBox, "Preset share code");
            LabelField(panel, "Import from code", codeBox, codeBox);
            var actions = new WrapPanel();
            bool importing = false;
            async Task Import(bool fromClipboard)
            {
                if (importing) return;
                importing = true;
                actions.IsEnabled = false;
                message.Text = "Reading share code…";
                try
                {
                    string code = fromClipboard ? Clipboard.GetText() : codeBox.Text;
                    var preset = await Task.Run(() => PresetShareCode.Import(code, dataDirectory));
                    if (!codeBox.IsLoaded) return;
                    preset.Name = AvailablePresetName(preset.Name);
                    preferences.Presets.Add(preset);
                    refresh(preset);
                    StartFrom(preset);
                    codeBox.Clear();
                    message.Text = "Imported and applied " + preset.Name + ".";
                }
                catch (Exception error) { if (codeBox.IsLoaded) message.Text = "Couldn't import the preset. " + error.Message; }
                finally { importing = false; actions.IsEnabled = true; }
            }
            actions.Children.Add(ActionButton("Import and apply", async () => await Import(false)));
            actions.Children.Add(ActionButton("Paste and apply", async () => await Import(true)));
            panel.Children.Add(actions);
        }

        private bool PresetNameExists(string name) =>
            preferences.Presets.Any(preset => string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase));

        private string AvailablePresetName(string name)
        {
            string candidate = name;
            for (int number = 2; PresetNameExists(candidate); number++)
            {
                string suffix = " (" + number + ")";
                candidate = name.Substring(0, Math.Min(name.Length, AppearancePreset.NameLimit - suffix.Length)).TrimEnd() + suffix;
            }
            return candidate;
        }

        private static Button ActionButton(string label, Action action)
        {
            var button = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 6, 6, 0) };
            button.SetResourceReference(StyleProperty, "ActionButton");
            button.Click += delegate { action(); };
            return button;
        }

        private void StartFrom(AppearancePreset preset, bool keepBuiltInSelection = false)
        {
            previousAppearance = AppearancePreset.Capture(preferences);
            undo.IsEnabled = true;
            ApplyAppearance(preset, keepBuiltInSelection);
        }

        private void ApplyAppearance(AppearancePreset preset, bool keepBuiltInSelection = false)
        {
            preset.ApplyTo(preferences);
            if (category.SelectedIndex != 0) LoadSection();
            else if (!keepBuiltInSelection) builtInPicker.SelectedIndex = 0;
            Changed();
            Commit();
        }

        private void TileFields(AppearanceOptions options)
        {
            var frame = Card("Tile shape");
            Choice(frame, "Artwork layout", AppAppearance.LayoutNames, options.Layout, value => options.Layout = value);
            Number(frame, AppearanceTarget.TileWidth, "Target tile width", 100, 520, 1, options.Width, value => options.Width = value);
            Number(frame, AppearanceTarget.TileWidth, "Maximum tile width", 100, 720, 1, options.MaximumWidth, value => options.MaximumWidth = value);
            Number(frame, AppearanceTarget.TileHeight, "Tile height", 64, 480, 1, options.Height, value => options.Height = value);
            Number(frame, AppearanceTarget.TileGap, "Gap between tiles", 0, 32, 1, options.Gap, value => options.Gap = value);
            Number(frame, AppearanceTarget.TileCorners, "Tile corner radius", 0, 48, 1, options.TileCorner, value => options.TileCorner = value);
            Number(frame, AppearanceTarget.TileOutline, "Tile border thickness", 0, 6, .5, options.TileOutline, value => options.TileOutline = value);
            var art = Card("Artwork");
            var crops = new[] { Stretch.UniformToFill, Stretch.Uniform, Stretch.Fill };
            Choice(art, "Artwork fit", new[] { "Crop to fill", "Show full image", "Stretch to fit" },
                Array.IndexOf(crops, options.Crop), value => options.Crop = crops[value]);
            Number(art, AppearanceTarget.Artwork, "Artwork opacity", 0, 1, .01, options.ArtworkOpacity, value => options.ArtworkOpacity = value, Percentage);
            Number(art, AppearanceTarget.ArtworkCorners, "Inset artwork corner radius", 0, 40, 1, options.ArtworkCorner,
                value => options.ArtworkCorner = value).ToolTip = "Applies to layouts with inset artwork.";
            Number(art, AppearanceTarget.SideArtwork, "Maximum side artwork width", 42, 175, 1, options.SideArtworkWidth,
                value => options.SideArtworkWidth = value).ToolTip = "Applies to Artwork beside and Detail rail.";
            Number(art, AppearanceTarget.Caption, "Full-cover caption shade", 0, 1, .01, options.CoverShade,
                value => options.CoverShade = value, Percentage).ToolTip = "Applies to the Full cover layout.";
            var details = Card("Tile details");
            Toggle(details, "Show source badges", options.ShowSource, value => options.ShowSource = value);
            Toggle(details, "Show launch count and playtime", options.ShowStats, value => options.ShowStats = value);
            Toggle(details, "Show play indicator", options.ShowPlay, value => options.ShowPlay = value);
            Toggle(details, "Show edit button", options.ShowEdit, value => options.ShowEdit = value);
        }

        private void TypographyFields(AppearanceOptions options)
        {
            var fonts = Card("Fonts");
            Font(fonts, "Interface font", options.BodyFont, value => options.BodyFont = value);
            Font(fonts, "Heading font", options.HeadingFont, value => options.HeadingFont = value);
            Font(fonts, "Tile font", options.TileFont, value => options.TileFont = value);
            var sizes = Card("Text size");
            Number(sizes, AppearanceTarget.InterfaceText, "Interface text", 10, 22, 1, options.BodySize, value => options.BodySize = value);
            Number(sizes, AppearanceTarget.Headings, "Headings", 16, 40, 1, options.HeadingSize, value => options.HeadingSize = value);
            Number(sizes, AppearanceTarget.TileTitles, "Tile titles", 10, 32, 1, options.TileTitleSize, value => options.TileTitleSize = value);
            Number(sizes, AppearanceTarget.TileDetails, "Tile details", 8, 22, 1, options.TileDetailSize, value => options.TileDetailSize = value);
        }

        private void Font(StackPanel panel, string label, string current, Action<string> set)
        {
            var choices = new[] { current }.Concat(FontsOnPc).Distinct(StringComparer.CurrentCultureIgnoreCase).ToArray();
            Choice(panel, label, choices, 0, index => set(choices[index]));
        }

        private void ColorFields(AppearanceOptions options)
        {
            var panel = Card("Palette");
            var paletteHighlights = new[] { AppearanceTarget.Accent, AppearanceTarget.Background, AppearanceTarget.Panels,
                AppearanceTarget.Controls, AppearanceTarget.Edges, AppearanceTarget.MutedText, AppearanceTarget.InterfaceText,
                AppearanceTarget.Warnings, AppearanceTarget.Background, AppearanceTarget.TileOutline, AppearanceTarget.ArtworkText };
            var target = Picker("Color to edit", new[] { "Accent" }.Concat(AppAppearance.ColorNames).ToArray(), 0);
            panel.Children.Add(target);
            var wheel = new ColorWheel { Width = 145, Height = 145 };
            AutomationProperties.SetName(wheel, "Color wheel");
            var hex = new TextBox { MaxLength = 7, MinHeight = 30 };
            AutomationProperties.SetName(hex, "Hex color");
            hex.SetResourceReference(BackgroundProperty, "AppControl");
            hex.SetResourceReference(Control.ForegroundProperty, "AppText");
            hex.SetResourceReference(TextBox.CaretBrushProperty, "AppText");
            hex.SetResourceReference(Control.BorderBrushProperty, "AppEdge");
            var swatch = new Border { BorderThickness = new Thickness(1) };
            var colorHighlight = new AppearanceHighlight();
            var colorSample = new Grid { Width = 28, Height = 28, Margin = new Thickness(0, 0, 7, 0),
                Children = { swatch, colorHighlight } };
            swatch.SetResourceReference(Border.BorderBrushProperty, "AppEdge");
            swatch.SetResourceReference(Border.CornerRadiusProperty, "AppControlCorner");
            var hexRow = new DockPanel();
            hexRow.Children.Add(colorSample);
            hexRow.Children.Add(hex);
            var tools = new StackPanel { Width = 134, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(new WrapPanel { Margin = new Thickness(0, 8, 0, 0), Children = { wheel, tools } });
            bool reflecting = false;
            Slider brightness = Number(tools, AppearanceTarget.Accent, "Brightness", 0, 1, .01, 1,
                value => { if (!reflecting) wheel.Brightness = value; }, Percentage, false,
                () => paletteHighlights[Math.Max(0, target.SelectedIndex)]);
            var brightnessRow = (FrameworkElement)brightness.Parent;
            brightnessRow.MouseEnter += delegate {
                colorHighlight.Show(new[] { new HighlightBox(new Rect(1, 1, 26, 26), HighlightPart.Frame,
                    options.ControlCorner, Rect.Empty) }, options.Motion > 0);
            };
            brightnessRow.MouseLeave += delegate { colorHighlight.Show(Array.Empty<HighlightBox>(), false); };
            brightnessRow.Unloaded += delegate { colorHighlight.Show(Array.Empty<HighlightBox>(), false); };
            LabelField(tools, "Hex color", hex, hexRow);

            Action reflect = delegate
            {
                reflecting = true;
                wheel.SelectColor(AppAppearance.ReadColor(target.SelectedIndex == 0 ? preferences.Accent : options.Colors[target.SelectedIndex - 1]));
                brightness.Value = wheel.Brightness;
                hex.Text = AppAppearance.Hex(wheel.Color);
                swatch.Background = new SolidColorBrush(wheel.Color);
                reflecting = false;
            };
            wheel.Changed += color =>
            {
                if (reflecting) return;
                reflecting = true;
                brightness.Value = wheel.Brightness;
                if (!hex.IsKeyboardFocused) hex.Text = AppAppearance.Hex(color);
                swatch.Background = new SolidColorBrush(color);
                if (target.SelectedIndex == 0) preferences.Accent = AppAppearance.Hex(color);
                else options.Colors[target.SelectedIndex - 1] = AppAppearance.Hex(color);
                AppAppearance.ApplyColors(preferences);
                reflecting = false;
                Changed();
            };
            hex.TextChanged += delegate
            {
                if (!reflecting && AppAppearance.TryReadColor(hex.Text, out Color color)) wheel.SelectColor(color);
            };
            hex.LostKeyboardFocus += delegate { hex.Text = AppAppearance.Hex(wheel.Color); };
            target.SelectionChanged += delegate { reflect(); };
            reflect();

            var backdrop = Card("Background");
            Toggle(backdrop, "Use a gradient", options.Gradient, value => options.Gradient = value);
            Number(backdrop, AppearanceTarget.Background, "Gradient angle", 0, 360, 1, options.GradientAngle, value => options.GradientAngle = value);
            Choice(backdrop, "Background texture", new[] { "None", "Grid", "Dots", "Fine grid" },
                options.Texture, value => options.Texture = value);
            Number(backdrop, AppearanceTarget.Background, "Texture opacity", 0, 1, .01, options.TextureOpacity, value => options.TextureOpacity = value, Percentage);

            var wallpaper = Card("Background image");
            var file = new TextBlock { Text = FileLabel(options.BackgroundImage), TextWrapping = TextWrapping.Wrap };
            var browse = new Button { Content = "Choose image…", Margin = new Thickness(0, 0, 6, 4) };
            var clear = new Button { Content = "Remove", IsEnabled = !string.IsNullOrWhiteSpace(options.BackgroundImage) };
            browse.SetResourceReference(StyleProperty, "ActionButton");
            clear.SetResourceReference(StyleProperty, "ActionButton");
            wallpaper.Children.Add(file);
            wallpaper.Children.Add(new WrapPanel { Margin = new Thickness(0, 6, 0, 3), Children = { browse, clear } });
            browse.Click += delegate
            {
                var picker = new OpenFileDialog { Title = "Choose a background image", Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff" };
                if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
                try
                {
                    AppAppearance.LoadWallpaper(picker.FileName);
                    options.BackgroundImage = picker.FileName;
                    file.Text = FileLabel(options.BackgroundImage);
                    clear.IsEnabled = true;
                    Changed();
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
                    error is ArgumentException || error is NotSupportedException || error is FileFormatException)
                {
                    file.Text = "Couldn’t open that image.";
                }
            };
            clear.Click += delegate
            {
                options.BackgroundImage = null;
                file.Text = FileLabel(null);
                clear.IsEnabled = false;
                Changed();
            };
            var fits = new[] { Stretch.UniformToFill, Stretch.Uniform, Stretch.Fill };
            Choice(wallpaper, "Background image fit", new[] { "Crop to fill", "Show full image", "Stretch to fit" },
                Array.IndexOf(fits, options.BackgroundImageFit), value => options.BackgroundImageFit = fits[value]);
            Number(wallpaper, AppearanceTarget.Background, "Background image opacity", 0, 1, .01, options.BackgroundImageOpacity,
                value => options.BackgroundImageOpacity = value, Percentage);
        }

        private static string FileLabel(string path) => string.IsNullOrWhiteSpace(path) ? "No image selected" : Path.GetFileName(path);

        private void LayoutFields(AppearanceOptions options)
        {
            var layout = Card("Library");
            Number(layout, AppearanceTarget.Scale, "Interface scale", .75, 1.5, .05, preferences.Scale, value => preferences.Scale = value, Percentage);
            Choice(layout, "Grid columns", new[] { "Automatic", "1", "2", "3", "4", "5", "6", "7", "8" },
                options.Columns, value => options.Columns = value);
            Number(layout, AppearanceTarget.SearchWidth, "Search box width", 100, 360, 1, options.SearchWidth, value => options.SearchWidth = value);
            Number(layout, AppearanceTarget.ControlCorners, "Control corner radius", 0, 24, 1, options.ControlCorner, value => options.ControlCorner = value);
            Toggle(layout, "Show total playtime in header", options.ShowPlaytime, value => options.ShowPlaytime = value);
            var pins = Card("Pinned apps");
            Choice(pins, "Pinned sidebar position", new[] { "Left", "Right", "Hidden" }, options.Sidebar, value => options.Sidebar = value);
            Number(pins, AppearanceTarget.SidebarWidth, "Sidebar width", 90, 320, 1, options.SidebarWidth, value => options.SidebarWidth = value);
            Number(pins, AppearanceTarget.PinHeight, "Pinned tile height", 32, 118, 1, options.PinHeight, value => options.PinHeight = value);
            Number(pins, AppearanceTarget.PinArtwork, "Pinned artwork opacity", 0, 1, .01, options.PinArtwork, value => options.PinArtwork = value, Percentage);
        }

        private void MotionFields(AppearanceOptions options)
        {
            var hover = Card("Tile hover");
            Number(hover, AppearanceTarget.HoverShift, "Panel shift", 0, 10, .5, options.HoverShift, value => options.HoverShift = value);
            Number(hover, AppearanceTarget.HoverGlow, "Glow intensity", 0, 1, .01, options.HoverGlow, value => options.HoverGlow = value, Percentage);
            Number(hover, AppearanceTarget.HoverGlow, "Glow softness", 0, 26, 1, options.GlowBlur, value => options.GlowBlur = value);
            Toggle(hover, "Light sweep across artwork", options.Sweep, value => options.Sweep = value);
            var launch = Card("On launch");
            Choice(launch, "Tile launch animation", new[] { "Accent ripple", "Accent pulse", "Light streak", "Off" },
                (int)options.LaunchCue, value => options.LaunchCue = (LaunchAnimation)value);
            var sample = new TileLaunchCue();
            var preview = ActionButton("Preview animation", () => sample.Play(options, options.ControlCorner));
            AutomationProperties.SetName(preview, "Preview animation");
            preview.MinWidth = 156;
            preview.MinHeight = 40;
            preview.Padding = new Thickness(0);
            preview.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            preview.VerticalContentAlignment = VerticalAlignment.Stretch;
            preview.Content = new Grid { Children = {
                new TextBlock { Text = "Preview animation", HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center }, sample
            } };
            launch.Children.Add(preview);
            var transitions = Card("Transitions");
            Number(transitions, AppearanceTarget.Transitions, "Animation duration", 0, 2, .05, options.Motion, value => options.Motion = value,
                value => value == 0 ? "Off" : Percentage(value));
        }

        private static string Percentage(double value) => value.ToString("P0", CultureInfo.CurrentCulture);

        private static ComboBox Picker(string label, string[] choices, int selected)
        {
            var picker = new ComboBox { ItemsSource = choices, SelectedIndex = selected, Height = 32, MaxDropDownHeight = 250 };
            AutomationProperties.SetName(picker, label);
            return picker;
        }

        private void Choice(StackPanel panel, string label, string[] choices, int selected, Action<int> set)
        {
            var picker = Picker(label, choices, selected);
            LabelField(panel, label, picker, picker);
            picker.SelectionChanged += delegate
            {
                if (picker.SelectedIndex < 0) return;
                set(picker.SelectedIndex);
                Changed();
            };
        }

        private void Toggle(StackPanel panel, string label, bool value, Action<bool> set)
        {
            var toggle = new CheckBox { Content = label, IsChecked = value };
            toggle.SetResourceReference(StyleProperty, "SettingSwitch");
            panel.Children.Add(toggle);
            RoutedEventHandler change = delegate { set(toggle.IsChecked == true); Changed(); };
            toggle.Checked += change;
            toggle.Unchecked += change;
        }

        private Slider Number(StackPanel panel, AppearanceTarget target, string label, double min, double max, double tick,
            double value, Action<double> set, Func<double, string> format = null, bool trackChanges = true,
            Func<AppearanceTarget> targetProvider = null)
        {
            var slider = new Slider { Minimum = min, Maximum = max, Value = value,
                TickFrequency = tick, SmallChange = tick, LargeChange = tick * 10, IsSnapToTickEnabled = true };
            slider.SetResourceReference(StyleProperty, "SettingSlider");
            AutomationProperties.SetName(slider, label);
            var readout = new TextBlock { Margin = new Thickness(8, 0, 0, 0) };
            readout.SetResourceReference(TextBlock.ForegroundProperty, "AppAccentText");
            SetDock(readout, Dock.Right);
            var header = new DockPanel { Margin = new Thickness(0, 5, 0, 0) };
            header.Children.Add(readout);
            header.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
            var row = new StackPanel { Background = Brushes.Transparent, Children = { header, slider } };
            panel.Children.Add(row);
            row.MouseEnter += delegate
            {
                highlightedRow = row;
                HighlightChanged?.Invoke(targetProvider == null ? target : targetProvider());
            };
            row.MouseLeave += delegate { if (ReferenceEquals(highlightedRow, row)) ClearHighlight(); };
            if (format == null) format = number => number.ToString("0.##", CultureInfo.CurrentCulture);
            readout.Text = format(value);
            slider.ValueChanged += delegate
            {
                readout.Text = format(slider.Value);
                set(slider.Value);
                if (trackChanges) Changed();
            };
            return slider;
        }

        private static void LabelField(StackPanel panel, string label, Control control, UIElement field)
        {
            var caption = new Label { Content = label, Target = control, Padding = new Thickness(0, 6, 0, 4) };
            caption.SetResourceReference(Control.ForegroundProperty, "AppText");
            panel.Children.Add(caption);
            panel.Children.Add(field);
        }

        private void Changed()
        {
            dirty = true;
            delay.Stop();
            delay.Start();
        }

        internal void Commit()
        {
            delay.Stop();
            if (!dirty) return;
            dirty = false;
            apply();
            save();
        }
    }
}
