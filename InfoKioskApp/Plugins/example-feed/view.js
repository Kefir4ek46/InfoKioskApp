/* plugins/example-feed/view.js
 * Шаблон вкладки плагина. PluginManager при старте киоска:
 *   1) читает plugins/<id>/plugin.json,
 *   2) если showInSidebar=true — добавляет кнопку в сайдбар с data-view="<id>",
 *   3) подгружает этот файл через <script src="/plugins/<id>/view.js">,
 *   4) вкладка становится доступна через window.KioskViews.<id>.
 *
 * Для вызова C#-плагина (если needsBackend=true):
 *   const data = await window.kiosk.call('plugin.<id>.<method>', { ... });
 *
 * Этот пример просто показывает приветствие и список «постов».
 */
window.KioskViews = window.KioskViews || {};

window.KioskViews['example-feed'] = (() => {
  'use strict';

  const POSTS = [
    { id: 1, title: 'Добро пожаловать', body: 'Это пример вкладки, добавленной плагином. Скопируйте папку plugins/example-feed и адаптируйте под себя.' },
    { id: 2, title: 'Как вызывать бэкенд', body: 'window.kiosk.call("plugin.<id>.<method>", { ... }) — метод попадёт в C# IPlugin.HandleCall.' },
    { id: 3, title: 'Свои стили', body: 'Положите view.css рядом с view.js — он подгрузится автоматически.' }
  ];

  function escapeHtml(s) {
    if (s == null) return '';
    return String(s).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  }

  function render($el) {
    $el.innerHTML = `
      <div class="view">
        <div class="view-header">
          <div>
            <div class="view-title">Пример плагина</div>
            <div class="view-subtitle">Демонстрация системы плагинов</div>
          </div>
        </div>
        <div class="news-list">
          ${POSTS.map(p => `
            <article class="news-card">
              <h3 class="news-title">${escapeHtml(p.title)}</h3>
              <p class="news-body">${escapeHtml(p.body)}</p>
            </article>
          `).join('')}
        </div>
      </div>
    `;
  }

  return {
    async mount($el) {
      $el.innerHTML = '<div class="view-loader"><div class="spinner"></div><div class="loader-text">Загрузка плагина…</div></div>';
      // Небольшая задержка, чтобы показать спиннер (в реальном плагине тут был бы fetch).
      await new Promise(r => setTimeout(r, 200));
      render($el);
    }
  };
})();
