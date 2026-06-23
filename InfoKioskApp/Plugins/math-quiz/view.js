/* plugins/math-quiz/view.js — математическая викторина.
 *
 * Появляются примеры, даётся время на решение, 4 варианта ответа.
 * Уровни сложности: лёгкий, средний, сложный.
 * Счёт = правильные ответы × сложность. Очки за быстроту.
 */
window.KioskViews = window.KioskViews || {};

window.KioskViews['math-quiz'] = (() => {
  'use strict';

  function getSettings() {
    const s = (window.KioskPlugins && window.KioskPlugins['math-quiz']
      ? window.KioskPlugins['math-quiz'].settings : {}) || {};
    return {
      difficulty: s.defaultDifficulty || 'easy',
      timePerQuestion: Number(s.timePerQuestion) || 10,
      totalQuestions: Number(s.totalQuestions) || 10
    };
  }

  let state = null;
  let timer = null;

  const difficultyConfig = {
    easy:   { maxNum: 20,  maxMul: 5,  allowDiv: false, pointsPerCorrect: 10, name: 'Лёгкий' },
    medium: { maxNum: 100, maxMul: 10, allowDiv: false, pointsPerCorrect: 20, name: 'Средний' },
    hard:   { maxNum: 1000, maxMul: 15, allowDiv: true,  pointsPerCorrect: 40, name: 'Сложный' }
  };

  function randInt(min, max) {
    return Math.floor(Math.random() * (max - min + 1)) + min;
  }

  // Генерирует один пример в зависимости от сложности.
  function generateQuestion(difficulty) {
    const cfg = difficultyConfig[difficulty] || difficultyConfig.easy;
    const ops = cfg.allowDiv ? ['+', '-', '×', '÷'] : ['+', '-', '×'];
    const op = ops[Math.floor(Math.random() * ops.length)];
    let a, b, answer, text;

    if (op === '+') {
      a = randInt(1, cfg.maxNum);
      b = randInt(1, cfg.maxNum);
      answer = a + b;
      text = `${a} + ${b}`;
    } else if (op === '-') {
      a = randInt(1, cfg.maxNum);
      b = randInt(1, a);  // чтобы результат был неотрицательный
      answer = a - b;
      text = `${a} − ${b}`;
    } else if (op === '×') {
      a = randInt(1, cfg.maxMul);
      b = randInt(1, cfg.maxMul);
      answer = a * b;
      text = `${a} × ${b}`;
    } else {  // ÷
      b = randInt(1, cfg.maxMul);
      answer = randInt(1, cfg.maxMul);
      a = b * answer;  // чтобы делилось нацело
      text = `${a} ÷ ${b}`;
    }

    // Генерируем 4 варианта ответа (включая правильный).
    const variants = new Set([answer]);
    while (variants.size < 4) {
      // Ложные ответы — близкие к правильному.
      const delta = randInt(1, Math.max(5, Math.floor(answer * 0.3) + 2));
      const sign = Math.random() < 0.5 ? -1 : 1;
      const wrong = answer + sign * delta;
      if (wrong >= 0) variants.add(wrong);
    }
    // Перемешиваем.
    const shuffled = Array.from(variants).sort(() => Math.random() - 0.5);

    return { text, answer, variants: shuffled, op };
  }

  function initState(difficulty) {
    const cfg = getSettings();
    return {
      difficulty: difficulty || cfg.difficulty,
      questionIndex: 0,
      totalQuestions: cfg.totalQuestions,
      score: 0,
      correctCount: 0,
      currentQuestion: null,
      timeLeft: cfg.timePerQuestion,
      timePerQuestion: cfg.timePerQuestion,
      finished: false,
      started: false
    };
  }

  function nextQuestion() {
    if (state.questionIndex >= state.totalQuestions) {
      finishGame();
      return;
    }
    state.currentQuestion = generateQuestion(state.difficulty);
    state.timeLeft = state.timePerQuestion;
    state.questionIndex++;
    renderQuestion();
    startTimer();
  }

  function startTimer() {
    if (timer) clearInterval(timer);
    timer = setInterval(() => {
      state.timeLeft--;
      updateTimer();
      if (state.timeLeft <= 0) {
        clearInterval(timer);
        // Время вышло — показываем правильный ответ, переходим к следующему.
        showCorrectAnswer(null);
        setTimeout(nextQuestion, 1500);
      }
    }, 1000);
  }

  function updateTimer() {
    const el = document.getElementById('mq-timer');
    if (el) el.textContent = `${state.timeLeft}с`;
    const bar = document.getElementById('mq-timer-bar');
    if (bar) {
      const pct = (state.timeLeft / state.timePerQuestion) * 100;
      bar.style.width = pct + '%';
      if (pct < 30) bar.style.background = '#ff6b6b';
      else if (pct < 60) bar.style.background = '#f0ad4e';
      else bar.style.background = '#4caf50';
    }
  }

  function answerClick(value) {
    if (!state.currentQuestion) return;
    clearInterval(timer);
    if (value === state.currentQuestion.answer) {
      // Правильный ответ.
      const cfg = difficultyConfig[state.difficulty];
      const timeBonus = Math.floor(state.timeLeft * 0.5);
      const gained = cfg.pointsPerCorrect + timeBonus;
      state.score += gained;
      state.correctCount++;
      showCorrectAnswer(value, true, gained);
    } else {
      showCorrectAnswer(value, false);
    }
    setTimeout(nextQuestion, 1500);
  }

  function showCorrectAnswer(selected, isCorrect, gained) {
    const cfg = difficultyConfig[state.difficulty];
    const buttons = document.querySelectorAll('.mq-answer-btn');
    buttons.forEach(btn => {
      const val = parseInt(btn.dataset.value, 10);
      if (val === state.currentQuestion.answer) {
        btn.classList.add('mq-correct');
      } else if (val === selected && !isCorrect) {
        btn.classList.add('mq-wrong');
      }
      btn.disabled = true;
    });
    // Показываем сообщение.
    const msgEl = document.getElementById('mq-feedback');
    if (msgEl) {
      if (isCorrect) {
        msgEl.textContent = `✅ Правильно! +${gained} очков`;
        msgEl.className = 'mq-feedback mq-feedback-correct';
      } else if (selected === null) {
        msgEl.textContent = `⏰ Время вышло! Правильный ответ: ${state.currentQuestion.answer}`;
        msgEl.className = 'mq-feedback mq-feedback-wrong';
      } else {
        msgEl.textContent = `❌ Неправильно! Правильный ответ: ${state.currentQuestion.answer}`;
        msgEl.className = 'mq-feedback mq-feedback-wrong';
      }
    }
    updateScore();
  }

  function updateScore() {
    const el = document.getElementById('mq-score');
    if (el) el.textContent = String(state.score);
    const correctEl = document.getElementById('mq-correct');
    if (correctEl) correctEl.textContent = `${state.correctCount}/${state.questionIndex}`;
  }

  function finishGame() {
    if (timer) clearInterval(timer);
    state.finished = true;
    const cfg = difficultyConfig[state.difficulty];
    const overlay = document.getElementById('mq-overlay');
    if (overlay) {
      overlay.style.display = '';
      const msg = document.getElementById('mq-overlay-msg');
      if (msg) {
        const pct = Math.round((state.correctCount / state.totalQuestions) * 100);
        msg.innerHTML = `Раунд окончен!<br>Счёт: <strong>${state.score}</strong><br>Правильных: ${state.correctCount} из ${state.totalQuestions} (${pct}%)`;
      }
    }
    // Открываем таблицу рекордов.
    if (state.score > 0 && window.Leaderboard) {
      setTimeout(() => {
        window.Leaderboard.open('math-quiz', {
          title: '➗ Математика — Таблица рекордов',
          score: state.score
        });
      }, 800);
    }
  }

  function startGame(difficulty) {
    state = initState(difficulty);
    state.started = true;
    const overlay = document.getElementById('mq-overlay');
    if (overlay) overlay.style.display = 'none';
    updateScore();
    nextQuestion();
  }

  function renderQuestion() {
    const q = state.currentQuestion;
    if (!q) return;
    const area = document.getElementById('mq-question-area');
    if (!area) return;
    const cfg = difficultyConfig[state.difficulty];
    area.innerHTML = `
      <div class="mq-question-header">
        <div class="mq-progress">Вопрос ${state.questionIndex} из ${state.totalQuestions}</div>
        <div class="mq-timer" id="mq-timer">${state.timeLeft}с</div>
      </div>
      <div class="mq-timer-bar-wrap"><div class="mq-timer-bar" id="mq-timer-bar" style="width:100%;"></div></div>
      <div class="mq-question">${q.text} = ?</div>
      <div class="mq-answers">
        ${q.variants.map(v => `<button class="mq-answer-btn" data-value="${v}" onclick="window.KioskViews['math-quiz']._answerClick(${v})">${v}</button>`).join('')}
      </div>
      <div class="mq-feedback" id="mq-feedback"></div>
    `;
    updateScore();
  }

  function render($el) {
    const cfg = getSettings();
    $el.innerHTML = `
      <div class="view">
        <div class="view-header">
          <div>
            <div class="view-title">➗ Математика</div>
            <div class="view-subtitle">Решайте примеры на время. Выберите правильный ответ.</div>
          </div>
        </div>
        <div class="mq-game">
          <div class="mq-stats">
            <div class="mq-stat"><span>Счёт:</span> <strong id="mq-score">0</strong></div>
            <div class="mq-stat"><span>Правильных:</span> <strong id="mq-correct">0/0</strong></div>
            <div class="mq-stat"><span>Сложность:</span> <strong id="mq-difficulty">${difficultyConfig[cfg.difficulty].name}</strong></div>
          </div>
          <div class="mq-question-area" id="mq-question-area"></div>
          <div class="mq-overlay" id="mq-overlay">
            <div class="mq-overlay-content">
              <div class="mq-overlay-msg" id="mq-overlay-msg">Выберите уровень сложности:</div>
              <div class="mq-difficulty-buttons">
                <button class="mq-diff-btn" data-diff="easy">🟢 Лёгкий</button>
                <button class="mq-diff-btn" data-diff="medium">🟡 Средний</button>
                <button class="mq-diff-btn" data-diff="hard">🔴 Сложный</button>
              </div>
            </div>
          </div>
          <div class="mq-controls">
            <button class="header-btn" id="mq-restart-btn" style="width:auto;padding:8px 20px;">🔄 Заново</button>
            <button class="header-btn" id="mq-leaderboard-btn" style="width:auto;padding:8px 20px;">🏆 Рекорды</button>
          </div>
        </div>
      </div>
    `;

    // Кнопки сложности.
    $el.querySelectorAll('.mq-diff-btn').forEach(btn => {
      btn.addEventListener('click', () => {
        const diff = btn.dataset.diff;
        const diffEl = document.getElementById('mq-difficulty');
        if (diffEl) diffEl.textContent = difficultyConfig[diff].name;
        startGame(diff);
      });
    });
    // Рестарт.
    document.getElementById('mq-restart-btn')?.addEventListener('click', () => {
      // Показываем оверлей выбора сложности.
      state = null;
      const overlay = document.getElementById('mq-overlay');
      if (overlay) {
        overlay.style.display = '';
        const msg = document.getElementById('mq-overlay-msg');
        if (msg) msg.textContent = 'Выберите уровень сложности:';
      }
      const area = document.getElementById('mq-question-area');
      if (area) area.innerHTML = '';
      const scoreEl = document.getElementById('mq-score');
      if (scoreEl) scoreEl.textContent = '0';
      const correctEl = document.getElementById('mq-correct');
      if (correctEl) correctEl.textContent = '0/0';
      if (timer) clearInterval(timer);
    });
    // Таблица рекордов.
    document.getElementById('mq-leaderboard-btn')?.addEventListener('click', () => {
      if (window.Leaderboard) window.Leaderboard.open('math-quiz', { title: '➗ Математика — Таблица рекордов' });
    });
  }

  return {
    async mount($el) { render($el); },
    unmount() {
      if (timer) clearInterval(timer);
    },
    // Публичный метод для onclick из HTML.
    _answerClick: answerClick
  };
})();
