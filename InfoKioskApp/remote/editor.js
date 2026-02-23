const api = location.origin;
let editorToken = sessionStorage.getItem("editorToken") || "";
let editorLogin = sessionStorage.getItem("editorLogin") || "";

window.addEventListener("load", async () => {
  if (editorToken) {
    document.getElementById("editor-auth-status").textContent = `Вошли как: ${editorLogin}`;
    await loadMyPending();
  }
});

async function loginEditor() {
  const login = (document.getElementById("editor-login").value || "").trim();
  const password = (document.getElementById("editor-password").value || "").trim();
  if (!login || !password) return alert("Введите логин и пароль");

  const res = await fetch(`${api}/auth/editor/login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ login, password })
  });

  if (!res.ok) return alert("Неверный логин/пароль");
  const data = await res.json();
  editorToken = data.token;
  editorLogin = data.login || login;
  sessionStorage.setItem("editorToken", editorToken);
  sessionStorage.setItem("editorLogin", editorLogin);
  document.getElementById("editor-auth-status").textContent = `Вошли как: ${editorLogin}`;
  await loadMyPending();
}

async function submitNews() {
  if (!editorToken) return alert("Сначала войдите");

  const title = (document.getElementById("news-title").value || "").trim();
  const content = (document.getElementById("news-content").value || "").trim();
  const videoUrl = (document.getElementById("news-video-url").value || "").trim();
  const fileInput = document.getElementById("news-video-file");
  const file = fileInput.files?.[0];

  if (!title || !content) return alert("Заполните заголовок и текст");

  let videoFile = "";
  if (file) {
    const utf8Name = unescape(encodeURIComponent(file.name));
    const nameB64 = btoa(utf8Name);
    const uploadRes = await fetch(`${api}/upload?target=newsmedia`, {
      method: "POST",
      headers: { "X-Filename-Base64": nameB64 },
      body: file
    });
    if (!uploadRes.ok) return alert("Не удалось загрузить видеофайл");
    videoFile = file.name;
  }

  const payload = { title, content, authorLogin: editorLogin, videoUrl, videoFile };
  const res = await fetch(`${api}/news/editor/submit`, {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-Editor-Token": editorToken },
    body: JSON.stringify(payload)
  });

  if (!res.ok) return alert("Ошибка отправки новости");

  alert("Новость отправлена на модерацию");
  document.getElementById("news-title").value = "";
  document.getElementById("news-content").value = "";
  document.getElementById("news-video-url").value = "";
  fileInput.value = "";
  await loadMyPending();
}

async function loadMyPending() {
  if (!editorToken || !editorLogin) return;

  const res = await fetch(`${api}/news/editor/mine?login=${encodeURIComponent(editorLogin)}`, {
    headers: { "X-Editor-Token": editorToken }
  });
  if (!res.ok) return;

  const data = await res.json();
  const ul = document.getElementById("my-news-list");
  ul.innerHTML = "";

  const render = (items, state) => {
    (items || []).forEach(n => {
      const title = n.title || n.Title;
      const created = n.createdAt || n.CreatedAt;
      const li = document.createElement("li");
      li.innerHTML = `<div><strong>${title}</strong><div style='color:var(--muted)'>${new Date(created).toLocaleString('ru-RU')} • ${state}</div></div>`;
      ul.appendChild(li);
    });
  };

  render(data.pending, "на модерации");
  render(data.published, "опубликовано");
  render(data.rejected, "отклонено");

  if (!ul.children.length) ul.innerHTML = "<li>Пока нет отправленных новостей</li>";
}
