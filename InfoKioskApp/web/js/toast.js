/* toast.js — уведомления */
window.Toast = (() => {
  const container = () => document.getElementById('toast-container');

  function show(msg, type = 'info', ms = 3500) {
    const c = container();
    if (!c) return;
    const t = document.createElement('div');
    t.className = 'toast ' + type;
    t.textContent = msg;
    c.appendChild(t);
    setTimeout(() => {
      t.classList.add('out');
      setTimeout(() => t.remove(), 200);
    }, ms);
  }
  return { show };
})();
