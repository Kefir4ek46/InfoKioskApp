[Setup]
PrivilegesRequired=admin
AppName=InfoKioskApp
AppVersion=2.0.1
DefaultDirName={pf}\InfoKioskApp
DefaultGroupName=InfoKioskApp
OutputDir=Output
OutputBaseFilename=InfoKioskApp_Setup
Compression=lzma
SolidCompression=yes

[Files]
; Основные файлы приложения (.NET 9)
Source: "bin\Release\net9.0-windows\*"; DestDir: "{app}"; Flags: recursesubdirs

; Инсталлятор .NET Desktop Runtime 9
Source: "Installer\dotnet-desktop-9.0.11.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall

; Инсталлятор WebView2
Source: "Installer\WebView2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall

; Скрипт Task Scheduler автозапуска
Source: "Installer\CreateAutoStartTask.bat"; DestDir: "{app}"
Source: "Installer\EnableLocalServerAccess.bat"; DestDir: "{app}"; Flags: ignoreversion


[Icons]
Name: "{group}\Запустить InfoKioskApp"; Filename: "{app}\InfoKioskApp.exe"
Name: "{group}\Настроить удалённый доступ"; Filename: "{app}\EnableRemoteAccess.bat"
Name: "{group}\Добавить в автозагрузку"; Filename: "{app}\AutoStartKiosk.bat"
Name: "{group}\Автовыключение ПК"; Filename: "{app}\shedule_shutdown.bat"

; --------------------------------------------
;   Проверка и установка .NET + WebView2
; --------------------------------------------

[Run]
; Проверка наличия .NET 9 Desktop Runtime
Filename: "{cmd}"; \
    Parameters: "/c ""{tmp}\dotnet-desktop-9.0.11.exe /install /quiet /norestart"""; \
    StatusMsg: "Проверка и установка .NET 9 Desktop Runtime..."; \
    Flags: runhidden waituntilterminated skipifsilent

; Проверка и установка WebView2 (если нет)
Filename: "{tmp}\WebView2Setup.exe"; \
    Parameters: "/silent /install"; \
    StatusMsg: "Проверка и установка WebView2..."; \
    Flags: waituntilterminated skipifsilent

    Filename: "{cmd}"; Parameters: "/c ""{app}\EnableLocalServerAccess.bat"""; \
StatusMsg: "Настройка локального сервера..."; Flags: runhidden waituntilterminated


; Создание автозапуска через Task Scheduler
Filename: "{cmd}"; \
    Parameters: "/c ""{app}\CreateAutoStartTask.bat"""; \
    StatusMsg: "Создается автостарт InfoKioskApp..."; \
    Flags: runhidden waituntilterminated skipifsilent

; Запуск приложения после установки
Filename: "{app}\InfoKioskApp.exe"; \
    Description: "Запустить InfoKioskApp"; \
    Flags: nowait postinstall skipifsilent
