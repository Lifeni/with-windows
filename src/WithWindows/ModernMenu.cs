using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using WithWindows.Interop;

namespace WithWindows;

/// <summary>Win11 风格菜单项。IconGlyph 为 Segoe Fluent Icons 字形；Checkable 项每次弹出时求值 IsChecked；
/// Shortcut 为右侧灰字快捷键提示（如 "F13"），由配置热键回填。</summary>
public sealed class ModernMenuItem
{
    public required string Text { get; init; }
    public string? IconGlyph { get; init; }
    public bool Checkable { get; init; }
    public Func<bool>? IsChecked { get; init; }
    public Action? OnClick { get; init; }
    public bool IsSeparator { get; init; }
    public string? Shortcut { get; set; }
}

/// <summary>
/// Win11 风格托盘菜单：无边框自绘弹窗。DWM 圆角 + 实色主题卡片（双缓冲消除悬停闪烁），
/// 亮/暗色随系统主题，Segoe Fluent Icons 图标 + 勾选位。点击外部关闭：Deactivate 主路径 +
/// WH_MOUSE_LL 低级鼠标钩子兜底（激活失败时也可靠）。零外部依赖，与项目单 exe 约束一致。
/// </summary>
public sealed class ModernMenu : IDisposable
{
    private readonly List<ModernMenuItem> _items = new();
    private MenuForm? _form;

    public void Add(ModernMenuItem item) => _items.Add(item);

    public void AddSeparator() => _items.Add(new ModernMenuItem { Text = "", IsSeparator = true });

    public void ShowAt(Point screenLocation)
    {
        if (_items.Count == 0)
            return;
        CloseCurrent();
        _form = new MenuForm(_items);
        _form.ShowAt(screenLocation);
    }

    public void CloseCurrent()
    {
        var form = _form;
        _form = null;
        form?.Close();
    }

    public void Dispose() => CloseCurrent();
}

internal sealed class MenuForm : Form
{
    // 96DPI 基准尺寸，实际按目标显示器 DPI 缩放。
    // 上下/左右边距统一（PadY=PadX），四周等距；文字与图标离边距留有余量
    private const int RowHeight = 32;
    private const int SeparatorRow = 10;
    private const int PadY = 8;
    private const int PadX = 8;
    private const int CheckCol = 28;
    private const int IconCol = 28;
    private const float CornerRadius = 6f;
    private const float MinWidth = 110f;  // 逻辑像素地板：低于实际内容，宽度跟随内容而非地板
    private const float MaxWidth = 340f;
    // 光学补偿（逻辑 px @96dpi）：CJK 字形墨迹不占满行盒底部（如"退出"无下伸笔画），
    // 首/末选项文字到边框的视觉间距差 ~2.6px，整体下移一半使上下等距
    private const float TextShift = 1.3f;

    private readonly List<ModernMenuItem> _items;
    private readonly bool _dark;
    private readonly Color _tint;
    private readonly Color _textColor;
    private readonly Color _hoverColor;
    private readonly Color _separatorColor;
    private readonly Color _borderColor;
    private readonly NativeMethods.HookProc _hookProc;
    private IntPtr _mouseHook;
    private int _hoverIndex = -1;
    private float _dpiScale = 1f;      // ShowAt 时按目标显示器采样
    private Font? _textFont;           // 像素单位，ShowAt 时按 _dpiScale 创建
    private Font? _glyphFont;
    private SolidBrush? _textBrush;
    private SolidBrush? _iconBrush;
    private SolidBrush? _hintBrush;

    public MenuForm(IReadOnlyList<ModernMenuItem> items)
    {
        _items = items.ToList();
        _dark = IsDarkMode();
        _tint = _dark ? Color.FromArgb(0x28, 0x28, 0x28) : Color.FromArgb(0xF3, 0xF3, 0xF3);
        _textColor = _dark ? Color.White : Color.FromArgb(0x1B, 0x1B, 0x1B);
        _hoverColor = _dark ? Color.FromArgb(0x3D, 0x3D, 0x3D) : Color.FromArgb(0xE5, 0xE5, 0xE5);
        _separatorColor = _dark ? Color.FromArgb(0x3A, 0x3A, 0x3A) : Color.FromArgb(0xE1, 0xE1, 0xE1);
        _borderColor = _dark ? Color.FromArgb(0x5A, 0x5A, 0x5A) : Color.FromArgb(0xCB, 0xCB, 0xCB);

        _textFont = null;
        _glyphFont = null;
        _hookProc = OnMouseHook;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        KeyPreview = true;
        ShowIcon = false;
        MinimizeBox = false;
        MaximizeBox = false;
        ControlBox = false;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = _tint;
        Text = "With Windows";
        Font = _textFont;
        DoubleBuffered = true; // 缓冲整帧重绘，消除悬停切换时的文字闪烁

        Deactivate += (_, _) => Close();
        FormClosed += (_, _) => UnhookMouse();
        KeyDown += OnKeyDown;
        MouseMove += (_, e) => UpdateHover(e.Location);
        MouseLeave += (_, _) =>
        {
            if (_hoverIndex >= 0) { _hoverIndex = -1; Invalidate(); }
        };
        MouseUp += OnMouseUp;
    }

