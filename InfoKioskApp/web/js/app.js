/* ============================================================
   app.js — главный контроллер киоска
   Надёжная навигация: graceful fallback, no View Transitions глюков.
   ============================================================ */

(() => {
  'use strict';

  // Реестр вьюх: { name → { mount, unmount, onConfig? } }
  // ВАЖНО: используем уже существующий window.KioskViews — его заполнили view-скрипты
  // (см. bootstrap-строку в index.html). Создание нового объекта затёрло бы регистрацию.
  const views = window.KioskViews = window.KioskViews || {};

  let currentView = null;
  let currentViewName = null;
  let config = null;
  let navigateInProgress = false;

  const $content = () => document.getElementById('kiosk-content');
  const $sidebar = () => document.getElementById('kiosk-sidebar');

  // ---------------------------- ROUTER ----------------------------
  async function navigate(name, params = {}) {
    if (navigateInProgress) {
      console.warn('[app] navigate already in progress, ignoring:', name);
      return;
    }

    // Проверяем блокировку расширения во время урока.
    // Если у плагина blockDuringLesson=true и сейчас идёт урок — показываем
    // экран «Доступно только на перемене».
    if (window.KioskPlugins && window.KioskPlugins[name] && window.KioskPlugins[name].blockDuringLesson) {
      if (isLessonActive()) {
        showLessonBlockScreen(name);
        return;
      }
    }

    if (!views[name]) {
      console.warn('[app] view not registered:', name, 'registered:', Object.keys(views));
      // Покажем ошибку, но не блокируем UI
      const c = $content();
      const known = Object.keys(views).join(', ') || '(нет)';
      if (c) c.innerHTML = `<div class="view-loader"><div class="loader-text">
        Вкладка «${escapeHtml(name)}» не найдена.<br>
        <small style="color:var(--text-dim);">Доступные: ${escapeHtml(known)}</small>
      </div></div>`;
      return;
    }

    navigateInProgress = true;

    // Если уходим с активной вьюхи — даём ей шанс размонтироваться
    if (currentView && currentView.unmount) {
      try { currentView.unmount(); } catch (e) { console.error('[app] unmount error:', e); }
    }

    // Подсветка кнопки меню
    document.querySelectorAll('.nav-btn').forEach(b => b.classList.remove('active'));
    const btn = document.querySelector(`.nav-btn[data-view="${name}"]`);
    if (btn) btn.classList.add('active');

    currentViewName = name;
    currentView = views[name];

    // Сначала ставим спиннер, чтобы пользователь видел прогресс
    const c = $content();
    if (c) c.innerHTML = '<div class="view-loader"><div class="spinner"></div><div class="loader-text">Загрузка…</div></div>';

    try {
      await currentView.mount(c, params);
    } catch (e) {
      console.error('[app] view mount error for', name, e);
      if (c) c.innerHTML = `<div class="view-loader"><div class="loader-text">Ошибка загрузки вкладки «${name}»:<br>${(e && e.message) || e}</div></div>`;
    }

    navigateInProgress = false;
  }

  // Проверяет, идёт ли сейчас урок по расписанию звонков.
  // Использует bell API (window.Clock хранит bell в памяти, но мы читаем заново).
  function isLessonActive() {
    try {
      // Синхронно проверяем через bell, сохранённый в Clock.
      if (!window.Clock) return false;
      // Clock.findLessonStatus не экспортирован — используем простой подход:
      // читаем текст бейджа урока. Если "Идёт урок" — урок активен.
      const labelEl = document.getElementById('lesson-label');
      if (labelEl) {
        const txt = labelEl.textContent.trim();
        if (txt === 'Идёт урок') return true;
      }
      return false;
    } catch (e) {
      console.warn('[app] isLessonActive failed', e);
      return false;
    }
  }

  // Экран блокировки расширения во время урока.
  function showLessonBlockScreen(name) {
    const c = $content();
    if (!c) return;
    // Снимаем подсветку с других кнопок.
    document.querySelectorAll('.nav-btn').forEach(b => b.classList.remove('active'));
    const plugin = window.KioskPlugins[name] || {};
    const icon = plugin.icon || '🔒';
    const pluginName = plugin.name || name;
    c.innerHTML = `
      <div class="view">
        <div class="lesson-block-screen">
          <div class="lesson-block-icon">${icon}</div>
          <h2 class="lesson-block-title">${escapeHtml(pluginName)} недоступно</h2>
          <p class="lesson-block-text">Сейчас идёт урок. Это расширение заблокировано до конца урока.</p>
          <p class="lesson-block-hint">Возвращайтесь на перемене! 🎮</p>
          <button class="header-btn lesson-block-btn" onclick="window.KioskApp.navigate('schedule')" style="width:auto;padding:10px 24px;">← К расписанию</button>
        </div>
      </div>
    `;
  }

  // ---------------------------- CONFIG ----------------------------
  async function loadConfig() {
    try {
      config = await window.kiosk.call('config.get');
    } catch (e) {
      console.error('[app] config.get failed:', e);
      config = {};
    }
    try {
      applyConfig(config);
    } catch (e) {
      console.error('[app] applyConfig failed:', e);
    }
    return config;
  }

  function applyConfig(cfg) {
    if (!cfg) return;
    // Тема — применяем все цвета и шрифты из InterfaceSettings.
    try {
      if (cfg.InterfaceSettings) {
        const isi = cfg.InterfaceSettings;
        const root = document.documentElement;
        if (isi.BackgroundColor) root.style.setProperty('--bg', isi.BackgroundColor);
        if (isi.PanelColor || isi.BackgroundColor) root.style.setProperty('--panel', isi.PanelColor || '#252525');
        if (isi.PanelColor) root.style.setProperty('--panel-2', isi.PanelColor);
        if (isi.AccentColor) root.style.setProperty('--accent', isi.AccentColor);
        if (isi.ButtonBackground) root.style.setProperty('--button', isi.ButtonBackground);
        if (isi.ButtonForeground) root.style.setProperty('--text', isi.ButtonForeground);
        if (isi.BorderColor) root.style.setProperty('--border', isi.BorderColor);
        if (isi.FontFamily) root.style.setProperty('--font', isi.FontFamily + ', sans-serif');
        if (isi.FontSize) root.style.fontSize = isi.FontSize + 'px';
      }
    } catch (e) { console.warn('[app] theme apply failed:', e); }

    // Тикер
    try {
      const ticker = document.getElementById('kiosk-ticker');
      if (cfg.Ticker && window.Ticker) {
        ticker.hidden = !(cfg.Ticker.Enabled ?? false);
        if (cfg.Ticker.Foreground) ticker.style.color = cfg.Ticker.Foreground;
        if (cfg.Ticker.Speed) window.Ticker.setSpeed(cfg.Ticker.Speed);
        const items = cfg.Ticker.Items && cfg.Ticker.Items.length
          ? cfg.Ticker.Items
          : (cfg.Ticker.Text ? [cfg.Ticker.Text] : []);
        window.Ticker.setItems(items);
      } else if (ticker) {
        ticker.hidden = true;
      }
    } catch (e) { console.warn('[app] ticker apply failed:', e); }

    // Idle
    try {
      if (cfg.IdleScreen && window.Idle) {
        window.Idle.setEnabled(cfg.IdleScreen.Enabled ?? true);
        window.Idle.setTimeout((cfg.IdleScreen.TimeoutSeconds ?? 90) * 1000);
        window.Idle.setSlideDuration((cfg.IdleScreen.SlideDurationSeconds ?? 8) * 1000);
        window.Idle.setLogoOnly(cfg.IdleScreen.ShowLogoOnly ?? false);
      }
    } catch (e) { console.warn('[app] idle apply failed:', e); }

    // Кастомные разделы
    try {
      rebuildCustomSections(cfg.CustomSections || []);
    } catch (e) { console.warn('[app] custom sections apply failed:', e); }
  }

  function rebuildCustomSections(sections) {
    const sidebar = $sidebar();
    if (!sidebar) return;
    // Удаляем старые кастомные
    sidebar.querySelectorAll('.nav-btn[data-custom]').forEach(b => b.remove());
    sections.forEach((s) => {
      const btn = document.createElement('button');
      btn.className = 'nav-btn';
      btn.dataset.custom = '1';
      btn.dataset.section = s.folderPath || s.FolderPath || '';
      btn.innerHTML = `<span class="nav-ico">${s.Icon || '📁'}</span><span class="nav-text">${s.Name || s.name || 'Раздел'}</span>`;
      btn.addEventListener('click', () => navigate('custom', { section: s }));
      sidebar.appendChild(btn);
    });
  }

  // ---------------------------- PLUGINS ----------------------------
  // Подгрузка JS-плагинов из папки plugins/. Каждый плагин описан в
  // plugins/<id>/plugin.json и содержит view.js (+ опц. view.css).
  // PluginManager отдаёт их список через kiosk.call('plugins.list').
  // Скрипты и стили инжектятся в <head> по требованию — ленивая загрузка.
  const _loadedPluginAssets = new Set();
  // Счётчик перезагрузок — добавляется к URL как cache-buster,
  // чтобы при горячей перезагрузке расширений обновлённые view.js/view.css
  // точно перезагрузились, а не взялись из кэша браузера.
  let _pluginReloadCounter = 0;
  async function loadPluginAssets(descriptor) {
    if (!descriptor) return;
    const bust = _pluginReloadCounter > 0 ? `?v=${_pluginReloadCounter}` : '';
    if (descriptor.viewCssUrl && !_loadedPluginAssets.has(descriptor.viewCssUrl + bust)) {
      _loadedPluginAssets.add(descriptor.viewCssUrl + bust);
      try {
        const link = document.createElement('link');
        link.rel = 'stylesheet';
        link.href = descriptor.viewCssUrl + bust;
        document.head.appendChild(link);
      } catch (e) { console.warn('[app] plugin css load failed', e); }
    }
    if (descriptor.viewJsUrl && !_loadedPluginAssets.has(descriptor.viewJsUrl + bust)) {
      _loadedPluginAssets.add(descriptor.viewJsUrl + bust);
      try {
        await new Promise((resolve, reject) => {
          const s = document.createElement('script');
          s.src = descriptor.viewJsUrl + bust;
          s.async = false; // сохранить порядок выполнения
          s.onload = resolve;
          s.onerror = () => reject(new Error('Failed to load ' + descriptor.viewJsUrl));
          document.head.appendChild(s);
        });
      } catch (e) { console.warn('[app] plugin js load failed', e); }
    }
  }

  async function loadPlugins() {
    try {
      const list = await window.kiosk.call('plugins.list');
      if (!Array.isArray(list) || !list.length) return;

      // Сохраняем дескрипторы глобально, чтобы view-скрипты могли
      // получить доступ к своим настройкам (window.KioskPlugins[id].settings).
      window.KioskPlugins = {};
      list.forEach(p => { window.KioskPlugins[p.id] = p; });

      const sidebar = $sidebar();
      if (!sidebar) return;

      // Фильтруем выключенные расширения (enabled === false).
      // showInSidebar=false означает «не показывать в основном сайдбаре»,
      // но расширение всё равно может быть в «Дополнительно» (showInMore=true).
      const enabled = list.filter(p => p.enabled !== false);

      // Разделяем на обычные (в сайдбаре) и те, что идут в подменю «Дополнительно».
      // В основной сайдбар идут расширения с showInSidebar!==false и showInMore!==true.
      // В «Дополнительно» — с showInMore===true (независимо от showInSidebar).
      const sidebarPlugins = enabled.filter(p => p.showInSidebar !== false && !p.showInMore);
      const morePlugins = enabled.filter(p => p.showInMore);

      // Группируем обычные по position:
      //   "start"           — вставить в начало (перед "Расписание")
      //   "after-schedule"  — после кнопки "Расписание"
      //   "after-news"      — после кнопки "Новости"
      //   "after-calendar"  — после кнопки "Календарь"
      //   "end" (по умолчанию) — в конец сайдбара
      const byPos = { start: [], 'after-schedule': [], 'after-news': [], 'after-calendar': [], end: [] };
      sidebarPlugins.forEach(p => {
        const pos = p.position || 'end';
        if (!byPos[pos]) byPos[pos] = [];
        byPos[pos].push(p);
      });
      // Внутри каждой группы сортируем по order.
      Object.values(byPos).forEach(arr => arr.sort((a, b) => (a.order || 100) - (b.order || 100)));
      // Расширения «Дополнительно» тоже сортируем.
      morePlugins.sort((a, b) => (a.order || 100) - (b.order || 100));

      // Находим anchor-кнопки в сайдбаре.
      const sidebarBtns = Array.from(sidebar.querySelectorAll('.nav-btn:not([data-plugin]):not([data-more-btn])'));
      const scheduleBtn = sidebarBtns.find(b => b.dataset.view === 'schedule');
      const newsBtn = sidebarBtns.find(b => b.dataset.view === 'news');
      const calendarBtn = sidebarBtns.find(b => b.dataset.view === 'calendar');

      // Создаёт кнопку-расширение.
      const makeBtn = (p) => {
        const btn = document.createElement('button');
        btn.className = 'nav-btn';
        btn.dataset.view = p.id;
        btn.dataset.plugin = '1';
        btn.innerHTML = `<span class="nav-ico">${p.icon || '🔌'}</span><span class="nav-text">${p.name || p.id}</span>`;
        btn.addEventListener('click', async () => {
          try { await loadPluginAssets(p); } catch (e) { console.warn('[app] plugin asset load failed', e); }
          navigate(p.id);
        });
        return btn;
      };

      // Вставка кнопки после referenceNode.
      const insertAfter = (referenceNode, btn) => {
        if (referenceNode && referenceNode.nextSibling) {
          sidebar.insertBefore(btn, referenceNode.nextSibling);
        } else {
          sidebar.appendChild(btn);
        }
      };

      // "start" — перед "Расписание".
      byPos.start.forEach(p => {
        const btn = makeBtn(p);
        if (scheduleBtn) sidebar.insertBefore(btn, scheduleBtn);
        else sidebar.insertBefore(btn, sidebar.firstChild);
      });
      // "after-schedule".
      let schedRef = scheduleBtn;
      byPos['after-schedule'].forEach(p => {
        const btn = makeBtn(p);
        insertAfter(schedRef, btn);
        schedRef = btn;
      });
      // "after-news".
      let newsRef = newsBtn;
      byPos['after-news'].forEach(p => {
        const btn = makeBtn(p);
        insertAfter(newsRef, btn);
        newsRef = btn;
      });
      // "after-calendar".
      let calRef = calendarBtn;
      byPos['after-calendar'].forEach(p => {
        const btn = makeBtn(p);
        insertAfter(calRef, btn);
        calRef = btn;
      });
      // "end" — в конец.
      byPos.end.forEach(p => {
        const btn = makeBtn(p);
        sidebar.appendChild(btn);
      });

      // Подменю «Дополнительно» — отдельная кнопка, открывает панель со списком.
      if (morePlugins.length) {
        const moreBtn = document.createElement('button');
        moreBtn.className = 'nav-btn nav-more-btn';
        moreBtn.dataset.moreBtn = '1';
        moreBtn.innerHTML = `<span class="nav-ico">✨</span><span class="nav-text">Дополнительно</span>`;
        moreBtn.addEventListener('click', () => openMorePanel(morePlugins));
        sidebar.appendChild(moreBtn);
      }

      // Статические nav-btn уже привязаны в wireNavButtons() — плагинные кнопки
      // имеют свой обработчик выше, их не нужно перепривязывать.
    } catch (e) {
      console.warn('[app] loadPlugins failed', e);
    }
  }

  // Открывает панель «Дополнительно» — оверлей с сеткой кнопок расширений.
  function openMorePanel(plugins) {
    // Закрываем предыдущую, если есть.
    closeMorePanel();
    const overlay = document.createElement('div');
    overlay.className = 'more-panel-overlay';
    overlay.id = 'more-panel-overlay';
    overlay.innerHTML = `
      <div class="more-panel">
        <div class="more-panel-header">
          <h2>✨ Дополнительно</h2>
          <button class="more-panel-close" onclick="window.KioskApp.closeMorePanel()">✕</button>
        </div>
        <div class="more-panel-grid">
          ${plugins.map(p => `
            <button class="more-panel-card" data-plugin-id="${escapeHtml(p.id)}">
              <span class="more-panel-card-icon">${p.icon || '🔌'}</span>
              <span class="more-panel-card-name">${escapeHtml(p.name || p.id)}</span>
            </button>
          `).join('')}
        </div>
      </div>
    `;
    document.body.appendChild(overlay);
    // Клики по карточкам.
    overlay.querySelectorAll('.more-panel-card').forEach(card => {
      card.addEventListener('click', async () => {
        const id = card.dataset.pluginId;
        const p = plugins.find(x => x.id === id);
        if (!p) return;
        try { await loadPluginAssets(p); } catch (e) { console.warn('[app] plugin asset load failed', e); }
        closeMorePanel();
        navigate(p.id);
      });
    });
    // Закрытие по клику на фон.
    overlay.addEventListener('click', (e) => {
      if (e.target === overlay) closeMorePanel();
    });
  }

  function closeMorePanel() {
    const overlay = document.getElementById('more-panel-overlay');
    if (overlay) overlay.remove();
  }

  // Горячая перезагрузка расширений: удаляем старые кнопки из сайдбара
  // (data-plugin и кнопку «Дополнительно»), заново запрашиваем plugins.list
  // и перестраиваем сайдбар. Текущая вкладка не сбрасывается (если она ещё
  // активна — остаётся; если удалена — переключаемся на расписание).
  async function reloadPlugins() {
    try {
      const sidebar = $sidebar();
      if (!sidebar) return;
      // Удаляем все кнопки расширений и кнопку «Дополнительно».
      sidebar.querySelectorAll('[data-plugin], [data-more-btn]').forEach(b => b.remove());
      // Увеличиваем счётчик перезагрузок — это заставит loadPluginAssets
      // добавить ?v=<counter> к URL и перезагрузить обновлённые скрипты.
      _pluginReloadCounter++;
      _loadedPluginAssets.clear();
      // Перечитываем список с сервера.
      await loadPlugins();

      // Если текущая вкладка — это удалённое расширение, переключаемся на расписание.
      if (currentViewName && window.KioskPlugins && !window.KioskPlugins[currentViewName]
          && !views[currentViewName]) {
        console.log('[app] current view was removed, navigating to schedule');
        navigate('schedule');
      }
    } catch (e) {
      console.error('[app] reloadPlugins error:', e);
    }
  }

  // ---------------------------- NAV WIRING ----------------------------
  function wireNavButtons() {
    document.querySelectorAll('.nav-btn[data-view]').forEach(btn => {
      btn.addEventListener('click', () => navigate(btn.dataset.view));
    });
  }

  // ---------------------------- INIT ----------------------------
  async function init() {
    console.log('[app] init start');

    // Ждём, пока C#-bridge инжектирует window.kiosk (с таймаутом 8с)
    if (!window.kiosk) {
      console.log('[app] waiting for kiosk bridge…');
      try {
        await new Promise((resolve, reject) => {
          const timer = setTimeout(() => {
            reject(new Error('kiosk bridge timeout'));
          }, 8000);
          window.addEventListener('kiosk:ready', () => {
            clearTimeout(timer);
            resolve();
          }, { once: true });
        });
      } catch (e) {
        console.error('[app] bridge not ready:', e);
        const c = $content();
        if (c) c.innerHTML = `<div class="view-loader">
          <div class="loader-text" style="color:#ff8a7a;">
            Bridge не отвечает.<br>
            Перезапустите приложение.
          </div></div>`;
        return;
      }
    }
    console.log('[app] kiosk bridge ready');

    // Проверка, что bridge реально работает
    try {
      const ping = await window.kiosk.call('ping', {}, 3000);
      console.log('[app] bridge ping OK:', ping);
    } catch (e) {
      console.error('[app] bridge ping failed:', e);
    }

    // Слушаем пуш-события из C#
    window.kiosk.on('config.changed', (cfg) => {
      console.log('[app] config changed (push)');
      config = cfg;
      applyConfig(cfg);
      if (currentView && currentView.onConfig) {
        try { currentView.onConfig(cfg); } catch (e) { console.error(e); }
      }
    });
    window.kiosk.on('data.changed', ({ section }) => {
      console.log('[app] data changed (push):', section);
      if (currentView && currentView.onDataChanged) {
        try { currentView.onDataChanged(section); } catch (e) { console.error(e); }
      }
      // При изменении звонков — перезагружаем bell в clock.
      if (section === 'bell') {
        console.log('[app] bell changed, reloading...');
        if (window.Clock && window.Clock.reloadBell) {
          try { window.Clock.reloadBell(); } catch (e) { console.error(e); }
        }
      }
      // При изменении списка расширений — горячая перезагрузка сайдбара.
      if (section === 'extensions') {
        console.log('[app] extensions changed, reloading sidebar...');
        try { reloadPlugins(); } catch (e) { console.error('[app] reloadPlugins failed:', e); }
      }
    });

    // Стартуем модули (каждый в try-catch, чтобы один сбой не ронял остальных)
    try { window.Clock.start();   } catch (e) { console.error('[app] Clock.start failed:', e); }
    try { window.Weather.start(); } catch (e) { console.error('[app] Weather.start failed:', e); }
    try { window.Ticker.start();  } catch (e) { console.error('[app] Ticker.start failed:', e); }
    try { window.Idle.start();    } catch (e) { console.error('[app] Idle.start failed:', e); }
    try { window.AdminAccess.init(); } catch (e) { console.error('[app] AdminAccess.init failed:', e); }

    try { wireNavButtons(); } catch (e) { console.error('[app] wireNavButtons failed:', e); }

    try { await loadConfig(); } catch (e) { console.error('[app] loadConfig failed:', e); }

    // Подгрузка плагинов — после конфига, чтобы плагины видели актуальные настройки.
    try { await loadPlugins(); } catch (e) { console.error('[app] loadPlugins failed:', e); }

    // Стартовая вкладка — расписание. Если упадёт, UI всё равно живой.
    try {
      await navigate('schedule');
    } catch (e) {
      console.error('[app] initial navigate failed:', e);
    }

    console.log('[app] init done');
  }

  // ---------------------------- EXPORT ----------------------------
  window.KioskApp = {
    init,
    navigate,
    getConfig: () => config,
    toast: (msg, type = 'info', ms = 3500) => window.Toast && window.Toast.show(msg, type, ms),
    closeMorePanel,
  };

  // Запуск
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
