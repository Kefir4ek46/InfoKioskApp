@echo off
setlocal

echo Разрешение удалённого доступа через порт 8080...

netsh advfirewall firewall delete rule name="InfoKiosk_RemoteAccess" >nul 2>&1

netsh advfirewall firewall add rule ^
 name="InfoKiosk_RemoteAccess" ^
 dir=in action=allow ^
 protocol=TCP localport=8080

echo Готово!
endlocal
exit /b 0
