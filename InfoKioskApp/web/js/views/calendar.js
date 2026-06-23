/* views/calendar.js — календарь с многоцветными точками, фильтром, "также в этот день" */
window.KioskViews.calendar = (() => {
  let events = [];
  let viewYear, viewMonth;
  let selectedDay = new Date();
  let activeFilters = new Set();  // пустой = показывать все

  // Категории событий с цветами и эмодзи.
  // Стандартных категорий НЕТ — все категории создаёт администратор.
  // Если в config пусто — используем серую точку "Без категории" как fallback.
  let eventCategories = {};

  // Fallback для событий без категории или когда config пуст.
  const DEFAULT_FALLBACK = { color: '#3A9FFF', icon: '📌' };

  async function load() {
    events = (await window.kiosk.call('calendar.list')) || [];
    // Загружаем кастомные категории из конфига.
    try {
      const cfg = window.KioskApp && window.KioskApp.getConfig
        ? window.KioskApp.getConfig()
        : null;
      if (cfg) {
        const cats = cfg.CalendarCategories || cfg.calendarCategories;
        if (cats && typeof cats === 'object') {
          // Нормализуем ключи: Color→color, Icon→icon.
          const normalized = {};
          for (const [name, cat] of Object.entries(cats)) {
            normalized[name] = {
              color: cat.color || cat.Color || '#3A9FFF',
              icon: cat.icon || cat.Icon || '📌',
            };
          }
          eventCategories = { ...eventCategories, ...normalized };
        }
      }
    } catch (e) {
      console.warn('[calendar] load categories failed:', e);
    }
  }

  function ymdLocal(d) {
    const y = d.getFullYear();
    const m = String(d.getMonth() + 1).padStart(2, '0');
    const day = String(d.getDate()).padStart(2, '0');
    return `${y}-${m}-${day}`;
  }

  function getCategoryColor(type) {
    if (!type) return DEFAULT_FALLBACK.color;
    const cat = eventCategories[type];
    if (!cat) return DEFAULT_FALLBACK.color;
    return cat.color || cat.Color || DEFAULT_FALLBACK.color;
  }

  function getCategoryIcon(type) {
    if (!type) return DEFAULT_FALLBACK.icon;
    const cat = eventCategories[type];
    if (!cat) return DEFAULT_FALLBACK.icon;
    return cat.icon || cat.Icon || DEFAULT_FALLBACK.icon;
  }

  function eventOnDay(day) {
    const d = ymdLocal(day);
    return events.filter(e => {
      const sRaw = e.StartDate || e.startDate || '';
      const enRaw = e.EndDate || e.endDate || '';
      const s = sRaw ? String(sRaw).slice(0,10) : '';
      const en = enRaw ? String(enRaw).slice(0,10) : '';

      const yearly = e.Yearly || e.yearly || false;
      if (yearly) {
        const dayMonth = d.slice(5);
        const sMonth = s ? s.slice(5) : '';
        const enMonth = en ? en.slice(5) : '';
        if (!sMonth || !enMonth) return false;
        if (sMonth <= enMonth) {
          return dayMonth >= sMonth && dayMonth <= enMonth;
        } else {
          return dayMonth >= sMonth || dayMonth <= enMonth;
        }
      }

      return d >= s && d <= en;
    }).filter(e => {
      // Фильтр по категориям.
      if (activeFilters.size === 0) return true;
      const type = e.Type || e.type || 'Другое';
      return activeFilters.has(type);
    });
  }

  function typeClass(t) {
    t = (t || '').toLowerCase();
    if (t.includes('праздник')) return 'holiday';
    return '';
  }

  function parseLocalDate(str) {
    if (!str) return null;
    const [y, m, d] = String(str).slice(0,10).split('-').map(Number);
    return new Date(y, m - 1, d, 12, 0, 0);
  }

  function fmtDate(d) {
    if (!d) return '';
    return d.toLocaleDateString('ru-RU');
  }

  function getUpcomingEvents(pastCount = 2, futureCount = 5) {
    const today = new Date();
    today.setHours(0, 0, 0, 0);

    const withStart = events.map(e => {
      const sRaw = e.StartDate || e.startDate || '';
      const enRaw = e.EndDate || e.endDate || '';
      const yearly = e.Yearly || e.yearly || false;

      let startDate = parseLocalDate(sRaw);
      let endDate = parseLocalDate(enRaw);

      if (yearly && startDate && endDate) {
        const thisYear = today.getFullYear();
        let bestStart = new Date(thisYear, startDate.getMonth(), startDate.getDate(), 12, 0, 0);
        let bestEnd = new Date(thisYear, endDate.getMonth(), endDate.getDate(), 12, 0, 0);
        if (bestEnd < bestStart) bestEnd.setFullYear(bestEnd.getFullYear() + 1);
        if (bestEnd < today) {
          bestStart.setFullYear(bestStart.getFullYear() + 1);
          bestEnd.setFullYear(bestEnd.getFullYear() + 1);
        }
        return { event: e, start: ymdLocal(bestStart), end: ymdLocal(bestEnd), startDate: bestStart, endDate: bestEnd, yearly: true };
      }

      return { event: e, start: sRaw ? String(sRaw).slice(0,10) : '', end: enRaw ? String(enRaw).slice(0,10) : '', startDate, endDate, yearly: false };
    });

    // Фильтруем по активным фильтрам.
    const filtered = withStart.filter(item => {
      if (activeFilters.size === 0) return true;
      const type = item.event.Type || item.event.type || 'Другое';
      return activeFilters.has(type);
    });

    const past = [];
    const future = [];
    for (const item of filtered) {
      if (!item.endDate) continue;
      const endDay = new Date(item.endDate);
      endDay.setHours(0, 0, 0, 0);
      if (endDay < today) past.push(item);
      else future.push(item);
    }

    past.sort((a, b) => (a.endDate < b.endDate ? 1 : -1));
    const recentPast = past.slice(0, pastCount).reverse();

    future.sort((a, b) => (a.startDate < b.startDate ? -1 : 1));
    const upcoming = future.slice(0, futureCount);

    return { recentPast, upcoming };
  }

  function renderEventCard(item, isPast) {
    const e = item.event;
    const tCls = typeClass(e.Type || e.type);
    const s = fmtDate(item.startDate);
    const en = fmtDate(item.endDate);
    const title = e.Title || e.title || '';
    const type = e.Type || e.type || '';
    const desc = e.Description || e.description || '';
    const pastCls = isPast ? ' past' : '';
    const color = getCategoryColor(type);
    const icon = getCategoryIcon(type);
    const yearly = e.Yearly || e.yearly;
    const yearlyBadge = yearly ? ' <span class="yearly-badge">♻ EVT</span>' : '';
    const dateAttr = item.startDate ? `data-event-date="${ymdLocal(item.startDate)}"` : '';
    return `<div class="cal-event ${tCls}${pastCls}" ${dateAttr} style="border-left-color:${color};cursor:pointer;">
      <div class="ev-title">${icon} ${escapeHtml(title)}${yearlyBadge}</div>
      <div class="ev-dates">${escapeHtml(type)} • ${s}${s !== en ? ' – ' + en : ''}</div>
      ${desc ? `<div class="ev-desc">${escapeHtml(desc)}</div>` : ''}
    </div>`;
  }

  // Загрузка постов/новостей за выбранную дату.
  async function loadOnThisDay(dateStr) {
    try {
      const result = [];

      // Новости
      try {
        const news = await window.kiosk.call('news.list');
        if (news && news.length) {
          news.forEach(n => {
            const created = n.CreatedAt || n.createdAt || '';
            if (created && created.slice(0, 10) === dateStr) {
              result.push({ type: 'news', icon: '📰', title: n.Title || n.title || 'Без названия', id: n.Id || n.id });
            }
          });
        }
      } catch (e) { /* news.list может не быть */ }

      // Медиа посты
      try {
        const cats = await window.kiosk.call('media.categories');
        if (cats && cats.length) {
          for (const cat of cats) {
            try {
              const res = await window.kiosk.call('media.posts', { category: cat.id || cat.name, page: 1, pageSize: 50 });
              if (res && res.posts) {
                res.posts.forEach(p => {
                  const date = p.Date || p.date || '';
                  if (date && date.slice(0, 10) === dateStr) {
                    result.push({ type: 'media', icon: '🖼️', title: p.Title || p.title || 'Без названия', category: cat.id || cat.name, id: p.id });
                  }
                });
              }
            } catch (e) {}
          }
        }
      } catch (e) {}

      return result.slice(0, 10);  // максимум 10
    } catch (e) {
      console.warn('[calendar] loadOnThisDay error:', e);
      return [];
    }
  }

  async function renderMonth($el) {
    const today = new Date();
    const first = new Date(viewYear, viewMonth, 1);
    const last  = new Date(viewYear, viewMonth + 1, 0);
    const startDow = (first.getDay() + 6) % 7;
    const totalDays = last.getDate();
    const cells = [];

    const prevLast = new Date(viewYear, viewMonth, 0).getDate();
    for (let i = startDow - 1; i >= 0; i--) {
      const d = new Date(viewYear, viewMonth - 1, prevLast - i);
      cells.push({ d, outside: true });
    }
    for (let i = 1; i <= totalDays; i++) {
      const d = new Date(viewYear, viewMonth, i);
      cells.push({ d, outside: false });
    }
    while (cells.length % 7 !== 0 || cells.length < 42) {
      const lastCell = cells[cells.length - 1].d;
      const d = new Date(lastCell);
      d.setDate(d.getDate() + 1);
      cells.push({ d, outside: d.getMonth() !== viewMonth });
      if (cells.length >= 42) break;
    }

    const dows = ['Пн','Вт','Ср','Чт','Пт','Сб','Вс'];
    const monthNames = ['Январь','Февраль','Март','Апрель','Май','Июнь','Июль','Август','Сентябрь','Октябрь','Ноябрь','Декабрь'];

    const dayCells = cells.map(c => {
      const evs = eventOnDay(c.d);
      const isToday = ymdLocal(c.d) === ymdLocal(today);
      const isSelected = ymdLocal(c.d) === ymdLocal(selectedDay);
      const dow = (c.d.getDay() + 6) % 7;
      const isWeekend = dow >= 5;
      const classes = ['cal-day'];
      if (c.outside) classes.push('outside');
      if (isToday) classes.push('today');
      if (isSelected) classes.push('selected');
      if (isSelected && evs.length) classes.push('has-events');
      if (isWeekend) classes.push('weekend');

      // Многоцветные точки — по одной на каждый тип события.
      const seenTypes = new Set();
      const dotsHtml = evs.length
        ? (evs.length <= 4
            ? evs.map(e => {
                const type = e.Type || e.type || 'Другое';
                const color = getCategoryColor(type);
                if (seenTypes.has(type)) return '';
                seenTypes.add(type);
                return `<span class="event-dot" style="background:${color}"></span>`;
              }).join('')
            : `<span class="event-dot" style="background:${getCategoryColor(evs[0].Type || evs[0].type)}"></span><span class="event-count">${evs.length}</span>`)
        : '';

      return `<div class="${classes.join(' ')}" data-date="${ymdLocal(c.d)}">
        ${c.d.getDate()}
        <span class="event-dots">${dotsHtml}</span>
      </div>`;
    }).join('');

    // События выбранного дня
    const selEvs = eventOnDay(selectedDay);
    const eventsHtml = selEvs.length
      ? selEvs.map(e => {
          const type = e.Type || e.type || '';
          const color = getCategoryColor(type);
          const icon = getCategoryIcon(type);
          const tCls = typeClass(type);
          const sRaw = e.StartDate || e.startDate || '';
          const enRaw = e.EndDate || e.endDate || '';
          const parseLocal = (str) => {
            if (!str) return '';
            const [y, m, d] = String(str).slice(0,10).split('-').map(Number);
            return new Date(y, m - 1, d, 12, 0, 0).toLocaleDateString('ru-RU');
          };
          const s = parseLocal(sRaw);
          const en = parseLocal(enRaw);
          const title = e.Title || e.title || '';
          const desc = e.Description || e.description || '';
          const yearly = e.Yearly || e.yearly;
          const yearlyBadge = yearly ? ' <span class="yearly-badge">♻ EVT</span>' : '';
          return `<div class="cal-event ${tCls}" style="border-left-color:${color}">
            <div class="ev-title">${icon} ${escapeHtml(title)}${yearlyBadge}</div>
            <div class="ev-dates">${escapeHtml(type)} • ${s}${s !== en ? ' – ' + en : ''}</div>
            ${desc ? `<div class="ev-desc">${escapeHtml(desc)}</div>` : ''}
          </div>`;
        }).join('')
      : '<div class="canteen-empty" style="padding:16px;">Нет событий</div>';

    // Ближайшие события
    const { recentPast, upcoming } = getUpcomingEvents(2, 5);
    const upcomingHtml = [
      ...recentPast.map(i => renderEventCard(i, true)),
      ...upcoming.map(i => renderEventCard(i, false)),
    ].join('') || '<div class="cal-empty">Нет ближайших событий</div>';

    // Фильтр по категориям
    const filterHtml = Object.entries(eventCategories).map(([type, cat]) => {
      const isActive = activeFilters.has(type);
      return `<button class="cal-filter-btn ${isActive ? 'active' : ''}" data-filter="${escapeHtml(type)}">
        <span class="cal-filter-dot" style="background:${cat.color}"></span>
        ${cat.icon} ${escapeHtml(type)}
      </button>`;
    }).join('');

    $el.innerHTML = `
      <div class="view calendar-view">
        <div class="cal-filters">${filterHtml}</div>
        <div class="calendar-wrap">
          <div class="calendar-month">
            <div class="cal-nav">
              <button data-nav="-1" title="Предыдущий месяц">‹</button>
              <div class="cal-nav-title">${monthNames[viewMonth]} ${viewYear}</div>
              <button data-nav="+1" title="Следующий месяц">›</button>
              <button data-today="1" title="Сегодня" class="cal-today-btn">●</button>
            </div>
            <div class="cal-grid">
              ${dows.map(d => `<div class="cal-dow">${d}</div>`).join('')}
              ${dayCells}
            </div>
            <div class="cal-stats">
              <div class="cal-stat"><span class="cal-stat-num">${events.length}</span><span class="cal-stat-label">всего</span></div>
              <div class="cal-stat"><span class="cal-stat-num">${upcoming.length}</span><span class="cal-stat-label">будущих</span></div>
              <div class="cal-stat"><span class="cal-stat-num">${events.filter(e => (e.Yearly || e.yearly)).length}</span><span class="cal-stat-label">ежегодных</span></div>
            </div>
          </div>
          <div class="cal-events">
            <h3>${selectedDay.toLocaleDateString('ru-RU', { day:'numeric', month:'long', year:'numeric' })}</h3>
            ${eventsHtml}
            <div class="cal-on-this-day" id="cal-on-this-day">
              <h4>Также в этот день</h4>
              <div id="cal-on-this-day-list"><div class="cal-empty">Загрузка…</div></div>
            </div>
            <div class="cal-upcoming">
              <h3>Ближайшие события</h3>
              ${upcomingHtml}
            </div>
          </div>
        </div>
      </div>
    `;

    // Загружаем "также в этот день" асинхронно.
    const dateStr = ymdLocal(selectedDay);
    loadOnThisDay(dateStr).then(items => {
      const listEl = document.getElementById('cal-on-this-day-list');
      if (!listEl) return;
      if (!items.length) {
        listEl.innerHTML = '<div class="cal-empty">Нет публикаций</div>';
        return;
      }
      listEl.innerHTML = items.map(item => `
        <div class="cal-on-this-day-item" data-type="${item.type}" data-id="${escapeHtml(item.id)}" ${item.category ? `data-category="${escapeHtml(item.category)}"` : ''}>
          <span class="cal-on-this-day-icon">${item.icon}</span>
          <span class="cal-on-this-day-title">${escapeHtml(item.title)}</span>
        </div>
      `).join('');

      // Кликабельные элементы — открываем конкретный пост.
      listEl.querySelectorAll('.cal-on-this-day-item').forEach(el => {
        el.addEventListener('click', async () => {
          const type = el.dataset.type;
          const id = el.dataset.id;
          const category = el.dataset.category;

          if (type === 'news') {
            // Открываем конкретную новость.
            try {
              const news = await window.kiosk.call('news.list');
              const post = (news || []).find(n => (n.Id || n.id) === id);
              if (post && window.KioskViews.news) {
                // Вызываем openPost напрямую через navigate + setTimeout.
                window.KioskApp.navigate('news');
                setTimeout(() => {
                  if (window.KioskViews.news && window.KioskViews.news.openPost) {
                    window.KioskViews.news.openPost(post);
                  }
                }, 300);
              }
            } catch (e) { console.warn('[calendar] open news:', e); }
          } else if (type === 'media') {
            // Открываем конкретный медиа-пост.
            try {
              window.KioskApp.navigate('media');
              setTimeout(async () => {
                if (window.KioskViews.media && window.KioskViews.media.openPost) {
                  // media.openPost принимает post-объект с id.
                  await window.KioskViews.media.openPost({ id, category });
                }
              }, 300);
            } catch (e) { console.warn('[calendar] open media:', e); }
          }
        });
      });
    });

    // Фильтры — множественный выбор (toggle).
    $el.querySelectorAll('.cal-filter-btn').forEach(btn => {
      btn.addEventListener('click', (e) => {
        e.stopPropagation();
        const filter = btn.dataset.filter;
        console.log('[calendar] filter toggle:', filter, 'before:', [...activeFilters]);
        if (activeFilters.has(filter)) {
          activeFilters.delete(filter);
        } else {
          activeFilters.add(filter);
        }
        console.log('[calendar] filter after:', [...activeFilters]);
        // Обновляем только визуальное состояние кнопок без полного rerender.
        $el.querySelectorAll('.cal-filter-btn').forEach(b => {
          const isActive = activeFilters.has(b.dataset.filter);
          b.classList.toggle('active', isActive);
        });
        // Перерисовываем календарь с новым фильтром.
        renderMonth($el);
      });
    });

    // Навигация
    $el.querySelectorAll('[data-nav]').forEach(b => {
      b.addEventListener('click', () => {
        const dir = parseInt(b.dataset.nav, 10);
        viewMonth += dir;
        if (viewMonth < 0) { viewMonth = 11; viewYear--; }
        if (viewMonth > 11) { viewMonth = 0; viewYear++; }
        renderMonth($el);
      });
    });
    $el.querySelectorAll('[data-today]').forEach(b => {
      b.addEventListener('click', () => {
        const now = new Date();
        viewYear = now.getFullYear();
        viewMonth = now.getMonth();
        selectedDay = new Date(now.getFullYear(), now.getMonth(), now.getDate(), 12, 0, 0);
        renderMonth($el);
      });
    });
    $el.querySelectorAll('.cal-day').forEach(c => {
      c.addEventListener('click', () => {
        const [y, m, d] = c.dataset.date.split('-').map(Number);
        selectedDay = new Date(y, m - 1, d, 12, 0, 0);
        renderMonth($el);
      });
    });
    $el.querySelectorAll('[data-event-date]').forEach(ev => {
      ev.addEventListener('click', () => {
        const dateStr = ev.dataset.eventDate;
        if (!dateStr) return;
        const [y, m, d] = dateStr.split('-').map(Number);
        selectedDay = new Date(y, m - 1, d, 12, 0, 0);
        viewYear = y;
        viewMonth = m - 1;
        renderMonth($el);
      });
    });
  }

  return {
    async mount($el) {
      const now = new Date();
      viewYear = now.getFullYear();
      viewMonth = now.getMonth();
      $el.innerHTML = '<div class="view-loader"><div class="spinner"></div><div class="loader-text">Загрузка календаря…</div></div>';
      await load();
      renderMonth($el);
    },
    onDataChanged(section) {
      if (section === 'calendar') {
        load().then(() => renderMonth(document.getElementById('kiosk-content')));
      }
    }
  };
})();
