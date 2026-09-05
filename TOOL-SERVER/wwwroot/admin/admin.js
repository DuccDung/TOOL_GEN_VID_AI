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
  refreshing: null
};

const loginScreen = document.getElementById('loginScreen');
const adminShell = document.getElementById('adminShell');
const panels = [...document.querySelectorAll('[data-panel]')];
const pageMeta = {
  overview: ['LICENSE CONTROL', 'Tổng quan hệ thống', 'Theo dõi tài khoản, license và phiên sử dụng từ dữ liệu hiện tại trên server.'],
  users: ['ACCOUNTS & ACCESS', 'Người dùng', 'Cấp, gia hạn hoặc thu hồi quyền sử dụng theo từng tài khoản.'],
  organizations: ['ORGANIZATION & AI', 'Tổ chức & AI', 'Quản lý thành viên, ngân sách, credential, usage và bảng giá AI.'],
  plans: ['LICENSE POLICY', 'Gói sử dụng', 'Thiết lập thời hạn, số thiết bị và số phiên chạy đồng thời.'],
  releases: ['DESKTOP DISTRIBUTION', 'Desktop Releases', 'Quản lý package, bộ cài và chính sách cập nhật VideoMaker.']
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

function errorMessage(payload, fallback = 'Không thể hoàn tất yêu cầu.') {
  if (!payload) return fallback;
  if (typeof payload === 'string') return payload;
  const fieldMessage = payload.errors && Object.values(payload.errors).flat().find(Boolean);
  return fieldMessage || payload.message || fallback;
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
    const error = new Error(errorMessage(payload));
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
  clearSession();
  setTopbarVisible(true);
  loginScreen.classList.remove('hidden');
  adminShell.classList.add('hidden');
  document.getElementById('loginMessage').textContent = message;
}

function showAdmin() {
  loginScreen.classList.add('hidden');
  adminShell.classList.remove('hidden');
  setTopbarVisible(true);
  const displayName = state.user?.displayName || state.user?.email || 'Administrator';
  document.getElementById('adminName').textContent = displayName;
  document.getElementById('adminEmail').textContent = state.user?.email || '';
  document.getElementById('adminAvatar').textContent = displayName.split(/\s+/).slice(0, 2).map(x => x[0]).join('').toUpperCase() || 'A';
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
    else failures.push(result.reason);
  });
  const authenticationError = failures.find(error => error?.status === 401 || error?.status === 403);
  if (authenticationError?.status === 401) return showLogin('Phiên đăng nhập đã hết hạn.');
  if (authenticationError?.status === 403) return showLogin('Tài khoản này chưa có quyền Admin.');
  renderAll();
  const refreshLabel = document.getElementById('adminLastRefresh');
  if (refreshLabel) refreshLabel.textContent = `Cập nhật ${new Intl.DateTimeFormat('vi-VN', { hour: '2-digit', minute: '2-digit' }).format(new Date())}`;
  if (failures.length) toast(`Có ${failures.length} khu vực chưa tải được. Bạn có thể thử làm mới lại.`, true);
  });
}

function renderAll() {
  renderOverview();
  renderUsers();
  renderPlans();
  renderReleases();
  appendReleasePagination();
  renderGrantPlans();
}

function appendReleasePagination() {
  if (state.releases.length) document.getElementById('releaseTable').insertAdjacentHTML('beforeend', paginationMarkup(state.releasesPaging, 'releases', 'desktop release'));
}

