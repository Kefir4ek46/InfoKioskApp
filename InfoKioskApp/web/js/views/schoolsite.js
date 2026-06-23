/* views/schoolsite.js — сайт школы открывается прямо в киоске (в основном WebView2).
 *
 * Раньше сайт открывался в отдельном WPF-оверлее со своим WebView2, но это
 * вызывало SEHException при инициализации. Теперь сайт открывается прямо
 * в основном WebView2 киоска — NavigationStarting в MainWindow разрешает
 * хост obo-afan.gosuslugi.ru.
 *
 * Когда сайт загружен, поверх него показывается плавающая кнопка
 * «← Назад в киоск» (через CSS position:fixed). При клике — навигируем
 * обратно на localhost.
 */
window.KioskViews.schoolsite = (() => {
  'use strict';

  async function mount($el) {
    $el.innerHTML = `
      <div class="view">
        <div class="view-header">
          <div>
            <div class="view-title">Сайт школы</div>
            <div class="view-subtitle">Открываем…</div>
          </div>
        </div>
        <div class="canteen-empty">
          <div class="spinner"></div>
          <div style="margin-top:14px;color:var(--text-muted);">Переходим на сайт школы…</div>
        </div>
      </div>
    `;

    // URL из конфига или дефолтный.
    let url = 'https://obo-afan.gosuslugi.ru';
    try {
      const cfg = window.KioskApp && window.KioskApp.getConfig
        ? window.KioskApp.getConfig()
        : null;
      if (cfg && cfg.SchoolSiteUrl) url = cfg.SchoolSiteUrl;
    } catch {}

    // Навигируем основной WebView киоска на сайт школы.
    // Bridge 'window.navigate' вызывает C# _core.Navigate(url).
    // NavigationStarting в MainWindow разрешает obo-afan.gosuslugi.ru.
    try {
      if (!window.kiosk || typeof window.kiosk.call !== 'function') {
        throw new Error('kiosk bridge not available');
      }
      // Используем window.navigate — он вызовет C# Navigate() в UI-потоке.
      // Но window.navigate разрешает только loopback URL! Сайт школы — внешний.
      // Поэтому используем специальный bridge 'schoolsite.navigate'.
      const r = await window.kiosk.call('schoolsite.navigate', { url }, 5000);
      if (!r || r.ok === false) {
        throw new Error((r && r.error) || 'C# не смог открыть сайт');
      }
      // Сайт откроется в этом же WebView. Киоск-UI будет заменён сайтом.
      // Плавающая кнопка "Назад" будет показана через инжекцию JS
      // (см. schoolsite.navigate handler в WebMessageBridge).
    } catch (e) {
      $el.innerHTML = `
        <div class="view">
          <div class="view-header">
            <div>
              <div class="view-title">Сайт школы</div>
              <div class="view-subtitle">Не удалось открыть</div>
            </div>
          </div>
          <div class="canteen-empty">
            <div style="font-size: 48px; margin-bottom: 14px;">🌐</div>
            <div style="margin-bottom: 8px; font-size: 16px;">Не удалось открыть сайт школы.</div>
            <div style="font-size: 13px; color: var(--text-muted); max-width: 460px;">
              ${escapeHtml(e.message || 'Ошибка')}
            </div>
          </div>
        </div>
      `;
    }
  }

  return { mount };
})();
