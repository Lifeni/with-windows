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
