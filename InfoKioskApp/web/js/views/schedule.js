/* views/schedule.js — расписание уроков.
 *
 * Три раздела:
 *   1. Основное — xlsx, листы по классам, подсветка текущего урока.
 *   2. Изменения — xlsx/docx/pdf/png, показ как есть + подсветка.
 *   3. Другое — несколько файлов, разные форматы.
 *
 * Подсветка:
 *   - Идёт урок → строка зелёная.
 *   - Перемена → текущий урок оранжевый мигающий, следующий — оранжевый фон.
 */
window.KioskViews.schedule = (() => {
  let state = {
    tab: 'main',
    main: null,
    mainSheet: 0,
    changesInfo: null,
    changesHtml: null,
    changesData: null,
    otherFiles: [],
    otherActive: null,
    otherSheet: 0,
    otherCache: {},
    otherInfoCache: {},
    otherHtmlCache: {},
    currentLessonNum: 0,   // 0 = нет урока
    nextLessonNum: 0,      // 0 = нет следующего
    isBreak: false,
  };

  function escapeHtml(s) {
    if (s == null) return '';
    return String(s).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  }

  // Определяем текущий и следующий урок.
  async function detectCurrentLesson() {
    try {
      const bell = await window.kiosk.call('bell.get');
      if (!bell || !bell.variants) return;
      const activeId = bell.active || 'default';
      const variant = bell.variants[activeId] || Object.values(bell.variants)[0];
      if (!variant || !variant.lessons) return;

      const now = new Date();
      const nowSec = now.getHours() * 3600 + now.getMinutes() * 60 + now.getSeconds();

      state.currentLessonNum = 0;
      state.nextLessonNum = 0;
      state.isBreak = false;

      for (let i = 0; i < variant.lessons.length; i++) {
        const l = variant.lessons[i];
        const [sh, sm] = (l.start || '').split(':').map(Number);
        const [eh, em] = (l.end || '').split(':').map(Number);
        if (isNaN(sh) || isNaN(eh)) continue;
        const s = sh * 3600 + sm * 60;
        const e = eh * 3600 + em * 60;

        if (nowSec >= s && nowSec <= e) {
          // Идёт урок.
          state.currentLessonNum = l.num || (i + 1);
          state.isBreak = false;
          // Следующий урок.
          if (i + 1 < variant.lessons.length) {
            state.nextLessonNum = variant.lessons[i + 1].num || (i + 2);
          }
          return;
        }

        if (nowSec < s) {
          // Этот урок ещё не начался. Значит мы в перемене перед ним.
          if (i > 0) {
            // Перемена между i-1 и i.
            state.currentLessonNum = variant.lessons[i - 1].num || i;
            state.nextLessonNum = l.num || (i + 1);
            state.isBreak = true;
          } else {
            // Перед первым уроком — "скоро".
            state.nextLessonNum = l.num || 1;
            state.isBreak = false;
          }
          return;
        }
      }
      // Все уроки закончились.
      state.currentLessonNum = 0;
      state.nextLessonNum = 0;
    } catch (e) {
      console.warn('[schedule] detectCurrentLesson failed:', e);
    }
  }

  function getDayOfWeek() {
    const d = new Date().getDay();
    return d >= 1 && d <= 5 ? d : 0;
  }

  async function loadData(force) {
    if (force || !state.main) {
      try {
        state.main = await window.kiosk.call('schedule.main');
        state.mainSheet = state.main?.currentSheet || 0;
      } catch (e) {
        console.warn('[schedule] main load failed:', e);
        state.main = { error: 'Не удалось загрузить основное расписание' };
      }
    }
    if (force || state.changesInfo === null) {
      try {
        state.changesInfo = await window.kiosk.call('schedule.changes.info');
      } catch (e) {
        console.warn('[schedule] changes info failed:', e);
        state.changesInfo = { exists: false };
      }
      state.changesHtml = null;
      state.changesData = null;
      // Если xlsx — читаем как таблицу.
      if (state.changesInfo?.exists && state.changesInfo?.ext === '.xlsx') {
        try {
          state.changesData = await window.kiosk.call('schedule.changes');
        } catch (e) { console.warn('[schedule] changes data failed:', e); }
      }
      // Если docx — рендерим в HTML.
      if (state.changesInfo?.exists && state.changesInfo?.ext === '.docx') {
        try {
          state.changesHtml = await window.kiosk.call('schedule.changes.docx');
        } catch (e) { console.warn('[schedule] changes docx failed:', e); }
      }
    }
    try {
      const res = await window.kiosk.call('schedule.other.list');
      state.otherFiles = (res && res.files) || [];
      if (state.otherActive && !state.otherFiles.some(f => f.name === state.otherActive)) {
        state.otherActive = null;
        state.otherSheet = 0;
      }
      if (!state.otherActive && state.otherFiles.length) {
        state.otherActive = state.otherFiles[0].name;
        state.otherSheet = 0;
      }
    } catch (e) {
      state.otherFiles = [];
    }
    await detectCurrentLesson();
  }

  function shouldShowChangesTab() {
    return state.changesInfo?.exists === true;
  }

  // Рендер таблицы с умной подсветкой.
  function renderTable(data, highlightLesson) {
    if (!data || data.error) return `<div class="canteen-empty">${escapeHtml((data && data.error) || 'Нет данных')}</div>`;
    if (!data.rows || !data.rows.length) return `<div class="canteen-empty">Таблица пуста</div>`;

    const columns = data.columns || [];
    const dayOfWeek = getDayOfWeek();
    let dayColIdx = -1;
    const dayNames = ['понедельник', 'вторник', 'среда', 'четверг', 'пятница'];
    if (dayOfWeek >= 1 && dayOfWeek <= 5) {
      const targetDay = dayNames[dayOfWeek - 1];
      for (let i = 0; i < columns.length; i++) {
        if (columns[i].toLowerCase().trim().startsWith(targetDay)) { dayColIdx = i; break; }
      }
    }

    const head = columns.map((c, i) => {
      const isToday = i === dayColIdx;
      return `<th class="${isToday ? 'schedule-col-today' : ''}">${escapeHtml(c)}</th>`;
    }).join('');

    const body = data.rows.map((r, rowIdx) => {
      const lessonNum = rowIdx + 1;
      let trClass = '';

      if (highlightLesson) {
        if (!state.isBreak && lessonNum === state.currentLessonNum) {
          // Идёт урок — зелёный.
          trClass = 'schedule-row-active';
        } else if (state.isBreak) {
          if (lessonNum === state.currentLessonNum) {
            // Перемена — прошлый урок оранжевый мигающий.
            trClass = 'schedule-row-break-pulse';
          } else if (lessonNum === state.nextLessonNum) {
            // Следующий урок — оранжевый фон.
            trClass = 'schedule-row-break-next';
          }
        }
      }

      const tds = r.map((c, colIdx) => {
        const isTodayCol = colIdx === dayColIdx;
        const classes = [];
        if (isTodayCol) classes.push('schedule-cell-today');
        const cls = classes.length ? ` class="${classes.join(' ')}"` : '';
        return `<td${cls}>${escapeHtml(c)}</td>`;
      }).join('');
      return `<tr class="${trClass}">${tds}</tr>`;
    }).join('');

    return `<table class="schedule-table"><thead><tr>${head}</tr></thead><tbody>${body}</tbody></table>`;
  }

  // Рендер изменённого расписания.
  function renderChanges() {
    const info = state.changesInfo;
    if (!info || !info.exists) {
      return '<div class="canteen-empty">Нет изменённого расписания</div>';
    }

    const ext = info.ext;
    const url = info.url;

    // XLSX — таблица с подсветкой.
    if (ext === '.xlsx' || ext === '.xls') {
      const data = state.changesData;
      return renderTable(data, true);
    }

    // DOCX — рендерим в HTML с подсветкой.
    if (ext === '.docx' || ext === '.doc') {
      const htmlData = state.changesHtml;
      if (!htmlData || !htmlData.html) {
        return '<div class="canteen-empty">Не удалось отобразить DOCX</div>';
      }
      let html = htmlData.html;
      // Применяем подсветку строк — добавляем классы к <tr>.
      // Строки данных имеют класс lesson-row-N (0-based: lesson-row-0 = урок 1).
      if (state.currentLessonNum > 0 || state.nextLessonNum > 0) {
        if (!state.isBreak && state.currentLessonNum > 0) {
          // Идёт урок — зелёная строка.
          const idx = state.currentLessonNum - 1;
          html = html.replace(`class="lesson-row-${idx}"`, `class="lesson-row-${idx} schedule-row-active"`);
        } else if (state.isBreak) {
          // Перемена — прошлый оранжевый мигающий, следующий оранжевый фон.
          if (state.currentLessonNum > 0) {
            const idx = state.currentLessonNum - 1;
            html = html.replace(`class="lesson-row-${idx}"`, `class="lesson-row-${idx} schedule-row-break-pulse"`);
          }
          if (state.nextLessonNum > 0) {
            const idx = state.nextLessonNum - 1;
            html = html.replace(`class="lesson-row-${idx}"`, `class="lesson-row-${idx} schedule-row-break-next"`);
          }
        }
      }
      return `<div class="docx-schedule-container">${html}</div>`;
    }

    // PDF — iframe.
    if (ext === '.pdf') {
      return `<iframe src="${url}#toolbar=0&navpanes=0&view=FitH" style="width:100%;height:calc(100vh - 280px);border:none;border-radius:var(--radius-md);background:#fff;"></iframe>`;
    }

    // Изображения.
    if (['.png', '.jpg', '.jpeg', '.gif', '.webp', '.bmp'].includes(ext)) {
      return `<div style="text-align:center;"><img src="${url}" style="max-width:100%;max-height:calc(100vh - 280px);border-radius:var(--radius-md);"></div>`;
    }

    // Другое.
    return `<div class="canteen-empty"><div>Файл: ${escapeHtml(info.fileName)}</div><a href="${url}" target="_blank" style="display:inline-block;margin-top:14px;padding:10px 20px;background:var(--accent);color:#fff;border-radius:var(--radius-sm);text-decoration:none;">Открыть файл</a></div>`;
  }

  function renderSheetTabs(sheets, currentSheet) {
    if (!sheets || sheets.length <= 1) return '';
    return `<div class="schedule-subtabs">` +
      sheets.map((s, i) => `<button class="schedule-subtab ${i === currentSheet ? 'active' : ''}" data-sheet="${i}">${escapeHtml(s)}</button>`).join('') +
      `</div>`;
  }

  async function loadMainSheet(sheetIdx) {
    state.main = await window.kiosk.call('schedule.sheet', { which: 'main', sheet: sheetIdx });
    state.mainSheet = sheetIdx;
  }

  async function loadOtherSheet(name, sheetIdx) {
    const key = name + ':' + sheetIdx;
    if (state.otherCache[key]) {
      state.otherCache[name] = state.otherCache[key];
      state.otherSheet = sheetIdx;
      return;
    }
    const data = await window.kiosk.call('schedule.other.get', { name, sheet: sheetIdx });
    state.otherCache[name] = data;
    state.otherCache[key] = data;
    state.otherSheet = sheetIdx;
  }

  async function loadOtherInfo(name) {
    if (state.otherInfoCache[name]) return state.otherInfoCache[name];
    const info = await window.kiosk.call('schedule.other.info', { name });
    state.otherInfoCache[name] = info;
    return info;
  }

  async function renderOther($el) {
    if (!state.otherActive) {
      $el.innerHTML += '<div class="canteen-empty">Нет дополнительных расписаний</div>';
      return;
    }

    const info = await loadOtherInfo(state.otherActive);
    const ext = info?.ext || '';
    const url = info?.url || '';

    if (ext === '.xlsx' || ext === '.xls') {
      if (!state.otherCache[state.otherActive]) {
        await loadOtherSheet(state.otherActive, 0);
      }
      const data = state.otherCache[state.otherActive];
      const sheetsHtml = renderSheetTabs(data?.sheets, state.otherSheet);
      $el.innerHTML += sheetsHtml + renderTable(data, false);
    } else if (ext === '.docx' || ext === '.doc') {
      if (!state.otherHtmlCache[state.otherActive]) {
        const htmlData = await window.kiosk.call('schedule.other.docx', { name: state.otherActive });
        state.otherHtmlCache[state.otherActive] = htmlData;
      }
      const htmlData = state.otherHtmlCache[state.otherActive];
      $el.innerHTML += `<div class="docx-schedule-container">${htmlData?.html || 'Ошибка'}</div>`;
    } else if (ext === '.pdf') {
      $el.innerHTML += `<iframe src="${url}#toolbar=0&navpanes=0&view=FitH" style="width:100%;height:calc(100vh - 280px);border:none;border-radius:var(--radius-md);background:#fff;"></iframe>`;
    } else if (['.png', '.jpg', '.jpeg', '.gif', '.webp', '.bmp'].includes(ext)) {
      $el.innerHTML += `<div style="text-align:center;"><img src="${url}" style="max-width:100%;max-height:calc(100vh - 280px);border-radius:var(--radius-md);"></div>`;
    } else if (url) {
      $el.innerHTML += `<div class="canteen-empty"><div>Файл: ${escapeHtml(info.fileName)}</div><a href="${url}" target="_blank" style="display:inline-block;margin-top:14px;padding:10px 20px;background:var(--accent);color:#fff;border-radius:var(--radius-sm);text-decoration:none;">Открыть файл</a></div>`;
    } else {
      $el.innerHTML += '<div class="canteen-empty">Файл не найден</div>';
    }
  }

  // Подсветка объединённых по вертикали ячеек.
  // В DocxToHtmlConverter: row 0 = заголовок, row 1 = урок 1, row 2 = урок 2, ...
  // Класс lesson-row-N (0-based) ставится на <tr>. Если ячейка имеет rowspan > 1,
  // она физически находится в строке начала объединения, но визуально покрывает
  // несколько уроков. Когда текущий урок попадает в диапазон объединения,
  // нужно подсветить саму merged-ячейку дополнительно к подсветке строки.
  function applyMergedCellHighlight(container) {
    if (!container) return;
    const currentLesson = state.currentLessonNum;
    const nextLesson = state.nextLessonNum;
    if (!currentLesson && !nextLesson) return;

    const rows = Array.from(container.querySelectorAll('tr'));
    if (!rows.length) return;

    container.querySelectorAll('td[rowspan]').forEach(td => {
      const rowspan = parseInt(td.getAttribute('rowspan') || '1', 10);
      if (rowspan <= 1) return;
      const tr = td.parentElement;
      if (!tr) return;
      const startRowIdx = rows.indexOf(tr);
      if (startRowIdx < 0) return;

      // row 0 = заголовок, row 1 = урок 1, row 2 = урок 2, ...
      // Значит startRowIdx = N → урок N (1-based).
      const startLesson = startRowIdx;
      const endLesson = startRowIdx + rowspan - 1;

      // Идёт урок — подсвечиваем merged-ячейку зелёным.
      if (!state.isBreak && currentLesson > 0 &&
          currentLesson >= startLesson && currentLesson <= endLesson) {
        td.classList.add('schedule-cell-active');
      }
      // Перемена — прошлый урок мигает, следующий подсвечен.
      if (state.isBreak) {
        if (currentLesson > 0 &&
            currentLesson >= startLesson && currentLesson <= endLesson) {
          td.classList.add('schedule-cell-break-pulse');
        }
        if (nextLesson > 0 &&
            nextLesson >= startLesson && nextLesson <= endLesson) {
          td.classList.add('schedule-cell-break-next');
        }
      }
    });
  }

  async function render($el) {
    const showChanges = shouldShowChangesTab();

    // Защита: если активная вкладка "Изменения", но файла изменений нет,
    // переключаемся на "Основное". Аналогично для "Другое".
    if (state.tab === 'changes' && !showChanges) state.tab = 'main';
    if (state.tab === 'other' && !state.otherFiles.length) state.tab = 'main';

    const isChanges = state.tab === 'changes';
    const isOther = state.tab === 'other';

    let subtitle = 'Основное расписание';
    if (isChanges) subtitle = 'Изменения в расписании';
    if (isOther && state.otherActive) subtitle = state.otherActive;

    const tabsHtml = [];
    tabsHtml.push(`<button class="schedule-tab ${state.tab==='main'?'active':''}" data-tab="main">Основное</button>`);
    if (showChanges) {
      tabsHtml.push(`<button class="schedule-tab ${state.tab==='changes'?'active':''}" data-tab="changes">Изменения</button>`);
    }
    if (state.otherFiles.length) {
      tabsHtml.push(`<button class="schedule-tab ${state.tab==='other'?'active':''}" data-tab="other">Другое</button>`);
    }

    let otherFileTabsHtml = '';
    if (isOther && state.otherFiles.length > 1) {
      otherFileTabsHtml = `<div class="schedule-file-tabs">` +
        state.otherFiles.map(f => `<button class="schedule-file-tab ${f.name===state.otherActive?'active':''}" data-file="${escapeHtml(f.name)}">${escapeHtml(f.name)}</button>`).join('') +
        `</div>`;
    }

    let contentHtml = '';
    let sheetTabsHtml = '';

    if (isChanges) {
      contentHtml = renderChanges();
    } else if (isOther) {
      const tmpDiv = document.createElement('div');
      await renderOther(tmpDiv);
      contentHtml = tmpDiv.innerHTML;
    } else {
      const data = state.main;
      sheetTabsHtml = renderSheetTabs(data?.sheets, state.mainSheet);
      contentHtml = renderTable(data, true);
    }

    $el.innerHTML = `
      <div class="view">
        <div class="view-header">
          <div>
            <div class="view-title">Расписание</div>
            <div class="view-subtitle">${escapeHtml(subtitle)}</div>
          </div>
        </div>
        <div class="schedule-tabs">
          ${tabsHtml.join('')}
        </div>
        ${otherFileTabsHtml}
        ${sheetTabsHtml}
        <div id="schedule-content">${contentHtml}</div>
      </div>
    `;

    // Подсветка объединённых по вертикали ячеек в DOCX.
    // Если урок объединён с соседними (например, 2-4) и текущий урок
    // попадает в этот диапазон, нужно подсветить саму merged-ячейку,
    // а не только строку.
    if (isChanges && state.changesInfo?.ext === '.docx') {
      applyMergedCellHighlight($el.querySelector('.docx-schedule-container'));
    }

    $el.querySelectorAll('.schedule-tab').forEach(b => {
      b.addEventListener('click', async () => {
        state.tab = b.dataset.tab;
        await render($el);
      });
    });

    $el.querySelectorAll('.schedule-file-tab').forEach(b => {
      b.addEventListener('click', async () => {
        state.otherActive = b.dataset.file;
        state.otherSheet = 0;
        await render($el);
      });
    });

    $el.querySelectorAll('.schedule-subtab').forEach(b => {
      b.addEventListener('click', async () => {
        const idx = parseInt(b.dataset.sheet, 10);
        if (isOther) {
          await loadOtherSheet(state.otherActive, idx);
        } else {
          await loadMainSheet(idx);
        }
        await render($el);
      });
    });
  }

  let highlightTimer = null;
  let statusChangeListener = null;

  // Обработчик смены статуса урока (вызывается из clock.js, когда таймер
  // "до конца:" / "до начала:" обнуляется и статус меняется).
  // Запускает перерасчёт текущего урока и перерисовку подсветки.
  async function onLessonStatusChanged() {
    await detectCurrentLesson();
    const $content = document.getElementById('kiosk-content');
    if ($content && (state.tab === 'main' || state.tab === 'changes')) {
      try { await render($content); } catch (e) { console.warn('[schedule] re-render on status change failed:', e); }
    }
  }

  return {
    async mount($el) {
      $el.innerHTML = '<div class="view-loader"><div class="spinner"></div><div class="loader-text">Загрузка расписания…</div></div>';
      await loadData();
      await render($el);
      if (highlightTimer) clearInterval(highlightTimer);
      highlightTimer = setInterval(async () => {
        await detectCurrentLesson();
        if (state.tab === 'main' || state.tab === 'changes') {
          render(document.getElementById('kiosk-content'));
        }
      }, 60000);
      // Подписываемся на событие смены статуса урока от clock.js.
      if (!statusChangeListener) {
        statusChangeListener = () => { onLessonStatusChanged(); };
        window.addEventListener('kiosk-lesson-status', statusChangeListener);
      }
    },
    // Публичный метод для внешнего вызова (например, из clock.js).
    refreshHighlight() { return onLessonStatusChanged(); },
    unmount() {
      if (highlightTimer) { clearInterval(highlightTimer); highlightTimer = null; }
      if (statusChangeListener) {
        window.removeEventListener('kiosk-lesson-status', statusChangeListener);
        statusChangeListener = null;
      }
    },
    onConfig() {},
    onDataChanged(section) {
      if (section === 'schedule' || section === 'bell') {
        state.otherCache = {};
        state.otherInfoCache = {};
        state.otherHtmlCache = {};
        state.changesInfo = null;
        state.changesHtml = null;
        state.changesData = null;
        loadData(true).then(() => {
          render(document.getElementById('kiosk-content'));
        });
      }
    }
  };
})();

window.escapeHtml = function(s) {
  if (s == null) return '';
  return String(s).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
};