    private float RowHeightOf(ModernMenuItem item) => item.IsSeparator ? SeparatorRow : RowHeight;

    /// <summary>目标显示器（弹出点所在）的缩放系数，ShowAt 时固定采样。</summary>
    private static float DpiScaleAt(Point screenPoint)
    {
        IntPtr monitor = NativeMethods.MonitorFromPoint(screenPoint, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0)
            return dpiX / 96f;
        return 1f;
    }

    public void ShowAt(Point screenLocation)
    {
        _ = Handle; // 先创建句柄，后续 Show/圆角设置需要
        _dpiScale = DpiScaleAt(screenLocation);

        // 像素单位字体：尺寸已含缩放，测量/绘制与图形 DPI 无关，杜绝跨显示器测量漂移
        _textFont = MakeFont("Segoe UI Variable Text", "Segoe UI", 12f * _dpiScale);
        _glyphFont = MakeFont("Segoe Fluent Icons", "Segoe MDL2 Assets", 15f * _dpiScale);
        _textBrush = new SolidBrush(_textColor);
        _iconBrush = new SolidBrush(_textColor);
        _hintBrush = new SolidBrush(_dark ? Color.FromArgb(0x9A, 0x9A, 0x9A) : Color.FromArgb(0x70, 0x70, 0x70));

        float f = _dpiScale;
        float width = MeasureWidth(f);
        width = Math.Min(Math.Max(width, MinWidth * f), MaxWidth * f);

        float height = PadY * f;
        foreach (var item in _items)
            height += RowHeightOf(item) * f;
        height += PadY * f;

        ClientSize = new Size((int)Math.Ceiling(width), (int)Math.Ceiling(height));

        var wa = Screen.FromPoint(screenLocation).WorkingArea;
        int x = Math.Min(screenLocation.X, wa.Right - Width);
        int y = Math.Min(screenLocation.Y, wa.Bottom - Height);
        x = Math.Max(x, wa.Left);
        y = Math.Max(y, wa.Top);
        Location = new Point(x, y);

        ApplyDwmRounding();
        Show();
        Activate();
        InstallMouseHook();
    }

    private float MeasureWidth(float f)
    {
        using var g = CreateGraphics();
        float width = 0f;
        foreach (var item in _items)
        {
            if (item.IsSeparator)
                continue;
            float textW = g.MeasureString(item.Text, _textFont, 10000).Width;
            float itemW = 2 * PadX * f
                + (item.Checkable ? CheckCol * f : 0f)
                + (string.IsNullOrEmpty(item.IconGlyph) ? 0f : IconCol * f)
                + textW;
            // 右侧快捷键提示占位（文本 + 12px 间隔）
            if (!string.IsNullOrEmpty(item.Shortcut))
            {
                float hintW = g.MeasureString(item.Shortcut, _textFont, 10000).Width;
                itemW += hintW + 12f * f;
            }
            width = Math.Max(width, itemW);
        }
        return width;
    }

    private void ApplyDwmRounding()
    {
        // Win11 圆角（Win10 下失败则保持直角，可接受）
        int corner = NativeMethods.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // 实色卡片：整帧不透明绘制（配合双缓冲），半透明/亚克力已在自绘弹窗中移除（其整窗重绘是闪烁根源）
        e.Graphics.Clear(_tint);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float f = _dpiScale;
        float y = PadY * f;
        var format = new StringFormat
        {
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap, // 文本永不换行，超宽时裁切而非折行
        };

        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            float rowH = RowHeightOf(item) * f;

            if (item.IsSeparator)
            {
                // 分割线缩进与左右边距一致
                using var pen = new Pen(_separatorColor, 1f);
                g.DrawLine(pen, PadX * f, y + rowH / 2f, Width - PadX * f, y + rowH / 2f);
                y += rowH;
                continue;
            }

            if (i == _hoverIndex)
            {
                using var path = RoundedRect(PadX * f, y, Width - 2 * PadX * f, rowH, 5f * f);
                using var brush = new SolidBrush(_hoverColor);
                g.FillPath(brush, path);
            }

            float x = PadX * f;
            float cy = y + TextShift * f; // 光学补偿：内容整体下移，拉平首/末项到边框的视觉间距
            if (item.Checkable)
            {
                if (item.IsChecked?.Invoke() == true)
                {
                    var check = g.MeasureString(CheckGlyph, _glyphFont);
                    g.DrawString(CheckGlyph, _glyphFont, _textBrush,
                        x + (CheckCol * f - check.Width) / 2f, cy + (rowH - check.Height) / 2f);
                }
                x += CheckCol * f;
            }

            if (!string.IsNullOrEmpty(item.IconGlyph))
            {
                var glyph = g.MeasureString(item.IconGlyph, _glyphFont);
                g.DrawString(item.IconGlyph, _glyphFont, _iconBrush,
                    x + (IconCol * f - glyph.Width) / 2f, cy + (rowH - glyph.Height) / 2f);
                x += IconCol * f;
            }

            // 右侧快捷键提示占位（如 "F13"），文本右对齐灰字
            float textRight = Width - PadX * f;
            if (!string.IsNullOrEmpty(item.Shortcut))
            {
                var hintSize = g.MeasureString(item.Shortcut, _textFont);
                float hintX = Width - PadX * f - hintSize.Width;
                g.DrawString(item.Shortcut, _textFont, _hintBrush,
                    new RectangleF(hintX, cy, hintSize.Width, rowH), format);
                textRight = hintX - 12f * f;
            }

            g.DrawString(item.Text, _textFont, _textBrush,
                new RectangleF(x, cy, textRight - x, rowH), format);
            y += rowH;
        }

