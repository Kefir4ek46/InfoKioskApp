const api = location.origin;

// === Инициализация ===
window.addEventListener("load", () => {
  document.getElementById("server-status").textContent = "✅ Подключено: " + api;
  loadOtherSchedules();
  loadFilesCategory();
  loadCalendar();
  setupTabs();
  setupDragAndDrop();
});

// === Переключение вкладок ===
function setupTabs() {
  const tabs = document.querySelectorAll(".tab");
  const contents = document.querySelectorAll(".tab-content");

  tabs.forEach(tab => {
    tab.addEventListener("click", () => {
      tabs.forEach(t => t.classList.remove("active"));
      contents.forEach(c => c.classList.remove("active"));

      tab.classList.add("active");
      document.getElementById("tab-" + tab.dataset.tab).classList.add("active");
    });
  });
}

// =========================================================
// 📦 === DRAG & DROP ===
// =========================================================
function setupDragAndDrop() {
  const dropZones = document.querySelectorAll("input[type='file']");

  dropZones.forEach(input => {
    const parent = input.parentElement;

    parent.addEventListener("dragover", e => {
      e.preventDefault();
      parent.classList.add("drag-hover");
    });

    parent.addEventListener("dragleave", () => {
      parent.classList.remove("drag-hover");
    });

    parent.addEventListener("drop", e => {
      e.preventDefault();
      parent.classList.remove("drag-hover");

      const files = e.dataTransfer.files;
      if (!files.length) return;

      input.files = files;
      const type = input.id.replace("-file", "");
      if (["main", "changes", "other"].includes(type)) uploadSchedule(type);
      else uploadFileType(type);
    });
  });
}

// =========================================================
// 📅 === РАСПИСАНИЯ ===
// =========================================================
async function uploadSchedule(type) {
  const input = document.getElementById(type + "-file");
  const files = Array.from(input.files);
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
      alert("❌ Ошибка при загрузке: " + err.message);
    }
  }

  alert("✅ Файлы загружены!");
  if (type === "other") loadOtherSchedules();
}

async function loadOtherSchedules() {
  const res = await fetch(`${api}/list?target=other`);
  const data = await res.json();
  const list = document.getElementById("other-files");
  list.innerHTML = "";
  if (data.files && data.files.length > 0) {
    data.files.forEach(f => {
      const li = document.createElement("li");
      li.innerHTML = `<span>${f.name}</span> <button onclick="deleteFile('${f.name}','other')">🗑</button>`;
      list.appendChild(li);
    });
  } else {
    list.innerHTML = "<li>Нет файлов</li>";
  }
}

// =========================================================
// 🗂 === ФАЙЛЫ ===
// =========================================================
async function uploadFileType(type) {
  const input = document.getElementById(type + "-file");
  const files = Array.from(input.files);
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
      console.error(err);
      alert("❌ Ошибка при загрузке: " + err.message);
    }
  }

  alert("✅ Файлы загружены!");
  loadFilesCategory();
}

async function loadFilesCategory() {
  const category = document.getElementById("file-category").value;
  const res = await fetch(`${api}/list?target=${category}`);
  const data = await res.json();
  const list = document.getElementById("files-list");
  list.innerHTML = "";

  if (data.files && data.files.length > 0) {
    data.files.forEach(f => {
      const li = document.createElement("li");
      li.innerHTML = `<span>${f.name}</span> <button onclick="deleteFile('${f.name}','${category}')">🗑</button>`;
      list.appendChild(li);
    });
  } else {
    list.innerHTML = "<li>Нет файлов</li>";
  }
}

// =========================================================
// 🗑 === УДАЛЕНИЕ ===
// =========================================================
async function deleteFile(name, type) {
  if (!confirm(`Удалить ${name}?`)) return;

  const utf8Name = unescape(encodeURIComponent(name));
  const nameB64 = btoa(utf8Name);

  const res = await fetch(`${api}/delete?target=${type}`, {
    method: "GET",
    headers: { "X-Filename-Base64": nameB64 }
  });

  if (res.ok) {
    if (type === "other") loadOtherSchedules();
    else loadFilesCategory();
  } else {
    alert("❌ Ошибка удаления файла");
  }
}

// =========================================================
// 📆 === КАЛЕНДАРЬ ===
// =========================================================
// map типов -> цвет (css-hex)
const categoryColors = {
  "Праздник": "#b42828",
  "Каникулы": "#289628",
  "Выходной": "#1e50b4",
  "Другое": "#009696"
};

async function loadCalendar() {
  const res = await fetch(`${api}/calendar/list`);
  if (!res.ok) {
    console.error('Ошибка загрузки календаря', res.status);
    return;
  }

  const events = await res.json();
  const ul = document.getElementById("calendar");
  ul.innerHTML = "";

  events.forEach(e => {
    const start = e.startDate ? new Date(e.startDate).toLocaleDateString("ru-RU") : "";
    const end = e.endDate ? new Date(e.endDate).toLocaleDateString("ru-RU") : "";
    const range = (end && end !== start) ? `${start} — ${end}` : start;
    const type = e.type || "Другое";

    const li = document.createElement("li");
    li.style.display = "flex";
    li.style.justifyContent = "space-between";
    li.style.alignItems = "center";
    li.style.padding = "8px 12px";
    li.style.margin = "6px 0";
    li.style.borderRadius = "6px";
    li.style.background = "#2e2e2e";
    li.style.color = "#fff";

    // цветная метка
    const color = categoryColors[type] || categoryColors["Другое"];
    const mark = document.createElement("span");
    mark.style.display = "inline-block";
    mark.style.width = "12px";
    mark.style.height = "12px";
    mark.style.background = color;
    mark.style.borderRadius = "3px";
    mark.style.marginRight = "10px";

    const left = document.createElement("div");
    left.style.display = "flex";
    left.style.alignItems = "center";
    left.innerHTML = `<strong style="margin-right:8px">${escapeHtml(e.title)}</strong> <span style="color:#bbb">${range}</span> <em style="margin-left:10px;color:#9fc2ff">${type}</em>`;
    left.prepend(mark);

    const btn = document.createElement("button");
    btn.textContent = "🗑";
    btn.style.background = "transparent";
    btn.style.border = "none";
    btn.style.color = "#ff6b6b";
    btn.style.cursor = "pointer";
    btn.onclick = () => {
      if (confirm(`Удалить событие "${e.title}"?`)) deleteEvent(e.id);
    };

    li.appendChild(left);
    li.appendChild(btn);

    ul.appendChild(li);
  });
}

function escapeHtml(str) {
  return (str || "").replace(/[&<>"']/g, s => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[s]));
}

async function deleteEvent(id) {
  const res = await fetch(`${api}/calendar/delete?id=${encodeURIComponent(id)}`);
  if (res.ok) {
    await loadCalendar();
  } else {
    alert("Ошибка удаления события");
  }
}


async function addEvent() {
  const title = document.getElementById("event-title").value.trim();
  const start = document.getElementById("event-start").value;
  const end = document.getElementById("event-end").value || start;
  const type = document.getElementById("event-type").value;

  if (!title || !start) {
    alert("Введите название и дату начала!");
    return;
  }

  const body = JSON.stringify({
    title,
    startDate: start,
    endDate: end,
    type
  });

  const res = await fetch(`${api}/calendar/add`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body
  });

  if (res.ok) {
    alert("✅ Событие добавлено!");
    document.getElementById("event-title").value = "";
    loadCalendar();
  } else {
    alert("❌ Ошибка при добавлении события");
  }
}
