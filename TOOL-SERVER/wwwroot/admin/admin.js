(() => {
'use strict';
if (!window.videoMakerAdminPages || !window.videoMakerAdminDialogs || document.body.dataset.adminUiGeneration !== '2') {
  document.getElementById('adminShell')?.classList.add('hidden');
  const shellElement = document.getElementById('adminShell');
  if (shellElement) shellElement.hidden = true;
  const note = document.getElementById('loginMessage');
  if (note) note.textContent = 'Trang quản trị chưa được cập nhật đầy đủ. Hãy nạp bản server mới rồi tải lại trang.';
  const button = document.querySelector('#loginForm button[type="submit"]');
  if (button) button.disabled = true;
  return;
}
const state = {
  accessToken: sessionStorage.getItem('vmAdminAccessToken'),
  refreshToken: sessionStorage.getItem('vmAdminRefreshToken'),
  user: readJson(sessionStorage.getItem('vmAdminUser')),
  overview: null,
  users: [],
  usersPaging: { page: 1, pageSize: 20, totalCount: 0, totalPages: 0, hasPrevious: false, hasNext: false },
  userSearch: '',
  plans: [],
  releases: [],
  releasesPaging: { page: 1, pageSize: 20, totalCount: 0, totalPages: 0, hasPrevious: false, hasNext: false },
  selectedUser: null,
  currentView: 'overview',
  adminReady: false,
  readErrors: {},
  refreshing: null
};

const loginScreen = document.getElementById('loginScreen');
const adminShell = document.getElementById('adminShell');
const panels = [...document.querySelectorAll('[data-panel]')];
const pageMeta = {
  overview: ['KHÔNG GIAN QUẢN TRỊ', 'Tổng quan', 'Tạo gói, chuẩn bị vận hành và quản lý khách hàng.'],
  users: ['ACCOUNTS & ACCESS', 'Người dùng', 'Cấp, gia hạn hoặc thu hồi quyền sử dụng theo từng tài khoản.'],
  organizations: ['ORGANIZATION & AI', 'Tổ chức & AI', 'Quản lý thành viên, ngân sách, credential, usage và bảng giá AI.'],
  tiktok: ['SYSTEM INTEGRATION', 'Tích hợp TikTok', 'Quản lý TikTok Developer App, kiểm tra OAuth và giới hạn đăng công khai.'],
  plans: ['LICENSE POLICY', 'Gói sử dụng', 'Thiết lập thời hạn, số thiết bị và số phiên chạy đồng thời.'],
  payments: ['THANH TOÁN SEPAY', 'Tài khoản nhận thanh toán', 'Quản lý ngân hàng nhận tiền cho các đơn mua gói.'],
  releases: ['DESKTOP DISTRIBUTION', 'Phát hành desktop', 'Quản lý package, bộ cài và chính sách cập nhật VideoMaker.']
};

function readJson(value) {
  try { return value ? JSON.parse(value) : null; } catch { return null; }
}

function saveSession(tokenResponse) {
  state.accessToken = tokenResponse.accessToken;
  state.refreshToken = tokenResponse.refreshToken;
  state.user = tokenResponse.user;
  sessionStorage.setItem('vmAdminAccessToken', state.accessToken);
  sessionStorage.setItem('vmAdminRefreshToken', state.refreshToken);
  sessionStorage.setItem('vmAdminUser', JSON.stringify(state.user));
}

function clearSession() {
  state.accessToken = null;
  state.refreshToken = null;
  state.user = null;
  sessionStorage.removeItem('vmAdminAccessToken');
  sessionStorage.removeItem('vmAdminRefreshToken');
  sessionStorage.removeItem('vmAdminUser');
}

function getFingerprint() {
  let value = localStorage.getItem('vmAdminDeviceId');
  if (!value) {
    value = crypto.randomUUID ? crypto.randomUUID() : `${Date.now()}-${Math.random().toString(16).slice(2)}`;
    localStorage.setItem('vmAdminDeviceId', value);
  }
  return value;
}

function devicePayload() {
  return {
    fingerprint: getFingerprint(),
    deviceName: `Admin Web - ${navigator.platform || 'Browser'}`,
    operatingSystem: navigator.userAgent.slice(0, 200),
    applicationVersion: 'web-admin/2.0'
  };
}

async function parseResponse(response) {
  if (response.status === 204) return null;
  const text = await response.text();
  if (!text) return null;
  try { return JSON.parse(text); } catch { return text; }
}

function errorMessage(payload, fallback = 'Không thể hoàn tất yêu cầu.', status = 0) {
  const statuses = {
    401: 'Phiên đăng nhập đã hết hạn (HTTP 401). Vui lòng đăng nhập lại.',
    403: 'Yêu cầu bị từ chối (HTTP 403). Kiểm tra quyền Admin và cấu hình truy cập trên hosting.',
    404: 'Không tìm thấy API (HTTP 404). Kiểm tra bản server đã deploy.',
    405: 'Hosting không chấp nhận phương thức gửi yêu cầu (HTTP 405). Kiểm tra cấu hình API.'
  };
  const statusFallback = statuses[status] || (status ? `${fallback} (HTTP ${status}).` : fallback);
  if (!payload) return statusFallback;
  const fieldMessage = typeof payload === 'object' && payload.errors && Object.values(payload.errors).flat().find(value => typeof value === 'string');
  const message = typeof payload === 'string' ? payload : fieldMessage || payload.message;
  if (typeof message !== 'string' || /<\s*!doctype|<\/?(?:html|head|body|title|style|script|div|h[1-6])\b/i.test(message)) return statusFallback;
  return message.slice(0, 1000);
}

async function refreshAccessToken() {
  if (!state.refreshToken) return false;
  if (state.refreshing) return state.refreshing;
  state.refreshing = (async () => {
    try {
      const response = await fetch('/api/auth/refresh', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ refreshToken: state.refreshToken })
      });
      if (!response.ok) return false;
      saveSession(await parseResponse(response));
      return true;
    } catch {
      return false;
    } finally {
      state.refreshing = null;
    }
  })();
  return state.refreshing;
}

async function api(path, options = {}, retry = true) {
  const headers = new Headers(options.headers || {});
  if (state.accessToken) headers.set('Authorization', `Bearer ${state.accessToken}`);
  if (options.body && !(options.body instanceof FormData) && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json');
  const response = await fetch(path, { ...options, headers });
  if (response.status === 401 && retry && await refreshAccessToken()) return api(path, options, false);
  const payload = await parseResponse(response);
  if (!response.ok) {
    const error = new Error(errorMessage(payload, 'Không thể hoàn tất yêu cầu.', response.status));
    error.status = response.status;
    error.payload = payload;
    throw error;
  }
  return payload;
}

function escapeHtml(value) {
  return String(value ?? '').replace(/[&<>'"]/g, character => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;'
  })[character]);
}