        // 1px 圆角描边（实色卡片下始终绘制，增加层次）
        using (var border = RoundedRect(0.5f, 0.5f, Width - 1f, Height - 1f, CornerRadius))
        using (var pen = new Pen(_borderColor, 1f))
        {
            g.DrawPath(pen, border);
        }
        format.Dispose();
    }

    private const string CheckGlyph = "\uE73E"; // CheckMark

    private int HitTest(Point p)
    {
        float f = _dpiScale;
        float y = PadY * f;
        for (int i = 0; i < _items.Count; i++)
        {
            float rowH = RowHeightOf(_items[i]) * f;
            if (p.Y >= y && p.Y < y + rowH)
                return i;
            y += rowH;
        }
        return -1;
    }

    private void UpdateHover(Point p)
    {
        int idx = HitTest(p);
        if (idx != _hoverIndex)
        {
            _hoverIndex = idx;
            Invalidate();
        }
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        int idx = HitTest(e.Location);
        if (idx < 0 || _items[idx].IsSeparator)
        {
            Close();
            return;
        }
        var handler = _items[idx].OnClick;
        Close();
        handler?.Invoke();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Escape:
                Close();
                break;
            case Keys.Down:
                MoveHover(1);
                break;
            case Keys.Up:
                MoveHover(-1);
                break;
            case Keys.Enter:
                if (_hoverIndex >= 0 && !_items[_hoverIndex].IsSeparator)
                    OnMouseUp(this, new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                break;
        }
    }

    private void MoveHover(int delta)
    {
        int n = _items.Count;
        int next = _hoverIndex;
        for (int step = 0; step < n; step++)
        {
            next = (next + delta + n) % n;
            if (!_items[next].IsSeparator)
            {
                _hoverIndex = next;
                Invalidate();
                return;
            }
        }
    }

    private void InstallMouseHook()
    {
        // 激活失败时的兜底：低级鼠标钩子检测外部点击（线程上下文为本 UI 线程消息循环）
        _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _hookProc, IntPtr.Zero, 0);
    }

    private void UnhookMouse()
    {
        if (_mouseHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
    }

    private IntPtr OnMouseHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            if (msg == NativeMethods.WM_LBUTTONDOWN || msg == NativeMethods.WM_RBUTTONDOWN || msg == NativeMethods.WM_MBUTTONDOWN)
            {
                if (NativeMethods.GetCursorPos(out Point p) && !Bounds.Contains(p) && !IsDisposed)
                    BeginInvoke(Close);
            }
        }
        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private static GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
    {
        var path = new GraphicsPath();
        r = Math.Min(r, Math.Min(w, h) / 2f);
        path.AddArc(x, y, 2 * r, 2 * r, 180, 90);
        path.AddArc(x + w - 2 * r, y, 2 * r, 2 * r, 270, 90);
        path.AddArc(x + w - 2 * r, y + h - 2 * r, 2 * r, 2 * r, 0, 90);
        path.AddArc(x, y + h - 2 * r, 2 * r, 2 * r, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static bool IsDarkMode()
    {
        try
        {
            return Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1) is int i && i == 0;
        }
        catch
        {
            return false;
        }
    }

    private static Font MakeFont(string preferred, string fallback, float sizePx)
    {
        var names = new HashSet<string>(FontFamily.Families.Select(f => f.Name));
        string family = names.Contains(preferred) ? preferred
            : names.Contains(fallback) ? fallback
            : FontFamily.GenericSansSerif.Name;
        return new Font(family, sizePx, FontStyle.Regular, GraphicsUnit.Pixel);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _textFont?.Dispose();
            _glyphFont?.Dispose();
            _textBrush?.Dispose();
            _iconBrush?.Dispose();
            _hintBrush?.Dispose();
        }
        base.Dispose(disposing);
    }
}
