(() => {
  'use strict';

  const shell = window.videoMakerAdminShell;
  if (!shell) return;
  const { api, escapeHtml, formatDate, icon, setBusy, toast } = shell;
  const organizationState = {
    organizations: null,
    selectedOrganizationId: null,
    selectedTab: 'overview',
    scope: 'directory',
    members: null,
    usage: null,
    providers: null,
    videoPolicy: null,
    audit: null,
    pricing: null,
    memberSearch: '',
    usageFilters: { provider: '', model: '', kind: '' },
    requests: new Map(),
    version: 0
  };

  const byId = id => document.getElementById(id);
  const roleCapabilities = {
    Owner: { members: true, billing: true, credentials: true, audit: true },
    OrganizationAdmin: { members: true, billing: true, credentials: true, audit: true },
    BillingManager: { members: false, billing: true, credentials: false, audit: false },
    Member: { members: false, billing: false, credentials: false, audit: false },
    Viewer: { members: false, billing: false, credentials: false, audit: false }
  };
  const errorMessages = {
    organization_access_denied: 'Bạn không còn là thành viên hoạt động của tổ chức này.',
    organization_role_denied: 'Vai trò hiện tại không có quyền thực hiện thao tác này.',
    owner_role_required: 'Chỉ Owner mới có thể cấp hoặc thay đổi vai trò Owner.',
    last_owner_required: 'Không thể khóa, xóa hoặc hạ quyền Owner đang hoạt động cuối cùng.',
    budget_below_committed_cost: 'Budget mới phải lớn hơn hoặc bằng chi phí đã dùng cộng khoản đang giữ.',
    provider_not_found: 'Không tìm thấy provider AI cần cấu hình.',
    provider_disabled: 'Provider hiện chưa được kích hoạt.',
    provider_credential_test_failed: 'Credential mới không vượt qua kiểm tra. Credential đang Active vẫn được giữ nguyên.',
    cost_rate_effective_date_conflict: 'Rate mới phải có thời điểm hiệu lực sau rate đang hoạt động.',
    provider_model_not_found: 'Không tìm thấy model AI cần cấu hình.',
    cost_rate_not_found: 'Không tìm thấy rate AI này.',
    user_not_found: 'Không tìm thấy tài khoản theo email đã nhập.',
    invalid_request: 'Dữ liệu chưa hợp lệ. Vui lòng kiểm tra lại các trường.'
  };
  const readinessMessages = {
    budget_disabled: 'Budget đang bằng 0 nên AI bị khóa.',
    provider_disabled: 'Provider đang bị tắt.',
    model_disabled: 'Model mặc định đang thiếu hoặc bị tắt.',
    credential_missing: 'Chưa có credential Active.',
    pricing_not_configured: 'Chưa có đủ rate bắt buộc.'
  };
  const auditLabels = {
    OrganizationCreated: 'Tạo tổ chức',
    OrganizationMemberAdded: 'Thêm thành viên',
    OrganizationMemberUpdated: 'Cập nhật thành viên',
    OrganizationBudgetUpdated: 'Cập nhật budget',
    OrganizationProviderCredentialRotated: 'Thay credential'
  };

  function selectedOrganization() {
    return organizationState.organizations?.find(item => item.organizationId === organizationState.selectedOrganizationId) || null;
  }

  function capabilities() {
    return roleCapabilities[selectedOrganization()?.role] || roleCapabilities.Viewer;
  }

  function friendlyError(error, fallback = 'Không thể hoàn tất yêu cầu.') {
    if (error?.status === 401) {
      shell.showLogin('Phiên đăng nhập đã hết hạn.');
      return 'Phiên đăng nhập đã hết hạn.';
    }
    if (error?.status === 403 && !error?.payload?.code) {
      return 'Bạn không có quyền thực hiện thao tác này.';
    }
    const code = error?.payload?.code;
    return errorMessages[code] || error?.message || fallback;
  }

  function loading(label = 'Đang tải dữ liệu...') {
    return `<div class="empty-state organization-loading">${escapeHtml(label)}</div>`;
  }

  function errorState(error, action) {
    return `<div class="empty-state error-state"><p>${escapeHtml(friendlyError(error))}</p><button type="button" class="ghost-button" data-organization-retry="${escapeHtml(action)}">Thử lại</button></div>`;
  }

  function formatMoney(value, currency = 'USD') {
    const number = Number(value ?? 0);
    if (!Number.isFinite(number)) return '—';
    return new Intl.NumberFormat('vi-VN', { style: 'currency', currency, minimumFractionDigits: 0, maximumFractionDigits: 6 }).format(number);
  }

  function formatMetric(value, suffix = '') {
    if (value === null || value === undefined) return '—';
    const number = Number(value);
    if (!Number.isFinite(number)) return '—';
    return `${new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 3 }).format(number)}${suffix}`;
  }

  function statusPill(label, status) {
    const css = status === 'ready' ? 'status-healthy' : status === 'blocked' ? 'status-failed' : 'status-warning';
    return `<span class="status-pill ${css}">${escapeHtml(label)}</span>`;
  }

  function renderReadinessAction(reason, providerCode = '') {
    const action = reason === 'budget_disabled' && capabilities().billing
      ? { target: 'budget', label: 'Thiết lập ngân sách' }
      : reason === 'provider_disabled'
        ? { target: 'provider-disabled', label: 'Xem cách kích hoạt' }
        : reason === 'pricing_not_configured'
          ? { target: 'pricing', label: 'Thiết lập bảng giá' }
          : reason === 'credential_missing' && capabilities().credentials
            ? { target: 'credential', label: 'Cấu hình credential' }
            : null;
    if (!action) return '';
    return `<button type="button" class="readiness-setup-link" data-readiness-action="${escapeHtml(action.target)}" data-readiness-provider="${escapeHtml(providerCode)}">${escapeHtml(action.label)} <span aria-hidden="true">→</span></button>`;
  }

  function readinessFor(organization, providerCode) {
    return organization.aiReadiness?.find(item => item.providerCode?.toLowerCase() === providerCode) || null;
  }

  function renderReadinessPill(readiness) {
    if (!readiness) return statusPill('Chưa đánh giá', 'warning');
    return readiness.ready
      ? statusPill('Sẵn sàng', 'ready')
      : statusPill(`${readiness.blockingReasons?.length || 1} điều kiện thiếu`, 'blocked');
  }

  function cancelRequests() {
    for (const controller of organizationState.requests.values()) controller.abort();
    organizationState.requests.clear();
    organizationState.version += 1;
  }

  async function request(key, path, options = {}) {
    organizationState.requests.get(key)?.abort();
    const controller = new AbortController();
    organizationState.requests.set(key, controller);
    try {
      return await api(path, { ...options, signal: controller.signal });
    } finally {
      if (organizationState.requests.get(key) === controller) organizationState.requests.delete(key);
    }
  }

  async function loadOrganizations(force = false) {
    if (organizationState.organizations && !force) {
      renderOrganizationList();
      return organizationState.organizations;
    }
    byId('organizationTable').innerHTML = loading('Đang tải danh sách tổ chức...');
    try {
      organizationState.organizations = await request('organizations', '/api/organizations');
      renderOrganizationList();
      return organizationState.organizations;
    } catch (error) {
      if (error.name !== 'AbortError') byId('organizationTable').innerHTML = errorState(error, 'organizations');
      throw error;
    }
  }

  function renderOrganizationList() {
    const root = byId('organizationTable');
    const organizations = organizationState.organizations || [];
    if (!organizations.length) {
      root.innerHTML = '<div class="empty-state"><strong>Chưa có tổ chức</strong><p>Tạo tổ chức đầu tiên để cấu hình thành viên, budget và AI Gateway.</p></div>';
      return;
    }
    root.innerHTML = `<div class="table-scroll"><table class="data-table organization-table"><thead><tr><th>Tổ chức</th><th>Vai trò</th><th>Thành viên</th><th>Budget kỳ hiện tại</th><th>OpenAI</th><th>Kling</th><th></th></tr></thead><tbody>${organizations.map(item => `
      <tr><td><strong>${escapeHtml(item.name)}</strong><br><small>${escapeHtml(item.code)} · ${escapeHtml(item.status)}</small></td><td>${escapeHtml(item.role)}</td><td><strong>${escapeHtml(item.activeMemberCount ?? 0)} Active</strong><br><small>${escapeHtml(item.memberCount ?? 0)} tổng cộng</small></td><td><strong>${escapeHtml(formatMoney(item.monthlyBudgetLimit, item.currencyCode))}</strong><br><small>Đã dùng ${escapeHtml(formatMoney(item.actualCost, item.currencyCode))} · giữ ${escapeHtml(formatMoney(item.reservedCost, item.currencyCode))}</small></td><td>${renderReadinessPill(readinessFor(item, 'openai'))}</td><td>${renderReadinessPill(readinessFor(item, 'kling'))}</td><td><button type="button" class="ghost-button" data-open-organization="${escapeHtml(item.organizationId)}">Xem chi tiết</button></td></tr>`).join('')}</tbody></table></div>`;
  }

  function showScope(scope) {
    organizationState.scope = scope;
    document.querySelectorAll('[data-organization-scope]').forEach(button => {
      const active = button.dataset.organizationScope === scope;
      button.classList.toggle('active', active);
      button.setAttribute('aria-selected', String(active));
    });
    byId('organizationPricing').classList.toggle('hidden', scope !== 'pricing');
    byId('organizationCostGuide').classList.toggle('hidden', scope !== 'cost-guide');
    byId('organizationDirectory').classList.toggle('hidden', scope !== 'directory' || Boolean(organizationState.selectedOrganizationId));
    byId('organizationDetail').classList.toggle('hidden', scope !== 'directory' || !organizationState.selectedOrganizationId);
    byId('topAddOrganization').classList.toggle('hidden', scope !== 'directory');
    if (scope === 'pricing') return loadPricing().catch(() => {});
    if (scope === 'cost-guide') return loadCostGuide().catch(() => {});
    return loadOrganizations().catch(() => {});
  }

  async function navigateToReadinessSetup(target, providerCode) {
    if (target === 'budget') {
      await showScope('directory');
      await selectTab('usage');
      const budgetInput = byId('organizationBudgetLimit');
      budgetInput?.scrollIntoView({ behavior: 'smooth', block: 'center' });
      budgetInput?.focus({ preventScroll: true });
      return;
    }
    if (target === 'pricing') {
      await showScope('pricing');
      const providerSection = [...document.querySelectorAll('[data-pricing-provider]')]
        .find(section => section.dataset.pricingProvider === providerCode);
      providerSection?.scrollIntoView({ behavior: 'smooth', block: 'start' });
      providerSection?.querySelector('[data-add-ai-rate]')?.focus({ preventScroll: true });
      return;
    }
    if (target === 'provider-disabled') {
      const provider = organizationState.pricing?.find(item => item.providerCode === providerCode);
      openProviderUnavailableDialog(providerCode, provider?.displayName || providerCode);
      return;
    }
    if (target === 'credential') {
      await showScope('directory');
      await selectTab('providers');
      const providerButton = [...document.querySelectorAll('[data-configure-provider]')]
        .find(button => button.dataset.configureProvider === providerCode);
      if (providerButton) openCredentialDialog(providerCode, providerButton.dataset.providerName || providerCode);
    }
  }

  async function openOrganization(organizationId) {
    cancelRequests();
    organizationState.selectedOrganizationId = organizationId;
    organizationState.selectedTab = 'overview';
    organizationState.members = null;
    organizationState.usage = null;
    organizationState.providers = null;
    organizationState.videoPolicy = null;
    organizationState.audit = null;
    byId('organizationDirectory').classList.add('hidden');
    byId('organizationDetail').classList.remove('hidden');
    renderOrganizationHeading();
    await selectTab('overview');
  }

  function closeOrganization() {
    cancelRequests();
    organizationState.selectedOrganizationId = null;
    organizationState.members = null;
    organizationState.usage = null;
    organizationState.providers = null;
    organizationState.videoPolicy = null;
    organizationState.audit = null;
    byId('organizationDetail').classList.add('hidden');
    byId('organizationDirectory').classList.remove('hidden');
  }

  function renderOrganizationHeading() {
    const organization = selectedOrganization();
    byId('organizationDetailHeading').innerHTML = organization ? `<span class="eyebrow">${escapeHtml(organization.role)}</span><h2>${escapeHtml(organization.name)}</h2><p>${escapeHtml(organization.code)} · ${escapeHtml(organization.status)}</p>` : '';
    const auditTab = document.querySelector('[data-organization-tab="audit"]');
    auditTab.classList.toggle('hidden', !capabilities().audit);
  }

  async function selectTab(tab, force = false) {
    if (!selectedOrganization()) return;
    if (tab === 'audit' && !capabilities().audit) tab = 'overview';
    organizationState.selectedTab = tab;
    document.querySelectorAll('[data-organization-tab]').forEach(button => {
      const active = button.dataset.organizationTab === tab;
      button.classList.toggle('active', active);
      button.setAttribute('aria-selected', String(active));
    });
    if (tab === 'overview') return renderOrganizationOverview();
    if (tab === 'members') return loadMembers(force);
    if (tab === 'usage') return loadUsage(force);
    if (tab === 'providers') return loadProviders(force);
    if (tab === 'audit') return loadAudit(force);
  }

  function renderOrganizationOverview() {
    const root = byId('organizationTabContent');
    const organization = selectedOrganization();
    if (!organization) return;
    const readiness = organization.aiReadiness || [];
    const warnings = readiness.flatMap(item => (item.blockingReasons || []).map(reason => ({ provider: item.providerCode, reason, missing: item.missingUsageTypes || [] })));
    root.innerHTML = `
      <div class="organization-metrics">
        <article><span>Budget tháng</span><strong>${escapeHtml(formatMoney(organization.monthlyBudgetLimit, organization.currencyCode))}</strong><small>Hạn mức nội bộ</small></article>
        <article><span>Đã dùng</span><strong>${escapeHtml(formatMoney(organization.actualCost, organization.currencyCode))}</strong><small>Kỳ UTC hiện tại</small></article>
        <article><span>Đang giữ</span><strong>${escapeHtml(formatMoney(organization.reservedCost, organization.currencyCode))}</strong><small>Reservation chưa quyết toán</small></article>
        <article><span>Còn lại</span><strong>${escapeHtml(formatMoney(organization.remainingBudget, organization.currencyCode))}</strong><small>Không phải số dư đã nạp</small></article>
      </div>
      ${Number(organization.monthlyBudgetLimit) === 0 ? `<div class="organization-alert danger"><strong>AI đang bị khóa</strong><span>Budget tổ chức bằng 0. Hãy đặt hạn mức lớn hơn 0 trước khi phát sinh request AI.</span>${renderReadinessAction('budget_disabled')}</div>` : ''}
      <div class="organization-overview-grid"><section><div class="section-heading"><div><span class="eyebrow">AI READINESS</span><h3>Trạng thái provider</h3></div></div><div class="organization-provider-list">${readiness.length ? readiness.map(renderOverviewProvider).join('') : '<div class="empty-state">Chưa có catalog OpenAI/Kling để đánh giá.</div>'}</div></section><section><div class="section-heading"><div><span class="eyebrow">CONFIGURATION</span><h3>Điều kiện cần xử lý</h3></div></div>${warnings.length ? `<ul class="warning-list">${warnings.map(item => `<li><strong>${escapeHtml(item.provider)}</strong><span>${escapeHtml(readinessMessages[item.reason] || item.reason)}${item.reason === 'pricing_not_configured' && item.missing.length ? ` Thiếu: ${escapeHtml(item.missing.join(', '))}.` : ''}</span>${renderReadinessAction(item.reason, item.provider)}</li>`).join('')}</ul>` : '<div class="empty-state success-state">Mọi điều kiện bắt buộc đã sẵn sàng.</div>'}</section></div>`;
  }

  function renderOverviewProvider(item) {
    const reasons = item.blockingReasons || [];
    return `<article class="organization-provider-summary"><span class="provider-logo">${escapeHtml(String(item.providerCode || '?').slice(0, 1).toUpperCase())}</span><div><strong>${escapeHtml(item.providerCode)}</strong><small>${escapeHtml(item.modelCode || 'Chưa có model')}</small></div>${item.ready ? statusPill('Sẵn sàng', 'ready') : statusPill(`${reasons.length} điều kiện thiếu`, 'blocked')}</article>`;
  }

  async function loadMembers(force = false) {
    const root = byId('organizationTabContent');
    if (organizationState.members && !force) return renderMembers();
    root.innerHTML = loading('Đang tải thành viên...');
    const version = organizationState.version;
    try {
      const data = await request('members', `/api/organizations/${organizationState.selectedOrganizationId}/members`);
      if (version !== organizationState.version) return;
      organizationState.members = data;
      renderMembers();
    } catch (error) {
      if (error.name !== 'AbortError') root.innerHTML = errorState(error, 'members');
    }
  }

  function renderMembers() {
    const root = byId('organizationTabContent');
    const canManage = capabilities().members;
    const actorRole = selectedOrganization()?.role;
    const search = organizationState.memberSearch.toLocaleLowerCase('vi');
    const members = (organizationState.members || []).filter(item => !search || `${item.email} ${item.displayName || ''} ${item.role} ${item.status}`.toLocaleLowerCase('vi').includes(search));
    root.innerHTML = `<div class="organization-tab-toolbar"><div class="search-form"><div>${icon('search')}<input id="organizationMemberSearch" type="search" value="${escapeHtml(organizationState.memberSearch)}" placeholder="Tìm email, tên, vai trò..." aria-label="Tìm thành viên" /></div></div>${canManage ? `<button type="button" class="primary-button" data-add-organization-member>${icon('plus')}<span>Thêm thành viên</span></button>` : ''}</div>${members.length ? `<div class="table-scroll"><table class="data-table"><thead><tr><th>Thành viên</th><th>Vai trò</th><th>Trạng thái</th><th>Hạn mức tháng</th><th>Ngày tham gia</th><th></th></tr></thead><tbody>${members.map(member => {
      const canEdit = canManage && (member.role !== 'Owner' || actorRole === 'Owner');
      return `<tr><td><strong>${escapeHtml(member.displayName || 'Chưa đặt tên')}</strong><br><small>${escapeHtml(member.email)}</small></td><td>${escapeHtml(member.role)}</td><td>${statusPill(member.status, member.status === 'Active' ? 'ready' : 'blocked')}</td><td>${member.monthlyBudgetLimit === null ? 'Không đặt' : escapeHtml(formatMoney(member.monthlyBudgetLimit))}</td><td>${escapeHtml(formatDate(member.joinedAtUtc))}</td><td>${canEdit ? `<button type="button" class="ghost-button" data-edit-organization-member="${escapeHtml(member.userId)}">Cập nhật</button>` : ''}</td></tr>`;
    }).join('')}</tbody></table></div>` : '<div class="empty-state">Không tìm thấy thành viên phù hợp.</div>'}`;
    byId('organizationMemberSearch')?.addEventListener('input', event => {
      organizationState.memberSearch = event.target.value;
      renderMembers();
      byId('organizationMemberSearch')?.focus();
    });
  }

  async function loadUsage(force = false) {
    const root = byId('organizationTabContent');
    if (organizationState.usage && !force) return renderUsage();
    root.innerHTML = loading('Đang tải budget và usage...');
    const version = organizationState.version;
    try {
      const [usage, members] = await Promise.all([
        request('usage', `/api/organizations/${organizationState.selectedOrganizationId}/usage?take=200`),
        organizationState.members ? Promise.resolve(organizationState.members) : request('usage-members', `/api/organizations/${organizationState.selectedOrganizationId}/members`)
      ]);
      if (version !== organizationState.version) return;
      organizationState.usage = usage;
      organizationState.members = members;
      renderUsage();
    } catch (error) {
      if (error.name !== 'AbortError') root.innerHTML = errorState(error, 'usage');
    }
  }

  function renderUsage() {
    const root = byId('organizationTabContent');
    const data = organizationState.usage;
    if (!data) return;
    const committed = Number(data.actualCost || 0) + Number(data.reservedCost || 0);
    const limit = Number(data.budgetLimit || 0);
    const percent = limit > 0 ? Math.min(100, committed / limit * 100) : 100;
    const progressStatus = percent >= 100 ? 'danger' : percent >= 90 ? 'warning' : percent >= 70 ? 'notice' : '';
    const providers = [...new Set(data.items.map(item => item.providerCode))];
    const models = [...new Set(data.items.map(item => item.modelCode))];
    const kinds = [...new Set(data.items.map(item => item.entryKind))];
    const filtered = data.items.filter(item => (!organizationState.usageFilters.provider || item.providerCode === organizationState.usageFilters.provider) && (!organizationState.usageFilters.model || item.modelCode === organizationState.usageFilters.model) && (!organizationState.usageFilters.kind || item.entryKind === organizationState.usageFilters.kind));
    const memberMap = new Map((organizationState.members || []).map(member => [member.userId, member.displayName || member.email]));
    root.innerHTML = `
      <div class="organization-metrics usage-metrics"><article><span>Budget tháng</span><strong>${escapeHtml(formatMoney(data.budgetLimit, data.currencyCode))}</strong><small>${escapeHtml(formatDate(data.periodStartsAtUtc))} → ${escapeHtml(formatDate(data.periodEndsAtUtc))}</small></article><article><span>Actual cost</span><strong>${escapeHtml(formatMoney(data.actualCost, data.currencyCode))}</strong><small>Đã quyết toán</small></article><article><span>Reserved cost</span><strong>${escapeHtml(formatMoney(data.reservedCost, data.currencyCode))}</strong><small>Đang giữ</small></article><article><span>Remaining</span><strong>${escapeHtml(formatMoney(data.remainingBudget, data.currencyCode))}</strong><small>Còn có thể reserve</small></article><article><span>Input token</span><strong>${escapeHtml(formatMetric(data.inputTokens))}</strong><small>Actual trong kỳ</small></article><article><span>Output token</span><strong>${escapeHtml(formatMetric(data.outputTokens))}</strong><small>Actual trong kỳ</small></article><article><span>Video</span><strong>${escapeHtml(formatMetric(data.videoSeconds, ' giây'))}</strong><small>Actual trong kỳ</small></article></div>
      <div class="budget-progress"><div><span>Đã dùng + đang giữ</span><strong>${escapeHtml(percent.toFixed(1))}%</strong></div><span class="budget-progress-track"><span class="${escapeHtml(progressStatus)}" style="width:${escapeHtml(percent)}%"></span></span></div>
      ${limit === 0 ? '<div class="organization-alert danger"><strong>AI đang bị khóa</strong><span>Budget 0 là trạng thái khóa, không phải không giới hạn.</span></div>' : ''}
      ${capabilities().billing ? `<form id="organizationBudgetForm" class="inline-budget-form"><label>Budget tháng mới (USD)<input id="organizationBudgetLimit" type="number" min="0" max="100000000" step="0.000001" value="${escapeHtml(data.budgetLimit)}" required /></label><button type="submit" class="primary-button">Cập nhật budget</button><small>Budget là hạn mức nội bộ; 0 sẽ khóa AI.</small><p class="form-message"></p></form>` : ''}
      <section class="usage-groups"><div class="section-heading"><div><span class="eyebrow">USAGE BREAKDOWN</span><h3>Theo provider, model và thành viên</h3></div></div>${data.groups?.length ? `<div class="table-scroll"><table class="data-table"><thead><tr><th>Provider / model</th><th>Thành viên</th><th>Chi phí</th><th>Input</th><th>Output</th><th>Video</th></tr></thead><tbody>${data.groups.map(group => `<tr><td><strong>${escapeHtml(group.providerCode)}</strong><br><small>${escapeHtml(group.modelCode)}</small></td><td>${escapeHtml(memberMap.get(group.userId) || group.userId)}</td><td>${escapeHtml(formatMoney(group.actualCost, data.currencyCode))}</td><td>${escapeHtml(formatMetric(group.inputTokens))}</td><td>${escapeHtml(formatMetric(group.outputTokens))}</td><td>${escapeHtml(formatMetric(group.videoSeconds, ' giây'))}</td></tr>`).join('')}</tbody></table></div>` : '<div class="empty-state">Chưa có usage Actual trong kỳ.</div>'}</section>
      <section class="usage-ledger"><div class="section-heading log-heading"><div><span class="eyebrow">LEDGER</span><h3>Đối soát reservation và actual</h3></div><div class="filters"><select data-usage-filter="provider"><option value="">Mọi provider</option>${providers.map(value => `<option ${value === organizationState.usageFilters.provider ? 'selected' : ''}>${escapeHtml(value)}</option>`).join('')}</select><select data-usage-filter="model"><option value="">Mọi model</option>${models.map(value => `<option ${value === organizationState.usageFilters.model ? 'selected' : ''}>${escapeHtml(value)}</option>`).join('')}</select><select data-usage-filter="kind"><option value="">Mọi loại</option>${kinds.map(value => `<option ${value === organizationState.usageFilters.kind ? 'selected' : ''}>${escapeHtml(value)}</option>`).join('')}</select></div></div>${filtered.length ? `<div class="table-scroll"><table class="data-table"><thead><tr><th>Thời gian</th><th>Loại</th><th>Provider / model</th><th>Project</th><th>Thành viên</th><th>Số tiền</th></tr></thead><tbody>${filtered.map(item => `<tr><td>${escapeHtml(formatDate(item.occurredAtUtc))}</td><td>${statusPill(item.entryKind, item.entryKind === 'Actual' ? 'ready' : item.entryKind === 'Reservation' ? 'warning' : 'blocked')}</td><td><strong>${escapeHtml(item.providerCode)}</strong><br><small>${escapeHtml(item.modelCode)}</small></td><td><code>${escapeHtml(item.projectId)}</code></td><td>${escapeHtml(memberMap.get(item.userId) || item.userId)}</td><td>${escapeHtml(formatMoney(item.amount, item.currencyCode))}</td></tr>`).join('')}</tbody></table></div>` : '<div class="empty-state">Không có ledger phù hợp bộ lọc.</div>'}</section>`;
    document.querySelectorAll('[data-usage-filter]').forEach(select => select.addEventListener('change', event => {
      organizationState.usageFilters[event.target.dataset.usageFilter] = event.target.value;
      renderUsage();
    }));
    byId('organizationBudgetForm')?.addEventListener('submit', submitBudget);
  }

  async function loadProviders(force = false) {
    const root = byId('organizationTabContent');
    if (organizationState.providers && organizationState.pricing && organizationState.videoPolicy !== undefined && !force) return renderProviders();
    root.innerHTML = loading('Đang tải trạng thái credential và rate...');
    const version = organizationState.version;
    try {
      const [providers, pricing, videoPolicy] = await Promise.all([
        request('providers', `/api/organizations/${organizationState.selectedOrganizationId}/providers`),
        organizationState.pricing ? Promise.resolve(organizationState.pricing) : request('pricing-detail', '/api/admin/ai-pricing'),
        request('video-policy', `/api/organizations/${organizationState.selectedOrganizationId}/video-policy`)
      ]);
      if (version !== organizationState.version) return;
      organizationState.providers = providers;
      organizationState.pricing = pricing;
      organizationState.videoPolicy = videoPolicy;
      renderProviders();
    } catch (error) {
      if (error.name !== 'AbortError') root.innerHTML = errorState(error, 'providers');
    }
  }

  function activeRates(model) {
    const now = Date.now();
    return (model?.costRates || []).filter(rate => rate.isActive && new Date(rate.effectiveFromUtc).getTime() <= now && (!rate.effectiveToUtc || new Date(rate.effectiveToUtc).getTime() > now));
  }

  function rateMetadata(rate) {
    try {
      const value = JSON.parse(rate?.metadataJson || '{}');
      return value && typeof value === 'object' ? value : {};
    } catch {
      return {};
    }
  }

  function isKlingNativeAudioRate(rate) {
    const metadata = rateMetadata(rate);
    return rate?.usageType === 'VideoSecond' && metadata.resolution?.toLowerCase() === '720p' && metadata.nativeAudio === true;
  }

  function configuredRates(providerCode, model) {
    const rates = activeRates(model);
    return providerCode === 'kling' ? rates.filter(isKlingNativeAudioRate) : rates;
  }

  function rateVariantLabel(providerCode, rate) {
    return providerCode === 'kling' && isKlingNativeAudioRate(rate) ? ' · 720p · Native Audio' : '';
  }

  function providerReadiness(catalogProvider, providerStatus) {
    const model = [...(catalogProvider.models || [])].sort((a, b) => Number(b.isEnabled && b.isDefault) - Number(a.isEnabled && a.isDefault) || Number(b.isEnabled) - Number(a.isEnabled))[0];
    const required = catalogProvider.providerCode === 'openai'
      ? ['InputToken', 'OutputToken']
      : catalogProvider.providerCode === 'kling'
        ? ['VideoSecond']
        : catalogProvider.providerCode === 'byteplus'
          ? ['OutputToken']
          : [];
    const configured = new Set(configuredRates(catalogProvider.providerCode, model).map(rate => rate.usageType));
    const missing = required.filter(value => !configured.has(value));
    const reasons = [];
    if (Number(selectedOrganization()?.monthlyBudgetLimit) <= 0) reasons.push('budget_disabled');
    if (!catalogProvider.isEnabled) {
      reasons.push('provider_disabled');
      return { model, required, missing, reasons, ready: false };
    }
    if (!model?.isEnabled) reasons.push('model_disabled');
    if (!providerStatus?.configured || providerStatus.credentialStatus !== 'Active') reasons.push('credential_missing');
    if (missing.length) reasons.push('pricing_not_configured');
    return { model, required, missing, reasons, ready: reasons.length === 0 };
  }

  function renderProviders() {
    const root = byId('organizationTabContent');
    const providerStatuses = new Map((organizationState.providers || []).map(item => [item.providerCode, item]));
    const catalog = (organizationState.pricing || []).filter(item => ['openai', 'kling', 'byteplus'].includes(item.providerCode));
    const currentPolicy = organizationState.videoPolicy;
    const videoModels = catalog.flatMap(provider => provider.isEnabled
      ? (provider.models || []).filter(model => model.isEnabled && model.modality === 'Video').map(model => ({ provider, model }))
      : []);
    const policyPanel = `<section class="provider-admin-card video-policy-card"><div class="provider-admin-heading"><span class="provider-logo">V</span><div><h3>Policy tạo video</h3><p>Desktop chỉ đọc policy này và không được chọn model.</p></div>${currentPolicy?.isActive ? statusPill(`v${currentPolicy.policyVersion}`, 'ready') : statusPill('Chưa cấu hình', 'blocked')}</div>${currentPolicy ? `<dl><div><dt>Provider</dt><dd>${escapeHtml(currentPolicy.providerName)}</dd></div><div><dt>Model</dt><dd>${escapeHtml(currentPolicy.modelCode)}</dd></div><div><dt>Biến thể</dt><dd>${escapeHtml(currentPolicy.resolution)} · ${currentPolicy.nativeAudio ? 'Native Audio' : 'Không âm thanh native'}</dd></div><div><dt>Cập nhật</dt><dd>${escapeHtml(formatDate(currentPolicy.updatedAtUtc))}</dd></div></dl>` : '<p class="provider-ready-note">Chọn một model video đã được Global Admin bật và đã có credential Active.</p>'}${capabilities().credentials ? `<form id="organizationVideoPolicyForm" class="inline-budget-form"><label>Model video do server sử dụng<select id="organizationVideoPolicyModel" required ${videoModels.length ? '' : 'disabled'}><option value="">Chọn provider / model</option>${videoModels.map(({ provider, model }) => `<option value="${escapeHtml(model.providerModelId)}" ${currentPolicy?.providerModelId === model.providerModelId ? 'selected' : ''}>${escapeHtml(provider.displayName)} · ${escapeHtml(model.displayName)}</option>`).join('')}</select></label><button type="submit" class="primary-button" ${videoModels.length ? '' : 'disabled'}>Lưu policy</button><small>Biến thể cố định: 720p · Native Audio. Dự án đã có snapshot sẽ không tự đổi model.</small><p class="form-message"></p></form>` : '<p class="form-hint">Chỉ Owner hoặc Organization Admin được đổi policy video.</p>'}</section>`;
    root.innerHTML = `${policyPanel}<div class="provider-admin-grid">${catalog.map(provider => {
      const status = providerStatuses.get(provider.providerCode);
      const readiness = providerReadiness(provider, status);
      const credentialControl = !capabilities().credentials
        ? '<p class="form-hint">Vai trò hiện tại chỉ được xem trạng thái.</p>'
        : provider.isEnabled
          ? `<button type="button" class="primary-button" data-configure-provider="${escapeHtml(provider.providerCode)}" data-provider-name="${escapeHtml(provider.displayName)}">${status?.configured ? 'Thay credential' : 'Cấu hình credential'}</button>`
          : `<button type="button" class="ghost-button" data-provider-unavailable="${escapeHtml(provider.providerCode)}" data-provider-name="${escapeHtml(provider.displayName)}">Xem cách kích hoạt</button>`;
      return `<article class="provider-admin-card"><div class="provider-admin-heading"><span class="provider-logo">${escapeHtml(provider.displayName.slice(0, 1).toUpperCase())}</span><div><h3>${escapeHtml(provider.displayName)}</h3><p>${escapeHtml(readiness.model?.modelCode || 'Chưa có model')}</p></div>${readiness.ready ? statusPill('Sẵn sàng', 'ready') : statusPill('Chưa sẵn sàng', 'blocked')}</div><dl><div><dt>Credential</dt><dd>${status?.configured ? `${escapeHtml(status.secretHint || '••••')} · v${escapeHtml(status.credentialVersion)}` : 'Chưa cấu hình'}</dd></div><div><dt>Trạng thái</dt><dd>${provider.isEnabled ? escapeHtml(status?.credentialStatus || 'NotConfigured') : 'ProviderInactive'}</dd></div><div><dt>Cập nhật</dt><dd>${escapeHtml(formatDate(status?.updatedAtUtc))}</dd></div><div><dt>Rate bắt buộc</dt><dd>${escapeHtml(readiness.required.join(', ') || 'Không xác định')}</dd></div></dl>${readiness.reasons.length ? `<ul class="provider-blockers">${readiness.reasons.map(reason => `<li><span>${escapeHtml(readinessMessages[reason] || reason)}${reason === 'pricing_not_configured' ? ` Thiếu: ${escapeHtml(readiness.missing.join(', '))}.` : ''}</span>${renderReadinessAction(reason, provider.providerCode)}</li>`).join('')}</ul>` : '<p class="provider-ready-note">Đủ budget, credential Active, model và rate bắt buộc.</p>'}${credentialControl}</article>`;
    }).join('')}</div>${!catalog.length ? '<div class="empty-state">Chưa có catalog provider AI.</div>' : ''}`;
    byId('organizationVideoPolicyForm')?.addEventListener('submit', submitVideoPolicy);
  }

  async function submitVideoPolicy(event) {
    event.preventDefault();
    const form = event.currentTarget;
    const button = form.querySelector('button[type="submit"]');
    const providerModelId = byId('organizationVideoPolicyModel').value;
    if (!providerModelId) return;
    setBusy(button, true, 'Đang lưu...');
    try {
      organizationState.videoPolicy = await api(`/api/organizations/${organizationState.selectedOrganizationId}/video-policy`, {
        method: 'PUT',
        body: JSON.stringify({ providerModelId, resolution: '720p', nativeAudio: true })
      });
      toast('Đã cập nhật policy video. Dự án mới sẽ dùng policy này; dự án cũ giữ nguyên model.');
      organizationState.organizations = null;
      await loadOrganizations(true);
      renderOrganizationHeading();
      await loadProviders(true);
    } catch (error) {
      form.querySelector('.form-message').textContent = friendlyError(error);
    } finally {
      setBusy(button, false);
    }
  }

  async function loadAudit(force = false) {
    const root = byId('organizationTabContent');
    if (!capabilities().audit) return renderOrganizationOverview();
    if (organizationState.audit && !force) return renderAudit();
    root.innerHTML = loading('Đang tải nhật ký tổ chức...');
    const version = organizationState.version;
    try {
      const data = await request('audit', `/api/organizations/${organizationState.selectedOrganizationId}/audit?take=100`);
      if (version !== organizationState.version) return;
      organizationState.audit = data;
      renderAudit();
    } catch (error) {
      if (error.name !== 'AbortError') root.innerHTML = errorState(error, 'audit');
    }
  }

  function renderAudit() {
    const data = organizationState.audit || [];
    byId('organizationTabContent').innerHTML = data.length ? `<div class="table-scroll"><table class="data-table audit-table"><thead><tr><th>Thời gian</th><th>Sự kiện</th><th>Người thao tác</th><th>Dữ liệu an toàn</th><th>Correlation ID</th></tr></thead><tbody>${data.map(item => `<tr><td>${escapeHtml(formatDate(item.occurredAtUtc))}</td><td><strong>${escapeHtml(auditLabels[item.eventType] || item.eventType)}</strong></td><td>${escapeHtml(item.actorDisplayName || item.actorEmail || item.actorUserId || 'Hệ thống')}</td><td><div class="audit-data">${Object.entries(item.data || {}).map(([key, value]) => `<span><b>${escapeHtml(key)}:</b> ${escapeHtml(value ?? 'null')}</span>`).join('') || '—'}</div></td><td><code>${escapeHtml(item.correlationId || '—')}</code></td></tr>`).join('')}</tbody></table></div>` : '<div class="empty-state">Chưa có sự kiện tổ chức.</div>';
  }

  function preferredGuideModel(provider) {
    return [...(provider?.models || [])]
      .sort((a, b) => Number(b.isEnabled && b.isDefault) - Number(a.isEnabled && a.isDefault) || Number(b.isEnabled) - Number(a.isEnabled))[0] || null;
  }

  function guideRate(model, usageType, providerCode = '') {
    return configuredRates(providerCode, model).find(rate => rate.usageType === usageType) || null;
  }

  function tokenCostForGuide(rate, tokens) {
    if (!rate) return null;
    const divisor = rate.unit === 'Token' ? 1 : rate.unit === '1KTokens' ? 1_000 : rate.unit === 'MillionTokens' ? 1_000_000 : null;
    if (!divisor) return null;
    return Number(rate.unitPrice) * tokens / divisor;
  }

  function renderGuideRateRow(provider, model, usageType, rate) {
    return `<tr><td><strong>${escapeHtml(provider?.displayName || provider?.providerCode || '—')}</strong></td><td>${escapeHtml(model?.modelCode || 'Chưa có model')}</td><td>${escapeHtml(usageType)}</td><td>${rate ? escapeHtml(rate.unit) : '—'}</td><td>${rate ? escapeHtml(formatMoney(rate.unitPrice, rate.currencyCode)) : statusPill('Thiếu rate', 'blocked')}</td><td>${rate ? escapeHtml(formatDate(rate.effectiveFromUtc)) : '—'}</td></tr>`;
  }

  async function loadCostGuide(force = false) {
    const root = byId('costGuideCurrentRates');
    if (organizationState.pricing && !force) return renderCostGuide();
    root.innerHTML = loading('Đang đọc rate hiện hành...');
    try {
      organizationState.pricing = await request('cost-guide-pricing', '/api/admin/ai-pricing');
      renderCostGuide();
    } catch (error) {
      if (error.name !== 'AbortError') root.innerHTML = errorState(error, 'cost-guide');
    }
  }

  function renderCostGuide() {
    const root = byId('costGuideCurrentRates');
    const providers = organizationState.pricing || [];
    const openAi = providers.find(provider => provider.providerCode === 'openai');
    const kling = providers.find(provider => provider.providerCode === 'kling');
    const openAiModel = preferredGuideModel(openAi);
    const klingModel = preferredGuideModel(kling);
    const inputRate = guideRate(openAiModel, 'InputToken');
    const outputRate = guideRate(openAiModel, 'OutputToken');
    const videoRate = guideRate(klingModel, 'VideoSecond', 'kling');
    const inputExample = tokenCostForGuide(inputRate, 10_000);
    const outputExample = tokenCostForGuide(outputRate, 2_000);
    const openAiExample = inputExample === null || outputExample === null ? null : inputExample + outputExample;
    const klingExample = videoRate?.unit === 'Second' ? Number(videoRate.unitPrice) * 10 : null;
    const rows = [
      renderGuideRateRow(openAi, openAiModel, 'InputToken', inputRate),
      renderGuideRateRow(openAi, openAiModel, 'OutputToken', outputRate),
      renderGuideRateRow(kling, klingModel, 'VideoSecond', videoRate)
    ].join('');
    root.innerHTML = `
      <div class="cost-guide-live-grid">
        <article><span>OPENAI · VÍ DỤ</span><h4>10.000 input + 2.000 output token</h4><strong>${openAiExample === null ? 'Chưa tính được' : escapeHtml(formatMoney(openAiExample, inputRate?.currencyCode || outputRate?.currencyCode || 'USD'))}</strong><small>${openAiExample === null ? 'Hãy cấu hình đủ InputToken và OutputToken.' : 'Tính bằng rate Active bên dưới.'}</small></article>
        <article><span>KLING · VÍ DỤ</span><h4>Video 720p · Native Audio · 10 giây</h4><strong>${klingExample === null ? 'Chưa tính được' : escapeHtml(formatMoney(klingExample, videoRate?.currencyCode || 'USD'))}</strong><small>${klingExample === null ? 'Hãy cấu hình đúng rate VideoSecond cho 720p Native Audio.' : 'Tính theo rate USD/giây của đúng biến thể 720p Native Audio.'}</small></article>
      </div>
      <div class="table-scroll"><table class="data-table cost-guide-table"><thead><tr><th>Provider</th><th>Model</th><th>Usage type</th><th>Đơn vị</th><th>Đơn giá Active</th><th>Hiệu lực từ</th></tr></thead><tbody>${rows}</tbody></table></div>`;
  }

  async function loadPricing(force = false) {
    const root = byId('aiPricingCatalog');
    if (organizationState.pricing && !force) return renderPricing();
    root.innerHTML = loading('Đang tải bảng giá AI...');
    try {
      organizationState.pricing = await request('pricing', '/api/admin/ai-pricing');
      renderPricing();
    } catch (error) {
      if (error.name !== 'AbortError') root.innerHTML = errorState(error, 'pricing');
    }
  }

  function requiredRates(providerCode) {
    return providerCode === 'openai'
      ? ['InputToken', 'OutputToken']
      : providerCode === 'kling'
        ? ['VideoSecond']
        : providerCode === 'byteplus'
          ? ['OutputToken']
          : [];
  }

  function renderPricing() {
    const root = byId('aiPricingCatalog');
    const providers = organizationState.pricing || [];
    const sections = providers.map(provider => {
      const providerToggle = `<button type="button" class="${provider.isEnabled ? 'danger-button' : 'primary-button'}" data-toggle-ai-provider="${escapeHtml(provider.providerId)}" data-ai-provider-enabled="${provider.isEnabled ? 'false' : 'true'}">${provider.isEnabled ? 'Tắt provider' : 'Bật provider'}</button>`;
      const models = provider.models.map(model => {
      const rates = configuredRates(provider.providerCode, model);
      const configured = new Set(rates.map(rate => rate.usageType));
      const missing = requiredRates(provider.providerCode).filter(value => !configured.has(value));
        const stateControls = `<button type="button" class="${model.isEnabled ? 'danger-button' : 'ghost-button'}" data-toggle-ai-model="${escapeHtml(model.providerModelId)}" data-ai-model-enabled="${model.isEnabled ? 'false' : 'true'}">${model.isEnabled ? 'Tắt model' : 'Bật model'}</button>${model.isEnabled && !model.isDefault ? `<button type="button" class="ghost-button" data-default-ai-model="${escapeHtml(model.providerModelId)}">Đặt mặc định</button>` : ''}`;
        const variant = provider.providerCode === 'kling' ? ' · 720p · Native Audio' : provider.providerCode === 'byteplus' ? ' · token video hoàn tất' : '';
        return `<article class="pricing-model"><div class="pricing-model-heading"><div><strong>${escapeHtml(model.displayName)}</strong><small>${escapeHtml(model.modelCode)} · ${escapeHtml(model.modality)} · ${model.isEnabled ? 'Enabled' : 'Disabled'}${model.isDefault ? ' · Default' : ''}</small></div><div class="dialog-actions">${stateControls}<button type="button" class="primary-button" data-add-ai-rate="${escapeHtml(model.providerModelId)}" data-ai-rate-model="${escapeHtml(model.displayName)}" data-ai-rate-provider="${escapeHtml(provider.providerCode)}">${icon('plus')}<span>Tạo rate</span></button></div></div>${missing.length ? `<div class="organization-alert warning"><strong>Thiếu rate bắt buộc</strong><span>${escapeHtml(missing.join(', '))}${variant}</span></div>` : ''}${model.costRates.length ? `<div class="table-scroll"><table class="data-table"><thead><tr><th>Usage type</th><th>Đơn vị</th><th>Đơn giá</th><th>Hiệu lực</th><th>Trạng thái</th><th></th></tr></thead><tbody>${model.costRates.map(rate => `<tr><td><strong>${escapeHtml(rate.usageType + rateVariantLabel(provider.providerCode, rate))}</strong></td><td>${escapeHtml(rate.unit)}</td><td>${escapeHtml(formatMoney(rate.unitPrice, rate.currencyCode))}</td><td>${escapeHtml(formatDate(rate.effectiveFromUtc))}<br><small>đến ${escapeHtml(formatDate(rate.effectiveToUtc))}</small></td><td>${rate.isActive ? statusPill('Active', 'ready') : statusPill('Inactive', 'warning')}</td><td>${rate.isActive ? `<button type="button" class="danger-button" data-deactivate-ai-rate="${escapeHtml(rate.costRateId)}">Ngừng rate</button>` : ''}</td></tr>`).join('')}</tbody></table></div>` : '<div class="empty-state">Model chưa có rate.</div>'}</article>`;
      }).join('');
      return `<section class="pricing-provider" data-pricing-provider="${escapeHtml(provider.providerCode)}"><div class="pricing-provider-heading"><div><span class="eyebrow">${escapeHtml(provider.providerCode)}</span><h3>${escapeHtml(provider.displayName)}</h3></div><div class="dialog-actions">${provider.isEnabled ? statusPill('Provider Active', 'ready') : statusPill('Provider Inactive', 'blocked')}${providerToggle}</div></div><div class="pricing-model-list">${models}</div></section>`;
    }).join('');
    root.innerHTML = sections ? `<div class="pricing-provider-list">${sections}</div>` : '<div class="empty-state">Chưa có catalog AI.</div>';
  }

  function openCreateOrganization() {
    const form = byId('organizationForm');
    form.reset();
    byId('organizationBudget').value = '0';
    form.querySelector('.form-message').textContent = '';
    byId('organizationDialog').showModal();
    byId('organizationName').focus();
  }

  function openMemberDialog(member = null) {
    const form = byId('organizationMemberForm');
    form.reset();
    form.querySelector('.form-message').textContent = '';
    byId('organizationMemberUserId').value = member?.userId || '';
    byId('organizationMemberEmail').value = member?.email || '';
    byId('organizationMemberEmail').required = !member;
    byId('organizationMemberEmail').disabled = Boolean(member);
    byId('organizationMemberEmailLabel').classList.toggle('hidden', Boolean(member));
    byId('organizationMemberStatusLabel').classList.toggle('hidden', !member);
    byId('organizationMemberRole').value = member?.role || 'Member';
    byId('organizationMemberStatus').value = member?.status || 'Active';
    byId('organizationMemberLimit').value = member?.monthlyBudgetLimit ?? '';
    byId('organizationMemberDialogTitle').textContent = member ? 'Cập nhật thành viên' : 'Thêm thành viên';
    const ownerOption = [...byId('organizationMemberRole').options].find(option => option.value === 'Owner');
    ownerOption.disabled = selectedOrganization()?.role !== 'Owner';
    byId('organizationMemberDialog').showModal();
  }

  function resetCredentialDialog() {
    const form = byId('organizationCredentialForm');
    form.reset();
    byId('organizationCredentialKey').value = '';
    byId('organizationCredentialProvider').value = '';
    form.querySelector('.form-message').textContent = '';
  }

  function openCredentialDialog(providerCode, providerName) {
    resetCredentialDialog();
    byId('organizationCredentialProvider').value = providerCode;
    byId('organizationCredentialName').value = `${providerName} organization`;
    byId('organizationCredentialDialogTitle').textContent = `Cấu hình ${providerName}`;
    byId('organizationCredentialDialog').showModal();
    byId('organizationCredentialKey').focus();
  }

  function openProviderUnavailableDialog(providerCode, providerName, message = '') {
    byId('organizationProviderUnavailableCode').value = providerCode;
    byId('organizationProviderUnavailableTitle').textContent = `${providerName} chưa được kích hoạt`;
    byId('organizationProviderUnavailableMessage').textContent = message ||
      'Provider này đang bị tắt trong catalog AI của VideoMaker. Hãy bật provider và ít nhất một model trong Bảng giá AI trước khi cấu hình credential. Credential hiện tại (nếu có) không bị thay đổi.';
    byId('organizationProviderUnavailableDialog').showModal();
    byId('organizationProviderUnavailableSetup').focus();
  }

  function syncRateUnit() {
    const video = byId('aiRateUsageType').value === 'VideoSecond';
    byId('aiRateUnit').value = video ? 'Second' : 'MillionTokens';
    [...byId('aiRateUnit').options].forEach(option => option.disabled = video ? option.value !== 'Second' : option.value === 'Second');
  }

  function openRateDialog(modelId, modelName, providerCode) {
    const form = byId('aiRateForm');
    form.reset();
    form.querySelector('.form-message').textContent = '';
    byId('aiRateModelId').value = modelId;
    byId('aiRateProviderCode').value = providerCode;
    byId('aiRateDialogTitle').textContent = `Tạo rate · ${modelName}`;
    byId('aiRateVariantHint').classList.toggle('hidden', providerCode !== 'kling');
    byId('aiRateUsageType').value = providerCode === 'kling'
      ? 'VideoSecond'
      : providerCode === 'byteplus'
        ? 'OutputToken'
        : 'InputToken';
    syncRateUnit();
    byId('aiRateDialog').showModal();
  }

  async function submitOrganization(event) {
    event.preventDefault();
    const form = event.currentTarget;
    const button = form.querySelector('button[type="submit"]');
    form.querySelector('.form-message').textContent = '';
    const budget = Number(byId('organizationBudget').value);
    if (!Number.isFinite(budget) || budget < 0) return form.querySelector('.form-message').textContent = 'Budget không hợp lệ.';
    setBusy(button, true, 'Đang tạo...');
    try {
      const created = await api('/api/organizations', { method: 'POST', body: JSON.stringify({ name: byId('organizationName').value.trim(), code: byId('organizationCode').value.trim() || null, monthlyBudgetLimit: budget, currencyCode: 'USD' }) });
      byId('organizationDialog').close();
      toast('Đã tạo tổ chức. Tài khoản hiện tại là Owner.');
      await loadOrganizations(true);
      await openOrganization(created.organizationId);
    } catch (error) {
      form.querySelector('.form-message').textContent = friendlyError(error);
    } finally {
      setBusy(button, false);
    }
  }

  async function submitMember(event) {
    event.preventDefault();
    const form = event.currentTarget;
    const button = form.querySelector('button[type="submit"]');
    const userId = byId('organizationMemberUserId').value;
    const current = organizationState.members?.find(item => item.userId === userId);
    const role = byId('organizationMemberRole').value;
    const status = userId ? byId('organizationMemberStatus').value : 'Active';
    const limitText = byId('organizationMemberLimit').value;
    const monthlyBudgetLimit = limitText === '' ? null : Number(limitText);
    if (monthlyBudgetLimit !== null && (!Number.isFinite(monthlyBudgetLimit) || monthlyBudgetLimit < 0)) return form.querySelector('.form-message').textContent = 'Hạn mức thành viên không hợp lệ.';
    if (current && (current.role !== role || status !== 'Active') && !confirm('Thay đổi vai trò hoặc trạng thái có thể làm mất quyền truy cập ngay lập tức. Tiếp tục?')) return;
    setBusy(button, true, 'Đang lưu...');
    try {
      const body = userId ? { role, status, monthlyBudgetLimit } : { email: byId('organizationMemberEmail').value.trim(), role, monthlyBudgetLimit };
      await api(userId ? `/api/organizations/${organizationState.selectedOrganizationId}/members/${encodeURIComponent(userId)}` : `/api/organizations/${organizationState.selectedOrganizationId}/members`, { method: userId ? 'PUT' : 'POST', body: JSON.stringify(body) });
      byId('organizationMemberDialog').close();
      toast(userId ? 'Đã cập nhật thành viên.' : 'Đã thêm thành viên.');
      organizationState.members = null;
      await Promise.all([loadMembers(true), loadOrganizations(true)]);
      renderOrganizationHeading();
    } catch (error) {
      form.querySelector('.form-message').textContent = friendlyError(error);
    } finally {
      setBusy(button, false);
    }
  }

  async function submitBudget(event) {
    event.preventDefault();
    const form = event.currentTarget;
    const button = form.querySelector('button[type="submit"]');
    const monthlyBudgetLimit = Number(byId('organizationBudgetLimit').value);
    if (!Number.isFinite(monthlyBudgetLimit) || monthlyBudgetLimit < 0) return form.querySelector('.form-message').textContent = 'Budget không hợp lệ.';
    if (monthlyBudgetLimit === 0 && !confirm('Budget 0 sẽ khóa mọi request AI của tổ chức. Tiếp tục?')) return;
    setBusy(button, true, 'Đang cập nhật...');
    try {
      await api(`/api/organizations/${organizationState.selectedOrganizationId}/budget`, { method: 'PUT', body: JSON.stringify({ monthlyBudgetLimit, currencyCode: 'USD' }) });
      toast('Đã cập nhật budget tổ chức.');
      await loadOrganizations(true);
      organizationState.usage = null;
      renderOrganizationHeading();
      await loadUsage(true);
    } catch (error) {
      form.querySelector('.form-message').textContent = friendlyError(error);
    } finally {
      setBusy(button, false);
    }
  }

  async function submitCredential(event) {
    event.preventDefault();
    const form = event.currentTarget;
    const button = form.querySelector('button[type="submit"]');
    const keyInput = byId('organizationCredentialKey');
    const providerCode = byId('organizationCredentialProvider').value;
    const name = byId('organizationCredentialName').value.trim() || null;
    const apiKey = keyInput.value;
    keyInput.value = '';
    form.querySelector('.form-message').textContent = '';
    setBusy(button, true, 'Đang kiểm tra...');
    try {
      await api(`/api/organizations/${organizationState.selectedOrganizationId}/providers/${encodeURIComponent(providerCode)}/credential`, { method: 'PUT', body: JSON.stringify({ apiKey, name }) });
      byId('organizationCredentialDialog').close();
      toast(`Đã kiểm tra và lưu credential ${providerCode}.`);
      organizationState.providers = null;
      await loadOrganizations(true);
      renderOrganizationHeading();
      await loadProviders(true);
    } catch (error) {
      const code = error?.payload?.code;
      if (code === 'provider_disabled' || code === 'provider_not_found') {
        const provider = organizationState.pricing?.find(item => item.providerCode === providerCode);
        byId('organizationCredentialDialog').close();
        openProviderUnavailableDialog(
          providerCode,
          provider?.displayName || providerCode,
          `${friendlyError(error)} Hãy kiểm tra catalog AI trước khi cấu hình credential. Credential hiện tại (nếu có) không bị thay đổi.`);
      } else {
        form.querySelector('.form-message').textContent = `${friendlyError(error)} Credential cũ (nếu có) vẫn Active.`;
      }
    } finally {
      keyInput.value = '';
      setBusy(button, false);
    }
  }

  async function submitRate(event) {
    event.preventDefault();
    const form = event.currentTarget;
    const button = form.querySelector('button[type="submit"]');
    const source = byId('aiRateSource').value.trim();
    const providerCode = byId('aiRateProviderCode').value;
    const metadata = providerCode === 'kling'
      ? { source: source || null, resolution: '720p', nativeAudio: true }
      : source ? { source } : null;
    setBusy(button, true, 'Đang tạo...');
    try {
      await api(`/api/admin/ai-pricing/models/${byId('aiRateModelId').value}/rates`, { method: 'POST', body: JSON.stringify({ usageType: byId('aiRateUsageType').value, unit: byId('aiRateUnit').value, unitPrice: Number(byId('aiRatePrice').value), currencyCode: 'USD', effectiveFromUtc: null, metadataJson: metadata ? JSON.stringify(metadata) : null }) });
      byId('aiRateDialog').close();
      toast('Đã tạo rate mới và kết thúc rate Active cũ cùng loại.');
      organizationState.pricing = null;
      await loadPricing(true);
      organizationState.organizations = null;
    } catch (error) {
      form.querySelector('.form-message').textContent = friendlyError(error);
    } finally {
      setBusy(button, false);
    }
  }

  async function deactivateRate(rateId) {
    if (!confirm('Ngừng rate này? Request mới sẽ bị chặn nếu model thiếu rate bắt buộc.')) return;
    try {
      await api(`/api/admin/ai-pricing/rates/${rateId}`, { method: 'DELETE' });
      toast('Đã ngừng rate AI.');
      organizationState.pricing = null;
      organizationState.organizations = null;
      await loadPricing(true);
    } catch (error) {
      toast(friendlyError(error), true);
    }
  }

  async function updateProviderState(providerId, isEnabled) {
    if (!isEnabled && !confirm('Tắt provider này? Dự án mới sẽ không thể chọn provider; worker vẫn tiếp tục task đã snapshot.')) return;
    try {
      await api(`/api/admin/ai-pricing/providers/${providerId}`, {
        method: 'PUT',
        body: JSON.stringify({ isEnabled })
      });
      toast(`Đã ${isEnabled ? 'bật' : 'tắt'} provider.`);
      organizationState.pricing = null;
      organizationState.organizations = null;
      await loadPricing(true);
    } catch (error) {
      toast(friendlyError(error), true);
    }
  }

  async function updateModelState(modelId, isEnabled, isDefault = false) {
    if (!isEnabled && !confirm('Tắt model này? Dự án mới sẽ không thể snapshot model; task đang chạy vẫn được worker xử lý.')) return;
    try {
      await api(`/api/admin/ai-pricing/models/${modelId}`, {
        method: 'PUT',
        body: JSON.stringify({ isEnabled, isDefault })
      });
      toast(isDefault ? 'Đã đặt model mặc định trong provider.' : `Đã ${isEnabled ? 'bật' : 'tắt'} model.`);
      organizationState.pricing = null;
      organizationState.organizations = null;
      await loadPricing(true);
    } catch (error) {
      toast(friendlyError(error), true);
    }
  }

  async function refresh() {
    if (organizationState.scope === 'pricing') return loadPricing(true).catch(() => {});
    if (organizationState.scope === 'cost-guide') return loadCostGuide(true).catch(() => {});
    const selectedId = organizationState.selectedOrganizationId;
    await loadOrganizations(true).catch(() => {});
    if (!selectedId) return;
    organizationState.selectedOrganizationId = selectedId;
    renderOrganizationHeading();
    if (organizationState.selectedTab === 'members') organizationState.members = null;
    if (organizationState.selectedTab === 'usage') organizationState.usage = null;
    if (organizationState.selectedTab === 'providers') organizationState.providers = null;
    if (organizationState.selectedTab === 'audit') organizationState.audit = null;
    await selectTab(organizationState.selectedTab, true);
  }

  byId('addOrganizationButton').addEventListener('click', openCreateOrganization);
  byId('backToOrganizations').addEventListener('click', closeOrganization);
  document.querySelectorAll('[data-organization-scope]').forEach(button => button.addEventListener('click', () => showScope(button.dataset.organizationScope)));
  document.querySelectorAll('[data-organization-tab]').forEach(button => button.addEventListener('click', () => selectTab(button.dataset.organizationTab)));
  byId('organizationForm').addEventListener('submit', submitOrganization);
  byId('organizationMemberForm').addEventListener('submit', submitMember);
  byId('organizationCredentialForm').addEventListener('submit', submitCredential);
  byId('aiRateForm').addEventListener('submit', submitRate);
  byId('aiRateUsageType').addEventListener('change', syncRateUnit);
  byId('organizationCredentialDialog').addEventListener('close', resetCredentialDialog);
  byId('organizationProviderUnavailableSetup').addEventListener('click', () => {
    const providerCode = byId('organizationProviderUnavailableCode').value;
    byId('organizationProviderUnavailableDialog').close();
    navigateToReadinessSetup('pricing', providerCode).catch(error => toast(friendlyError(error), true));
  });
  byId('organizationCostGuide').addEventListener('click', event => {
    if (event.target.closest('[data-open-pricing-from-guide]')) showScope('pricing');
    const retry = event.target.closest('[data-organization-retry]');
    if (retry) loadCostGuide(true).catch(() => {});
  });

  byId('organizationTable').addEventListener('click', event => {
    const open = event.target.closest('[data-open-organization]');
    if (open) openOrganization(open.dataset.openOrganization);
    const retry = event.target.closest('[data-organization-retry]');
    if (retry) loadOrganizations(true).catch(() => {});
  });
  byId('organizationTabContent').addEventListener('click', event => {
    const readinessAction = event.target.closest('[data-readiness-action]');
    if (readinessAction) {
      navigateToReadinessSetup(readinessAction.dataset.readinessAction, readinessAction.dataset.readinessProvider).catch(error => toast(friendlyError(error), true));
      return;
    }
    const addMember = event.target.closest('[data-add-organization-member]');
    if (addMember) return openMemberDialog();
    const editMember = event.target.closest('[data-edit-organization-member]');
    if (editMember) return openMemberDialog(organizationState.members?.find(item => item.userId === editMember.dataset.editOrganizationMember));
    const unavailableProvider = event.target.closest('[data-provider-unavailable]');
    if (unavailableProvider) return openProviderUnavailableDialog(unavailableProvider.dataset.providerUnavailable, unavailableProvider.dataset.providerName);
    const provider = event.target.closest('[data-configure-provider]');
    if (provider) return openCredentialDialog(provider.dataset.configureProvider, provider.dataset.providerName);
    const retry = event.target.closest('[data-organization-retry]');
    if (retry) selectTab(retry.dataset.organizationRetry, true);
  });
  byId('aiPricingCatalog').addEventListener('click', event => {
    const providerToggle = event.target.closest('[data-toggle-ai-provider]');
    if (providerToggle) return updateProviderState(providerToggle.dataset.toggleAiProvider, providerToggle.dataset.aiProviderEnabled === 'true');
    const modelToggle = event.target.closest('[data-toggle-ai-model]');
    if (modelToggle) return updateModelState(modelToggle.dataset.toggleAiModel, modelToggle.dataset.aiModelEnabled === 'true');
    const defaultModel = event.target.closest('[data-default-ai-model]');
    if (defaultModel) return updateModelState(defaultModel.dataset.defaultAiModel, true, true);
    const add = event.target.closest('[data-add-ai-rate]');
    if (add) return openRateDialog(add.dataset.addAiRate, add.dataset.aiRateModel, add.dataset.aiRateProvider);
    const deactivate = event.target.closest('[data-deactivate-ai-rate]');
    if (deactivate) return deactivateRate(deactivate.dataset.deactivateAiRate);
    const retry = event.target.closest('[data-organization-retry]');
    if (retry) loadPricing(true).catch(() => {});
  });

  window.videoMakerOrganizationAdmin = Object.freeze({
    activate: () => showScope(organizationState.scope),
    refresh,
    openCreateOrganization
  });
})();
