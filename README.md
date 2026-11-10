<picture>
  <source media="(prefers-color-scheme: dark)" srcset="assets/banner-dark.svg">
  <img src="assets/banner-light.svg" alt="With Windows">
</picture>

# With Windows

面向 Windows 11 的 WinUI 3 轻量托盘工具箱，支持 Windows 11 22H2 及以上版本，仅提供 x64 安装器与便携包。

<p>
  <img alt="版本" src="https://img.shields.io/github/v/release/Lifeni/with-windows?label=%E7%89%88%E6%9C%AC">
  <img alt="协议" src="https://img.shields.io/github/license/Lifeni/with-windows?label=%E5%8D%8F%E8%AE%AE">
</p>

> 本项目由 AI 协作完成：代码、文档与迭代均经 AI 生成和优化。

## 功能

- **记事**：自动保存、查找替换和状态栏。
- **待办**：完成状态、到期时间与 Windows 原生提醒。
- **AI 对话**：兼容 OpenAI 接口，支持流式回复和图片粘贴。
- **定时任务**：在应用运行期间按计划执行粘贴的 Python 代码或关联的 `.py` 文件，支持解释器选择、停止、超时和执行历史。
- **投影**：保存投屏模式并一键切换。

定时任务需要本机已安装 Python 3。关闭主窗口后应用继续驻留托盘并执行调度；从托盘退出应用后不再运行定时任务。Python 脚本以当前用户权限执行，不是沙箱。

## 截图

<table>
  <tr>
    <td width="50%"><img src="docs/images/notepad.png" width="100%" alt="记事页面"></td>
    <td width="50%"><img src="docs/images/todo.png" width="100%" alt="待办页面"></td>
  </tr>
  <tr>
    <td width="50%"><img src="docs/images/ai-chat.png" width="100%" alt="AI 对话页面"></td>
    <td width="50%"><img src="docs/images/projection.png" width="100%" alt="投影页面"></td>
  </tr>
</table>

## 许可证

本项目使用 [MIT 许可证](LICENSE)。
