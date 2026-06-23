/* views/honor.js — почётная доска */
window.KioskViews.honor = (() => {
  let items = [];
  async function load() { items = (await window.kiosk.call('honor.list')) || []; }

  function render($el) {
    $el.innerHTML = `
      <div class="view">
        <div class="view-header">
          <div>
            <div class="view-title">Мы ими гордимся</div>
            <div class="view-subtitle">${items.length} ${pluralRu(items.length, ['человек','человека','человек'])}</div>
          </div>
        </div>
        ${items.length ? `<div class="honor-grid">
          ${items.map((p, i) => {
            const photo = p.PhotoFile ? `/download?target=honor&name=${encodeURIComponent(p.PhotoFile)}` : '';
            return `<div class="honor-card stagger-in hover-lift" style="--i:${i}">
              ${photo ? `<img class="honor-photo" src="${photo}" alt="">` : `<div class="honor-photo"></div>`}
              <div class="honor-name">${escapeHtml(p.FullName)}</div>
              ${p.Description ? `<div class="honor-desc">${escapeHtml(p.Description)}</div>` : ''}
            </div>`;
          }).join('')}
        </div>` : `<div class="canteen-empty">Список пуст</div>`}
      </div>
    `;
  }

  return {
    async mount($el) {
      $el.innerHTML = '<div class="view-loader"><div class="spinner"></div><div class="loader-text">Загрузка…</div></div>';
      await load();
      render($el);
    },
    onDataChanged(section) {
      if (section === 'honor') load().then(() => render(document.getElementById('kiosk-content')));
    }
  };
})();
