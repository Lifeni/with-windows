using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;
using WithWindows.Interop;

namespace WithWindows.Notepad;

/// <summary>
/// 自写记事本：无系统标题栏，自绘"标题栏 + 工具栏"单行（图标按钮）、等宽字体、
/// 始终置顶、明暗主题适配、位置记忆、实时自动保存（notepad.txt）、基础语法高亮（自动检测语言）、
/// 运行按钮（HTML 浏览器打开 / Python·JS 控制台）、Ctrl+滚轮缩放、Ctrl+S 保存。
/// </summary>
public sealed class NotepadWindow : Form
{
    private const string PositionKey = @"HKEY_CURRENT_USER\Software\WithWindows\Notepad";
    private const string ContentFileName = "notepad.txt";
    private const float BaseFontSize = 12f; // 编辑器字号

    private const string UndoFile = "undo.dat";
    private const int UndoLimit = 200;

    private readonly string _dataDir;
    private readonly TextBox _editor;
    private readonly System.Windows.Forms.Timer _saveTimer;
    private readonly Stack<string> _undoStack = new();
    private readonly Stack<string> _redoStack = new();
    private string _lastText = "";
    private bool _suppressHistory;
    private float _zoom = 1f;
    private readonly bool _dark;
    private readonly Color _textColor;
    private readonly Color _editorBg;

