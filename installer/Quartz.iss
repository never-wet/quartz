#define MyAppName "Quartz"
#define MyAppVersion "2.1.0"
#define MyAppPublisher "Quartz contributors"
#define MyAppExeName "Quartz.exe"
#define PublishDir "..\artifacts\publish\win-x64"

[Setup]
AppId={{7D9B7B67-2ED8-45DF-8D26-CBC705BB7BE0}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoVersion=2.1.0.0
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
OutputBaseFilename=Quartz-2.1.0-Setup
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
CloseApplicationsFilter=Quartz.exe
ChangesAssociations=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel1=Welcome to Quartz
WelcomeLabel2=Set up a focused Chromium browser workspace on this PC.%n%nQuartz includes its .NET runtime and Chromium/CEF engine files.
FinishedHeadingLabel=Quartz is ready
FinishedLabel=Setup has finished installing Quartz on your computer.

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "dependencies\VC_redist.x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Icons]
Name: "{group}\Quartz"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\Uninstall Quartz"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Quartz"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{tmp}\VC_redist.x64.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "Preparing the Chromium native runtime..."; Flags: waituntilterminated; Check: not IsVCRuntimeInstalled
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Quartz"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[Code]
function IsVCRuntimeInstalled: Boolean;
var
  Installed: Cardinal;
begin
  Result := RegQueryDWordValue(
    HKLM64,
    'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64',
    'Installed',
    Installed) and (Installed = 1);
end;
