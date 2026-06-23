// script.js — InfoKiosk Remote Admin
// Полный клиент для media/posts + модалка с листанием, drag&drop, CRUD постов.

// базовый адрес API (использует тот же origin, откуда загружена страница)
const api = location.origin;
let adminToken = sessionStorage.getItem("adminToken") || "";

// ====================================================================
// ТОКЕН ИЗ URL — киоск передаёт токен админки через ?token=XXX при открытии
// отдельного окна. Если токен есть — сохраняем и пропускаем prompt().
// ====================================================================
(function applyTokenFromUrl() {
  try {
    const params = new URLSearchParams(window.location.search);
    const t = params.get("token");
    if (t) {
      adminToken = t;
      sessionStorage.setItem("adminToken", t);
      // Чистим URL (убираем ?token= из адресной строки и истории).
      try { history.replaceState({}, "", window.location.pathname); } catch {}
      console.log("[admin] token applied from URL");
    }
  } catch (e) {
    console.warn("[admin] applyTokenFromUrl failed:", e);
  }
})();

// ---- Инициализация ----
window.addEventListener("load", async () => {
  try {
    document.getElementById("server-status").textContent = "✔ " + api;

    await requireAdminAuth();
    setupTabs();
    setupDragAndDrop();
    setupModalControls();
    setupNewsModalControls();
    setupThemePresets();

    await loadPostCategorySelector();
    await loadCategoriesAndBuildUI();

    await loadOtherSchedules();
    await loadFilesCategory();
    await loadCalendar();
    await loadSettings();
    await loadConfigSettings();
    await loadThemeAndExtraSettings();
    await loadEditors();
    await loadPendingNews();
    await loadPublishedNewsAdmin();
    await loadHonorBoard();
  } catch (err) {
    console.error("Init error:", err);
  }
});

async function requireAdminAuth() {
  if (adminToken) {
    const check = await fetch(`${api}/news/admin/pending`, { headers: { "X-Admin-Token": adminToken } });
    if (check.ok) return;
    if (check.status === 401) {
      adminToken = "";
      sessionStorage.removeItem("adminToken");
    }
  }

  while (!adminToken) {
    const password = prompt("Введите пароль администратора:");
    if (password === null) {
      renderAuthBlocked();
      throw new Error("Admin auth cancelled");
    }

    if (!password.trim()) {
      alert("Введите пароль");
      continue;
    }

    const res = await fetch(`${api}/auth/admin/login`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ password: password.trim() })
    });

    if (res.ok) {
      const data = await res.json();
      adminToken = data.token;
      sessionStorage.setItem("adminToken", adminToken);
      return;
    }

    alert("Неверный пароль. Попробуйте снова.");
  }
}

function renderAuthBlocked() {
  document.body.innerHTML = `<div style="padding:30px;color:white;max-width:560px">
      <div style="font-size:28px;font-weight:700;margin-bottom:8px">Требуется авторизация</div>
      <div style="opacity:.85;margin-bottom:16px">Вход был отменен. Нажмите кнопку ниже, чтобы повторить авторизацию.</div>
      <button id="retry-auth-btn" style="padding:10px 14px;font-size:16px;cursor:pointer">Повторить вход</button>
    </div>`;
  const btn = document.getElementById("retry-auth-btn");
  if (btn) btn.addEventListener("click", () => location.reload());
}

async function loadEditors() {
  try {
    const res = await fetch(`${api}/news/admin/editors`, { headers: { "X-Admin-Token": adminToken } });
    if (!res.ok) return;
    const editors = await res.json();
    const ul = document.getElementById("editors-list");
    if (!ul) return;
    ul.innerHTML = "";
    editors.forEach(ed => {
      const name = ed.name || ed.Name || "";
      const login = ed.login || ed.Login || "";
      const password = ed.password || ed.Password || "";
      const canPublish = ed.canPublishWithoutApproval || ed.CanPublishWithoutApproval || false;
      const displayName = ed.displayName || ed.DisplayName || "";
      const active = ed.active !== false && ed.Active !== false;
      const crownIcon = canPublish ? '👑 ' : '';
      const activeBadge = active ? '' : ' <span style="color:var(--muted);font-size:12px;">(заблокирован)</span>';
      const displayLine = displayName ? `<div style='color:var(--muted);font-size:12px;'>Отображается как: ${escapeHtml(displayName)}</div>` : '';
      const li = document.createElement("li");
      li.innerHTML = `<div>
        <strong>${crownIcon}${escapeHtml(name)}</strong>${activeBadge}
        <div style='color:var(--muted)'>${escapeHtml(login)} • Пароль: ${escapeHtml(password)}</div>
        ${displayLine}
        <div style='margin-top:6px;display:flex;gap:6px;flex-wrap:wrap;'>
          <button class="small-btn ${canPublish ? 'active' : ''}" onclick="togglePublishRights('${escapeHtml(login)}', ${canPublish})" title="Право публикации без модерации">
            ${canPublish ? '👑 Снять права' : '👑 Дать права'}
          </button>
          <button class="small-btn danger" onclick="deleteEditor('${escapeHtml(login)}')">🗑 Удалить</button>
        </div>
      </div>`;
      ul.appendChild(li);
    });
    const linkInput = document.getElementById("editor-link");
    if (linkInput) linkInput.value = `${location.origin}/editor`;
  } catch (err) {
    console.error("loadEditors", err);
  }
}

async function createEditor() {
  const name = (document.getElementById("editor-name").value || "").trim();
  const login = (document.getElementById("editor-login").value || "").trim();
  const password = (document.getElementById("editor-password").value || "").trim();
  const canPublish = !!document.getElementById("editor-can-publish")?.checked;
  if (!name || !login || !password) return alert("Введите имя, логин и пароль");

  const res = await fetch(`${api}/news/admin/editors`, {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-Admin-Token": adminToken },
    body: JSON.stringify({ action: "add", name, login, password, canPublishWithoutApproval: canPublish })
  });

  if (!res.ok) return alert("Ошибка добавления редактора");
  document.getElementById("editor-name").value = "";
  document.getElementById("editor-login").value = "";
  document.getElementById("editor-password").value = "";
  if (document.getElementById("editor-can-publish")) document.getElementById("editor-can-publish").checked = false;
  await loadEditors();
}

async function togglePublishRights(login, currentlyHas) {
  const action = currentlyHas ? "снять" : "выдать";
  if (!confirm(`${action} права публикации без модерации у редактора ${login}?`)) return;
  const res = await fetch(`${api}/news/admin/editors`, {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-Admin-Token": adminToken },
    body: JSON.stringify({ action: "toggle-publish-rights", login, canPublishWithoutApproval: !currentlyHas })
  });
  if (!res.ok) return alert("Ошибка изменения прав");
  await loadEditors();
}

async function deleteEditor(login) {
  if (!confirm(`Удалить редактора ${login}?`)) return;
  const res = await fetch(`${api}/news/admin/editors`, {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-Admin-Token": adminToken },
    body: JSON.stringify({ action: "delete", login })
  });
  if (!res.ok) return alert("Ошибка удаления");
  await loadEditors();
}

function copyEditorLink() {
  const input = document.getElementById("editor-link");
  if (!input) return;
  input.select();
  document.execCommand("copy");
  alert("Ссылка скопирована");
}

function resetHonorForm() {
  document.getElementById("honor-id").value = "";
  document.getElementById("honor-name").value = "";
  document.getElementById("honor-description").value = "";
  document.getElementById("honor-photo").value = "";
}

async function saveHonorPerson() {
  const id = (document.getElementById("honor-id").value || "").trim();
  const fullName = (document.getElementById("honor-name").value || "").trim();
  const description = (document.getElementById("honor-description").value || "").trim();
  const photoInput = document.getElementById("honor-photo");

  if (!fullName) return alert("Введите ФИО");

  let photoFile = "";
  const photo = photoInput.files?.[0];
  if (photo) {
    const utf8Name = unescape(encodeURIComponent(photo.name));
    const nameB64 = btoa(utf8Name);
    const uploadRes = await fetch(`${api}/upload?target=honor`, {
      method: "POST",
      headers: { "X-Filename-Base64": nameB64 },
      body: photo
    });
    if (!uploadRes.ok) return alert("Не удалось загрузить фото");
    photoFile = photo.name;
  }

  const res = await fetch(`${api}/honor/save`, {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-Admin-Token": adminToken },
    body: JSON.stringify({ id, fullName, description, photoFile })
  });

  if (!res.ok) return alert("Ошибка сохранения карточки");
  resetHonorForm();
  await loadHonorBoard();
}

async function deleteHonorPerson(id) {
  if (!confirm("Удалить карточку?")) return;
  const res = await fetch(`${api}/honor/delete`, {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-Admin-Token": adminToken },
    body: JSON.stringify({ id })
  });
  if (!res.ok) return alert("Ошибка удаления");
  await loadHonorBoard();
}

async function loadHonorBoard() {
  const res = await fetch(`${api}/honor/list`);
  if (!res.ok) return;
  const items = await res.json();
  const host = document.getElementById("honor-admin-list");
  if (!host) return;
  host.innerHTML = "";

  if (!items.length) {
    host.innerHTML = "<div class='card'>Пока нет карточек</div>";
    return;
  }

  items.forEach(item => {
    const id = item.id || item.Id;
    const fullName = item.fullName || item.FullName || "";
    const description = item.description || item.Description || "";
    const photo = item.photoFile || item.PhotoFile || "";
    const card = document.createElement("div");
    card.className = "post-card";
    const imgHtml = photo ? `<img src='${api}/download?target=honor&name=${encodeURIComponent(photo)}' style='width:100%;height:160px;object-fit:cover'>` : "<div style='height:160px;display:flex;align-items:center;justify-content:center;background:#0f131c;color:#777'>Нет фото</div>";
    card.innerHTML = `<div class='cover'>${imgHtml}</div><div class='meta'><div class='title'>${escapeHtml(fullName)}</div><div style='color:var(--muted)'>${escapeHtml(description.slice(0,100))}</div><div class='actions'><button onclick='editHonorPerson(${JSON.stringify(id)},${JSON.stringify(fullName)},${JSON.stringify(description)})'>✏️</button><button class='danger' onclick='deleteHonorPerson(${JSON.stringify(id)})'>🗑</button></div></div>`;
    host.appendChild(card);
  });
}

function editHonorPerson(id, fullName, description) {
  document.getElementById("honor-id").value = id || "";
  document.getElementById("honor-name").value = fullName || "";
  document.getElementById("honor-description").value = description || "";
  document.getElementById("tab-honor")?.scrollIntoView({ behavior: "smooth", block: "start" });
}

const newsState = { items: [], source: "pending", index: 0, mediaIndex: 0, rotation: 0 };

function normalizeNews(n, source) {
  return {
    id: n.id || n.Id,
    title: n.title || n.Title || "Без названия",
    author: n.authorName || n.AuthorName || n.authorLogin || n.AuthorLogin || "редактор",
    content: n.content || n.Content || "",
    createdAt: n.createdAt || n.CreatedAt || "",
    videoUrl: n.videoUrl || n.VideoUrl || "",
    linkUrl: n.linkUrl || n.LinkUrl || "",
    videoFile: n.videoFile || n.VideoFile || "",
    photos: n.photoFiles || n.PhotoFiles || [],
    publishedByName: n.publishedByName || n.PublishedByName || "",
    publishedByLogin: n.publishedByLogin || n.PublishedByLogin || "",
    autoPublished: n.autoPublished || n.AutoPublished || false,
    source
  };
}

function buildNewsPreviewCard(item) {
  const mediaBase = `${api}/download?target=newsmedia&name=`;
  const firstPhoto = item.photos && item.photos.length ? item.photos[0] : "";
  const thumb = firstPhoto
    ? `<img src="${mediaBase}${encodeURIComponent(firstPhoto)}" style="width:120px;height:80px;object-fit:cover;border-radius:8px;">`
    : (item.videoFile ? `<video src="${mediaBase}${encodeURIComponent(item.videoFile)}" style="width:120px;height:80px;object-fit:contain;background:#000;border-radius:8px;"></video>` : "<div style='width:120px;height:80px;background:#111;border-radius:8px;display:flex;align-items:center;justify-content:center;color:#777;'>нет медиа</div>");

  // Бейдж PublishedBy: "опубликовал: X" если есть.
  const pubByHtml = item.publishedByName
    ? `<div style='color:#f0ad4e;font-size:11px;margin-top:2px;'>📤 Опубликовал: ${escapeHtml(item.publishedByName)}${item.autoPublished ? ' (авто)' : ''}</div>`
    : "";

  const el = document.createElement("div");
  el.className = "card";
  el.style.marginBottom = "10px";
  el.style.cursor = "pointer";
  el.innerHTML = `<div style='display:flex;gap:12px;align-items:flex-start;'>${thumb}<div style='flex:1;'><strong>${escapeHtml(item.title)}</strong><div style='color:var(--muted);font-size:12px;margin:4px 0;'>Автор: ${escapeHtml(item.author)} · ${escapeHtml(String(item.createdAt))}</div>${pubByHtml}<div style='white-space:pre-wrap;margin-top:4px;'>${escapeHtml(item.content.slice(0,180))}</div></div></div>`;
  return el;
}

function openNewsModal(item) {
  newsState.rotation = 0;
  newsState.mediaIndex = 0;
  const all = [];
  if (item.videoFile) all.push({ type: "video", file: item.videoFile });
  (item.photos || []).forEach(f => all.push({ type: "photo", file: f }));
  if (!all.length && item.videoUrl) all.push({ type: "link", url: item.videoUrl });
  newsState.items = all;
  newsState.index = 0;
  newsState.current = item;

  document.getElementById("news-modal-title").value = item.title;
  // В метаданных модалки показываем автора и (опционально) кто опубликовал.
  let metaText = `Автор: ${item.author} • ${item.createdAt || ""}`;
  if (item.publishedByName) {
    metaText += ` • Опубликовал: ${item.publishedByName}${item.autoPublished ? " (авто)" : ""}`;
  }
  document.getElementById("news-modal-meta").textContent = metaText;
  document.getElementById("news-modal-content").value = item.content || "";
  const modalLink = document.getElementById("news-modal-link");
  if (modalLink) modalLink.value = item.linkUrl || "";
  document.getElementById("news-modal").classList.remove("hidden");

  document.getElementById("news-publish-btn").style.display = item.source === "pending" ? "inline-block" : "none";
  document.getElementById("news-reject-btn").style.display = item.source === "pending" ? "inline-block" : "none";

  renderNewsMedia();
}

