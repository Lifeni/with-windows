using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using WithWindows.Core.Chat;

namespace WithWindows.Core.Tests;

public class ChatImageProcessorTests
{
    [Fact]
    public async Task PrepareAsync_超过1MiB_自动压缩到限制内()
    {
        byte[] source = await CreateNoisePngAsync(1024, 1024);
        Assert.True(source.Length > ChatAttachmentStore.MaxBytes);

        ProcessedChatImage result = await ChatImageProcessor.PrepareAsync(
            source,
            "image/png",
            CancellationToken.None);

        Assert.InRange(result.Bytes.Length, 1, ChatAttachmentStore.MaxBytes);
        Assert.True(ChatAttachmentStore.IsSupportedMimeType(result.MimeType));
        Assert.True(result.Width > 0);
        Assert.True(result.Height > 0);
    }

    private static async Task<byte[]> CreateNoisePngAsync(int width, int height)
    {
        var stream = new InMemoryRandomAccessStream();
        BitmapEncoder encoder =
            await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        byte[] pixels = new byte[width * height * 4];
        new Random(42).NextBytes(pixels);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Ignore,
            (uint)width,
            (uint)height,
            96,
            96,
            pixels);
        await encoder.FlushAsync();

        stream.Seek(0);
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        byte[] result = new byte[stream.Size];
        reader.ReadBytes(result);
        return result;
    }
}
