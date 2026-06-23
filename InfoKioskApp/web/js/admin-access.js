/* admin-access.js — кнопка входа в админку из киоска.
 *
 * FLOW:
 *   1) Пользователь жмёт «Админ» внизу-слева.
 *   2) Показываем PIN-форму (большая цифровая клавиатура — удобно на тачскрине).
 *   3) По «Войти» отправляем POST /auth/admin/login {pin}.
 *   4) При успехе просим C# открыть админку в ОТДЕЛЬНОМ окне
 *      (kiosk.call('admin.open', {token})) — киоск остаётся на месте.
 *   5) При ошибке показываем её в форме, не закрывая модалку.
 *
 * PIN-код берётся из config.PinCode (по умолчанию "1234"), меняется в админке.
 *
 * ВАЖНО про события на тачскрине:
 *   Ранее на кнопки PIN-пэда вешались И click, И touchstart/touchend.
 *   В WebView2 preventDefault() на touchstart не всегда подавляет последующий
 *   click → один тап генерировал два события → вводилось «11223344» вместо «1234».
 *   Теперь используем ТОЛЬКО click. CSS touch-action: manipulation убирает
 *   300мс задержку, так что отклик мгновенный.
 */
window.AdminAccess = (() => {
  'use strict';

  const $ = (id) => document.getElementById(id);

  let isOpen = false;
  let isSubmitting = false;

  function open() {
    if (isOpen) return;
    isOpen = true;
    const modal = $('pin-modal');
    if (!modal) {
      console.error('[admin-access] pin-modal element not found');
      return;
    }
    modal.hidden = false;
    const input = $('pin-modal-input');
    if (input) {
      input.value = '';
      setTimeout(() => { try { input.focus({ preventScroll: true }); } catch {} }, 50);
    }
    const err = $('pin-modal-error');
    if (err) {
      err.hidden = true;
      err.innerHTML = '';
    }
    const okBtn = $('pin-modal-ok');
    if (okBtn) {
      okBtn.disabled = false;
      okBtn.textContent = 'Войти';
    }
  }

  function close() {
    isOpen = false;
    const modal = $('pin-modal');
    if (modal) modal.hidden = true;
  }

  function showError(msg) {
    console.error('[admin-access]', msg);
    const err = $('pin-modal-error');
    if (!err) return;
    err.textContent = msg;
    err.hidden = false;
    err.style.animation = 'none';
    void err.offsetWidth;
    err.style.animation = '';
  }

  function setStatus(msg) {
    console.log('[admin-access]', msg);
    const okBtn = $('pin-modal-ok');
    if (okBtn) okBtn.textContent = msg;
  }

  function appendDigit(d) {
    const input = $('pin-modal-input');
    if (!input) return;
    if (input.value.length >= 20) return;
    input.value += d;
  }
  function backspace() {
    const input = $('pin-modal-input');
    if (!input) return;
    input.value = input.value.slice(0, -1);
  }
  function clearAll() {
    const input = $('pin-modal-input');
    if (!input) return;
    input.value = '';
  }

  // ====================================================================
  // Открытие админки в отдельном окне через C#-bridge
  // ====================================================================
  async function openAdminWindow(token) {
    try {
      if (!window.kiosk || typeof window.kiosk.call !== 'function') {
        console.warn('[admin-access] kiosk bridge not available');
        return false;
      }
      const r = await window.kiosk.call('admin.open', { token }, 5000);
      console.log('[admin-access] admin.open response:', r);
      return !!(r && r.ok);
    } catch (e) {
      console.warn('[admin-access] admin.open bridge call failed:', e);
      return false;
    }
  }

  // ====================================================================
  // Модалка управления окном — показывается ПОСЛЕ успешного ввода PIN.
  // Здесь пользователь выбирает: открыть админку / свернуть / закрыть.
  // Без ввода PIN кнопки свернуть/закрыть недоступны.
  // ====================================================================
  let lastToken = null;

  function showWindowControlModal(token) {
    lastToken = token;
    const modal = $('window-control-modal');
    if (!modal) return;
    modal.hidden = false;
  }

  function closeWindowControlModal() {
    const modal = $('window-control-modal');
    if (modal) modal.hidden = true;
    lastToken = null;
  }

  async function submit() {
    if (isSubmitting) return;
    const input = $('pin-modal-input');
    if (!input) {
      console.error('[admin-access] pin-modal-input not found');
      return;
    }
    const pin = input.value.trim();
    if (!pin) {
      showError('Введите PIN-код');
      return;
    }

    isSubmitting = true;
    const okBtn = $('pin-modal-ok');
    if (okBtn) { okBtn.disabled = true; okBtn.textContent = 'Проверка…'; }

    try {
      console.log('[admin-access] sending POST /auth/admin/login, pin length:', pin.length);
      const res = await fetch('/auth/admin/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ pin })
      });

      console.log('[admin-access] login response status:', res.status, res.statusText);
      let data = null;
      try { data = await res.json(); } catch (e) { console.warn('[admin-access] cannot parse json:', e); }
      console.log('[admin-access] login response body:', data);

      if (res.ok && data && data.token) {
        // Токен нужен для передачи в админ-окно (через C# bridge).
        try { localStorage.setItem('infokiosk.adminToken', data.token); } catch {}
        try { sessionStorage.setItem('infokiosk.adminToken', data.token); } catch {}

        // Закрываем PIN-модалку и показываем модалку управления.
        close();
        showWindowControlModal(data.token);
        return;
      }

      // Показываем понятную ошибку
      const errMsg = (data && data.error) || `Неверный PIN-код (HTTP ${res.status})`;
      showError(errMsg);

      // Очистим поле
      const inp = $('pin-modal-input');
      if (inp) {
        inp.value = '';
        setTimeout(() => { try { inp.focus({ preventScroll: true }); } catch {} }, 50);
      }
    } catch (e) {
      console.error('[admin-access] submit error:', e);
      showError('Не удалось связаться с сервером: ' + (e.message || e));
    } finally {
      isSubmitting = false;
      if (okBtn) { okBtn.disabled = false; okBtn.textContent = 'Войти'; }
    }
  }

  function bind() {
    const btn = $('admin-access-btn');
    if (btn) {
      btn.addEventListener('click', (e) => { e.preventDefault(); open(); });
    } else {
      console.warn('[admin-access] admin-access-btn not found');
    }

    const cancel = $('pin-modal-cancel');
    if (cancel) cancel.addEventListener('click', (e) => { e.preventDefault(); close(); });

    const ok = $('pin-modal-ok');
    if (ok) {
      ok.addEventListener('click', (e) => { e.preventDefault(); submit(); });
    }

    const input = $('pin-modal-input');
    if (input) {
      input.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') { e.preventDefault(); submit(); }
        else if (e.key === 'Escape') { e.preventDefault(); close(); }
      });
    }

    // PIN-пэд: используем mousedown (не click, не pointerdown).
    // В WebView2 на тачскринах click может срабатывать дважды (touch + mouse emulation).
    // mousedown срабатывает ровно один раз.
    // Дополнительно: per-button флаг _pressed, сбрасывается через 300мс.
    const pad = $('pin-modal-pad');
    if (pad) {
      const buttons = pad.querySelectorAll('button[data-key]');
      buttons.forEach(b => {
        const handler = (e) => {
          e.preventDefault();
          e.stopPropagation();
          e.stopImmediatePropagation();

          // Per-button dedup: если кнопка уже нажата — игнорируем.
          if (b._pressed) return;
          b._pressed = true;
          setTimeout(() => { b._pressed = false; }, 300);

          const k = b.dataset.key;
          if (k === 'clear')  clearAll();
          else if (k === 'back') backspace();
          else appendDigit(k);
        };
        // mousedown — срабатывает один раз на любое устройство ввода.
        b.addEventListener('mousedown', handler);
      });
    }

    // Клик по фону модалки — закрываем
    const modal = $('pin-modal');
    if (modal) {
      modal.addEventListener('click', (e) => {
        if (e.target === modal) close();
      });
    }

    // Глобальный ESC закрывает PIN-модалку
    document.addEventListener('keydown', (e) => {
      if (isOpen && e.key === 'Escape') {
        e.preventDefault();
        close();
      }
      // ESC закрывает модалку управления окном.
      const wcModal = $('window-control-modal');
      if (wcModal && !wcModal.hidden && e.key === 'Escape') {
        closeWindowControlModal();
      }
    });

    // === Модалка управления окном ===
    const wcModal = $('window-control-modal');
    if (wcModal) {
      // Клик по фону — закрыть.
      wcModal.addEventListener('click', (e) => {
        if (e.target === wcModal) closeWindowControlModal();
      });

      // Кнопка "Открыть админку".
      const adminOpenBtn = $('admin-open-btn');
      if (adminOpenBtn) {
        adminOpenBtn.addEventListener('click', async () => {
          if (lastToken) {
            await openAdminWindow(lastToken);
          }
          closeWindowControlModal();
        });
      }

      // Кнопка "Свернуть".
      const minBtn = $('win-minimize');
      if (minBtn) {
        minBtn.addEventListener('click', async () => {
          try {
            if (window.kiosk && typeof window.kiosk.call === 'function') {
              await window.kiosk.call('window.minimize', {}, 3000);
            }
          } catch (e) { console.warn('[window-controls] minimize failed:', e); }
          closeWindowControlModal();
        });
      }

      // Кнопка "Закрыть".
      const closeBtn = $('win-close');
      if (closeBtn) {
        closeBtn.addEventListener('click', async () => {
          if (!confirm('Закрыть приложение?')) return;
          try {
            if (window.kiosk && typeof window.kiosk.call === 'function') {
              await window.kiosk.call('window.close', {}, 3000);
            }
          } catch (e) { console.warn('[window-controls] close failed:', e); }
          closeWindowControlModal();
        });
      }

      // Кнопка "Отмена".
      const cancelBtn = $('window-control-cancel');
      if (cancelBtn) {
        cancelBtn.addEventListener('click', () => closeWindowControlModal());
      }
    }
  }

  function init() {
    try {
      bind();
      console.log('[admin-access] bound. admin-access-btn present:', !!$('admin-access-btn'));
    } catch (e) {
      console.error('[admin-access] bind failed', e);
    }
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }

  return {
    init,
    open,
    close,
    submit
  };
})();
