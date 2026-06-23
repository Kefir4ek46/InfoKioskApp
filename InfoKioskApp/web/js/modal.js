/* modal.js — модальные окна */
window.Modal = (() => {
  const backdrop = () => document.getElementById('modal-backdrop');
  const modal = () => document.getElementById('modal');

  function open(htmlOrNode, { onClose } = {}) {
    const m = modal();
    m.innerHTML = '';
    if (typeof htmlOrNode === 'string') m.innerHTML = htmlOrNode;
    else m.appendChild(htmlOrNode);
    backdrop().hidden = false;
    backdrop().dataset.onClose = onClose ? '1' : '0';
    backdrop()._onClose = onClose;

    // Сбрасываем scroll модалки наверх — на случай, если ранее открывали
    // другой пост и скроллили вниз. Без этого новый пост откроется на той же
    // позиции прокрутки.
    requestAnimationFrame(() => {
      try {
        m.scrollTop = 0;
        // Также сбросим scroll любого внутреннего скроллируемого контента.
        m.querySelectorAll('.post-viewer, .post-content, [data-scroll-reset]').forEach(el => {
          el.scrollTop = 0;
        });
      } catch (e) { /* ignore */ }
    });
  }
  function close() {
    backdrop().hidden = true;
    if (backdrop()._onClose) {
      try { backdrop()._onClose(); } catch (e) { console.error(e); }
      backdrop()._onClose = null;
    }
  }
  function init() {
    backdrop().addEventListener('click', (e) => {
      if (e.target === backdrop()) close();
    });
    document.addEventListener('keydown', (e) => {
      if (e.key === 'Escape' && !backdrop().hidden) close();
    });
  }
  // Авто-инициализация
  if (document.readyState !== 'loading') init();
  else document.addEventListener('DOMContentLoaded', init);

  return { open, close };
})();