function renderNewsMedia() {
  const host = document.getElementById("news-media-host");
  const idx = document.getElementById("news-index");
  host.innerHTML = "";

  if (!newsState.items.length) {
    host.innerHTML = "<div style='color:#777'>Нет медиа</div>";
    idx.textContent = "0/0";
    return;
  }

  if (newsState.mediaIndex < 0) newsState.mediaIndex = newsState.items.length - 1;
  if (newsState.mediaIndex >= newsState.items.length) newsState.mediaIndex = 0;

  const m = newsState.items[newsState.mediaIndex];
  const mediaBase = `${api}/download?target=newsmedia&name=`;

  if (m.type === "photo") {
    const img = document.createElement("img");
    img.src = `${mediaBase}${encodeURIComponent(m.file)}`;
    img.style.maxWidth = "100%";
    img.style.maxHeight = "500px";
    img.style.objectFit = "contain";
    img.style.transform = `rotate(${newsState.rotation}deg)`;
    host.appendChild(img);
  } else if (m.type === "video") {
    const v = document.createElement("video");
    v.src = `${mediaBase}${encodeURIComponent(m.file)}`;
    v.controls = true;
    v.style.maxWidth = "100%";
    v.style.maxHeight = "500px";
    v.style.objectFit = "contain";
    v.style.transform = `rotate(${newsState.rotation}deg)`;
    host.appendChild(v);
  } else {
    const a = document.createElement("a");
    a.href = m.url;
    a.target = "_blank";
    a.textContent = "Открыть видео по ссылке";
    host.appendChild(a);
  }

  idx.textContent = `${newsState.mediaIndex + 1}/${newsState.items.length}`;
}

async function saveNewsText() {
  const item = newsState.current;
  if (!item) return;
  const title = (document.getElementById("news-modal-title").value || "").trim();
  const content = (document.getElementById("news-modal-content").value || "").trim();
  const linkUrl = (document.getElementById("news-modal-link")?.value || "").trim();
  const res = await fetch(`${api}/news/admin/update`, {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-Admin-Token": adminToken },
    body: JSON.stringify({ id: item.id, title, content, linkUrl })
  });
  if (!res.ok) return alert("Ошибка сохранения новости");
  alert("Текст сохранён");
  await loadPendingNews();
  await loadPublishedNewsAdmin();
}

async function deleteNews(id) {
  if (!confirm("Удалить новость?")) return;
  const res = await fetch(`${api}/news/admin/delete`, {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-Admin-Token": adminToken },
    body: JSON.stringify({ id })
  });
  if (!res.ok) return alert("Ошибка удаления");
  document.getElementById("news-modal").classList.add("hidden");
  await loadPendingNews();
  await loadPublishedNewsAdmin();
}

async function loadPendingNews() {
  try {
    const res = await fetch(`${api}/news/admin/pending`, { headers: { "X-Admin-Token": adminToken } });
    if (!res.ok) return;
    const items = await res.json();
    const area = document.getElementById("pending-news-list");
    if (!area) return;
    area.innerHTML = "";

    if (!items.length) {
      area.innerHTML = "<div class='card'>Нет новостей на модерации</div>";
      return;
    }

    items.map(n => normalizeNews(n, "pending")).forEach(item => {
      const card = buildNewsPreviewCard(item);
      card.addEventListener("click", () => openNewsModal(item));
      area.appendChild(card);
    });
  } catch (err) {
    console.error("loadPendingNews", err);
  }
}

async function loadPublishedNewsAdmin() {
  try {
    const res = await fetch(`${api}/news/published`);
    if (!res.ok) return;
    const items = await res.json();
    const area = document.getElementById("published-news-list");
    if (!area) return;
    area.innerHTML = "";

    if (!items.length) {
      area.innerHTML = "<div class='card'>Пока нет опубликованных новостей</div>";
      return;
    }

    items.map(n => normalizeNews(n, "published")).slice(0, 30).forEach(item => {
      const card = buildNewsPreviewCard(item);
      card.addEventListener("click", () => openNewsModal(item));
      area.appendChild(card);
    });
  } catch (err) {
    console.error("loadPublishedNewsAdmin", err);
  }
}

async function publishNews(id) {
  const res = await fetch(`${api}/news/admin/publish`, {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-Admin-Token": adminToken },
    body: JSON.stringify({ id })
  });
  if (!res.ok) return alert("Ошибка публикации");
  await loadPendingNews();
    await loadPublishedNewsAdmin();
    await loadHonorBoard();
}

async function rejectNews(id) {
  const reason = (prompt("Причина отклонения:", "") || "").trim();
  if (!reason) return alert("Укажите причину отклонения");
  const res = await fetch(`${api}/news/admin/reject`, {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-Admin-Token": adminToken },
    body: JSON.stringify({ id, reason })
  });
  if (!res.ok) return alert("Ошибка отклонения");
  await loadPendingNews();
    await loadPublishedNewsAdmin();
    await loadHonorBoard();
}

function setupNewsModalControls() {
  const modal = document.getElementById("news-modal");
  const close = document.getElementById("news-modal-close");
  if (!modal || !close) return;

  close.addEventListener("click", () => modal.classList.add("hidden"));
  document.getElementById("news-prev")?.addEventListener("click", () => { newsState.mediaIndex--; renderNewsMedia(); });
  document.getElementById("news-next")?.addEventListener("click", () => { newsState.mediaIndex++; renderNewsMedia(); });
  document.getElementById("news-rotate")?.addEventListener("click", () => { newsState.rotation = (newsState.rotation + 90) % 360; renderNewsMedia(); });

  document.getElementById("news-save-btn")?.addEventListener("click", saveNewsText);
  document.getElementById("news-publish-btn")?.addEventListener("click", async () => {
    if (!newsState.current) return;
    await publishNews(newsState.current.id);
    modal.classList.add("hidden");
  });
  document.getElementById("news-reject-btn")?.addEventListener("click", async () => {
    if (!newsState.current) return;
    await rejectNews(newsState.current.id);
    modal.classList.add("hidden");
  });
  document.getElementById("news-delete-btn")?.addEventListener("click", async () => {
    if (!newsState.current) return;
    await deleteNews(newsState.current.id);
  });
}

// ---- UI: Tabs ----
function setupTabs() {
  // Поддержка и старого .menu-item, и нового .sidebar-item.
  const tabs = document.querySelectorAll(".menu-item, .sidebar-item");
  const contents = document.querySelectorAll(".tab-content");
  tabs.forEach(tab => {
    tab.addEventListener("click", () => {
      tabs.forEach(t => t.classList.remove("active"));
      contents.forEach(c => c.classList.remove("active"));
      tab.classList.add("active");
      const id = tab.dataset.tab;
      const target = document.getElementById("tab-" + id);
      if (target) target.classList.add("active");

      // Lazy-load данных для некоторых вкладок.
      if (id === 'settings-plugins') loadPluginsList();
      if (id === 'settings-system') loadStorageInfo();
      if (id === 'settings-bells') initBellUI();
      if (id === 'calendar') loadCalendarCategories();
      if (id === 'settings-idle') loadIdleImages();
    });
  });
}

// ---- Helpers ----
function escapeHtml(str) {
  return (str || "").replace(/[&<>"']/g, s => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[s]));
}

function placeholderSvgDataUri(text = "Нет фото", w = 600, h = 400) {
  const svg = `<svg xmlns='http://www.w3.org/2000/svg' width='${w}' height='${h}'><rect width='100%' height='100%' fill='#111'/><text x='50%' y='50%' fill='#777' font-family='Segoe UI, Roboto, Arial' font-size='28' dominant-baseline='middle' text-anchor='middle'>${escapeHtml(text)}</text></svg>`;
  return "data:image/svg+xml;charset=utf-8," + encodeURIComponent(svg);
}

// ---- Drag & Drop for file inputs ----
function setupDragAndDrop() {
  // generic file inputs
  document.querySelectorAll("input[type='file']").forEach(input => {
    const container = input.closest(".drop-zone") || input.parentElement;
    if (!container) return;

    container.addEventListener("dragover", e => {
      e.preventDefault();
      container.classList.add("drag-hover");
    });
    container.addEventListener("dragleave", e => {
      container.classList.remove("drag-hover");
    });
    container.addEventListener("drop", e => {
      e.preventDefault();
      container.classList.remove("drag-hover");
      const dt = e.dataTransfer;
      if (!dt || !dt.files || dt.files.length === 0) return;
      input.files = dt.files;
      // auto-upload for certain inputs:
      if (input.id === "post-images") {
        // nothing — images are uploaded after creating a post
      } else {
        const type = input.id.replace("-file", "");
        if (["main", "changes", "other"].includes(type)) uploadSchedule(type);
        else uploadFileType(type);
      }
    });
  });
}

// ---- Load settings (small) ----
async function loadSettings() {
  try {
    const res = await fetch(`${api}/settings/get`);
    if (!res.ok) return;
    const data = await res.json();

    const autoStartValue = data.remoteAutoStart ?? data.RemoteAutoStart ?? data.autostart ?? false;
    document.getElementById("autostart-toggle").checked = !!autoStartValue;

    const cfgRes = await fetch(`${api}/config`);
    if (cfgRes.ok) {
      const cfg = await cfgRes.json();
      const sleepValue = cfg.sleepAt || cfg.SleepAt || "";
      document.getElementById("sleep-time").value = sleepValue;
      const sleepEnabledEl = document.getElementById("sleep-enabled");
      if (sleepEnabledEl) sleepEnabledEl.checked = !!sleepValue;
    }
  } catch (err) {
    console.warn("loadSettings:", err);
  }
  loadStorageInfo();
}

async function toggleAutostart() {
  try {
    const enabled = document.getElementById("autostart-toggle").checked;
    const getRes = await fetch(`${api}/config`);
    if (!getRes.ok) throw new Error("config get failed");

    const cfg = await getRes.json();
    cfg.remoteAutoStart = enabled;

    const saveRes = await fetch(`${api}/config`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(cfg)
    });

    if (!saveRes.ok) throw new Error("config save failed");
  } catch (err) {
    console.error("toggleAutostart", err);
    alert("Не удалось сохранить автозапуск");
  }
}

async function saveSleepMode() {
  const value = (document.getElementById("sleep-time").value || "").trim();
  if (value && !/^\d{2}:\d{2}$/.test(value)) {
    return alert("Используйте формат HH:mm");
  }

  const getRes = await fetch(`${api}/config`);
  if (!getRes.ok) return alert("Не удалось загрузить config");
  const cfg = await getRes.json();
  cfg.sleepAt = value;
  cfg.SleepAt = value;

  const saveRes = await fetch(`${api}/config`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(cfg)
  });

  if (!saveRes.ok) return alert("Не удалось сохранить время сна");
  alert("Время сна сохранено");
}

async function loadConfigSettings() {
  try {
    const res = await fetch(`${api}/config`);
    if (!res.ok) return;
    const cfg = await res.json();
    document.getElementById("food-block-id").value = cfg.foodBlockId || cfg.FoodBlockId || "15159";
    // Погода
    const cityEl = document.getElementById("weather-city");
    const latEl = document.getElementById("weather-lat");
    const lonEl = document.getElementById("weather-lon");
    const apiKeyEl = document.getElementById("weather-api-key");
    if (cityEl) cityEl.value = cfg.city || cfg.City || "";
    if (latEl) latEl.value = cfg.cachedLatitude || cfg.CachedLatitude || "";
    if (lonEl) lonEl.value = cfg.cachedLongitude || cfg.CachedLongitude || "";
    if (apiKeyEl) apiKeyEl.value = cfg.openWeatherApiKey || cfg.OpenWeatherApiKey || "";
  } catch (err) {
    console.warn("loadConfigSettings:", err);
  }
}

// Парсинг координат из формата "51.176333, 36.288285" (Яндекс.Карты).
function parseWeatherCoords() {
  const input = document.getElementById('weather-coords');
  if (!input) return;
  const val = input.value.trim();
  if (!val) return;

  // Различные форматы: "51.176333, 36.288285" или "51.176333 36.288285"
  const parts = val.split(/[,\s]+/).filter(Boolean);
  if (parts.length >= 2) {
    const lat = parseFloat(parts[0]);
    const lon = parseFloat(parts[1]);
    if (!isNaN(lat) && !isNaN(lon)) {
      const latEl = document.getElementById('weather-lat');
      const lonEl = document.getElementById('weather-lon');
      if (latEl) latEl.value = lat;
      if (lonEl) lonEl.value = lon;
      console.log('[weather] parsed coords:', lat, lon);
    }
  }
}

async function saveWeatherSettings() {
  try {
    const city = (document.getElementById("weather-city").value || "").trim();
    const latVal = parseFloat(document.getElementById("weather-lat").value);
    const lonVal = parseFloat(document.getElementById("weather-lon").value);
    const apiKeyEl = document.getElementById("weather-api-key");
    const apiKey = apiKeyEl ? apiKeyEl.value.trim() : "";

    const getRes = await fetch(`${api}/config`);
    if (!getRes.ok) { alert("Не удалось загрузить конфиг"); return; }
    const cfg = await getRes.json();

    // Сохраняем всё в один запрос — город, координаты, API key.
    cfg.city = city;
    cfg.City = city;

    if (!isNaN(latVal) && latVal !== 0) {
      cfg.cachedLatitude = latVal;
      cfg.CachedLatitude = latVal;
    } else {
      cfg.cachedLatitude = 0;
      cfg.CachedLatitude = 0;
    }
    if (!isNaN(lonVal) && lonVal !== 0) {
      cfg.cachedLongitude = lonVal;
      cfg.CachedLongitude = lonVal;
    } else {
      cfg.cachedLongitude = 0;
      cfg.CachedLongitude = 0;
    }

    if (apiKey) {
      cfg.openWeatherApiKey = apiKey;
      cfg.OpenWeatherApiKey = apiKey;
    }

    const saveRes = await fetch(`${api}/config`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(cfg)
    });
    if (!saveRes.ok) {
      const errText = await saveRes.text().catch(() => '');
      alert("Ошибка сохранения: " + errText);
      return;
    }

    alert("Настройки погоды сохранены. Координаты будут определены автоматически.");
  } catch (err) {
    console.error("saveWeatherSettings", err);
    alert("Ошибка: " + err.message);
  }
}

async function saveFoodBlockId() {
  try {
    const value = (document.getElementById("food-block-id").value || "").trim() || "15159";

    const getRes = await fetch(`${api}/config`);
    if (!getRes.ok) {
      alert("Не удалось загрузить текущий конфиг");
      return;
    }

    const cfg = await getRes.json();
    cfg.foodBlockId = value;
    cfg.FoodBlockId = value;

    const saveRes = await fetch(`${api}/config`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(cfg)
    });

    if (!saveRes.ok) {
      alert("Ошибка сохранения FoodBlockId");
      return;
    }

    alert("FoodBlockId сохранён");
  } catch (err) {
    console.error("saveFoodBlockId", err);
    alert("Ошибка сохранения FoodBlockId");
  }
}