function renderOverview() {
  const data = state.overview || {};
  const metrics = [
    ['Tổng người dùng', data.totalUsers || 0, 'Tài khoản đã đăng ký', 'metric-blue'],
    ['License đang hoạt động', data.activeLicenses || 0, 'Có quyền sử dụng app', 'metric-green'],
    ['Phiên đang online', data.onlineSessions || 0, 'Heartbeat trong 10 phút', 'metric-purple'],
    ['Sắp hết hạn', data.expiringWithinSevenDays || 0, 'Trong vòng 7 ngày', 'metric-orange']
  ];
  document.getElementById('overviewMetrics').innerHTML = metrics.map(([label, value, note, color]) => `
    <article class="metric-card ${color}"><small>${escapeHtml(label)}</small><strong>${escapeHtml(value)}</strong><span>${escapeHtml(note)}</span></article>`).join('');

  const activeUsers = state.users.filter(x => x.currentLicense && licenseState(x.currentLicense).label === 'Active').slice(0, 7);
  document.getElementById('userOverview').innerHTML = activeUsers.length ? `<div class="compact-list">${activeUsers.map(user => `
    <button class="compact-user" data-user-id="${escapeHtml(user.userId)}"><span class="user-avatar">${escapeHtml(initials(user))}</span><span><strong>${escapeHtml(user.displayName || user.email)}</strong><small>${escapeHtml(user.currentLicense.planName)} · đến ${escapeHtml(formatDate(user.currentLicense.expiresAtUtc, true))}</small></span><span class="online-count">${user.activeSessionCount} online</span>${icon('arrow-right')}</button>`).join('')}</div>` : '<div class="empty-state">Chưa có license đang hoạt động.</div>';

  const activePlans = state.plans.filter(x => x.isActive);
  document.getElementById('planOverview').innerHTML = activePlans.length ? `<div class="plan-mini-list">${activePlans.slice(0, 6).map(plan => `
    <div class="plan-mini"><span class="plan-symbol">${icon('id-card')}</span><div><strong>${escapeHtml(plan.name)}</strong><small>${plan.defaultDurationDays ? `${plan.defaultDurationDays} ngày` : 'Tùy thời hạn'} · ${plan.maxActivatedDevices} thiết bị · ${plan.maxConcurrentSessions} phiên</small></div></div>`).join('')}</div>` : '<div class="empty-state">Chưa có gói đang mở.</div>';
}

function initials(user) {
  const value = user.displayName || user.email || 'U';
  return value.split(/\s+/).slice(0, 2).map(x => x[0]).join('').toUpperCase();
}

function renderUsers() {
  const root = document.getElementById('userTable');
  if (!state.users.length) {
    root.innerHTML = '<div class="empty-state">Không tìm thấy người dùng.</div>';
    return;
  }
  root.innerHTML = `<div class="table-scroll"><table class="data-table user-table"><thead><tr><th>Người dùng</th><th>Gói hiện tại</th><th>Thời hạn</th><th>Thiết bị</th><th>Phiên online</th><th>Trạng thái</th><th></th></tr></thead><tbody>${state.users.map(user => {
    const license = user.currentLicense;
    const visual = licenseState(license);
    return `<tr>
      <td><div class="user-cell"><span class="user-avatar">${escapeHtml(initials(user))}</span><div><strong>${escapeHtml(user.displayName || 'Chưa đặt tên')}</strong><small>${escapeHtml(user.email)}</small></div></div></td>
      <td>${license ? `<strong>${escapeHtml(license.planName)}</strong><br><small>${escapeHtml(license.planCode)}</small>` : '—'}</td>
      <td>${license ? `${escapeHtml(formatDate(license.startsAtUtc, true))}<br><small>đến ${escapeHtml(formatDate(license.expiresAtUtc, true))}</small>` : '—'}</td>
      <td>${user.registeredDeviceCount}</td><td>${user.activeSessionCount}</td>
      <td><span class="status-pill ${visual.css}">${escapeHtml(visual.label)}</span></td>
      <td><div class="row-actions"><button class="ghost-button" data-user-id="${escapeHtml(user.userId)}">Chi tiết</button><button class="primary-button" data-grant-user="${escapeHtml(user.userId)}">${license ? 'Đổi gói' : 'Cấp gói'}</button></div></td>
    </tr>`;
  }).join('')}</tbody></table></div>${paginationMarkup(state.usersPaging, 'users', 'người dùng')}`;
}

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

