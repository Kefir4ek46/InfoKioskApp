/* clock.js — часы + дата + активный урок + обратный отсчёт до начала/конца урока.
 *
 * В шапке:
 *   #clock-time, #clock-date — текущее время и дата
 *   #lesson-block (виден, если есть хоть какой-то урок сегодня):
 *     #lesson-badge    — номер + интервал текущего/следующего урока
 *     #lesson-countdown — «до начала 5 мин 12 сек» / «до конца 3 мин 45 сек» / «перемена — 4 мин»
 * На заставке:
 *   #idle-clock-time, #idle-clock-date — то же время и дата
 *   #idle-lesson — короткий текст текущего статуса (для красоты)
 */
window.Clock = (() => {
  let bell = null;       // { lessons: [{ num, start, end }] }
  let timer = null;
  let lastStatusKey = null;  // для отслеживания смены статуса урока

  function fmtTime(d) {
    return String(d.getHours()).padStart(2,'0') + ':' + String(d.getMinutes()).padStart(2,'0');
  }
  function fmtDate(d) {
    const days   = ['Воскресенье','Понедельник','Вторник','Среда','Четверг','Пятница','Суббота'];
    const months = ['января','февраля','марта','апреля','мая','июня','июля','августа','сентября','октября','ноября','декабря'];
    return `${days[d.getDay()]}, ${d.getDate()} ${months[d.getMonth()]}`;
  }

  // "HH:MM" → секунды от полуночи
  function toSec(hhmm) {
    if (!hhmm || !hhmm.includes(':')) return null;
    const [h, m] = hhmm.split(':').map(n => parseInt(n, 10));
    if (isNaN(h) || isNaN(m)) return null;
    return h * 3600 + m * 60;
  }

  async function loadBell() {
    try {
      const data = await window.kiosk.call('bell.get');
      console.log('[clock] bell data:', data);
      // Новый формат: { active, variants: { id: { name, lessons } } }
      if (data && data.variants) {
        const activeId = data.active || 'default';
        const variant = data.variants[activeId] || Object.values(data.variants)[0];
        if (variant && variant.lessons) {
          bell = { lessons: variant.lessons };
          console.log('[clock] active variant:', activeId, 'lessons:', bell.lessons.length);
        } else {
          bell = null;
        }
      } else if (data && data.lessons) {
        // Старый формат.
        bell = data;
        console.log('[clock] legacy format, lessons:', bell.lessons.length);
      } else {
        bell = null;
      }
    } catch (e) {
      console.warn('[clock] bell.get failed', e);
      bell = null;
    }
  }

  /**
   * Найти статус урока на данный момент.
   * Возвращает:
   *   { kind: 'lesson',     num, start, end, remainingSec, lessonEndsAt }
   *   { kind: 'break',      num, start, end, remainingSec, nextStartsAt, prevEndedAt }
   *   { kind: 'upcoming',   num, start, end, remainingSec, startsAt }
   *   { kind: 'after',      ... } — уроки закончились
   *   null — bell пустой
   */
  function findLessonStatus(now) {
    if (!bell || !bell.lessons || !bell.lessons.length) return null;
    const nowSec = now.getHours() * 3600 + now.getMinutes() * 60 + now.getSeconds();

    // Идёт ли сейчас урок?
    for (const l of bell.lessons) {
      const s = toSec(l.start), e = toSec(l.end);
      if (s == null || e == null) continue;
      if (nowSec >= s && nowSec <= e) {
        return {
          kind: 'lesson',
          num: l.num, start: l.start, end: l.end,
          remainingSec: e - nowSec,
          elapsedSec: nowSec - s,
          totalSec: e - s,
          lessonEndsAt: l.end
        };
      }
    }

    // Перемена: между концом предыдущего и началом следующего
    for (let i = 0; i < bell.lessons.length; i++) {
      const l = bell.lessons[i];
      const s = toSec(l.start);
      if (s == null) continue;
      if (nowSec < s) {
        const prevEnd = i > 0 ? toSec(bell.lessons[i - 1].end) : null;
        const inBreak = prevEnd != null && nowSec > prevEnd;
        const result = {
          kind: inBreak ? 'break' : 'upcoming',
          num: l.num, start: l.start, end: l.end,
          remainingSec: s - nowSec,
          startsAt: l.start
        };
        if (inBreak) {
          result.prevEndedAt = bell.lessons[i - 1].end;
          result.prevNum = bell.lessons[i - 1].num;
          result.elapsedSec = nowSec - prevEnd;
          result.totalSec = s - prevEnd;
        }
        return result;
      }
    }

    // Все уроки закончились
    const last = bell.lessons[bell.lessons.length - 1];
    return {
      kind: 'after',
      num: last.num, start: last.start, end: last.end,
      remainingSec: 0
    };
  }

  function fmtCountdown(sec) {
    if (sec < 0) sec = 0;
    const m = Math.floor(sec / 60);
    const s = Math.floor(sec % 60);
    if (m <= 0) return `${s} сек`;
    if (m < 60) return `${m} мин ${String(s).padStart(2,'0')} сек`;
    const h = Math.floor(m / 60);
    const mm = m % 60;
    return `${h} ч ${mm} мин`;
  }

  function tick() {
    const now = new Date();
    const tEl = document.getElementById('clock-time');
    const dEl = document.getElementById('clock-date');
    if (tEl) tEl.textContent = fmtTime(now);
    if (dEl) dEl.textContent = fmtDate(now);

    const idleTime = document.getElementById('idle-clock-time');
    const idleDate = document.getElementById('idle-clock-date');
    if (idleTime) idleTime.textContent = fmtTime(now);
    if (idleDate) idleDate.textContent = fmtDate(now);

    // === Бейдж урока + прошло/осталось ===
    const block = document.getElementById('lesson-block');
    const st = findLessonStatus(now);
    // Отслеживаем смену статуса урока. Когда таймер "до конца:" / "до начала:"
    // обнуляется — kind или num меняются — отправляем событие, чтобы расписание
    // могло обновить подсветку немедленно, не дожидаясь ежеминутного таймера.
    if (st) {
      const key = `${st.kind}:${st.num || 0}:${st.start || ''}`;
      if (key !== lastStatusKey) {
        lastStatusKey = key;
        try {
          window.dispatchEvent(new CustomEvent('kiosk-lesson-status', { detail: st }));
        } catch (e) { /* старые браузеры без CustomEvent */ }
        // Прямой вызов, если расписание уже смонтировано.
        if (window.KioskViews && window.KioskViews.schedule &&
            typeof window.KioskViews.schedule.refreshHighlight === 'function') {
          try { window.KioskViews.schedule.refreshHighlight(); } catch (e) {}
        }
      }
    } else {
      lastStatusKey = null;
    }
    if (block) {
      if (!st) {
        block.hidden = true;
      } else {
        block.hidden = false;
        const labelEl = document.getElementById('lesson-label');
        const numEl   = document.getElementById('lesson-num');
        const timeEl  = document.getElementById('lesson-time');
        const cdEl    = document.getElementById('lesson-countdown');
        const badge   = document.getElementById('lesson-badge');

        if (st.kind === 'lesson') {
          if (labelEl) labelEl.textContent = 'Идёт урок';
          if (numEl)   numEl.textContent = `№${st.num}`;
          if (timeEl)  timeEl.textContent = `${st.start}–${st.end}`;
          if (cdEl)    cdEl.textContent = `Прошло: ${fmtCountdown(st.elapsedSec)} • До конца: ${fmtCountdown(st.remainingSec)}`;
          if (badge)   { badge.classList.remove('break', 'upcoming'); badge.classList.add('lesson'); }
        } else if (st.kind === 'break') {
          if (labelEl) labelEl.textContent = `Перемена после ${st.prevNum}`;
          if (numEl)   numEl.textContent = `→ ${st.num}`;
          if (timeEl)  timeEl.textContent = `${st.start}–${st.end}`;
          if (cdEl)    cdEl.textContent = `Прошло: ${fmtCountdown(st.elapsedSec)} • До начала: ${fmtCountdown(st.remainingSec)}`;
          if (badge)   { badge.classList.remove('lesson', 'upcoming'); badge.classList.add('break'); }
        } else if (st.kind === 'upcoming') {
          if (labelEl) labelEl.textContent = 'Скоро';
          if (numEl)   numEl.textContent = `№${st.num}`;
          if (timeEl)  timeEl.textContent = `${st.start}–${st.end}`;
          if (cdEl)    cdEl.textContent = `До начала: ${fmtCountdown(st.remainingSec)}`;
          if (badge)   { badge.classList.remove('lesson', 'break'); badge.classList.add('upcoming'); }
        } else { // after
          if (labelEl) labelEl.textContent = 'Уроки закончились';
          if (numEl)   numEl.textContent = '—';
          if (timeEl)  timeEl.textContent = `${st.start}–${st.end}`;
          if (cdEl)    cdEl.textContent = 'Завтра с утра';
          if (badge)   { badge.classList.remove('lesson', 'break', 'upcoming'); }
        }
      }
    }

    // === Короткий текст на заставку ===
    const idleLesson = document.getElementById('idle-lesson');
    if (idleLesson) {
      if (!st) {
        idleLesson.hidden = true;
        idleLesson.textContent = '';
      } else {
        idleLesson.hidden = false;
        if (st.kind === 'lesson') {
          idleLesson.textContent = `Урок №${st.num} • до конца ${fmtCountdown(st.remainingSec)}`;
        } else if (st.kind === 'break') {
          idleLesson.textContent = `Перемена • до урока №${st.num} ${fmtCountdown(st.remainingSec)}`;
        } else if (st.kind === 'upcoming') {
          idleLesson.textContent = `Урок №${st.num} начнётся через ${fmtCountdown(st.remainingSec)}`;
        } else {
          idleLesson.textContent = 'Уроки закончились';
        }
      }
    }
  }

  return {
    start() {
      loadBell();
      tick();
      timer = setInterval(tick, 1000);
    },
    reloadBell: loadBell,
    stop() { if (timer) clearInterval(timer); }
  };
})();
