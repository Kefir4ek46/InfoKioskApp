/* ticker.js — бегущая строка через requestAnimationFrame */
window.Ticker = (() => {
  let items = [];
  let speed = 1.5;       // px/frame при 60fps
  let x = 0;
  let raf = null;
  let lastTs = 0;
  let enabled = false;

  const track = () => document.getElementById('ticker-track');

  function render() {
    if (!items.length) { track().textContent = ''; return; }
    track().textContent = items.join('     •     ');
  }

  function loop(ts) {
    if (!enabled) return;
    const dt = lastTs ? (ts - lastTs) : 16;
    lastTs = ts;
    x -= speed * (dt / 16);
    const trackEl = track();
    if (trackEl) {
      const w = trackEl.scrollWidth;
      if (-x >= w) x = 0;
      trackEl.style.transform = `translate3d(${x}px,0,0)`;
    }
    raf = requestAnimationFrame(loop);
  }

  return {
    setItems(arr)  { items = Array.isArray(arr) ? arr : (arr ? [arr] : []); render(); },
    setSpeed(s)    { speed = Math.max(0.2, Math.min(8, +s || 1.5)); },
    start() {
      if (enabled) return;
      enabled = true;
      lastTs = 0;
      raf = requestAnimationFrame(loop);
    },
    stop() {
      enabled = false;
      if (raf) cancelAnimationFrame(raf);
    }
  };
})();
