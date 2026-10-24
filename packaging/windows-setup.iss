#if PREPROCVER < EncodeVer(6, 3, 0)
  #error Inno Setup 6.3 or newer is required to compile this installer.
#endif

#ifndef AppVersion
  #error "AppVersion is required"
#endif
#ifndef PackageRoot
  #error "PackageRoot is required"
#endif
#ifndef MainProgram
  #error "MainProgram is required"
#endif
#ifndef OutputRoot
  #error "OutputRoot is required"
#endif

[Setup]
AppId={{BCF70286-94D4-40B3-BAA1-A773E3AF3066}
AppName=狂热运输3修改器
AppVersion={#AppVersion}
AppPublisher=hk9992221
AppPublisherURL=https://github.com/hk9992221/transport-fever-3-modifier
VersionInfoVersion={#AppVersion}.0
VersionInfoProductName=狂热运输3修改器
DefaultDirName={localappdata}\Programs\TransportFever3Modifier
DefaultGroupName=狂热运输3修改器
DisableProgramGroupPage=yes
AllowNoIcons=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
WizardStyle=modern
SetupIconFile=..\src\assets\App.ico
UninstallDisplayIcon={app}\versions\v{#AppVersion}\{#MainProgram}
OutputDir={#OutputRoot}
OutputBaseFilename=TransportFever3Modifier-v{#AppVersion}-windows-x64-setup
Compression=lzma2
SolidCompression=no
CloseApplications=no
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
; Separate version directories preserve a runtime DLL still loaded by the game.
Source: "{#PackageRoot}\*"; DestDir: "{app}\versions\v{#AppVersion}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\狂热运输3修改器"; Filename: "{app}\versions\v{#AppVersion}\{#MainProgram}"; WorkingDir: "{app}\versions\v{#AppVersion}"
Name: "{userdesktop}\狂热运输3修改器"; Filename: "{app}\versions\v{#AppVersion}\{#MainProgram}"; WorkingDir: "{app}\versions\v{#AppVersion}"; Tasks: desktopicon

[Run]
Filename: "{app}\versions\v{#AppVersion}\{#MainProgram}"; Description: "Launch 狂热运输3修改器"; Flags: postinstall unchecked nowait skipifsilent
