#nullable enable
using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PDFManager.Services
{
    public enum ImagePageSize { SameAsImage, A4, Letter }

    // How images become pages: the page size, and for A4 or Letter whether to leave a margin
    public record ImagePageOptions(ImagePageSize PageSize, bool Margin);

    /// <summary>
    /// Image files the Image to PDF tool accepts, and the EXIF orientation phones record instead of
    /// turning the pixels. PDF viewers ignore EXIF, so the tool applies the rotation itself.
    /// </summary>
    public static class ImageFiles
    {
        public static readonly string[] Extensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff" };

        public const string DialogFilter = "Images (*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff)|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff";

        public static bool IsImage(string path) =>
            Array.Exists(Extensions, e => string.Equals(e, Path.GetExtension(path), StringComparison.OrdinalIgnoreCase));

        // Clockwise degrees (0, 90, 180 or 270) the stored pixels must turn to be upright.
        // Mirrored orientations, which cameras do not produce, are treated as their rotation
        public static int ReadRotation(string path)
        {
            try
            {
                using var stream = File.OpenRead(path);
                var frame = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                if (frame.Metadata is BitmapMetadata metadata && metadata.GetQuery("System.Photo.Orientation") is ushort orientation)
                {
                    return orientation switch
                    {
                        3 or 4 => 180,
                        5 or 6 => 90,
                        7 or 8 => 270,
                        _ => 0
                    };
                }
            }
            catch
            {
                // No readable metadata (many PNGs and BMPs): the pixels are already upright
            }
            return 0;
        }

        // A small upright preview for the file list, created off the UI thread and frozen
        public static BitmapSource LoadThumbnail(string path, int width)
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.DecodePixelWidth = width;
            image.CacheOption = BitmapCacheOption.OnLoad; // read now, so the file is not kept open
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.EndInit();

            BitmapSource upright = image;
            int rotation = ReadRotation(path);
            if (rotation != 0)
                upright = new TransformedBitmap(image, new RotateTransform(rotation));
            upright.Freeze();
            return upright;
        }
    }
}
