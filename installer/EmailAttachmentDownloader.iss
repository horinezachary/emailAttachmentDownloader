; Inno Setup 6.1+ script. Built by CI (.github/workflows/build.yml) after `dotnet publish`:
;   ISCC.exe /DAppVersion=1.2.3 installer\EmailAttachmentDownloader.iss
; Expects ..\publish\tray (the tray app) and ..\publish\cli (the `ead` tool).
;
; Per-user install (no admin prompt) because the app's settings and passwords are per-user.
; If the .NET Desktop Runtime is missing, Setup offers to download it from Microsoft, checks that the
; download is signed by Microsoft, and runs it (that one step asks for administrator approval).
; Pass /SKIPRUNTIMECHECK=1 to skip the runtime check (managed machines, CI smoke tests).

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#define AppName "Email Attachment Downloader"
#define AppExe "EmailAttachmentDownloader.exe"
#define AppUrl "https://github.com/horinezachary/emailAttachmentDownloader"

; The app targets .NET 10 and uses WinForms, so it needs the "Windows Desktop Runtime" 10.x (x64).
#define RuntimeMajorPrefix "10."
#define RuntimeUrl "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe"
#define RuntimeDownloadPage "https://dotnet.microsoft.com/download/dotnet/10.0"

[Setup]
AppId={{D0B0E740-24DE-4DAE-A533-06597079C221}
AppName={#AppName}
AppVersion={#AppVersion}
VersionInfoVersion={#AppVersion}.0
AppPublisher=horinezachary
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
SetupIconFile=..\assets\app-icon.ico
UninstallDisplayIcon={app}\{#AppExe}
OutputDir=..\publish\installer
OutputBaseFilename=EmailAttachmentDownloader-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ChangesEnvironment=yes
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "startup"; Description: "Start {#AppName} when I sign in to Windows"; GroupDescription: "Options:"; Flags: unchecked
Name: "addtopath"; Description: "Add the command-line tool (ead) to my PATH"; GroupDescription: "Options:"; Flags: unchecked

[Files]
Source: "..\publish\tray\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\publish\cli\*"; DestDir: "{app}\cli"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"

[Registry]
; Same key and value name the app's own "Start with Windows" menu item uses, so the two stay in sync.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "EmailAttachmentDownloader"; ValueData: """{app}\{#AppExe}"""; Tasks: startup
Root: HKCU; Subkey: "Environment"; ValueType: expandsz; ValueName: "Path"; ValueData: "{olddata};{app}\cli"; Check: NeedsAddPath(ExpandConstant('{app}\cli')); Tasks: addtopath

[Run]
Filename: "{app}\{#AppExe}"; Description: "Start {#AppName} now"; Flags: nowait postinstall skipifsilent
; The app's own "update now" runs this installer silently with /RELAUNCH=1, then it should come back up by itself.
Filename: "{app}\{#AppExe}"; Flags: nowait runasoriginaluser; Check: RelaunchRequested

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  RunValue = 'EmailAttachmentDownloader';
  RuntimeRegKey = 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App';

var
  DownloadPage: TDownloadWizardPage;

function RelaunchRequested: Boolean;
begin
  Result := ExpandConstant('{param:RELAUNCH|0}') = '1';
end;

function SkipRuntimeCheck: Boolean;
begin
  Result := ExpandConstant('{param:SKIPRUNTIMECHECK|0}') = '1';
end;

{ True when DotnetRoot\shared\Microsoft.WindowsDesktop.App contains a stable (non-preview) 10.x folder.
  This is how Microsoft documents checking for an installed runtime: the folders are what the app host actually loads. }
function HasDesktopRuntimeIn(const DotnetRoot: String): Boolean;
var
  FindRec: TFindRec;
  Base: String;
begin
  Result := False;
  if DotnetRoot = '' then
    Exit;

  Base := AddBackslash(DotnetRoot) + 'shared\Microsoft.WindowsDesktop.App\';
  if FindFirst(Base + '{#RuntimeMajorPrefix}*', FindRec) then
  begin
    try
      repeat
        if ((FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0) and (Pos('-', FindRec.Name) = 0) then
        begin
          Log('Found Desktop Runtime ' + FindRec.Name + ' in ' + DotnetRoot);
          Result := True;
          Exit;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

{ True when a stable Windows Desktop Runtime 10.x (x64) is installed. Looks where .NET registers itself, then the
  default location, then DOTNET_ROOT, and finally the registry list some installers also write. }
function IsDesktopRuntimeInstalled: Boolean;
var
  Root: String;
  Names: TArrayOfString;
  I: Integer;
begin
  Result := True;

  if RegQueryStringValue(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64', 'InstallLocation', Root) then
    if HasDesktopRuntimeIn(Root) then
      Exit;

  if HasDesktopRuntimeIn(GetEnv('ProgramW6432') + '\dotnet') then
    Exit;

  if HasDesktopRuntimeIn(GetEnv('DOTNET_ROOT')) then
    Exit;

  if RegGetValueNames(HKLM64, RuntimeRegKey, Names) then
  begin
    for I := 0 to GetArrayLength(Names) - 1 do
    begin
      if (Pos('{#RuntimeMajorPrefix}', Names[I]) = 1) and (Pos('-', Names[I]) = 0) then
      begin
        Log('Found Desktop Runtime ' + Names[I] + ' in the registry');
        Exit;
      end;
    end;
  end;

  Log('No Desktop Runtime 10.x found');
  Result := False;
end;

function OnDownloadProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  if Progress = ProgressMax then
    Log('Downloaded ' + FileName);
  Result := True;
end;

{ CI hook: /DETECTONLY=1 logs whether the runtime was detected and quits without installing anything. }
function InitializeSetup: Boolean;
begin
  Result := True;
  if ExpandConstant('{param:DETECTONLY|0}') = '1' then
  begin
    if IsDesktopRuntimeInstalled then
      Log('DesktopRuntimeDetected=True')
    else
      Log('DesktopRuntimeDetected=False');
    Result := False;
  end;
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(
    'Downloading .NET',
    'Downloading the .NET Desktop Runtime from Microsoft...',
    @OnDownloadProgress);
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := MemoDirInfo;
  if MemoTasksInfo <> '' then
    Result := Result + NewLine + NewLine + MemoTasksInfo;
  if (not SkipRuntimeCheck) and (not IsDesktopRuntimeInstalled) then
    Result := Result + NewLine + NewLine + 'Also:' + NewLine + Space +
      'Download and install the .NET 10 Desktop Runtime from Microsoft (about 55 MB).';
end;

{ Checks the downloaded file carries a valid Microsoft Authenticode signature before it is run. }
function IsSignedByMicrosoft(const FilePath: String): Boolean;
var
  Escaped, Script: String;
  ResultCode: Integer;
begin
  Escaped := FilePath;
  StringChangeEx(Escaped, '''', '''''', True);
  Script := '$s = Get-AuthenticodeSignature -LiteralPath ''' + Escaped + '''; ' +
    'if ($s.Status -eq ''Valid'' -and $s.SignerCertificate.Subject -match ''O=Microsoft Corporation'') { exit 0 } else { exit 1 }';
  Result := Exec(
    ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "' + Script + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function ManualInstallHint: String;
begin
  Result := #13#10#13#10 + 'You can install it yourself from {#RuntimeDownloadPage} ' +
    '(choose ".NET Desktop Runtime", x64), then run this setup again.';
end;

{ Runs after the user clicks Install and before any file is copied. A non-empty result aborts Setup with that message. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  InstallerPath: String;
  ResultCode: Integer;
begin
  Result := '';
  if SkipRuntimeCheck or IsDesktopRuntimeInstalled then
    Exit;

  if SuppressibleMsgBox(
       '{#AppName} needs the .NET 10 Desktop Runtime, which is not installed on this computer.' + #13#10#13#10 +
       'Download it from Microsoft (about 55 MB) and install it now? Windows will ask for administrator approval.',
       mbConfirmation, MB_YESNO, IDYES) <> IDYES then
  begin
    Result := 'Setup cannot continue without the .NET 10 Desktop Runtime.' + ManualInstallHint;
    Exit;
  end;

  DownloadPage.Clear;
  DownloadPage.Add('{#RuntimeUrl}', 'windowsdesktop-runtime-win-x64.exe', '');
  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
    except
      Result := 'The .NET Desktop Runtime could not be downloaded: ' + GetExceptionMessage + ManualInstallHint;
      Exit;
    end;
  finally
    DownloadPage.Hide;
  end;

  InstallerPath := ExpandConstant('{tmp}\windowsdesktop-runtime-win-x64.exe');

  if not IsSignedByMicrosoft(InstallerPath) then
  begin
    Result := 'The downloaded .NET installer is not signed by Microsoft, so it was not run.' + ManualInstallHint;
    Exit;
  end;

  { "runas" asks for administrator approval; the rest of Setup stays per-user. }
  if not ShellExec('runas', InstallerPath, '/install /quiet /norestart', '', SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode) then
  begin
    Result := 'The .NET installer could not be started (' + SysErrorMessage(ResultCode) + ').' + ManualInstallHint;
    Exit;
  end;

  { 0 = installed, 3010 = installed but a restart is pending, 1638 = same or newer version already present. }
  if (ResultCode <> 0) and (ResultCode <> 3010) and (ResultCode <> 1638) then
  begin
    Result := 'The .NET installer did not finish (exit code ' + IntToStr(ResultCode) + ').' + ManualInstallHint;
    Exit;
  end;

  { The installer reported success, so go on even if detection disagrees; the log records what it saw. }
  if not IsDesktopRuntimeInstalled then
    Log('The .NET installer reported success but the runtime was not detected afterwards.');
end;

{ Adds the CLI folder to the user PATH only if it is not already there. }
function NeedsAddPath(Dir: String): Boolean;
var
  Current: String;
begin
  if not RegQueryStringValue(HKCU, 'Environment', 'Path', Current) then
  begin
    Result := True;
    Exit;
  end;
  Result := Pos(';' + Lowercase(Dir) + ';', ';' + Lowercase(Current) + ';') = 0;
end;

procedure RemoveFromUserPath(Dir: String);
var
  Current, Needle: String;
  P: Integer;
begin
  if not RegQueryStringValue(HKCU, 'Environment', 'Path', Current) then
    Exit;

  Current := ';' + Current + ';';
  Needle := ';' + Dir + ';';
  P := Pos(Lowercase(Needle), Lowercase(Current));
  while P > 0 do
  begin
    { Remove ";dir" and keep the following ";" so neighbouring entries stay separated. }
    Delete(Current, P, Length(Needle) - 1);
    P := Pos(Lowercase(Needle), Lowercase(Current));
  end;

  { Strip the separators added above. }
  if (Length(Current) > 0) and (Current[1] = ';') then
    Delete(Current, 1, 1);
  if (Length(Current) > 0) and (Current[Length(Current)] = ';') then
    Delete(Current, Length(Current), 1);

  RegWriteExpandStringValue(HKCU, 'Environment', 'Path', Current);
end;

{ Settings, saved pictures and stored passwords live outside the install folder and are left alone. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    { The app's own "Start with Windows" item may have created this value, so remove it explicitly. }
    RegDeleteValue(HKCU, RunKey, RunValue);
    RemoveFromUserPath(ExpandConstant('{app}\cli'));
  end;
end;
