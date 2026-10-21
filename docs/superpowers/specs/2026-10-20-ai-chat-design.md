# With Windows · AI 对话第一阶段设计

- 日期：2026-10-20
- 状态：已评审
- 范围：OpenAI 兼容协议的最小可用闭环
- 依赖决策：只新增 `Markdig`，不引入 OpenAI SDK、MVVM、Semantic Kernel 或 Markdown 控件库

> **2026-10-21 实现扩展**：第一阶段在文本对话之外增加了剪贴板图片。支持位图截图和 PNG / JPEG / WebP / GIF 图片，每条消息最多 4 张；最终文件最大 1 MiB，超限时自动缩放并优先无损压缩，仍超限再使用高质量 JPEG。附件独立存放在 `chat/attachments/`，新建会话时清理。普通文件、Responses API 和自动上下文压缩仍不在本阶段。

## 1. 目标

新增一个独立的「AI 对话」工具页，通过用户配置的 OpenAI 兼容端点完成流式多轮文本对话，并完成以下闭环：

- 配置 Base URL、模型、系统提示和 API Key。
- 发送消息并以流式方式显示回复。
- 保存当前会话，在应用重启后恢复。
- 支持停止生成、清空会话和复制消息。
- 回复完成后使用 Markdown 渲染常见内容。
- 在没有配置或发生网络、认证、限流错误时给出可执行的界面提示。

第一阶段不包含：

- 普通文件上传。
- OpenAI Responses API。
- 自动上下文压缩。
- 多会话列表、搜索和导出。
- 工具调用、函数调用、图片生成和语音。
- 对非 OpenAI 兼容协议的适配。

## 2. 技术选型

| 问题 | 选择 | 理由 |
|---|---|---|
| 协议客户端 | `HttpClient` + `System.Net.ServerSentEvents` | OpenAI 兼容协议本质是 HTTP、JSON 与 SSE，不依赖官方 SDK |
| 协议模式 | Chat Completions | 第三方兼容端点覆盖最广 |
| Markdown 解析 | `Markdig` | 成熟、活跃、无 WinUI 依赖，不绑定具体渲染库 |
| Markdown 渲染 | 自建轻量原生控件 | 避免 `WinUI.Markdown` 的预发布状态和 Windows App SDK 元包依赖 |
| AI 抽象 | 应用内 `IChatCompletionService` | 只抽象本应用需要的流式增量，不引入过大的通用 AI 框架 |
| 密钥保护 | Windows `DataProtectionProvider` 的 `LOCAL=user` | 使用系统 DPAPI 绑定当前用户，不把明文密钥写入配置 |
| 密钥访问 | `Windows.Security.Cryptography.DataProtection` | 使用官方 WinRT 封装，不手写加密或 P/Invoke |
| 会话存储 | JSON 文件、原子替换 | 与会话规模匹配，不引入数据库 |
| 状态管理 | code-behind + 普通可观察会话模型 | 当前只有一个页面共享聊天状态，暂不引入 MVVM 库 |

新增包版本写入 `Directory.Packages.props`：

| 包 | 版本 |
|---|---|
| `Markdig` | `1.4.0` |

`Markdig` 需要同步加入 `AGENTS.md` 的第三方依赖白名单，并且只由 `WithWindows.App` 引用。`HttpClient`、`System.Text.Json` 和 `System.Net.ServerSentEvents` 属于 .NET 10 自带能力，不扩展白名单。

## 3. 架构

```text
ChatPage
  ├─ 会话与控件状态
  ├─ 配置对话框
  └─ MarkdownMessageView
          │
          ▼
ChatSession / ChatHistoryStore
          │
          ▼
IChatCompletionService
          │
          ▼
OpenAiCompatibleChatService
          │
          ▼
用户配置的 OpenAI 兼容端点
```

### 3.1 Core

新增目录 `src/WithWindows.Core/Chat/`：

