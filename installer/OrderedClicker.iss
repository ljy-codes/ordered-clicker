#ifndef AppVersion
  #define AppVersion "1.1.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\publish\win-x64"
#endif

#ifndef OutputDir
  #define OutputDir "..\publish\packages"
#endif

#define AppName "有序连点器"
#define AppPublisher "ljy-codes"
#define AppUrl "https://github.com/ljy-codes/ordered-clicker"

[Setup]
AppId={{D25D23BA-1607-4D23-82BD-D1891DD195A5}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
DefaultDirName={localappdata}\Programs\OrderedClicker
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename=ordered-clicker-setup-v{#AppVersion}
SetupIconFile=assets\ordered-clicker.ico
UninstallDisplayIcon={app}\OrderedClicker.exe
UninstallDisplayName={#AppName}
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Windows 安装程序
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
DisableWelcomePage=no
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=yes
UsePreviousTasks=yes
SetupLogging=yes
ShowLanguageDialog=no
LanguageDetectionMethod=uilanguage

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："; Flags: checkedonce

[Files]
Source: "{#SourceDir}\OrderedClicker.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\OrderedClicker.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\OrderedClicker.exe"; Tasks: desktopicon
Name: "{group}\卸载{#AppName}"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\OrderedClicker.exe"; Description: "运行{#AppName}"; Flags: nowait postinstall skipifsilent
