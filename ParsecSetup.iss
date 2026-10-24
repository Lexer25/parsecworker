; Установщик службы Parsec Integration Client (.NET 8, self-contained single-file).
; Заменяет ClickOnce-публикацию прежней версии (setup.exe + Application Files).
; Сборка: build.cmd  ->  Output\ParsecIntegrationSetup_v<версия>.exe
; Требуется Inno Setup 6 (ISCC.exe).

#define MyAppName "Parsec Integration Client"
; Версия берётся из файла службы, то есть из <Version> в csproj: одно место правки.
#define MyAppVersion GetFileVersion("publish\ParsecIntegrationClient.exe")
#define MyAppExeName "ParsecIntegrationClient.exe"
#define ServiceName "ParsecIntegrationService"
#define ServiceAccount "NT AUTHORITY\LocalService"

[Setup]
AppId={{8C1F5E42-9B3D-4C77-9E51-2A6E0C4B7D31}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=Артонит
AppPublisherURL=https://www.artonit.ru
DefaultDirName={autopf}\ParsecIntegrationService
DisableDirPage=no
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
OutputDir=Output
OutputBaseFilename=ParsecIntegrationSetup_v{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName={#MyAppName} {#MyAppVersion}
; Службу останавливаем сами в [Code], автозакрытие приложений не нужно.
CloseApplications=no

[Files]
; Файлы службы. appsettings.json исключён: при обновлении настройки не перезаписываются.
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "appsettings.json"
; Конфигурация ставится только при первой установке и переживает удаление программы.
Source: "publish\appsettings.json"; DestDir: "{app}"; Flags: onlyifdoesntexist uninsneveruninstall

[Code]
function RunHidden(const FileName, Params: string): Integer;
var
  ResultCode: Integer;
begin
  Exec(FileName, Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := ResultCode;
end;

procedure StopService;
begin
  if RunHidden(ExpandConstant('{sys}\sc.exe'), 'query "{#ServiceName}"') = 0 then
  begin
    RunHidden(ExpandConstant('{sys}\net.exe'), 'stop "{#ServiceName}"');
    Sleep(5000);
  end;
end;

procedure RegisterService;
var
  Sc, ExePath: string;
begin
  Sc := ExpandConstant('{sys}\sc.exe');
  ExePath := ExpandConstant('{app}\{#MyAppExeName}');

  if RunHidden(Sc, 'query "{#ServiceName}"') = 0 then
    RunHidden(Sc, 'config "{#ServiceName}" binPath= "' + ExePath + '" start= auto')
  else
    RunHidden(Sc, 'create "{#ServiceName}" binPath= "' + ExePath + '" start= auto DisplayName= "{#ServiceName}"');

  RunHidden(Sc, 'description "{#ServiceName}" "Модуль интеграции СКУД Артонит и СКУД Парсек"');
  RunHidden(Sc, 'failure "{#ServiceName}" reset= 86400 actions= restart/60000/restart/60000/restart/60000');
  RunHidden(Sc, 'config "{#ServiceName}" obj= "{#ServiceAccount}"');
  RunHidden(ExpandConstant('{sys}\net.exe'), 'start "{#ServiceName}"');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
    StopService;
  if CurStep = ssPostInstall then
    RegisterService;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    StopService;
    RunHidden(ExpandConstant('{sys}\sc.exe'), 'delete "{#ServiceName}"');
  end;
end;

