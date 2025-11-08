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
async function loadCalendar() {
  const res = await fetch(`${api}/calendar/list`);
  const events = await res.json();
  const ul = document.getElementById("calendar");
  ul.innerHTML = "";
  events.forEach(e => {
    const li = document.createElement("li");
    li.innerHTML = `${e.title} (${e.date}) <button onclick="deleteEvent('${e.title}')">🗑</button>`;
    ul.appendChild(li);
  });
}

async function addEvent() {
  const title = document.getElementById("event-title").value.trim();
  const date = document.getElementById("event-date").value;
  if (!title || !date) return alert("Введите данные!");

  const body = JSON.stringify({ title, date });
  await fetch(`${api}/calendar/add`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body
  });

  document.getElementById("event-title").value = "";
  loadCalendar();
}

async function deleteEvent(title) {
  await fetch(`${api}/calendar/delete?title=${encodeURIComponent(title)}`);
  loadCalendar();
}
