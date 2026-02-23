const api = location.origin;
let editorToken = sessionStorage.getItem("editorToken") || "";
let editorLogin = sessionStorage.getItem("editorLogin") || "";

window.addEventListener("load", async () => {
  const modal = document.getElementById("editor-auth-modal");
  if (editorToken) {
    if (modal) modal.classList.add("hidden");
    document.getElementById("editor-auth-status").textContent = `Вошли как: ${editorLogin}`;
    await loadMyPending();
  } else {
    if (modal) modal.classList.remove("hidden");
  }
});

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

async function loginEditor() {
  const login = (document.getElementById("editor-login").value || "").trim();
  const password = (document.getElementById("editor-password").value || "").trim();
  if (!login || !password) return alert("Введите логин и пароль");

  const ok = await doEditorLogin(login, password);
  if (!ok) return alert("Неверный логин/пароль");

}

async function doEditorLogin(login, password) {
  const res = await fetch(`${api}/auth/editor/login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ login, password })
  });

  if (!res.ok) return false;
  const data = await res.json();
  editorToken = data.token;
  editorLogin = data.login || login;
  sessionStorage.setItem("editorToken", editorToken);
  sessionStorage.setItem("editorLogin", editorLogin);
  document.getElementById("editor-auth-status").textContent = `Вошли как: ${editorLogin}`;
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
        const result = {
          videoWidth: Number(video.videoWidth || 0),
          videoHeight: Number(video.videoHeight || 0)
        };
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
  const videoUrl = (document.getElementById("news-video-url").value || "").trim();
  const videoInput = document.getElementById("news-video-file");
  const photosInput = document.getElementById("news-photos");

  if (!title || !content) return alert("Заполните заголовок и текст");

  try {
    let videoFile = "";
    let videoWidth = 0;
    let videoHeight = 0;
    const video = videoInput.files?.[0];
    if (video)
    {
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

    const payload = { title, content, authorLogin: editorLogin, videoUrl, videoFile, photoFiles, videoWidth, videoHeight };
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
    videoInput.value = "";
    photosInput.value = "";
    await loadMyPending();
  } catch (e) {
    alert(e.message || "Ошибка загрузки файлов");
  }
}

function escapeHtml(str) {
  return (str || "").replace(/[&<>"']/g, s => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[s]));
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
      const rejectReason = n.rejectReason || n.RejectReason || "";
      const reasonHtml = rejectReason ? `<div style='color:#ff9d9d'>Причина: ${escapeHtml(rejectReason)}</div>` : "";
      const li = document.createElement("li");
      li.innerHTML = `<div><strong>${escapeHtml(title)}</strong><div style='color:var(--muted)'>${new Date(created).toLocaleString('ru-RU')} • ${state}</div>${reasonHtml}</div>`;
      ul.appendChild(li);
    });
  };

  render(data.pending, "на модерации");
  render(data.published, "опубликовано");
  render(data.rejected, "отклонено");

  if (!ul.children.length) ul.innerHTML = "<li>Пока нет отправленных новостей</li>";
}