function renderPlans() {
  const root = document.getElementById('planTable');
  if (!state.plans.length) {
    root.innerHTML = '<div class="empty-state">Chưa có gói sử dụng.</div>';
    return;
  }
  root.innerHTML = `<div class="plan-grid">${state.plans.map(plan => `
    <article class="plan-card-admin ${plan.isActive ? '' : 'inactive'}">
      <div class="plan-card-top"><span class="plan-symbol">${icon('id-card')}</span><span class="status-pill ${plan.isActive && plan.isPublic ? 'status-healthy' : ''}">${plan.isPublic ? 'Public' : (plan.isActive ? 'Internal' : 'Inactive')}</span></div>
      <h3>${escapeHtml(plan.name)}</h3><code>${escapeHtml(plan.planCode)}</code><p>${escapeHtml(plan.description || 'Chưa có mô tả cho gói này.')}</p>
      <dl><div><dt>Thời hạn</dt><dd>${plan.defaultDurationDays ? `${plan.defaultDurationDays} ngày` : 'Tùy chỉnh'}</dd></div><div><dt>Giá bán</dt><dd>${plan.salePriceVnd ? `${new Intl.NumberFormat('vi-VN').format(plan.salePriceVnd)} đ` : 'Chưa đặt'}</dd></div><div><dt>Thiết bị</dt><dd>${plan.maxActivatedDevices}</dd></div><div><dt>Thứ tự</dt><dd>${plan.displayOrder || 0}</dd></div><div><dt>Phiên đồng thời</dt><dd>${plan.maxConcurrentSessions}</dd></div><div><dt>Offline grace</dt><dd>${plan.offlineGraceHours} giờ</dd></div></dl>
      <button class="ghost-button" data-edit-plan="${plan.licensePlanId}">${icon('pencil')}<span>Chỉnh sửa gói</span></button>
    </article>`).join('')}</div>`;
}

function renderReleases() {
  const root = document.getElementById('releaseTable');
  if (!state.releases.length) {
    root.innerHTML = '<div class="empty-state">Chưa có desktop release.</div>';
    return;
  }
  root.innerHTML = `<div class="table-scroll"><table class="data-table"><thead><tr><th>Version</th><th>Channel</th><th>Phát hành</th><th>Artifact</th><th>Chính sách</th><th></th></tr></thead><tbody>${state.releases.map(item => `
    <tr><td><strong>${escapeHtml(item.version)} (${item.buildNumber})</strong><br />${escapeHtml(item.platform)}</td><td><span class="status-pill ${item.isActive ? 'status-healthy' : ''}">${escapeHtml(item.channel)} · ${item.isActive ? 'Active' : 'Inactive'}</span></td><td>${escapeHtml(formatDate(item.publishedAtUtc))}</td><td><div class="artifact-list">${item.artifacts.map(artifact => `<span class="artifact-chip" title="SHA-256: ${escapeHtml(artifact.sha256)}">${escapeHtml(artifact.kind)} · ${escapeHtml(formatBytes(artifact.sizeBytes))}</span>`).join('') || '—'}</div></td><td>${item.isMandatory ? '<span class="status-pill status-failed">Bắt buộc</span>' : '<span class="status-pill">Tùy chọn</span>'}</td><td><div class="release-actions"><button class="ghost-button" data-edit-release="${item.releaseId}">${icon('pencil')}<span>Sửa</span></button><button class="danger-button" data-delete-release="${item.releaseId}">${icon('trash-2')}<span>Xóa</span></button></div></td></tr>`).join('')}</tbody></table></div>`;
}

async function loadReleasesPage(page = state.releasesPaging.page, pageSize = state.releasesPaging.pageSize) {
  return preservePagePosition(async () => {
    const root = document.getElementById('releaseTable');
    root.innerHTML = '<div class="organization-loading empty-state">Đang tải desktop release...</div>';
    try {
      const data = await api(`/api/admin/desktop-releases/page?page=${page}&pageSize=${pageSize}`);
      state.releases = data.items || [];
      state.releasesPaging = data;
      renderReleases();
      appendReleasePagination();
    } catch (error) {
      root.innerHTML = `<div class="empty-state">${escapeHtml(error.message)}</div>`;
      throw error;
    }
  });
}

function renderGrantPlans() {
  document.getElementById('grantPlan').innerHTML = state.plans.filter(x => x.isActive).map(plan => `<option value="${plan.licensePlanId}" data-days="${plan.defaultDurationDays || 30}">${escapeHtml(plan.name)}${plan.defaultDurationDays ? ` · ${plan.defaultDurationDays} ngày` : ''}</option>`).join('');
}

