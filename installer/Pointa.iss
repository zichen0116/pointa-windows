#ifndef AppVersion
  #define AppVersion "0.1.4"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\win-x64"
#endif
#ifndef ArtifactDir
  #define ArtifactDir "..\artifacts"
#endif

[Setup]
AppId={{9E3B0814-B994-4A8A-ADAA-B495753B7B26}
AppName=Pointa
AppVersion={#AppVersion}
AppPublisher=Pointa contributors
AppPublisherURL=https://github.com/zichen0116/pointa-windows
DefaultDirName={localappdata}\Programs\Pointa
DefaultGroupName=Pointa
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\Pointa.exe
OutputDir={#ArtifactDir}
OutputBaseFilename=Pointa-{#AppVersion}-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
SetupLogging=yes

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\Pointa\Pointa"; Filename: "{app}\Pointa.exe"
Name: "{userprograms}\Pointa\Uninstall Pointa"; Filename: "{uninstallexe}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Pointa"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\Pointa.exe"; Parameters: "--shutdown"; Flags: runhidden waituntilterminated
Filename: "{app}\Pointa.exe"; Description: "Launch Pointa / 启动映点"; Flags: postinstall nowait skipifsilent unchecked

[Code]
function InitializeUninstall(): Boolean;
var
  ExitCode: Integer;
  ApplicationPath: String;
begin
  Result := True;
  ApplicationPath := ExpandConstant('{app}\Pointa.exe');
  if FileExists(ApplicationPath) then
  begin
    Result := Exec(ApplicationPath, '--shutdown', ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ExitCode);
    if Result then
      Result := ExitCode = 0;
    if not Result then
    begin
      Log('Pointa did not exit. Uninstall was cancelled to preserve running files and user data.');
      if not UninstallSilent then
        MsgBox('Please quit Pointa from the system tray, then retry uninstall.' + #13#10 +
          '请先从系统托盘退出映点，再重新卸载。', mbError, MB_OK);
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataPath: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataPath := ExpandConstant('{localappdata}\Pointa');
    if DirExists(DataPath) and not UninstallSilent then
      if MsgBox('Also delete this Windows user''s Pointa settings and paired devices?' + #13#10 +
        'Default: No (keep them for reinstall).' + #13#10 + #13#10 +
        '同时删除当前 Windows 用户的映点设置和已配对设备？' + #13#10 +
        '默认选择“否”，重新安装时可继续使用。' + #13#10 + DataPath,
        mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        if not DelTree(DataPath, True, True, True) then
          MsgBox('Some settings could not be removed: ' + DataPath, mbInformation, MB_OK);
  end;
end;
