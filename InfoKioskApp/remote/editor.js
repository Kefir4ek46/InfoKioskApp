const api = location.origin;
let editorToken = sessionStorage.getItem("editorToken") || "";
let editorLogin = sessionStorage.getItem("editorLogin") || "";
let editorName = sessionStorage.getItem("editorName") || "";
let editorCanPublish = false;

// Кэш новостей для предпросмотра по id.
// Заполняется при loadMyPending / loadMyPublished / loadModerateQueue.
const newsPreviewCache = {
  pending: [],
  published: [],
  rejected: [],
  moderate: []
};

function getDeviceId() {
  let id = localStorage.getItem("editorDeviceId");
  if (!id) {
    id = `${navigator.userAgent}-${Date.now()}-${Math.random().toString(16).slice(2)}`;
    localStorage.setItem("editorDeviceId", id);
  }
  return id;
}

window.addEventListener("load", async () => {
  const rememberedLogin = localStorage.getItem("editorRememberLogin") || "";
  if (rememberedLogin) {
    const loginModalInput = document.getElementById("editor-login-modal");
    if (loginModalInput) loginModalInput.value = rememberedLogin;
  }

  // Восстанавливаем флаг прав из sessionStorage.
  editorCanPublish = sessionStorage.getItem("editorCanPublish") === "1";
  applyEditorRightsUI();

  const modal = document.getElementById("editor-auth-modal");
  if (editorToken) {
    if (modal) modal.classList.add("hidden");
    const nameEl = document.getElementById("editor-name");
    const loginEl = document.getElementById("editor-login");
    if (nameEl) nameEl.value = editorName || "";
    if (loginEl) loginEl.value = editorLogin || "";
    document.getElementById("editor-auth-status").textContent = `Вошли как: ${editorName || editorLogin}`;
    // Подтягиваем актуальные права и профиль с сервера.
    await loadEditorInfo();
    await loadMyPending();
    return;
  }

  if (rememberedLogin) {
    const ok = await deviceLogin(rememberedLogin);
    if (ok) {
      if (modal) modal.classList.add("hidden");
      return;
    }
  }

  if (modal) modal.classList.remove("hidden");
});

