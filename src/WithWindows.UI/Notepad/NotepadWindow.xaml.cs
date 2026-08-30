using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.Input;
using Windows.Graphics;
using Microsoft.UI.Text;
using Microsoft.Win32;
using Windows.System;
using Windows.UI.Core;
using WithWindows.Config;
using WithWindows.Core;
using WithWindows.Interop;

namespace WithWindows.Notepad;

/// <summary>
/// 快捷记事本：竖长置顶窗口。纯文本编辑（Ctrl+C/V 复制粘贴，Ctrl+S 另存为），
/// 隐藏/关闭时内容自动复制到剪贴板并保存到 notepad.txt；工具栏"设置"打开设置窗口。
/// </summary>
public sealed partial class NotepadWindow : Window
{
    private readonly string _savePath;
    private readonly Logger _log;
    private readonly ConfigStore _configStore;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _saveTimer;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _clockTimer;
    private const string PinRegistryPath = @"HKEY_CURRENT_USER\Software\WithWindows\Notepad";
    private const string PinRegistryValue = "Pinned";
    private const double MinFontSize = 10;
    private const double MaxFontSize = 32;
    private const double DefaultFontSize = 14;
    private OverlappedPresenter? _presenter;
    private bool _pinned; // 置顶状态（持久化注册表，窗口销毁重建也能恢复）
    private bool _sized;

    /// <summary>窗口当前是否可见（热键切换判断）。</summary>
    public new bool Visible => AppWindow.IsVisible;

