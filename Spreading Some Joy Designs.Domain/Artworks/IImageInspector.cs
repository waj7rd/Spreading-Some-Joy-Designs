namespace SpreadingJoy.Domain.Artworks;

// Decides what a pile of bytes actually is, by decoding it rather than by
// believing its file extension or its Content-Type header.
//
// This is the only thing standing between "the customer said it was a PNG" and
// what the press receives. A file can claim image/png and be an HTML page, a
// zip, or a polyglot that a browser renders as script.
public interface IImageInspector
{
    // Null when the bytes aren't a decodable image in a format we accept.
    InspectedImage? Inspect(byte[] content);

    // Re-encodes the image to a clean file of the same format.
    //
    // The point isn't compression — it's that the output contains only pixels.
    // Whatever else was in the original container (EXIF with the photographer's
    // home address, colour profiles, an embedded thumbnail that doesn't match
    // the image, trailing bytes after the end marker) does not survive a decode
    // and re-encode. What we store is what a human moderator saw.
    byte[] Normalise(byte[] content, InspectedImage info);
}

// Named to stay out of the way of SixLabors.ImageSharp.ImageInfo, which the
// DAL has in scope wherever it implements this.
public record InspectedImage(string ContentType, string Extension, int WidthPx, int HeightPx)
{
    public long PixelCount => (long)WidthPx * HeightPx;
}