async function loadThemeAndExtraSettings() {
  try {
    const res = await fetch(`${api}/config`);
    if (!res.ok) return;
    const cfg = await res.json();
    const ui = cfg.interfaceSettings || cfg.InterfaceSettings || {};
    const ticker = cfg.ticker || cfg.Ticker || {};
    const idle = cfg.idleScreen || cfg.IdleScreen || {};

    // Старые поля (для совместимости).
    const oldFont = document.getElementById("theme-font-family");
    const oldFontSize = document.getElementById("theme-font-size");
    const oldNavSize = document.getElementById("theme-nav-font-size");
    if (oldFont) oldFont.value = ui.fontFamily || ui.FontFamily || "Roboto";
    if (oldFontSize) oldFontSize.value = ui.fontSize || ui.FontSize || 16;
    if (oldNavSize) oldNavSize.value = ui.navigationButtonFontSize || ui.NavigationButtonFontSize || 15;

    // Новые color picker'ы.
    const setVal = (id, val) => { const el = document.getElementById(id); if (el && val) el.value = val; };
    setVal('theme-bg-color', ui.backgroundColor || ui.BackgroundColor || '#1E1E1E');
    setVal('theme-panel-color', ui.panelColor || '#252525');
    setVal('theme-accent-color', ui.accentColor || '#3A9FFF');
    setVal('theme-text-color', ui.buttonForeground || ui.ButtonForeground || '#FFFFFF');
    setVal('theme-button-color', ui.buttonBackground || ui.ButtonBackground || '#3A3A3A');
    setVal('theme-border-color', ui.borderColor || '#333333');

    document.getElementById("ticker-enabled").checked = !!(ticker.enabled ?? ticker.Enabled ?? false);
    document.getElementById("ticker-text").value = ticker.text || ticker.Text || "";
    const tickerItems = ticker.items || ticker.Items || [];
    tickerItemsState = Array.isArray(tickerItems) ? [...tickerItems] : [];
    renderTickerItemsList();
    document.getElementById("ticker-speed").value = ticker.speed || ticker.Speed || 1.5;

    document.getElementById("idle-enabled").checked = !!(idle.enabled ?? idle.Enabled ?? true);
    document.getElementById("idle-timeout").value = idle.timeoutSeconds || idle.TimeoutSeconds || 90;
    document.getElementById("idle-slide-duration").value = idle.slideDurationSeconds || idle.SlideDurationSeconds || 8;

    // School site URL.
    setVal('schoolsite-url', cfg.schoolSiteUrl || cfg.SchoolSiteUrl || 'https://obo-afan.gosuslugi.ru');
  } catch (err) {
    console.warn("loadThemeAndExtraSettings", err);
  }
}

async function saveThemeSettings() {
  try {
    const res = await fetch(`${api}/config`);
    if (!res.ok) return alert("Ошибка чтения config");
    const cfg = await res.json();
    cfg.interfaceSettings = cfg.interfaceSettings || cfg.InterfaceSettings || {};
    cfg.InterfaceSettings = cfg.interfaceSettings;

    cfg.interfaceSettings.theme = document.getElementById("theme-mode").value;
    cfg.interfaceSettings.fontFamily = document.getElementById("theme-font-family").value || "Segoe UI";
    cfg.interfaceSettings.fontSize = Number(document.getElementById("theme-font-size").value || 14);
    cfg.interfaceSettings.navigationButtonFontSize = Number(document.getElementById("theme-nav-font-size").value || 15);
    cfg.interfaceSettings.navigationButtonBackground = document.getElementById("theme-nav-bg").value || "#3A3A3A";
    cfg.interfaceSettings.navigationButtonForeground = document.getElementById("theme-nav-fg").value || "#FFFFFF";

    const selectedTheme = (cfg.interfaceSettings.theme || "dark").toLowerCase();
    if (selectedTheme === "dark") {
      cfg.interfaceSettings.backgroundColor = "#1E1E1E";
      cfg.interfaceSettings.buttonBackground = "#3A3A3A";
      cfg.interfaceSettings.buttonForeground = "White";
      cfg.interfaceSettings.navigationButtonBackground = "#3A3A3A";
      cfg.interfaceSettings.navigationButtonForeground = "White";
    } else {
      cfg.interfaceSettings.backgroundColor = "#F5F5F5";
      cfg.interfaceSettings.buttonBackground = "#E0E0E0";
      cfg.interfaceSettings.buttonForeground = "#222222";
    }

    const save = await fetch(`${api}/config`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(cfg) });
    if (!save.ok) return alert("Ошибка сохранения темы");
    alert("Тема сохранена");
  } catch (err) {
    console.error("saveThemeSettings", err);
    alert("Ошибка сохранения темы");
  }
}

// Список сообщений бегущей строки.
// Хранится в памяти, рендерится в #ticker-items-list.
// Каждое сообщение добавляется кнопкой ➕, удаляется кнопкой ✕.
let tickerItemsState = [];

// Рендер списка сообщений бегущей строки.
function renderTickerItemsList() {
  const list = document.getElementById('ticker-items-list');
  if (!list) return;
  list.innerHTML = '';
  if (!tickerItemsState.length) {
    list.innerHTML = '<p class="hint" style="margin:6px 0;">Сообщений нет. Добавьте первое выше.</p>';
    return;
  }
  tickerItemsState.forEach((item, idx) => {
    const row = document.createElement('div');
    row.className = 'ticker-item-row';
    row.innerHTML = `
      <span class="ticker-item-text">${escapeHtml(item)}</span>
      <button class="small-btn danger" onclick="removeTickerItem(${idx})" title="Удалить">✕</button>
    `;
    list.appendChild(row);
  });
}

// Добавление сообщения в список.
function addTickerItem() {
  const input = document.getElementById('ticker-new-item');
  if (!input) return;
  const text = (input.value || '').trim();
  if (!text) return;
  // Проверяем дубликаты (case-insensitive).
  const exists = tickerItemsState.some(t => t.toLowerCase() === text.toLowerCase());
  if (exists) {
    input.value = '';
    return;
  }
  if (tickerItemsState.length >= 200) {
    alert('Достигнут максимум сообщений (200)');
    return;
  }
  tickerItemsState.push(text);
  input.value = '';
  renderTickerItemsList();
  input.focus();
}

// Удаление сообщения по индексу.
function removeTickerItem(idx) {
  if (idx < 0 || idx >= tickerItemsState.length) return;
  tickerItemsState.splice(idx, 1);
  renderTickerItemsList();
}

async function saveTickerSettings() {
  try {
    const res = await fetch(`${api}/config`);
    if (!res.ok) return alert("Ошибка чтения config");
    const cfg = await res.json();
    cfg.ticker = cfg.ticker || cfg.Ticker || {};
    cfg.Ticker = cfg.ticker;
    cfg.ticker.enabled = document.getElementById("ticker-enabled").checked;
    const textValue = (document.getElementById("ticker-text").value || "").trim();
    // Берём массив сообщений из tickerItemsState (уже без дубликатов).
    const items = [...tickerItemsState];
    cfg.ticker.text = textValue;
    cfg.ticker.items = items;
    if (!cfg.ticker.enabled || (!textValue && items.length === 0)) {
      cfg.ticker.text = "";
      cfg.ticker.items = [];
    }
    cfg.ticker.speed = Number(document.getElementById("ticker-speed").value || 1.5);

    const save = await fetch(`${api}/config`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(cfg) });
    if (!save.ok) return alert("Ошибка сохранения бегущей строки");
    alert("Настройки бегущей строки сохранены");
  } catch (err) {
    console.error("saveTickerSettings", err);
    alert("Ошибка сохранения бегущей строки");
  }
}


async function clearTickerSettings() {
  document.getElementById("ticker-enabled").checked = false;
  document.getElementById("ticker-text").value = "";
  tickerItemsState = [];
  renderTickerItemsList();
  await saveTickerSettings();
}

async function changeAdminPassword() {
  const oldPassword = (document.getElementById("admin-old-password").value || "").trim();
  const newPassword = (document.getElementById("admin-new-password").value || "").trim();
  if (!oldPassword || !newPassword) return alert("Введите текущий и новый пароль");
  if (newPassword.length < 4) return alert("Новый пароль слишком короткий (минимум 4 символа)");
  const res = await fetch(`${api}/auth/admin/change-password`, {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-Admin-Token": adminToken },
    body: JSON.stringify({ oldPassword, newPassword })
  });
  if (!res.ok) {
    const err = await res.text().catch(() => "");
    return alert("Не удалось сменить пароль: " + err);
  }
  document.getElementById("admin-old-password").value = "";
  document.getElementById("admin-new-password").value = "";
  alert("Пароль изменён. Новый пароль используется и для входа в админку с киоска, и для входа в панель администратора.");
}

async function changeVolume(action) {
  const res = await fetch(`${api}/system/volume?action=${encodeURIComponent(action)}`, {
    headers: { "X-Admin-Token": adminToken }
  });
  if (!res.ok) return alert("Команда громкости не выполнена");
}
async function saveIdleSettings() {
  try {
    const res = await fetch(`${api}/config`);
    if (!res.ok) return alert("Ошибка чтения config");
    const cfg = await res.json();
    cfg.idleScreen = cfg.idleScreen || cfg.IdleScreen || {};
    cfg.IdleScreen = cfg.idleScreen;
    cfg.idleScreen.enabled = document.getElementById("idle-enabled").checked;
    cfg.idleScreen.timeoutSeconds = Number(document.getElementById("idle-timeout").value || 90);
    cfg.idleScreen.slideDurationSeconds = Number(document.getElementById("idle-slide-duration").value || 8);

    const save = await fetch(`${api}/config`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(cfg) });
    if (!save.ok) return alert("Ошибка сохранения экрана ожидания");
    alert("Настройки экрана ожидания сохранены");
  } catch (err) {
    console.error("saveIdleSettings", err);
    alert("Ошибка сохранения экрана ожидания");
  }
}

// ---- Categories + Posts UI ----
async function loadCategoriesAndBuildUI() {
  try {
    const res = await fetch(`${api}/media/categories`);
    if (!res.ok) throw new Error("categories failed");
    const cats = await res.json();

    const sel = document.getElementById('category-select');
    sel.innerHTML = "";
    if (!cats || cats.length === 0) {
      sel.innerHTML = '<option value="">Нет категорий</option>';
      return;
    }

    cats.forEach(c => {
      const opt = document.createElement("option");
      opt.value = c.id;
      opt.textContent = c.name || c.id;
      sel.appendChild(opt);
    });

    sel.selectedIndex = 0;
    await onCategoryChange();
  } catch (err) {
    console.error("loadCategoriesAndBuildUI", err);
  }
}

async function loadPostCategorySelector() {
  try {
    const res = await fetch(`${api}/media/categories`);
    if (!res.ok) return;
    const cats = await res.json();
    const sel = document.getElementById("post-category");
    sel.innerHTML = "";
    cats.forEach(c => {
      const opt = document.createElement("option");
      opt.value = c.id;
      opt.textContent = c.name;
      sel.appendChild(opt);
    });
  } catch (err) {
    console.error("loadPostCategorySelector", err);
  }
}

async function onCategoryChange() {
  const sel = document.getElementById('category-select');
  if (!sel) return;
  const chosen = sel.value;
  if (!chosen) return;
  await loadPosts(chosen, 1, 9);
}

// ---- Load posts list ----
async function loadPosts(category, page = 1, pageSize = 9) {
  try {
    const res = await fetch(`${api}/media/posts?category=${encodeURIComponent(category)}&page=${page}&pageSize=${pageSize}`);
    if (!res.ok) throw new Error("posts list failed");
    const data = await res.json();
    renderPostsGrid(data.items || [], data.page, data.pageSize, data.total, category);
  } catch (err) {
    console.error("loadPosts", err);
  }
}

// ---- Render grid ----
function renderPostsGrid(items, page, pageSize, total, category) {
  const area = document.getElementById("media-posts-area");
  if (!area) return;
  area.innerHTML = "";

  if (!items || items.length === 0) {
    area.innerHTML = "<p>Нет постов</p>";
    return;
  }

  items.forEach(post => {
    const id = post.Id || post.id;
    const title = post.Title || post.title || "Без названия";
    const date = post.Date || post.date || "";
    const imagesCount = (post.ImagesCount != null) ? post.ImagesCount : (post.Images ? post.Images.length : 0);
    const coverFile = post.Cover || post.cover || "";

    // build cover src if exists
    const coverSrc = coverFile
      ? `${api}/download?target=media&category=${encodeURIComponent(category)}&post=${encodeURIComponent(id)}&name=${encodeURIComponent(coverFile)}`
      : placeholderSvgDataUri("Нет фото", 800, 450);

    const card = document.createElement("div");
    card.className = "post-card";

    card.innerHTML = `
      <div class="cover"><img src="${coverSrc}" alt="cover"></div>
      <div class="meta">
        <div class="title">${escapeHtml(title)}</div>
        <div class="date">${escapeHtml(date)}</div>
       <div class="footer">
            <div class="count">Фото: ${imagesCount}</div>
            <div class="actions">
            <button class="pill open">Открыть</button>
            <button class="pill del">Удалить</button>
        </div>
</div>

</div>

      </div>
    `;

    // events
    card.querySelector(".open").onclick = e => { e.stopPropagation(); openPostModalById(category, id); };
    card.querySelector(".del").onclick = async e => {
      e.stopPropagation();
      if (!confirm(`Удалить пост "${title}"?`)) return;
      await deletePost(category, id);
    };

    card.onclick = () => openPostModalById(category, id);

    area.appendChild(card);
  });
}

// ---- Open post modal (loads full post and images array) ----
let modalState = {
  images: [], // array of { file, url }
  currentIndex: 0,
  postId: null,
  category: null
};

async function openPostModalById(category, postId) {
  try {
    const res = await fetch(`${api}/media/post?category=${encodeURIComponent(category)}&id=${encodeURIComponent(postId)}`);
    if (!res.ok) {
      alert("Пост не найден");
      return;
    }
    const post = await res.json();

    // title/date/desc
    document.getElementById("modal-post-title").textContent = post.title || post.Title || "";
    document.getElementById("modal-post-date").textContent = post.date || post.Date || "";
    document.getElementById("modal-post-desc").textContent = post.description || post.Description || "";

    // build images list
    const imgs = post.images || post.Images || [];
    modalState.images = imgs.map(img => {
      const fileName = img.file || img.File || img.name || img.Name;
      const url = fileName
        ? `${api}/download?target=media&category=${encodeURIComponent(category)}&post=${encodeURIComponent(postId)}&name=${encodeURIComponent(fileName)}`
        : "";
      return { file: fileName, url };
    }).filter(x => !!x.file);

    modalState.currentIndex = 0;
    modalState.postId = postId;
    modalState.category = category;

    // show first image
    showModalImage(0);

    document.getElementById("post-modal").classList.remove("hidden");
  } catch (err) {
    console.error("openPostModalById", err);
    alert("Ошибка загрузки поста");
  }
}