    public NotepadWindow(string savePath, Logger log, ConfigStore configStore)
    {
        _savePath = savePath;
        _log = log;
        _configStore = configStore;
        InitializeComponent();

        SetupTitleBar();
        _presenter = AppWindow.Presenter as OverlappedPresenter; // 置顶由状态栏按钮控制
        _pinned = Registry.GetValue(PinRegistryPath, PinRegistryValue, 0) is int v && v != 0;

        _saveTimer = DispatcherQueue.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(400);
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); Save(); };

        // 标题栏时钟：每秒刷新
        _clockTimer = DispatcherQueue.CreateTimer();
        _clockTimer.Interval = TimeSpan.FromSeconds(1);
        _clockTimer.Tick += (_, _) => TitleTimeText.Text = DateTime.Now.ToString("HH:mm:ss");
        _clockTimer.Start();

        Editor.PointerWheelChanged += OnEditorWheel; // Ctrl+滚轮缩放字体
        // 行距加大：设置默认段落格式并写回，作用于已有与新内容
        var fmt = Editor.Document.GetDefaultParagraphFormat();
        fmt.SetLineSpacing(LineSpacingRule.Multiple, 1.05f);
        Editor.Document.SetDefaultParagraphFormat(fmt);
        LoadSavedText();
        Closed += OnClosed;
    }

    /// <summary>重绘标题栏并设置窗口图标：内容延伸到标题栏，系统按钮透明，背景随主题。</summary>
    private void SetupTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarElement);
        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;

        string icoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "with-windows.ico");
        // 显式 32x32 帧：避免系统默认尺寸取帧不一致导致拉伸/模糊
        IntPtr hIcon = NativeMethods.LoadImage(IntPtr.Zero, icoPath, 1 /* IMAGE_ICON */, 32, 32, 0x10 /* LR_LOADFROMFILE */);
        if (hIcon != IntPtr.Zero)
            AppWindow.SetIcon(new IconId((ulong)hIcon));
    }

    /// <summary>显示并聚焦。</summary>
    public void ShowAndFocus()
    {
        if (!_sized)
        {
            LoadWindowState(); // 先设尺寸/位置/字体再显示，避免闪跳
            _sized = true;
        }
        Activate();
        // 重开窗口恢复置顶状态（IsChecked 与视觉树可能被重置，用字段兜底）
        PinButton.IsChecked = _pinned;
        ApplyPinVisual();
    }


    /// <summary>恢复记忆的窗口尺寸/位置与字体大小。</summary>
    private void LoadWindowState()
    {
        try
        {
            var ws = _configStore.Load().WindowState;
            Editor.FontSize = Math.Clamp(ws.NotepadFontSize, MinFontSize, MaxFontSize);
            AppWindow.Resize(new SizeInt32((int)ws.NotepadWidth, (int)ws.NotepadHeight));
            if (ws.NotepadX != 0 || ws.NotepadY != 0)
                AppWindow.Move(new PointInt32((int)ws.NotepadX, (int)ws.NotepadY));
        }
        catch (Exception ex)
        {
            _log.Error($"窗口状态恢复失败: {ex}");
        }
    }

    /// <summary>保存窗口尺寸/位置与字体大小（关闭或隐藏时）。</summary>
    private void SaveWindowState()
    {
        try
        {
            var config = _configStore.Load();
            var ws = config.WindowState;
            ws.NotepadFontSize = Editor.FontSize;
            ws.NotepadWidth = AppWindow.Size.Width;
            ws.NotepadHeight = AppWindow.Size.Height;
            var pos = AppWindow.Position;
            ws.NotepadX = pos.X;
            ws.NotepadY = pos.Y;
            _configStore.Save(config);
        }
        catch (Exception ex)
        {
            _log.Error($"窗口状态保存失败: {ex}");
        }
    }

    /// <summary>复制内容到剪贴板并隐藏（热键再次按下）。</summary>
    public void CopyAndHide()
    {
        CopyToClipboard();
        SaveWindowState();
        AppWindow.Hide();
    }

    private void LoadSavedText()
    {
        try
        {
            if (File.Exists(_savePath))
                Editor.Document.SetText(TextSetOptions.None, File.ReadAllText(_savePath));
        }
        catch (Exception ex)
        {
            _log.Error($"记事本读取失败: {ex}");
        }
        // 对全部文本应用行距（加载后段落格式重置为默认）
        var fmt = Editor.Document.GetDefaultParagraphFormat();
        fmt.SetLineSpacing(LineSpacingRule.Multiple, 1.05f);
        var selection = Editor.Document.Selection;
        selection.SetRange(0, int.MaxValue);
        selection.ParagraphFormat.SetLineSpacing(LineSpacingRule.Multiple, 1.05f);
        selection.SetRange(0, 0);
        UpdateStatus();
    }

    private void OnTextChanged(object sender, RoutedEventArgs e)
    {
        UpdateStatus();
        _saveTimer.Stop();
        _saveTimer.Start(); // 防抖保存
    }

    private void OnSelectionChanged(object sender, RoutedEventArgs e) => UpdateStatus();

    /// <summary>状态栏：光标行列 + 总字符数，并同步窗口标题。</summary>
    private void UpdateStatus()
    {
        Editor.Document.GetText(TextGetOptions.None, out string text);
        int start = Math.Clamp(Editor.Document.Selection.StartPosition, 0, text.Length);
        int line = 1, column = 1;
        for (int i = 0; i < start; i++)
        {
            if (text[i] == '\n') { line++; column = 1; }
            else column++;
        }
        StatusText.Text = $"行 {line}，列 {column}　·　共 {text.Length} 字符";
        string title = $"快捷记事（{text.Length} 字符）";
        Title = title;
        AppWindow.Title = title; // 自绘标题栏下 Window.Title 不同步 Win32 文本，需显式设置
    }

    // ---- 快捷键：Ctrl+S 另存为（Ctrl+C/V 由 TextBox 原生支持） ----

    private void OnEditorKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!IsCtrlDown()) return;

        if (e.Key == VirtualKey.S)
        {
            e.Handled = true;
            _ = SaveAsAsync();
        }
        else if ((uint)e.Key == 0xBB || e.Key == VirtualKey.Add) // Ctrl+加号 / Ctrl+=（0xBB = 等号键）
        {
            e.Handled = true;
            ZoomFont(1);
        }
        else if ((uint)e.Key == 0xBD || e.Key == VirtualKey.Subtract) // Ctrl+减号 / Ctrl+-（0xBD = 减号键）
        {
            e.Handled = true;
            ZoomFont(-1);
        }
        else if (e.Key == VirtualKey.Number0) // Ctrl+0 重置
        {
            e.Handled = true;
            Editor.FontSize = DefaultFontSize;
        }
    }

    /// <summary>Ctrl+滚轮缩放字体。</summary>
    private void OnEditorWheel(object sender, PointerRoutedEventArgs e)
    {
        if (!IsCtrlDown()) return;
        int delta = e.GetCurrentPoint(Editor).Properties.MouseWheelDelta;
        ZoomFont(delta > 0 ? 1 : -1);
        e.Handled = true;
    }

    private void ZoomFont(double step)
        => Editor.FontSize = Math.Clamp(Editor.FontSize + step, MinFontSize, MaxFontSize);

    private static bool IsCtrlDown()
        => InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);

    // ---- 置顶开关 ----

    private void OnPinToggle(object sender, RoutedEventArgs e)
    {
        _pinned = PinButton.IsChecked == true;
        if (_presenter is not null)
            _presenter.IsAlwaysOnTop = _pinned;
        Registry.SetValue(PinRegistryPath, PinRegistryValue, _pinned ? 1 : 0, RegistryValueKind.DWord);
        ApplyPinVisual();
    }

    /// <summary>按置顶状态同步按钮视觉（初始/重开窗口时也需应用，避免灰色默认态）。</summary>
    private void ApplyPinVisual()
    {
        // 未选中：无边框无背景；选中：加背景与边框（图标不变）
        PinButton.Background = _pinned
            ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
            : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        PinButton.BorderThickness = _pinned ? new Thickness(1) : new Thickness(0);
    }

    // ---- 另存为（Ctrl+S） ----

    /// <summary>另存为。</summary>
    private async Task SaveAsAsync()
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            picker.FileTypeChoices.Add("文本文件", new List<string> { ".txt" });
            picker.SuggestedFileName = $"记事本-{DateTime.Now:yyyyMMdd-HHmmss}";

            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            Editor.Document.GetText(TextGetOptions.None, out string text);
            await Windows.Storage.FileIO.WriteTextAsync(file, text);
            _log.Info($"[notepad] 已另存为: {file.Path}");
        }
        catch (Exception ex)
        {
            _log.Error($"另存为失败: {ex}");
        }
    }

    // ---- 保存与剪贴板 ----

    private void Save()
    {
        try
        {
            Editor.Document.GetText(TextGetOptions.None, out string text);
            File.WriteAllText(_savePath, text);
        }
        catch (Exception ex)
        {
            _log.Error($"记事本保存失败: {ex}");
        }
    }

    private void CopyToClipboard()
    {
        try
        {
            Editor.Document.GetText(TextGetOptions.None, out string text);
            var data = new DataPackage();
            data.SetText(text);
            Clipboard.SetContent(data);
        }
        catch (Exception ex)
        {
            _log.Error($"剪贴板写入失败: {ex}");
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        // 应用退出：允许关闭；用户点 X：拦截为最小化到托盘（窗口常驻）
        if (App.IsExiting)
        {
            _clockTimer.Stop();
            _saveTimer.Stop();
            Save();
            CopyToClipboard();
            SaveWindowState();
            return;
        }
        args.Handled = true;
        Save();
        CopyToClipboard();
        SaveWindowState();
        AppWindow.Hide();
    }
}
