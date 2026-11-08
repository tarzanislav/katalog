; KATALOG installer (Inno Setup 6). Built by GitHub Actions; the version and the folder with the published program are
; passed on the command line (/DAppVersion=..., /DSourceDir=...). The library (Documents\KATALOG) and the settings
; (%APPDATA%\KATALOG) are never touched: uninstalling removes only the program.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif

#define AppName "KATALOG"
#define AppExe "Katalog.exe"

[Setup]
AppId={{351F95AE-7149-4302-A8D6-F0D7F33D3CB1}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=tarzanislav
AppPublisherURL=https://github.com/tarzanislav/katalog
AppComments=Súkromný katalóg videotéky a diskov
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\KATALOG
DefaultGroupName=KATALOG
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=KATALOG_Setup_{#AppVersion}
UninstallDisplayIcon={app}\{#AppExe}
SetupIconFile=..\src\Katalog.Okno\Zdroje\katalog.ico
UninstallDisplayName={#AppName}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern
MinVersion=10.0.17763
CloseApplications=yes
LicenseFile=..\LICENSE

[Languages]
#if FileExists(CompilerPath + "Languages\Slovak.isl")
Name: "sk"; MessagesFile: "compiler:Languages\Slovak.isl"
#else
Name: "en"; MessagesFile: "compiler:Default.isl"
#endif

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\KATALOG"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\KATALOG"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,KATALOG}"; Flags: nowait postinstall skipifsilent runasoriginaluser
; after an automatic update (silent install with /SPUSTIT=1) KATALOG starts again
Filename: "{app}\{#AppExe}"; Flags: nowait runasoriginaluser; Check: StartAfterUpdate

[Code]
function StartAfterUpdate: Boolean;
begin
  Result := ExpandConstant('{param:SPUSTIT|0}') = '1';
end;
