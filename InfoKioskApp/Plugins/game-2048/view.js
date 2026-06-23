/* plugins/game-2048/view.js — игра 2048.
 *
 * Управление: стрелки или свайпы. Цель — собрать плитку 2048.
 * Счёт = сумма всех объединённых плиток.
 */
window.KioskViews = window.KioskViews || {};

window.KioskViews['game-2048'] = (() => {
  'use strict';

  function getSettings() {
    const s = (window.KioskPlugins && window.KioskPlugins['game-2048']
      ? window.KioskPlugins['game-2048'].settings : {}) || {};
    return {
      gridSize: parseInt(s.gridSize, 10) || 4,
      winTile: parseInt(s.winTile, 10) || 2048
    };
  }

  let grid = [];
  let score = 0;
  let gameOver = false;
  let won = false;
  let size = 4;
  let winTile = 2048;

  function initState() {
    const cfg = getSettings();
    size = cfg.gridSize;
    winTile = cfg.winTile;
    grid = Array(size).fill(null).map(() => Array(size).fill(0));
    score = 0;
    gameOver = false;
    won = false;
    addRandomTile();
    addRandomTile();
  }

  function addRandomTile() {
    const empty = [];
    for (let r = 0; r < size; r++) {
      for (let c = 0; c < size; c++) {
        if (grid[r][c] === 0) empty.push({ r, c });
      }
    }
    if (!empty.length) return false;
    const { r, c } = empty[Math.floor(Math.random() * empty.length)];
    grid[r][c] = Math.random() < 0.9 ? 2 : 4;
    return true;
  }

  // Сдвиг одной строки влево (с объединением).
  function slideLeft(row) {
    let arr = row.filter(x => x !== 0);
    let gained = 0;
    for (let i = 0; i < arr.length - 1; i++) {
      if (arr[i] === arr[i + 1]) {
        arr[i] *= 2;
        gained += arr[i];
        if (arr[i] >= winTile) won = true;
        arr[i + 1] = 0;
      }
    }
    arr = arr.filter(x => x !== 0);
    while (arr.length < size) arr.push(0);
    return { row: arr, gained };
  }

  function rotateLeft(g) {
    const n = g.length;
    const res = Array(n).fill(null).map(() => Array(n).fill(0));
    for (let r = 0; r < n; r++) {
      for (let c = 0; c < n; c++) {
        res[n - 1 - c][r] = g[r][c];
      }
    }
    return res;
  }

  function rotateRight(g) {
    const n = g.length;
    const res = Array(n).fill(null).map(() => Array(n).fill(0));
    for (let r = 0; r < n; r++) {
      for (let c = 0; c < n; c++) {
        res[c][n - 1 - r] = g[r][c];
      }
    }
    return res;
  }

  function move(direction) {
    if (gameOver) return;
    const before = JSON.stringify(grid);
    let totalGained = 0;

    // Поворачиваем сетку так, чтобы сдвиг всегда был «влево».
    let g = grid;
    if (direction === 'up')    g = rotateLeft(g);
    if (direction === 'right') g = rotateLeft(g); g = rotateLeft(g); if (direction === 'right') g = rotateLeft(g);
    // Упрощённо: для каждого направления делаем нужные повороты.
    // Сбрасываем, если направление было 'right' или 'down' — пересчитаем ниже.
    g = grid;
    if (direction === 'up') {
      g = rotateLeft(g);
      for (let r = 0; r < size; r++) {
        const res = slideLeft(g[r]);
        g[r] = res.row;
        totalGained += res.gained;
      }
      g = rotateRight(g);
    } else if (direction === 'down') {
      g = rotateRight(g);
      for (let r = 0; r < size; r++) {
        const res = slideLeft(g[r]);
        g[r] = res.row;
        totalGained += res.gained;
      }
      g = rotateLeft(g);
    } else if (direction === 'left') {
      for (let r = 0; r < size; r++) {
        const res = slideLeft(g[r]);
        g[r] = res.row;
        totalGained += res.gained;
      }
    } else if (direction === 'right') {
      g = rotateLeft(g);
      g = rotateLeft(g);
      for (let r = 0; r < size; r++) {
        const res = slideLeft(g[r]);
        g[r] = res.row;
        totalGained += res.gained;
      }
      g = rotateRight(g);
      g = rotateRight(g);
    }
    grid = g;

    const after = JSON.stringify(grid);
    if (before !== after) {
      score += totalGained;
      addRandomTile();
      checkGameOver();
      draw();
      updateScore();
    }
  }

  function checkGameOver() {
    // Есть пустые клетки — не конец.
    for (let r = 0; r < size; r++) {
      for (let c = 0; c < size; c++) {
        if (grid[r][c] === 0) return;
      }
    }
    // Есть соседние одинаковые — не конец.
    for (let r = 0; r < size; r++) {
      for (let c = 0; c < size; c++) {
        if (c < size - 1 && grid[r][c] === grid[r][c + 1]) return;
        if (r < size - 1 && grid[r][c] === grid[r + 1][c]) return;
      }
    }
    gameOver = true;
    onGameOver();
  }

  function onGameOver() {
    const overlay = document.getElementById('g2048-overlay');
    if (overlay) {
      overlay.style.display = '';
      const msg = document.getElementById('g2048-overlay-msg');
      if (msg) msg.textContent = won ? `🎉 Победа! Счёт: ${score}` : `Игра окончена! Счёт: ${score}`;
    }
    if (score > 0 && window.Leaderboard) {
      setTimeout(() => {
        window.Leaderboard.open('game-2048', {
          title: '🔢 2048 — Таблица рекордов',
          score
        });
      }, 600);
    }
  }

  function tileColor(val) {
    const colors = {
      2: '#3a3a3a', 4: '#4a3a2a', 8: '#e8a04a', 16: '#e88a3a',
      32: '#e86a3a', 64: '#e84a3a', 128: '#e8d23a', 256: '#e8c41a',
      512: '#e8b40a', 1024: '#a8e83a', 2048: '#3ae8a0', 4096: '#3ae8e8',
      8192: '#3a8ae8'
    };
    return colors[val] || '#1a1a1a';
  }

  function tileText(val) {
    if (val <= 0) return '';
    return String(val);
  }

  function draw() {
    const gridEl = document.getElementById('g2048-grid');
    if (!gridEl) return;
    gridEl.style.gridTemplateColumns = `repeat(${size}, 1fr)`;
    gridEl.innerHTML = '';
    for (let r = 0; r < size; r++) {
      for (let c = 0; c < size; c++) {
        const val = grid[r][c];
        const cell = document.createElement('div');
        cell.className = 'g2048-cell' + (val > 0 ? ' g2048-filled' : '');
        if (val > 0) {
          cell.style.background = tileColor(val);
          cell.style.color = val <= 4 ? '#fff' : '#000';
          cell.textContent = tileText(val);
          if (val >= 1024) cell.style.fontSize = '20px';
          if (val >= 10000) cell.style.fontSize = '16px';
        }
        gridEl.appendChild(cell);
      }
    }
  }

  function updateScore() {
    const el = document.getElementById('g2048-score');
    if (el) el.textContent = String(score);
  }

  function startGame() {
    initState();
    const overlay = document.getElementById('g2048-overlay');
    if (overlay) overlay.style.display = 'none';
    draw();
    updateScore();
  }

  function onKeyDown(e) {
    switch (e.key) {
      case 'ArrowLeft': case 'a': case 'A': case 'ф': case 'Ф':
        move('left'); e.preventDefault(); break;
      case 'ArrowRight': case 'd': case 'D': case 'в': case 'В':
        move('right'); e.preventDefault(); break;
      case 'ArrowUp': case 'w': case 'W': case 'ц': case 'Ц':
        move('up'); e.preventDefault(); break;
      case 'ArrowDown': case 's': case 'S': case 'ы': case 'Ы':
        move('down'); e.preventDefault(); break;
    }
  }

  let touchStart = null;
  function onTouchStart(e) {
    if (e.touches.length === 1) {
      touchStart = { x: e.touches[0].clientX, y: e.touches[0].clientY };
    }
  }
  function onTouchEnd(e) {
    if (!touchStart) return;
    const t = e.changedTouches[0];
    const dx = t.clientX - touchStart.x;
    const dy = t.clientY - touchStart.y;
    const absDx = Math.abs(dx), absDy = Math.abs(dy);
    if (Math.max(absDx, absDy) < 30) return;
    if (absDx > absDy) {
      move(dx > 0 ? 'right' : 'left');
    } else {
      move(dy > 0 ? 'down' : 'up');
    }
    touchStart = null;
  }

  function render($el) {
    $el.innerHTML = `
      <div class="view">
        <div class="view-header">
          <div>
            <div class="view-title">🔢 2048</div>
            <div class="view-subtitle">Управление: стрелки или свайпы. Цель — собрать ${winTile}.</div>
          </div>
        </div>
        <div class="g2048-game">
          <div class="g2048-stats">
            <div class="g2048-stat"><span>Счёт:</span> <strong id="g2048-score">0</strong></div>
          </div>
          <div class="g2048-grid-wrap">
            <div class="g2048-grid" id="g2048-grid"></div>
            <div class="g2048-overlay" id="g2048-overlay">
              <div class="g2048-overlay-content">
                <div class="g2048-overlay-msg" id="g2048-overlay-msg">Нажмите «Старт», чтобы начать</div>
                <button class="g2048-start-btn" id="g2048-start-btn">▶ Старт</button>
              </div>
            </div>
          </div>
          <div class="g2048-controls">
            <button class="header-btn" id="g2048-restart-btn" style="width:auto;padding:8px 20px;">🔄 Заново</button>
            <button class="header-btn" id="g2048-leaderboard-btn" style="width:auto;padding:8px 20px;">🏆 Рекорды</button>
          </div>
        </div>
      </div>
    `;
    initState();
    draw();
    updateScore();

    document.getElementById('g2048-start-btn')?.addEventListener('click', startGame);
    document.getElementById('g2048-restart-btn')?.addEventListener('click', startGame);
    document.getElementById('g2048-leaderboard-btn')?.addEventListener('click', () => {
      if (window.Leaderboard) window.Leaderboard.open('game-2048', { title: '🔢 2048 — Таблица рекордов' });
    });

    document.addEventListener('keydown', onKeyDown);
    const gridEl = document.getElementById('g2048-grid');
    gridEl?.addEventListener('touchstart', onTouchStart, { passive: true });
    gridEl?.addEventListener('touchend', onTouchEnd, { passive: true });
  }

  return {
    async mount($el) { render($el); },
    unmount() {
      document.removeEventListener('keydown', onKeyDown);
    }
  };
})();