// ---- Modal controls & gallery ----
function setupModalControls() {
  const modal = document.getElementById("post-modal");
  const closeBtn = modal.querySelector(".post-modal-close");
  const prevBtn = document.getElementById("modal-prev");
  const nextBtn = document.getElementById("modal-next");
  const imgEl = document.getElementById("modal-img");

  closeBtn.addEventListener("click", () => {
    modal.classList.add("hidden");
    modalState.images = [];
    modalState.currentIndex = 0;
  });

  prevBtn.addEventListener("click", () => {
    if (!modalState.images || modalState.images.length === 0) return;
    const next = (modalState.currentIndex - 1 + modalState.images.length) % modalState.images.length;
    showModalImage(next);
  });

  nextBtn.addEventListener("click", () => {
    if (!modalState.images || modalState.images.length === 0) return;
    const next = (modalState.currentIndex + 1) % modalState.images.length;
    showModalImage(next);
  });

  // keyboard support
  window.addEventListener("keydown", (e) => {
    if (document.getElementById("post-modal").classList.contains("hidden")) return;
    if (e.key === "ArrowLeft") prevBtn.click();
    if (e.key === "ArrowRight") nextBtn.click();
    if (e.key === "Escape") closeBtn.click();
  });

  // click on image advances forward
  imgEl.addEventListener("click", () => {
    nextBtn.click();
  });
}

function showModalImage(index) {
  if (!modalState.images || modalState.images.length === 0) {
    document.getElementById("modal-img").src = placeholderSvgDataUri("Нет фото");
    return;
  }
  if (index < 0 || index >= modalState.images.length) index = 0;
  modalState.currentIndex = index;
  const item = modalState.images[index];
  document.getElementById("modal-img").src = item.url;
}

// ---- Delete post ----
async function deletePost(category, id) {
  try {
    const res = await fetch(`${api}/media/post/delete?category=${encodeURIComponent(category)}&id=${encodeURIComponent(id)}`, {
      method: "GET"
    });
    if (!res.ok) {
      alert("Ошибка удаления поста");
      return;
    }
    const data = await res.json();
    if (data.status === "deleted") {
      await loadPosts(category, 1, 9);
    } else {
      alert("Не получилось удалить пост");
    }
  } catch (err) {
    console.error("deletePost", err);
    alert("Ошибка при удалении");
  }
}

// ---- Create post + upload images ----
async function createFullPost() {
  try {
    const title = document.getElementById("post-title").value.trim();
    const description = document.getElementById("post-description").value.trim();
    const date = document.getElementById("post-date").value;
    const category = document.getElementById("post-category").value;
    const files = Array.from(document.getElementById("post-images").files);

    if (!title) return alert("Введите название поста");
    if (!category) return alert("Выберите категорию");

    // 1) create post
    const createRes = await fetch(`${api}/media/post/create`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ category, title, description, date })
    });
    if (!createRes.ok) throw new Error("Ошибка создания поста");
    const createData = await createRes.json();
    const postId = createData.id || createData.Id;
    if (!postId) throw new Error("Не получили id поста");

    // 2) upload files
    const uploaded = [];
    for (const file of files) {
      const ok = await uploadImageToPost(file, category, postId);
      if (ok) uploaded.push({ file: file.name, caption: "" });
    }

    // 3) update images array
    if (uploaded.length > 0) {
      await fetch(`${api}/media/post/updateImages`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ Category: category, Id: postId, Images: uploaded })
      });
    }

    alert("Пост создан");
    // reset form
    document.getElementById("post-title").value = "";
    document.getElementById("post-description").value = "";
    document.getElementById("post-date").value = "";
    document.getElementById("post-images").value = "";
    // reload list
    await loadPosts(category, 1, 9);
  } catch (err) {
    console.error("createFullPost", err);
    alert("Ошибка создания поста: " + (err.message || err));
  }
}

async function uploadImageToPost(file, category, postId) {
  try {
    const utf8Name = unescape(encodeURIComponent(file.name));
    const nameB64 = btoa(utf8Name);
    const url = `${api}/upload?target=media&category=${encodeURIComponent(category)}&post=${encodeURIComponent(postId)}`;
    const res = await fetch(url, {
      method: "POST",
      headers: { "X-Filename-Base64": nameB64 },
      body: file
    });
    return res.ok;
  } catch (err) {
    console.error("uploadImageToPost", err);
    return false;
  }
}

// ---- Upload / file management (schedules, docs) ----
async function uploadSchedule(type) {
  const input = document.getElementById(type + "-file");
  const files = input ? Array.from(input.files) : [];
  if (!files.length) return alert("Выберите файл(ы)!");
  for (const file of files) {
    try {
      const utf8Name = unescape(encodeURIComponent(file.name));
      const nameB64 = btoa(utf8Name);
      const url = `${api}/upload?target=${type}`;
      const res = await fetch(url, {
        method: "POST",
        headers: { "X-Filename-Base64": nameB64 },
        body: file
      });
      if (!res.ok) throw new Error("Ошибка загрузки " + file.name);
    } catch (err) {
      console.error(err);
      alert("Ошибка при загрузке: " + err.message);
    }
  }
  alert("Файлы загружены");
  if (input) input.value = '';
  if (type === "other") await loadOtherSchedules();
  await loadScheduleInfo();
}

async function loadOtherSchedules() {
  try {
    const res = await fetch(`${api}/list?target=other`);
    if (!res.ok) return;
    const data = await res.json();
    const list = document.getElementById("other-files");
    if (!list) return;
    list.innerHTML = "";
    if (data.files && data.files.length) {
      data.files.forEach(f => {
        const ext = (f.name.split('.').pop() || '').toLowerCase();
        const icon = {xlsx:'📗',xls:'📗',pdf:'📕',doc:'📘',docx:'📘',png:'🖼️',jpg:'🖼️',jpeg:'🖼️',gif:'🖼️'}[ext] || '📄';
        const li = document.createElement("li");
        li.innerHTML = `<span>${icon} ${escapeHtml(f.name)}</span><div>
          <a href="${api}/download?target=other&name=${encodeURIComponent(f.name)}" download="${escapeHtml(f.name)}" title="Скачать" style="padding:6px 10px;font-size:14px;text-decoration:none;display:inline-flex;align-items:center;background:var(--button);color:var(--text);border-radius:var(--radius-sm);margin-right:4px;">⬇</a>
          <button onclick="deleteFile('${escapeHtml(f.name)}','other')" class="danger" style="padding:6px 10px;margin:0;font-size:14px;">🗑</button>
        </div>`;
        list.appendChild(li);
      });
    } else list.innerHTML = "<li>Нет файлов</li>";
  } catch (err) {
    console.error("loadOtherSchedules", err);
  }
}

// Загрузка информации о расписаниях (основное и изменённое).
async function loadScheduleInfo() {
  try {
    // Основное — через /list?target=main
    const mainRes = await fetch(`${api}/list?target=main`);
    if (mainRes.ok) {
      const mainData = await mainRes.json();
      const mainEl = document.getElementById('main-schedule-info');
      if (mainEl) {
        const files = mainData.files || [];
        if (files.length) {
          const f = files[0];
          mainEl.innerHTML = `<div style="padding:10px;background:var(--surface);border-radius:var(--radius-sm);border:1px solid var(--border);">
            <div style="font-size:13px;font-weight:500;">📄 ${escapeHtml(f.name)}</div>
            <div style="margin-top:6px;display:flex;gap:6px;">
              <a href="${api}/download?target=main&name=${encodeURIComponent(f.name)}" download="${escapeHtml(f.name)}" style="padding:4px 10px;font-size:12px;text-decoration:none;background:var(--accent);color:#fff;border-radius:var(--radius-sm);">⬇ Скачать</a>
            </div>
          </div>`;
        } else {
          mainEl.innerHTML = '<p class="hint" style="font-size:12px;">Не загружено</p>';
        }
      }
    }
    // Изменённое — через /list?target=changes
    const changesRes = await fetch(`${api}/list?target=changes`);
    if (changesRes.ok) {
      const changesData = await changesRes.json();
      const changesEl = document.getElementById('changes-schedule-info');
      if (changesEl) {
        const files = changesData.files || [];
        if (files.length) {
          const f = files[0];
          changesEl.innerHTML = `<div style="padding:10px;background:var(--surface);border-radius:var(--radius-sm);border:1px solid var(--border);">
            <div style="font-size:13px;font-weight:500;">📄 ${escapeHtml(f.name)}</div>
            <div style="margin-top:6px;display:flex;gap:6px;">
              <a href="${api}/download?target=changes&name=${encodeURIComponent(f.name)}" download="${escapeHtml(f.name)}" style="padding:4px 10px;font-size:12px;text-decoration:none;background:var(--accent);color:#fff;border-radius:var(--radius-sm);">⬇ Скачать</a>
            </div>
          </div>`;
        } else {
          changesEl.innerHTML = '<p class="hint" style="font-size:12px;">Не загружено</p>';
        }
      }
    }
  } catch (err) {
    console.warn('loadScheduleInfo:', err);
  }
}

async function uploadFileType(type) {
  const input = document.getElementById(type + "-file");
  const files = input ? Array.from(input.files) : [];
  if (!files.length) return alert("Выберите файл(ы)!");
  for (const file of files) {
    try {
      const utf8Name = unescape(encodeURIComponent(file.name));
      const nameB64 = btoa(utf8Name);
      const url = `${api}/upload?target=${type}`;
      const res = await fetch(url, {
        method: "POST",
        headers: { "X-Filename-Base64": nameB64 },
        body: file
      });
      if (!res.ok) throw new Error(file.name);
    } catch (err) {
      console.error("uploadFileType", err);
      alert("Ошибка при загрузке: " + err.message);
    }
  }
  alert("Файлы загружены");
  await loadFilesCategory();
}

async function loadFilesCategory() {
  try {
    const category = "docs";
    const res = await fetch(`${api}/list?target=${encodeURIComponent(category)}`);
    if (!res.ok) return;
    const data = await res.json();
    const grid = document.getElementById("files-grid");
    if (!grid) return;
    const files = data.files || [];
    if (!files.length) {
      grid.innerHTML = '<p class="hint" style="text-align:center;padding:30px;">Нет загруженных файлов</p>';
      return;
    }
    grid.innerHTML = files.map(f => {
      const ext = (f.name.split('.').pop() || '').toLowerCase();
      const icon = {
        pdf:'📕', doc:'📘', docx:'📘', xls:'📗', xlsx:'📗',
        ppt:'📙', pptx:'📙', txt:'📄', csv:'📊', json:'⚙️',
        jpg:'🖼️', jpeg:'🖼️', png:'🖼️', gif:'🖼️', svg:'🖼️',
        mp4:'🎬', webm:'🎬', mp3:'🎵',
        zip:'📦', rar:'📦',
      }[ext] || '📄';
      const size = f.size ? (f.size / 1024).toFixed(1) + ' KB' : '';
      return `<div class="file-card-admin">
        <div class="file-card-icon">${icon}</div>
        <div class="file-card-info">
          <div class="file-card-name" title="${escapeHtml(f.name)}">${escapeHtml(f.name)}</div>
          <div class="file-card-meta">${escapeHtml(ext.toUpperCase())} ${size ? '• ' + size : ''}</div>
        </div>
        <div class="file-card-actions">
          <button onclick="previewFile('${escapeHtml(f.name)}','${escapeHtml(category)}')" title="Просмотр" style="padding:6px 10px;margin:0;font-size:14px;">👁</button>
          <a href="${api}/download?target=${encodeURIComponent(category)}&name=${encodeURIComponent(f.name)}" download="${escapeHtml(f.name)}" title="Скачать" style="padding:6px 10px;font-size:14px;text-decoration:none;display:inline-flex;align-items:center;background:var(--button);color:var(--text);border-radius:var(--radius-sm);">⬇</a>
          <button onclick="deleteFile('${escapeHtml(f.name)}','${escapeHtml(category)}')" class="danger" title="Удалить" style="padding:6px 10px;margin:0;font-size:14px;">🗑</button>
        </div>
      </div>`;
    }).join('');
  } catch (err) {
    console.error("loadFilesCategory", err);
  }
}

async function deleteFile(name, type) {
  try {
    if (!confirm(`Удалить ${name}?`)) return;
    const utf8Name = unescape(encodeURIComponent(name));
    const nameB64 = btoa(utf8Name);
    const res = await fetch(`${api}/delete?target=${encodeURIComponent(type)}`, {
      method: "GET",
      headers: { "X-Filename-Base64": nameB64 }
    });
    if (!res.ok) alert("Ошибка удаления файла");
    else {
      if (type === "other") await loadOtherSchedules();
      else await loadFilesCategory();
    }
  } catch (err) {
    console.error("deleteFile", err);
    alert("Ошибка при удалении файла");
  }
}