- `ChatMessage`：角色与文本内容。
- `ChatSession`：当前会话的有序消息。
- `ChatProviderConfig`：Base URL、模型、系统提示。
- `IChatCompletionService`：接收完整会话和取消令牌，返回文本增量异步序列。
- `OpenAiCompatibleChatService`：发送 Chat Completions HTTP 请求，解析 JSON 与 SSE，并产生统一增量。
- `ChatHistoryStore`：读取和保存当前会话。
- `ChatSecretStore`：通过 `DataProtectionProvider("LOCAL=user")` 保存和读取 API Key。
- `ChatValidation`：校验 Base URL、模型和消息输入。
- `MarkdownLinkPolicy`：只允许安全的链接协议。

`ChatHistoryStore` 和 `ChatSecretStore` 不依赖 WinUI。

### 3.2 App

新增：

- `src/WithWindows.App/Views/ChatPage.xaml`
- `src/WithWindows.App/Views/ChatPage.xaml.cs`
- `src/WithWindows.App/Controls/MarkdownMessageView.xaml`
- `src/WithWindows.App/Controls/MarkdownMessageView.xaml.cs`
- `src/WithWindows.App/Controls/ChatProviderDialog.cs`

`ToolCatalog` 新增 `ai_chat` 页面项：

- 标题：`AI 对话`
- 图标：使用 WinUI Gallery 图标表核实过的聊天气泡字形
- 出现在左侧导航
- 第一阶段不绑定全局热键

## 4. 配置与数据

### 4.1 配置迁移

`AppConfig` 从版本 `1` 升级到版本 `2`，避免在同一个 schema 版本中静默增加字段。

版本 `1` 升级到版本 `2` 时保留：

- `tools`
- `displayMode`
- `windowState`

新增：

```json
{
  "chat": {
    "baseUrl": "",
    "model": "",
    "systemPrompt": ""
  }
}
```

API Key 不在 `config.json` 中。版本 `2` 的已知工具列表保持不变，`ai_chat` 是导航页面而不是可绑定热键的动作。

旧数组格式、旧 v3 格式和无法识别的版本仍按现有策略重建默认配置。主文件损坏时继续从 `.bak` 恢复。

### 4.2 Base URL 规则

- 必须是绝对 `http` 或 `https` URI。
- 不允许携带查询字符串或片段，避免拼接 `/chat/completions` 后地址失真。
- 保存用户输入的端点根路径，例如 `https://example.test/v1`。
- 代码不自动追加 `/v1` 或 `/chat/completions`，避免重复路径。
- 结尾斜杠在构造客户端前去除。
- 空 Base URL 或空模型视为未配置。
- API Key 允许为空；为空时不发送 `Authorization` 请求头。

### 4.3 会话文件

新增路径：

- `%APPDATA%\WithWindows\chat\current.json`
- `%APPDATA%\WithWindows\chat\credential.bin`

`current.json` 只保存当前会话，包含：

```json
{
  "version": 1,
  "updatedAt": "2026-10-20T12:00:00+08:00",
  "messages": [
    {
      "role": "user",
      "content": "你好"
    },
    {
      "role": "assistant",
      "content": "你好，有什么可以帮你？"
    }
  ]
}
```

保存采用临时文件加 `File.Replace`，并保留 `current.json.bak`。空会话清理时删除主文件和备份。

`credential.bin` 是 DPAPI 加密后的 UTF-8 API Key。空密钥会删除该文件。密钥和解密后的文本不得写入日志。

## 5. 界面

### 5.1 页面结构

`ChatPage` 采用三行布局：

1. 顶部工具条：标题、配置按钮、清空会话按钮。
2. 中间消息列表：`ListView` 独占滚动，用户消息右对齐，AI 消息左对齐。
3. 底部输入区：多行 `TextBox`、发送或停止按钮。

空会话显示简短空状态，提示先配置或直接提问。

### 5.2 消息

