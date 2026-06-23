/* views/media.js — медиа-галерея. Категории + сетка постов + карусель фото.
 *
 * Посты сортируются по дате (сначала свежие). Фото в посте — карусель
 * с prev/next/точками/свайпами.
 */
window.KioskViews.media = (() => {
  let state = { cats: [], activeCat: null, posts: [], page: 1, pageSize: 12, total: 0 };

  async function loadCats() {
    state.cats = await window.kiosk.call('media.categories');
    if (!state.activeCat && state.cats.length) state.activeCat = state.cats[0].name;
  }
  async function loadPosts() {
    const res = await window.kiosk.call('media.posts', {
      category: state.activeCat, page: state.page, pageSize: state.pageSize
    });
    let posts = (res && res.posts) || [];
    posts = posts.slice().sort((a, b) => {
      const da = (a.Date || a.date || '').toString();
      const db = (b.Date || b.date || '').toString();
      if (da === db) return 0;
      return da < db ? 1 : -1;
    });
    state.posts = posts;
    state.total = res.total || 0;
  }

  // ====================================================================
  // КАРУСЕЛЬ ФОТО
  // ====================================================================
  function buildMediaCarousel(photos) {
    if (!photos || !photos.length) return '';
    if (photos.length === 1) {
      // Одно фото — без карусели, просто большое.
      return `<div class="photo-carousel">
        <div class="carousel-stage">
          <img src="${photos[0]}" alt="Фото" style="max-height:60vh;">
        </div>
      </div>`;
    }
    const dots = photos.map((u, i) =>
      `<button class="carousel-dot ${i === 0 ? 'active' : ''}" data-idx="${i}" data-src="${u}" aria-label="Фото ${i+1}"></button>`
    ).join('');
    return `
      <div class="photo-carousel" id="media-post-carousel">
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
    if (!stage || !img || !dots.length) return;

    let current = 0;
    const total = dots.length;

    function show(idx) {
      if (idx < 0) idx = total - 1;
      if (idx >= total) idx = 0;
      current = idx;
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

    // Свайпы
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

  async function openPost(post) {
    const full = await window.kiosk.call('media.post', { category: state.activeCat, id: post.id });
    const images = (full.images || []).map(f =>
      `/download?target=media&category=${encodeURIComponent(state.activeCat)}&post=${encodeURIComponent(post.id)}&name=${encodeURIComponent(f)}`
    );
    const carouselHtml = buildMediaCarousel(images);
    const html = `
      <div class="post-viewer" id="media-viewer-content">
        <h2>${escapeHtml(full.Title || post.Title || 'Без названия')}</h2>
        <div class="post-meta">${escapeHtml(full.Date || post.Date || '')}</div>
        ${full.Description ? `<div class="post-content">${escapeHtml(full.Description)}</div>` : ''}
        ${carouselHtml}
        <div style="margin-top:20px;text-align:right;">
          <button class="header-btn" onclick="window.Modal.close()" style="width:auto;padding:8px 20px;">Закрыть</button>
        </div>
      </div>
    `;
    window.Modal.open(html);
    requestAnimationFrame(() => {
      const carousel = document.getElementById('media-post-carousel');
      if (carousel) wireCarousel(carousel);
    });
  }

  async function render($el) {
    $el.innerHTML = `
      <div class="view">
        <div class="view-header">
          <div>
            <div class="view-title">Медиа</div>
            <div class="view-subtitle">Фото и видео</div>
          </div>
        </div>
        <div class="media-cats">
          ${state.cats.map(c => `<button class="media-cat ${c.name===state.activeCat?'active':''}" data-cat="${escapeHtml(c.name)}">${escapeHtml(c.name)} (${c.postsCount})</button>`).join('')}
        </div>
        <div class="media-grid" id="media-grid">
          ${state.posts.map((p, i) => {
            const cover = p.Cover || (p.Images && p.Images[0] && p.Images[0].File) || '';
            const url = cover ? `/download?target=media&category=${encodeURIComponent(state.activeCat)}&post=${encodeURIComponent(p.id)}&name=${encodeURIComponent(cover)}` : '';
            return `<div class="media-card stagger-in hover-lift" style="--i:${i}" data-id="${escapeHtml(p.id)}">
              <div class="media-cover" ${url ? `style="background-image:url(${url})"` : ''}></div>
              <div class="media-body">
                <div class="media-title">${escapeHtml(p.Title || 'Без названия')}</div>
                <div class="media-date">${escapeHtml(p.Date || '')}</div>
              </div>
            </div>`;
          }).join('')}
        </div>
      </div>
    `;
    $el.querySelectorAll('.media-cat').forEach(b => {
      b.addEventListener('click', async () => {
        state.activeCat = b.dataset.cat;
        state.page = 1;
        await loadPosts();
        render($el);
      });
    });
    $el.querySelectorAll('.media-card').forEach(c => {
      c.addEventListener('click', () => {
        const p = state.posts.find(x => x.id === c.dataset.id);
        if (p) openPost(p);
      });
    });
  }

  return {
    async mount($el) {
      $el.innerHTML = '<div class="view-loader"><div class="spinner"></div><div class="loader-text">Загрузка медиа…</div></div>';
      await loadCats();
      await loadPosts();
      render($el);
    },
    onDataChanged(section) {
      if (section === 'media') {
        loadCats().then(() => loadPosts()).then(() => render(document.getElementById('kiosk-content')));
      }
    },
    openPost: async (postOrId) => {
      // Если передан объект с category — устанавливаем активную категорию.
      if (postOrId && postOrId.category) {
        state.activeCat = postOrId.category;
        await loadPosts();
      }
      // Открываем пост.
      const id = postOrId && postOrId.id ? postOrId.id : postOrId;
      const p = state.posts.find(x => x.id === id);
      if (p) await openPost(p);
    }
  };
})();
