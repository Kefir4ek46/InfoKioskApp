/* views/custom.js — пользовательский раздел (список файлов из папки).
 *
 * Использует тот же просмотрщик в модалке, что и Documents — никаких
 * новых окон и скачиваний, киоск нельзя покинуть.
 */
window.KioskViews.custom = (() => {
  'use strict';

  let section = null;
  let files = [];

  function iconFor(ext) {
    return {
      pdf:'📕', doc:'📘', docx:'📘', xls:'📗', xlsx:'📗',
      ppt:'📙', pptx:'📙',
      txt:'📄', rtf:'📄',
      jpg:'🖼️', jpeg:'🖼️', png:'🖼️', svg:'🖼️',
      gif:'🖼️', webp:'🖼️', bmp:'🖼️',
      mp4:'🎬', webm:'🎬', mov:'🎬',
      mp3:'🎵', wav:'🎵', ogg:'🎵',
      zip:'📦', rar:'📦', '7z':'📦',
      json:'⚙️', csv:'📊', html:'🌐', htm:'🌐'
    }[ext] || '📄';
  }
  function humanSize(b) {
    if (!b) return '—';
    const u = ['B','KB','MB','GB']; let i = 0;
    while (b >= 1024 && i < u.length-1) { b /= 1024; i++; }
    return b.toFixed(b < 10 ? 1 : 0) + ' ' + u[i];
  }

  function fileUrl(f) {
    const folder = section.folderPath || section.FolderPath || '';
    return '/download?target=custom&name=' + encodeURIComponent(f.name) +
           '&folderPath=' + encodeURIComponent(folder);
  }

  function openFile(file) {
    if (!file) return;
    const url = fileUrl(file);
    const ext = (file.ext || '').toLowerCase();
    const safeName = escapeHtml(file.name);

    let viewerHtml = '';
    if (['png','jpg','jpeg','gif','webp','bmp','svg'].includes(ext)) {
      viewerHtml = `<img class="doc-viewer-img" src="${url}" alt="${safeName}">`;
    } else if (['mp4','webm','mov','ogg','ogv'].includes(ext)) {
      viewerHtml = `<video class="doc-viewer-video" src="${url}" controls autoplay></video>`;
    } else if (['mp3','wav','ogg'].includes(ext)) {
      viewerHtml = `<audio class="doc-viewer-audio" src="${url}" controls autoplay></audio>`;
    } else if (ext === 'pdf') {
      viewerHtml = `<iframe class="doc-viewer-frame" src="${url}" title="${safeName}"></iframe>`;
    } else if (['txt','json','csv','html','htm','rtf','log','md'].includes(ext)) {
      if (ext === 'html' || ext === 'htm') {
        viewerHtml = `<iframe class="doc-viewer-frame" src="${url}" title="${safeName}"></iframe>`;
      } else {
        viewerHtml = `<pre class="doc-viewer-text" id="doc-viewer-text">Загрузка…</pre>`;
        fetchText(url);
      }
    } else if (['doc','docx','xls','xlsx','ppt','pptx'].includes(ext)) {
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
    } else {
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
    if (closeBtn) closeBtn.addEventListener('click', () => window.Modal.close());
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
            <div class="view-title">${section.Icon || '📁'} ${escapeHtml(section.Name || section.name || 'Раздел')}</div>
            <div class="view-subtitle">${files.length} ${pluralRu(files.length, ['файл','файла','файлов'])}</div>
          </div>
        </div>
        ${files.length ? `<div class="doc-grid">
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
        </div>` : `<div class="canteen-empty">Папка пуста</div>`}
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
    async mount($el, params) {
      section = params.section;
      $el.innerHTML = '<div class="view-loader"><div class="spinner"></div><div class="loader-text">Загрузка…</div></div>';
      try {
        const res = await window.kiosk.call('customsection.files', {
          folderPath: section.folderPath || section.FolderPath || ''
        });
        files = (res && res.files) || [];
      } catch (e) { files = []; }
      render($el);
    }
  };
})();
