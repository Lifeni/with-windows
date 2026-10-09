; With Windows 安装器（Inno Setup 6）
; 编译：ISCC.exe /DAppVersion=0.5.0 WithWindows.iss
; 载荷：dist\WithWindows.exe（自包含单文件）与 dist\Assets\*

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#define AppName "With Windows"
#define AppExeName "WithWindows.exe"
#define SourceDir "..\dist"

; Inno Setup 6 官方安装包不含中文语言文件。把 ChineseSimplified.isl 放到本目录即可自动启用中文向导；
; 没有该文件时回退英文（不预置第三方翻译文件，避免引入未经审阅的内容）。
#define ChineseIsl AddBackslash(SourcePath) + "ChineseSimplified.isl"

[Setup]
AppId={{8E1F0B62-9C4A-4F1E-9E77-3C2B6D5A41C7}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=With Windows
DefaultDirName={autopf}\With Windows
DefaultGroupName=With Windows
DisableProgramGroupPage=yes
OutputDir=..\dist-installer
OutputBaseFilename=WithWindows-{#AppVersion}-x64-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; 用户级安装：不强制提权，避免未签名安装器触发 UAC 后的额外摩擦
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\{#AppExeName}

[Languages]
#if FileExists(ChineseIsl)
Name: "chinese"; MessagesFile: "compiler:Default.isl,{#ChineseIsl}"
#else
Name: "english"; MessagesFile: "compiler:Default.isl"
#endif

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："
Name: "autostart"; Description: "开机自动启动"; GroupDescription: "附加任务："; Flags: unchecked

[Files]
Source: "{#SourceDir}\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\Assets\*"; DestDir: "{app}\Assets"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; 与设置页的「开机自启」开关写同一个位置，保证两处一致
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
  ValueName: "WithWindows"; ValueData: """{app}\{#AppExeName}"""; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExeName}"; Description: "启动 {#AppName}"; Flags: nowait postinstall skipifsilent
