using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
using WithWindows.Core.Chat;

namespace WithWindows.Views;

/// <summary>消息图片的缩略图与发送模型。</summary>
public sealed class ChatAttachmentViewModel
{
    private ChatAttachmentViewModel(
        byte[] bytes,
        string displayName,
        string mimeType,
        int width,
        int height,
        bool wasCompressed,
        ChatAttachment? model)
    {
        Bytes = bytes;
        DisplayName = displayName;
        MimeType = mimeType;
        Width = width;
        Height = height;
        WasCompressed = wasCompressed;
        Model = model;

        string size = FormatSize(bytes.Length);
        SizeText = wasCompressed ? $"{size} · 已压缩" : size;
        DetailText = $"{Width}×{Height} · {SizeText}";
    }

    public byte[] Bytes { get; }
    public string DisplayName { get; }
    public string MimeType { get; }
    public int Width { get; }
    public int Height { get; }
    public bool WasCompressed { get; }
    public string SizeText { get; }
    public string DetailText { get; }
    public ImageSource? Preview { get; private init; }
    public ChatAttachment? Model { get; set; }

    public static async Task<ChatAttachmentViewModel> CreateAsync(
        byte[] bytes,
        string displayName,
        string mimeType,
        int width,
        int height,
        bool wasCompressed,
        ChatAttachment? model = null)
    {
        ImageSource? preview = null;
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            using var writer = new DataWriter(stream);
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
            stream.Seek(0);

            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            preview = bitmap;
        }
        catch (Exception ex)
        {
            App.Host?.Log.Error($"图片预览生成失败：{ex.GetType().Name}");
        }

        return new ChatAttachmentViewModel(
            bytes,
            displayName,
            mimeType,
            width,
            height,
            wasCompressed,
            model)
        {
            Preview = preview,
        };
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024d:0.#} KB";
        return $"{bytes / (1024d * 1024d):0.##} MB";
    }
}