async function deviceLogin(login) {
  const res = await fetch(`${api}/auth/editor/device-login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ login, deviceId: getDeviceId() })
  });
  if (!res.ok) return false;
  const data = await res.json();
  setEditorSession(data, login);
  await loadMyPending();
  return true;
}

function setEditorSession(data, fallbackLogin) {
  editorToken = data.token;
  editorLogin = data.login || fallbackLogin;
  editorName = data.name || "";
  editorCanPublish = !!data.canPublishWithoutApproval;
  sessionStorage.setItem("editorToken", editorToken);
  sessionStorage.setItem("editorLogin", editorLogin);
  sessionStorage.setItem("editorName", editorName);
  sessionStorage.setItem("editorCanPublish", editorCanPublish ? "1" : "0");
  applyEditorRightsUI();
  document.getElementById("editor-auth-status").textContent = `Вошли как: ${editorName || editorLogin}`;
  const nameEl = document.getElementById("editor-name");
  const loginEl = document.getElementById("editor-login");
  const nameModalEl = document.getElementById("editor-name-modal");
  if (nameEl) nameEl.value = editorName || "";
  if (loginEl) loginEl.value = editorLogin || "";
  if (nameModalEl) nameModalEl.value = editorName || "";
}

// Обновляет UI в зависимости от прав редактора.
// Если есть права на публикацию — показываем корону в шапке, вкладку "Модерация",
// меняем подсказку на кнопке отправки.
function applyEditorRightsUI() {
  const crown = document.getElementById("editor-crown-badge");
  const sidebarModerate = document.getElementById("sidebar-moderate");
  const newNewsHint = document.getElementById("new-news-hint");
  const rightsStatus = document.getElementById("editor-rights-status");

  if (crown) crown.style.display = editorCanPublish ? "" : "none";
  if (sidebarModerate) sidebarModerate.style.display = editorCanPublish ? "" : "none";

  if (editorCanPublish) {
    if (newNewsHint) newNewsHint.textContent = "Заполните поля. Новость будет опубликована автоматически (у вас есть права публикации без модерации).";
    if (rightsStatus) rightsStatus.innerHTML = "👑 У вас есть права на публикацию без модерации. Вы также можете публиковать новости других редакторов во вкладке «Модерация».";
  } else {
    if (newNewsHint) newNewsHint.textContent = "Заполните поля и отправьте на модерацию администратору.";
    if (rightsStatus) rightsStatus.textContent = "";
  }

  // Обновляем карточку пользователя в сайдбаре, шапке и профиле.
  updateUserDisplay();
}

// Возвращает инициалы из имени (до 2 символов).
function getInitials(name) {
  if (!name) return "?";
  const parts = name.trim().split(/\s+/).filter(Boolean);
  if (!parts.length) return "?";
  if (parts.length === 1) return parts[0].slice(0, 1).toUpperCase();
  return (parts[0][0] + parts[1][0]).toUpperCase();
}

// Обновляет все элементы отображения пользователя:
//   - шапка (subtitle "Вы вошли как ...")
//   - аватар в шапке
//   - карточка пользователя в сайдбаре
//   - баннер в профиле
function updateUserDisplay() {
  const displayName = editorName || editorLogin || "Гость";
  const initials = getInitials(editorName || editorLogin);

  // Шапка.
  const subtitle = document.getElementById("editor-subtitle");
  if (subtitle) {
    if (editorToken) {
      subtitle.innerHTML = `Вы вошли как <strong>${escapeHtml(displayName)}</strong>${editorCanPublish ? ' 👑' : ''}`;
    } else {
      subtitle.textContent = "Вы вошли как гость";
    }
  }
  const headerAvatar = document.getElementById("editor-avatar");
  if (headerAvatar) headerAvatar.textContent = initials;

  // Карточка в сайдбаре.
  const sun = document.getElementById("sidebar-user-name");
  const sul = document.getElementById("sidebar-user-login");
  const sua = document.getElementById("sidebar-user-avatar");
  const sub = document.getElementById("sidebar-user-badge");
  if (sun) sun.textContent = displayName;
  if (sul) sul.textContent = editorLogin ? `@${editorLogin}` : "";
  if (sua) sua.textContent = initials;
  if (sub) sub.style.display = editorCanPublish ? "" : "none";

  // Баннер в профиле.
  const pba = document.getElementById("profile-banner-avatar");
  const pbn = document.getElementById("profile-banner-name");
  const pbl = document.getElementById("profile-banner-login");
  if (pba) pba.textContent = initials;
  if (pbn) pbn.textContent = displayName;
  if (pbl) pbl.textContent = editorLogin ? `@${editorLogin}` : "";
}

async function loginEditorFromModal() {
  const loginEl = document.getElementById("editor-login-modal");
  const passEl = document.getElementById("editor-password-modal");
  const statusEl = document.getElementById("editor-auth-modal-status");
  if (!loginEl || !passEl) return;

  const login = (loginEl.value || "").trim();
  const password = (passEl.value || "").trim();
  if (!login || !password) {
    if (statusEl) statusEl.textContent = "Введите логин и пароль";
    return;
  }

  const ok = await doEditorLogin(login, password);
  if (!ok) {
    if (statusEl) statusEl.textContent = "Неверный логин/пароль";
    return;
  }

  document.getElementById("editor-auth-modal")?.classList.add("hidden");
}

async function doEditorLogin(login, password) {
  const remember = !!document.getElementById("remember-editor")?.checked;
  const res = await fetch(`${api}/auth/editor/login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ login, password, deviceId: remember ? getDeviceId() : "" })
  });

  if (!res.ok) return false;
  const data = await res.json();
  setEditorSession(data, login);
  if (remember) localStorage.setItem("editorRememberLogin", editorLogin);
  await loadMyPending();
  return true;
}

async function uploadOneFile(file) {
  const utf8Name = unescape(encodeURIComponent(file.name));
  const nameB64 = btoa(utf8Name);
  const uploadRes = await fetch(`${api}/upload?target=newsmedia`, {
    method: "POST",
    headers: { "X-Filename-Base64": nameB64 },
    body: file
  });
  if (!uploadRes.ok) throw new Error(`Не удалось загрузить файл: ${file.name}`);
  return file.name;
}

async function readVideoSize(file) {
  return await new Promise((resolve) => {
    try {
      const video = document.createElement("video");
      video.preload = "metadata";
      video.muted = true;
      video.playsInline = true;
      video.onloadedmetadata = () => {
        const result = { videoWidth: Number(video.videoWidth || 0), videoHeight: Number(video.videoHeight || 0) };
        URL.revokeObjectURL(video.src);
        resolve(result);
      };
      video.onerror = () => {
        if (video.src) URL.revokeObjectURL(video.src);
        resolve({ videoWidth: 0, videoHeight: 0 });
      };
      video.src = URL.createObjectURL(file);
    } catch {
      resolve({ videoWidth: 0, videoHeight: 0 });
    }
  });
}

