using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
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
        if (AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.IsAlwaysOnTop = true; // 始终置顶：随时弹出记录
        // 竖长形态与尺寸限制（Win32 WM_GETMINMAXINFO）
        WindowSizeLimits.Apply(WinRT.Interop.WindowNative.GetWindowHandle(this), 420, 600, 720, 1200);

        _saveTimer = DispatcherQueue.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(400);
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); Save(); };

        // 标题栏时钟：每秒刷新
        _clockTimer = DispatcherQueue.CreateTimer();
        _clockTimer.Interval = TimeSpan.FromSeconds(1);
        _clockTimer.Tick += (_, _) => TitleTimeText.Text = DateTime.Now.ToString("HH:mm:ss");
        _clockTimer.Start();

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
        IntPtr hIcon = NativeMethods.LoadImage(IntPtr.Zero, icoPath, 1 /* IMAGE_ICON */, 0, 0, 0x10 /* LR_LOADFROMFILE */);
        if (hIcon != IntPtr.Zero)
            AppWindow.SetIcon(new IconId((ulong)hIcon));
    }

    /// <summary>显示并聚焦。</summary>
    public void ShowAndFocus()
    {
        Activate();
        if (!_sized)
        {
            AppWindow.Resize(new SizeInt32(520, 780));
            _sized = true;
        }
    }

    /// <summary>复制内容到剪贴板并隐藏（热键再次按下）。</summary>
    public void CopyAndHide()
    {
        CopyToClipboard();
        AppWindow.Hide();
    }

    private void LoadSavedText()
    {
        try
        {
            if (File.Exists(_savePath))
                Editor.Text = File.ReadAllText(_savePath);
        }
        catch (Exception ex)
        {
            _log.Error($"记事本读取失败: {ex}");
        }
        UpdateStatus();
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateStatus();
        _saveTimer.Stop();
        _saveTimer.Start(); // 防抖保存
    }

    private void OnSelectionChanged(object sender, RoutedEventArgs e) => UpdateStatus();

    /// <summary>状态栏：光标行列 + 总字符数，并同步窗口标题。</summary>
    private void UpdateStatus()
    {
        string text = Editor.Text;
        int start = Math.Clamp(Editor.SelectionStart, 0, text.Length);
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
        if (e.Key == VirtualKey.S && IsCtrlDown())
        {
            e.Handled = true;
            _ = SaveAsAsync();
        }
    }

    private static bool IsCtrlDown()
        => InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);

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
            await Windows.Storage.FileIO.WriteTextAsync(file, Editor.Text);
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
            File.WriteAllText(_savePath, Editor.Text);
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
            var data = new DataPackage();
            data.SetText(Editor.Text);
            Clipboard.SetContent(data);
        }
        catch (Exception ex)
        {
            _log.Error($"剪贴板写入失败: {ex}");
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _clockTimer.Stop();
        _saveTimer.Stop();
        Save();
        CopyToClipboard();
    }
}
