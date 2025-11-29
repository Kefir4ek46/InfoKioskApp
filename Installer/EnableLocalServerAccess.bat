@echo off
setlocal

echo Определение языка системы...

REM Получаем язык интерфейса Windows
for /f "tokens=2 delims==" %%i in ('wmic os get locale /value') do set LOCALE=%%i

REM Переводим HEX локаль в переменную
REM 0419 = Русский, 0409 = Английский (США)
if /i "%LOCALE%"=="0419" (
    set USERNAME=Все
    echo Обнаружена русская система. Использую пользователя: %USERNAME%
) else (
    set USERNAME=Everyone
    echo Обнаружена нерусская система. Использую пользователя: %USERNAME%
)

echo Настройка прав URLACL: http://+:8080/ ...

REM Удаляем старое правило
netsh http delete urlacl url=http://+:8080/ >nul 2>&1

REM Добавляем новое правило
netsh http add urlacl url=http://+:8080/ user=%USERNAME%

echo Готово!
endlocal
exit /b 0
