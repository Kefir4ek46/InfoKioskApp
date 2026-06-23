/* plugins/canteen/view.js — меню столовой (вынесено из киоска в плагин).
 *
 * Дата-навигация, читаем xlsx через bridge-команду 'canteen.menu'.
 * Файлы могут иметь имена: yyyy-mm-dd.xlsx, yyyy-mm-dd-sm.xlsx и т.д.
 */
window.KioskViews = window.KioskViews || {};

window.KioskViews['canteen'] = (() => {
  'use strict';

  let state = { date: new Date(), data: null };

  function fmt(d) { return d.toISOString().slice(0,10); }
  function fmtRu(d) {
    const months = ['января','февраля','марта','апреля','мая','июня','июля','августа','сентября','октября','ноября','декабря'];
    return `${d.getDate()} ${months[d.getMonth()]} ${d.getFullYear()}`;
  }

  function shift(days) {
    const d = new Date(state.date);
    d.setDate(d.getDate() + days);
    state.date = d;
    return load();
  }

  async function load() {
    const dateStr = fmt(state.date);
    try {
      state.data = await window.kiosk.call('canteen.menu', { date: dateStr });
    } catch (e) {
      state.data = { error: e.message };
    }
    render(document.getElementById('kiosk-content'));
  }

  function escapeHtml(s) {
    if (s == null) return '';
    return String(s).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  }

  function render($el) {
    const d = state.data || {};
    // Описание подзаголовка из настроек расширения (если задано).
    const pluginDesc = window.KioskPlugins && window.KioskPlugins.canteen
      ? (window.KioskPlugins.canteen.settings || {}).description
      : '';
    const subtitle = pluginDesc || 'Меню на день';

    const rows = d.rows && d.rows.length
      ? `<table class="canteen-table">
          <thead><tr>${(d.columns||[]).map(c => `<th>${escapeHtml(c)}</th>`).join('')}</tr></thead>
          <tbody>${d.rows.map(r => `<tr>${r.map(c => `<td>${escapeHtml(c)}</td>`).join('')}</tr>`).join('')}</tbody>
        </table>`
      : `<div class="canteen-empty">${escapeHtml(d.message || d.error || 'На этот день меню не загружено')}</div>`;

    $el.innerHTML = `
      <div class="view">
        <div class="view-header">
          <div>
            <div class="view-title">Столовая</div>
            <div class="view-subtitle">${escapeHtml(subtitle)}</div>
          </div>
        </div>
        <div class="canteen-date-nav">
          <button class="canteen-arrow" data-d="-1">‹</button>
          <div class="canteen-date">${fmtRu(state.date)}</div>
          <button class="canteen-arrow" data-d="+1">›</button>
          <button class="canteen-arrow" data-today title="Сегодня">●</button>
        </div>
        ${rows}
      </div>
    `;
    $el.querySelectorAll('.canteen-arrow').forEach(b => {
      b.addEventListener('click', () => {
        if (b.dataset.today !== undefined) {
          state.date = new Date();
          load();
        } else {
          shift(parseInt(b.dataset.d, 10) || 0);
        }
      });
    });
  }

  return {
    async mount($el) {
      $el.innerHTML = '<div class="view-loader"><div class="spinner"></div><div class="loader-text">Загрузка меню…</div></div>';
      await load();
    },
    onDataChanged(section) {
      if (section === 'canteen') load();
    }
  };
})();
