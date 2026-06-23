/* weather.js — погода в шапке + подробная панель.
 *
 * Источник данных: C#-bridge → WeatherService.GetWeatherAsync (OpenWeatherMap).
 * В шапке показываем иконку + температуру + краткое описание.
 * По клику на виджет открывается большая панель с городом, ощущается-как,
 * влажностью, давлением (мм рт. ст.), ветром (скорость + направление),
 * облачностью, видимостью, восходом/закатом.
 */
window.Weather = (() => {
  let timer = null;
  let lastData = null;
  const REFRESH_MS = 10 * 60 * 1000; // 10 минут

  // Локальное время из Unix epoch (учитывая смещение клиента).
  function fmtTime(unixSec) {
    if (!unixSec) return '—';
    try {
      const d = new Date(unixSec * 1000);
      return d.toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' });
    } catch { return '—'; }
  }

  // Экранирование — простое, для чисел/строк из API.
  function esc(s) {
    if (s == null) return '';
    return String(s).replace(/[&<>"']/g, c => ({
      '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'
    }[c]));
  }

  async function refresh() {
    try {
      const cfg = window.KioskApp ? window.KioskApp.getConfig() : null;
      const city = (cfg && (cfg.City || cfg.city)) || 'Москва';
      console.log('[weather] refreshing for city:', city);
      const w = await window.kiosk.call('weather.get', { city });
      if (!w) {
        console.warn('[weather] no data returned');
        return;
      }
      if (w.error) {
        console.warn('[weather] error:', w.error);
        return;
      }
      console.log('[weather] got data:', w.City, w.Temperature, w.Description);
      lastData = w;

      // ----- Шапка -----
      const root = document.getElementById('weather');
      if (!root) return;
      root.hidden = false;
      const icon = document.getElementById('weather-icon');
      if (icon) {
        if (w.IconUrl) icon.src = w.IconUrl;
        else if (w.IconCode) icon.src = `https://openweathermap.org/img/wn/${w.IconCode}@2x.png`;
      }
      const tempC = (typeof w.Temperature === 'number')
        ? Math.round(w.Temperature)
        : '—';
      const tempEl = document.getElementById('weather-temp');
      if (tempEl) tempEl.textContent = `${tempC}°`;
      const descEl = document.getElementById('weather-desc');
      if (descEl) descEl.textContent = w.Description || '';

      // ----- Подробная панель -----
      renderPanel(w);
    } catch (e) {
      console.warn('[weather] failed', e);
    }
  }

  function renderPanel(w) {
    const panel = document.getElementById('weather-panel');
    if (!panel || panel.hidden) return;

    const icon = document.getElementById('wp-icon');
    if (icon) {
      if (w.IconUrl) icon.src = w.IconUrl;
      else if (w.IconCode) icon.src = `https://openweathermap.org/img/wn/${w.IconCode}@4x.png`;
    }
    document.getElementById('wp-city').textContent = w.City || '—';
    const tempC = (typeof w.Temperature === 'number') ? Math.round(w.Temperature) : '—';
    document.getElementById('wp-temp').textContent = `${tempC}°C`;
    document.getElementById('wp-desc').textContent = w.Description || '';

    const feelsLike = (typeof w.FeelsLike === 'number') ? Math.round(w.FeelsLike) + '°C' : '—';
    const windSpeed = (typeof w.WindSpeed === 'number') ? w.WindSpeed.toFixed(1) + ' м/с' : '—';
    const windGust = (w.WindGust != null) ? w.WindGust.toFixed(1) + ' м/с' : null;
    const visibility = (w.Visibility != null)
      ? (w.Visibility >= 1000 ? (w.Visibility / 1000).toFixed(1) + ' км' : w.Visibility + ' м')
      : null;
    const cloudiness = (w.Cloudiness != null) ? w.Cloudiness + ' %' : '—';

    const rows = [
      { icon: '🌡️', label: 'Ощущается', value: feelsLike },
      { icon: '💧', label: 'Влажность', value: (w.Humidity != null ? w.Humidity + ' %' : '—') },
      { icon: '🧭', label: 'Давление', value: (w.PressureMmHg ? w.PressureMmHg + ' мм рт. ст.' : '—') },
      { icon: '🌬️', label: 'Ветер', value: `${windSpeed} ${esc(w.WindDirection || '')}`.trim() },
      { icon: '☁️', label: 'Облачность', value: cloudiness },
      { icon: '👁️', label: 'Видимость', value: visibility || '—' },
      { icon: '🌅', label: 'Восход', value: fmtTime(w.SunriseUnix) },
      { icon: '🌇', label: 'Закат', value: fmtTime(w.SunsetUnix) },
    ].filter(r => r.value && r.value !== '—');

    if (windGust) {
      rows.splice(3, 1, { icon: '🌬️', label: 'Ветер', value: `${windSpeed} ${esc(w.WindDirection || '')}`.trim() });
      rows.splice(4, 0, { icon: '💨', label: 'Порывы', value: windGust });
    }

    const grid = document.getElementById('wp-grid');
    if (grid) {
      grid.innerHTML = rows.map(r => `
        <div class="wp-cell">
          <div class="wp-cell-icon">${r.icon}</div>
          <div class="wp-cell-label">${esc(r.label)}</div>
          <div class="wp-cell-value">${esc(r.value)}</div>
        </div>
      `).join('');
    }
  }

  function openPanel() {
    const panel = document.getElementById('weather-panel');
    if (!panel) return;
    panel.hidden = false;
    if (lastData) renderPanel(lastData);
  }

  function closePanel() {
    const panel = document.getElementById('weather-panel');
    if (panel) panel.hidden = true;
  }

  function bind() {
    const btn = document.getElementById('weather');
    if (btn) btn.addEventListener('click', openPanel);

    const close = document.getElementById('weather-panel-close');
    if (close) close.addEventListener('click', closePanel);

    const panel = document.getElementById('weather-panel');
    if (panel) {
      panel.addEventListener('click', (e) => {
        if (e.target === panel) closePanel();
      });
    }

    document.addEventListener('keydown', (e) => {
      const p = document.getElementById('weather-panel');
      if (p && !p.hidden && e.key === 'Escape') closePanel();
    });
  }

  return {
    start() {
      try { bind(); } catch (e) { console.warn('[weather] bind failed', e); }
      refresh();
      timer = setInterval(refresh, REFRESH_MS);
    },
    refresh,
    stop() { if (timer) clearInterval(timer); }
  };
})();
