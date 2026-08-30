using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

// 生成应用图标:深色圆角方块背景 + 金色闪电(一键触发)。
// 用法:dotnet run --project scripts/IconGen -- <输出.ico>  (默认 src/WithWindows/Assets/with-windows.ico)

int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
string output = args.Length > 0
    ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "WithWindows.UI", "Assets", "with-windows.ico");

// 基准 256x256 绘制
using var canvas = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
using (var g = Graphics.FromImage(canvas))
{
    g.SmoothingMode = SmoothingMode.AntiAlias;

    using var bgPath = RoundedRect(0, 0, 256, 256, 56);
    // 亮蓝渐变背景 + 白色闪电：深浅任务栏下均高对比、清晰
    using var bgBrush = new LinearGradientBrush(
        new Rectangle(0, 0, 256, 256),
        Color.FromArgb(0xFF, 0x1E, 0x88, 0xE5),
        Color.FromArgb(0xFF, 0x0F, 0x54, 0xA0),
        45f);
    g.FillPath(bgBrush, bgPath);

    // 闪电多边形(居中,尖端朝下)
    var bolt = new PointF[]
    {
        new(154, 20),
        new(64, 138),
        new(118, 138),
        new(98, 236),
        new(192, 102),
        new(134, 102),
    };
    using var boltBrush = new SolidBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
    g.FillPolygon(boltBrush, bolt);
}

// 各尺寸 PNG
var images = new List<(int Size, byte[] Png)>();
foreach (int size in sizes)
{
    using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(bmp))
    {
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(canvas, 0, 0, size, size);
    }
    using var ms = new MemoryStream();
    bmp.Save(ms, ImageFormat.Png);
    images.Add((size, ms.ToArray()));
}

// 组装 ICO(Vista+ 支持 PNG 条目)
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
using (var fs = File.Create(output))
using (var bw = new BinaryWriter(fs))
{
    bw.Write((ushort)0);                    // reserved
    bw.Write((ushort)1);                    // type: icon
    bw.Write((ushort)images.Count);
    int offset = 6 + 16 * images.Count;
    foreach (var (size, png) in images)
    {
        bw.Write((byte)(size >= 256 ? 0 : size));  // width(256 记 0)
        bw.Write((byte)(size >= 256 ? 0 : size));  // height
        bw.Write((byte)0);                         // colors
        bw.Write((byte)0);                         // reserved
        bw.Write((ushort)1);                       // planes
        bw.Write((ushort)32);                      // bitcount
        bw.Write((uint)png.Length);
        bw.Write((uint)offset);
        offset += png.Length;
    }
    foreach (var (_, png) in images)
        bw.Write(png);
}

Console.WriteLine($"已生成 {output} ({images.Count} 个尺寸)");

static GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
{
    var path = new GraphicsPath();
    float d = r * 2;
    path.AddArc(x, y, d, d, 180, 90);
    path.AddArc(x + w - d, y, d, d, 270, 90);
    path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
    path.AddArc(x, y + h - d, d, d, 90, 90);
    path.CloseFigure();
    return path;
}
