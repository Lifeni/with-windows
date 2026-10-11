using System;
using System.IO;
using Microsoft.UI.Dispatching;
using WithWindows.Core.Diagnostics;

namespace WithWindows.Services;

/// <summary>
/// 记事本草稿持久化：写入 %APPDATA%\WithWindows\notepad.txt。
/// 编辑期间按防抖写盘（默认 500 ms），避免每次按键都落盘。
/// </summary>
public sealed class NotepadDraft : IDisposable
{
    private readonly DispatcherQueueTimer _debounce;
    private string _pending = "";
    private bool _dirty;

    public NotepadDraft(DispatcherQueue queue, TimeSpan? debounce = null)
    {
        _debounce = queue.CreateTimer();
        _debounce.Interval = debounce ?? TimeSpan.FromMilliseconds(500);
        _debounce.IsRepeating = false;
        _debounce.Tick += (_, _) => Flush();
    }

    /// <summary>读取草稿；不存在或读取失败时返回空串（不阻断打开）。</summary>
    public static string Load()
    {
        try
        {
            string path = AppPaths.NotepadDraftPath;
            return File.Exists(path) ? File.ReadAllText(path) : "";
        }
        catch (IOException)
        {
            return "";
        }
        catch (UnauthorizedAccessException)
        {
            return "";
        }
    }

    /// <summary>排入一次写盘（防抖）。</summary>
    public void QueueSave(string text)
    {
        _pending = text;
        _dirty = true;
        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>立即写盘；没有待写内容时什么都不做。失败只吞掉——草稿属于尽力而为。</summary>
    public void Flush()
    {
        if (!_dirty)
            return;

        _debounce.Stop();
        try
        {
            Directory.CreateDirectory(AppPaths.DataRoot);
            File.WriteAllText(AppPaths.NotepadDraftPath, _pending);
            _dirty = false;
        }
        catch (IOException)
        {
            // 保持 _dirty，下一次编辑再试
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void Dispose() => Flush();
}