async function previewFile(name, type) {
  try {
    // Используем прямой URL с name в query string.
    const fileUrl = `${api}/download?target=${encodeURIComponent(type)}&name=${encodeURIComponent(name)}`;
    const preview = document.getElementById("preview-area");
    preview.innerHTML = "";

    // Кнопка скачивания — всегда показываем.
    const downloadBar = document.createElement("div");
    downloadBar.style.cssText = "display:flex;align-items:center;justify-content:space-between;margin-bottom:12px;padding:10px 14px;background:var(--surface);border-radius:var(--radius-sm);border:1px solid var(--border);";
    downloadBar.innerHTML = `
      <span style="font-size:14px;font-weight:500;">${escapeHtml(name)}</span>
      <a href="${fileUrl}" download="${escapeHtml(name)}" style="padding:8px 16px;background:var(--accent);color:#fff;border-radius:var(--radius-sm);text-decoration:none;font-size:14px;font-weight:500;">⬇ Скачать</a>
    `;
    preview.appendChild(downloadBar);

    if (name.match(/\.(jpg|jpeg|png|gif|webp|bmp|svg)$/i)) {
      const img = document.createElement("img");
      img.src = fileUrl;
      img.style.maxWidth = "100%";
      img.style.borderRadius = "8px";
      preview.appendChild(img);
      return;
    }
    if (name.match(/\.pdf$/i)) {
      const iframe = document.createElement("iframe");
      iframe.src = fileUrl + "#toolbar=0&navpanes=0&view=FitH";
      iframe.style.width = "100%";
      iframe.style.height = "600px";
      iframe.style.border = "none";
      iframe.style.borderRadius = "8px";
      preview.appendChild(iframe);
      return;
    }
    if (name.match(/\.(mp4|webm|mov|ogg)$/i)) {
      const video = document.createElement("video");
      video.src = fileUrl;
      video.controls = true;
      video.style.width = "100%";
      video.style.borderRadius = "8px";
      preview.appendChild(video);
      return;
    }
    if (name.match(/\.(mp3|wav)$/i)) {
      const audio = document.createElement("audio");
      audio.src = fileUrl;
      audio.controls = true;
      audio.style.width = "100%";
      preview.appendChild(audio);
      return;
    }
    if (name.match(/\.(txt|json|csv|md|log|rtf)$/i)) {
      const res = await fetch(fileUrl);
      const txt = await res.text();
      const pre = document.createElement("pre");
      pre.textContent = txt;
      pre.style.cssText = "white-space:pre-wrap;word-break:break-word;padding:14px;background:var(--bg);border-radius:8px;font-family:monospace;font-size:13px;max-height:600px;overflow:auto;";
      preview.appendChild(pre);
      return;
    }
    if (name.match(/\.(xlsx|xls)$/i)) {
      // Загружаем через bridge — для админки используем /list + preview.
      preview.innerHTML += `<p style="padding:20px;text-align:center;color:var(--text-muted);">Excel-файлы можно скачать и открыть локально. Предпросмотр доступен в киоске.</p>`;
      return;
    }
    if (name.match(/\.(doc|docx|ppt|pptx)$/i)) {
      preview.innerHTML += `<p style="padding:20px;text-align:center;color:var(--text-muted);">Office-документы можно скачать и открыть локально.</p>`;
      return;
    }
    // Другие типы — предлагаем открыть.
    preview.innerHTML += `<p style="padding:20px;text-align:center;color:var(--text-muted);">Предпросмотр недоступен. Используйте кнопку «Скачать».</p>`;
  } catch (err) {
    console.error("previewFile", err);
    document.getElementById("preview-area").innerHTML = "<p>Ошибка предпросмотра</p>";
  }
}

// ---- Calendar ----
const categoryColors = {
  "Праздник": "#b42828",
  "Каникулы": "#289628",
  "Выходной": "#1e50b4",
  "Другое": "#009696"
};

async function loadCalendar() {
  try {
    const res = await fetch(`${api}/calendar/list`);
    if (!res.ok) return;
    const events = await res.json();
    const ul = document.getElementById("calendar");
    ul.innerHTML = "";
    events.forEach(e => {
      const start = e.startDate ? new Date(e.startDate).toLocaleDateString("ru-RU") : "";
      const end = e.endDate ? new Date(e.endDate).toLocaleDateString("ru-RU") : "";
      const range = (end && end !== start) ? `${start} — ${end}` : start;
      const type = e.type || "Другое";
      const yearlyBadge = e.yearly ? ' <span style="font-size:11px;color:var(--accent);">♻ ежегод</span>' : '';
      const li = document.createElement("li");
      li.innerHTML = `<div><strong>${escapeHtml(e.title)}${yearlyBadge}</strong> <div style="color:var(--muted)">${escapeHtml(range)} — ${escapeHtml(type)}${e.yearly ? ' (ежегодное)' : ''}</div></div><div><button onclick="deleteEvent('${escapeHtml(e.id)}')">🗑</button></div>`;
      ul.appendChild(li);
    });
  } catch (err) {
    console.error("loadCalendar", err);
  }
}

async function deleteEvent(id) {
  try {
    const res = await fetch(`${api}/calendar/delete?id=${encodeURIComponent(id)}`);
    if (res.ok) await loadCalendar();
    else alert("Ошибка удаления события");
  } catch (err) {
    console.error("deleteEvent", err);
  }
}

async function addEvent() {
  try {
    const title = document.getElementById("event-title").value.trim();
    const desc = document.getElementById("event-description")?.value?.trim() || "";
    const start = document.getElementById("event-start").value;
    const end = document.getElementById("event-end").value || start;
    const type = document.getElementById("event-type").value;
    const yearly = document.getElementById("event-yearly")?.checked || false;
    if (!title || !start) return alert("Введите название и дату начала!");
    if (!type) return alert("Сначала создайте категорию в блоке «Категории событий» ниже, затем выберите её в выпадающем списке.");
    const body = JSON.stringify({ title, description: desc, startDate: start, endDate: end, type, yearly });
    const res = await fetch(`${api}/calendar/add`, { method: "POST", headers: { "Content-Type": "application/json" }, body });
    if (res.ok) {
      alert("Событие добавлено");
      document.getElementById("event-title").value = "";
      if (document.getElementById("event-description")) document.getElementById("event-description").value = "";
      if (document.getElementById("event-yearly")) document.getElementById("event-yearly").checked = false;
      await loadCalendar();
    } else alert("Ошибка при добавлении");
  } catch (err) {
    console.error("addEvent", err);
  }
}

// ====================================================================
// КАЛЕНДАРНЫЕ КАТЕГОРИИ — создание/удаление с цветом и эмодзи
// ====================================================================
// Категории по умолчанию УБРАНЫ — пользователь создаёт свои.
// Если в config.json нет ни одной категории — показываем подсказку.
async function loadCalendarCategories() {
  try {
    const res = await fetch(`${api}/config`);
    if (!res.ok) return;
    const cfg = await res.json();
    const cats = cfg.calendarCategories || cfg.CalendarCategories || {};

    // Без стандартных категорий — только кастомные из config.
    const all = { ...cats };

    // Список категорий.
    const listEl = document.getElementById('calendar-categories-list');
    if (listEl) {
      if (!Object.keys(all).length) {
        listEl.innerHTML = '<p class="hint" style="margin:0;">Нет категорий. Создайте первую ниже — она появится в выпадающем списке при добавлении события.</p>';
      } else {
        listEl.innerHTML = Object.entries(all).map(([name, cat]) => {
          return `<div class="cal-cat-item">
            <span class="cal-cat-dot" style="background:${cat.color}"></span>
            <span class="cal-cat-icon">${cat.icon || '📌'}</span>
            <span class="cal-cat-name">${escapeHtml(name)}</span>
            <button class="danger" onclick="deleteCalendarCategory('${escapeHtml(name)}')" style="padding:4px 8px;margin:0;font-size:12px;">✕</button>
          </div>`;
        }).join('');
      }
    }

    // Обновляем select в форме добавления события.
    const sel = document.getElementById('event-type');
    if (sel) {
      const currentVal = sel.value;
      sel.innerHTML = '';
      if (!Object.keys(all).length) {
        const opt = document.createElement('option');
        opt.value = '';
        opt.textContent = '— создайте категорию ниже —';
        sel.appendChild(opt);
      } else {
        for (const [name, cat] of Object.entries(all)) {
          const opt = document.createElement('option');
          opt.value = name;
          opt.textContent = `${cat.icon || '📌'} ${name}`;
          sel.appendChild(opt);
        }
        // Сохраняем текущий выбор, если он ещё существует.
        if (currentVal && all[currentVal]) {
          sel.value = currentVal;
        }
      }
    }
  } catch (err) {
    console.warn('loadCalendarCategories:', err);
  }
}

async function addCalendarCategory() {
  try {
    const name = document.getElementById('cal-cat-name')?.value?.trim();
    const icon = document.getElementById('cal-cat-icon')?.value?.trim() || '📌';
    const color = document.getElementById('cal-cat-color')?.value || '#3A9FFF';
    if (!name) return alert("Введите название категории");

    const res = await fetch(`${api}/config`);
    if (!res.ok) return alert("Ошибка");
    const cfg = await res.json();

    if (!cfg.calendarCategories) cfg.calendarCategories = {};
    if (!cfg.CalendarCategories) cfg.CalendarCategories = {};

    cfg.calendarCategories[name] = { color, icon };
    cfg.CalendarCategories[name] = { color, icon };

    const saveRes = await fetch(`${api}/config`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(cfg)
    });
    if (!saveRes.ok) return alert("Ошибка сохранения");

    document.getElementById('cal-cat-name').value = '';
    document.getElementById('cal-cat-icon').value = '';
    await loadCalendarCategories();
    alert("Категория добавлена");
  } catch (err) {
    alert("Ошибка: " + err.message);
  }
}

async function deleteCalendarCategory(name) {
  try {
    if (!confirm(`Удалить категорию "${name}"?`)) return;
    const res = await fetch(`${api}/config`);
    if (!res.ok) return;
    const cfg = await res.json();
    if (cfg.calendarCategories) delete cfg.calendarCategories[name];
    if (cfg.CalendarCategories) delete cfg.CalendarCategories[name];
    await fetch(`${api}/config`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(cfg)
    });
    await loadCalendarCategories();
  } catch (err) {
    alert("Ошибка: " + err.message);
  }
}

// ====================================================================
// EMOJI PICKER — выбор эмодзи для категории
// ====================================================================
const EMOJI_LIST = [
  '🎉','🎊','🎈','🎂','🎁','🏆','🥇','🏅',
  '🎵','🎶','🎨','🎭','🎬','📷','📸','🎤',
  '📚','📖','✏️','📝','🎓','🏫','🔢','🔤',
  '⚽','🏀','🏈','⚾','🎾','🏐','🏉','🎱',
  '🏃','🚴','🏊','⛷️','🏂','🏋️','🤸','🤺',
  '🌱','🌳','🌸','🌺','🌻','🌹','🍀','🍁',
  '☀️','🌙','⭐','⚡','🔥','❄️','🌈','☁️',
  '✈️','🚂','🚗','🚌','🚲','⛵','🗺️','🧭',
  '🍕','🍔','🍟','🍿','🍩','🍪','🎂','🍰',
  '❤️','💛','💚','💙','💜','🖤','🤍','🤎',
  '😀','😎','🤩','🥳','😇','🤔','😴','🤗',
  '👥','🧑‍🏫','👶','🧒','👦','👧','👨','👩',
  '📌','📍','📎','✂️','📐','📏','💡','🔔',
  '🇷🇺','🌐','💻','📱','⌨️','🖥️','🖨️','🖱️',
  '♻️','✅','❌','⚠️','📢','📣','💬','🔍',
];

function openEmojiPicker() {
  const modal = document.getElementById('emoji-picker-modal');
  const grid = document.getElementById('emoji-grid');
  if (!modal || !grid) return;

  grid.innerHTML = EMOJI_LIST.map(e =>
    `<button type="button" style="font-size:26px;padding:10px;background:var(--surface);border:1px solid var(--border);border-radius:var(--radius-sm);cursor:pointer;margin:0;aspect-ratio:1;display:flex;align-items:center;justify-content:center;" onmouseover="this.style.background='var(--button)'" onmouseout="this.style.background='var(--surface)'" onclick="selectEmoji('${e}')">${e}</button>`
  ).join('');

  modal.classList.remove('hidden');
}

function closeEmojiPicker() {
  const modal = document.getElementById('emoji-picker-modal');
  if (modal) modal.classList.add('hidden');
}

function selectEmoji(emoji) {
  const hidden = document.getElementById('cal-cat-icon');
  const btn = document.getElementById('cal-cat-icon-btn');
  if (hidden) hidden.value = emoji;
  if (btn) btn.textContent = emoji;
  closeEmojiPicker();
}

// ====================================================================
// IDLE ФОТО — загрузка, просмотр, удаление
// ====================================================================
async function uploadIdleImages() {
  const input = document.getElementById('idle-images-file');
  if (!input || !input.files.length) return alert('Выберите файлы');
  for (const file of Array.from(input.files)) {
    try {
      const utf8Name = unescape(encodeURIComponent(file.name));
      const nameB64 = btoa(utf8Name);
      const res = await fetch(`${api}/upload?target=idle`, {
        method: 'POST',
        headers: { 'X-Filename-Base64': nameB64 },
        body: file
      });
      if (!res.ok) throw new Error(file.name);
    } catch (err) {
      console.error('uploadIdleImages', err);
      alert('Ошибка при загрузке: ' + err.message);
    }
  }
  alert('Фото загружены');
  input.value = '';
  await loadIdleImages();
}

async function loadIdleImages() {
  try {
    const res = await fetch(`${api}/list?target=idle`);
    if (!res.ok) return;
    const data = await res.json();
    const list = document.getElementById('idle-images-list');
    if (!list) return;
    const files = data.files || [];
    if (!files.length) {
      list.innerHTML = '<p class="hint">Нет загруженных фото</p>';
      return;
    }
    list.innerHTML = files.map(f => `
      <div class="idle-image-card">
        <img src="${api}/download?target=idle&name=${encodeURIComponent(f.name)}" alt="${escapeHtml(f.name)}">
        <div class="idle-image-info">
          <span class="idle-image-name">${escapeHtml(f.name)}</span>
          <button class="danger" onclick="deleteIdleImage('${escapeHtml(f.name)}')" style="padding:4px 10px;margin:0;font-size:12px;">🗑</button>
        </div>
      </div>
    `).join('');
  } catch (err) {
    console.error('loadIdleImages', err);
  }
}

async function deleteIdleImage(name) {
  if (!confirm(`Удалить фото ${name}?`)) return;
  try {
    const utf8Name = unescape(encodeURIComponent(name));
    const nameB64 = btoa(utf8Name);
    const res = await fetch(`${api}/delete?target=idle`, {
      method: 'GET',
      headers: { 'X-Filename-Base64': nameB64 }
    });
    if (!res.ok) return alert('Ошибка удаления');
    await loadIdleImages();
  } catch (err) {
    alert('Ошибка: ' + err.message);
  }
}

async function createCategory() {
    const name = document.getElementById('new-category-name').value.trim();
    if (!name) return alert("Введите имя категории!");

    const id = name.toLowerCase().replace(/\s+/g, "_");

    const res = await fetch(`${api}/media/category/add`, { 
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ id, name })
    });

    if (!res.ok) {
        alert("Ошибка создания категории");
        return;
    }

    alert("Категория создана");

    document.getElementById('new-category-name').value = "";

    // 🔥 Обновляем оба селектора
    await loadCategoriesAndBuildUI();  
    await loadPostCategorySelector();  
}


async function renameCategoryPrompt() {
    const sel = document.getElementById("category-select");
    const oldId = sel.value;
    const oldName = sel.selectedOptions[0].textContent;

    const newName = prompt("Новое имя категории:", oldName);
    if (!newName || !newName.trim()) return;

    const newId = newName.trim().toLowerCase().replace(/\s+/g, "_");

    const res = await fetch(`${api}/media/category/rename`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ oldId, newId, newName })
    });

    if (!res.ok) {
        alert("Ошибка переименования категории");
        return;
    }

    alert("Категория переименована");
    await loadCategoriesAndBuildUI();
    await loadPostCategorySelector();
}


