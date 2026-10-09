# 安装器（Inno Setup）

安装器把自包含单文件 exe 与 `Assets/` 打成 `WithWindows-<版本>-x64-setup.exe`。

## 本地编译

```powershell
# 1. 产出载荷：自包含单文件（必须带 x64；单文件会自动开启 EnableMsixTooling）
dotnet publish src/WithWindows.App/WithWindows.App.csproj -c Release -o dist -p:Platform=x64 -p:SelfContained=true -p:PublishSingleFile=true

# 2. 编译安装器（版本号取自 csproj，保持与 Release 一致）
$ver = [regex]::Match((Get-Content src/WithWindows.App/WithWindows.App.csproj -Raw), '<Version>([^<]+)</Version>').Groups[1].Value
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" /DAppVersion=$ver installer\WithWindows.iss
```

产物在 `dist-installer\WithWindows-<版本>-x64-setup.exe`。

## 行为说明

- 装到 `{autopf}\With Windows`（用户级，`PrivilegesRequired=lowest`，不强制提权）
- 创建开始菜单项；桌面快捷方式为可选；「开机自动启动」为可选任务，写 `HKCU\...\Run` 的同一个值名，与设置页开关一致
- 卸载时删除程序目录与自启项，**不触碰** `%APPDATA%\WithWindows\` 下的用户数据

## 已知事项

- 未签名：首次运行会看到 SmartScreen「未知发布者」，点「更多信息 → 仍要运行」即可；这是设计文档第 10.4 节的既定取舍。
- 向导语言：Inno Setup 6 官方安装包**不含**中文语言文件，因此默认是英文向导。把 `ChineseSimplified.isl` 放到本目录后重新编译，脚本会通过 `#if FileExists` 自动切为中文（不预置该文件以免引入未经审阅的第三方内容）。
- `Assets/` 会随安装包一起安装。注意：单文件发布**不会**把 `Content` 复制到 publish 目录，且**仅加 `CopyToPublishDirectory="PreserveNewest"` 无效**——真正生效的是 csproj 里的 `CopyAssetsToPublish` MSBuild 目标（`AfterTargets="Publish"`，实测把该目标去掉后 publish 目录就只剩 exe 与 pdb）。排查图标丢失时别把那个目标当冗余删掉。
