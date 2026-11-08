using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

// 从 Inkscape 导出的 256×256 PNG 打包多尺寸 ICO。
// 用法：dotnet run --project scripts/IconGen -- <输入.png> [输出.ico]

if (args.Length < 1)
{
    Console.Error.WriteLine("用法：dotnet run --project scripts/IconGen -- <输入.png> [输出.ico]");
    return 1;
}

string input = Path.GetFullPath(args[0]);
if (!File.Exists(input))
{
    Console.Error.WriteLine($"找不到输入 PNG：{input}");
    return 1;
}

string output = args.Length > 1
    ? Path.GetFullPath(args[1])
    : Path.Combine(
        Directory.GetCurrentDirectory(),
        "src",
        "WithWindows.App",
        "Assets",
        "with-windows.ico"
    );

int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
using var source = Image.FromFile(input);
var images = new List<(int Size, byte[] Data, bool IsPng)>();

foreach (int size in sizes)
{
    using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using (var graphics = Graphics.FromImage(bitmap))
    {
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(source, 0, 0, size, size);
    }

    if (size <= 48)
    {
        images.Add((size, ToDib(bitmap), false));
    }
    else
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        images.Add((size, stream.ToArray(), true));
    }
}

Directory.CreateDirectory(Path.GetDirectoryName(output)!);
using var file = File.Create(output);
using var writer = new BinaryWriter(file);
writer.Write((ushort)0);
writer.Write((ushort)1);
writer.Write((ushort)images.Count);

int offset = 6 + 16 * images.Count;
foreach (var (size, data, _) in images)
{
    writer.Write((byte)(size >= 256 ? 0 : size));
    writer.Write((byte)(size >= 256 ? 0 : size));
    writer.Write((byte)0);
    writer.Write((byte)0);
    writer.Write((ushort)1);
    writer.Write((ushort)32);
    writer.Write((uint)data.Length);
    writer.Write((uint)offset);
    offset += data.Length;
}

foreach (var (_, data, _) in images)
{
    writer.Write(data);
}

Console.WriteLine($"已生成 {output}（{images.Count} 个尺寸）");
return 0;

static byte[] ToDib(Bitmap bitmap)
{
    int width = bitmap.Width;
    int height = bitmap.Height;
    var header = new byte[40];
    BitConverter.GetBytes(40).CopyTo(header, 0);
    BitConverter.GetBytes(width).CopyTo(header, 4);
    BitConverter.GetBytes(height * 2).CopyTo(header, 8);
    BitConverter.GetBytes((short)1).CopyTo(header, 12);
    BitConverter.GetBytes((short)32).CopyTo(header, 14);

    var pixels = new byte[width * height * 4];
    for (int y = 0; y < height; y++)
    {
        for (int x = 0; x < width; x++)
        {
            var color = bitmap.GetPixel(x, height - 1 - y);
            int index = (y * width + x) * 4;
            pixels[index] = color.B;
            pixels[index + 1] = color.G;
            pixels[index + 2] = color.R;
            pixels[index + 3] = color.A;
        }
    }

    var mask = new byte[(width + 7) / 8 * height];
    return header.Concat(pixels).Concat(mask).ToArray();
}
