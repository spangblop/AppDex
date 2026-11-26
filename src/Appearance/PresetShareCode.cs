using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;

namespace GameShelf
{
    internal static class PresetShareCode
    {
        private const string Prefix = "ADXP1-";
        private const int MaxMetadataBytes = 16384;
        private const int MaxImageBytes = 7 * 1024 * 1024;
        private const int ImageSize = 1280;
        internal const int MaxCodeLength = ((MaxMetadataBytes + MaxImageBytes + 8192) * 4 + 2) / 3;
        private static readonly byte[] PngHeader = { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82 };
        private static DataContractJsonSerializer Serializer() => new DataContractJsonSerializer(typeof(AppearancePreset),
            new DataContractJsonSerializerSettings { MaxItemsInObjectGraph = 240 });

        internal static string Export(AppearancePreset original, bool includeBackground)
        {
            var preset = original.Copy();
            string background = preset.Appearance.BackgroundImage;
            preset.Appearance.BackgroundImage = null;
            byte[] image = includeBackground && !string.IsNullOrWhiteSpace(background)
                ? EncodeBackground(background) : Array.Empty<byte>();

            using (var metadata = new MemoryStream())
            using (var packed = new MemoryStream())
            {
                Serializer().WriteObject(metadata, preset);
                if (metadata.Length > MaxMetadataBytes) throw new InvalidDataException("This preset is too large to share.");
                using (var compression = new GZipStream(packed, CompressionMode.Compress, true))
                using (var writer = new BinaryWriter(compression))
                {
                    writer.Write((int)metadata.Length);
                    writer.Write(image.Length);
                    writer.Write(metadata.ToArray());
                    writer.Write(image);
                }
                return Prefix + Convert.ToBase64String(packed.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            }
        }

        internal static AppearancePreset Import(string code, string dataDirectory)
        {
            if (string.IsNullOrWhiteSpace(code)) throw new InvalidDataException("Paste an AppDex preset code.");
            if (code.Length > MaxCodeLength) throw new InvalidDataException("That preset code is too large.");
            code = code.Trim();
            if (!code.StartsWith(Prefix, StringComparison.Ordinal))
                throw new InvalidDataException("Use a complete AppDex preset code starting with " + Prefix + ".");
            string body = code.Substring(Prefix.Length).Replace('-', '+').Replace('_', '/');
            if (body.Any(char.IsWhiteSpace)) body = new string(body.Where(character => !char.IsWhiteSpace(character)).ToArray());
            byte[] payload = Convert.FromBase64String(body.PadRight((body.Length + 3) / 4 * 4, '='));
            AppearancePreset preset;
            byte[] image;
            using (var packed = new MemoryStream(payload))
            using (var compression = new GZipStream(packed, CompressionMode.Decompress))
            using (var reader = new BinaryReader(compression))
            {
                int metadataLength = reader.ReadInt32(), imageLength = reader.ReadInt32();
                if (metadataLength < 1 || metadataLength > MaxMetadataBytes || imageLength < 0 || imageLength > MaxImageBytes)
                    throw new InvalidDataException("That preset code is invalid or too large.");
                byte[] metadata = reader.ReadBytes(metadataLength);
                image = reader.ReadBytes(imageLength);
                if (metadata.Length != metadataLength || image.Length != imageLength || compression.ReadByte() != -1)
                    throw new InvalidDataException("That preset code is incomplete or damaged.");
                using (var stream = new MemoryStream(metadata))
                    preset = Serializer().ReadObject(stream) as AppearancePreset;
            }

            if (preset?.Appearance == null || string.IsNullOrWhiteSpace(preset.Name) ||
                preset.Name.Trim().Length > AppearancePreset.NameLimit || !string.IsNullOrEmpty(preset.Appearance.BackgroundImage))
                throw new InvalidDataException("That preset code contains invalid settings.");
            ValidateFont(preset.Appearance.BodyFont);
            ValidateFont(preset.Appearance.HeadingFont);
            ValidateFont(preset.Appearance.TileFont);
            preset.Normalize();
            if (image.Length > 0) preset.Appearance.BackgroundImage = StoreBackground(image, dataDirectory);
            return preset;
        }

        private static void ValidateFont(string font)
        {
            if (font != null && (font.Length > 200 || font.IndexOfAny(new[] { '#', '/', '\\', ':', '%' }) >= 0 ||
                font.Any(char.IsControl))) throw new InvalidDataException("That preset code contains an invalid font name.");
        }

        private static byte[] EncodeBackground(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("The background image is missing. Clear “Include background image” to share the remaining settings.");
            var image = GameCatalog.ReadSizedPicture(path, ImageSize);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = new MemoryStream())
            {
                encoder.Save(stream);
                if (stream.Length > MaxImageBytes) throw new InvalidDataException("The background image is too large to include.");
                return stream.ToArray();
            }
        }

        private static int ReadDimension(byte[] image, int index) =>
            image[index] << 24 | image[index + 1] << 16 | image[index + 2] << 8 | image[index + 3];

        private static string StoreBackground(byte[] image, string dataDirectory)
        {
            if (image.Length < 24 || !PngHeader.SequenceEqual(image.Take(PngHeader.Length)) ||
                ReadDimension(image, 16) < 1 || ReadDimension(image, 16) > ImageSize ||
                ReadDimension(image, 20) < 1 || ReadDimension(image, 20) > ImageSize)
                throw new InvalidDataException("That preset code contains an invalid background image.");
            using (var stream = new MemoryStream(image)) GameCatalog.ReadSizedPicture(stream, ImageSize);
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(image)).Replace("-", "");
            string directory = Path.Combine(dataDirectory, "Shared wallpapers");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, hash + ".png");
            if (File.Exists(path)) return path;
            string temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllBytes(temporary, image);
                try { File.Move(temporary, path); }
                catch (IOException) when (File.Exists(path)) { }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return path;
        }
    }
}