function aiApiKeyLink(providerCode) {
  const providers = [
    { code: 'openai', name: 'OpenAI', url: 'https://platform.openai.com/api-keys' },
    { code: 'fal', name: 'Fal', url: 'https://fal.ai/dashboard/keys' },
    { code: 'kling', name: 'Kling', url: 'https://kling.ai/dev/api-key' },
    { code: 'byteplus', name: 'BytePlus', url: 'https://ai.byteplus.com/ark/region:ap-southeast-1/apikey' }
  ];
  const provider = providers.find(item => item.code === String(providerCode ?? '').trim().toLowerCase());
  if (!provider) return '';
  return `<a href="${provider.url}" target="_blank" rel="noopener noreferrer" aria-label="Tạo API key ${provider.name} (mở tab mới)">Tạo API key ${provider.name} <span aria-hidden="true">↗</span></a>`;
}

function icon(name, className = '') {
  return `<svg class="ui-icon${className ? ` ${className}` : ''}" aria-hidden="true"><use href="#icon-${name}"></use></svg>`;
}

function formatDate(value, dateOnly = false) {
  if (!value) return '—';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '—';
  return new Intl.DateTimeFormat('vi-VN', dateOnly ? { dateStyle: 'short' } : { dateStyle: 'short', timeStyle: 'short' }).format(date);
}

function formatBytes(value) {
  let size = Number(value || 0), unit = 0;
  const units = ['B', 'KB', 'MB', 'GB'];
  while (size >= 1024 && unit < units.length - 1) { size /= 1024; unit += 1; }
  return `${size.toFixed(unit ? 1 : 0)} ${units[unit]}`;
}

function statusClass(status) {
  const value = String(status || '').toLowerCase();
  if (['active', 'trial', 'healthy', 'completed'].includes(value)) return 'status-healthy';
  if (['revoked', 'suspended', 'failed', 'expired'].includes(value)) return 'status-failed';
  if (['processing', 'submitted', 'queued', 'warning'].includes(value)) return 'status-warning';
  return '';
}

function licenseState(license) {
  if (!license) return { label: 'Chưa có gói', css: '' };
  const expired = license.expiresAtUtc && new Date(license.expiresAtUtc) <= new Date();
  return { label: expired ? 'Expired' : license.status, css: statusClass(expired ? 'Expired' : license.status) };
}

function toast(message, error = false) {
  const element = document.createElement('div');
  element.className = `toast${error ? ' error' : ''}`;
  element.innerHTML = `${icon(error ? 'x' : 'circle-check')}<span>${escapeHtml(message)}</span>`;
  document.getElementById('toastStack').appendChild(element);
  window.setTimeout(() => element.remove(), 3800);
}

function setBusy(button, busy, text) {
  if (!button) return;
  if (!button.dataset.idleHtml) button.dataset.idleHtml = button.innerHTML;
  button.disabled = busy;
  if (busy) button.textContent = text;
  else button.innerHTML = button.dataset.idleHtml;
}

// Dynamic admin panels briefly replace their content with a loading state; keep the viewport stable.
function capturePagePosition() {
  const scrollingElement = document.scrollingElement || document.documentElement;
  return {
    left: window.scrollX || scrollingElement.scrollLeft || 0,
    top: window.scrollY || scrollingElement.scrollTop || 0
  };
}

function restorePagePosition(position) {
  if (!position) return;
  const scrollingElement = document.scrollingElement || document.documentElement;
  scrollingElement.scrollLeft = position.left;
  scrollingElement.scrollTop = position.top;
  if (typeof window.scrollTo === 'function') window.scrollTo({ left: position.left, top: position.top, behavior: 'auto' });
}

function preservePagePosition(action) {
  const position = capturePagePosition();
  let result;
  try {
    result = action();
  } catch (error) {
    restorePagePosition(position);
    throw error;
  }
  if (result && typeof result.then === 'function') return result.finally(() => restorePagePosition(position));
  restorePagePosition(position);
  return result;
}

function paginationMarkup(pagination, key, label) {
  if (!pagination || pagination.totalPages <= 1) return '';
  const page = Number(pagination.page || 1);
  const totalPages = Number(pagination.totalPages || 0);
  const pageSize = Number(pagination.pageSize || 20);
  const totalCount = Number(pagination.totalCount || 0);
  const first = (page - 1) * pageSize + 1;
  const last = Math.min(page * pageSize, totalCount);
  const start = Math.max(1, Math.min(page - 2, totalPages - 4));
  const end = Math.min(totalPages, start + 4);
  const buttons = [];
  for (let value = start; value <= end; value += 1) {
    buttons.push(`<button type="button" class="pagination-page${value === page ? ' active' : ''}" data-page="${value}" aria-current="${value === page ? 'page' : 'false'}">${value}</button>`);
  }
  return `<nav class="admin-pagination" data-pagination="${escapeHtml(key)}" aria-label="Phân trang ${escapeHtml(label)}"><span class="pagination-summary">${first}–${last} trên ${totalCount}</span><div class="pagination-controls"><button type="button" class="pagination-page pagination-edge" data-page="1" ${pagination.hasPrevious ? '' : 'disabled'} aria-label="Trang đầu">«</button><button type="button" class="pagination-page pagination-edge" data-page="${Math.max(1, page - 1)}" ${pagination.hasPrevious ? '' : 'disabled'} aria-label="Trang trước">‹</button>${buttons.join('')}<button type="button" class="pagination-page pagination-edge" data-page="${Math.min(totalPages, page + 1)}" ${pagination.hasNext ? '' : 'disabled'} aria-label="Trang sau">›</button><button type="button" class="pagination-page pagination-edge" data-page="${totalPages}" ${pagination.hasNext ? '' : 'disabled'} aria-label="Trang cuối">»</button></div><label class="pagination-size">Hiển thị<select data-page-size><option value="10" ${pageSize === 10 ? 'selected' : ''}>10</option><option value="20" ${pageSize === 20 ? 'selected' : ''}>20</option><option value="50" ${pageSize === 50 ? 'selected' : ''}>50</option><option value="100" ${pageSize === 100 ? 'selected' : ''}>100</option></select></label></nav>`;
}

function showLogin(message = '') {
  state.adminReady = false;
  window.videoMakerAdminSetup?.reset();
  window.videoMakerOrganizationAdmin?.resetSetupNavigation();
  window.videoMakerPlanComposer?.reset();
  window.videoMakerTikTokAdmin?.reset();
  window.videoMakerSepayAdmin?.reset();
  window.videoMakerReleaseAdmin?.reset();
  window.videoMakerDesktopDownloadSettings?.reset();
  clearSession();
  window.videoMakerAdminDialogs.cancel();
  document.querySelectorAll('dialog[open]').forEach(dialog => dialog.close());
  setTopbarVisible(true);
  loginScreen.classList.remove('hidden');
  adminShell.classList.add('hidden');
  loginScreen.hidden = false;
  adminShell.hidden = true;
  document.getElementById('loginMessage').textContent = message;
}

