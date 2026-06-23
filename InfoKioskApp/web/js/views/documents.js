/* views/documents.js — список документов карточками + in-app просмотр.
 *
 * Поддерживаемые форматы:
 *   - Изображения (png/jpg/gif/...) — <img>
 *   - Видео (mp4/webm/...) — <video controls>
 *   - Аудио (mp3/wav/...) — <audio controls>
 *   - PDF — <iframe> (WebView2 умеет рендерить PDF)
 *   - HTML — <iframe>
 *   - Текст (txt/json/csv/md/log) — <pre> с подгрузкой через fetch
 *   - XLSX/XLS — таблица с навигацией по листам (через bridge documents.xlsx.view)
 *   - DOCX/DOC/PPT/PPTX — информационная заглушка (не поддерживается в киоске)
 */
window.KioskViews.documents = (() => {
  'use strict';

  let files = [];
  const $content = () => document.getElementById('kiosk-content');

  async function load() {
    files = (await window.kiosk.call('documents.list')) || [];
  }

  function iconFor(ext) {
    return {
      pdf:  '📕', doc: '📘', docx: '📘', xls: '📗', xlsx: '📗',
      ppt:  '📙', pptx: '📙',
      txt:  '📄', rtf: '📄',
      jpg:  '🖼️', jpeg: '🖼️', png: '🖼️', svg: '🖼️',
      gif:  '🖼️', webp: '🖼️', bmp: '🖼️',
      mp4:  '🎬', webm: '🎬', mov: '🎬',
      mp3:  '🎵', wav: '🎵', ogg: '🎵',
      zip:  '📦', rar: '📦', '7z': '📦',
      json: '⚙️', csv: '📊', html: '🌐', htm: '🌐'
    }[ext] || '📄';
  }

  function humanSize(bytes) {
    if (!bytes) return '—';
    const u = ['B','KB','MB','GB']; let i = 0;
    while (bytes >= 1024 && i < u.length-1) { bytes /= 1024; i++; }
    return bytes.toFixed(bytes < 10 ? 1 : 0) + ' ' + u[i];
  }

  function fileUrl(name) {
    return '/download?target=documents&name=' + encodeURIComponent(name);
  }

  // ====================================================================
  // XLSX-просмотрщик с навигацией по листам
  // ====================================================================
  async function buildXlsxViewer(fileName) {
    // Загружаем первый лист.
    const initial = await window.kiosk.call('documents.xlsx.view', { name: fileName, sheet: 0 });
    return renderXlsxViewer(fileName, initial, 0);
  }

  function renderXlsxViewer(fileName, data, currentSheet) {
    if (!data || data.error) {
      return `<div class="doc-viewer-unsupported">
        <div class="doc-viewer-unsupported-icon">⚠️</div>
        <div class="doc-viewer-unsupported-title">${escapeHtml(fileName)}</div>
        <div class="doc-viewer-unsupported-text">Ошибка чтения файла: ${escapeHtml(data?.error || 'неизвестно')}</div>
      </div>`;
    }

    const sheets = data.sheets || [];
    const columns = data.columns || [];
    const rows = data.rows || [];

    // Вкладки листов (если их больше одного).
    const sheetTabs = sheets.length > 1
      ? `<div class="xlsx-sheet-tabs">
          ${sheets.map((s, i) => `<button class="xlsx-sheet-tab ${i === currentSheet ? 'active' : ''}" data-sheet="${i}">${escapeHtml(s)}</button>`).join('')}
        </div>`
      : '';

    // Таблица.
    const tableHtml = rows.length
      ? `<div class="xlsx-table-wrap"><table class="schedule-table xlsx-table">
          <thead><tr>${columns.map(c => `<th>${escapeHtml(c)}</th>`).join('')}</tr></thead>
          <tbody>${rows.map(r => `<tr>${r.map(c => `<td>${escapeHtml(c)}</td>`).join('')}</tr>`).join('')}</tbody>
        </table></div>`
      : `<div class="canteen-empty">Лист пуст</div>`;

    return `
      <div class="xlsx-viewer" data-file="${escapeHtml(fileName)}">
        ${sheetTabs}
        ${tableHtml}
        <div class="xlsx-meta">Лист ${currentSheet + 1} из ${sheets.length} • ${rows.length} строк</div>
      </div>
    `;
  }

  async function wireXlsxViewer() {
    const viewer = document.querySelector('.xlsx-viewer');
    if (!viewer) return;
    const fileName = viewer.dataset.file;
    viewer.querySelectorAll('.xlsx-sheet-tab').forEach(btn => {
      btn.addEventListener('click', async () => {
        const sheetIdx = parseInt(btn.dataset.sheet, 10);
        // Загружаем новый лист.
        const data = await window.kiosk.call('documents.xlsx.view', { name: fileName, sheet: sheetIdx });
        // Перерисовываем только содержимое вьюера.
        const newHtml = renderXlsxViewer(fileName, data, sheetIdx);
        // Заменяем весь вьюер.
        const wrapper = document.createElement('div');
        wrapper.innerHTML = newHtml;
        const newViewer = wrapper.firstElementChild;
        viewer.replaceWith(newViewer);
        // Перепривязываем обработчики.
        wireXlsxViewer();
      });
    });
  }

  // ====================================================================
  // Открытие файла
  // ====================================================================
  function openFile(file) {
    if (!file) return;
    const url = fileUrl(file.name);
    const ext = (file.ext || '').toLowerCase();
    const safeName = escapeHtml(file.name);

    let viewerHtml = '';

    if (['png','jpg','jpeg','gif','webp','bmp','svg'].includes(ext)) {
      viewerHtml = `<img class="doc-viewer-img" src="${url}" alt="${safeName}">`;
    }
    else if (['mp4','webm','mov','ogg','ogv'].includes(ext)) {
      viewerHtml = `<video class="doc-viewer-video" src="${url}" controls autoplay></video>`;
    }
    else if (['mp3','wav','ogg'].includes(ext)) {
      viewerHtml = `<audio class="doc-viewer-audio" src="${url}" controls autoplay></audio>`;
    }
    else if (ext === 'pdf') {
      // PDF — WebView2 умеет показывать через <iframe>.
      // #toolbar=0 скрывает верхнюю панель (печать, настройки, загрузка).
      // navpanes=0 скрывает боковую панель закладок.
      // view=FitH — вписать по ширине.
      viewerHtml = `<iframe class="doc-viewer-frame" src="${url}#toolbar=0&navpanes=0&view=FitH" title="${safeName}"></iframe>`;
    }
    else if (['txt','json','csv','md','log','rtf'].includes(ext)) {
      viewerHtml = `<pre class="doc-viewer-text" id="doc-viewer-text">Загрузка…</pre>`;
      fetchText(url);
    }
    else if (ext === 'html' || ext === 'htm') {
      viewerHtml = `<iframe class="doc-viewer-frame" src="${url}" title="${safeName}"></iframe>`;
    }
    else if (ext === 'xlsx' || ext === 'xls') {
      // XLSX — асинхронно грузим через bridge, показываем таблицу с листами.
      viewerHtml = `<div id="xlsx-viewer-loading" class="view-loader"><div class="spinner"></div><div class="loader-text">Чтение файла…</div></div>`;
      buildXlsxViewer(file.name).then(html => {
        const host = document.getElementById('xlsx-viewer-loading');
        if (host) {
          host.outerHTML = html;
          wireXlsxViewer();
        }
      });
    }
    else if (['doc','docx','ppt','pptx'].includes(ext)) {
      viewerHtml = `<div class="doc-viewer-unsupported">
        <div class="doc-viewer-unsupported-icon">${iconFor(ext)}</div>
        <div class="doc-viewer-unsupported-title">${safeName}</div>
        <div class="doc-viewer-unsupported-text">
          Предпросмотр документов этого типа (${ext.toUpperCase()}) не поддерживается в киоске.
          Обратитесь к администратору, чтобы открыть файл на отдельном компьютере.
        </div>
        <div class="doc-viewer-unsupported-meta">
          <span>${humanSize(file.size)}</span>
          <span class="dot">•</span>
          <span>${ext.toUpperCase()}</span>
        </div>
      </div>`;
    }
    else {
      viewerHtml = `<div class="doc-viewer-unsupported">
        <div class="doc-viewer-unsupported-icon">${iconFor(ext)}</div>
        <div class="doc-viewer-unsupported-title">${safeName}</div>
        <div class="doc-viewer-unsupported-text">
          Тип файла «${escapeHtml(ext || '—')}» не поддерживается для предпросмотра.
        </div>
      </div>`;
    }

    const modalHtml = `
      <div class="doc-viewer">
        <div class="doc-viewer-header">
          <div class="doc-viewer-title" title="${safeName}">
            <span class="doc-viewer-icon">${iconFor(ext)}</span>
            <span class="doc-viewer-name">${safeName}</span>
          </div>
          <div class="doc-viewer-meta">
            <span class="doc-viewer-size">${humanSize(file.size)}</span>
            <button type="button" class="doc-viewer-close" id="doc-viewer-close" aria-label="Закрыть">
              <svg viewBox="0 0 24 24" width="22" height="22"><path fill="currentColor" d="M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z"/></svg>
            </button>
          </div>
        </div>
        <div class="doc-viewer-body">${viewerHtml}</div>
      </div>
    `;

    window.Modal.open(modalHtml, { onClose: () => stopMedia() });
    const closeBtn = document.getElementById('doc-viewer-close');
    if (closeBtn) {
      closeBtn.addEventListener('click', () => window.Modal.close());
    }
  }

  async function fetchText(url) {
    try {
      const res = await fetch(url);
      const txt = await res.text();
      const el = document.getElementById('doc-viewer-text');
      if (el) el.textContent = txt;
    } catch (e) {
      const el = document.getElementById('doc-viewer-text');
      if (el) el.textContent = 'Ошибка загрузки: ' + (e.message || e);
    }
  }

  function stopMedia() {
    try {
      const v = document.querySelector('.doc-viewer-video');
      if (v) { v.pause(); v.src = ''; }
      const a = document.querySelector('.doc-viewer-audio');
      if (a) { a.pause(); a.src = ''; }
    } catch {}
  }

  function render($el) {
    $el.innerHTML = `
      <div class="view">
        <div class="view-header">
          <div>
            <div class="view-title">Документы</div>
            <div class="view-subtitle">${files.length} ${pluralRu(files.length, ['файл','файла','файлов'])}</div>
          </div>
        </div>
        ${files.length
          ? `<div class="doc-grid">
              ${files.map((f, i) => `<button type="button"
                    class="doc-card stagger-in"
                    style="--i:${i}"
                    data-name="${escapeHtml(f.name)}"
                    title="Открыть: ${escapeHtml(f.name)}">
                  <div class="doc-card-icon">${iconFor(f.ext)}</div>
                  <div class="doc-card-info">
                    <div class="doc-card-name">${escapeHtml(f.name)}</div>
                    <div class="doc-card-meta">
                      <span class="doc-card-ext">${escapeHtml(f.ext || 'file')}</span>
                      <span class="dot">•</span>
                      <span class="doc-card-size">${humanSize(f.size)}</span>
                    </div>
                  </div>
                </button>`).join('')}
            </div>`
          : `<div class="canteen-empty">Нет документов</div>`}
      </div>
    `;

    $el.querySelectorAll('.doc-card').forEach(card => {
      card.addEventListener('click', (e) => {
        e.preventDefault();
        const name = card.dataset.name;
        const file = files.find(f => f.name === name);
        if (file) openFile(file);
      });
    });
  }

  return {
    async mount($el) {
      $el.innerHTML = '<div class="view-loader"><div class="spinner"></div><div class="loader-text">Загрузка документов…</div></div>';
      await load();
      render($el);
    },
    onDataChanged(section) {
      if (section === 'documents') {
        load().then(() => render($content()));
      }
    }
  };
})();
