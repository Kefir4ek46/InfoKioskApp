# InfoKiosk — гибридная архитектура (WPF + WebView2)

Перенос интерфейса школьного инфокиоска с WPF/XAML на HTML+CSS+JS
при сохранении всей C#-бизнес-логики, хранилища данных и апдейтера.

## Что в архиве

```
InfoKioskApp/                     ← решение (.sln)
├── InfoKioskApp/                 ← WPF shell с WebView2
│   ├── MainWindow.xaml(.cs)      ← единственный XAML: <wv2:WebView2> на весь экран
│   ├── Services/
│   │   ├── WebMessageBridge.cs   ← НОВОЕ: диспетчер JS ⇄ C#
│   │   ├── RemoteServerService.cs← пропатчен: раздаёт web/ и remote/
│   │   ├── ConfigService.cs
│   │   ├── CalendarService.cs
│   │   ├── WeatherService.cs
│   │   ├── DocxRenderer.cs
│   │   └── SystemInfoService.cs
│   ├── Models/                   ← без изменений (AppConfig, NewsPost, HonorPerson, ...)
│   ├── web/                      ← НОВОЕ: HTML-киоск
│   │   ├── index.html
│   │   ├── css/   (main, views, animations, fonts)
│   │   ├── js/    (app, clock, weather, ticker, idle, fullscreen, modal, toast)
│   │   ├── js/views/ (schedule, canteen, media, news, honor, documents, calendar, schoolsite, custom)
│   │   └── assets/ (fonts/, idle/)
│   ├── remote/                   ← НОВОЕ: HTML-админка по LAN
│   │   ├── admin.html            ← по PIN-коду
│   │   ├── editor.html           ← для редакторов новостей
│   │   ├── admin.js, editor.js
│   │   └── shared/ (style.css, api.js, ui.js)
│   ├── data/                     ← демо-данные (config.json, BellSchedule.json, ...)
│   ├── AutoStartKiosk.bat        ← запуск киоска + watchdog
│   ├── Installer/
│   │   ├── CreateAutoStartTask.bat  ← поставить в Task Scheduler
│   │   └── RemoveAutoStartTask.bat
│   └── InfoKioskInstaller.iss    ← Inno Setup (без изменений)
├── InfoKioskUpdater/             ← GitHub Releases updater (без изменений)
└── InfoKioskWatchdog/            ← НОВОЕ: сторожевой процесс
    ├── Program.cs
    └── InfoKioskWatchdog.csproj
```

## Сборка

```powershell
dotnet build InfoKioskApp.sln -c Release
# или открыть InfoKioskApp.sln в Visual Studio 2022 и нажать F5
```

Требуется:
- .NET 9 SDK (или Visual Studio 2022 17.8+ с workload .NET desktop)
- Microsoft Edge WebView2 Runtime (Evergreen) — ставится отдельно,
  см. https://developer.microsoft.com/microsoft-edge/webview2/

## Запуск

После сборки:
1. Зайти в `bin/Release/net9.0-windows/`
2. Запустить `InfoKioskApp.exe` — откроется окно на весь экран с HTML-киоском
3. С любого устройства в локальной сети:
   - `http://<IP-киоска>:8080/admin.html` — админка (PIN `1234` по умолчанию)
   - `http://<IP-киоска>:8080/editor.html` — для редакторов
   - `http://<IP-киоска>:8080/` — сам киоск (можно открыть с любого браузера)

## Архитектура JS ⇄ C#

JS в WebView2 вызывает C# через `window.kiosk.call(type, data)`:

```js
const cfg    = await window.kiosk.call('config.get');
const menu   = await window.kiosk.call('canteen.menu', { date: '2026-06-18' });
const posts  = await window.kiosk.call('news.list');
```

C# пушит в JS:
```js
window.kiosk.on('config.changed', (cfg) => applyConfig(cfg));
window.kiosk.on('data.changed',   ({ section }) => reloadSection(section));
```

Поддерживаемые типы сообщений (см. `WebMessageBridge.DispatchAsync`):
- `config.get` / `config.save`
- `bell.get`
- `schedule.main` / `schedule.changes`
- `canteen.menu` ({date})
- `calendar.list`
- `news.list`
- `honor.list`
- `media.categories` / `media.posts` / `media.post`
- `documents.list`
- `weather.get` ({city})
- `customsections.list` / `customsection.files`
- `idle.images`
- `window.fullscreen` / `window.reload`
- `system.now` / `system.info`

## Watchdog

`InfoKioskWatchdog.exe` — отдельный консольный процесс:
- Каждые 10 секунд проверяет, жив ли `InfoKioskApp.exe`
- Если процесс исчез или завис (Not Responding) — перезапускает
- Лимит 30 перезапусков в час (защита от цикла падений)
- Установка в Task Scheduler:
  ```
  InfoKioskWatchdog.exe --install
  InfoKioskWatchdog.exe --uninstall
  ```

## Автозапуск

1. Запустить `Installer\CreateAutoStartTask.bat` от администратора
2. Создаст задачу "InfoKioskAutoStart" в Task Scheduler
3. При входе пользователя запускается `AutoStartKiosk.bat`, который
   поднимает киоск + watchdog

## Updater

`InfoKioskUpdater` — без изменений. Тянет релизы с GitHub:
```
https://api.github.com/repos/Kefir4ek46/InfoKioskApp/releases/latest
```
Эндпоинты `/api/update/*` в `RemoteServerService` доступны из админки.

## Где что менять

| Что | Где |
|-----|-----|
| Стили киоска | `web/css/main.css`, `web/css/views.css` |
| Поведение вкладки | `web/js/views/<name>.js` |
| Общий контроллер | `web/js/app.js` |
| Админка UI | `remote/admin.html`, `remote/admin.js` |
| Админка логика API | `remote/shared/api.js` |
| API киоска (новый метод) | `Services/WebMessageBridge.cs` → `DispatchAsync` |
| HTTP-эндпоинты | `Services/RemoteServerService.cs` → `HandleRequest` |
| Настройки по умолчанию | `data/config.json` |