async function deleteCategory() {
    const sel = document.getElementById("category-select");
    const id = sel.value;
    const name = sel.selectedOptions[0].textContent;

    if (!confirm(`Удалить категорию "${name}" вместе со всеми постами?`))
        return;

    const res = await fetch(`${api}/media/category/delete?id=${encodeURIComponent(id)}`, {
        method: "GET"
    });

    if (!res.ok) {
        alert("Ошибка удаления категории");
        return;
    }

    alert("Категория удалена");
    await loadCategoriesAndBuildUI();
    await loadPostCategorySelector();
}


async function loadStorageInfo() {
    try {
        const res = await fetch("/api/storage");
        if (!res.ok) throw new Error("storage api error");

        const data = await res.json();

        document.getElementById("disk-total").textContent =
            `${data.disk.name} — ${data.disk.total}`;

        document.getElementById("disk-used").textContent = `${data.disk.used} (${Math.round((data.disk.usedBytes / data.disk.totalBytes) * 100)}%)`;
        document.getElementById("disk-free").textContent = data.disk.free;
        document.getElementById("data-size").textContent = data.dataFolder.size;
        const mediaSizeEl = document.getElementById("media-size");
        if (mediaSizeEl) mediaSizeEl.textContent = data.mediaFolder?.size || "—";
        const newsSizeEl = document.getElementById("news-size");
        if (newsSizeEl) newsSizeEl.textContent = data.newsFolder?.size || "—";

        const perfProcRamEl = document.getElementById("perf-process-ram");
        if (perfProcRamEl) perfProcRamEl.textContent = data.performance?.processWorkingSet || "—";

        const perfManagedRamEl = document.getElementById("perf-managed-ram");
        if (perfManagedRamEl) perfManagedRamEl.textContent = data.performance?.managedMemory || "—";

        const perfCpuEl = document.getElementById("perf-cpu");
        if (perfCpuEl) {
            const cpu = Number(data.performance?.cpuUsagePercent || 0);
            perfCpuEl.textContent = cpu > 0 ? `${cpu.toFixed(1)}%` : "—";
        }

        const appProcessEl = document.getElementById("app-process");
        if (appProcessEl) appProcessEl.textContent = data.app?.process || "—";

        const appPidEl = document.getElementById("app-pid");
        if (appPidEl) appPidEl.textContent = data.app?.pid ?? "—";

        const appBaseDirEl = document.getElementById("app-base-dir");
        if (appBaseDirEl) appBaseDirEl.textContent = data.app?.baseDir || "—";

    } catch (e) {
        console.warn("Storage info unavailable", e);
    }
}


// ===============================
// UPDATE UI
// ===============================

const updateLogEl = document.getElementById("updateLog");
const btnInstall = document.getElementById("btnInstallUpdate");
const currentVersionEl = document.getElementById("currentVersion");
const latestVersionEl = document.getElementById("latestVersion");

let logTimer = null;

// -------------------------------
// helpers
// -------------------------------
function appendUpdateLog(text) {
    updateLogEl.textContent += "\n" + text;
    updateLogEl.scrollTop = updateLogEl.scrollHeight;
}

function clearUpdateLog() {
    updateLogEl.textContent = "";
}

function setInstallEnabled(enabled) {
    btnInstall.disabled = !enabled;
}

// -------------------------------
// LOAD STATUS (при открытии)
// -------------------------------
async function loadUpdateStatus() {
    try {
        const res = await fetch("/api/update/status");
        const json = await res.json();
        const data = json?.data || json;

        currentVersionEl.textContent = data.currentVersion || "—";
        latestVersionEl.textContent = data.latestVersion || "—";
        setInstallEnabled(data.updateAvailable === true);

        const appBaseEl = document.getElementById("update-app-base-dir");
        const installDirEl = document.getElementById("update-install-dir");
        if (appBaseEl) appBaseEl.textContent = data.paths?.appBaseDir || "—";
        if (installDirEl) installDirEl.textContent = data.paths?.installDirFromConfig || "—";

        const rel = data.githubRelease || {};
        const relNameEl = document.getElementById("update-release-name");
        const relTagEl = document.getElementById("update-release-tag");
        const relUrlEl = document.getElementById("update-release-url");
        const relNotesEl = document.getElementById("update-release-notes");

        if (relNameEl) relNameEl.textContent = rel.name || "—";
        if (relTagEl) relTagEl.textContent = rel.tag || "—";
        if (relUrlEl) {
            relUrlEl.textContent = rel.url || "—";
            relUrlEl.href = rel.url || "#";
        }
        if (relNotesEl) relNotesEl.textContent = rel.body || "Информация о релизе недоступна";

        if (data.paths?.appBaseDir && data.paths?.installDirFromConfig) {
            const normA = String(data.paths.appBaseDir).replace(/[\/]+$/, "").toLowerCase();
            const normB = String(data.paths.installDirFromConfig).replace(/[\/]+$/, "").toLowerCase();
            if (normA !== normB) {
                appendUpdateLog("⚠ Внимание: путь приложения и InstallDir updater различаются. Перед запуском обновления конфиг updater синхронизируется автоматически.");
            }
        }
    } catch (e) {
        appendUpdateLog("❌ Не удалось получить статус обновления");
    }
}

// -------------------------------
// CHECK
// -------------------------------
async function checkUpdate() {
    clearUpdateLog();
    appendUpdateLog("🔍 Проверка обновлений...");

    try {
        const res = await fetch("/api/update/check", {
            method: "POST"
        });

        const json = await res.json();
        const data = json?.data || json;

        currentVersionEl.textContent = data.currentVersion || "—";
        latestVersionEl.textContent = data.latestVersion || "—";

        if (data.updateAvailable) {
            appendUpdateLog("🆕 Доступно обновление");
            setInstallEnabled(true);
        } else {
            appendUpdateLog("✅ Установлена последняя версия");
            setInstallEnabled(false);
        }
    } catch (e) {
        appendUpdateLog("❌ Ошибка проверки обновлений");
    }
}




// -------------------------------
// INSTALL
// -------------------------------
async function installUpdate() {
    clearUpdateLog();
    appendUpdateLog("⬇ Запуск обновления...");

    setInstallEnabled(false);
    startLogPolling();

    try {
        const res = await fetch("/api/update/install", { method: "POST" });
        const json = await res.json();

        if (json.ok === true || json.status === "ok") {
            appendUpdateLog("✅ Обновление завершено");
            await loadUpdateStatus(); // обновим версии
        } else {
            appendUpdateLog("❌ Ошибка обновления: " + (json.msg || json.message || "неизвестно"));
        }
    } catch (e) {
        appendUpdateLog("❌ Не удалось запустить обновление");
    } finally {
        stopLogPolling(); // 🔥 ВАЖНО
    }
}


// -------------------------------
// ROLLBACK
// -------------------------------
async function rollbackUpdate() {
    if (!confirm("Откатить приложение к предыдущей версии?")) return;

    clearUpdateLog();
    appendUpdateLog("↩ Запуск отката версии...");

    startLogPolling();

    try {
        const res = await fetch("/api/update/rollback", { method: "POST" });
        const json = await res.json();

        if (json.ok === true || json.status === "ok") {
            appendUpdateLog("✅ Откат завершён");
            await loadUpdateStatus();
        } else {
            appendUpdateLog("❌ Ошибка отката: " + (json.msg || json.message || "неизвестно"));
        }
    } catch (e) {
        appendUpdateLog("❌ Не удалось запустить rollback");
    } finally {
        stopLogPolling(); // 🔥
    }
}


// -------------------------------
// LOG POLLING
// -------------------------------
function startLogPolling() {
    if (logTimer) return;

    logTimer = setInterval(async () => {
        try {
            const res = await fetch("/api/update/log");
            const text = await res.text();

            updateLogEl.textContent = text;
            updateLogEl.scrollTop = updateLogEl.scrollHeight;
        } catch {
            // сервер может временно упасть — молчим
        }
    }, 1500);
}

function stopLogPolling() {
    if (logTimer) {
        clearInterval(logTimer);
        logTimer = null;
    }
}

// -------------------------------
// INIT
// -------------------------------
document.addEventListener("DOMContentLoaded", () => {
    loadUpdateStatus();
    loadPluginsList();
    loadStorageInfo();
    initBellUI();
    loadCalendarCategories();
    loadIdleImages();
    loadScheduleInfo();

    // Enter в поле ввода нового сообщения бегущей строки — добавляет сообщение.
    const tickerInput = document.getElementById('ticker-new-item');
    if (tickerInput) {
      tickerInput.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') {
          e.preventDefault();
          addTickerItem();
        }
      });
    }
});

// ====================================================================
// ВЫХОД ИЗ АДМИНКИ
// ====================================================================
function logoutAdmin() {
  if (!confirm("Выйти из админки?")) return;
  adminToken = "";
  sessionStorage.removeItem("adminToken");
  location.reload();
}

// ====================================================================
// ТЕМЫ — пресеты, color picker, сохранение кастомных
// ====================================================================
function setupThemePresets() {
  const presets = document.querySelectorAll('.theme-preset');
  presets.forEach(btn => {
    btn.addEventListener('click', () => {
      const bg = btn.dataset.bg;
      const panel = btn.dataset.panel;
      const accent = btn.dataset.accent;
      const text = btn.dataset.text;

      // Заполняем color picker'ы.
      const setVal = (id, val) => { const el = document.getElementById(id); if (el) el.value = val; };
      setVal('theme-bg-color', bg);
      setVal('theme-panel-color', panel);
      setVal('theme-accent-color', accent);
      setVal('theme-text-color', text);

      // Подсветка выбранного пресета.
      presets.forEach(p => p.classList.remove('selected'));
      btn.classList.add('selected');
    });
  });

  // Загружаем сохранённые темы.
  loadSavedThemes();
}

async function saveThemeSettings() {
  try {
    const res = await fetch(`${api}/config`);
    if (!res.ok) return alert("Ошибка чтения config");
    const cfg = await res.json();

    // InterfaceSettings
    cfg.interfaceSettings = cfg.interfaceSettings || cfg.InterfaceSettings || {};
    cfg.InterfaceSettings = cfg.interfaceSettings;

    // Цвета из color picker'ов.
    const getVal = (id) => document.getElementById(id)?.value || '';
    cfg.interfaceSettings.backgroundColor = getVal('theme-bg-color');
    cfg.interfaceSettings.buttonBackground = getVal('theme-button-color');
    cfg.interfaceSettings.buttonForeground = getVal('theme-text-color');
    cfg.interfaceSettings.navigationButtonBackground = getVal('theme-button-color');
    cfg.interfaceSettings.navigationButtonForeground = getVal('theme-text-color');

    // Шрифты.
    cfg.interfaceSettings.fontFamily = getVal('theme-font-family') || 'Roboto';
    cfg.interfaceSettings.fontSize = Number(getVal('theme-font-size') || 16);
    cfg.interfaceSettings.navigationButtonFontSize = Number(getVal('theme-nav-font-size') || 15);

    // Доп. поля для accent/panel/border (киоск применит через CSS).
    cfg.interfaceSettings.accentColor = getVal('theme-accent-color');
    cfg.interfaceSettings.panelColor = getVal('theme-panel-color');
    cfg.interfaceSettings.borderColor = getVal('theme-border-color');

    const save = await fetch(`${api}/config`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(cfg)
    });
    if (!save.ok) return alert("Ошибка сохранения темы");
    alert("Тема применена. Киоск обновится.");
  } catch (err) {
    console.error("saveThemeSettings", err);
    alert("Ошибка: " + err.message);
  }
}

function loadSavedThemes() {
  try {
    const saved = JSON.parse(localStorage.getItem('infokiosk.savedThemes') || '[]');
    const container = document.getElementById('saved-themes-list');
    if (!container) return;
    if (!saved.length) {
      container.innerHTML = '<p class="hint">Нет сохранённых тем</p>';
      return;
    }
    container.innerHTML = saved.map((t, i) => `
      <div class="saved-theme-card">
        <span class="saved-theme-name">${escapeHtml(t.name)}</span>
        <div class="saved-theme-actions">
          <button onclick="applySavedTheme(${i})">Применить</button>
          <button class="danger" onclick="deleteSavedTheme(${i})">🗑</button>
        </div>
      </div>
    `).join('');
  } catch (e) { console.warn('loadSavedThemes', e); }
}

function saveCustomTheme() {
  const name = document.getElementById('custom-theme-name')?.value?.trim();
  if (!name) return alert("Введите название темы");
  const getVal = (id) => document.getElementById(id)?.value || '';
  const theme = {
    name,
    bg: getVal('theme-bg-color'),
    panel: getVal('theme-panel-color'),
    accent: getVal('theme-accent-color'),
    text: getVal('theme-text-color'),
    button: getVal('theme-button-color'),
    border: getVal('theme-border-color'),
    fontFamily: getVal('theme-font-family'),
    fontSize: getVal('theme-font-size'),
    navFontSize: getVal('theme-nav-font-size'),
  };
  try {
    const saved = JSON.parse(localStorage.getItem('infokiosk.savedThemes') || '[]');
    saved.push(theme);
    localStorage.setItem('infokiosk.savedThemes', JSON.stringify(saved));
    document.getElementById('custom-theme-name').value = '';
    loadSavedThemes();
    alert("Тема сохранена");
  } catch (e) { alert("Ошибка: " + e.message); }
}

function applySavedTheme(idx) {
  try {
    const saved = JSON.parse(localStorage.getItem('infokiosk.savedThemes') || '[]');
    const t = saved[idx];
    if (!t) return;
    const setVal = (id, val) => { const el = document.getElementById(id); if (el && val) el.value = val; };
    setVal('theme-bg-color', t.bg);
    setVal('theme-panel-color', t.panel);
    setVal('theme-accent-color', t.accent);
    setVal('theme-text-color', t.text);
    setVal('theme-button-color', t.button);
    setVal('theme-border-color', t.border);
    setVal('theme-font-family', t.fontFamily);
    setVal('theme-font-size', t.fontSize);
    setVal('theme-nav-font-size', t.navFontSize);
    // Снимаем выделение пресетов.
    document.querySelectorAll('.theme-preset').forEach(p => p.classList.remove('selected'));
  } catch (e) { alert("Ошибка: " + e.message); }
}

function deleteSavedTheme(idx) {
  try {
    const saved = JSON.parse(localStorage.getItem('infokiosk.savedThemes') || '[]');
    saved.splice(idx, 1);
    localStorage.setItem('infokiosk.savedThemes', JSON.stringify(saved));
    loadSavedThemes();
  } catch (e) {}
}

// ====================================================================
// ЗАГРУЗКА ТЕМ ИЗ КОНФИГА В ПОЛЯ
// ====================================================================
// loadThemeAndExtraSettings уже существует, но добавим загрузку color picker'ов.
// Оборачиваем оригинальную функцию.

