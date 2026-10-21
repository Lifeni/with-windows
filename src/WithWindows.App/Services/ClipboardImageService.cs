using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using WithWindows.Core.Chat;

namespace WithWindows.Services;

public sealed record ClipboardImageData(
    byte[] Bytes,
    string DisplayName,
    string MimeType,
    int Width,
    int Height,
    bool WasCompressed);

/// <summary>读取剪贴板中的位图或图片文件，并统一压缩到 1 MiB 以内。</summary>
public static class ClipboardImageService
{
    private const int MaxImages = 4;
    private const long MaxSourceBytes = 50L * 1024 * 1024;

    public static bool ContainsPotentialImage(DataPackageView content)
        => content.Contains(StandardDataFormats.Bitmap)
           || content.Contains(StandardDataFormats.StorageItems);

    public static async Task<IReadOnlyList<ClipboardImageData>> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        DataPackageView content = Clipboard.GetContent();
        if (content.Contains(StandardDataFormats.Bitmap))
        {
            byte[] source = await ReadBitmapAsync(content, cancellationToken);
            ProcessedChatImage image = await ChatImageProcessor.PrepareAsync(
                source,
                "image/png",
                forceTranscode: true,
                cancellationToken);
            return
            [
                new ClipboardImageData(
                    image.Bytes,
                    $"剪贴板图片-{DateTime.Now:HHmmss}{ChatAttachmentStore.ExtensionForMimeType(image.MimeType)}",
                    image.MimeType,
                    image.Width,
                    image.Height,
                    WasCompressed: source.Length > ChatAttachmentStore.MaxBytes
                        || !string.Equals(image.MimeType, "image/png", StringComparison.OrdinalIgnoreCase)),
            ];
        }

        if (!content.Contains(StandardDataFormats.StorageItems))
            return [];

        IReadOnlyList<IStorageItem> items = await content.GetStorageItemsAsync();
        var results = new List<ClipboardImageData>();
        foreach (IStorageItem item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (results.Count >= MaxImages)
                break;
            if (item is not StorageFile file)
                continue;

            string? mimeType = MimeTypeFromExtension(Path.GetExtension(file.Name));
            if (mimeType is null)
                continue;

            byte[] source = await ReadFileAsync(file, cancellationToken);
            ProcessedChatImage image = await ChatImageProcessor.PrepareAsync(
                source,
                mimeType,
                cancellationToken);
            results.Add(new ClipboardImageData(
                image.Bytes,
                file.Name,
                image.MimeType,
                image.Width,
                image.Height,
                WasCompressed: source.Length > ChatAttachmentStore.MaxBytes
                    || !ReferenceEquals(source, image.Bytes)));
        }

        return results;
    }

    public static string? MimeTypeFromExtension(string extension)
        => extension.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => null,
        };

    private static async Task<byte[]> ReadBitmapAsync(
        DataPackageView content,
        CancellationToken cancellationToken)
    {
        RandomAccessStreamReference reference = await content.GetBitmapAsync();
        using IRandomAccessStreamWithContentType stream = await reference.OpenReadAsync();
        return await ReadStreamAsync(stream, cancellationToken);
    }

    private static async Task<byte[]> ReadFileAsync(
        StorageFile file,
        CancellationToken cancellationToken)
    {
        using IRandomAccessStreamWithContentType stream = await file.OpenReadAsync();
        return await ReadStreamAsync(stream, cancellationToken);
    }

    private static async Task<byte[]> ReadStreamAsync(
        IRandomAccessStream stream,
        CancellationToken cancellationToken)
    {
        if (stream.Size > MaxSourceBytes)
            throw new ArgumentException("原始图片不能超过 50 MiB。");

        cancellationToken.ThrowIfCancellationRequested();
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        cancellationToken.ThrowIfCancellationRequested();

        byte[] bytes = new byte[(int)stream.Size];
        reader.ReadBytes(bytes);
        return bytes;
    }
}
