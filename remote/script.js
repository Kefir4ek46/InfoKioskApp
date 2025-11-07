const api = location.origin;

// === Проверка состояния сервера ===
window.addEventListener("load", () => {
  document.getElementById("server-status").textContent = "✅ Подключено: " + api;
  loadFileList();
  loadCalendar();
});

// === Загрузка файла ===
async function uploadFile() {
  const fileInput = document.getElementById("file-input");
  const type = document.getElementById("upload-type").value;
  const file = fileInput.files[0];
  if (!file) return alert("Выберите файл!");

  const url = `${api}/upload?target=${type}&name=${encodeURIComponent(file.name)}`;
  const res = await fetch(url, {
    method: "POST",
    body: file
  });

  if (res.ok) {
    alert("✅ Файл загружен!");
    loadFileList();
  } else {
    alert("❌ Ошибка загрузки");
  }
}

// === Получить список файлов ===
async function loadFileList() {
  const res = await fetch(`${api}/list?target=schedules`);
  const data = await res.json();
  const filesList = document.getElementById("files");
  filesList.innerHTML = "";

  if (data.files && data.files.length > 0) {
    data.files.forEach(f => {
      const li = document.createElement("li");
      li.innerHTML = `
        <span>${f.name}</span>
        <button onclick="deleteFile('${f.name}')">🗑</button>
      `;
      filesList.appendChild(li);
    });
  } else {
    filesList.innerHTML = "<li>Нет файлов</li>";
  }
}

// === Удаление файла ===
async function deleteFile(name) {
  if (!confirm(`Удалить ${name}?`)) return;
  const res = await fetch(`${api}/delete?target=schedules&name=${encodeURIComponent(name)}`);
  if (res.ok) loadFileList();
}

// === Календарь ===
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