消息使用页面内普通可观察模型，至少包含：

- 角色
- 原始文本
- 是否正在流式生成
- 是否渲染 Markdown

用户消息始终按纯文本显示。AI 消息在流式阶段显示纯文本，收到流结束后再解析 Markdown。这样可以避免每个 token 都重新解析 Markdown 导致闪烁和 CPU 浪费。

消息操作：

- 复制：复制原始 Markdown 文本。
- 停止：只对正在生成的 AI 消息可用。

### 5.3 输入

- `Enter` 发送。
- `Shift+Enter` 换行。
- 输入法组合期间按 `Enter` 不触发发送。
- 发送时清空输入框并禁用重复提交。
- 生成中按钮变为「停止」。
- 空白输入不能发送。

### 5.4 配置对话框

配置对话框包含：

- Base URL
- 模型
- API Key：`PasswordBox`
- 系统提示：多行文本框
- 已存密钥时显示“已保存”，留空保存表示继续使用旧密钥。
- “清除已保存密钥”勾选框用于显式删除密钥。
- 保存与取消

保存后立即重建协议客户端，不要求重启应用。取消不修改任何已保存值。

### 5.5 Markdown 范围

第一阶段支持：

- 标题
- 段落和换行
- 粗体、斜体、删除线
- 有序和无序列表
- 引用
- 行内代码
- 围栏代码块
- 链接
- 水平线

暂不支持表格、数学公式、Mermaid、图片嵌入和原始 HTML。原始 HTML 按文本显示，不交给浏览器渲染。

链接只打开 `http`、`https` 和 `mailto`，其他协议不执行。

每个可交互控件必须有稳定的 `AutomationProperties.AutomationId`。

## 6. 协议与流式行为

`OpenAiCompatibleChatService` 直接发送 HTTP 请求：

```http
POST {baseUrl}/chat/completions
Authorization: Bearer {apiKey}
Content-Type: application/json

{
  "model": "{model}",
  "messages": [],
  "stream": true
}
```

- 配置了系统提示时，请求开头加入一条系统消息。
- 当前会话中的用户与助手消息按原顺序发送。
- `Authorization` 只在 API Key 非空时发送。
- 请求使用 `HttpCompletionOption.ResponseHeadersRead`，不能先把完整响应读入内存。
- 流式响应使用 `SseParser.Create` 逐事件读取，不手写按行拆分 SSE。
- 响应内容类型不是 `application/json` 或 `+json` 时按 SSE 处理，兼容返回 `text/plain` 的代理。
- 每个事件的 `data` 为 `[DONE]` 时正常结束。
- 其他事件解析 `choices[0].delta.content`，每次只提取新增文本。
- 如果兼容服务忽略 `stream` 并返回普通 JSON，则解析 `choices[0].message.content`。
- 忽略不含文本的增量，例如只包含角色、工具调用或空内容的更新。
- 正常结束、错误和取消分别映射为内部结果状态；第一阶段不单独呈现 `finish_reason`。

页面只消费 `IAsyncEnumerable<ChatDelta>`，不依赖具体 HTTP 协议。

每次发送有独立 `CancellationTokenSource`：

- 用户点击「停止」时取消。
- 页面离开时取消，但已完成的消息保留并保存。
- 应用退出时取消。
- 取消后已收到的文本保留，并标记为「已停止」。

请求不设置全局固定两分钟超时。取消由用户操作和页面生命周期驱动，`HttpClient` 负责连接级失败处理。

## 7. 错误处理

| 场景 | 界面行为 |
|---|---|
| 未配置端点或模型 | 显示配置引导，不发送请求 |
| 401 / 403 | 提示检查 API Key，不自动重复请求 |
| 404 | 提示检查 Base URL 是否包含正确路径 |
| 429 | 显示服务端错误摘要，允许用户手动重试 |
| 5xx | 显示服务暂时不可用，允许手动重试 |
| DNS、TLS、连接失败 | 显示网络错误摘要 |
| 用户取消 | 保留已有文本，不显示失败弹窗 |
| 流中途断开 | 保留已有文本并标记「回复不完整」 |

