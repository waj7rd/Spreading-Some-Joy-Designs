using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SpreadingJoy.Domain.Artworks;

namespace SpreadingJoy.DAL.Imaging;

// Decides what bytes actually are by decoding them, and re-encodes them so what
// we store contains nothing but pixels.
public class ImageSharpInspector : IImageInspector
{
    public InspectedImage? Inspect(byte[] content)
    {
        try
        {
            // Identify reads the header only — it does not decode the pixels.
            // That matters: it's how the dimensions can be checked against the
            // decompression-bomb limit before anything allocates a bitmap.
            var info = Image.Identify(content);
            if (info?.Metadata?.DecodedImageFormat == null)
                return null;

            var format = info.Metadata.DecodedImageFormat;

            var contentType = format.DefaultMimeType.ToLowerInvariant();
            if (!ImageLimits.AllowedContentTypes.Contains(contentType))
                return null;

            var extension = format.FileExtensions.First().ToLowerInvariant();

            return new InspectedImage(contentType, extension, info.Width, info.Height);
        }
        catch (UnknownImageFormatException)
        {
            return null;
        }
        catch (InvalidImageContentException)
        {
            // Header parsed, pixels are corrupt. Same answer to the customer:
            // we can't use this.
            return null;
        }
    }

    public byte[] Normalise(byte[] content, InspectedImage info)
    {
        using var image = Image.Load(content);

        // Everything that isn't pixels goes. EXIF can carry the GPS coordinates
        // of where a photo was taken, an XMP block can carry arbitrary text, and
        // an embedded thumbnail can show something different from the image
        // itself — which would mean the moderator approved one picture and the
        // press printed another.
        image.Metadata.ExifProfile = null;
        image.Metadata.XmpProfile = null;
        image.Metadata.IptcProfile = null;
        image.Metadata.IccProfile = null;

        using var output = new MemoryStream();
        image.Save(output, EncoderFor(info.ContentType));

        return output.ToArray();
    }

    // Re-encoded in the same format it arrived in, so a transparent PNG doesn't
    // silently become a JPEG with a black background.
    private static IImageEncoder EncoderFor(string contentType) => contentType switch
    {
        "image/png" => new PngEncoder(),
        "image/gif" => new GifEncoder(),
        "image/webp" => new WebpEncoder(),

        // 92 keeps artwork clean enough to print; re-encoding at the default
        // would visibly soften edges on line art.
        "image/jpeg" => new JpegEncoder { Quality = 92 },

        _ => throw new ArgumentOutOfRangeException(nameof(contentType), contentType,
                 "Inspect only returns allowed content types; this means the two lists have drifted."),
    };
}