function showAdmin() {
  if (!state.adminReady) return;
  loginScreen.classList.add('hidden');
  adminShell.classList.remove('hidden');
  loginScreen.hidden = true;
  adminShell.hidden = false;
  setTopbarVisible(true);
  const displayName = state.user?.displayName || state.user?.email || 'Administrator';
  document.getElementById('adminName').textContent = displayName;
  document.getElementById('adminName').title = state.user?.email || displayName;
  document.getElementById('adminEmail').textContent = state.user?.email || '';
  document.getElementById('adminAvatar').textContent = displayName.split(/\s+/).slice(0, 2).map(x => x[0]).join('').toUpperCase() || 'A';
  navigate('overview');
  window.dispatchEvent(new Event('videomaker:admin-ready'));
}

async function loadAll() {
  return preservePagePosition(async () => {
  const requests = [
    ['overview', api('/api/admin/licenses/overview')],
    ['plans', api('/api/admin/licenses/plans')],
    ['users', api('/api/admin/licenses/users/page?page=1&pageSize=20')],
    ['releases', api('/api/admin/desktop-releases/page?page=1&pageSize=20')]
  ];
  const results = await Promise.allSettled(requests.map(([, request]) => request));
  const failures = [];
  results.forEach((result, index) => {
    const key = requests[index][0];
    if (result.status === 'fulfilled') {
      delete state.readErrors[key];
      if (key === 'users' && result.value && !Array.isArray(result.value)) {
        state.users = result.value.items || [];
        state.usersPaging = result.value;
      } else if (key === 'releases' && result.value && !Array.isArray(result.value)) {
        state.releases = result.value.items || [];
        state.releasesPaging = result.value;
      } else {
        state[key] = result.value;
      }
    }
    else { state.readErrors[key] = true; failures.push(result.reason); }
  });
  const authenticationError = failures.find(error => error?.status === 401 || error?.status === 403);
  if (authenticationError?.status === 401) return showLogin('Phiên đăng nhập đã hết hạn.');
  if (authenticationError?.status === 403) return showLogin('Tài khoản này chưa có quyền Admin.');
  if (!state.adminReady && results.every(result => result.status === 'rejected')) {
    document.getElementById('loginMessage').textContent = 'Chưa kết nối được hệ thống quản trị. Hãy thử đăng nhập lại.';
    return;
  }
  renderAll();
  state.adminReady = true;
  const refreshLabel = document.getElementById('adminLastRefresh');
  if (refreshLabel) refreshLabel.textContent = `Cập nhật ${new Intl.DateTimeFormat('vi-VN', { hour: '2-digit', minute: '2-digit' }).format(new Date())}`;
  if (failures.length) toast(`Có ${failures.length} khu vực chưa tải được. Bạn có thể thử làm mới lại.`, true);
  window.dispatchEvent(new Event('videomaker:admin-ready'));
  });
}

function renderAll() {
  renderOverview();
  renderUsers();
  renderPlans();
  renderReleases();
  renderGrantPlans();
}

function viewContext() { return { state, escapeHtml, icon, initials, licenseState, statusClass, formatDate, formatBytes, paginationMarkup }; }

function renderOverview() { window.videoMakerAdminPages.overview(viewContext()); }

function initials(user) {
  const value = user.displayName || user.email || 'U';
  return value.split(/\s+/).slice(0, 2).map(x => x[0]).join('').toUpperCase();
}

function renderUsers() { window.videoMakerAdminPages.users(viewContext()); }

async function loadUsersPage(page = state.usersPaging.page, pageSize = state.usersPaging.pageSize) {
  return preservePagePosition(async () => {
    const root = document.getElementById('userTable');
    root.innerHTML = '<div class="organization-loading empty-state">Đang tải danh sách người dùng...</div>';
    const search = state.userSearch.trim();
    const query = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
    if (search) query.set('search', search);
    try {
      const data = await api(`/api/admin/licenses/users/page?${query.toString()}`);
      state.users = data.items || [];
      state.usersPaging = data;
      renderUsers();
    } catch (error) {
      root.innerHTML = `<div class="empty-state">${escapeHtml(error.message)}</div>`;
      throw error;
    }
  });
}

function renderPlans() { window.videoMakerAdminPages.plans(viewContext()); }

function renderReleases() { window.videoMakerAdminPages.releases(viewContext()); }

async function loadReleasesPage(page = state.releasesPaging.page, pageSize = state.releasesPaging.pageSize) {
  return preservePagePosition(async () => {
    const root = document.getElementById('releaseTable');
    root.innerHTML = '<div class="organization-loading empty-state">Đang tải desktop release...</div>';
    try {
      const data = await api(`/api/admin/desktop-releases/page?page=${page}&pageSize=${pageSize}`);
      state.releases = data.items || [];
      state.releasesPaging = data;
      renderReleases();
    } catch (error) {
      root.innerHTML = `<div class="empty-state">${escapeHtml(error.message)}</div>`;
      throw error;
    }
  });
}

function renderGrantPlans() {
  document.getElementById('grantPlan').innerHTML = state.plans.filter(x => x.isActive).map(plan => `<option value="${plan.licensePlanId}" data-days="${plan.defaultDurationDays || 30}">${escapeHtml(plan.name)}${plan.defaultDurationDays ? ` · ${plan.defaultDurationDays} ngày` : ''}</option>`).join('');
}

function setPageMeta(_eyebrow, title, subtitle) {
  document.getElementById('pageTitle').textContent = title;
  document.getElementById('pageSubtitle').textContent = subtitle;
  document.getElementById('breadcrumbCurrent').textContent = title;
}

function setSetupReturn(visible) {
  document.getElementById('returnToSetupButton').classList.toggle('hidden', !visible);
}

function setTopbarVisible(visible) {
  document.querySelector('.topbar')?.classList.toggle('hidden', !visible);
}

function setOrganizationMenuExpanded(expanded) {
  const parent = document.querySelector('[data-nav-parent="organizations"]');
  const submenu = document.getElementById('organizationSubmenu');
  if (!parent || !submenu) return;
  parent.setAttribute('aria-expanded', String(expanded));
  submenu.classList.toggle('hidden', !expanded);
}

