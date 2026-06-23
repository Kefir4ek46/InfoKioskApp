/* fullscreen.js — fullscreen через C# (JS в WebView не может сам) */
window.Fullscreen = (() => {
  let isOn = false;
  return {
    init() {
      const btn = document.getElementById('btn-fullscreen');
      if (btn) btn.addEventListener('click', () => Fullscreen.toggle());
    },
    async toggle() {
      isOn = !isOn;
      try { await window.kiosk.call('window.fullscreen', { enable: isOn }); }
      catch (e) { console.warn('[fullscreen]', e); }
    },
    set(v) { isOn = !!v; }
  };
})();
