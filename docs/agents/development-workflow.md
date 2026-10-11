# 开发工作流

## 一、每轮改动的固定闭环

顺序不可跳步。**没有跑通全部六步之前，不允许声称任务完成。**

```
1. dotnet build              → 零错误
2. dotnet run                → 必须真的启动起来
3. winapp ui inspect         → 确认新控件出现在无障碍树里
4. winapp ui screenshot      → 目视确认布局没错乱
5. 对照需求自检
6. 才允许声称完成
```

第 2 步的 `dotnet run` 会前台阻塞。自动化场景下必须**非阻塞启动**（后台进程），确认窗口存在后结束进程，不要让命令挂住。

## 一之二、每批改动后的独立代码审查（硬性）

六步闭环由**实现者**自己跑，缺少独立性。因此**每完成一批改动（一个计划里的多个任务，或一次会话量级的改动）后，必须再跑一次独立审查**：

```powershell
# 审查本批新增的全部提交（<base> 取本批之前的那个提交）
# 注意带上范围限定，否则会花大量时间在历史文档与 DPI 记录上
codex exec review --base <base-commit> "只审 src/** 的代码改动；docs/** 只做显而易见的一致性检查，不要逐条读长文档；不要执行构建，也不要结束正在运行的 WithWindows.exe 进程"

# 只审查尚未提交的改动
codex exec review --uncommitted

# 审查单个提交
codex exec review --commit <sha>
```

**当前 CLI 的限制（2026-10-09 实测）**：本机 `codex exec review` 的 `--base` 与 `[PROMPT]` **互斥**（报 `the argument '--base <BRANCH>' cannot be used with '[PROMPT]'`），无法一边按范围审一边带限定指令。此时改用等价的独立审查：

```powershell
$prompt = @'
你是独立代码审查者。审查范围：<base>..HEAD。只审 src/**；docs/** 仅在明显矛盾时提一句，不要读长文档历史条目；
不要执行构建/测试，也不要结束 WithWindows.exe 进程。按 P1/P2/P3 输出发现（文件:行、问题、依据、建议）。
'@
$prompt | codex exec -C <worktree> -s read-only -
```

规则：

- **默认就执行，不要就「要不要跑审查」征求用户意见**——这是每批改动的固定收尾步骤，和构建、测试同级。
- **顺序：先把应用打开给用户看，再并行跑审查**（用户明确要求）；不要等审查结束才交付界面。
- **给审查限定范围**：明确「只审 `src/**`；`docs/**` 只做显而易见的一致性检查；不要执行构建、不要结束 `WithWindows.exe` 进程」。不加限定会一直纠缠 `ui-baseline.md` 的历史 DPI 记录。
- **给审查设时间上限**：正常一轮约 10–20 分钟。若进程 20 分钟以上无任何输出（`Get-Process` 里 CPU 几乎不涨），说明卡在模型/网络侧，直接结束该进程并重跑，不要无限等（实测遇到过一次卡住近 30 分钟）。
- `codex exec review` 是**独立进程、独立上下文**，不共享本会话的判断，因此能发现「自己审自己」漏掉的问题。
- 审查发现的问题按优先级处理：**先修 Critical / Important，再提交**；确属误判或有意取舍的，在提交信息或文档里写明理由，不要静默忽略。
- 审查结论（发现项与处置）记入 `docs/agents/ui-baseline.md` 的对应条目或 `.superpowers/sdd/` 的执行日志。
- 子代理通道若恢复，优先用 `superpowers:subagent-driven-development`（每任务一个实现者 + 审查者）；通道不可用时，`codex exec review` 是**替代方案而非可选项**。

## 二、界面自检命令

前置条件：应用正在运行，且处于有图形会话的环境。

| 命令 | 作用 |
|---|---|
| `winapp ui inspect` | 导出无障碍树（相当于"看"到控件层级） |
| `winapp ui screenshot` | 截取应用窗口 |
| `winapp ui click <selector>` | 点击按钮或选择项 |
| `winapp ui invoke <selector>` | 触发控件默认动作 |
| `winapp ui search <text>` | 按名称或 AutomationId 查找元素 |
| `winapp ui set-value <selector> <value>` | 给输入框赋值 |

选择器依赖 `AutomationProperties.AutomationId`，所以**新增可交互控件必须同时加 AutomationId**。

`winapp` CLI 目前是公共预览版，命令语法可能变动。执行前用 `winapp --help` 核对其「选择目标窗口」的参数，不要凭记忆拼参数。

## 三、构建失败的处理

最多尝试 3 次。每次必须：

1. 完整读错误信息，不要只看第一行；
2. XAML 相关错误先检查命名空间是不是 `Microsoft.UI.Xaml.*`；
3. 启动期崩溃先检查 `Platform` 是否为 x64、`app.manifest` 是否有效。

3 次仍未解决，停下并报告：

- 卡在哪一步（build / run / inspect）
- 完整错误文本
- 已排除的可能
- 建议的下一步

**不允许**为了绕过问题更换框架、更换 UI 库或大范围重写。

## 四、发布

发布产物是 Inno Setup 安装器（未打包 + 自包含单文件 exe 作为载荷）。完整发布流程在 P5 阶段确定，届时本节补全命令。

已知约束：

- 常驻实例会锁住 exe，构建前必须关闭应用（否则报 MSB3021 / MSB3027，那是文件被占用而不是代码错误）；
- 未签名安装器首次运行会有 SmartScreen「未知发布者」提示，属预期行为。

## 五、完成定义（DoD）

- [ ] `dotnet build -p:Platform=x64` 零错误
- [ ] `dotnet test -p:Platform=x64` 全绿
- [ ] 涉及界面时已实际启动，且 `winapp ui inspect` 能看到新增控件
- [ ] 涉及界面时截图目视无错乱
- [ ] 新增可交互控件均有 `AutomationProperties.AutomationId`
- [ ] 新增逻辑有对应单元测试
- [ ] README 与 CHANGELOG 已同步
