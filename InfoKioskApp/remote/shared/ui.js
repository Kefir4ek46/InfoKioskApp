/* shared/ui.js — общие UI-хелперы для admin/editor */

window.UI = (() => {
  function toast(msg, type = 'info', ms = 3500) {
    const c = document.getElementById('toast');
    if (!c) { alert(msg); return; }
    const t = document.createElement('div');
    t.className = 'toast ' + type;
    t.textContent = msg;
    c.appendChild(t);
    setTimeout(() => {
      t.style.transition = 'opacity .2s, transform .2s';
      t.style.opacity = '0';
      t.style.transform = 'translateX(40px)';
      setTimeout(() => t.remove(), 220);
    }, ms);
  }

  function confirmDialog(text) {
    return new Promise(resolve => {
      const backdrop = document.createElement('div');
      backdrop.className = 'modal-backdrop';
      backdrop.innerHTML = `
        <div class="modal-box">
          <h3>Подтверждение</h3>
          <div>${escapeHtml(text)}</div>
          <div class="modal-actions">
            <button class="btn-secondary" data-act="no">Отмена</button>
            <button class="btn-danger" data-act="yes">OK</button>
          </div>
        </div>`;
      document.body.appendChild(backdrop);
      backdrop.addEventListener('click', (e) => {
        if (e.target === backdrop || e.target.dataset.act === 'no') {
          backdrop.remove();
          resolve(false);
        } else if (e.target.dataset.act === 'yes') {
          backdrop.remove();
          resolve(true);
        }
      });
    });
  }

  function promptDialog(text, def = '') {
    return new Promise(resolve => {
      const backdrop = document.createElement('div');
      backdrop.className = 'modal-backdrop';
      backdrop.innerHTML = `
        <div class="modal-box">
          <h3>${escapeHtml(text)}</h3>
          <input type="text" class="select" style="margin:8px 0 0;" value="${escapeHtml(def)}">
          <div class="modal-actions">
            <button class="btn-secondary" data-act="no">Отмена</button>
            <button class="btn-primary" data-act="yes">OK</button>
          </div>
        </div>`;
      document.body.appendChild(backdrop);
      const input = backdrop.querySelector('input');
      input.focus(); input.select();
      const submit = () => { backdrop.remove(); resolve(input.value); };
      backdrop.addEventListener('click', (e) => {
        if (e.target === backdrop || e.target.dataset.act === 'no') {
          backdrop.remove(); resolve(null);
        } else if (e.target.dataset.act === 'yes') {
          submit();
        }
      });
      input.addEventListener('keydown', (e) => { if (e.key === 'Enter') submit(); });
    });
  }

  function escapeHtml(s) {
    if (s == null) return '';
    return String(s).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  }

  function pluralRu(n, forms) {
    const n10 = n % 10, n100 = n % 100;
    if (n10 === 1 && n100 !== 11) return forms[0];
    if (n10 >= 2 && n10 <= 4 && (n100 < 10 || n100 >= 20)) return forms[1];
    return forms[2];
  }

  function fmtDate(iso) {
    if (!iso) return '';
    try { return new Date(iso).toLocaleString('ru-RU', { day:'numeric', month:'long', year:'numeric', hour:'2-digit', minute:'2-digit' }); }
    catch { return iso; }
  }

  return { toast, confirmDialog, promptDialog, escapeHtml, pluralRu, fmtDate };
})();