async function submitNews() {
  if (!editorToken) return alert("Сначала войдите");

  const title = (document.getElementById("news-title").value || "").trim();
  const content = (document.getElementById("news-content").value || "").trim();
  const videoUrl = "";
  const linkUrl = (document.getElementById("news-link-url").value || "").trim();
  const videoInput = document.getElementById("news-video-file");
  const photosInput = document.getElementById("news-photos");

  if (!title || !content) return alert("Заполните заголовок и текст");

  try {
    let videoFile = "";
    let videoWidth = 0;
    let videoHeight = 0;
    const video = videoInput.files?.[0];
    if (video) {
      const dims = await readVideoSize(video);
      videoWidth = dims.videoWidth || 0;
      videoHeight = dims.videoHeight || 0;
      videoFile = await uploadOneFile(video);
    }

    const photoFiles = [];
    for (const image of Array.from(photosInput.files || [])) {
      const uploaded = await uploadOneFile(image);
      photoFiles.push(uploaded);
    }

    const payload = { title, content, authorLogin: editorLogin, authorName: editorName || editorLogin, videoUrl, linkUrl, videoFile, photoFiles, videoWidth, videoHeight };
    const res = await fetch(`${api}/news/editor/submit`, {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-Editor-Token": editorToken },
      body: JSON.stringify(payload)
    });

    if (!res.ok) return alert("Ошибка отправки новости");

    const result = await res.json().catch(() => ({}));
    if (result.autoPublished) {
      alert("Новость опубликована автоматически (у вас есть права публикации).");
    } else {
      alert("Новость отправлена на модерацию.");
    }
    document.getElementById("news-title").value = "";
    document.getElementById("news-content").value = "";
    document.getElementById("news-link-url").value = "";
    document.getElementById("qr-preview").innerHTML = "";
    videoInput.value = "";
    photosInput.value = "";
    await loadMyPending();
  } catch (e) {
    alert(e.message || "Ошибка загрузки файлов");
  }
}


async function changeEditorPassword() {
  if (!editorToken) return alert("Сначала войдите");
  const oldPassword = (document.getElementById("editor-old-password").value || "").trim();
  const newPassword = (document.getElementById("editor-new-password").value || "").trim();
  if (!oldPassword || !newPassword) return alert("Введите текущий и новый пароль");

  const res = await fetch(`${api}/auth/editor/change-password`, {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-Editor-Token": editorToken },
    body: JSON.stringify({ login: editorLogin, oldPassword, newPassword })
  });

  if (!res.ok) return alert("Не удалось сменить пароль");
  document.getElementById("editor-old-password").value = "";
  document.getElementById("editor-new-password").value = "";
  alert("Пароль успешно изменен");
}