function navigate(view, options = {}) {
  if (!pageMeta[view]) return;
  if (view !== 'organizations') window.videoMakerAdminSetup?.deactivate();
  if (view !== 'plans') window.videoMakerPlanComposer?.deactivate();
  if (state.currentView === 'tiktok' && view !== 'tiktok') window.videoMakerTikTokAdmin?.deactivate();
  if (state.currentView === 'payments' && view !== 'payments') window.videoMakerSepayAdmin?.deactivate();
  if (state.currentView === 'releases' && view !== 'releases') {
    window.videoMakerReleaseAdmin?.deactivate();
    window.videoMakerDesktopDownloadSettings?.deactivate();
  }
  state.currentView = view;
  setTopbarVisible(true);
  if (!options.keepSetupReturn) setSetupReturn(false);
  document.querySelectorAll('.nav-item').forEach(item => {
    item.classList.toggle('active', item.dataset.view === view);
    if (item.dataset.view === view) item.setAttribute('aria-current', 'page');
    else item.removeAttribute('aria-current');
  });
  document.getElementById('manageUsersShortcut').classList.toggle('hidden', view !== 'overview');
  if (view !== 'organizations') setNavigationOpen(false);
  panels.forEach(panel => panel.classList.toggle('hidden', panel.dataset.panel !== view));
  const [eyebrow, title, subtitle] = pageMeta[view];
  setPageMeta(eyebrow, title, subtitle);
  if (view === 'organizations') {
    setOrganizationMenuExpanded(options.organizationMenuExpanded ?? true);
    return window.videoMakerOrganizationAdmin?.activate(options.organizationScope);
  } else {
    setOrganizationMenuExpanded(false);
    if (view === 'plans') return window.videoMakerPlanComposer?.activate();
    if (view === 'tiktok') return window.videoMakerTikTokAdmin?.activate();
    if (view === 'payments') return window.videoMakerSepayAdmin?.activate();
    if (view === 'releases') {
      window.videoMakerDesktopDownloadSettings?.activate();
      return window.videoMakerReleaseAdmin?.activate();
    }
  }
}

function setNavigationOpen(open) {
  document.querySelector('.sidebar').classList.toggle('is-navigation-open', open);
  document.getElementById('adminMenuToggle').setAttribute('aria-expanded', String(open));
}

async function searchUsers(value) {
  state.userSearch = value.trim();
  document.getElementById('userSearch').value = state.userSearch;
  navigate('users');
  try { await loadUsersPage(1, state.usersPaging.pageSize); }
  catch (error) { toast(error.message, true); }
}

function toLocalDateTime(value) {
  if (!value) return '';
  const date = new Date(value);
  const offset = date.getTimezoneOffset() * 60000;
  return new Date(date.getTime() - offset).toISOString().slice(0, 16);
}

function openGrantDialog(userId) {
  const form = document.getElementById('grantForm');
  form.reset();
  form.querySelector('.dialog-message').textContent = '';
  document.getElementById('grantUserId').value = userId;
  renderGrantPlans();
  syncGrantDuration();
  document.getElementById('grantDialog').showModal();
}

function syncGrantDuration() {
  const option = document.getElementById('grantPlan').selectedOptions[0];
  document.getElementById('grantDuration').value = option?.dataset.days || 30;
}

let releaseImportedSetup = null;
let releaseWasPublished = false;
let releaseEditing = null;
let releaseExternalChanged = false;
let releaseImporting = false;
const releaseUploadedFiles = new Map();

function releaseField(id) { return document.getElementById(id); }

function syncReleaseSource() {
  const external = releaseField('releaseSource').value === 'ExternalVps';
  releaseField('releaseInternalFields').hidden = external;
  releaseField('releasePackage').disabled = external || releaseWasPublished;
  releaseField('releaseExternalFields').hidden = !external;
  releaseField('releaseExternalFields').disabled = !external || releaseWasPublished;
}

function releaseError(message, fieldId = null) {
  const summary = releaseField('releaseErrorSummary');
  summary.replaceChildren();
  const text = document.createElement(fieldId ? 'a' : 'span');
  text.textContent = message;
  if (fieldId) {
    text.href = '#' + fieldId;
    text.addEventListener('click', event => { event.preventDefault(); releaseField(fieldId).focus(); });
    releaseField(fieldId).setAttribute('aria-invalid', 'true');
    const inline = document.createElement('small');
    inline.id = fieldId + 'Error'; inline.dataset.releaseError = fieldId; inline.className = 'form-message'; inline.textContent = message;
    releaseField(fieldId).insertAdjacentElement('afterend', inline);
    const describedBy = releaseField(fieldId).getAttribute('aria-describedby');
    releaseField(fieldId).setAttribute('aria-describedby', [describedBy, inline.id].filter(Boolean).join(' '));
  }
  summary.appendChild(text); summary.hidden = false; summary.focus();
}

function clearReleaseErrors() {
  releaseField('releaseErrorSummary').hidden = true;
  releaseField('releaseForm').querySelectorAll('[aria-invalid]').forEach(field => field.removeAttribute('aria-invalid'));
  releaseField('releaseForm').querySelectorAll('[data-release-error]').forEach(error => {
    const field = releaseField(error.dataset.releaseError);
    const describedBy = (field.getAttribute('aria-describedby') || '').split(' ').filter(id => id !== error.id).join(' ');
    if (describedBy) field.setAttribute('aria-describedby', describedBy); else field.removeAttribute('aria-describedby');
    error.remove();
  });
  releaseField('releaseForm').querySelector('.dialog-message').textContent = '';
}

