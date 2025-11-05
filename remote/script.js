let currentFolder = "data/media";

function showTab(i) {
    document.querySelectorAll('.tabs button').forEach((b, j) => b.classList.toggle('active', i === j));
    document.querySelectorAll('section').forEach((s, j) => s.classList.toggle('active', i === j));
    if (i === 0) loadConfig();
    if (i === 1) loadFiles();
}

async function loadConfig() {
    const res = await fetch('/config');
    const txt = await res.text();
    document.getElementById('configArea').value = txt;
}

async function saveConfig() {
    const data = document.getElementById('configArea').value;
    await fetch('/config', { method: 'POST', body: data });
    alert('✅ Настройки сохранены');
}

function setFolder(path) {
    currentFolder = path;
    document.getElementById('currentFolder').querySelector('span').textContent = path;
    document.querySelectorAll('#categoryButtons button').forEach(b => b.classList.remove('active'));
    document.getElementById('cat_' + path.split('/')[1]).classList.add('active');
    loadFiles();
}

async function loadFiles() {
    const res = await fetch('/files?folder=' + encodeURIComponent(currentFolder));
    const arr = await res.json();
    const list = document.getElementById('fileList');
    list.innerHTML = '';
    arr.forEach(f => {
        const li = document.createElement('li');
        li.textContent = f.split('/').pop();
        const delBtn = document.createElement('button');
        delBtn.textContent = 'Удалить';
        delBtn.onclick = () => deleteFile(li.textContent);
        li.appendChild(delBtn);
        list.appendChild(li);
    });
}

async function uploadFile() {
    const files = document.getElementById('fileInput').files;
    if (!files.length) return alert('Выберите файлы!');
    for (const file of files) {
        const res = await fetch('/upload?folder=' + encodeURIComponent(currentFolder), {
            method: 'POST',
            headers: { 'X-Filename': file.name },
            body: file
        });
        if (res.ok) console.log(`✅ Загружен ${file.name}`);
        else alert(`Ошибка при загрузке ${file.name}`);
    }
    loadFiles();
}

async function deleteFile(name) {
    await fetch('/files?folder=' + encodeURIComponent(currentFolder) + '&name=' + encodeURIComponent(name), { method: 'DELETE' });
    loadFiles();
}

async function restartApp() {
    await fetch('/restart');
    alert('Приложение перезапускается...');
}

// Drag & Drop
const dropZone = document.getElementById('dropZone');
dropZone.addEventListener('dragover', e => { e.preventDefault(); dropZone.classList.add('dragover'); });
dropZone.addEventListener('dragleave', () => dropZone.classList.remove('dragover'));
dropZone.addEventListener('drop', e => {
    e.preventDefault();
    dropZone.classList.remove('dragover');
    document.getElementById('fileInput').files = e.dataTransfer.files;
    uploadFile();
});

loadConfig();