function escapeHtml(str) {
  return (str || "").replace(/[&<>"']/g, s => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[s]));
}

// Выйти из аккаунта — очищаем sessionStorage и показываем форму входа.
function logoutEditor() {
  if (!confirm("Выйти из аккаунта?")) return;
  editorToken = "";
  editorLogin = "";
  editorName = "";
  editorCanPublish = false;
  sessionStorage.removeItem("editorToken");
  sessionStorage.removeItem("editorLogin");
  sessionStorage.removeItem("editorName");
  sessionStorage.removeItem("editorCanPublish");
  localStorage.removeItem("editorRememberLogin");
  applyEditorRightsUI();
  // Показываем модалку входа.
  const modal = document.getElementById("editor-auth-modal");
  if (modal) modal.classList.remove("hidden");
  // Очищаем поля профиля.
  const nameEl = document.getElementById("editor-name");
  const loginEl = document.getElementById("editor-login");
  const dnEl = document.getElementById("editor-display-name");
  const statusEl = document.getElementById("editor-auth-status");
  if (nameEl) nameEl.value = "";
  if (loginEl) loginEl.value = "";
  if (dnEl) dnEl.value = "";
  if (statusEl) statusEl.textContent = "Не выполнен вход";
  // Очищаем списки новостей.
  const ul = document.getElementById("my-news-list");
  if (ul) ul.innerHTML = "";
  // Сбрасываем кэш предпросмотра.
  newsPreviewCache.pending = [];
  newsPreviewCache.published = [];
  newsPreviewCache.rejected = [];
  newsPreviewCache.moderate = [];
  // Очищаем элементы списков.
  ["my-pending-list", "my-rejected-list", "my-published-list", "moderate-news-list"].forEach(id => {
    const el = document.getElementById(id);
    if (el) el.innerHTML = "";
  });
}

async function loadMyPending() {
  if (!editorToken || !editorLogin) return;
  const res = await fetch(`${api}/news/editor/mine?login=${encodeURIComponent(editorLogin)}`, { headers: { "X-Editor-Token": editorToken } });
  if (!res.ok) return;

  const data = await res.json();
  // Кэшируем для предпросмотра.
  newsPreviewCache.pending = data.pending || [];
  newsPreviewCache.rejected = data.rejected || [];
  newsPreviewCache.published = data.published || [];

  const pendingArea = document.getElementById("my-pending-list");
  const rejectedArea = document.getElementById("my-rejected-list");
  if (pendingArea) pendingArea.innerHTML = "";
  if (rejectedArea) rejectedArea.innerHTML = "";

  const renderList = (area, items, state) => {
    if (!area) return;
    if (!items || !items.length) {
      area.innerHTML = `<div class="card" style="padding:10px;color:var(--text-muted);font-size:13px;">Нет новостей</div>`;
      return;
    }
    items.forEach(n => {
      const card = buildNewsListCard(n, state);
      area.appendChild(card);
    });
  };

  renderList(pendingArea, data.pending, "pending");
  renderList(rejectedArea, data.rejected, "rejected");
}

// Загружает список опубликованных новостей текущего редактора.
async function loadMyPublished() {
  if (!editorToken || !editorLogin) return;
  try {
    const res = await fetch(`${api}/news/editor/mine?login=${encodeURIComponent(editorLogin)}`, { headers: { "X-Editor-Token": editorToken } });
    if (!res.ok) return;
    const data = await res.json();
    // Кэшируем для предпросмотра.
    newsPreviewCache.published = data.published || [];
    const area = document.getElementById("my-published-list");
    if (!area) return;
    area.innerHTML = "";
    const published = data.published || [];
    if (!published.length) {
      area.innerHTML = `<div class="card" style="padding:14px;color:var(--text-muted);">У вас пока нет опубликованных новостей</div>`;
      return;
    }
    published.forEach(n => {
      const card = buildNewsListCard(n, "published");
      area.appendChild(card);
    });
  } catch (e) {
    console.warn("[editor] loadMyPublished failed:", e);
  }
}

// Строит карточку новости для списков (pending/published/rejected/moderate).
// state: "pending" | "published" | "rejected" | "moderate"
function buildNewsListCard(n, state) {
  const id = n.id || n.Id;
  const title = n.title || n.Title || "Без названия";
  const content = n.content || n.Content || "";
  const created = n.createdAt || n.CreatedAt;
  const dateStr = new Date(created).toLocaleString('ru-RU');
  const author = n.authorName || n.AuthorName || n.authorLogin || n.AuthorLogin || "—";
  const rejectReason = n.rejectReason || n.RejectReason || "";
  const publishedBy = n.publishedByName || n.PublishedByName || "";
  const autoPub = n.autoPublished || n.AutoPublished || false;
  const photos = n.photoFiles || n.PhotoFiles || [];
  const videoFile = n.videoFile || n.VideoFile || "";
  const linkUrl = n.linkUrl || n.LinkUrl || "";

  const statusLabels = {
    pending: "На модерации",
    published: "Опубликована",
    rejected: "Отклонена",
    moderate: "Ожидает публикации"
  };
  const statusHtml = `<span class="np-meta-status ${state}">${statusLabels[state] || state}</span>`;

  const reasonHtml = rejectReason
    ? `<div class="news-card-reject-reason">Причина отклонения: ${escapeHtml(rejectReason)}</div>`
    : "";

  const pubByHtml = publishedBy && publishedBy !== author
    ? `<div style="font-size:11px;color:#f0ad4e;margin-top:2px;">📤 Опубликовал: ${escapeHtml(publishedBy)}${autoPub ? ' (авто)' : ''}</div>`
    : "";

  const card = document.createElement("div");
  card.className = "news-card";
  // Сохраняем данные для предпросмотра.
  card.dataset.id = id;
  card.dataset.state = state;

  // Кнопки в зависимости от state.
  const actionsHtml = [];
  actionsHtml.push(`<button class="small-btn" onclick="event.stopPropagation();openNewsPreviewById('${escapeHtml(id)}','${escapeHtml(state)}')">👁 Просмотр</button>`);
  if (state === "moderate" && editorCanPublish) {
    actionsHtml.push(`<button class="small-btn active" onclick="event.stopPropagation();publishModerateNews('${escapeHtml(id)}')">📤 Опубликовать</button>`);
  }

  card.innerHTML = `
    <div class="news-card-title">${escapeHtml(title)}</div>
    <div class="news-card-meta">Автор: ${escapeHtml(author)} • ${dateStr} ${statusHtml}</div>
    ${pubByHtml}
    ${reasonHtml}
    <div style="color:var(--text-muted);font-size:13px;margin-top:6px;max-height:60px;overflow:hidden;">${escapeHtml(content.slice(0,200))}${content.length > 200 ? '…' : ''}</div>
    <div class="news-card-actions" style="margin-top:10px;">${actionsHtml.join('')}</div>
  `;
  // Клик по карточке тоже открывает просмотр.
  card.style.cursor = "pointer";
  card.addEventListener("click", () => openNewsPreviewById(id, state));
  return card;
}

// Загружает информацию о редакторе с сервера (права, отображаемое имя).
async function loadEditorInfo() {
  if (!editorToken || !editorLogin) return;
  try {
    const res = await fetch(`${api}/news/editor/info?login=${encodeURIComponent(editorLogin)}`, {
      headers: { "X-Editor-Token": editorToken }
    });
    if (!res.ok) return;
    const info = await res.json();
    editorCanPublish = !!info.canPublishWithoutApproval;
    editorName = info.displayAs || info.name || editorLogin;
    sessionStorage.setItem("editorCanPublish", editorCanPublish ? "1" : "0");
    sessionStorage.setItem("editorName", editorName);
    applyEditorRightsUI();

    const nameEl = document.getElementById("editor-name");
    const dnEl = document.getElementById("editor-display-name");
    const loginEl = document.getElementById("editor-login");
    if (nameEl) nameEl.value = info.name || "";
    if (loginEl) loginEl.value = info.login || editorLogin;
    if (dnEl) dnEl.value = info.displayName || "";
    document.getElementById("editor-auth-status").textContent = `Вошли как: ${editorName || editorLogin}`;
  } catch (e) {
    console.warn("[editor] loadEditorInfo failed:", e);
  }
}

// Сохраняет отображаемое имя редактора.
async function saveDisplayName() {
  if (!editorToken) return alert("Сначала войдите");
  const dnEl = document.getElementById("editor-display-name");
  if (!dnEl) return;
  const displayName = (dnEl.value || "").trim();
  try {
    const res = await fetch(`${api}/news/editor/update-profile`, {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-Editor-Token": editorToken },
      body: JSON.stringify({ login: editorLogin, displayName })
    });
    if (!res.ok) return alert("Не удалось сохранить имя");
    const data = await res.json();
    editorName = data.displayAs || displayName || editorLogin;
    sessionStorage.setItem("editorName", editorName);
    document.getElementById("editor-auth-status").textContent = `Вошли как: ${editorName}`;
    alert("Отображаемое имя сохранено: " + editorName);
  } catch (e) {
    alert("Ошибка: " + e.message);
  }
}

// Загружает очередь модерации (для редакторов с правами публикации).
async function loadModerateQueue() {
  if (!editorToken || !editorLogin) return;
  if (!editorCanPublish) {
    const area = document.getElementById("moderate-news-list");
    if (area) area.innerHTML = "<div class='card'>У вас нет прав на модерацию.</div>";
    return;
  }
  try {
    const res = await fetch(`${api}/news/editor/pending?login=${encodeURIComponent(editorLogin)}`, {
      headers: { "X-Editor-Token": editorToken }
    });
    if (!res.ok) {
      const area = document.getElementById("moderate-news-list");
      if (area) area.innerHTML = "<div class='card'>Ошибка загрузки</div>";
      return;
    }
    const items = await res.json();
    // Кэшируем для предпросмотра.
    newsPreviewCache.moderate = items || [];
    const area = document.getElementById("moderate-news-list");
    if (!area) return;
    area.innerHTML = "";
    if (!items || !items.length) {
      area.innerHTML = "<div class='card'>Нет новостей на модерации</div>";
      return;
    }
    items.forEach(n => {
      const card = buildNewsListCard(n, "moderate");
      area.appendChild(card);
    });
  } catch (e) {
    console.warn("[editor] loadModerateQueue failed:", e);
  }
}

async function publishModerateNews(id) {
  if (!editorToken || !editorCanPublish) return alert("Нет прав на публикацию");
  if (!confirm("Опубликовать эту новость?")) return;
  try {
    const res = await fetch(`${api}/news/editor/publish`, {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-Editor-Token": editorToken },
      body: JSON.stringify({ id, login: editorLogin })
    });
    if (!res.ok) {
      const err = await res.text().catch(() => "");
      return alert("Ошибка публикации: " + err);
    }
    await loadModerateQueue();
  } catch (e) {
    alert("Ошибка: " + e.message);
  }
}

async function previewModerateNews(id) {
  // Делегируем в универсальную функцию предпросмотра.
  openNewsPreviewById(id, "moderate");
}

// Текущая открытая в модалке новость (для действий).
let npCurrentItem = null;
let npCurrentState = null;
let npIsEditing = false;

// Находит новость по id в нужном кэше, при необходимости подгружает с сервера.
async function findNewsById(id, state) {
  // Сначала проверяем кэш.
  let cacheKey = state;
  if (state === "published") cacheKey = "published";
  let list = newsPreviewCache[cacheKey] || [];
  let item = list.find(x => (x.id || x.Id) === id);
  if (item) return item;

  // Если нет в кэше — подгружаем с сервера.
  if (state === "moderate") {
    try {
      const res = await fetch(`${api}/news/editor/pending?login=${encodeURIComponent(editorLogin)}`, {
        headers: { "X-Editor-Token": editorToken }
      });
      if (res.ok) {
        const items = await res.json();
        newsPreviewCache.moderate = items || [];
        return (items || []).find(x => (x.id || x.Id) === id);
      }
    } catch (e) {}
  } else {
    // pending / published / rejected — все в /news/editor/mine.
    try {
      const res = await fetch(`${api}/news/editor/mine?login=${encodeURIComponent(editorLogin)}`, {
        headers: { "X-Editor-Token": editorToken }
      });
      if (res.ok) {
        const data = await res.json();
        newsPreviewCache.pending = data.pending || [];
        newsPreviewCache.published = data.published || [];
        newsPreviewCache.rejected = data.rejected || [];
        list = newsPreviewCache[cacheKey] || [];
        return list.find(x => (x.id || x.Id) === id);
      }
    } catch (e) {}
  }
  return null;
}

// Открывает модалку предпросмотра/редактирования новости.
// state: "pending" | "published" | "rejected" | "moderate"
async function openNewsPreviewById(id, state) {
  if (!editorToken) return alert("Сначала войдите");
  const item = await findNewsById(id, state);
  if (!item) return alert("Новость не найдена");
  npCurrentItem = item;
  npCurrentState = state;
  npIsEditing = false;
  renderNewsPreviewModal();
  document.getElementById("news-preview-modal").classList.remove("hidden");
}

function closeNewsPreviewModal() {
  document.getElementById("news-preview-modal").classList.add("hidden");
  npCurrentItem = null;
  npCurrentState = null;
  npIsEditing = false;
}

// Переключение режима редактирования.
function toggleNewsPreviewEdit() {
  if (!npCurrentItem) return;
  // Редактировать можно только pending и moderate (неопубликованные).
  if (npCurrentState !== "pending" && npCurrentState !== "moderate") {
    return alert("Редактировать можно только неопубликованные новости");
  }
  npIsEditing = !npIsEditing;
  renderNewsPreviewModal();
}

// Сохранение отредактированной новости (title/content/link).
async function saveNewsPreviewEdit() {
  if (!npCurrentItem || !npIsEditing) return;
  const id = npCurrentItem.id || npCurrentItem.Id;
  const title = (document.getElementById("np-title").value || "").trim();
  const content = (document.getElementById("np-content").value || "").trim();
  const linkUrl = (document.getElementById("np-link").value || "").trim();
  if (!title) return alert("Заголовок не может быть пустым");

  try {
    const res = await fetch(`${api}/news/editor/update`, {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-Editor-Token": editorToken },
      body: JSON.stringify({ id, login: editorLogin, title, content, linkUrl })
    });
    if (!res.ok) {
      const err = await res.text().catch(() => "");
      return alert("Ошибка сохранения: " + err);
    }
    // Обновляем локальный кэш.
    npCurrentItem.title = title;
    npCurrentItem.Title = title;
    npCurrentItem.content = content;
    npCurrentItem.Content = content;
    npCurrentItem.linkUrl = linkUrl;
    npCurrentItem.LinkUrl = linkUrl;
    npIsEditing = false;
    renderNewsPreviewModal();
    alert("Сохранено");
    // Перезагружаем списки.
    refreshCurrentList();
  } catch (e) {
    alert("Ошибка: " + e.message);
  }
}

// Публикация новости (из moderate или pending, если есть права).
async function publishFromPreview() {
  if (!npCurrentItem) return;
  if (!editorCanPublish) return alert("Нет прав на публикацию");
  const id = npCurrentItem.id || npCurrentItem.Id;
  if (!confirm("Опубликовать эту новость?")) return;
  try {
    const res = await fetch(`${api}/news/editor/publish`, {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-Editor-Token": editorToken },
      body: JSON.stringify({ id, login: editorLogin })
    });
    if (!res.ok) {
      const err = await res.text().catch(() => "");
      return alert("Ошибка публикации: " + err);
    }
    closeNewsPreviewModal();
    refreshCurrentList();
  } catch (e) {
    alert("Ошибка: " + e.message);
  }
}

// Перезагружает текущий активный список.
function refreshCurrentList() {
  if (npCurrentState === "moderate") loadModerateQueue();
  else if (npCurrentState === "published") loadMyPublished();
  else loadMyPending();
}

// Рендерит содержимое модалки предпросмотра.
function renderNewsPreviewModal() {
  if (!npCurrentItem) return;
  const n = npCurrentItem;
  const id = n.id || n.Id;
  const title = n.title || n.Title || "";
  const content = n.content || n.Content || "";
  const created = n.createdAt || n.CreatedAt;
  const dateStr = new Date(created).toLocaleString('ru-RU');
  const author = n.authorName || n.AuthorName || n.authorLogin || n.AuthorLogin || "—";
  const authorLogin = n.authorLogin || n.AuthorLogin || "";
  const rejectReason = n.rejectReason || n.RejectReason || "";
  const publishedBy = n.publishedByName || n.PublishedByName || "";
  const autoPub = n.autoPublished || n.AutoPublished || false;
  const photos = n.photoFiles || n.PhotoFiles || [];
  const videoFile = n.videoFile || n.VideoFile || "";
  const linkUrl = n.linkUrl || n.LinkUrl || "";

  const mediaBase = `${api}/download?target=newsmedia&name=`;

  // Заголовок модалки.
  const modalTitle = document.getElementById("np-modal-title");
  if (modalTitle) modalTitle.textContent = npIsEditing ? "Редактирование новости" : "Просмотр новости";

  // Метаданные.
  const metaEl = document.getElementById("np-meta");
  if (metaEl) {
    const statusLabels = {
      pending: "На модерации",
      published: "Опубликована",
      rejected: "Отклонена",
      moderate: "Ожидает публикации"
    };
    let metaHtml = `<div><strong>Автор:</strong> ${escapeHtml(author)}</div>`;
    if (authorLogin) metaHtml += `<div><strong>Логин автора:</strong> @${escapeHtml(authorLogin)}</div>`;
    metaHtml += `<div><strong>Создано:</strong> ${dateStr}</div>`;
    metaHtml += `<div><span class="np-meta-status ${npCurrentState}">${statusLabels[npCurrentState] || npCurrentState}</span></div>`;
    if (publishedBy && publishedBy !== author) {
      metaHtml += `<div><strong>Опубликовал:</strong> ${escapeHtml(publishedBy)}${autoPub ? ' (авто)' : ''}</div>`;
    }
    if (rejectReason) {
      metaHtml += `<div style="color:#ff9d9d;"><strong>Причина отклонения:</strong> ${escapeHtml(rejectReason)}</div>`;
    }
    metaEl.innerHTML = metaHtml;
  }

  // Поля редактирования vs просмотра.
  const editFields = document.getElementById("np-edit-fields");
  const viewFields = document.getElementById("np-view-fields");
  if (npIsEditing) {
    if (editFields) editFields.style.display = "";
    if (viewFields) viewFields.style.display = "none";
    document.getElementById("np-title").value = title;
    document.getElementById("np-content").value = content;
    document.getElementById("np-link").value = linkUrl;
  } else {
    if (editFields) editFields.style.display = "none";
    if (viewFields) viewFields.style.display = "";
    document.getElementById("np-view-title").textContent = title;
    document.getElementById("np-view-content").textContent = content;
    const linkEl = document.getElementById("np-view-link");
    if (linkEl) {
      linkEl.innerHTML = linkUrl
        ? `<div style="padding:10px 12px;background:var(--panel);border-radius:var(--radius-sm);border:1px solid var(--border);font-size:13px;">
            <strong>🔗 Ссылка:</strong> <span style="color:var(--accent);word-break:break-all;">${escapeHtml(linkUrl)}</span>
          </div>`
        : "";
    }
  }

  // Медиа.
  const mediaEl = document.getElementById("np-media");
  if (mediaEl) {
    let mediaHtml = "";
    if (videoFile) {
      mediaHtml += `<video src="${mediaBase}${encodeURIComponent(videoFile)}" controls style="max-width:300px;max-height:300px;"></video>`;
    }
    photos.forEach(f => {
      mediaHtml += `<img src="${mediaBase}${encodeURIComponent(f)}" style="max-width:200px;max-height:200px;object-fit:cover;">`;
    });
    if (!mediaHtml) mediaHtml = `<div class="np-media-empty">Нет медиа</div>`;
    mediaEl.innerHTML = mediaHtml;
  }

  // QR-код, если есть ссылка (только в режиме просмотра).
  const qrEl = document.getElementById("np-qr");
  if (qrEl) {
    if (linkUrl && !npIsEditing) {
      try {
        new URL(linkUrl);
        const qrUrl = `https://api.qrserver.com/v1/create-qr-code/?size=160x160&data=${encodeURIComponent(linkUrl)}`;
        qrEl.innerHTML = `<div style="display:flex;align-items:center;gap:12px;padding:10px;background:var(--panel);border:1px solid var(--border);border-radius:8px;">
          <img src="${qrUrl}" alt="QR" style="width:120px;height:120px;background:#fff;padding:4px;border-radius:4px;">
          <div>
            <div style="font-size:13px;font-weight:600;">QR-код к ссылке</div>
            <div style="font-size:11px;color:var(--muted);">Будет показан на киоске</div>
          </div>
        </div>`;
      } catch (e) {
        qrEl.innerHTML = "";
      }
    } else {
      qrEl.innerHTML = "";
    }
  }

  // Кнопки действий.
  const actionsEl = document.getElementById("np-actions");
  if (actionsEl) {
    const buttons = [];
    // Режим просмотра.
    if (!npIsEditing) {
      // Редактировать можно pending и moderate (если есть права на moderate).
      if (npCurrentState === "pending" || npCurrentState === "moderate") {
        // Для moderate — только если есть права.
        if (npCurrentState === "pending" || editorCanPublish) {
          buttons.push(`<button onclick="toggleNewsPreviewEdit()">✏️ Редактировать</button>`);
        }
      }
      // Опубликовать можно moderate (если есть права).
      if (npCurrentState === "moderate" && editorCanPublish) {
        buttons.push(`<button class="active" onclick="publishFromPreview()">📤 Опубликовать</button>`);
      }
      buttons.push(`<button class="secondary" onclick="closeNewsPreviewModal()">Закрыть</button>`);
    } else {
      // Режим редактирования.
      buttons.push(`<button onclick="saveNewsPreviewEdit()">💾 Сохранить</button>`);
      buttons.push(`<button class="secondary" onclick="toggleNewsPreviewEdit()">Отмена</button>`);
    }
    actionsEl.innerHTML = buttons.join('');
  }
}

// Генерация QR-кода для предпросмотра ссылки.
// Используем публичный API qrserver.com (без ключа).
// Если ссылка невалидна — ничего не показываем.
function updateQrPreview() {
  const input = document.getElementById("news-link-url");
  const preview = document.getElementById("qr-preview");
  if (!input || !preview) return;
  const url = (input.value || "").trim();
  if (!url) {
    preview.innerHTML = "";
    return;
  }
  // Простая валидация URL.
  try {
    new URL(url);
  } catch (e) {
    preview.innerHTML = "<div style='color:var(--muted);font-size:12px;'>Неверный формат ссылки</div>";
    return;
  }
  const size = 160;
  const qrUrl = `https://api.qrserver.com/v1/create-qr-code/?size=${size}x${size}&data=${encodeURIComponent(url)}`;
  preview.innerHTML = `
    <div style="display:flex;align-items:center;gap:12px;padding:10px;background:var(--panel);border:1px solid var(--border);border-radius:8px;">
      <img src="${qrUrl}" alt="QR код" style="width:${size}px;height:${size}px;background:#fff;padding:4px;border-radius:4px;">
      <div>
        <div style="font-size:13px;font-weight:600;">QR-код к ссылке</div>
        <div style="font-size:12px;color:var(--muted);">Будет показан в новости на киоске</div>
      </div>
    </div>`;
}
