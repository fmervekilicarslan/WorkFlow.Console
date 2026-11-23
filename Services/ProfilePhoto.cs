using SkiaSharp;

namespace WorkFlow.Mvc.Services;

internal static class ProfilePhoto
{
    public const int MaxBytes = 2 * 1024 * 1024;
    public const int MaxRequestBytes = MaxBytes + 64 * 1024;
    private const int MaxSide = 2048;
    private const int OutputSide = 256;

    public static async Task<byte[]?> NormalizeAsync(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length is < 8 or > MaxBytes)
            return null;

        await using var stream = file.OpenReadStream();
        var header = new byte[8];
        await stream.ReadExactlyAsync(header, cancellationToken);
        var png = header.AsSpan().SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var jpeg = header[0] == 255 && header[1] == 216 && header[2] == 255;
        if (!png && !jpeg)
            return null;

        stream.Position = 0;
        using var codec = SKCodec.Create(stream);
        if (codec is null || codec.EncodedFormat != (png ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg))
            return null;
        var width = codec.Info.Width;
        var height = codec.Info.Height;
        if (width is < 1 or > MaxSide || height is < 1 or > MaxSide)
            return null;

        var sourceInfo = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var source = new SKBitmap(sourceInfo);
        if (codec.GetPixels(sourceInfo, source.GetPixels()) != SKCodecResult.Success)
            return null;

        // Encode only pixels into a new image, discarding the original name and metadata.
        var side = Math.Min(width, height);
        var crop = SKRect.Create((width - side) / 2f, (height - side) / 2f, side, side);
        var bounds = SKRect.Create(0, 0, OutputSide, OutputSide);
        using var surface = SKSurface.Create(new SKImageInfo(OutputSide, OutputSide));
        surface.Canvas.Clear(SKColors.White);
        using var decoded = SKImage.FromBitmap(source);
        surface.Canvas.DrawImage(decoded, crop, bounds, new SKSamplingOptions(SKFilterMode.Linear));
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);
        return data?.ToArray();
    }
}
