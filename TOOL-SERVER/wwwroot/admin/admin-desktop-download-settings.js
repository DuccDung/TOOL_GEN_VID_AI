(() => {
  'use strict';
  const shell = window.videoMakerAdminShell;
  const form = document.getElementById('desktopDownloadSettingsForm');
  if (!shell || !form) return;
  const field = document.getElementById('desktopSetupDownloadUrl');
  const errorNode = document.getElementById('desktopSetupDownloadError');
  const status = document.getElementById('desktopDownloadSettingsStatus');
  const save = document.getElementById('desktopDownloadSettingsSave');
  const open = document.getElementById('desktopDownloadSettingsOpen');
  const endpoint = '/api/admin/desktop-download-settings';
  let active = false, epoch = 0, data = null, loaded = false, reader = null, writer = null;
  const user = () => shell.state.user?.userId || shell.state.user?.email || '';
  const current = (generation, expectedUser) => active && generation === epoch && expectedUser === user() &&
    Boolean(shell.state.accessToken) && shell.state.adminReady && shell.state.currentView === 'releases';

  function validLink(value) {
    if (!value || value.length > 2000 || /[\s\u0000-\u001f\u007f-\u009f\\?#]/.test(value) ||
      /%(?![a-f\d]{2})|%2f|%5c/i.test(value)) return false;
    try {
      const decoded = decodeURIComponent(value);
      if (/[\u0000-\u001f\u007f-\u009f\\%?#]/.test(decoded) ||
        decoded.split('/').some(segment => segment === '.' || segment === '..')) return false;
      const url = new URL(value);
      return url.protocol === 'https:' && !url.username && !url.password &&
        !url.search && !url.hash && /\.exe$/i.test(decodeURIComponent(url.pathname)) && url.href.length <= 2000;
    } catch { return false; }
  }
  function clearError() { errorNode.hidden = true; errorNode.textContent = ''; field.removeAttribute('aria-invalid'); }
  function fieldError(message, focus = false) {
    errorNode.textContent = message; errorNode.hidden = false; field.setAttribute('aria-invalid', 'true');
    if (focus) field.focus();
  }
  function controls() {
    const busy = Boolean(reader || writer);
    form.setAttribute('aria-busy', String(busy)); field.disabled = busy || !loaded;
    save.disabled = busy || loaded && Boolean(data?.setupDownloadUrl) && field.value.trim() === data.setupDownloadUrl;
    save.textContent = writer ? 'Đang lưu…' : reader ? 'Đang tải…' : loaded ? 'Lưu' : 'Thử lại';
    open.disabled = busy || !loaded || !validLink(data?.setupDownloadUrl) || field.value.trim() !== data.setupDownloadUrl;
  }
  function settings(response) {
    if (!response || response.setupDownloadUrl !== null && typeof response.setupDownloadUrl !== 'string' ||
      response.revision !== null && typeof response.revision !== 'string') throw new Error('Server trả về cấu hình link Setup không hợp lệ.');
    return response;
  }
  function authFailure(error) {
    if (error.status !== 401) return false;
    shell.showLogin('Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.'); return true;
  }
  async function load({ preserveDraft = false, conflict = false } = {}) {
    if (!active || writer) return;
    const draft = preserveDraft ? field.value : null;
    reader?.abort(); const operation = new AbortController(); reader = operation;
    const generation = ++epoch, expectedUser = user(); loaded = false;
    clearError(); status.textContent = 'Đang tải link Setup…'; controls();
    const timeout = setTimeout(() => operation.abort(), 30000);
    try {
      const result = settings(await shell.api(endpoint, { signal: operation.signal }));
      if (!current(generation, expectedUser)) return;
      data = result; loaded = true; field.value = draft ?? result.setupDownloadUrl ?? '';
      status.textContent = conflict ? 'Link đã được sửa ở phiên khác. Đã tải cấu hình mới; hãy kiểm tra trước khi lưu lại.'
        : result.setupDownloadUrl ? 'Đã tải link đang lưu. File tại link chưa được kiểm tra.' : 'Chưa có link tải Setup. Nhập link rồi bấm Lưu.';
      if (result.setupDownloadUrl && !validLink(result.setupDownloadUrl)) fieldError('Link đang lưu không hợp lệ. Hãy nhập lại link HTTPS trực tiếp tới file .exe.');
    } catch (error) {
      if (!current(generation, expectedUser) || authFailure(error)) return;
      status.textContent = error.name === 'AbortError' ? 'Không tải được link trong thời gian chờ. Bấm Thử lại.' : error.message || 'Không tải được link Setup. Bấm Thử lại.';
    } finally {
      clearTimeout(timeout);
      if (reader === operation) { reader = null; controls(); }
    }
  }
  form.addEventListener('submit', async event => {
    event.preventDefault(); if (!active || reader || writer || !shell.state.adminReady) return;
    if (!loaded) return load();
    clearError();
    const value = field.value.trim();
    if (!validLink(value)) { fieldError('Nhập link HTTPS trực tiếp tới file .exe trên VPS, không chứa tài khoản, query hoặc fragment.', true); return; }
    const operation = new AbortController(), generation = epoch, expectedUser = user(); writer = operation; controls();
    status.textContent = 'Đang lưu link tải Setup…';
    let reloadConflict = false, focusInvalidField = false;
    const timeout = setTimeout(() => operation.abort(), 30000);
    try {
      const result = settings(await shell.api(endpoint, { method: 'POST', body: JSON.stringify({ setupDownloadUrl: value, revision: data?.revision ?? null }), signal: operation.signal }));
      if (!current(generation, expectedUser)) return;
      if (!validLink(result.setupDownloadUrl)) throw new Error('Server chưa xác nhận đúng link Setup đã lưu.');
      data = result; field.value = result.setupDownloadUrl ?? ''; loaded = true;
      status.textContent = 'Đã lưu link tải Setup. File tại link chưa được kiểm tra.';
    } catch (error) {
      if (!current(generation, expectedUser) || authFailure(error)) return;
      if (error.status === 409) reloadConflict = true;
      else if (error.status === 400) {
        fieldError(error.message || 'Link Setup chưa hợp lệ.'); focusInvalidField = true;
        status.textContent = 'Chưa lưu link Setup.';
      }
      else {
        loaded = false;
        status.textContent = error.name === 'AbortError' ? 'Lượt lưu chưa rõ kết quả. Bấm Thử lại để tải cấu hình mới.'
          : error.status ? `${error.message} Bấm Thử lại để tải cấu hình mới trước khi lưu lại.`
          : 'Chưa xác nhận được kết quả lưu. Bấm Thử lại để tải cấu hình mới.';
      }
    } finally {
      clearTimeout(timeout);
      if (writer === operation) {
        writer = null; controls();
        if (focusInvalidField && current(generation, expectedUser)) field.focus();
        if (reloadConflict && current(generation, expectedUser)) await load({ preserveDraft: true, conflict: true });
        else if (active && generation !== epoch && expectedUser === user()) await load();
      }
    }
  });
  field.addEventListener('input', () => { clearError(); controls(); });
  open.addEventListener('click', () => {
    if (!open.disabled && active && loaded && validLink(data?.setupDownloadUrl) && field.value.trim() === data.setupDownloadUrl)
      window.open(data.setupDownloadUrl, '_blank', 'noopener,noreferrer');
  });
  function deactivate() { active = false; epoch++; reader?.abort(); reader = null; loaded = false; controls(); }
  function reset() {
    deactivate(); writer?.abort(); writer = null; data = null; field.value = ''; clearError();
    status.textContent = 'Chưa tải link Setup.'; controls();
  }
  window.videoMakerDesktopDownloadSettings = Object.freeze({
    activate: () => { active = true; if (writer) { status.textContent = 'Đang chờ lượt lưu hoàn tất…'; controls(); } else load(); },
    refresh: () => { if (active) load(); },
    deactivate, reset
  });
  if (shell.state.adminReady && shell.state.currentView === 'releases') window.videoMakerDesktopDownloadSettings.activate();
})();
