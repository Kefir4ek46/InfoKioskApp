/* =================================================================
   InfoKiosk — front-end logic + WebView2 bridge
   ================================================================= */
(() => {
  "use strict";

  /* ---------- DOM refs ---------- */
  const $ = (sel) => document.querySelector(sel);
  const $$ = (sel) => Array.from(document.querySelectorAll(sel));

  const els = {
    app:          $("#app"),
    time:         $("#timeText"),
    date:         $("#dateText"),
    lessonStatus: $("#lessonStatus"),
    lessonInfo:   $("#lessonInfo"),
    weatherIcon:  $("#weatherIcon"),
    weatherTemp:  $("#weatherTemp"),
    weatherCity:  $("#weatherCity"),
    weatherExtra: $("#weatherExtra"),
    weatherTomorrow: $("#weatherTomorrow"),
    tickerBar:    $("#tickerBar"),
    tickerTrack:  $("#tickerTrack"),
    tickerItem:   $("#tickerItem"),
    leftMenu:     $("#leftMenuPanel"),
    customSections: $("#customSections"),
    content:      $("#contentArea"),
    customTitle:  $("#customTitle"),
    splash:       $("#splash"),
    idleOverlay:  $("#idleOverlay"),
    idleImage:    $("#idleImage"),
  };

  /* ---------- State ---------- */
  const state = {
    tickerItems: [],
    tickerIndex: 0,
    tickerSpeed: 90,            // px/sec
    tickerAnimId: null,
    tickerX: 0,
    tickerLastTs: 0,
    currentScreen: "home",
    idleVisible: false,
  };

  /* ---------- Bridge helpers ---------- */
  // WebView2 exposes window.chrome.webview.
  // When running in a plain browser (dev/preview), we mock it so the UI still works.
  const bridge = (() => {
    if (window.chrome && window.chrome.webview) {
      return {
        isHost: true,
        post: (msg) => window.chrome.webview.postMessage(msg),
        on:    (handler) => window.chrome.webview.addEventListener("message", (e) => handler(e.data)),
      };
    }
    // Dev/preview fallback — simulate a C# host
    console.warn("[InfoKiosk] WebView2 host not detected — running in preview mode.");
    return {
      isHost: false,
      post: (msg) => console.log("[mock → C#]", msg),
      on:    (handler) => window.addEventListener("mock-csharp", (e) => handler(e.detail)),
    };
  })();

  /* ---------- Bridge → JS handlers ---------- */
  const incoming = {
    clock(data) {
      if (data.time) { els.time.textContent = data.time; pulse(els.time, "is-tick"); }
      if (data.date) els.date.textContent = data.date;
    },
    weather(data) {
      els.weatherIcon.textContent = data.icon || "🌍";
      els.weatherTemp.textContent = data.temp || "--°C";
      els.weatherCity.textContent = data.city || "";
      els.weatherExtra.textContent = data.extra || "";
      els.weatherTomorrow.textContent = data.tomorrow || "";
    },
    lesson(data) {
      els.lessonStatus.textContent = data.status || "—";
      els.lessonInfo.textContent = data.info || "";
    },
    ticker(data) {
      state.tickerItems = Array.isArray(data.items) ? data.items : [];
      state.tickerSpeed = (data.speed && data.speed > 0 ? data.speed : 1.5) * 60;
      state.tickerIndex = 0;

      if (data.color) document.documentElement.style.setProperty("--ticker-color", data.color);
      if (data.size)  document.documentElement.style.setProperty("--ticker-size", data.size + "px");

      if (!data.enabled || state.tickerItems.length === 0) {
        els.tickerBar.classList.add("is-hidden");
        stopTicker();
        return;
      }
      els.tickerBar.classList.remove("is-hidden");
      els.tickerItem.textContent = state.tickerItems[0] || "";
      // wait a frame for layout, then position
      requestAnimationFrame(() => startTicker());
    },
    theme(data) {
      const root = document.documentElement;
      if (data.theme) root.setAttribute("data-theme", data.theme);
      if (data.bg)          root.style.setProperty("--bg", data.bg);
      if (data.buttonBg)    root.style.setProperty("--button-bg", data.buttonBg);
      if (data.buttonFg)    root.style.setProperty("--button-fg", data.buttonFg);
      if (data.text)        root.style.setProperty("--text", data.text);
      if (data.panel)       root.style.setProperty("--panel", data.panel);
      if (data.accent)      root.style.setProperty("--accent", data.accent);
      if (data.fontFamily)  root.style.setProperty("--font", data.fontFamily);
    },
    customSections(data) {
      els.customSections.innerHTML = "";
      (data.sections || []).forEach((s) => {
        const btn = document.createElement("button");
        btn.className = "menu-btn";
        btn.dataset.target = "custom";
        btn.dataset.folder = s.folderPath || "";
        btn.innerHTML = `
          <span class="menu-btn__icon">📂</span>
          <span class="menu-btn__label">${escapeHtml(s.name || "Раздел")}</span>
          <span class="menu-btn__shimmer"></span>`;
        btn.addEventListener("click", () => {
          els.customTitle.textContent = s.name || "Раздел";
          switchScreen("custom");
          setActiveButton(btn);
          notifyHost({ type: "nav", target: "custom", folder: s.folderPath, name: s.name });
        });
        els.customSections.appendChild(btn);
      });
    },
    idle(data) {
      if (data.visible) showIdle(data.imageUrl || "");
      else hideIdle();
    },
    screen(data) {
      // C# tells JS to switch screen
      if (data.target) {
        switchScreen(data.target);
        const btn = document.querySelector(`.menu-btn[data-target="${data.target}"]`);
        if (btn) setActiveButton(btn);
      }
    },
    content(data) {
      // C# can push raw HTML into the active screen
      const screen = document.querySelector(`.screen[data-screen="${data.target}"]`);
      if (!screen) return;
      if (data.html !== undefined) {
        screen.innerHTML = data.html;
      }
    },
    ready() {
      // host signals it finished booting — hide splash
      hideSplash();
    },
  };

  bridge.on((msg) => {
    if (!msg || !msg.type) return;
    const fn = incoming[msg.type];
    if (fn) fn(msg);
  });

  /* ---------- JS → C# notifications ---------- */
  function notifyHost(msg) {
    bridge.post(msg);
  }

  /* ---------- Screen switching ---------- */
  function switchScreen(target) {
    if (state.currentScreen === target) return;
    const current = document.querySelector(".screen.is-active");
    const next = document.querySelector(`.screen[data-screen="${target}"]`);
    if (!next) return;

    if (current) current.classList.remove("is-active");
    next.classList.add("is-active");
    state.currentScreen = target;
  }

  function setActiveButton(btn) {
    $$(".menu-btn").forEach((b) => b.classList.remove("is-active"));
    if (btn) btn.classList.add("is-active");
  }

  /* ---------- Menu button click handler ---------- */
  $$(".menu-btn").forEach((btn) => {
    btn.addEventListener("click", () => {
      const target = btn.dataset.target;
      if (!target) return;
      setActiveButton(btn);
      switchScreen(target);
      notifyHost({ type: "nav", target });
    });
  });

  /* ---------- Ticker animation ---------- */
  function startTicker() {
    if (state.tickerAnimId) cancelAnimationFrame(state.tickerAnimId);
    state.tickerX = els.tickerBar.offsetWidth + 20;
    state.tickerLastTs = performance.now();
    els.tickerTrack.style.transform = `translateX(${state.tickerX}px)`;
    state.tickerAnimId = requestAnimationFrame(tickerFrame);
  }
  function stopTicker() {
    if (state.tickerAnimId) cancelAnimationFrame(state.tickerAnimId);
    state.tickerAnimId = null;
  }
  function tickerFrame(ts) {
    const dt = (ts - state.tickerLastTs) / 1000;
    state.tickerLastTs = ts;
    if (dt > 0 && dt < 0.5) {
      state.tickerX -= state.tickerSpeed * dt;
      const itemWidth = els.tickerItem.offsetWidth + 60;
      if (state.tickerX < -itemWidth) {
        // advance to next item
        if (state.tickerItems.length > 0) {
          state.tickerIndex = (state.tickerIndex + 1) % state.tickerItems.length;
          els.tickerItem.textContent = state.tickerItems[state.tickerIndex];
        }
        state.tickerX = els.tickerBar.offsetWidth + 20;
      }
      els.tickerTrack.style.transform = `translateX(${state.tickerX}px)`;
    }
    state.tickerAnimId = requestAnimationFrame(tickerFrame);
  }
  window.addEventListener("resize", () => {
    if (els.tickerBar && !els.tickerBar.classList.contains("is-hidden")) {
      // restart ticker on resize so it stays in view
      startTicker();
    }
  });

  /* ---------- Idle overlay ---------- */
  function showIdle(imageUrl) {
    state.idleVisible = true;
    els.idleOverlay.classList.add("is-visible");
    if (imageUrl) {
      els.idleImage.classList.remove("is-loaded");
      els.idleImage.onload = () => els.idleImage.classList.add("is-loaded");
      els.idleImage.src = imageUrl;
    }
  }
  function hideIdle() {
    state.idleVisible = false;
    els.idleOverlay.classList.remove("is-visible");
    els.idleImage.classList.remove("is-loaded");
  }
  els.idleOverlay.addEventListener("click", () => {
    hideIdle();
    notifyHost({ type: "activity" });
  });

  /* ---------- Activity forwarding ---------- */
  ["mousemove", "mousedown", "keydown", "touchstart"].forEach((ev) =>
    document.addEventListener(ev, () => notifyHost({ type: "activity" }), { passive: true })
  );

  /* ---------- Small UX helpers ---------- */
  function pulse(el, cls) {
    el.classList.remove(cls);
    void el.offsetWidth;
    el.classList.add(cls);
    setTimeout(() => el.classList.remove(cls), 400);
  }

  function escapeHtml(s) {
    return String(s)
      .replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;").replace(/'/g, "&#039;");
  }

  function hideSplash() {
    els.splash.classList.add("is-hidden");
    setTimeout(() => els.splash.remove(), 900);
  }

  /* ---------- Boot ---------- */
  function boot() {
    // Tell the host we're ready to receive state
    notifyHost({ type: "ready" });

    // In preview mode (no C# host), simulate some data so the UI looks alive.
    if (!bridge.isHost) {
      simulatePreviewData();
    }

    // Auto-hide splash after 1.6s even if host doesn't respond
    setTimeout(hideSplash, 1600);
  }

  function simulatePreviewData() {
    const now = new Date();
    incoming.clock({ time: now.toTimeString().slice(0, 5), date: now.toLocaleDateString("ru-RU", { weekday: "long", day: "2-digit", month: "long", year: "numeric" }) });
    incoming.weather({ icon: "☀️", temp: "22°C", city: "Москва", extra: "ясно, ветер 3.2 м/с, влажность 45%, давление 1013 гПа", tomorrow: "" });
    incoming.lesson({ status: "Идёт 3-й урок", info: "Прошло: 15 мин, осталось: 30 мин" });
    incoming.ticker({ enabled: true, items: ["Добро пожаловать в информационный киоск!", "Сегодня школьная олимпиада по математике", "Не забудьте сдать тетради"], speed: 1.5, color: "#FFFFFF", size: 20 });
    incoming.customSections({ sections: [{ name: "Доп. образование", folderPath: "data/custom/extra" }] });

    // animated clock
    setInterval(() => {
      const n = new Date();
      incoming.clock({
        time: n.toTimeString().slice(0, 5),
        date: n.toLocaleDateString("ru-RU", { weekday: "long", day: "2-digit", month: "long", year: "numeric" })
      });
    }, 1000);
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", boot);
  } else {
    boot();
  }
})();
