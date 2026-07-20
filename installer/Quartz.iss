#define MyAppName "Quartz"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "Quartz contributors"
#define MyAppExeName "Quartz.exe"
#define PublishDir "..\artifacts\publish\win-x64"

[Setup]
AppId={{7D9B7B67-2ED8-45DF-8D26-CBC705BB7BE0}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoVersion=1.1.0.0
VersionInfoProductName=Quartz Browser
VersionInfoProductVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=Quartz Browser Setup
DefaultDirName={localappdata}\Programs\Quartz
DefaultGroupName=Quartz
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\release
OutputBaseFilename=QuartzSetup
SetupIconFile=..\src\Quartz\Assets\Quartz.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName=Quartz Browser
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern dark includetitlebar hidebevels
WizardSizePercent=110
WizardImageFile=assets\wizard-large.png
WizardSmallImageFile=assets\wizard-small.png
WizardImageBackColor=#0d0b14
WizardSmallImageBackColor=#15121f
WizardBackColor=#0d0b14
DisableWelcomePage=no
CloseApplications=yes
RestartApplications=no
ChangesAssociations=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel1=Welcome to Quartz
WelcomeLabel2=Set up a focused browser workspace on this PC.%n%nQuartz includes its .NET runtime and can prepare WebView2 automatically when Windows needs it.
FinishedHeadingLabel=Quartz is ready
FinishedLabel=Setup has finished installing Quartz on your computer.

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "dependencies\MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Icons]
Name: "{group}\Quartz"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\Uninstall Quartz"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Quartz"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "Preparing the WebView2 browser engine..."; Flags: waituntilterminated; Check: not IsWebView2Installed
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Quartz"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[Code]
function IsWebView2Installed: Boolean;
begin
  Result :=
    DirExists(ExpandConstant('{pf32}\Microsoft\EdgeWebView\Application')) or
    DirExists(ExpandConstant('{localappdata}\Microsoft\EdgeWebView\Application'));
end;