    public NotepadWindow(string dataDir)
    {
        _dataDir = dataDir;
        _dark = IsDarkMode();
        _editorBg = _dark ? Color.FromArgb(30, 30, 30) : Color.White;
        _textColor = _dark ? Color.FromArgb(212, 212, 212) : Color.FromArgb(27, 27, 27);

        Text = "快捷记事";
        Icon = IconLoader.Load(); // 使用应用图标（任务栏/Alt-Tab 显示）
        TopMost = true; // 始终置顶
        FormBorderStyle = FormBorderStyle.Sizable; // 完全原生窗口：缩放/动画/吸附全由系统处理
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(480, 320);
        Size = new Size(800, 533); // 默认窗口（1200×800 各缩 1/3 取整）
        BackColor = _editorBg;

        RestorePosition();

        // 编辑器：原生多行文本框，占满整个窗口；中文字体用 CJK 列表（避免宋体）
        _editor = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font(MakeMonoFamily(), BaseFontSize), // Maple Mono NF CN：等宽 + 中文覆盖
            Multiline = true,
            WordWrap = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = _editorBg,
            ForeColor = _textColor,
            BorderStyle = BorderStyle.None,
            HideSelection = true,
        };
        var editorHost = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10, 16, 10, 16), // 上下边距
            BackColor = _editorBg,
        };
        editorHost.Controls.Add(_editor);

        _saveTimer = new System.Windows.Forms.Timer { Interval = 300 };
        _editor.TextChanged += (_, _) => OnEditorTextChanged();
        _editor.Resize += (_, _) => UpdateScrollBar();
        // TextBox 无 SelectionChanged：光标移动用 MouseUp/KeyUp 刷新标题栏
        _editor.MouseUp += (_, _) => UpdateStatus();
        _editor.KeyUp += (_, _) => UpdateStatus();
        _editor.MouseWheel += OnEditorMouseWheel;

        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            PersistContent(); // 实时自动保存：停止输入约 0.3 秒后写入 notepad.txt
        };

        Controls.Add(editorHost);

        KeyPreview = true;
        KeyDown += OnFormKeyDown;
        FormClosing += (_, _) =>
        {
            SavePosition();
            PersistContent();
            SaveUndoHistory();
        };

        LoadContent();
    }

    /// <summary>暗色主题下让滚动条等系统元素跟随暗色（DWM 沉浸式暗色模式）；Win11 圆角。</summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int corner = NativeMethods.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        if (_dark)
        {
            int value = 1;
            NativeMethods.DwmSetWindowAttribute(Handle, 20, ref value, sizeof(int));
            // 滚动条暗色化（避免白色滚动条突兀）
            NativeMethods.SetWindowTheme(_editor.Handle, "DarkMode_Explorer", null);
        }
    }

    private static float DpiScaleAt(Point screenPoint)
    {
        IntPtr monitor = NativeMethods.MonitorFromPoint(screenPoint, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0)
            return dpiX / 96f;
        return 1f;
    }

    /// <summary>显示并聚焦（内容保持上次状态，可继续编辑）。</summary>
    public void ShowWindow()
    {
        Show();
        Activate();
        _editor.Focus();
    }

    /// <summary>隐藏并保存内容与位置（不清除文本）。</summary>
    public void HideWindow()
    {
        SavePosition();
        PersistContent();
        Hide();
    }

    /// <summary>复制全部内容到剪贴板（不清除文本）。</summary>
    public void CopyToClipboard()
    {
        if (_editor.TextLength == 0)
            return;
        try
        {
            Clipboard.SetText(_editor.Text);
        }
        catch
        {
            // 剪贴板被其他进程占用等：静默失败
        }
    }

    private void PersistContent()
    {
        try
        {
            Directory.CreateDirectory(_dataDir);
            File.WriteAllText(Path.Combine(_dataDir, ContentFileName), _editor.Text);
        }
        catch
        {
            // 保存失败不影响使用（下次仍会尝试）
        }
    }

    /// <summary>另存为：弹出对话框让用户选择保存位置，成功后按钮闪对勾。</summary>
    private void SaveToFile()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            FileName = "快捷记事.txt",
            Title = "另存为",
            DefaultExt = "txt",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            File.WriteAllText(dialog.FileName, _editor.Text);
        }
        catch
        {
            // 保存失败静默（下次仍会尝试）
        }
    }

    /// <summary>按钮短暂显示对勾（操作成功提示），随后恢复原图标。</summary>

    private void LoadContent()
    {
        try
        {
            string path = Path.Combine(_dataDir, ContentFileName);
            if (File.Exists(path))
                _editor.Text = File.ReadAllText(path);
        }
        catch
        {
            // 读取失败按空内容处理
        }
        // 加载后刷新标题栏并恢复撤销历史（不记录加载本身为一次撤销）
        _suppressHistory = true;
        UpdateStatus();
        UpdateScrollBar();
        _suppressHistory = false;
        _lastText = _editor.Text;
        LoadUndoHistory();
    }

    /// <summary>更新状态栏分段：行数/列数/字词数；选中时显示选中范围与字符数。</summary>
    /// <summary>更新标题栏：第 X 行 第 Y 列 · N 字符。</summary>
    private void UpdateStatus()
    {
        int caret = _editor.SelectionStart;
        string text = _editor.Text;
        int line = 1, lineStart = 0;
        for (int k = 0; k < caret && k < text.Length; k++)
        {
            if (text[k] == '\n')
            {
                line++;
                lineStart = k + 1;
            }
        }
        int col = caret - lineStart + 1;
        int sel = _editor.SelectionLength;
        Text = sel > 0
            ? $"快捷记事 — 第 {line} 行 第 {col} 列 · 已选 {sel} / {text.Length} 字符"
            : $"快捷记事 — 第 {line} 行 第 {col} 列 · {text.Length} 字符";
    }

    /// <summary>返回字符位置所在行号（1 起）。</summary>


    /// <summary>文本变化时记录撤销快照（变化前的文本）。</summary>
    private void OnEditorTextChanged()
    {
        if (!_suppressHistory)
        {
            _undoStack.Push(_lastText);
            if (_undoStack.Count > UndoLimit)
                _undoStack.Pop();
            _redoStack.Clear();
        }
        _lastText = _editor.Text;
        _saveTimer.Stop();
        _saveTimer.Start();
        UpdateStatus();
        UpdateScrollBar();
    }

    private void Undo()
    {
        if (_undoStack.Count == 0)
            return;
        _suppressHistory = true;
        _redoStack.Push(_editor.Text);
        _editor.Text = _undoStack.Pop();
        _suppressHistory = false;
        _lastText = _editor.Text;
        UpdateStatus();
        UpdateScrollBar();
    }

    private void Redo()
    {
        if (_redoStack.Count == 0)
            return;
        _suppressHistory = true;
        _undoStack.Push(_editor.Text);
        _editor.Text = _redoStack.Pop();
        _suppressHistory = false;
        _lastText = _editor.Text;
        UpdateStatus();
        UpdateScrollBar();
    }

    /// <summary>关闭时把撤销/恢复历史写入磁盘（跨窗口开关保留）。</summary>
    private void SaveUndoHistory()
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            foreach (var t in _undoStack.Reverse())
                sb.Append(t.Length).Append('\n').Append(t);
            sb.Append('|');
            foreach (var t in _redoStack.Reverse())
                sb.Append(t.Length).Append('\n').Append(t);
            File.WriteAllText(Path.Combine(_dataDir, UndoFile), sb.ToString());
        }
        catch
        {
            // 保存失败不影响使用
        }
    }

    /// <summary>打开时恢复撤销/恢复历史。</summary>
    private void LoadUndoHistory()
    {
        try
        {
            string path = Path.Combine(_dataDir, UndoFile);
            if (!File.Exists(path))
                return;
            string data = File.ReadAllText(path);
            int bar = data.IndexOf('|');
            string undoPart = bar < 0 ? data : data.Substring(0, bar);
            string redoPart = bar < 0 ? "" : data.Substring(bar + 1);
            var stack = _undoStack;
            foreach (string part in new[] { undoPart, redoPart })
            {
                int i = 0;
                while (i < part.Length)
                {
                    int nl = part.IndexOf('\n', i);
                    if (nl < 0 || !int.TryParse(part.Substring(i, nl - i), out int len))
                        break;
                    if (nl + 1 + len > part.Length)
                        break;
                    stack.Push(part.Substring(nl + 1, len));
                    i = nl + 1 + len;
                }
                stack = _redoStack;
            }
        }
        catch
        {
            // 恢复失败按空历史处理
        }
    }

    /// <summary>Ctrl + 鼠标滚轮缩放文本。</summary>
    private void OnEditorMouseWheel(object? sender, MouseEventArgs e)
    {
        if (!ModifierKeys.HasFlag(Keys.Control))
            return;
        ZoomText(e.Delta > 0 ? 1.1f : 1f / 1.1f);
        ((HandledMouseEventArgs)e).Handled = true;
    }

    /// <summary>缩放编辑器文本（70%～250%），缩放后重算滚动条。</summary>
    private void ZoomText(float factor)
    {
        _zoom = Math.Min(Math.Max(_zoom * factor, 0.7f), 2.5f);
        _editor.Font = new Font(MakeMonoFamily(), BaseFontSize * _zoom);
        UpdateScrollBar();
    }

    /// <summary>内容未超出可视高度时不显示滚动条（用最后一个字符的位置精确判断，含自动换行）。</summary>
    private void UpdateScrollBar()
    {
        if (_editor.IsDisposed)
            return;
        var sb = ScrollBars.None;
        if (_editor.TextLength > 0)
        {
            Point p = _editor.GetPositionFromCharIndex(_editor.TextLength - 1);
            if (p.Y + _editor.Font.Height > _editor.ClientSize.Height)
                sb = ScrollBars.Vertical;
        }
        if (_editor.ScrollBars != sb)
            _editor.ScrollBars = sb;
    }

    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.S)
        {
            SaveToFile();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.Control && e.KeyCode == Keys.Z && !e.Shift)
        {
            // 撤回（自定义历史，跨开关窗口保留）
            Undo();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if ((e.Control && e.KeyCode == Keys.Y) || (e.Control && e.KeyCode == Keys.Z && e.Shift))
        {
            // 恢复（Ctrl+Y 或 Ctrl+Shift+Z）
            Redo();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.Control && (e.KeyCode == Keys.Add || e.KeyCode == Keys.Oemplus))
        {
            // Ctrl + 加号：放大文本
            ZoomText(1.1f);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.Control && (e.KeyCode == Keys.Subtract || e.KeyCode == Keys.OemMinus))
        {
            // Ctrl + 减号：缩小文本
            ZoomText(1f / 1.1f);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.Control && (e.KeyCode == Keys.D0 || e.KeyCode == Keys.NumPad0))
        {
            // Ctrl + 0：重置缩放
            ZoomText(1f);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }



    /// <summary>中文字体回退：优先可用的现代中文字体，最后兜底微软雅黑。</summary>
    /// <summary>等宽字体：Maple Mono NF CN（含中文）→ Consolas → 通用等宽。</summary>
    private static string MakeMonoFamily()
    {
        var families = new HashSet<string>(FontFamily.Families.Select(f => f.Name));
        return families.Contains("Maple Mono NF CN") ? "Maple Mono NF CN"
            : families.Contains("Consolas") ? "Consolas"
            : FontFamily.GenericMonospace.Name;
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

    private void RestorePosition()
    {
        try
        {
            int x = ReadInt("X"), y = ReadInt("Y"), w = ReadInt("Width"), h = ReadInt("Height");
            if (w >= MinimumSize.Width && h >= MinimumSize.Height)
            {
                var wa = Screen.FromPoint(new Point(x, y)).WorkingArea;
                x = Math.Min(Math.Max(x, wa.Left), Math.Max(wa.Left, wa.Right - w));
                y = Math.Min(Math.Max(y, wa.Top), Math.Max(wa.Top, wa.Bottom - h));
                Location = new Point(x, y);
                Size = new Size(w, h);
                return;
            }
        }
        catch
        {
            // 注册表异常按默认位置处理
        }
        var area = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(area.Right - Width - 40, area.Top + 40);
    }

    private void SavePosition()
    {
        try
        {
            if (WindowState == FormWindowState.Normal)
            {
                Registry.SetValue(PositionKey, "X", Location.X.ToString(), RegistryValueKind.String);
                Registry.SetValue(PositionKey, "Y", Location.Y.ToString(), RegistryValueKind.String);
                Registry.SetValue(PositionKey, "Width", Size.Width.ToString(), RegistryValueKind.String);
                Registry.SetValue(PositionKey, "Height", Size.Height.ToString(), RegistryValueKind.String);
            }
        }
        catch
        {
            // 写入失败不影响使用
        }
    }

    private int ReadInt(string name)
    {
        object? value = Registry.GetValue(PositionKey, name, null);
        return value is string s && int.TryParse(s, out int result) ? result : 0;
    }

}