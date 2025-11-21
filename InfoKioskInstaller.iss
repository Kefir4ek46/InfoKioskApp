[Setup]
AppName=InfoKioskApp
AppVersion=1.0
DefaultDirName={pf}\InfoKioskApp
DefaultGroupName=InfoKioskApp
OutputDir=Output
OutputBaseFilename=InfoKioskApp_Setup
Compression=lzma
SolidCompression=yes

[Files]
Source: "bin\Release\*"; DestDir: "{app}"; Flags: recursesubdirs

[Icons]
Name: "{group}\Запустить InfoKioskApp"; Filename: "{app}\InfoKioskApp.exe"
Name: "{group}\Настроить удалённый доступ"; Filename: "{app}\EnableRemoteAccess.bat"
Name: "{group}\Добавить в автозагрузку"; Filename: "{app}\AutoStartKiosk.bat"
Name: "{group}\Автовыключение ПК"; Filename: "{app}\shedule_shutdown.bat"

[Run]
Filename: "{app}\InfoKioskApp.exe"; Description: "Запустить InfoKioskApp"; Flags: nowait postinstall skipifsilent