// ====================================================================
// СОХРАНЕНИЕ URL САЙТА ШКОЛЫ
// ====================================================================
async function saveSchoolSiteUrl() {
  try {
    const url = document.getElementById('schoolsite-url')?.value?.trim() || '';
    const getRes = await fetch(`${api}/config`);
    if (!getRes.ok) return alert("Ошибка");
    const cfg = await getRes.json();
    cfg.schoolSiteUrl = url;
    cfg.SchoolSiteUrl = url;
    const saveRes = await fetch(`${api}/config`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(cfg)
    });
    if (!saveRes.ok) return alert("Ошибка сохранения");
    alert("URL сайта сохранён");
  } catch (err) {
    alert("Ошибка: " + err.message);
  }
}

// ====================================================================
// PIN-код киоска = пароль администратора (объединены).
// Смена PIN-кода производится через changeAdminPassword
// в разделе "Безопасность" (использует /auth/admin/change-password).
// ====================================================================

// ====================================================================
// ЗВОНКИ — полная переработка
// ====================================================================
// Хранится в data/BellSchedule.json:
// { active: "id", variants: { id: { name, lessons: [{num,start,end}] } } }
//
// API endpoints:
//   GET  /bell/get     — читать весь объект
//   POST /bell/save    — сохранить весь объект (требует admin token)
//   POST /bell/activate — установить активный вариант

let bellData = null;
let currentBellVariant = 'default';

// Загрузка данных звонков с сервера.
async function loadBellData() {
  try {
    const res = await fetch(`${api}/bell/get`);
    if (!res.ok) {
      console.warn('[bell] /bell/get failed:', res.status);
      return null;
    }
    const data = await res.json();
    return data;
  } catch (e) {
    console.warn('[bell] loadBellData error:', e);
    return null;
  }
}

// Инициализация UI звонков — вызывается при переключении на вкладку.
async function initBellUI() {
  bellData = await loadBellData();
  if (!bellData) {
    bellData = { active: '', variants: {} };
  }
  if (!bellData.variants) bellData.variants = {};

  // Скрываем creator при инициализации.
  hideBellCreator();

  // Заполняем select активного расписания.
  renderBellActiveSelect();
  // Заполняем список сохранённых расписаний.
  renderBellVariantsList();
  // Обновляем сводку активного расписания.
  renderBellActiveSummary();
}

// Рендер select'а активного расписания.
function renderBellActiveSelect() {
  const sel = document.getElementById('bell-active-select');
  if (!sel) return;
  sel.innerHTML = '';
  const entries = Object.entries(bellData.variants || {});
  if (!entries.length) {
    const opt = document.createElement('option');
    opt.value = '';
    opt.textContent = '— нет сохранённых расписаний —';
    sel.appendChild(opt);
    return;
  }
  for (const [id, v] of entries) {
    const opt = document.createElement('option');
    opt.value = id;
    opt.textContent = v.name || id;
    if (id === bellData.active) opt.selected = true;
    sel.appendChild(opt);
  }
}

// Рендер списка сохранённых расписаний (карточки).
function renderBellVariantsList() {
  const list = document.getElementById('bell-variants-list');
  if (!list) return;
  list.innerHTML = '';
  const entries = Object.entries(bellData.variants || {});
  if (!entries.length) {
    list.innerHTML = '<p class="hint">Нет сохранённых расписаний. Создайте первое кнопкой ниже.</p>';
    return;
  }
  entries.forEach(([id, v]) => {
    const isActive = id === bellData.active;
    const lessons = v.lessons || [];
    const lessonsCount = lessons.length;
    const firstLesson = lessons[0];
    const lastLesson = lessons[lessonsCount - 1];
    const timeRange = firstLesson && lastLesson
      ? `${firstLesson.start}–${lastLesson.end}`
      : '—';
    const card = document.createElement('div');
    card.className = 'news-card';
    card.style.marginBottom = '10px';
    card.innerHTML = `
      <div class="news-card-title">${escapeHtml(v.name || id)} ${isActive ? '<span class="np-meta-status published">активно</span>' : ''}</div>
      <div class="news-card-meta">Уроков: ${lessonsCount} • ${timeRange}</div>
      <div class="news-card-actions" style="margin-top:8px;">
        <button class="small-btn" onclick="editBellVariant('${escapeHtml(id)}')">✏️ Редактировать</button>
        ${isActive ? '' : `<button class="small-btn active" onclick="activateBellVariant('${escapeHtml(id)}')">✅ Применить</button>`}
        <button class="small-btn danger" onclick="deleteBellVariantById('${escapeHtml(id)}')">🗑 Удалить</button>
      </div>
    `;
    list.appendChild(card);
  });
}

// Сводка активного расписания (краткая информация под select'ом).
function renderBellActiveSummary() {
  const el = document.getElementById('bell-active-summary');
  if (!el) return;
  const activeId = bellData.active;
  if (!activeId || !bellData.variants[activeId]) {
    el.textContent = 'Активное расписание не выбрано.';
    return;
  }
  const v = bellData.variants[activeId];
  const lessons = v.lessons || [];
  const count = lessons.length;
  const first = lessons[0];
  const last = lessons[count - 1];
  const range = first && last ? `${first.start}–${last.end}` : '—';
  el.textContent = `${v.name || activeId}: ${count} ${count === 1 ? 'урок' : 'уроков'}, ${range}`;
}

// Применение расписания, выбранного в select.
async function applyBellVariant() {
  const sel = document.getElementById('bell-active-select');
  if (!sel || !sel.value) {
    alert('Нет сохранённых расписаний. Создайте новое.');
    return;
  }
  const id = sel.value;
  await activateBellVariant(id);
}

// Активация расписания по id (через /bell/activate).
async function activateBellVariant(id) {
  try {
    const res = await fetch(`${api}/bell/activate`, {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-Admin-Token": adminToken },
      body: JSON.stringify({ id })
    });
    if (!res.ok) {
      const err = await res.text().catch(() => '');
      return alert('Ошибка активации: ' + err);
    }
    bellData.active = id;
    renderBellActiveSelect();
    renderBellVariantsList();
    renderBellActiveSummary();
    alert('Расписание активировано: ' + (bellData.variants[id]?.name || id));
  } catch (e) {
    alert('Ошибка: ' + e.message);
  }
}

// Показать шаблон создания нового расписания.
function showBellCreator() {
  // Очищаем поля.
  document.getElementById('bell-variant-name').value = '';
  document.getElementById('bell-template-count').value = '7';
  document.getElementById('bell-template-start').value = '08:30';
  document.getElementById('bell-template-lesson').value = '45';
  document.getElementById('bell-template-break').value = '10';
  document.getElementById('bell-template-big-after').value = '3';
  document.getElementById('bell-template-big-break').value = '20';
  // Готовим временный variant для creator.
  currentBellVariant = '__new__';
  if (!bellData.variants.__new__) {
    bellData.variants.__new__ = { name: '', lessons: [] };
  } else {
    bellData.variants.__new__.lessons = [];
    bellData.variants.__new__.name = '';
  }
  renderBellLessons([]);
  // Показываем форму.
  document.getElementById('bell-creator').style.display = '';
  document.getElementById('bell-create-btn').style.display = 'none';
  // Прокручиваем к форме.
  document.getElementById('bell-creator')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
}

// Скрыть шаблон создания.
function hideBellCreator() {
  const el = document.getElementById('bell-creator');
  if (el) el.style.display = 'none';
  const btn = document.getElementById('bell-create-btn');
  if (btn) btn.style.display = '';
  // Чистим временный variant.
  if (bellData && bellData.variants && bellData.variants.__new__) {
    delete bellData.variants.__new__;
  }
  if (currentBellVariant === '__new__') currentBellVariant = '';
}

// Редактирование существующего расписания — открывает creator с данными.
function editBellVariant(id) {
  const v = bellData.variants[id];
  if (!v) return;
  currentBellVariant = id;
  // Заполняем поля.
  document.getElementById('bell-variant-name').value = v.name || '';
  renderBellLessons(v.lessons || []);
  // Показываем форму.
  document.getElementById('bell-creator').style.display = '';
  document.getElementById('bell-create-btn').style.display = 'none';
  document.getElementById('bell-creator')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
}

// Удаление расписания по id.
async function deleteBellVariantById(id) {
  if (!bellData.variants[id]) return;
  if (!confirm(`Удалить расписание "${bellData.variants[id].name || id}"?`)) return;
  delete bellData.variants[id];
  if (bellData.active === id) bellData.active = '';
  // Сохраняем обновлённый bellData на сервер.
  try {
    const res = await fetch(`${api}/bell/save`, {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-Admin-Token": adminToken },
      body: JSON.stringify(bellData)
    });
    if (!res.ok) return alert('Ошибка сохранения');
  } catch (e) {
    return alert('Ошибка: ' + e.message);
  }
  renderBellActiveSelect();
  renderBellVariantsList();
  renderBellActiveSummary();
}

// Рендер списка уроков в редакторе.
function renderBellLessons(lessons) {
  const list = document.getElementById('bell-lessons-list');
  if (!list) return;
  list.innerHTML = '';
  if (!lessons || !lessons.length) {
    list.innerHTML = '<p class="hint">Нет уроков. Добавьте вручную или сгенерируйте по шаблону.</p>';
    return;
  }
  lessons.forEach((les, i) => {
    const div = document.createElement('div');
    div.className = 'bell-lesson-row';
    div.innerHTML = `
      <span class="bell-lesson-num">${les.num || i + 1}</span>
      <input type="time" value="${les.start || ''}" data-field="start" data-idx="${i}">
      <span>—</span>
      <input type="time" value="${les.end || ''}" data-field="end" data-idx="${i}">
      <button class="danger" onclick="removeBellLesson(${i})" style="padding:6px 10px;margin:0;">✕</button>
    `;
    list.appendChild(div);
  });
}

// Генерация уроков по шаблону.
function generateBellTemplate() {
  if (!bellData) return;
  // Работаем с текущим variant (либо __new__, либо существующим).
  if (!bellData.variants[currentBellVariant]) {
    bellData.variants[currentBellVariant] = { name: '', lessons: [] };
  }
  const variant = bellData.variants[currentBellVariant];

  const count = parseInt(document.getElementById('bell-template-count')?.value || '7', 10);
  const startTime = document.getElementById('bell-template-start')?.value || '08:30';
  const lessonMin = parseInt(document.getElementById('bell-template-lesson')?.value || '45', 10);
  const breakMin = parseInt(document.getElementById('bell-template-break')?.value || '10', 10);
  const bigAfter = parseInt(document.getElementById('bell-template-big-after')?.value || '0', 10);
  const bigBreakMin = parseInt(document.getElementById('bell-template-big-break')?.value || '20', 10);

  const [startH, startM] = startTime.split(':').map(Number);
  let cursor = startH * 60 + startM;

  const lessons = [];
  for (let i = 1; i <= count; i++) {
    const lesStart = cursor;
    const lesEnd = cursor + lessonMin;
    lessons.push({ num: i, start: fmtBellMin(lesStart), end: fmtBellMin(lesEnd) });
    cursor = lesEnd;
    if (i === bigAfter) {
      cursor += bigBreakMin;
    } else if (i < count) {
      cursor += breakMin;
    }
  }

  variant.lessons = lessons;
  renderBellLessons(lessons);
}

function fmtBellMin(min) {
  const h = Math.floor(min / 60);
  const m = min % 60;
  return String(h).padStart(2, '0') + ':' + String(m).padStart(2, '0');
}

// Добавление урока вручную.
function addBellLesson() {
  if (!bellData) return;
  const variant = bellData.variants[currentBellVariant];
  if (!variant) return;
  if (!variant.lessons) variant.lessons = [];
  const prev = variant.lessons[variant.lessons.length - 1];
  let start = '08:30';
  let end = '09:15';
  if (prev) {
    const [peH, peM] = prev.end.split(':').map(Number);
    const breakMin = 10;
    const newStart = peH * 60 + peM + breakMin;
    const [psH, psM] = prev.start.split(':').map(Number);
    const prevDur = (peH * 60 + peM) - (psH * 60 + psM);
    const newEnd = newStart + (prevDur > 0 ? prevDur : 45);
    start = fmtBellMin(newStart);
    end = fmtBellMin(newEnd);
  }
  variant.lessons.push({ num: variant.lessons.length + 1, start, end });
  renderBellLessons(variant.lessons);
}

function removeBellLesson(idx) {
  if (!bellData) return;
  const variant = bellData.variants[currentBellVariant];
  if (!variant || !variant.lessons) return;
  variant.lessons.splice(idx, 1);
  variant.lessons.forEach((l, i) => l.num = i + 1);
  renderBellLessons(variant.lessons);
}

// Сохранение расписания (нового или отредактированного).
// Берёт данные из полей creator'а, сохраняет в bellData и на сервер.
// Новое расписание НЕ становится активным автоматически — для активации
// пользователь должен выбрать его в select и нажать "Применить".
async function saveBellVariant() {
  try {
    if (!bellData) {
      alert('Нет данных для сохранения');
      return;
    }

    // Собираем название.
    const nameEl = document.getElementById('bell-variant-name');
    const name = (nameEl?.value || '').trim();
    if (!name) {
      alert('Введите название расписания');
      nameEl?.focus();
      return;
    }

    // Работаем с текущим variant (новый или существующий).
    let variant = bellData.variants[currentBellVariant];
    if (!variant) {
      // Такого не должно быть, но на всякий случай.
      variant = { name, lessons: [] };
      bellData.variants[currentBellVariant] = variant;
    }
    variant.name = name;

    // Собираем уроки из input'ов.
    if (!variant.lessons) variant.lessons = [];
    variant.lessons.forEach((les, i) => {
      const startEl = document.querySelector(`#bell-lessons-list input[data-field="start"][data-idx="${i}"]`);
      const endEl = document.querySelector(`#bell-lessons-list input[data-field="end"][data-idx="${i}"]`);
      if (startEl) les.start = startEl.value;
      if (endEl) les.end = endEl.value;
    });

    if (!variant.lessons.length) {
      if (!confirm('В расписании нет уроков. Сохранить пустое расписание?')) return;
    }

    // Если это было новое расписание — генерируем постоянный id.
    if (currentBellVariant === '__new__') {
      const newId = 'variant_' + Date.now();
      bellData.variants[newId] = variant;
      delete bellData.variants.__new__;
      currentBellVariant = newId;
    }

    // Сохраняем bellData на сервер (без изменения active).
    const res = await fetch(`${api}/bell/save`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-Admin-Token": adminToken
      },
      body: JSON.stringify(bellData)
    });

    if (!res.ok) {
      const errText = await res.text().catch(() => '');
      alert('Ошибка сохранения: ' + errText);
      return;
    }

    const result = await res.json();
    if (!result.ok && result.ok !== undefined && result.error) {
      alert('Ошибка: ' + result.error);
      return;
    }

    alert('Расписание сохранено: ' + name);
    hideBellCreator();
    renderBellActiveSelect();
    renderBellVariantsList();
    renderBellActiveSummary();
  } catch (err) {
    console.error('saveBellVariant', err);
    alert('Ошибка: ' + err.message);
  }
}