function readReleaseExternal() {
  const fail = (message, fieldId) => { const error = new Error(message); error.fieldId = fieldId; throw error; };
  const url = releaseField('releaseExternalUrl').value.trim();
  let parsed;
  try { parsed = new URL(url); } catch { fail('Nhập URL HTTPS của ZIP ứng dụng.', 'releaseExternalUrl'); }
  if (parsed.protocol !== 'https:' || parsed.username || parsed.password || parsed.search || parsed.hash) {
    fail('URL phải dùng HTTPS, không chứa tài khoản, query hoặc fragment.', 'releaseExternalUrl');
  }
  const fileName = releaseField('releaseExternalFileName').value.trim();
  if (!/^[^\\/:*?"<>|\x00-\x1f]+\.zip$/i.test(fileName)) fail('Tên file phải là tên ZIP, không kèm đường dẫn.', 'releaseExternalFileName');
  let manifest;
  try { manifest = JSON.parse(releaseField('releaseExternalManifest').value); }
  catch { fail('Package manifest phải là JSON hợp lệ từ ZIP ứng dụng.', 'releaseExternalManifest'); }
  if (!manifest || manifest.product !== 'VideoMaker' || manifest.version !== releaseField('releaseVersion').value.trim() ||
      manifest.buildNumber !== Number(releaseField('releaseBuild').value) || manifest.platform !== releaseField('releasePlatform').value.trim() ||
      !Array.isArray(manifest.managedFiles) || !manifest.managedFiles.length) {
    fail('Manifest không khớp phiên bản/build/platform hoặc thiếu danh sách file.', 'releaseExternalManifest');
  }
  return { schemaVersion: 1, version: releaseField('releaseVersion').value.trim(), buildNumber: Number(releaseField('releaseBuild').value),
    channel: releaseField('releaseChannel').value, platform: releaseField('releasePlatform').value.trim(), downloadUrl: url, fileName,
    sizeBytes: Number(releaseField('releaseExternalSize').value), sha256: releaseField('releaseExternalHash').value.trim().toLowerCase(),
    packageManifest: manifest, expandedSizeBytes: Number(releaseField('releaseExternalExpanded').value), entryCount: Number(releaseField('releaseExternalEntries').value),
    installMode: 'StaticArchive', distributionProtocol: 2, modelBundleId: 'models-20261016-03' };
}

async function importReleaseRegistration(file) {
  if (!file) return;
  if (file.size > 4 * 1024 * 1024) throw new Error('JSON đăng ký vượt giới hạn 4 MiB.');
  const data = JSON.parse(await file.text());
  if (data?.schemaVersion !== 1 || data.installMode !== 'StaticArchive' || data.distributionProtocol !== 2 ||
      data.modelBundleId !== 'models-20261016-03' || !data.packageManifest || !data.setup ||
      !/^[a-f0-9]{64}$/i.test(data.setup.sha256 || '') || !Number.isSafeInteger(data.setup.sizeBytes) || data.setup.sizeBytes <= 0 || data.setup.sizeBytes > 512 * 1024 * 1024 ||
      !data.setup.fileName || data.setup.fileName.length > 180 || !['Stable', 'Beta', 'Development'].includes(data.channel) ||
      !/^[a-f0-9]{64}$/i.test(data.sha256 || '') || !Number.isSafeInteger(data.sizeBytes) || data.sizeBytes < 1 || data.sizeBytes > 2147483648 ||
      !Number.isSafeInteger(data.expandedSizeBytes) || data.expandedSizeBytes < 1 || data.expandedSizeBytes > 4294967296 ||
      !Number.isSafeInteger(data.entryCount) || data.entryCount < 1 || data.entryCount > 200000 ||
      !Number.isInteger(data.buildNumber) || data.buildNumber < 1 || data.buildNumber > 2147483647 ||
      data.platform !== 'win-x64' || data.packageManifest.version !== data.version || data.packageManifest.buildNumber !== data.buildNumber ||
      data.packageManifest.platform !== data.platform || data.packageManifest.product !== 'VideoMaker' || !Array.isArray(data.packageManifest.managedFiles)) {
    throw new Error('JSON không đúng schema đăng ký StaticArchive/protocol 2 hoặc thiếu binding Setup.');
  }
  const fields = { releaseVersion: data.version, releaseBuild: data.buildNumber, releaseChannel: data.channel,
    releasePlatform: data.platform, releaseExternalUrl: data.downloadUrl, releaseExternalFileName: data.fileName,
    releaseExternalSize: data.sizeBytes, releaseExternalHash: data.sha256, releaseExternalExpanded: data.expandedSizeBytes,
    releaseExternalEntries: data.entryCount };
  for (const [id, value] of Object.entries(fields)) if (value === undefined || value === null) throw new Error('JSON thiếu ' + id + '.');
  for (const [id, value] of Object.entries(fields)) releaseField(id).value = value;
  releaseField('releaseExternalManifest').value = JSON.stringify(data.packageManifest, null, 2);
  releaseImportedSetup = { fileName: data.setup.fileName, sizeBytes: data.setup.sizeBytes, sha256: data.setup.sha256.toLowerCase() };
  releaseField('releaseImportedSetup').textContent = `Setup yêu cầu: ${data.setup.fileName} · ${formatBytes(data.setup.sizeBytes)} · SHA-256 ${data.setup.sha256}. Chưa kiểm file trên VPS.`;
  releaseField('releaseSource').value = 'ExternalVps'; syncReleaseSource();
  releaseExternalChanged = true;
}

async function inspectReleaseSetup(file) {
  if (!file) return releaseImportedSetup;
  if (file.size <= 0 || file.size > 512 * 1024 * 1024) throw new Error('Setup phải có dữ liệu và không vượt 512 MiB.');
  if (file.size > 256 * 1024 * 1024) {
    if (!releaseImportedSetup) throw new Error('Với Setup trên 256 MiB, nạp JSON từ pipeline để lấy checksum; server sẽ kiểm byte khi upload.');
    if (file.name !== releaseImportedSetup.fileName || file.size !== releaseImportedSetup.sizeBytes) {
      const error = new Error('Tên hoặc dung lượng Setup không khớp JSON. Hãy chọn đúng bộ bàn giao.'); error.fieldId = 'releaseSetup'; throw error;
    }
    releaseField('releaseImportedSetup').textContent = 'Setup trên 256 MiB: dùng checksum trong JSON pipeline; server sẽ kiểm byte và checksum khi upload.';
    return releaseImportedSetup;
  }
  const digest = await crypto.subtle.digest('SHA-256', await file.arrayBuffer());
  const actual = { fileName: file.name, sizeBytes: file.size, sha256: [...new Uint8Array(digest)].map(value => value.toString(16).padStart(2, '0')).join('') };
  if (releaseImportedSetup && (actual.fileName !== releaseImportedSetup.fileName || actual.sizeBytes !== releaseImportedSetup.sizeBytes || actual.sha256 !== releaseImportedSetup.sha256)) {
    const error = new Error('File Setup đã chọn không khớp tên/dung lượng/checksum trong JSON. Hãy chọn đúng bộ bàn giao.');
    error.fieldId = 'releaseSetup'; throw error;
  }
  return actual;
}

function openReleaseDialog(release = null) {
  const form = document.getElementById('releaseForm');
  form.reset();
  clearReleaseErrors(); releaseImportedSetup = null; releaseUploadedFiles.clear(); releaseEditing = release; releaseExternalChanged = false;
  releaseWasPublished = Boolean(release?.firstPublishedAtUtc || release?.publishedOnceAtUtc);
  form.querySelectorAll('input,select,textarea,button').forEach(field => { field.disabled = false; });
  document.getElementById('releaseId').value = release?.releaseId || '';
  document.getElementById('releaseVersion').value = release?.version || '';
  document.getElementById('releaseBuild').value = release?.buildNumber || 1;
  document.getElementById('releaseChannel').value = release?.channel || 'Stable';
  document.getElementById('releasePlatform').value = release?.platform || 'win-x64';
  document.getElementById('releaseMinimum').value = release?.minimumSupportedDesktopVersion || '';
  document.getElementById('releasePublished').value = toLocalDateTime(release?.publishedAtUtc);
  document.getElementById('releaseNotes').value = release?.releaseNotes || '';
  document.getElementById('releaseMandatory').checked = release?.isMandatory ?? false;
  const packageArtifact = release?.artifacts?.find(item => item.kind === 'DesktopPackage');
  const external = packageArtifact?.sourceKind === 'ExternalVps';
  releaseField('releaseSource').value = external ? 'ExternalVps' : 'InternalUpload';
  releaseField('releaseExternalUrl').value = packageArtifact?.downloadUrl || '';
  releaseField('releaseExternalFileName').value = external ? packageArtifact.fileName : '';
  releaseField('releaseExternalSize').value = external ? packageArtifact.sizeBytes : '';
  releaseField('releaseExternalHash').value = external ? packageArtifact.sha256 : '';
  releaseField('releaseExternalExpanded').value = packageArtifact?.expandedSizeBytes || '';
  releaseField('releaseExternalEntries').value = packageArtifact?.entryCount || '';
  releaseField('releaseExternalManifest').value = packageArtifact?.packageManifest ? JSON.stringify(packageArtifact.packageManifest, null, 2) : '';
  releaseImportedSetup = packageArtifact?.setupBinding || packageArtifact?.expectedSetup || null;
  releaseField('releaseImportedSetup').textContent = external ? 'Metadata đã đăng ký; trạng thái này không chứng nhận file thực trên VPS.' : '';
  releaseField('releaseIdentityNotice').hidden = !releaseWasPublished;
  for (const id of ['releaseVersion', 'releaseBuild', 'releaseChannel', 'releasePlatform', 'releaseSource', 'releasePackage', 'releaseSetup']) releaseField(id).disabled = releaseWasPublished;
  // Existing internal download links must keep their source. A different source uses a new build.
  if (packageArtifact) releaseField('releaseSource').disabled = true;
  syncReleaseSource();
  document.getElementById('releaseDialogTitle').textContent = release ? 'Chỉnh sửa release' : 'Tạo release';
  document.getElementById('releaseDialog').showModal();
}

async function openUserDialog(userId) {
  const root = document.getElementById('userDetail');
  root.innerHTML = '<div class="empty-state">Đang tải thông tin...</div>';
  document.getElementById('userDialog').showModal();
  try {
    state.selectedUser = await api(`/api/admin/licenses/users/${encodeURIComponent(userId)}`);
    renderUserDetail();
  } catch (error) {
    root.innerHTML = `<div class="empty-state error-state">${escapeHtml(error.message)}</div>`;
  }
}

function renderUserDetail() { window.videoMakerAdminPages.userDetail(viewContext()); }

async function refreshSelectedUser() {
  if (!state.selectedUser) return;
  state.selectedUser = await api(`/api/admin/licenses/users/${encodeURIComponent(state.selectedUser.user.userId)}`);
  renderUserDetail();
  await loadAll();
}

document.getElementById('loginForm').addEventListener('submit', async event => {
  event.preventDefault();
  const button = event.currentTarget.querySelector('button[type="submit"]');
  const message = document.getElementById('loginMessage');
  message.textContent = '';
  setBusy(button, true, 'Đang xác thực...');
  try {
    const response = await fetch('/api/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: document.getElementById('loginEmail').value.trim(), password: document.getElementById('loginPassword').value, device: devicePayload() }) });
    const payload = await parseResponse(response);
    if (!response.ok) throw new Error(errorMessage(payload, 'Đăng nhập không thành công.', response.status));
    if (!payload.user?.roles?.some(role => role.toLowerCase() === 'admin')) throw new Error('Tài khoản này chưa được cấp role Admin.');
    saveSession(payload); await loadAll(); if (state.adminReady) showAdmin();
  } catch (error) { message.textContent = error.message; }
  finally { setBusy(button, false); }
});

document.querySelectorAll('.nav-item').forEach(button => button.addEventListener('click', () => {
  if (button.dataset.navParent) {
    const expanded = button.getAttribute('aria-expanded') !== 'true';
    navigate(button.dataset.view, { organizationMenuExpanded: expanded });
    return;
  }
  navigate(button.dataset.view);
}));
document.querySelectorAll('[data-go]').forEach(button => button.addEventListener('click', () => navigate(button.dataset.go)));
document.getElementById('adminMain').addEventListener('click', event => {
  if (!state.adminReady) return;
  if (event.target.closest('[data-home-create]')) return window.videoMakerPlanComposer.open();
  if (event.target.closest('[data-home-setup]')) return navigate('organizations', { organizationScope: 'setup' });
  const action = event.target.closest('[data-home-go]');
  if (!action) return;
  if (action.dataset.homeGo === 'pools') return navigate('organizations', { organizationScope: 'pools' });
  if (action.dataset.homeGo === 'organizations') return navigate('organizations', { organizationScope: 'directory' });
  navigate(action.dataset.homeGo);
});
document.querySelectorAll('[data-close]').forEach(button => button.addEventListener('click', () => button.closest('dialog').close()));
document.getElementById('adminMenuToggle').addEventListener('click', event => {
  setNavigationOpen(event.currentTarget.getAttribute('aria-expanded') !== 'true');
});
document.querySelector('.sidebar').addEventListener('click', event => {
  if (event.target.closest('.nav-subitem')) setNavigationOpen(false);
});
document.querySelector('.sidebar').addEventListener('keydown', event => {
  if (event.key === 'Escape' && document.getElementById('adminMenuToggle').getAttribute('aria-expanded') === 'true') {
    setNavigationOpen(false);
    document.getElementById('adminMenuToggle').focus();
  }
});
document.getElementById('adminSearchForm').addEventListener('submit', event => {
  event.preventDefault();
  searchUsers(document.getElementById('adminSearch').value);
});
document.getElementById('refreshButton').addEventListener('click', () => {
  if (state.currentView === 'organizations') return window.videoMakerOrganizationAdmin?.refresh();
  if (state.currentView === 'tiktok') return window.videoMakerTikTokAdmin?.refresh();
  if (state.currentView === 'payments') return window.videoMakerSepayAdmin?.refresh();
  if (state.currentView === 'releases') window.videoMakerDesktopDownloadSettings?.refresh();
  return loadAll();
});
document.getElementById('addPlanButton').addEventListener('click', () => {
  if (window.videoMakerPlanComposer) window.videoMakerPlanComposer.open();
  else toast('Chưa tải được form tạo gói. Hãy tải lại trang quản trị.', true);
});
document.getElementById('addReleaseButton').addEventListener('click', () => {
  if (window.videoMakerReleaseAdmin) return window.videoMakerReleaseAdmin.open();
  toast('Chưa tải được form phát hành mới. Hãy tải lại trang.', true);
});
document.getElementById('grantPlan').addEventListener('change', syncGrantDuration);
document.getElementById('returnToSetupButton').addEventListener('click', () => {
  navigate('organizations', { keepSetupReturn: true });
  window.videoMakerOrganizationAdmin?.showSetup();
});

document.getElementById('logoutButton').addEventListener('click', async () => {
  try { await api('/api/auth/logout', { method: 'POST', body: JSON.stringify({ refreshToken: state.refreshToken }) }); } catch { /* local logout must complete */ }
  showLogin('Đã đăng xuất khỏi Admin Console.');
});

document.getElementById('userSearchForm').addEventListener('submit', async event => {
  event.preventDefault();
  await searchUsers(document.getElementById('userSearch').value);
});

document.addEventListener('click', event => {
  const pageButton = event.target.closest('[data-pagination="users"] [data-page]');
  if (pageButton && !pageButton.disabled) {
    loadUsersPage(Number(pageButton.dataset.page), state.usersPaging.pageSize).catch(error => toast(error.message, true));
  }
  const releasePageButton = event.target.closest('[data-pagination="releases"] [data-page]');
  if (releasePageButton && !releasePageButton.disabled) {
    loadReleasesPage(Number(releasePageButton.dataset.page), state.releasesPaging.pageSize).catch(error => toast(error.message, true));
  }
});

document.addEventListener('change', event => {
  const pageSize = event.target.closest('[data-pagination="users"] [data-page-size]');
  if (pageSize) loadUsersPage(1, Number(pageSize.value)).catch(error => toast(error.message, true));
  const releasePageSize = event.target.closest('[data-pagination="releases"] [data-page-size]');
  if (releasePageSize) loadReleasesPage(1, Number(releasePageSize.value)).catch(error => toast(error.message, true));
});

document.addEventListener('click', event => {
  const userButton = event.target.closest('[data-user-id]');
  if (userButton) openUserDialog(userButton.dataset.userId);
  const grantButton = event.target.closest('[data-grant-user]');
  if (grantButton) openGrantDialog(grantButton.dataset.grantUser);
  const editPlan = event.target.closest('[data-edit-plan]');
  if (editPlan) window.videoMakerPlanComposer?.edit(state.plans.find(x => x.licensePlanId === editPlan.dataset.editPlan));
  const copyPlan = event.target.closest('[data-copy-plan]');
  if (copyPlan) window.videoMakerPlanComposer?.open(state.plans.find(x => x.licensePlanId === copyPlan.dataset.copyPlan));
});

document.getElementById('grantForm').addEventListener('submit', async event => {
  event.preventDefault();
  const form = event.currentTarget;
  const button = form.querySelector('button[type="submit"]');
  const start = document.getElementById('grantStart').value;
  const body = { licensePlanId: document.getElementById('grantPlan').value, durationDays: Number(document.getElementById('grantDuration').value), startsAtUtc: start ? new Date(start).toISOString() : null, expiresAtUtc: null, isTrial: document.getElementById('grantTrial').checked };
  setBusy(button, true, 'Đang cấp...');
  try {
    await api(`/api/admin/licenses/users/${encodeURIComponent(document.getElementById('grantUserId').value)}/grant`, { method: 'POST', body: JSON.stringify(body) });
    document.getElementById('grantDialog').close(); toast('Đã cấp license cho người dùng.'); await loadAll();
    if (state.selectedUser) await refreshSelectedUser();
  } catch (error) { form.querySelector('.dialog-message').textContent = error.message; }
  finally { setBusy(button, false); }
});

document.getElementById('userDetail').addEventListener('click', async event => {
  const extend = event.target.closest('[data-extend-license]');
  if (extend) {
    const days = await window.videoMakerAdminDialogs.requestDays();
    if (!Number.isInteger(days) || days <= 0) return;
    try { await api(`/api/admin/licenses/user-licenses/${extend.dataset.extendLicense}/extend`, { method: 'POST', body: JSON.stringify({ durationDays: days }) }); toast(`Đã gia hạn thêm ${days} ngày.`); await refreshSelectedUser(); } catch (error) { toast(error.message, true); }
    return;
  }
  const status = event.target.closest('[data-license-status]');
  if (status) {
    const labels = { Active: 'mở lại', Suspended: 'tạm khóa', Revoked: 'thu hồi' };
    if (!await window.videoMakerAdminDialogs.confirm(`Xác nhận ${labels[status.dataset.licenseStatus]} license này?`)) return;
    try { await api(`/api/admin/licenses/user-licenses/${status.dataset.licenseId}/status`, { method: 'PUT', body: JSON.stringify({ status: status.dataset.licenseStatus, reason: `Admin ${labels[status.dataset.licenseStatus]} license` }) }); toast('Đã cập nhật trạng thái license.'); await refreshSelectedUser(); } catch (error) { toast(error.message, true); }
    return;
  }
  const device = event.target.closest('[data-revoke-device]');
  if (device && await window.videoMakerAdminDialogs.confirm('Thu hồi thiết bị này? Người dùng sẽ phải kích hoạt lại.')) {
    try { await api(`/api/admin/licenses/devices/${device.dataset.revokeDevice}`, { method: 'DELETE' }); toast('Đã thu hồi thiết bị.'); await refreshSelectedUser(); } catch (error) { toast(error.message, true); }
    return;
  }
  const session = event.target.closest('[data-revoke-session]');
  if (session && await window.videoMakerAdminDialogs.confirm('Đăng xuất phiên này ngay?')) {
    try { await api(`/api/admin/licenses/sessions/${session.dataset.revokeSession}`, { method: 'DELETE' }); toast('Đã thu hồi phiên sử dụng.'); await refreshSelectedUser(); } catch (error) { toast(error.message, true); }
  }
});

document.getElementById('releaseTable').addEventListener('click', async event => {
  const edit = event.target.closest('[data-edit-release]');
  if (edit) {
    const release = state.releases.find(x => x.releaseId === edit.dataset.editRelease);
    if (release?.artifacts?.some(artifact => artifact.distributionProtocol >= 3)) {
      if (window.videoMakerReleaseAdmin) return window.videoMakerReleaseAdmin.openForRelease(release.releaseId);
      return toast('Chưa tải được form phát hành mới. Hãy tải lại trang.', true);
    }
    return openReleaseDialog(release);
  }
  const remove = event.target.closest('[data-delete-release]');
  if (remove && await window.videoMakerAdminDialogs.confirm('Xóa release và toàn bộ artifact của phiên bản này?')) {
    try { await api(`/api/admin/desktop-releases/${remove.dataset.deleteRelease}`, { method: 'DELETE' }); toast('Đã xóa release.'); await loadAll(); } catch (error) { toast(error.message, true); }
  }
});

releaseField('releaseSource').addEventListener('change', () => { releaseExternalChanged = true; syncReleaseSource(); });
releaseField('releaseExternalFields').addEventListener('input', () => { releaseExternalChanged = true; });
releaseField('releaseSetup').addEventListener('change', () => { if (releaseField('releaseSource').value === 'ExternalVps') releaseExternalChanged = true; });
releaseField('releaseRegistrationFile').addEventListener('change', async event => {
  clearReleaseErrors();
  releaseImporting = true;
  try { await importReleaseRegistration(event.target.files[0]); }
  catch (error) { releaseError(error.message, 'releaseRegistrationFile'); }
  finally { releaseImporting = false; }
});
releaseField('releaseDialog').addEventListener('cancel', event => {
  if (releaseField('releaseForm').getAttribute('aria-busy') === 'true') event.preventDefault();
});

document.getElementById('releaseForm').addEventListener('submit', async event => {
  event.preventDefault();
  const form = event.currentTarget;
  if (form.getAttribute('aria-busy') === 'true') return;
  if (releaseImporting) { releaseError('Đang đọc JSON đăng ký, vui lòng chờ hoàn tất.', 'releaseRegistrationFile'); return; }
  clearReleaseErrors();
  const external = releaseField('releaseSource').value === 'ExternalVps';
  const existingExternal = releaseEditing?.artifacts?.some(item => item.kind === 'DesktopPackage' && item.sourceKind === 'ExternalVps');
  const registerExternal = external && !releaseWasPublished && (!existingExternal || releaseExternalChanged);
  const invalid = [...form.elements].find(field => field.willValidate &&
    !(releaseField('releaseExternalFields').contains(field) && !registerExternal) && !field.checkValidity());
  if (invalid) { releaseError(invalid.validationMessage || 'Kiểm tra trường này.', invalid.id); return; }
  const publish = event.submitter?.value === 'publish';
  let id = document.getElementById('releaseId').value;
  const publishedValue = document.getElementById('releasePublished').value;
  const body = { version: document.getElementById('releaseVersion').value.trim(), buildNumber: Number(document.getElementById('releaseBuild').value), channel: document.getElementById('releaseChannel').value, platform: document.getElementById('releasePlatform').value.trim(), minimumSupportedDesktopVersion: document.getElementById('releaseMinimum').value || null, releaseNotes: document.getElementById('releaseNotes').value || null, isMandatory: document.getElementById('releaseMandatory').checked, isActive: false, publishedAtUtc: publishedValue ? new Date(publishedValue).toISOString() : null, revision: releaseEditing?.revision || null };
  const button = event.submitter || releaseField('releaseSaveDraft');
  const disabled = [...form.querySelectorAll('input,select,textarea,button')].map(field => [field, field.disabled]);
  const packageFile = releaseField('releasePackage').files[0], setupFile = releaseField('releaseSetup').files[0];
  form.setAttribute('aria-busy', 'true'); setBusy(button, true, 'Đang lưu...');
  disabled.forEach(([field]) => { field.disabled = true; });
  try {
    let registration = registerExternal ? readReleaseExternal() : null;
    if (registration) {
      registration.setup = await inspectReleaseSetup(setupFile);
      if (!registration.setup) { const error = new Error('Chọn đúng Setup hoặc nạp JSON có binding Setup trước khi đăng ký ZIP VPS.'); error.fieldId = 'releaseSetup'; throw error; }
    }
    releaseEditing = await api(id ? `/api/admin/desktop-releases/${id}` : '/api/admin/desktop-releases', { method: id ? 'PUT' : 'POST', body: JSON.stringify(body) });
    id = releaseEditing.releaseId;
    // Retain the created draft after a later registration/upload failure.
    releaseField('releaseId').value = id;
    if (registration) {
      button.textContent = 'Đang đăng ký ZIP VPS...';
      await api(`/api/admin/desktop-releases/${id}/artifacts/DesktopPackage/external`, { method: 'POST', body: JSON.stringify(registration) });
      releaseEditing = await api(`/api/admin/desktop-releases/${id}`);
    }
    const uploads = [['DesktopPackage', external ? null : packageFile], ['Setup', setupFile]];
    for (const [kind, file] of uploads) {
      if (!file || releaseUploadedFiles.get(kind) === file) continue;
      button.textContent = `Đang upload ${kind}...`;
      const data = new FormData(); data.append('file', file);
      await api(`/api/admin/desktop-releases/${id}/artifacts/${kind}`, { method: 'POST', body: data });
      releaseUploadedFiles.set(kind, file);
      releaseEditing = await api(`/api/admin/desktop-releases/${id}`);
    }
    if (publish) {
      button.textContent = 'Đang phát hành...';
      releaseEditing = await api(`/api/admin/desktop-releases/${id}`);
      releaseEditing = await api(`/api/admin/desktop-releases/${id}/publish`, { method: 'POST', body: JSON.stringify({ revision: releaseEditing.revision }) });
    }
    document.getElementById('releaseDialog').close(); toast(publish ? 'Đã phát hành desktop release.' : 'Đã lưu bản nháp desktop release.'); await loadAll();
  } catch (error) {
    // Keep the original revision on a CAS conflict; require reopening the fresh release.
    const field = error.fieldId || (/url|source|nguồn/i.test(error.payload?.code || '') ? 'releaseExternalUrl' : null);
    releaseError(error.message, field);
  } finally {
    form.removeAttribute('aria-busy'); disabled.forEach(([field, wasDisabled]) => { field.disabled = wasDisabled; }); setBusy(button, false);
  }
});

window.videoMakerAdminShell = Object.freeze({
  state,
  openLegacyRelease: openReleaseDialog,
  refreshReleases: loadReleasesPage,
  confirm: (message, options) => window.videoMakerAdminDialogs.confirm(message, options),
  upsertPlan: plan => {
    const index = state.plans.findIndex(item => item.licensePlanId === plan.licensePlanId);
    if (index < 0) state.plans.push(plan); else state.plans[index] = plan;
    renderPlans(); renderGrantPlans(); renderOverview();
  },
  api,
  escapeHtml,
  aiApiKeyLink,
  formatDate,
  icon,
  paginationMarkup,
  preservePagePosition,
  setBusy,
  navigate,
  setPageMeta,
  setSetupReturn,
  setTopbarVisible,
  showLogin,
  statusClass,
  toast
});

(async function initialize() {
  if (!state.accessToken || !state.user?.roles?.some(role => role.toLowerCase() === 'admin')) return showLogin();
  await loadAll();
  if (state.adminReady) showAdmin();
})();

})();
