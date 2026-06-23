/* leaderboard.js — таблица рекордов для игр-расширений.
 *
 * API:
 *   window.Leaderboard.getTop(gameId) — возвращает массив записей.
 *   window.Leaderboard.submitScore(gameId, name, score) — добавляет рекорд.
 *   window.Leaderboard.open(gameId, options) — открывает модалку с таблицей.
 *     options: { title, score, onSubmitted }
 *
 * Хранилище: data/leaderboards.json (через HTTP /leaderboard/*).
 */
window.Leaderboard = (() => {
  const api = location.origin;

  function escapeHtml(s) {
    if (s == null) return '';
    return String(s).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  }

  // Возвращает топ-N рекордов игры.
  async function getTop(gameId) {
    try {
      const res = await fetch(`${api}/leaderboard/get?game=${encodeURIComponent(gameId)}`);
      if (!res.ok) return [];
      const data = await res.json();
      return data.entries || [];
    } catch (e) {
      console.warn('[leaderboard] getTop failed:', e);
      return [];
    }
  }

  // Добавляет рекорд. Возвращает обновлённый топ-N.
  async function submitScore(gameId, name, score) {
    try {
      const res = await fetch(`${api}/leaderboard/submit`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ game: gameId, name, score })
      });
      if (!res.ok) return [];
      const data = await res.json();
      return data.entries || [];
    } catch (e) {
      console.warn('[leaderboard] submitScore failed:', e);
      return [];
    }
  }

  // Форматирование даты "2026-06-23T12:34:56" → "23.06 12:34".
  function fmtDate(iso) {
    if (!iso) return '';
    try {
      const d = new Date(iso);
      const dd = String(d.getDate()).padStart(2, '0');
      const mm = String(d.getMonth() + 1).padStart(2, '0');
      const hh = String(d.getHours()).padStart(2, '0');
      const mi = String(d.getMinutes()).padStart(2, '0');
      return `${dd}.${mm} ${hh}:${mi}`;
    } catch { return ''; }
  }

  // Открывает модалку с таблицей рекордов.
  // options:
  //   title — заголовок модалки (по умолчанию "🏆 Таблица рекордов").
  //   score — если задано, показывает форму ввода имени для нового рекорда.
  //   onSubmitted — callback после отправки рекорда.
  async function open(gameId, options = {}) {
    const title = options.title || '🏆 Таблица рекордов';
    const newScore = typeof options.score === 'number' ? options.score : null;

    // Загружаем текущий топ.
    let entries = await getTop(gameId);

    // Рендерим модалку.
    renderModal(gameId, title, entries, newScore, options);

    // Если есть новый рекорд — показываем форму ввода имени.
    if (newScore !== null) {
      // Проверяем, попал ли рекорд в топ-10 (или если топ пуст — тоже показываем).
      const minScore = entries.length >= 10 ? (entries[entries.length - 1]?.score || 0) : -1;
      if (newScore > minScore || entries.length < 10) {
        showNameInput(gameId, newScore, options);
      }
    }
  }

  function renderModal(gameId, title, entries, newScore, options) {
    let modal = document.getElementById('leaderboard-modal');
    if (!modal) {
      modal = document.createElement('div');
      modal.id = 'leaderboard-modal';
      modal.className = 'leaderboard-modal-overlay';
      document.body.appendChild(modal);
    }

    const rows = entries.length
      ? entries.map((e, i) => {
          const medal = i === 0 ? '🥇' : i === 1 ? '🥈' : i === 2 ? '🥉' : `${i + 1}.`;
          const isNew = newScore !== null && e.score === newScore && i < 3;
          return `<tr class="${isNew ? 'leaderboard-new-row' : ''}">
            <td class="leaderboard-rank">${medal}</td>
            <td class="leaderboard-name">${escapeHtml(e.name)}</td>
            <td class="leaderboard-score">${e.score}</td>
            <td class="leaderboard-date">${fmtDate(e.date)}</td>
          </tr>`;
        }).join('')
      : '<tr><td colspan="4" class="leaderboard-empty">Пока нет рекордов. Будьте первым!</td></tr>';

    modal.innerHTML = `
      <div class="leaderboard-modal-card">
        <div class="leaderboard-modal-header">
          <h2>${escapeHtml(title)}</h2>
          <button class="leaderboard-close-btn" onclick="window.Leaderboard.close()">✕</button>
        </div>
        <div id="leaderboard-name-input-area"></div>
        <table class="leaderboard-table">
          <thead>
            <tr>
              <th>#</th>
              <th>Имя</th>
              <th>Очки</th>
              <th>Дата</th>
            </tr>
          </thead>
          <tbody id="leaderboard-tbody">${rows}</tbody>
        </table>
        ${newScore !== null ? `<div class="leaderboard-new-score">Ваш счёт: <strong>${newScore}</strong></div>` : ''}
      </div>
    `;
    modal.style.display = 'flex';
    // Закрытие по клику на фон.
    modal.addEventListener('click', (e) => {
      if (e.target === modal) close();
    }, { once: true });
  }

  function showNameInput(gameId, score, options) {
    const area = document.getElementById('leaderboard-name-input-area');
    if (!area) return;
    area.innerHTML = `
      <div class="leaderboard-name-input">
        <div class="leaderboard-name-input-title">🎉 Новый рекорд!</div>
        <div class="leaderboard-name-input-hint">Введите имя для таблицы:</div>
        <div class="leaderboard-name-input-row">
          <input type="text" id="leaderboard-name-field" placeholder="Ваше имя" maxlength="20"
                 value="${escapeHtml(localStorage.getItem('leaderboard-last-name') || '')}">
          <button id="leaderboard-name-submit">Отправить</button>
        </div>
      </div>
    `;
    const input = document.getElementById('leaderboard-name-field');
    const btn = document.getElementById('leaderboard-name-submit');
    if (input) {
      input.focus();
      input.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') doSubmit();
      });
    }
    if (btn) btn.addEventListener('click', doSubmit);

    async function doSubmit() {
      const name = (input?.value || '').trim() || 'Аноним';
      localStorage.setItem('leaderboard-last-name', name);
      // Отправляем рекорд.
      const entries = await submitScore(gameId, name, score);
      // Обновляем таблицу.
      const tbody = document.getElementById('leaderboard-tbody');
      if (tbody && entries.length) {
        tbody.innerHTML = entries.map((e, i) => {
          const medal = i === 0 ? '🥇' : i === 1 ? '🥈' : i === 2 ? '🥉' : `${i + 1}.`;
          const isNew = e.name === name && e.score === score;
          return `<tr class="${isNew ? 'leaderboard-new-row' : ''}">
            <td class="leaderboard-rank">${medal}</td>
            <td class="leaderboard-name">${escapeHtml(e.name)}</td>
            <td class="leaderboard-score">${e.score}</td>
            <td class="leaderboard-date">${fmtDate(e.date)}</td>
          </tr>`;
        }).join('');
      }
      // Скрываем форму ввода.
      area.innerHTML = '<div class="leaderboard-submitted">✅ Рекорд сохранён!</div>';
      if (options.onSubmitted) options.onSubmitted(entries);
    }
  }

  function close() {
    const modal = document.getElementById('leaderboard-modal');
    if (modal) modal.style.display = 'none';
  }

  return {
    getTop,
    submitScore,
    open,
    close
  };
})();
