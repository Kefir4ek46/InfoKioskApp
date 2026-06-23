/* idle.js — экран неактивности. Плавный кросс-фейд между слайдами.
   ВАЖНО: заставка должна закрываться по любому клику/тачу/нажатию клавиши. */
window.Idle = (() => {
  let enabled = true;
  let timeoutMs = 90000;
  let slideMs = 8000;
  let logoOnly = false;
  let images = [];
  let idx = 0;
  let idleTimer = null;
  let slideTimer = null;
  let active = false;
  let lastActivity = Date.now();
  let eventsBound = false;
  let currentSlideEl = null;
  let nextSlideEl = null;

  const overlay = () => document.getElementById('idle-overlay');
  const slideEl = () => document.getElementById('idle-slide');
  const slideNextEl = () => document.getElementById('idle-slide-next');

  function bumpActivity() {
    lastActivity = Date.now();
    if (active) hide();
    if (!active) scheduleWake();
  }

  function scheduleWake() {
    if (idleTimer) clearTimeout(idleTimer);
    if (!enabled) return;
    idleTimer = setTimeout(checkIdle, 5000);
  }

  function checkIdle() {
    if (!enabled) return;
    if (active) return;
    if (Date.now() - lastActivity >= timeoutMs) {
      show();
    } else {
      scheduleWake();
    }
  }

  async function show() {
    if (active) return;
    active = true;

    // Всегда перезагружаем список изображений.
    try {
      const res = await window.kiosk.call('idle.images');
      images = (res && res.images) || [];
      console.log('[idle] loaded images:', images.length, images);
    } catch (e) { console.warn('[idle] images load', e); }

    const ov = overlay();
    if (!ov) { active = false; return; }
    ov.hidden = false;
    ov.style.pointerEvents = 'auto';

    currentSlideEl = slideEl();
    nextSlideEl = slideNextEl();

    if (!images.length) {
      // Нет фото — тёмный фон с часами.
      if (currentSlideEl) {
        currentSlideEl.style.backgroundImage = '';
        currentSlideEl.classList.add('active');
      }
    } else {
      // Показываем первый слайд.
      showSlide(0);
      // Запускаем интервальное переключение.
      slideTimer = setInterval(nextSlide, slideMs);
    }
  }

  function hide() {
    active = false;
    const ov = overlay();
    if (ov) ov.hidden = true;
    if (slideTimer) { clearInterval(slideTimer); slideTimer = null; }
    // Сбрасываем слайды.
    if (currentSlideEl) currentSlideEl.classList.remove('active');
    if (nextSlideEl) nextSlideEl.classList.remove('active');
    lastActivity = Date.now();
    scheduleWake();
  }

  function showSlide(i) {
    if (!images.length) return;
    idx = (i + images.length) % images.length;
    const url = images[idx];
    console.log('[idle] showSlide:', idx, url);

    if (!currentSlideEl || !nextSlideEl) return;

    // Загружаем новое фото в nextSlideEl (который сейчас скрыт).
    nextSlideEl.style.backgroundImage = `url("${url}")`;

    // Ждём загрузки изображения, затем делаем кросс-фейд.
    const img = new Image();
    img.onload = () => {
      // Переключаем: next становится активным, current — нет.
      nextSlideEl.classList.add('active');
      currentSlideEl.classList.remove('active');

      // Меняем местами: current = next, next = old current.
      const tmp = currentSlideEl;
      currentSlideEl = nextSlideEl;
      nextSlideEl = tmp;

      // Обновляем z-index: current поверх.
      currentSlideEl.style.zIndex = '1';
      nextSlideEl.style.zIndex = '0';
    };
    img.onerror = () => {
      console.warn('[idle] image load failed:', url);
    };
    img.src = url;
  }

  function nextSlide() { showSlide(idx + 1); }

  function bindEvents() {
    if (eventsBound) return;
    eventsBound = true;

    ['mousemove','mousedown','click','touchstart','keydown','wheel','scroll'].forEach(ev => {
      document.addEventListener(ev, bumpActivity, { passive: true, capture: true });
    });

    const ov = overlay();
    if (ov) {
      ov.addEventListener('click', (e) => {
        e.stopPropagation();
        hide();
      });
      ov.addEventListener('touchstart', (e) => {
        e.stopPropagation();
        hide();
      }, { passive: true });
    }

    document.addEventListener('keydown', (e) => {
      if (active && (e.key === 'Escape' || e.key === 'Enter' || e.key === ' ')) {
        e.preventDefault();
        hide();
      }
    }, { capture: true });
  }

  function ensureBound() {
    if (eventsBound) return;
    if (document.getElementById('idle-overlay')) {
      bindEvents();
    } else {
      setTimeout(ensureBound, 200);
    }
  }

  return {
    start() {
      ensureBound();
      scheduleWake();
    },
    setEnabled(v)   { enabled = !!v; if (!enabled) hide(); },
    setTimeout(ms)  { timeoutMs = Math.max(5000, ms); },
    setSlideDuration(ms) { slideMs = Math.max(2000, ms); },
    setLogoOnly(v)  { logoOnly = !!v; },
    isActive: () => active,
    hide,
    stop() {
      if (idleTimer) clearTimeout(idleTimer);
      if (slideTimer) clearInterval(slideTimer);
    }
  };
})();