错误摘要只读取有限长度，日志只记录状态码、异常类型和端点主机名，不记录请求正文、API Key 或完整响应体。

## 8. 生命周期与并发

- 同一时间只允许一个生成任务。
- `IChatCompletionService` 的生命周期与页面一致，配置变更时替换。
- `HttpClient` 由 `AppHost` 持有并复用到进程退出，不按每次请求或页面新建。
- `HttpClient.Timeout` 为无限，连接超时为 15 秒，流式生成由取消令牌控制。
- 取消令牌源由页面字段持有。
- 流式回调通过 `DispatcherQueue.TryEnqueue` 更新 UI。
- `async void` 只用于事件处理器，并在内部捕获全部异常。
- 页面卸载时取消流并保存已完成的会话。
- 会话保存失败只记录日志，不打断生成。

## 9. 安全

- API Key 使用 `DataProtectionProvider("LOCAL=user")`，保护范围为当前 Windows 用户。
- `config.json`、`current.json` 和日志均不得出现 API Key。
- Markdown 不执行原始 HTML。
- 链接协议采用允许列表。
- 配置对话框不回显已有 API Key，只显示“已保存”状态；留空并保存会保留旧密钥，只有勾选“清除已保存密钥”才删除。
- 清空会话不会自动删除 API Key；密钥另有清除动作。

## 10. 测试

Core 单元测试覆盖：

- 配置 v1 到 v2 迁移，原有工具、显示模式和窗口状态不丢失。
- v2 配置往返一致。
- Base URL 规范化和非法 URI。
- 消息到 Chat Completions 请求 JSON 的映射，包括系统提示顺序。
- SSE 的 `[DONE]`、空内容、多内容片段、格式错误事件和完整文本拼接。
- 非流式 JSON 响应的兼容解析。
- 取消后保留部分文本。
- 会话 JSON 的原子保存、恢复和损坏回退。
- DPAPI 密钥往返、空密钥删除和错误数据拒绝。
- Markdown 链接协议过滤。

协议测试使用伪 HTTP 或伪服务，不访问真实端点，也不需要 API Key。

界面验证：

- `winapp ui inspect` 能看到导航项、消息列表、输入框、发送按钮和配置按钮。
- `winapp ui screenshot` 检查空状态、普通对话、长消息和错误状态。
- 验证浅色与深色主题。
- 验证窗口缩窄后输入区和消息列表不溢出。

## 11. 文档与完成定义

实现完成后同步：

- `README.md`：功能说明、配置示例和 AI 对话使用方式。
- `CHANGELOG.md`：新增 AI 对话、依赖和密钥保护说明。
- `AGENTS.md`：新依赖白名单。
- `docs/agents/winui3-hard-rules.md`：文件选择器文档更新留到附件阶段处理。

完成定义：

- [ ] `dotnet build WithWindows.sln -p:Platform=x64` 零错误。
- [ ] `dotnet test tests/WithWindows.Core.Tests/WithWindows.Core.Tests.csproj -p:Platform=x64` 全绿。
- [ ] 应用实际启动并保持运行。
- [ ] `winapp ui inspect` 能看到新增控件。
- [ ] `winapp ui screenshot` 目视无错乱。
- [ ] 新增可交互控件均有 `AutomationProperties.AutomationId`。
- [ ] 没有真实 API 密钥进入源码、配置或日志。

## 12. 后续阶段

第二阶段：

- 普通文件附件。
- 新版 Windows App SDK 文件选择器。
- Responses API 与 `input_file`。

第三阶段：

- 多会话管理。
- token 预算和自动摘要压缩。
- 表格、数学公式和代码高亮。

第四阶段：

- 多个协议适配器。
- 本地文档提取或检索增强。
