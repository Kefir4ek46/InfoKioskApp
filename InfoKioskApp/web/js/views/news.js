/* views/news.js — лента опубликованных новостей + карусель фото */
window.KioskViews.news = (() => {
  let posts = [];

  async function load() {
    let list = (await window.kiosk.call('news.list')) || [];
    // Сортируем по дате создания (сначала свежие).
    list = list.slice().sort((a, b) => {
      const da = new Date(a.CreatedAt || a.createdAt || 0).getTime();
      const db = new Date(b.CreatedAt || b.createdAt || 0).getTime();
      return db - da;  // убывание (новые первыми)
    });
    posts = list;
  }

  // ====================================================================
  // КАРУСЕЛЬ ФОТО
  // ====================================================================
  function buildCarousel(photos) {
    if (!photos || !photos.length) return '';
    const id = 'carousel-' + Date.now() + '-' + Math.random().toString(36).slice(2, 7);
    const dots = photos.map((_, i) =>
      `<button class="carousel-dot ${i === 0 ? 'active' : ''}" data-idx="${i}" aria-label="Фото ${i+1}"></button>`
    ).join('');

    return `
      <div class="photo-carousel" id="${id}">
        <div class="carousel-counter"><span class="cc-cur">1</span> / ${photos.length}</div>
        <div class="carousel-stage">
          <button class="carousel-nav prev" data-dir="-1" aria-label="Предыдущее">‹</button>
          <img src="${photos[0]}" alt="Фото 1" data-idx="0">
          <button class="carousel-nav next" data-dir="1" aria-label="Следующее">›</button>
        </div>
        <div class="carousel-dots">${dots}</div>
      </div>
    `;
  }

  function wireCarousel(container) {
    if (!container) return;
    const stage = container.querySelector('.carousel-stage');
    const img = container.querySelector('.carousel-stage img');
    const counter = container.querySelector('.cc-cur');
    const dots = container.querySelectorAll('.carousel-dot');
    const navBtns = container.querySelectorAll('.carousel-nav');
    if (!stage || !img) return;

    // Собираем список URL из data-attr или из src по точкам.
    const photos = Array.from(dots).map((_, i) => {
      // URL храним в data-src атрибуте точки? Нет — проще: в src img при
      // смене. Сохраним массив URL при инициализации.
      return null;
    });

    // Лучше: соберём URL из data-attr кнопок dots (data-photo).
    // Но мы их не сохраняли. Перестроим: сохраним URL в data-src каждой точки.
    // Для этого перепарсим — но проще переделать buildCarousel, чтобы хранить URL.
    // Сейчас сделаем простой вариант: URL берём из текущего img src при первой загрузке,
    // а остальные подставим через data-attr точек при рендере.

    let current = 0;
    const total = dots.length;

    function show(idx) {
      if (idx < 0) idx = total - 1;
      if (idx >= total) idx = 0;
      current = idx;
      // URL хранится в data-src точки.
      const url = dots[idx].dataset.src;
      if (url) img.src = url;
      img.alt = `Фото ${idx + 1}`;
      if (counter) counter.textContent = String(idx + 1);
      dots.forEach((d, i) => d.classList.toggle('active', i === idx));
    }

    navBtns.forEach(b => {
      b.addEventListener('click', (e) => {
        e.stopPropagation();
        const dir = parseInt(b.dataset.dir, 10);
        show(current + dir);
      });
    });
    dots.forEach((d, i) => {
      d.addEventListener('click', (e) => {
        e.stopPropagation();
        show(i);
      });
    });

    // Свайпы (touch)
    let touchStartX = 0;
    stage.addEventListener('touchstart', (e) => {
      touchStartX = e.touches[0].clientX;
    }, { passive: true });
    stage.addEventListener('touchend', (e) => {
      const dx = e.changedTouches[0].clientX - touchStartX;
      if (Math.abs(dx) > 50) {
        show(current + (dx < 0 ? 1 : -1));
      }
    }, { passive: true });

    // Клавиатура
    container._keyHandler = (e) => {
      if (!container.isConnected) return;
      if (e.key === 'ArrowLeft') show(current - 1);
      else if (e.key === 'ArrowRight') show(current + 1);
    };
    document.addEventListener('keydown', container._keyHandler);
  }

  function openPost(p) {
    const photos = (p.PhotoFiles || []).map(f =>
      `/download?target=newsmedia&name=${encodeURIComponent(f)}`
    );
    const date = new Date(p.CreatedAt).toLocaleString('ru-RU', { day:'numeric', month:'long', year:'numeric', hour:'2-digit', minute:'2-digit' });

    // Линия "опубликовал: X" — если новость опубликовал не автор.
    // PublishedByName может быть "Администратор" (когда публиковал админ),
    // либо имя другого редактора.
    let publishedByLine = '';
    if (p.PublishedByName && p.PublishedByName !== (p.AuthorName || '')) {
      publishedByLine = ` • 📤 Опубликовал: ${escapeHtml(p.PublishedByName)}`;
    }

    // QR-код к ссылке (если есть LinkUrl).
    // Используем публичный API qrserver.com (без ключа, без лимитов для киоска).
    // Размер 200×200 — достаточно для сканирования с экрана киоска.
    let qrBlock = '';
    if (p.LinkUrl) {
      try {
        new URL(p.LinkUrl);
        const qrUrl = `https://api.qrserver.com/v1/create-qr-code/?size=200x200&data=${encodeURIComponent(p.LinkUrl)}`;
        qrBlock = `<div style="margin-top:14px;padding:14px 16px;background:var(--panel-2);border-radius:var(--radius-sm);border:1px solid var(--border);display:flex;align-items:center;gap:16px;">
          <img src="${qrUrl}" alt="QR код" style="width:140px;height:140px;background:#fff;padding:6px;border-radius:var(--radius-sm);">
          <div style="flex:1;">
            <div style="font-size:15px;font-weight:600;color:var(--text);margin-bottom:4px;">🔗 Ссылка к новости</div>
            <div style="font-size:13px;color:var(--text-muted);margin-bottom:6px;">Наведите камеру телефона на QR-код, чтобы открыть:</div>
            <div style="font-size:12px;color:var(--accent);word-break:break-all;">${escapeHtml(p.LinkUrl)}</div>
          </div>
        </div>`;
      } catch (e) {
        // Невалидный URL — показываем просто текст.
        qrBlock = `<div style="margin-top:14px;padding:12px 16px;background:var(--panel-2);border-radius:var(--radius-sm);border:1px solid var(--border);">
          <div style="font-size:14px;color:var(--text-muted);margin-bottom:4px;">🔗 Ссылка:</div>
          <div style="font-size:13px;color:var(--accent);word-break:break-all;">${escapeHtml(p.LinkUrl)}</div>
        </div>`;
      }
    }

    // Карусель: строим с data-src на точках, чтобы wireCarousel мог переключать.
    let carouselHtml = '';
    if (photos.length === 1) {
      // Одно фото — без карусели, просто большое фото.
      carouselHtml = `<div class="photo-carousel">
        <div class="carousel-stage">
          <img src="${photos[0]}" alt="Фото" style="max-height:60vh;">
        </div>
      </div>`;
    } else if (photos.length > 1) {
      const dots = photos.map((u, i) =>
        `<button class="carousel-dot ${i === 0 ? 'active' : ''}" data-idx="${i}" data-src="${u}" aria-label="Фото ${i+1}"></button>`
      ).join('');
      carouselHtml = `
        <div class="photo-carousel" id="post-carousel">
          <div class="carousel-counter"><span class="cc-cur">1</span> / ${photos.length}</div>
          <div class="carousel-stage">
            <button class="carousel-nav prev" data-dir="-1" aria-label="Предыдущее">‹</button>
            <img src="${photos[0]}" alt="Фото 1" data-idx="0">
            <button class="carousel-nav next" data-dir="1" aria-label="Следующее">›</button>
          </div>
          <div class="carousel-dots">${dots}</div>
        </div>
      `;
    }

    const html = `
      <div class="post-viewer" id="post-viewer-content">
        <h2>${escapeHtml(p.Title)}</h2>
        <div class="post-meta">${escapeHtml(p.AuthorName || '')} • ${date}${publishedByLine}</div>
        <div class="post-content">${escapeHtml(p.Content)}</div>
        ${carouselHtml}
        ${p.VideoFile ? `<div style="margin-top:16px;">
          <video src="/download?target=newsmedia&name=${encodeURIComponent(p.VideoFile)}" controls style="width:100%;max-height:50vh;border-radius:var(--radius-md);background:#000;"></video>
        </div>` : ''}
        ${qrBlock}
        <div style="margin-top:20px;text-align:right;">
          <button class="header-btn" onclick="window.Modal.close()" style="width:auto;padding:8px 20px;">Закрыть</button>
        </div>
      </div>
    `;
    // Открываем модалку. Modal.open сбросит scroll в топ.
    window.Modal.open(html);

    // Сбрасываем scroll контента наверх (на случай если ранее открывали и скроллили).
    requestAnimationFrame(() => {
      const viewer = document.getElementById('post-viewer-content');
      if (viewer) viewer.scrollTop = 0;
      const modal = document.getElementById('modal');
      if (modal) modal.scrollTop = 0;
      // Wire карусель после рендера.
      const carousel = document.getElementById('post-carousel');
      if (carousel) wireCarousel(carousel);
    });
  }

  function render($el) {
    if (!posts.length) {
      $el.innerHTML = `<div class="view"><div class="view-header"><div class="view-title">Новости</div></div>
        <div class="canteen-empty">Пока нет опубликованных новостей</div></div>`;
      return;
    }
    $el.innerHTML = `
      <div class="view">
        <div class="view-header">
          <div>
            <div class="view-title">Новости</div>
            <div class="view-subtitle">${posts.length} ${pluralRu(posts.length, ['новость','новости','новостей'])}</div>
          </div>
        </div>
        <div class="news-list">
          ${posts.map((p, i) => {
            const date = new Date(p.CreatedAt).toLocaleDateString('ru-RU', { day:'numeric', month:'long', year:'numeric' });
            const photos = (p.PhotoFiles || []).slice(0, 3).map(f =>
              `<img src="/download?target=newsmedia&name=${encodeURIComponent(f)}">`).join('');
            const publishedByHtml = p.PublishedByName && p.PublishedByName !== (p.AuthorName || '')
              ? ` • 📤 ${escapeHtml(p.PublishedByName)}`
              : '';
            return `<article class="news-card stagger-in" style="--i:${i}" data-id="${escapeHtml(p.Id)}">
              <div class="news-title">${escapeHtml(p.Title)}</div>
              <div class="news-meta">${escapeHtml(p.AuthorName || '')} • ${date}${publishedByHtml}</div>
              <div class="news-preview">${escapeHtml(p.Content)}</div>
              ${photos ? `<div class="news-photos">${photos}</div>` : ''}
            </article>`;
          }).join('')}
        </div>
      </div>
    `;
    $el.querySelectorAll('.news-card').forEach(c => {
      c.addEventListener('click', () => {
        const p = posts.find(x => x.Id === c.dataset.id);
        if (p) openPost(p);
      });
    });
  }

  return {
    async mount($el) {
      $el.innerHTML = '<div class="view-loader"><div class="spinner"></div><div class="loader-text">Загрузка новостей…</div></div>';
      await load();
      render($el);
    },
    onDataChanged(section) {
      if (section === 'news') load().then(() => render(document.getElementById('kiosk-content')));
    },
    openPost
  };
})();

function pluralRu(n, forms) {
  const n10 = n % 10, n100 = n % 100;
  if (n10 === 1 && n100 !== 11) return forms[0];
  if (n10 >= 2 && n10 <= 4 && (n100 < 10 || n100 >= 20)) return forms[1];
  return forms[2];
}
window.pluralRu = pluralRu;
