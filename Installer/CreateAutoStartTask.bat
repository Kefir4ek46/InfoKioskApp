@echo off
setlocal

set TASKNAME=InfoKioskAutoStart
set APPPATH=%~dp0InfoKioskApp.exe

REM Удаляем старую задачу, если была
schtasks /Delete /TN "%TASKNAME%" /F >nul 2>&1

REM Создаём новую задачу автозапуска
schtasks /Create ^
 /TN "%TASKNAME%" ^
 /TR "\"%APPPATH%\"" ^
 /SC ONLOGON ^
 /RL HIGHEST ^
 /F

endlocal
exit /b 0
