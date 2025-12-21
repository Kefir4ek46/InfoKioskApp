// script.js — InfoKiosk Remote Admin
// Полный клиент для media/posts + модалка с листанием, drag&drop, CRUD постов.

// базовый адрес API (использует тот же origin, откуда загружена страница)
const api = location.origin;

// ---- Инициализация ----
window.addEventListener("load", async () => {
  try {
    document.getElementById("server-status").textContent = "✔ Подключено: " + api;

    setupTabs();
    setupDragAndDrop();
    setupModalControls();

    await loadPostCategorySelector();
    await loadCategoriesAndBuildUI();

    await loadOtherSchedules();
    await loadFilesCategory();
    await loadCalendar();
    await loadSettings();
  } catch (err) {
    console.error("Init error:", err);
  }
});

// ---- UI: Tabs ----
function setupTabs() {
  const tabs = document.querySelectorAll(".menu-item");
  const contents = document.querySelectorAll(".tab-content");
  tabs.forEach(tab => {
    tab.addEventListener("click", () => {
      tabs.forEach(t => t.classList.remove("active"));
      contents.forEach(c => c.classList.remove("active"));
      tab.classList.add("active");
      const id = tab.dataset.tab;
      document.getElementById("tab-" + id).classList.add("active");
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
    document.getElementById("autostart-toggle").checked = !!data.autostart;
    document.getElementById("sleep-time").value = data.sleepMinutes ?? "";
  } catch (err) {
    console.warn("loadSettings:", err);
  }
  loadStorageInfo();
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
        <div class="count">Фото: ${imagesCount}
            <span style="float:right" class="actions">
                <button class="pill open">Открыть</button>
                <button class="pill del">Удалить</button>
            </span>
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
  if (type === "other") await loadOtherSchedules();
}

async function loadOtherSchedules() {
  try {
    const res = await fetch(`${api}/list?target=other`);
    if (!res.ok) return;
    const data = await res.json();
    const list = document.getElementById("other-files");
    list.innerHTML = "";
    if (data.files && data.files.length) {
      data.files.forEach(f => {
        const li = document.createElement("li");
        li.innerHTML = `<span>${escapeHtml(f.name)}</span><div><button onclick="deleteFile('${escapeHtml(f.name)}','other')">🗑</button></div>`;
        list.appendChild(li);
      });
    } else list.innerHTML = "<li>Нет файлов</li>";
  } catch (err) {
    console.error("loadOtherSchedules", err);
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
    const category = document.getElementById("file-category").value || "media";
    const res = await fetch(`${api}/list?target=${encodeURIComponent(category)}`);
    if (!res.ok) return;
    const data = await res.json();
    const list = document.getElementById("files-list");
    list.innerHTML = "";
    if (data.files && data.files.length) {
      data.files.forEach(f => {
        const li = document.createElement("li");
        li.innerHTML = `<span>${escapeHtml(f.name)}</span><div><button onclick="previewFile('${escapeHtml(f.name)}','${escapeHtml(category)}')">👁</button> <button onclick="deleteFile('${escapeHtml(f.name)}','${escapeHtml(category)}')">🗑</button></div>`;
        list.appendChild(li);
      });
    } else list.innerHTML = "<li>Нет файлов</li>";
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
    if (type === "main" || type === "changes" || type === "other") {
      alert("Просмотр расписаний отключён.");
      return;
    }
    const utf8Name = unescape(encodeURIComponent(name));
    const nameB64 = btoa(utf8Name);
    const url = `${api}/download?target=${encodeURIComponent(type)}`;
    const res = await fetch(url, { method: "GET", headers: { "X-Filename-Base64": nameB64 } });
    if (!res.ok) {
      document.getElementById("preview-area").innerHTML = "<p>Ошибка загрузки файла</p>";
      return;
    }
    const blob = await res.blob();
    const fileUrl = URL.createObjectURL(blob);
    const preview = document.getElementById("preview-area");
    preview.innerHTML = "";
    if (name.match(/\.(jpg|jpeg|png|gif)$/i)) {
      const img = document.createElement("img");
      img.src = fileUrl;
      preview.appendChild(img);
      return;
    }
    if (name.match(/\.pdf$/i)) {
      const iframe = document.createElement("iframe");
      iframe.src = fileUrl;
      iframe.style.width = "100%";
      iframe.style.height = "600px";
      preview.appendChild(iframe);
      return;
    }
    // other types: offer download
    preview.innerHTML = `<p>Предпросмотр не доступен.</p><a class="download-btn" href="${fileUrl}" download="${escapeHtml(name)}">⬇ Скачать</a>`;
  } catch (err) {
    console.error("previewFile", err);
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
      const li = document.createElement("li");
      li.innerHTML = `<div><strong>${escapeHtml(e.title)}</strong> <div style="color:var(--muted)">${escapeHtml(range)} — ${escapeHtml(type)}</div></div><div><button onclick="deleteEvent('${escapeHtml(e.id)}')">🗑</button></div>`;
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
    const start = document.getElementById("event-start").value;
    const end = document.getElementById("event-end").value || start;
    const type = document.getElementById("event-type").value;
    if (!title || !start) return alert("Введите название и дату начала!");
    const body = JSON.stringify({ title, startDate: start, endDate: end, type });
    const res = await fetch(`${api}/calendar/add`, { method: "POST", headers: { "Content-Type": "application/json" }, body });
    if (res.ok) {
      alert("Событие добавлено");
      document.getElementById("event-title").value = "";
      await loadCalendar();
    } else alert("Ошибка при добавлении");
  } catch (err) {
    console.error("addEvent", err);
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

        document.getElementById("disk-used").textContent = data.disk.used;
        document.getElementById("disk-free").textContent = data.disk.free;
        document.getElementById("data-size").textContent = data.dataFolder.size;

    } catch (e) {
        console.warn("Storage info unavailable", e);
    }
}


