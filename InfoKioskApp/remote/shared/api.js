/* shared/api.js — обёртка над fetch с поддержкой токенов */

window.API = (() => {
  // Читаем токены из localStorage ИЛИ sessionStorage — в WebView2 иногда
  // localStorage недоступен (приватный режим / настройки безопасности),
  // и admin-access.js сохраняет токен в оба хранилища для надёжности.
  function readToken(key) {
    try {
      let v = localStorage.getItem(key);
      if (v) return v;
    } catch {}
    try {
      let v = sessionStorage.getItem(key);
      if (v) return v;
    } catch {}
    return '';
  }

  let adminToken  = readToken('infokiosk.adminToken')  || '';
  let editorToken = readToken('infokiosk.editorToken') || '';

  async function request(path, { method = 'GET', body, raw = false, headers = {} } = {}) {
    const opts = { method, headers: { ...headers } };
    if (body !== undefined) {
      if (body instanceof FormData) {
        opts.body = body;
      } else {
        opts.headers['Content-Type'] = 'application/json';
        opts.body = JSON.stringify(body);
      }
    }
    if (adminToken)  opts.headers['X-Admin-Token']  = adminToken;
    if (editorToken) opts.headers['X-Editor-Token'] = editorToken;

    const res = await fetch(path, opts);
    if (raw) return res;
    if (!res.ok) {
      let msg = res.statusText;
      try { const j = await res.json(); msg = j.error || j.message || msg; } catch {}

      // 401 на защищённом запросе — токен невалиден. Чистим и перезагружаем,
      // чтобы пользователь увидел экран входа, а не пустую панель.
      if (res.status === 401) {
        const wasAdmin  = !!adminToken;
        const wasEditor = !!editorToken;
        adminToken = '';
        try { localStorage.removeItem('infokiosk.adminToken'); } catch {}
        try { sessionStorage.removeItem('infokiosk.adminToken'); } catch {}
        editorToken = '';
        try { localStorage.removeItem('infokiosk.editorToken'); } catch {}
        try { sessionStorage.removeItem('infokiosk.editorToken'); } catch {}
        // Логин-эндпоинты сами возвращают 401 — в этом случае НЕ перезагружаем,
        // ошибка должна показаться в форме. Перезагрузка нужна только если токен был.
        if (wasAdmin && !path.endsWith('/auth/admin/login')) {
          setTimeout(() => location.reload(), 50);
        } else if (wasEditor && !path.endsWith('/auth/editor/login')) {
          setTimeout(() => location.reload(), 50);
        }
      }

      throw new Error(msg);
    }
    const ct = res.headers.get('Content-Type') || '';
    if (ct.includes('application/json')) return res.json();
    return res.text();
  }

  return {
    // Auth
    adminLogin(pin)   { return request('/auth/admin/login',  { method:'POST', body: { pin } }); },
    editorLogin(login, password) { return request('/auth/editor/login', { method:'POST', body: { login, password } }); },
    setAdminToken(t)  {
      adminToken = t;
      try { localStorage.setItem('infokiosk.adminToken', t); } catch {}
      try { sessionStorage.setItem('infokiosk.adminToken', t); } catch {}
    },
    setEditorToken(t) {
      editorToken = t;
      try { localStorage.setItem('infokiosk.editorToken', t); } catch {}
      try { sessionStorage.setItem('infokiosk.editorToken', t); } catch {}
    },
    clearAdminToken()  {
      adminToken = '';
      try { localStorage.removeItem('infokiosk.adminToken'); } catch {}
      try { sessionStorage.removeItem('infokiosk.adminToken'); } catch {}
    },
    clearEditorToken() {
      editorToken = '';
      try { localStorage.removeItem('infokiosk.editorToken'); } catch {}
      try { sessionStorage.removeItem('infokiosk.editorToken'); } catch {}
    },
    isAdmin()  { return !!adminToken;  },
    isEditor() { return !!editorToken; },

    // News
    newsPending()   { return request('/news/admin/pending'); },
    newsPublish(id) { return request('/news/admin/publish', { method:'POST', body: { id } }); },
    newsReject(id, reason) { return request('/news/admin/reject', { method:'POST', body: { id, reason } }); },
    newsDelete(id)  { return request('/news/admin/delete', { method:'POST', body: { id } }); },
    newsPublished() { return request('/news/published'); },
    newsSubmit(data) { return request('/news/editor/submit', { method:'POST', body: data }); },
    newsMine()      { return request('/news/editor/mine'); },

    // Honor
    honorList()      { return request('/honor/list'); },
    honorSave(p)     { return request('/honor/save', { method:'POST', body: p }); },
    honorDelete(id)  { return request('/honor/delete', { method:'POST', body: { id } }); },

    // Calendar
    calendarList()   { return request('/calendar/list'); },
    calendarAdd(ev)  { return request('/calendar/add', { method:'POST', body: ev }); },
    calendarDelete(id) { return request('/calendar/delete', { method:'POST', body: { id } }); },

    // Files
    filesList(target) { return request('/list?target=' + encodeURIComponent(target)); },
    filesUpload(target, formData) {
      return request('/upload?target=' + encodeURIComponent(target), { method:'POST', body: formData });
    },
    filesDelete(target, name) {
      return request('/delete', { method:'POST', body: { target, name } });
    },

    // Config
    configGet()      { return request('/config'); },
    configSave(cfg)  { return request('/config', { method:'POST', body: cfg }); },

    // System
    systemVolume(v)  { return request('/system/volume?v=' + encodeURIComponent(v), { method:'POST' }); },

    request
  };
})();
