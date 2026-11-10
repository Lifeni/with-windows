# Lua 脚本任务设计

> 设计日期：2026-11-10
> 状态：已实现，取代 2026-11-09 的 Python 定时任务方案

## 1. 目标

将原“定时 Python 任务”简化为应用内置的 Lua 脚本管理：

- 页面主要展示已添加脚本列表；
- 通过独立对话框添加或编辑脚本；
- 支持直接填写 Lua 代码或从 `.lua` 文件导入内容；
- 支持仅手动运行和单次定时执行；
- 支持立即运行、停止、超时和历史记录；
- 不长期关联外部脚本文件。

Lua 脚本可以通过标准库的 `os.execute` 或 `io.popen` 调用目标机器已安装的 Python、Node.js 文件。对应运行时不由 With Windows 安装。

## 2. 运行时选型

采用 KeraLua 1.4.11 和 Lua 5.4：

- Lua 5.4 比 MoonSharp 的 Lua 5.2 兼容性更完整；
- KeraLua 的 `runtimes/win-x64/native/lua54.dll` 可随应用发布；
- `lua54.dll` 只依赖 `KERNEL32.dll`，不要求用户额外安装 VC++ 运行库；
- win-x64 增量约为托管绑定 32 KiB、原生 DLL 523 KiB；
- 单文件发布必须设置 `IncludeNativeLibrariesForSelfExtract=true`，否则 `lua54.dll` 会留在 exe 外。

不采用 NLua：当前需求不需要 Lua 到 .NET 的反射桥，直接使用 KeraLua 可以缩小接口面。

## 3. 执行隔离

Lua 不在主 UI 进程中直接执行。父进程启动同一 exe 的隐藏 `--script-helper` 模式：

1. 父进程创建带 `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` 的 Job Object；
2. 启动 helper 并加入 Job；
3. 完成握手后才通知 helper 执行 Lua；
4. Lua 的 `os.execute`、`io.popen` 启动的 Python、Node.js 等子进程继续继承 Job；
5. 超时、手动停止或退出应用时终止完整进程树；
6. Lua 原生崩溃只影响 helper，不拖垮主应用。

helper 使用 UTF-8 处理 Lua 字符串和错误输出。Lua 仍不是安全沙箱，与当前用户权限相同。

## 4. 数据模型

任务只保存脚本正文，不保存 Python 解释器、工作目录或外部文件路径：

```json
{
  "version": 2,
  "tasks": [
    {
      "id": "d35ec245bf9c4cf1888649d29c84a966",
      "name": "整理下载目录",
      "schedule": {
        "kind": "manual",
        "oneTimeRunAt": null,
        "oneTimeConsumedAt": null
      },
      "scriptText": "print('hello from Lua')",
      "timeoutSeconds": 600,
      "createdAt": "2026-11-10T13:00:00+08:00",
      "updatedAt": "2026-11-10T13:00:00+08:00"
    }
  ]
}
```

`schedule.kind` 只允许 `manual` 和 `once`。单次任务保存绝对时间，触发后写入 `oneTimeConsumedAt`；手动运行不消费单次计划。

任务文档和历史文档版本升级为 2。旧版本空文档可以继续加载；当前功能尚未发布，不迁移旧 Python 任务。

## 5. 页面

主页面参考待办页和设置页：

- 顶部显示标题、脚本数量和“添加脚本”；
- 主体为脚本列表；
- 行内提供运行、停止、历史、编辑和删除；
- 运行中禁用编辑与删除；
- 空状态提示添加第一个脚本；
- 执行历史在独立只读对话框中查看。

编辑对话框：

- 脚本名称；
- 执行方式：仅手动运行、单次定时执行；
- 单次任务显示日期和时间；
- 导入 `.lua` 按钮；
- 多行 Lua 编辑区；
- 超时秒数；
- 验证错误固定在滚动区下方。

不提供解释器检测、外部脚本文件选择、工作目录、每天或每周计划。
不显示 Lua 版本或额外运行时说明。日期和时间采用待办提醒对话框的双列布局，两个选择器保持相同高度与宽度。

## 6. 测试

- 手动计划不产生调度时间；
- 未消费单次任务按绝对时间触发一次；
- 已消费、启动前过期和创建前时间不补跑；
- 单次任务改期会清除旧消费标记；
- Lua 脚本正文保存往返；
- LuaRunSpec 只接受绝对脚本路径和工作目录；
- 真实 Lua 输出 UTF-8 中文；
- `7 // 2` 等 Lua 5.4 语法可执行；
- `os.execute`、`io.popen` 可调用外部 Python、Node.js；
- 超时能终止 Lua 及外部子进程；
- 单文件发布能从 exe 内加载 `lua54.dll`；
- 列表页、编辑对话框和历史对话框的 AutomationId 可从 UI 树读取；
- 最终应用启动并保持运行。