// ====================================================================
// РАСШИРЕНИЯ — управление плагинами киоска
// ====================================================================
// Источник данных: GET /extensions/list (возвращает массив с манифестом + состоянием).
// Сохранение: POST /extensions/toggle, /extensions/save-settings.
// Установка: POST /extensions/install с { url }.

let extensionsState = []; // кэш списка расширений

async function loadPluginsList() {
  await loadExtensionsList();
}

async function loadExtensionsList() {
  try {
    const container = document.getElementById('extensions-list');
    if (!container) return;
    container.innerHTML = '<p class="hint">Загрузка…</p>';

    const res = await fetch(`${api}/extensions/list`, {
      headers: { "X-Admin-Token": adminToken }
    });
    if (!res.ok) {
      container.innerHTML = '<p class="hint">Ошибка загрузки списка расширений</p>';
      return;
    }
    extensionsState = await res.json();
    renderExtensionsGrid();
  } catch (e) {
    console.warn('loadExtensionsList', e);
    const container = document.getElementById('extensions-list');
    if (container) container.innerHTML = '<p class="hint">Ошибка: ' + escapeHtml(e.message) + '</p>';
  }
}

function renderExtensionsGrid() {
  const container = document.getElementById('extensions-list');
  if (!container) return;
  container.innerHTML = '';
  if (!extensionsState || !extensionsState.length) {
    container.innerHTML = '<p class="hint">Нет установленных расширений. Установите первое выше.</p>';
    return;
  }

  extensionsState.forEach(ext => {
    const card = document.createElement('div');
    card.className = 'extension-card' + (ext.enabled ? '' : ' disabled');
    card.dataset.id = ext.id;

    const settingsFields = ext.settingsFields || [];
    const hasSettings = ext.hasSettings && settingsFields.length > 0;

    card.innerHTML = `
      <div class="extension-card-header">
        <div class="extension-card-icon">${escapeHtml(ext.icon || '🔌')}</div>
        <div class="extension-card-info">
          <div class="extension-card-name">
            ${escapeHtml(ext.name || ext.id)}
            ${ext.version ? `<span class="extension-card-version">v${escapeHtml(ext.version)}</span>` : ''}
            ${ext.enabled ? '' : '<span class="extension-card-version" style="background:rgba(220,53,69,0.15);color:#ff6b6b;">выключено</span>'}
          </div>
          <div class="extension-card-desc">${escapeHtml(ext.description || 'Без описания')}</div>
        </div>
      </div>

      <div class="extension-card-controls">
        <label class="extension-toggle">
          <input type="checkbox" ${ext.enabled ? 'checked' : ''} onchange="toggleExtension('${escapeHtml(ext.id)}', this.checked)">
          <span class="extension-toggle-slider"></span>
          <span>${ext.enabled ? 'Включено' : 'Выключено'}</span>
        </label>
        <div class="extension-card-actions">
          <button class="small-btn" onclick="toggleExtensionDetails('${escapeHtml(ext.id)}')" title="Настройки расширения">▼ Настройки</button>
        </div>
      </div>

      <div class="extension-card-details" id="ext-details-${escapeHtml(ext.id)}">
        <div class="extension-meta-row">
          ${ext.author ? `<div><strong>Автор:</strong> ${escapeHtml(ext.author)}</div>` : ''}
          <div><strong>ID:</strong> <code>${escapeHtml(ext.id)}</code></div>
          <div><strong>Версия:</strong> ${escapeHtml(ext.version || '1.0.0')}</div>
          <div><strong>view.js:</strong> ${ext.hasViewJs ? '✅' : '❌'}</div>
          <div><strong>view.css:</strong> ${ext.hasViewCss ? '✅' : '—'}</div>
        </div>

        <div class="extension-detail-row">
          <label>Положение в киоске:</label>
          <select class="extension-position-select" id="ext-position-${escapeHtml(ext.id)}">
            <option value="start" ${ext.position === 'start' ? 'selected' : ''}>В начале (перед расписанием)</option>
            <option value="after-schedule" ${ext.position === 'after-schedule' ? 'selected' : ''}>После расписания</option>
            <option value="after-news" ${ext.position === 'after-news' ? 'selected' : ''}>После новостей</option>
            <option value="after-calendar" ${ext.position === 'after-calendar' ? 'selected' : ''}>После календаря</option>
            <option value="end" ${ext.position === 'end' || !ext.position ? 'selected' : ''}>В конце</option>
          </select>
        </div>

        <div class="extension-detail-row">
          <label class="checkbox-label">
            <input type="checkbox" id="ext-showInMore-${escapeHtml(ext.id)}" ${ext.showInMore ? 'checked' : ''}>
            <span>📋 Показывать в разделе «Дополнительно»</span>
          </label>
          <p class="hint" style="margin:4px 0 0 28px;">Расширение будет скрыто из основного сайдбара и доступно через кнопку «Дополнительно». Для развлечений и редко используемых функций.</p>
        </div>

        <div class="extension-detail-row">
          <label class="checkbox-label">
            <input type="checkbox" id="ext-blockDuringLesson-${escapeHtml(ext.id)}" ${ext.blockDuringLesson ? 'checked' : ''}>
            <span>🚫 Не использовать во время урока</span>
          </label>
          <p class="hint" style="margin:4px 0 0 28px;">Расширение будет заблокировано, когда идёт урок по расписанию звонков. Ученики увидят экран «Доступно только на перемене».</p>
        </div>

        ${hasSettings ? `
          <div class="extension-detail-row">
            <label>Настройки расширения:</label>
            <div id="ext-settings-${escapeHtml(ext.id)}">
              ${settingsFields.map(f => renderExtensionField(f, ext.settings || {})).join('')}
            </div>
          </div>
        ` : '<p class="hint" style="margin:6px 0;">У этого расширения нет настраиваемых полей.</p>'}

        <div class="modal-actions-row" style="margin-top:12px;padding-top:10px;">
          <button onclick="saveExtensionSettings('${escapeHtml(ext.id)}')">💾 Сохранить настройки</button>
          ${ext.category === 'game' || ext.id === 'snake' || ext.id === 'game-2048' || ext.id === 'math-quiz'
            ? `<button class="danger" onclick="resetExtensionLeaderboard('${escapeHtml(ext.id)}')">🗑 Сбросить рекорды</button>`
            : ''}
        </div>
      </div>
    `;
    container.appendChild(card);
  });
}

// Рендер одного поля настройки расширения.
function renderExtensionField(field, settings) {
  const value = settings[field.key] !== undefined ? settings[field.key] : (field.default !== undefined ? field.default : '');
  const required = field.required ? ' <span style="color:#ff6b6b;">*</span>' : '';
  const help = field.help ? `<p class="hint" style="margin:4px 0 0;">${escapeHtml(field.help)}</p>` : '';

  if (field.type === 'checkbox') {
    const checked = value === true || value === 'true' ? 'checked' : '';
    return `
      <div style="margin-bottom:10px;">
        <label class="checkbox-label">
          <input type="checkbox" id="ext-field-${escapeHtml(field.key)}" ${checked}>
          <span>${escapeHtml(field.label || field.key)}${required}</span>
        </label>
        ${help}
      </div>`;
  }

  if (field.type === 'textarea') {
    return `
      <div style="margin-bottom:10px;">
        <label style="margin-top:0;">${escapeHtml(field.label || field.key)}${required}</label>
        <textarea id="ext-field-${escapeHtml(field.key)}" placeholder="${escapeHtml(field.placeholder || '')}">${escapeHtml(String(value ?? ''))}</textarea>
        ${help}
      </div>`;
  }

  if (field.type === 'select') {
    const options = (field.options || []).map(opt =>
      `<option value="${escapeHtml(opt.value)}" ${String(value) === String(opt.value) ? 'selected' : ''}>${escapeHtml(opt.label || opt.value)}</option>`
    ).join('');
    return `
      <div style="margin-bottom:10px;">
        <label style="margin-top:0;">${escapeHtml(field.label || field.key)}${required}</label>
        <select id="ext-field-${escapeHtml(field.key)}" style="width:100%;">${options}</select>
        ${help}
      </div>`;
  }

  // text / number
  const inputType = field.type === 'number' ? 'number' : 'text';
  const minAttr = field.min !== undefined ? `min="${field.min}"` : '';
  const maxAttr = field.max !== undefined ? `max="${field.max}"` : '';
  return `
    <div style="margin-bottom:10px;">
      <label style="margin-top:0;">${escapeHtml(field.label || field.key)}${required}</label>
      <input type="${inputType}" id="ext-field-${escapeHtml(field.key)}" placeholder="${escapeHtml(field.placeholder || '')}" value="${escapeHtml(String(value ?? ''))}" ${minAttr} ${maxAttr}>
      ${help}
    </div>`;
}

// Раскрыть/скрыть детали расширения.
function toggleExtensionDetails(id) {
  const el = document.getElementById('ext-details-' + id);
  if (!el) return;
  el.classList.toggle('open');
  // Меняем текст кнопки.
  const card = el.closest('.extension-card');
  const btn = card?.querySelector('.extension-card-actions button');
  if (btn) {
    btn.textContent = el.classList.contains('open') ? '▲ Свернуть' : '▼ Настройки';
  }
}

// Включение/выключение расширения.
async function toggleExtension(id, enabled) {
  try {
    const res = await fetch(`${api}/extensions/toggle`, {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-Admin-Token": adminToken },
      body: JSON.stringify({ id, enabled })
    });
    if (!res.ok) return alert('Ошибка переключения расширения');
    // Обновляем локальный стейт.
    const ext = extensionsState.find(e => e.id === id);
    if (ext) ext.enabled = enabled;
    renderExtensionsGrid();
    // Горячая перезагрузка в киоске.
    await reloadExtensionsInKiosk();
  } catch (e) {
    alert('Ошибка: ' + e.message);
  }
}

// Сохранение настроек расширения (position + fields + showInMore + blockDuringLesson).
async function saveExtensionSettings(id) {
  const ext = extensionsState.find(e => e.id === id);
  if (!ext) return;

  const positionEl = document.getElementById('ext-position-' + id);
  const position = positionEl?.value || 'end';

  const showInMoreEl = document.getElementById('ext-showInMore-' + id);
  const showInMore = showInMoreEl ? showInMoreEl.checked : false;

  const blockDuringLessonEl = document.getElementById('ext-blockDuringLesson-' + id);
  const blockDuringLesson = blockDuringLessonEl ? blockDuringLessonEl.checked : false;

  // Собираем значения полей.
  const settings = {};
  (ext.settingsFields || []).forEach(f => {
    const fieldEl = document.getElementById('ext-field-' + f.key);
    if (!fieldEl) return;
    if (f.type === 'checkbox') {
      settings[f.key] = fieldEl.checked;
    } else if (f.type === 'number') {
      settings[f.key] = fieldEl.value === '' ? null : Number(fieldEl.value);
    } else {
      settings[f.key] = fieldEl.value;
    }
  });

  try {
    const res = await fetch(`${api}/extensions/save-settings`, {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-Admin-Token": adminToken },
      body: JSON.stringify({ id, position, settings, showInMore, blockDuringLesson })
    });
    if (!res.ok) return alert('Ошибка сохранения настроек');
    // Обновляем локальный стейт.
    ext.position = position;
    ext.showInMore = showInMore;
    ext.blockDuringLesson = blockDuringLesson;
    ext.settings = settings;
    alert('Настройки расширения сохранены');
    // Горячая перезагрузка в киоске — чтобы позиция, showInMore и т.д.
    // применились сразу без перезапуска.
    await reloadExtensionsInKiosk();
  } catch (e) {
    alert('Ошибка: ' + e.message);
  }
}

// Установка расширения из GitHub/zip.
async function installExtensionFromGithub() {
  const urlInput = document.getElementById('ext-install-url');
  const statusEl = document.getElementById('ext-install-status');
  if (!urlInput) return;
  const url = (urlInput.value || '').trim();
  if (!url) return alert('Введите URL');

  if (statusEl) {
    statusEl.textContent = '⏳ Скачивание и установка…';
    statusEl.style.color = 'var(--accent)';
  }

  try {
    const res = await fetch(`${api}/extensions/install`, {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-Admin-Token": adminToken },
      body: JSON.stringify({ url })
    });
    const result = await res.json();
    if (!res.ok || !result.ok) {
      if (statusEl) {
        statusEl.textContent = '❌ ' + (result.error || 'Ошибка установки');
        statusEl.style.color = '#ff6b6b';
      } else {
        alert(result.error || 'Ошибка установки');
      }
      return;
    }
    // Горячая перезагрузка расширений в киоске — без перезапуска.
    await reloadExtensionsInKiosk();
    if (statusEl) {
      statusEl.textContent = `✅ Расширение "${result.name || result.id}" установлено и подключено к киоску.`;
      statusEl.style.color = '#4caf50';
    } else {
      alert(`Расширение "${result.name || result.id}" установлено и подключено.`);
    }
    urlInput.value = '';
    // Перезагружаем список.
    await loadExtensionsList();
  } catch (e) {
    if (statusEl) {
      statusEl.textContent = '❌ Ошибка: ' + e.message;
      statusEl.style.color = '#ff6b6b';
    } else {
      alert('Ошибка: ' + e.message);
    }
  }
}

// Горячая перезагрузка расширений в киоске — вызывает /extensions/reload,
// который пересканирует папку plugins/ и отправит push в киоск для
// перерисовки сайдбара. Не требует перезапуска киоска.
async function reloadExtensionsInKiosk() {
  try {
    const res = await fetch(`${api}/extensions/reload`, {
      method: "POST",
      headers: { "X-Admin-Token": adminToken }
    });
    if (!res.ok) {
      console.warn('[extensions] reload failed:', res.status);
      return;
    }
    const result = await res.json();
    console.log('[extensions] reloaded:', result);
  } catch (e) {
    console.warn('[extensions] reload error:', e);
  }
}

// Сброс таблицы рекордов игры.
async function resetExtensionLeaderboard(gameId) {
  if (!confirm(`Сбросить таблицу рекордов для "${gameId}"? Это действие нельзя отменить.`)) return;
  try {
    const res = await fetch(`${api}/leaderboard/reset`, {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-Admin-Token": adminToken },
      body: JSON.stringify({ game: gameId })
    });
    if (!res.ok) return alert('Ошибка сброса рекордов');
    alert('Таблица рекордов сброшена');
  } catch (e) {
    alert('Ошибка: ' + e.message);
  }
}