function setPageMeta(eyebrow, title, subtitle) {
  document.getElementById('pageEyebrow').textContent = eyebrow;
  document.getElementById('pageTitle').textContent = title;
  document.getElementById('pageSubtitle').textContent = subtitle;
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
  state.currentView = view;
  setTopbarVisible(true);
  if (!options.keepSetupReturn) setSetupReturn(false);
  document.querySelectorAll('.nav-item').forEach(item => item.classList.toggle('active', item.dataset.view === view));
  panels.forEach(panel => panel.classList.toggle('hidden', panel.dataset.panel !== view));
  const [eyebrow, title, subtitle] = pageMeta[view];
  setPageMeta(eyebrow, title, subtitle);
  if (view === 'organizations') {
    setOrganizationMenuExpanded(options.organizationMenuExpanded ?? true);
    window.videoMakerOrganizationAdmin?.activate(options.organizationScope);
  } else {
    setOrganizationMenuExpanded(false);
  }
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

function syncPlanSalesRequirements() {
  const form = document.getElementById('planForm');
  const isPublic = document.getElementById('planPublic').checked;
  const duration = document.getElementById('planDuration');
  const salePrice = document.getElementById('planSalePrice');
  duration.required = isPublic;
  salePrice.required = isPublic;
  duration.setCustomValidity(isPublic && !duration.value ? 'Vui lòng nhập thời hạn mặc định cho gói được mở bán.' : '');
  salePrice.setCustomValidity(isPublic && !salePrice.value ? 'Vui lòng nhập giá bán VND cho gói được mở bán.' : '');
  if (!isPublic || (duration.value && salePrice.value)) form.querySelector('.dialog-message').textContent = '';
}

function openPlanDialog(plan = null) {
  const form = document.getElementById('planForm');
  form.reset();
  form.querySelector('.dialog-message').textContent = '';
  document.getElementById('planId').value = plan?.licensePlanId || '';
  document.getElementById('planCode').value = plan?.planCode || '';
  document.getElementById('planCode').disabled = Boolean(plan);
  document.getElementById('planName').value = plan?.name || '';
  document.getElementById('planDescription').value = plan?.description || '';
  document.getElementById('planDuration').value = plan?.defaultDurationDays || '';
  document.getElementById('planSalePrice').value = plan?.salePriceVnd || '';
  document.getElementById('planDisplayOrder').value = plan?.displayOrder || 0;
  try { document.getElementById('planMarketingFeatures').value = JSON.parse(plan?.marketingFeaturesJson || '[]').join('\n'); }
  catch { document.getElementById('planMarketingFeatures').value = ''; }
  document.getElementById('planDevices').value = plan?.maxActivatedDevices || 1;
  document.getElementById('planSessions').value = plan?.maxConcurrentSessions || 1;
  document.getElementById('planOffline').value = plan?.offlineGraceHours || 0;
  document.getElementById('planActive').checked = plan?.isActive ?? true;
  document.getElementById('planPublic').checked = plan?.isPublic ?? false;
  syncPlanSalesRequirements();
  document.getElementById('planDialogTitle').textContent = plan ? 'Chỉnh sửa gói' : 'Tạo gói';
  document.getElementById('planDialog').showModal();
}

function openReleaseDialog(release = null) {
  const form = document.getElementById('releaseForm');
  form.reset();
  form.querySelector('.dialog-message').textContent = '';
  document.getElementById('releaseId').value = release?.releaseId || '';
  document.getElementById('releaseVersion').value = release?.version || '';
  document.getElementById('releaseBuild').value = release?.buildNumber || 1;
  document.getElementById('releaseChannel').value = release?.channel || 'Stable';
  document.getElementById('releasePlatform').value = release?.platform || 'win-x64';
  document.getElementById('releaseMinimum').value = release?.minimumSupportedDesktopVersion || '';
  document.getElementById('releasePublished').value = toLocalDateTime(release?.publishedAtUtc);
  document.getElementById('releaseNotes').value = release?.releaseNotes || '';
  document.getElementById('releaseMandatory').checked = release?.isMandatory ?? false;
  document.getElementById('releaseActive').checked = release?.isActive ?? true;
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

function renderUserDetail() {
  const data = state.selectedUser;
  if (!data) return;
  const user = data.user;
  const current = user.currentLicense;
  document.getElementById('userDetail').innerHTML = `
    <div class="user-detail-header"><span class="user-avatar large">${escapeHtml(initials(user))}</span><div><h3>${escapeHtml(user.displayName || 'Chưa đặt tên')}</h3><p>${escapeHtml(user.email)}</p></div><button class="primary-button" data-grant-user="${escapeHtml(user.userId)}">${current ? 'Đổi gói' : 'Cấp gói'}</button></div>
    <div class="detail-stats"><div><span>Trạng thái</span><strong>${escapeHtml(user.accountStatus)}</strong></div><div><span>Lần đăng nhập cuối</span><strong>${escapeHtml(formatDate(user.lastLoginAtUtc))}</strong></div><div><span>Thiết bị</span><strong>${user.registeredDeviceCount}</strong></div><div><span>Phiên online</span><strong>${user.activeSessionCount}</strong></div></div>
    <section class="detail-section"><div class="section-heading compact-detail"><div><span class="eyebrow">LICENSE HISTORY</span><h3>Lịch sử gói</h3></div></div>${data.licenses.length ? `<div class="license-history">${data.licenses.map(license => `<article><div><strong>${escapeHtml(license.planName)}</strong><span class="status-pill ${statusClass(license.status)}">${escapeHtml(license.status)}</span></div><p>${escapeHtml(formatDate(license.startsAtUtc))} → ${escapeHtml(formatDate(license.expiresAtUtc))} · ${license.activeDeviceCount} thiết bị</p><div class="row-actions">${license.status === 'Active' ? `<button class="ghost-button" data-extend-license="${license.userLicenseId}">Gia hạn</button><button class="ghost-button warning-action" data-license-status="Suspended" data-license-id="${license.userLicenseId}">Tạm khóa</button><button class="danger-button" data-license-status="Revoked" data-license-id="${license.userLicenseId}">Thu hồi</button>` : license.status === 'Suspended' ? `<button class="ghost-button" data-license-status="Active" data-license-id="${license.userLicenseId}">Mở lại</button><button class="danger-button" data-license-status="Revoked" data-license-id="${license.userLicenseId}">Thu hồi</button>` : ''}</div></article>`).join('')}</div>` : '<div class="empty-state">Chưa có license.</div>'}</section>
    <div class="detail-columns">
      <section class="detail-section"><div class="section-heading compact-detail"><div><span class="eyebrow">DEVICES</span><h3>Thiết bị đã đăng ký</h3></div></div>${data.devices.length ? `<div class="access-list">${data.devices.map(device => `<article><span class="access-icon">${icon('monitor')}</span><div><strong>${escapeHtml(device.deviceName)}</strong><small>${escapeHtml(device.operatingSystem || 'Không rõ hệ điều hành')} · ${device.isRevoked ? 'Revoked' : 'Active'}</small><small>Lần cuối: ${escapeHtml(formatDate(device.lastSeenAtUtc))}</small></div>${!device.isRevoked ? `<button class="danger-button" data-revoke-device="${device.deviceId}">Thu hồi</button>` : ''}</article>`).join('')}</div>` : '<div class="empty-state">Chưa có thiết bị.</div>'}</section>
      <section class="detail-section"><div class="section-heading compact-detail"><div><span class="eyebrow">SESSIONS</span><h3>Phiên sử dụng</h3></div></div>${data.sessions.length ? `<div class="access-list">${data.sessions.map(session => `<article><span class="access-icon online">${icon('radio')}</span><div><strong>${escapeHtml(session.deviceName)}</strong><small>${escapeHtml(session.status)} · ${escapeHtml(session.applicationVersion || '')}</small><small>Heartbeat: ${escapeHtml(formatDate(session.lastSeenAtUtc))}</small></div>${session.status === 'Active' ? `<button class="danger-button" data-revoke-session="${session.sessionId}">Đăng xuất</button>` : ''}</article>`).join('')}</div>` : '<div class="empty-state">Chưa có phiên.</div>'}</section>
    </div>`;
}

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
    if (!response.ok) throw new Error(errorMessage(payload, 'Đăng nhập không thành công.'));
    if (!payload.user?.roles?.some(role => role.toLowerCase() === 'admin')) throw new Error('Tài khoản này chưa được cấp role Admin.');
    saveSession(payload); showAdmin(); await loadAll();
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
document.querySelectorAll('[data-close]').forEach(button => button.addEventListener('click', () => button.closest('dialog').close()));
document.getElementById('refreshButton').addEventListener('click', () => state.currentView === 'organizations'
  ? window.videoMakerOrganizationAdmin?.refresh()
  : loadAll());
document.getElementById('addPlanButton').addEventListener('click', () => openPlanDialog());
document.getElementById('addReleaseButton').addEventListener('click', () => openReleaseDialog());
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
  state.userSearch = document.getElementById('userSearch').value.trim();
  loadUsersPage(1, state.usersPaging.pageSize).catch(error => toast(error.message, true));
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
  if (editPlan) openPlanDialog(state.plans.find(x => x.licensePlanId === editPlan.dataset.editPlan));
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

document.getElementById('planPublic').addEventListener('change', syncPlanSalesRequirements);
document.getElementById('planDuration').addEventListener('input', syncPlanSalesRequirements);
document.getElementById('planSalePrice').addEventListener('input', syncPlanSalesRequirements);
document.getElementById('planForm').addEventListener('invalid', event => {
  event.currentTarget.querySelector('.dialog-message').textContent = event.target.validationMessage;
}, true);

document.getElementById('planForm').addEventListener('submit', async event => {
  event.preventDefault();
  const form = event.currentTarget;
  const id = document.getElementById('planId').value;
  const marketingFeatures = document.getElementById('planMarketingFeatures').value.split(/\r?\n/).map(x => x.trim()).filter(Boolean);
  const body = { planCode: document.getElementById('planCode').value, name: document.getElementById('planName').value, description: document.getElementById('planDescription').value || null, maxActivatedDevices: Number(document.getElementById('planDevices').value), maxConcurrentSessions: Number(document.getElementById('planSessions').value), offlineGraceHours: Number(document.getElementById('planOffline').value), defaultDurationDays: document.getElementById('planDuration').value ? Number(document.getElementById('planDuration').value) : null, featureFlagsJson: id ? state.plans.find(x => x.licensePlanId === id)?.featureFlagsJson || null : null, isActive: document.getElementById('planActive').checked, salePriceVnd: document.getElementById('planSalePrice').value ? Number(document.getElementById('planSalePrice').value) : null, isPublic: document.getElementById('planPublic').checked, displayOrder: Number(document.getElementById('planDisplayOrder').value || 0), marketingFeaturesJson: marketingFeatures.length ? JSON.stringify(marketingFeatures) : null };
  const button = form.querySelector('button[type="submit"]'); setBusy(button, true, 'Đang lưu...');
  try { await api(id ? `/api/admin/licenses/plans/${id}` : '/api/admin/licenses/plans', { method: id ? 'PUT' : 'POST', body: JSON.stringify(body) }); document.getElementById('planDialog').close(); toast('Đã lưu gói sử dụng.'); await loadAll(); }
  catch (error) { form.querySelector('.dialog-message').textContent = error.message; }
  finally { setBusy(button, false); }
});

document.getElementById('userDetail').addEventListener('click', async event => {
  const extend = event.target.closest('[data-extend-license]');
  if (extend) {
    const days = Number(prompt('Gia hạn thêm bao nhiêu ngày?', '30'));
    if (!Number.isInteger(days) || days <= 0) return;
    try { await api(`/api/admin/licenses/user-licenses/${extend.dataset.extendLicense}/extend`, { method: 'POST', body: JSON.stringify({ durationDays: days }) }); toast(`Đã gia hạn thêm ${days} ngày.`); await refreshSelectedUser(); } catch (error) { toast(error.message, true); }
    return;
  }
  const status = event.target.closest('[data-license-status]');
  if (status) {
    const labels = { Active: 'mở lại', Suspended: 'tạm khóa', Revoked: 'thu hồi' };
    if (!confirm(`Xác nhận ${labels[status.dataset.licenseStatus]} license này?`)) return;
    try { await api(`/api/admin/licenses/user-licenses/${status.dataset.licenseId}/status`, { method: 'PUT', body: JSON.stringify({ status: status.dataset.licenseStatus, reason: `Admin ${labels[status.dataset.licenseStatus]} license` }) }); toast('Đã cập nhật trạng thái license.'); await refreshSelectedUser(); } catch (error) { toast(error.message, true); }
    return;
  }
  const device = event.target.closest('[data-revoke-device]');
  if (device && confirm('Thu hồi thiết bị này? Người dùng sẽ phải kích hoạt lại.')) {
    try { await api(`/api/admin/licenses/devices/${device.dataset.revokeDevice}`, { method: 'DELETE' }); toast('Đã thu hồi thiết bị.'); await refreshSelectedUser(); } catch (error) { toast(error.message, true); }
    return;
  }
  const session = event.target.closest('[data-revoke-session]');
  if (session && confirm('Đăng xuất phiên này ngay?')) {
    try { await api(`/api/admin/licenses/sessions/${session.dataset.revokeSession}`, { method: 'DELETE' }); toast('Đã thu hồi phiên sử dụng.'); await refreshSelectedUser(); } catch (error) { toast(error.message, true); }
  }
});

document.getElementById('releaseTable').addEventListener('click', async event => {
  const edit = event.target.closest('[data-edit-release]');
  if (edit) return openReleaseDialog(state.releases.find(x => x.releaseId === edit.dataset.editRelease));
  const remove = event.target.closest('[data-delete-release]');
  if (remove && confirm('Xóa release và toàn bộ artifact của phiên bản này?')) {
    try { await api(`/api/admin/desktop-releases/${remove.dataset.deleteRelease}`, { method: 'DELETE' }); toast('Đã xóa release.'); await loadAll(); } catch (error) { toast(error.message, true); }
  }
});

document.getElementById('releaseForm').addEventListener('submit', async event => {
  event.preventDefault();
  const form = event.currentTarget;
  const id = document.getElementById('releaseId').value;
  const publishedValue = document.getElementById('releasePublished').value;
  const body = { version: document.getElementById('releaseVersion').value, buildNumber: Number(document.getElementById('releaseBuild').value), channel: document.getElementById('releaseChannel').value, platform: document.getElementById('releasePlatform').value, minimumSupportedDesktopVersion: document.getElementById('releaseMinimum').value || null, releaseNotes: document.getElementById('releaseNotes').value || null, isMandatory: document.getElementById('releaseMandatory').checked, isActive: document.getElementById('releaseActive').checked, publishedAtUtc: publishedValue ? new Date(publishedValue).toISOString() : null };
  const button = form.querySelector('button[type="submit"]'); setBusy(button, true, 'Đang lưu...');
  try {
    const release = await api(id ? `/api/admin/desktop-releases/${id}` : '/api/admin/desktop-releases', { method: id ? 'PUT' : 'POST', body: JSON.stringify(body) });
    const uploads = [['DesktopPackage', document.getElementById('releasePackage').files[0]], ['Setup', document.getElementById('releaseSetup').files[0]]];
    for (const [kind, file] of uploads) {
      if (!file) continue;
      button.textContent = `Đang upload ${kind}...`;
      const data = new FormData(); data.append('file', file);
      await api(`/api/admin/desktop-releases/${release.releaseId}/artifacts/${kind}`, { method: 'POST', body: data });
    }
    document.getElementById('releaseDialog').close(); toast('Đã lưu desktop release.'); await loadAll();
  } catch (error) { form.querySelector('.dialog-message').textContent = error.message; }
  finally { setBusy(button, false); }
});

window.videoMakerAdminShell = Object.freeze({
  state,
  api,
  escapeHtml,
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
  showAdmin();
  await loadAll();
})();
