using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace WithWindows.Core.Chat;

/// <summary>经过格式识别和 1 MiB 限制处理的图片。</summary>
public sealed record ProcessedChatImage(
    byte[] Bytes,
    string MimeType,
    int Width,
    int Height);

/// <summary>解码、缩放并重新编码图片，保证发送和落盘均不超过 1 MiB。</summary>
public static class ChatImageProcessor
{
    private const int MaxSourceBytes = 50 * 1024 * 1024;

    public static async Task<ProcessedChatImage> PrepareAsync(
        byte[] source,
        string mimeType,
        bool forceTranscode,
        CancellationToken cancellationToken = default)
    {
        if (source.Length == 0)
            throw new ArgumentException("图片内容为空。", nameof(source));
        if (source.Length > MaxSourceBytes)
            throw new ArgumentException("原始图片不能超过 50 MiB。", nameof(source));

        cancellationToken.ThrowIfCancellationRequested();
        (int sourceWidth, int sourceHeight) = await ReadDimensionsAsync(source);

        string normalizedMime = NormalizeMimeType(mimeType);
        if (!forceTranscode
            && source.Length <= ChatAttachmentStore.MaxBytes
            && ChatAttachmentStore.IsSupportedMimeType(normalizedMime))
        {
            return new ProcessedChatImage(
                source,
                normalizedMime,
                sourceWidth,
                sourceHeight);
        }

        int[] scales = [100, 90, 80, 70, 60, 50, 40, 30, 25, 20];
        foreach (int scale in scales)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (uint width, uint height) = ScaleDimensions(sourceWidth, sourceHeight, scale);
            byte[] png = await EncodeAsync(
                source,
                width,
                height,
                BitmapEncoder.PngEncoderId,
                quality: null);
            if (png.Length <= ChatAttachmentStore.MaxBytes)
                return new ProcessedChatImage(png, "image/png", (int)width, (int)height);
        }

        double[] qualities = [0.85, 0.75, 0.65];
        foreach (double quality in qualities)
        {
            foreach (int scale in scales)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (uint width, uint height) =
                    ScaleDimensions(sourceWidth, sourceHeight, scale);
                byte[] jpeg = await EncodeAsync(
                    source,
                    width,
                    height,
                    BitmapEncoder.JpegEncoderId,
                    quality);
                if (jpeg.Length <= ChatAttachmentStore.MaxBytes)
                {
                    return new ProcessedChatImage(
                        jpeg,
                        "image/jpeg",
                        (int)width,
                        (int)height);
                }
            }
        }

        throw new InvalidOperationException("图片无法压缩到 1 MiB 以内。");
    }

    public static async Task<ProcessedChatImage> PrepareAsync(
        byte[] source,
        string mimeType,
        CancellationToken cancellationToken = default)
        => await PrepareAsync(
            source,
            mimeType,
            forceTranscode: false,
            cancellationToken);

    private static async Task<(int Width, int Height)> ReadDimensionsAsync(byte[] bytes)
    {
        using InMemoryRandomAccessStream stream = await CreateStreamAsync(bytes);
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
        return ((int)decoder.PixelWidth, (int)decoder.PixelHeight);
    }

    private static async Task<byte[]> EncodeAsync(
        byte[] source,
        uint width,
        uint height,
        Guid encoderId,
        double? quality)
    {
        using InMemoryRandomAccessStream input = await CreateStreamAsync(source);
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(input);
        var transform = new BitmapTransform
        {
            ScaledWidth = width,
            ScaledHeight = height,
        };
        SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            transform,
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.DoNotColorManage);

        using InMemoryRandomAccessStream output = new();
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(encoderId, output);
        if (quality is not null)
        {
            var properties = new BitmapPropertySet
            {
                ["ImageQuality"] = new BitmapTypedValue(
                    (float)quality.Value,
                    PropertyType.Single),
            };
            await encoder.BitmapProperties.SetPropertiesAsync(properties);
        }

        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();
        return await ReadStreamAsync(output);
    }

    private static async Task<InMemoryRandomAccessStream> CreateStreamAsync(byte[] bytes)
    {
        var stream = new InMemoryRandomAccessStream();
        using var writer = new DataWriter(stream);
        writer.WriteBytes(bytes);
        await writer.StoreAsync();
        await writer.FlushAsync();
        writer.DetachStream();
        stream.Seek(0);
        return stream;
    }

    private static async Task<byte[]> ReadStreamAsync(IRandomAccessStream stream)
    {
        stream.Seek(0);
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        byte[] bytes = new byte[(int)stream.Size];
        reader.ReadBytes(bytes);
        return bytes;
    }

    private static (uint Width, uint Height) ScaleDimensions(
        int width,
        int height,
        int percent)
    {
        double factor = percent / 100d;
        return (
            (uint)Math.Max(1, Math.Round(width * factor)),
            (uint)Math.Max(1, Math.Round(height * factor)));
    }

    private static string NormalizeMimeType(string mimeType)
        => mimeType.Trim().ToLowerInvariant() switch
        {
            "image/jpg" => "image/jpeg",
            "image/png" => "image/png",
            "image/jpeg" => "image/jpeg",
            "image/webp" => "image/webp",
            "image/gif" => "image/gif",
            _ => throw new ArgumentException("当前仅支持 PNG、JPEG、WebP 或 GIF 图片。", nameof(mimeType)),
        };
}
