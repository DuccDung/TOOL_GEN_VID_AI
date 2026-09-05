(() => {
  'use strict';

  const shell = window.videoMakerAdminShell;
  if (!shell) return;
  const { api, escapeHtml, formatDate, icon, paginationMarkup, preservePagePosition, setBusy, setTopbarVisible, toast } = shell;
  const organizationState = {
    organizations: null,
    setupOrganizations: null,
    directoryLoaded: false,
    organizationsPaging: { page: 1, pageSize: 20, totalCount: 0, totalPages: 0, hasPrevious: false, hasNext: false },
    selectedOrganizationId: null,
    selectedTab: 'overview',
    scope: 'setup',
    members: null,
    memberDirectory: null,
    membersPaging: { page: 1, pageSize: 20, totalCount: 0, totalPages: 0, hasPrevious: false, hasNext: false },
    usage: null,
    usagePaging: { page: 1, pageSize: 50, totalCount: 0, totalPages: 0, hasPrevious: false, hasNext: false },
    providers: null,
    videoPolicy: null,
    longFormVideoPolicy: null,
    audit: null,
    auditPaging: { page: 1, pageSize: 20, totalCount: 0, totalPages: 0, hasPrevious: false, hasNext: false },
    pricing: null,
    pools: null,
    setupPools: null,
    poolsPaging: { page: 1, pageSize: 20, totalCount: 0, totalPages: 0, hasPrevious: false, hasNext: false },
    selectedPoolId: null,
    poolDetail: null,
    poolSetupVisible: false,
    planPoolMappings: new Map(),
    pricingExpandedProviders: new Set(['openai', 'fal']),
    memberSearch: '',
    membersLoadedSearch: '',
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
    pricing_not_configured: 'Chưa có đủ rate bắt buộc.',
    video_policy_missing: 'Chưa chọn provider cho Policy Video dài.',
    video_policy_invalid: 'Policy Video dài không còn khớp provider, model hoặc biến thể được hỗ trợ.'
  };
  const auditLabels = {
    OrganizationCreated: 'Tạo tổ chức',
    OrganizationMemberAdded: 'Thêm thành viên',
    OrganizationMemberUpdated: 'Cập nhật thành viên',
    OrganizationBudgetUpdated: 'Cập nhật budget',
    OrganizationProviderCredentialRotated: 'Thay credential'
  };
  const scopeMeta = {
    setup: ['ADMIN SETUP CENTER', 'Thiết lập vận hành', 'Hoàn tất lần lượt hạ tầng AI, tổ chức, gói và phân bổ khách hàng.'],
    directory: ['ORGANIZATION', 'Tổ chức', 'Quản lý cấu hình, thành viên và khả năng chạy Video dài của từng tổ chức.'],
    pools: ['CUSTOMER ALLOCATION', 'Gói và phân bổ khách hàng', 'Kết nối gói bán với các tổ chức đã sẵn sàng và còn sức chứa.'],
    pricing: ['GLOBAL AI PRICING', 'Bảng giá AI', 'Bật đúng provider, model và đơn giá lấy từ tài khoản provider đang sử dụng.'],
    'cost-guide': ['AI COST GUIDE', 'Cách tính chi phí', 'Tra cứu cách VideoMaker giữ budget và quyết toán theo rate hiện hành.']
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
        : reason === 'video_policy_missing' || reason === 'video_policy_invalid'
          ? { target: 'policy', label: 'Chọn Policy Video dài' }
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

  const assignmentStatusLabels = {
    Reserved: ['Đang giữ chỗ', 'warning'],
    Scheduled: ['Đã lên lịch', 'warning'],
    Active: ['Đang hoạt động', 'ready'],
    Released: ['Đã giải phóng', 'warning'],
    Failed: ['Phân bổ lỗi', 'blocked']
  };
  const paymentStatusLabels = {
    Pending: 'Chờ thanh toán',
    Paid: 'Đã nhận tiền',
    Fulfilled: 'Đã hoàn tất',
    Expired: 'Đã hết hạn',
    Failed: 'Thanh toán lỗi'
  };
  const provisioningIssueLabels = {
    organization_capacity_unavailable: 'Không còn tổ chức đủ điều kiện và còn chỗ.',
    organization_not_ready: 'Tổ chức chưa vượt qua kiểm tra sẵn sàng.',
    organization_provisioning_pending: 'Đang chờ hệ thống cấp tổ chức.',
    license_plan_pool_not_configured: 'Gói chưa được gắn với nhóm phân bổ đang hoạt động.'
  };

  function assignmentPresentation(item) {
    const paidPending = item.paymentStatus === 'Paid';
    if (paidPending) {
      return {
        label: 'Đã nhận tiền — chờ cấp tổ chức',
        tone: 'blocked',
        detail: provisioningIssueLabels[item.failureCode] || provisioningIssueLabels[item.releaseReason] || 'Cần quản trị viên kiểm tra và thử phân bổ lại.',
        canRetry: true
      };
    }
    const [label, tone] = assignmentStatusLabels[item.status] || [item.status || 'Chưa xác định', 'warning'];
    const issue = provisioningIssueLabels[item.failureCode] || provisioningIssueLabels[item.releaseReason];
    return {
      label,
      tone,
      detail: issue || paymentStatusLabels[item.paymentStatus] || '',
      canRetry: item.status === 'Failed'
    };
  }

  function longFormReadiness(organization) {
    return organization.aiReadiness?.find(item => item.providerCode?.toLowerCase() !== 'openai') || null;
  }

  function renderReadinessPill(readiness) {
    if (!readiness) return statusPill('Chưa đánh giá', 'warning');
    return readiness.ready
      ? statusPill('Sẵn sàng', 'ready')
      : statusPill(`${readiness.blockingReasons?.length || 1} điều kiện thiếu`, 'blocked');
  }

  function renderLongFormReadinessPill(organization) {
    const readiness = longFormReadiness(organization);
    const provider = readiness?.providerCode === 'fal'
      ? 'Fal/Veo'
      : readiness?.providerCode === 'video-long-form'
        ? 'Chưa chọn policy'
        : readiness?.providerCode || 'Chưa đánh giá';
    return `<span>${escapeHtml(provider)}</span><br><small>${renderReadinessPill(readiness)}</small>`;
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

  async function loadOrganizations(force = false, page = organizationState.organizationsPaging.page, pageSize = organizationState.organizationsPaging.pageSize) {
    return preservePagePosition(async () => {
      if (organizationState.directoryLoaded && organizationState.organizations && !force && organizationState.organizationsPaging.page === page && organizationState.organizationsPaging.pageSize === pageSize) {
        renderOrganizationList();
        return organizationState.organizations;
      }
      byId('organizationTable').innerHTML = loading('Đang tải danh sách tổ chức...');
      try {
        const query = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
        const data = await request('organizations', `/api/organizations/page?${query.toString()}`);
        organizationState.organizations = data.items || [];
        organizationState.organizationsPaging = data;
        organizationState.directoryLoaded = true;
        renderOrganizationList();
        return organizationState.organizations;
      } catch (error) {
        if (error.name !== 'AbortError') byId('organizationTable').innerHTML = errorState(error, 'organizations');
        throw error;
      }
    });
  }

  function renderOrganizationList() {
    const root = byId('organizationTable');
    const organizations = organizationState.organizations || [];
    if (!organizations.length) {
      root.innerHTML = '<div class="empty-state"><strong>Chưa có tổ chức</strong><p>Tạo tổ chức đầu tiên để cấu hình thành viên, budget và AI Gateway.</p></div>';
      return;
    }
    root.innerHTML = `<div class="table-scroll"><table class="data-table organization-table"><thead><tr><th>Tổ chức</th><th>Vai trò</th><th>Thành viên</th><th>Budget kỳ hiện tại</th><th>OpenAI</th><th>Video dài</th><th></th></tr></thead><tbody>${organizations.map(item => `
      <tr><td><strong>${escapeHtml(item.name)}</strong><br><small>${escapeHtml(item.code)} · ${escapeHtml(item.status)}</small></td><td>${escapeHtml(item.role)}</td><td><strong>${escapeHtml(item.activeMemberCount ?? 0)} Active</strong><br><small>${escapeHtml(item.memberCount ?? 0)} tổng cộng</small></td><td><strong>${escapeHtml(formatMoney(item.monthlyBudgetLimit, item.currencyCode))}</strong><br><small>Đã dùng ${escapeHtml(formatMoney(item.actualCost, item.currencyCode))} · giữ ${escapeHtml(formatMoney(item.reservedCost, item.currencyCode))}</small></td><td>${renderReadinessPill(readinessFor(item, 'openai'))}</td><td>${renderLongFormReadinessPill(item)}</td><td><button type="button" class="ghost-button" data-open-organization="${escapeHtml(item.organizationId)}">Xem chi tiết</button></td></tr>`).join('')}</tbody></table></div>${paginationMarkup(organizationState.organizationsPaging, 'organizations', 'tổ chức')}`;
  }

  function showScope(scope) {
    return preservePagePosition(async () => {
    organizationState.scope = scope;
    const detailVisible = scope === 'directory' && Boolean(organizationState.selectedOrganizationId);
    setTopbarVisible(!detailVisible);
    byId('organizationDetailSetupButton')?.classList.toggle('hidden', !detailVisible);
    const meta = scopeMeta[scope] || scopeMeta.setup;
    shell.setPageMeta(...meta);
    document.querySelectorAll('[data-organization-scope]').forEach(button => {
      const active = button.dataset.organizationScope === scope;
      button.classList.toggle('active', active);
      if (active) button.setAttribute('aria-current', 'page');
      else button.removeAttribute('aria-current');
    });
    byId('organizationPricing').classList.toggle('hidden', scope !== 'pricing');
    byId('organizationCostGuide').classList.toggle('hidden', scope !== 'cost-guide');
    byId('organizationPools').classList.toggle('hidden', scope !== 'pools');
    byId('organizationSetup').classList.toggle('hidden', scope !== 'setup');
    byId('organizationDirectory').classList.toggle('hidden', scope !== 'directory' || Boolean(organizationState.selectedOrganizationId));
    byId('organizationDetail').classList.toggle('hidden', scope !== 'directory' || !organizationState.selectedOrganizationId);
    if (scope === 'setup') return loadSetup().catch(() => {});
    if (scope === 'pricing') return loadPricing().catch(() => {});
    if (scope === 'cost-guide') return loadCostGuide().catch(() => {});
    if (scope === 'pools') return loadPools().catch(() => {});
    return loadOrganizations().catch(() => {});
    });
  }

  function providerSetupState(providerCode) {
    const provider = (organizationState.pricing || []).find(item => item.providerCode === providerCode);
    const modality = providerCode === 'openai' ? 'Text' : 'Video';
    const required = requiredRates(providerCode);
    const models = (provider?.models || []).filter(model => model.modality === modality);
    const readyModel = models.find(model => model.isEnabled && required.every(type => configuredRates(providerCode, model).some(rate => rate.usageType === type)));
    const enabledModel = models.find(model => model.isEnabled);
    const missing = !provider?.isEnabled
      ? ['Provider đang tắt']
      : !enabledModel
        ? ['Chưa bật model phù hợp']
        : required.filter(type => !configuredRates(providerCode, enabledModel).some(rate => rate.usageType === type));
    return { provider, model: readyModel || enabledModel || models[0], ready: Boolean(provider?.isEnabled && readyModel), missing };
  }

  function organizationVeoSetupState(organization) {
    const openAi = readinessFor(organization, 'openai');
    const video = longFormReadiness(organization);
    const usesFal = video?.providerCode?.toLowerCase() === 'fal';
    const budgetReady = Number(organization.monthlyBudgetLimit) > 0;
    return {
      ready: Boolean(budgetReady && openAi?.ready && usesFal && video?.ready),
      budgetReady,
      openAiReady: Boolean(openAi?.ready),
      policyReady: usesFal,
      videoReady: Boolean(usesFal && video?.ready)
    };
  }

  async function loadSetup(force = false) {
    return preservePagePosition(async () => {
      const root = byId('adminSetupCenter');
      const hasData = organizationState.setupOrganizations && organizationState.pricing && organizationState.setupPools;
      if (hasData && !force) return renderSetupCenter();
      root.innerHTML = loading('Đang kiểm tra các điều kiện vận hành...');
      try {
        const [organizations, pricing, pools] = await Promise.all([
          force || !organizationState.setupOrganizations ? request('setup-organizations', '/api/organizations') : Promise.resolve(organizationState.setupOrganizations),
          force || !organizationState.pricing ? request('setup-pricing', '/api/admin/ai-pricing') : Promise.resolve(organizationState.pricing),
          force || !organizationState.setupPools ? request('setup-pools', '/api/admin/organization-pools') : Promise.resolve(organizationState.setupPools)
        ]);
        organizationState.setupOrganizations = organizations;
        organizationState.organizations = organizations;
        organizationState.directoryLoaded = false;
        organizationState.pricing = pricing;
        organizationState.setupPools = pools;
        renderSetupCenter();
      } catch (error) {
        if (error.name !== 'AbortError') root.innerHTML = errorState(error, 'setup');
      }
    });
  }

  function renderSetupCenter() {
    const root = byId('adminSetupCenter');
    const organizations = organizationState.setupOrganizations || [];
    const plans = shell.state.plans || [];
    const pools = organizationState.setupPools || [];
    const openAi = providerSetupState('openai');
    const fal = providerSetupState('fal');
    const globalReady = openAi.ready && fal.ready;
    const organizationStates = organizations.map(organization => ({ organization, state: organizationVeoSetupState(organization) }));
    const readyOrganizations = organizationStates.filter(item => item.state.ready);
    const pendingOrganization = organizationStates.find(item => !item.state.ready);
    const activePlans = plans.filter(plan => plan.isActive && plan.isPublic && Number(plan.salePriceVnd) > 0 && Number(plan.defaultDurationDays) > 0);
    const readyPools = pools.filter(pool => pool.status === 'Active' && pool.allocatableOrganizationCount > 0 && pool.activeLicensePlanCount > 0 && pool.allocatableAvailableSeats > 0);
    const stages = [
      { title: 'Hạ tầng OpenAI và Fal/Veo', ready: globalReady, detail: globalReady ? 'Provider, model và bảng giá bắt buộc đã sẵn sàng.' : `${openAi.ready ? 'OpenAI đã sẵn sàng' : `OpenAI: ${openAi.missing.join(', ')}`}; ${fal.ready ? 'Fal/Veo đã sẵn sàng' : `Fal/Veo: ${fal.missing.join(', ')}`}.` },
      { title: 'Tổ chức chạy Video dài', ready: readyOrganizations.length > 0, detail: readyOrganizations.length ? `${readyOrganizations.length} tổ chức đã đủ budget, credential và Policy Video dài Fal/Veo.` : organizations.length ? `${organizations.length} tổ chức còn thiếu điều kiện vận hành.` : 'Chưa có tổ chức để cấu hình.' },
      { title: 'Gói bán cho khách hàng', ready: activePlans.length > 0, detail: activePlans.length ? `${activePlans.length} gói đang hoạt động và được hiển thị để mua.` : 'Chưa có gói công khai đang hoạt động.' },
      { title: 'Nhóm phân bổ khách hàng', ready: readyPools.length > 0, detail: readyPools.length ? `${readyPools.length} nhóm đang hoạt động và còn sức chứa.` : pools.length ? `${pools.length} nhóm chưa đủ điều kiện nhận khách.` : 'Chưa có nhóm phân bổ.' }
    ];
    let next = { action: 'complete', label: 'Hệ thống đã sẵn sàng' };
    if (!globalReady) next = { action: 'pricing', label: 'Hoàn tất bảng giá AI' };
    else if (!organizations.length) next = { action: 'create-organization', label: 'Tạo tổ chức đầu tiên' };
    else if (!readyOrganizations.length) next = { action: 'organization', id: pendingOrganization.organization.organizationId, label: `Tiếp tục ${pendingOrganization.organization.name}` };
    else if (!activePlans.length) next = { action: 'plans', label: 'Tạo gói bán' };
    else if (!pools.length) next = { action: 'pools', label: 'Tạo nhóm phân bổ' };
    else if (!readyPools.length) next = { action: 'pool', id: pools[0].organizationPoolId, label: `Tiếp tục ${pools[0].name}` };
    const completed = stages.filter(stage => stage.ready).length;
    const progressLabel = `Đã hoàn tất ${completed} trên ${stages.length} nhóm thiết lập`;
    root.innerHTML = `<section class="setup-center-hero ${completed === stages.length ? 'complete' : ''}">
      <div class="setup-center-hero-copy">
        <span class="setup-progress-label">TIẾN ĐỘ VẬN HÀNH · ${completed}/${stages.length} NHÓM</span>
        <h3>${completed === stages.length ? 'Sẵn sàng nhận khách và tạo Video dài' : 'Hoàn thành từng bước theo thứ tự'}</h3>
        <p>${completed === stages.length ? 'OpenAI, Fal/Veo, tổ chức, gói và nhóm phân bổ đều đã đạt điều kiện hiện tại.' : 'Bạn không cần nhớ cấu trúc kỹ thuật. VideoMaker sẽ đưa bạn đến đúng nơi cần xử lý tiếp theo.'}</p>
        <progress class="setup-progress" role="progressbar" aria-label="${escapeHtml(progressLabel)}" aria-valuemin="0" aria-valuemax="${stages.length}" aria-valuenow="${completed}" max="${stages.length}" value="${completed}">${escapeHtml(progressLabel)}</progress>
      </div>
      <button type="button" class="primary-button setup-next-button" data-setup-next="${escapeHtml(next.action)}" ${next.id ? `data-setup-id="${escapeHtml(next.id)}"` : ''} ${next.action === 'complete' ? 'disabled' : ''}>${escapeHtml(next.label)}${next.action === 'complete' ? '' : icon('arrow-right')}</button>
    </section>
    <div class="setup-stage-list">${stages.map((stage, index) => `<article class="setup-stage ${stage.ready ? 'complete' : 'pending'}"><span class="setup-stage-number" aria-hidden="true">${stage.ready ? icon('circle-check') : index + 1}</span><div><strong>${escapeHtml(stage.title)}</strong><p>${escapeHtml(stage.detail)}</p></div>${statusPill(stage.ready ? 'Sẵn sàng' : 'Cần xử lý', stage.ready ? 'ready' : 'warning')}</article>`).join('')}</div>
    <div class="setup-center-note"><strong>Luồng mục tiêu:</strong><span>OpenAI sinh content/kịch bản; Fal/Veo tạo clip Video dài; gói và nhóm phân bổ chỉ nhận khách khi tổ chức đã vượt qua kiểm tra.</span></div>`;
  }

  async function runSetupAction(action, id) {
    if (action === 'pricing') {
      shell.setSetupReturn(true);
      return showScope('pricing');
    }
    if (action === 'create-organization') {
      await showScope('directory');
      return openCreateOrganization();
    }
    if (action === 'organization') {
      await showScope('directory');
      return openOrganization(id);
    }
    if (action === 'plans') {
      shell.setSetupReturn(true);
      return shell.navigate('plans', { keepSetupReturn: true });
    }
    if (action === 'pools') {
      await showScope('pools');
      return openPoolDialog();
    }
    if (action === 'pool') {
      await showScope('pools');
      return openPool(id, true);
    }
  }

  async function loadPools(force = false, page = organizationState.poolsPaging.page, pageSize = organizationState.poolsPaging.pageSize) {
    return preservePagePosition(async () => {
      const root = byId('organizationPoolConsole');
      if (organizationState.pools && !force && organizationState.poolsPaging.page === page && organizationState.poolsPaging.pageSize === pageSize) {
        renderPools();
        return organizationState.pools;
      }
      root.innerHTML = loading('Đang tải pool tổ chức...');
      try {
        const data = await request('organization-pools', `/api/admin/organization-pools/page?page=${page}&pageSize=${pageSize}`);
        organizationState.pools = data.items || [];
        organizationState.poolsPaging = data;
        if (organizationState.selectedPoolId) {
          organizationState.poolDetail = await request(
            'organization-pool-detail',
            `/api/admin/organization-pools/${organizationState.selectedPoolId}`);
        }
        renderPools();
        return organizationState.pools;
      } catch (error) {
        if (error.name !== 'AbortError') root.innerHTML = errorState(error, 'pools');
        throw error;
      }
    });
  }

  async function openPool(poolId, showSetup = false) {
    return preservePagePosition(async () => {
      organizationState.selectedPoolId = poolId;
      organizationState.poolSetupVisible = showSetup;
      byId('organizationPoolConsole').innerHTML = loading('Đang tải cấu hình sức chứa...');
      try {
        organizationState.poolDetail = await request(
          'organization-pool-detail',
          `/api/admin/organization-pools/${poolId}`);
        renderPools();
      } catch (error) {
        if (error.name !== 'AbortError') byId('organizationPoolConsole').innerHTML = errorState(error, 'pools');
      }
    });
  }

  function renderPools() {
    const root = byId('organizationPoolConsole');
    const pools = organizationState.pools || [];
    const detail = organizationState.poolDetail;
    const list = pools.length
      ? `<div class="pool-list-summary"><strong>${pools.length} nhóm phân bổ</strong><span>${pools.filter(pool => pool.status === 'Active').length} đang hoạt động · ${pools.reduce((total, pool) => total + Number(pool.allocatableAvailableSeats || 0), 0)} chỗ có thể phân bổ</span></div><div class="table-scroll"><table class="data-table"><thead><tr><th>Nhóm phân bổ</th><th>Tổ chức / gói hoạt động</th><th>Sức chứa tổng</th><th>Đang dùng / giữ</th><th>Có thể phân bổ</th><th></th></tr></thead><tbody>${pools.map(pool => { const incomplete = !pool.allocatableOrganizationCount || !pool.activeLicensePlanCount || pool.status !== 'Active'; return `<tr><td><strong>${escapeHtml(pool.name)}</strong><br><small>${escapeHtml(pool.code)}</small></td><td>${escapeHtml(pool.allocatableOrganizationCount)} / ${escapeHtml(pool.organizationCount)} tổ chức · ${escapeHtml(pool.activeLicensePlanCount)} / ${escapeHtml(pool.licensePlanCount)} gói</td><td>${escapeHtml(pool.seatCapacity)}</td><td>${escapeHtml(pool.activeSeats)} / ${escapeHtml(pool.reservedSeats)}</td><td><strong>${escapeHtml(pool.allocatableAvailableSeats)}</strong><br><small>${pool.status !== 'Active' ? 'Nhóm đang ở bản nháp' : pool.allocatableAvailableSeats > 0 ? 'Sẵn sàng nhận thêm khách' : 'Không có tổ chức sẵn sàng còn chỗ'}</small></td><td><button type="button" class="${incomplete ? 'primary-button' : 'ghost-button'}" ${incomplete ? 'data-open-pool-setup' : 'data-open-pool'}="${escapeHtml(pool.organizationPoolId)}">${incomplete ? 'Tiếp tục thiết lập' : 'Quản lý'}</button></td></tr>`; }).join('')}</tbody></table></div>`
      : '<div class="empty-state"><strong>Chưa có nhóm phân bổ</strong><p>Tạo nhóm nháp, thêm tổ chức đã cấu hình AI, kiểm tra sẵn sàng rồi gắn gói license.</p></div>';
    if (!detail) {
      root.innerHTML = list;
      if (pools.length) root.insertAdjacentHTML('beforeend', paginationMarkup(organizationState.poolsPaging, 'pools', 'nhóm phân bổ'));
      return;
    }

    const pool = detail.pool;
    const organizations = detail.organizations || [];
    const plans = detail.licensePlans || [];
    const assignments = detail.recentAssignments || [];
    const hasReadyOrganization = organizations.some(item => item.isAutoAssignmentEnabled && item.isReady);
    const hasActivePlan = plans.some(item => item.isActive && item.isSellable);
    const setupComplete = pool.status === 'Active' && hasReadyOrganization && hasActivePlan;
    const organizationToCheck = organizations.find(item => item.isAutoAssignmentEnabled && !item.isReady);
    const organizationToEnable = organizations.find(item => !item.isAutoAssignmentEnabled);
    const setupPanel = !setupComplete && organizationState.poolSetupVisible ? `<section class="pool-setup-panel"><div class="pool-setup-heading"><div><span class="eyebrow">THIẾT LẬP NHÓM</span><h3>Hoàn tất để bắt đầu nhận khách</h3><p>Các bước được kiểm tra theo dữ liệu thật; trạng thái sẵn sàng không do Admin tự khai báo.</p></div>${statusPill('Cần hoàn tất', 'warning')}</div><div class="pool-setup-checklist"><article class="${organizations.length ? 'complete' : 'pending'}"><span>1</span><div><strong>Thêm tổ chức và sức chứa</strong><small>${organizations.length ? `Đã có ${organizations.length} tổ chức trong nhóm.` : 'Chưa có tổ chức nào để nhận khách.'}</small></div>${organizations.length ? '' : '<button type="button" class="primary-button" data-add-pool-organization>Thêm tổ chức</button>'}</article><article class="${hasReadyOrganization ? 'complete' : 'pending'}"><span>2</span><div><strong>Kiểm tra tổ chức sẵn sàng</strong><small>${hasReadyOrganization ? 'Đã có tổ chức vượt qua kiểm tra và đang bật phân bổ tự động.' : 'Server cần kiểm tra budget, OpenAI, policy, credential và đơn giá.'}</small></div>${hasReadyOrganization ? '' : organizationToCheck ? `<button type="button" class="primary-button" data-check-pool-organization="${escapeHtml(organizationToCheck.organizationId)}">Kiểm tra ngay</button>` : organizationToEnable ? `<button type="button" class="ghost-button" data-edit-pool-organization="${escapeHtml(organizationToEnable.organizationId)}">Bật phân bổ</button>` : ''}</article><article class="${hasActivePlan ? 'complete' : 'pending'}"><span>3</span><div><strong>Gắn gói license</strong><small>${hasActivePlan ? 'Đã có gói license hoạt động.' : 'Chưa có gói nào được phân bổ vào nhóm.'}</small></div>${hasActivePlan ? '' : '<button type="button" class="primary-button" data-add-pool-plan>Gắn gói</button>'}</article><article class="${pool.status === 'Active' ? 'complete' : 'pending'}"><span>4</span><div><strong>Kích hoạt nhận khách</strong><small>${pool.status === 'Active' ? 'Nhóm đang hoạt động.' : 'Chỉ kích hoạt sau khi tổ chức và gói đã sẵn sàng.'}</small></div>${pool.status === 'Active' ? '' : `<button type="button" class="ghost-button" data-edit-pool="${escapeHtml(pool.organizationPoolId)}">Đổi trạng thái</button>`}</article></div></section>` : '';
    root.innerHTML = `<section class="pool-detail-panel standalone">
      <button type="button" class="text-button pool-back-button" data-close-pool>${icon('arrow-left')}<span>Danh sách nhóm phân bổ</span></button>
      <div class="section-heading"><div><span class="eyebrow">${escapeHtml(pool.code)}</span><h2>${escapeHtml(pool.name)}</h2><p>${pool.status === 'Active' ? 'Đang hoạt động' : 'Bản nháp'} · Cân bằng theo ưu tiên</p></div><div class="inline-actions">${!setupComplete ? '<button type="button" class="ghost-button" data-show-pool-setup>Hướng dẫn thiết lập</button>' : ''}<button type="button" class="ghost-button" data-edit-pool="${escapeHtml(pool.organizationPoolId)}">Sửa nhóm</button><button type="button" class="primary-button" data-add-pool-organization>Thêm tổ chức</button><button type="button" class="ghost-button" data-add-pool-plan>Gắn gói</button></div></div>
      ${setupPanel}
      <div class="organization-metrics pool-metrics"><article><span>Sức chứa cấu hình</span><strong>${escapeHtml(pool.seatCapacity)}</strong><small>Tổng giới hạn của mọi tổ chức</small></article><article><span>Đang dùng</span><strong>${escapeHtml(pool.activeSeats)}</strong><small>Khách đang hoạt động</small></article><article><span>Đang giữ</span><strong>${escapeHtml(pool.reservedSeats)}</strong><small>Chỗ đang chờ thanh toán</small></article><article><span>Có thể phân bổ</span><strong>${escapeHtml(pool.allocatableAvailableSeats)}</strong><small>${escapeHtml(pool.allocatableOrganizationCount)} tổ chức đang sẵn sàng</small></article></div>
      <h3>Tổ chức nhận khách</h3>${organizations.length ? `<div class="table-scroll"><table class="data-table"><thead><tr><th>Tổ chức</th><th>Sức chứa</th><th>Dùng / giữ</th><th>Có thể phân bổ</th><th>Ưu tiên</th><th>Kiểm tra sẵn sàng</th><th></th></tr></thead><tbody>${organizations.map(item => `<tr><td><strong>${escapeHtml(item.organizationName)}</strong><br><small>${escapeHtml(item.organizationCode)} · ${escapeHtml(item.organizationStatus)}</small></td><td>${escapeHtml(item.seatCapacity)}</td><td>${escapeHtml(item.activeSeats)} / ${escapeHtml(item.reservedSeats)}</td><td><strong>${escapeHtml(item.allocatableAvailableSeats)}</strong><br><small>${item.canReceiveCustomers ? 'Đang mở nhận khách' : `Còn ${escapeHtml(item.availableSeats)} chỗ cấu hình nhưng chưa thể bán`}</small></td><td>${escapeHtml(item.priority)}</td><td>${item.canReceiveCustomers ? statusPill('Đang nhận khách', 'ready') : item.isAutoAssignmentEnabled && item.isReady ? statusPill('Tổ chức đã sẵn sàng · chờ nhóm/gói', 'warning') : item.isReady ? statusPill('Đã kiểm tra · tự động đang tắt', 'warning') : item.isAutoAssignmentEnabled ? `${statusPill('Chưa sẵn sàng', 'blocked')}<br><button type="button" class="readiness-setup-link" data-check-pool-organization="${escapeHtml(item.organizationId)}">Kiểm tra sẵn sàng →</button>` : `${statusPill('Tự động đang tắt', 'warning')}<br><small>Bật phân bổ trước khi kiểm tra.</small>`}<br><small>${escapeHtml(item.readinessMessage || '')}</small></td><td><div class="inline-actions"><button type="button" class="ghost-button" data-edit-pool-organization="${escapeHtml(item.organizationId)}">Sửa</button><button type="button" class="ghost-button danger" data-remove-pool-organization="${escapeHtml(item.organizationId)}">Gỡ</button></div></td></tr>`).join('')}</tbody></table></div>` : '<div class="empty-state">Chưa thêm tổ chức vào nhóm phân bổ.</div>'}
      <h3>Gói được phân bổ</h3>${plans.length ? `<div class="table-scroll"><table class="data-table"><thead><tr><th>Gói</th><th>Hạn mức AI thành viên</th><th>Trạng thái</th><th></th></tr></thead><tbody>${plans.map(item => { const sellable = item.isActive && item.isSellable; const label = !item.isActive ? 'Mapping đã tắt' : item.isSellable ? 'Đang mở bán' : 'Gói chưa đủ điều kiện mở bán'; return `<tr><td><strong>${escapeHtml(item.planName)}</strong><br><small>${escapeHtml(item.planCode)}</small></td><td>${item.defaultMemberMonthlyBudgetLimit === null ? 'Theo tổ chức' : escapeHtml(formatMoney(item.defaultMemberMonthlyBudgetLimit))}</td><td>${statusPill(label, sellable ? 'ready' : 'warning')}</td><td><div class="inline-actions"><button type="button" class="ghost-button" data-edit-pool-plan="${escapeHtml(item.licensePlanId)}">Sửa</button><button type="button" class="ghost-button danger" data-remove-pool-plan="${escapeHtml(item.licensePlanId)}">Gỡ</button></div></td></tr>`; }).join('')}</tbody></table></div>` : '<div class="empty-state">Chưa gắn gói license vào nhóm này.</div>'}
      <h3>Phân bổ gần đây</h3>${assignments.length ? `<div class="table-scroll"><table class="data-table"><thead><tr><th>Người dùng</th><th>Gói / đơn</th><th>Tổ chức</th><th>Trạng thái nghiệp vụ</th><th>Hiệu lực</th><th></th></tr></thead><tbody>${assignments.map(item => { const presentation = assignmentPresentation(item); return `<tr><td>${escapeHtml(item.userEmail)}<br><small>${item.membershipManaged ? 'Thành viên tự động' : 'Thành viên thủ công'}</small></td><td><strong>${escapeHtml(item.planCode)}</strong><br><small>${escapeHtml(item.orderCode)}</small></td><td>${escapeHtml(item.organizationName)}</td><td>${statusPill(presentation.label, presentation.tone)}<br><small>${escapeHtml(presentation.detail)}</small></td><td><small>${escapeHtml(formatDate(item.startsAtUtc || item.reservedAtUtc))}<br>${escapeHtml(formatDate(item.endsAtUtc || item.reservationExpiresAtUtc))}</small></td><td>${presentation.canRetry ? `<button type="button" class="primary-button" data-retry-assignment="${escapeHtml(item.organizationSeatAssignmentId)}">Thử phân bổ lại</button>` : ''}</td></tr>`; }).join('')}</tbody></table></div><p class="form-hint">Hiển thị tối đa 100 lượt phân bổ được cập nhật gần nhất.</p>` : '<div class="empty-state">Chưa có lượt phân bổ nào.</div>'}
    </section>`;
  }

  function openPoolDialog(pool = null) {
    const form = byId('organizationPoolForm');
    form.reset();
    form.querySelector('.form-message').textContent = '';
    byId('organizationPoolId').value = pool?.organizationPoolId || '';
    byId('organizationPoolCode').value = pool?.code || '';
    byId('organizationPoolCode').dataset.autoValue = pool?.code || '';
    byId('organizationPoolName').value = pool?.name || '';
    byId('organizationPoolStatus').value = pool?.status || 'Inactive';
    byId('organizationPoolDialogTitle').textContent = pool ? 'Cập nhật nhóm phân bổ' : 'Tạo nhóm phân bổ';
    byId('organizationPoolSubmit').textContent = pool ? 'Lưu thay đổi' : 'Tạo nhóm';
    byId('organizationPoolDialog').showModal();
  }

  function poolCodeFromName(value) {
    return value.normalize('NFD')
      .replace(/[\u0300-\u036f]/g, '')
      .toLowerCase()
      .replace(/đ/g, 'd')
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-+|-+$/g, '')
      .slice(0, 50);
  }

  function syncPoolCodeFromName() {
    const code = byId('organizationPoolCode');
    const suggested = poolCodeFromName(byId('organizationPoolName').value);
    if (!code.value || code.value === code.dataset.autoValue) {
      code.value = suggested;
      code.dataset.autoValue = suggested;
    }
  }

  function closePool() {
    return preservePagePosition(() => {
      organizationState.selectedPoolId = null;
      organizationState.poolDetail = null;
      organizationState.poolSetupVisible = false;
      renderPools();
    });
  }

  async function openPoolOrganizationDialog(organizationId = null) {
    if (!organizationState.setupOrganizations) await loadSetup();
    const detail = organizationState.poolDetail;
    const current = detail?.organizations?.find(item => item.organizationId === organizationId) || null;
    const available = [...(organizationState.setupOrganizations || [])];
    if (current && !available.some(item => item.organizationId === current.organizationId)) available.push(current);
    if (!available.length) {
      toast('Chưa có tổ chức để thêm. Hãy tạo và cấu hình tổ chức trước.', true);
      await showScope('directory');
      openCreateOrganization();
      return;
    }
    const select = byId('poolOrganizationId');
    select.innerHTML = available.map(item => `<option value="${escapeHtml(item.organizationId)}">${escapeHtml(item.name || item.organizationName)} · ${escapeHtml(item.code || item.organizationCode)}</option>`).join('');
    select.disabled = Boolean(current);
    select.value = current?.organizationId || select.options[0]?.value || '';
    byId('poolOrganizationPoolId').value = detail?.pool.organizationPoolId || '';
    const minimumCapacity = Math.max(1, Number(current?.activeSeats || 0) + Number(current?.reservedSeats || 0));
    byId('poolOrganizationCapacity').min = String(minimumCapacity);
    byId('poolOrganizationCapacity').value = current?.seatCapacity || minimumCapacity;
    byId('poolOrganizationCapacityMinimum').textContent = current
      ? `Hiện có ${current.activeSeats} khách đang dùng và ${current.reservedSeats} chỗ đang giữ. Không thể giảm dưới ${minimumCapacity}.`
      : 'Số khách tối đa có thể được phân bổ vào tổ chức này.';
    byId('poolOrganizationPriority').value = current?.priority ?? 100;
    byId('poolOrganizationAuto').checked = current?.isAutoAssignmentEnabled || false;
    byId('organizationPoolOrganizationForm').querySelector('.form-message').textContent = '';
    byId('organizationPoolOrganizationDialog').showModal();
  }

  async function openPoolPlanDialog(planId = null) {
    const detail = organizationState.poolDetail;
    const current = detail?.licensePlans?.find(item => item.licensePlanId === planId) || null;
    const plans = shell.state.plans || [];
    if (!plans.length) {
      toast('Chưa có gói license. Hãy tạo gói trước khi cấu hình phân bổ.', true);
      shell.setSetupReturn(true);
      shell.navigate('plans', { keepSetupReturn: true });
      return;
    }
    const poolDetails = await Promise.all((organizationState.setupPools || organizationState.pools || []).map(pool =>
      pool.organizationPoolId === detail?.pool.organizationPoolId
        ? Promise.resolve(detail)
        : request(`pool-plan-mapping-${pool.organizationPoolId}`, `/api/admin/organization-pools/${pool.organizationPoolId}`)));
    organizationState.planPoolMappings = new Map();
    poolDetails.forEach(poolDetail => (poolDetail?.licensePlans || []).forEach(mapping => {
      organizationState.planPoolMappings.set(mapping.licensePlanId, {
        poolId: mapping.organizationPoolId,
        poolName: mapping.organizationPoolName
      });
    }));
    const select = byId('poolPlanId');
    select.innerHTML = plans.map(item => {
      const mapping = organizationState.planPoolMappings.get(item.licensePlanId);
      const suffix = mapping && mapping.poolId !== detail?.pool.organizationPoolId ? ` · đang ở ${mapping.poolName}` : '';
      return `<option value="${escapeHtml(item.licensePlanId)}">${escapeHtml(item.name)} · ${escapeHtml(item.planCode)}${escapeHtml(suffix)}</option>`;
    }).join('');
    select.disabled = Boolean(current);
    select.value = current?.licensePlanId || select.options[0]?.value || '';
    byId('poolPlanPoolId').value = detail?.pool.organizationPoolId || '';
    byId('poolPlanMemberBudget').value = current?.defaultMemberMonthlyBudgetLimit ?? '';
    byId('poolPlanActive').checked = current?.isActive ?? true;
    byId('organizationPoolPlanForm').querySelector('.form-message').textContent = '';
    byId('organizationPoolPlanDialog').showModal();
  }

  async function submitPool(event) {
    event.preventDefault();
    const form = event.currentTarget;
    const button = form.querySelector('button[type="submit"]');
    const id = byId('organizationPoolId').value;
    const nextStatus = byId('organizationPoolStatus').value;
    const current = organizationState.pools?.find(pool => pool.organizationPoolId === id);
    if (nextStatus === 'Active' && current?.status !== 'Active' && !confirm('Kích hoạt nhóm phân bổ? Gói đang ánh xạ sẽ bắt đầu bán theo số chỗ thực sự sẵn sàng.')) return;
    if (nextStatus === 'Inactive' && current?.status === 'Active' && !confirm('Chuyển nhóm về Nháp? Hệ thống sẽ ngừng giữ chỗ mới cho các gói thuộc nhóm này.')) return;
    setBusy(button, true, 'Đang lưu...');
    try {
      const saved = await api(id ? `/api/admin/organization-pools/${id}` : '/api/admin/organization-pools', {
        method: id ? 'PUT' : 'POST',
        body: JSON.stringify({ code: byId('organizationPoolCode').value.trim(), name: byId('organizationPoolName').value.trim(), status: nextStatus })
      });
      byId('organizationPoolDialog').close();
      organizationState.selectedPoolId = saved.organizationPoolId;
      organizationState.poolDetail = null;
      organizationState.pools = null;
      organizationState.setupPools = null;
      toast('Đã lưu pool tổ chức.');
      await loadPools(true);
    } catch (error) {
      form.querySelector('.form-message').textContent = friendlyError(error);
    } finally {
      setBusy(button, false);
    }
  }

  async function submitPoolOrganization(event) {
    event.preventDefault();
    const form = event.currentTarget;
    const button = form.querySelector('button[type="submit"]');
    const poolId = byId('poolOrganizationPoolId').value;
    const organizationId = byId('poolOrganizationId').value;
    const current = organizationState.poolDetail?.organizations?.find(item => item.organizationId === organizationId);
    const autoAssignmentEnabled = byId('poolOrganizationAuto').checked;
    if (current?.isAutoAssignmentEnabled && !autoAssignmentEnabled && !confirm('Tắt phân bổ tự động cho tổ chức này? Số chỗ trống của tổ chức sẽ lập tức không còn được bán.')) return;
    setBusy(button, true, 'Đang kiểm tra...');
    try {
      await api(`/api/admin/organization-pools/${poolId}/organizations/${organizationId}`, {
        method: 'PUT',
        body: JSON.stringify({ organizationId, seatCapacity: Number(byId('poolOrganizationCapacity').value), priority: Number(byId('poolOrganizationPriority').value), isAutoAssignmentEnabled: autoAssignmentEnabled, isReady: current?.isReady || false, readinessMessage: null })
      });
      byId('organizationPoolOrganizationDialog').close();
      organizationState.pools = null;
      organizationState.setupPools = null;
      toast('Đã lưu cấu hình tổ chức. Hãy chạy kiểm tra sẵn sàng trước khi nhận khách.');
      await loadPools(true);
    } catch (error) {
      form.querySelector('.form-message').textContent = friendlyError(error);
    } finally {
      setBusy(button, false);
    }
  }

  async function checkPoolOrganizationReady(organizationId, button) {
    const current = organizationState.poolDetail?.organizations?.find(item => item.organizationId === organizationId);
    const poolId = organizationState.poolDetail?.pool.organizationPoolId;
    if (!current || !poolId) return;
    if (!current.isAutoAssignmentEnabled) {
      toast('Hãy bật phân bổ khách tự động cho tổ chức trước khi kiểm tra.', true);
      return openPoolOrganizationDialog(organizationId);
    }
    setBusy(button, true, 'Đang kiểm tra...');
    try {
      await api(`/api/admin/organization-pools/${poolId}/organizations/${organizationId}`, {
        method: 'PUT',
        body: JSON.stringify({ organizationId, seatCapacity: Number(current.seatCapacity), priority: Number(current.priority), isAutoAssignmentEnabled: true, isReady: true, readinessMessage: null })
      });
        organizationState.pools = null;
        organizationState.setupPools = null;
      toast('Tổ chức đã vượt qua kiểm tra sẵn sàng.');
      await loadPools(true);
    } catch (error) {
      toast(friendlyError(error), true);
    } finally {
      setBusy(button, false);
    }
  }

  async function submitPoolPlan(event) {
    event.preventDefault();
    const form = event.currentTarget;
    const button = form.querySelector('button[type="submit"]');
    const poolId = byId('poolPlanPoolId').value;
    const planId = byId('poolPlanId').value;
    const budgetText = byId('poolPlanMemberBudget').value;
    const existingMapping = organizationState.planPoolMappings.get(planId);
    if (existingMapping && existingMapping.poolId !== poolId &&
        !confirm(`Gói này đang thuộc nhóm “${existingMapping.poolName}”. Chuyển gói sang nhóm hiện tại?`)) return;
    setBusy(button, true, 'Đang lưu...');
    try {
      await api(`/api/admin/organization-pools/license-plans/${planId}`, {
        method: 'PUT',
        body: JSON.stringify({ organizationPoolId: poolId, defaultMemberMonthlyBudgetLimit: budgetText === '' ? null : Number(budgetText), isActive: byId('poolPlanActive').checked })
      });
      byId('organizationPoolPlanDialog').close();
      organizationState.pools = null;
      organizationState.setupPools = null;
      toast('Đã gắn gói vào pool.');
      await loadPools(true);
    } catch (error) {
      form.querySelector('.form-message').textContent = friendlyError(error);
    } finally {
      setBusy(button, false);
    }
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
      organizationState.pricingExpandedProviders.add(providerCode);
      shell.setSetupReturn(true);
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
    if (target === 'policy') {
      await showScope('directory');
      await selectTab('providers');
      document.querySelector('[data-policy-scope="LongForm"]')?.scrollIntoView({ behavior: 'smooth', block: 'center' });
      document.querySelector('[data-policy-scope="LongForm"] select')?.focus({ preventScroll: true });
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
    return preservePagePosition(async () => {
      cancelRequests();
      organizationState.selectedOrganizationId = organizationId;
      organizationState.selectedTab = 'overview';
      organizationState.members = null;
      organizationState.memberDirectory = null;
      organizationState.memberSearch = '';
      organizationState.membersLoadedSearch = '';
      organizationState.membersPaging = { page: 1, pageSize: 20, totalCount: 0, totalPages: 0, hasPrevious: false, hasNext: false };
      organizationState.usage = null;
      organizationState.usagePaging = { page: 1, pageSize: organizationState.usagePaging.pageSize, totalCount: 0, totalPages: 0, hasPrevious: false, hasNext: false };
      organizationState.usagePaging = { page: 1, pageSize: 50, totalCount: 0, totalPages: 0, hasPrevious: false, hasNext: false };
      organizationState.providers = null;
      organizationState.videoPolicy = null;
      organizationState.longFormVideoPolicy = null;
      organizationState.audit = null;
      organizationState.auditPaging = { page: 1, pageSize: 20, totalCount: 0, totalPages: 0, hasPrevious: false, hasNext: false };
      byId('organizationDirectory').classList.add('hidden');
      byId('organizationDetail').classList.remove('hidden');
      setTopbarVisible(false);
      byId('organizationDetailSetupButton')?.classList.remove('hidden');
      renderOrganizationHeading();
      shell.setSetupReturn(true);
      await selectTab('overview');
    });
  }

  function closeOrganization() {
    return preservePagePosition(() => {
      cancelRequests();
      organizationState.selectedOrganizationId = null;
      organizationState.members = null;
      organizationState.memberDirectory = null;
      organizationState.memberSearch = '';
      organizationState.membersLoadedSearch = '';
      organizationState.membersPaging = { page: 1, pageSize: 20, totalCount: 0, totalPages: 0, hasPrevious: false, hasNext: false };
      organizationState.usage = null;
      organizationState.usagePaging = { page: 1, pageSize: 50, totalCount: 0, totalPages: 0, hasPrevious: false, hasNext: false };
      organizationState.providers = null;
      organizationState.videoPolicy = null;
      organizationState.longFormVideoPolicy = null;
      organizationState.audit = null;
      organizationState.auditPaging = { page: 1, pageSize: 20, totalCount: 0, totalPages: 0, hasPrevious: false, hasNext: false };
      byId('organizationDetail').classList.add('hidden');
      byId('organizationDirectory').classList.remove('hidden');
      setTopbarVisible(true);
      byId('organizationDetailSetupButton')?.classList.add('hidden');
      shell.setPageMeta(...scopeMeta.directory);
    });
  }

  function renderOrganizationHeading() {
    const organization = selectedOrganization();
    if (organization) shell.setPageMeta('ORGANIZATION SETUP', organization.name, `${organization.code} · ${organization.status} · ${organization.role}`);
    const auditTab = document.querySelector('[data-organization-tab="audit"]');
    auditTab.classList.toggle('hidden', !capabilities().audit);
  }

  async function selectTab(tab, force = false) {
    return preservePagePosition(async () => {
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
    });
  }

  function renderOrganizationOverview() {
    const root = byId('organizationTabContent');
    const organization = selectedOrganization();
    if (!organization) return;
    const readiness = organization.aiReadiness || [];
    const warnings = readiness.flatMap(item => (item.blockingReasons || []).map(reason => ({ provider: item.providerCode, reason, missing: item.missingUsageTypes || [] })));
    const veoSetup = organizationVeoSetupState(organization);
    const setupSteps = [
      ['Budget', veoSetup.budgetReady],
      ['OpenAI sinh nội dung', veoSetup.openAiReady],
      ['Policy Video dài dùng Fal/Veo', veoSetup.policyReady],
      ['Fal/Veo tạo video', veoSetup.videoReady]
    ];
    const completedSteps = setupSteps.filter(([, ready]) => ready).length;
    const nextWarning = warnings.find(item => item.reason === 'budget_disabled') ||
      warnings.find(item => item.provider === 'openai') ||
      warnings.find(item => item.reason === 'video_policy_missing' || item.reason === 'video_policy_invalid') ||
      warnings[0];
    root.innerHTML = `
      <section class="organization-setup-hero ${veoSetup.ready ? 'complete' : ''}">
        <div class="organization-setup-copy"><span class="eyebrow">VIDEO DÀI · OPENAI + FAL/VEO</span><h3>${veoSetup.ready ? 'Tổ chức đã sẵn sàng' : `${completedSteps}/${setupSteps.length} điều kiện đã hoàn tất`}</h3><p>${veoSetup.ready ? 'Dự án Video dài mới có thể dùng OpenAI để sinh nội dung và Fal/Veo để tạo clip theo policy hiện tại.' : 'Hoàn tất điều kiện còn thiếu theo thứ tự; provider không được chọn sẽ không chặn tổ chức.'}</p></div>
        <div class="organization-setup-actions">${statusPill(veoSetup.ready ? 'Sẵn sàng' : 'Cần thiết lập', veoSetup.ready ? 'ready' : 'warning')}${nextWarning ? renderReadinessAction(nextWarning.reason, nextWarning.provider) : ''}</div>
        <div class="organization-setup-steps">${setupSteps.map(([label, ready]) => `<span class="${ready ? 'complete' : ''}">${ready ? icon('circle-check') : '<i></i>'}${escapeHtml(label)}</span>`).join('')}</div>
      </section>
      <div class="organization-metrics">
        <article><span>Budget tháng</span><strong>${escapeHtml(formatMoney(organization.monthlyBudgetLimit, organization.currencyCode))}</strong><small>Hạn mức nội bộ</small></article>
        <article><span>Đã dùng</span><strong>${escapeHtml(formatMoney(organization.actualCost, organization.currencyCode))}</strong><small>Kỳ UTC hiện tại</small></article>
        <article><span>Đang giữ</span><strong>${escapeHtml(formatMoney(organization.reservedCost, organization.currencyCode))}</strong><small>Reservation chưa quyết toán</small></article>
        <article><span>Còn lại</span><strong>${escapeHtml(formatMoney(organization.remainingBudget, organization.currencyCode))}</strong><small>Không phải số dư đã nạp</small></article>
      </div>
      ${Number(organization.monthlyBudgetLimit) === 0 ? `<div class="organization-alert danger"><strong>AI đang bị khóa</strong><span>Budget tổ chức bằng 0. Hãy đặt hạn mức lớn hơn 0 trước khi phát sinh request AI.</span>${renderReadinessAction('budget_disabled')}</div>` : ''}
      <div class="organization-overview-grid"><section><div class="section-heading"><div><span class="eyebrow">AI READINESS</span><h3>Luồng Video dài</h3></div></div><div class="organization-provider-list">${readiness.length ? readiness.map(renderOverviewProvider).join('') : '<div class="empty-state">Chưa có dữ liệu để đánh giá OpenAI và Policy Video dài.</div>'}</div></section><section><div class="section-heading"><div><span class="eyebrow">CONFIGURATION</span><h3>Điều kiện cần xử lý</h3></div></div>${warnings.length ? `<ul class="warning-list">${warnings.map(item => `<li><strong>${escapeHtml(readinessProviderLabel(item.provider))}</strong><span>${escapeHtml(readinessMessages[item.reason] || item.reason)}${item.reason === 'pricing_not_configured' && item.missing.length ? ` Thiếu: ${escapeHtml(item.missing.join(', '))}.` : ''}</span>${renderReadinessAction(item.reason, item.provider)}</li>`).join('')}</ul>` : '<div class="empty-state success-state">OpenAI và provider Video dài đã sẵn sàng.</div>'}</section></div>`;
  }

  function readinessProviderLabel(providerCode) {
    if (providerCode === 'fal') return 'Fal/Veo';
    if (providerCode === 'video-long-form') return 'Video dài';
    return providerCode;
  }

  function renderOverviewProvider(item) {
    const reasons = item.blockingReasons || [];
    const label = readinessProviderLabel(item.providerCode);
    return `<article class="organization-provider-summary"><span class="provider-logo">${escapeHtml(String(label || '?').slice(0, 1).toUpperCase())}</span><div><strong>${escapeHtml(label)}</strong><small>${escapeHtml(item.modelCode || (item.providerCode === 'video-long-form' ? 'Chưa chọn policy' : 'Chưa có model'))}</small></div>${item.ready ? statusPill('Sẵn sàng', 'ready') : statusPill(`${reasons.length} điều kiện thiếu`, 'blocked')}</article>`;
  }

  async function loadMembers(force = false, page = organizationState.membersPaging.page, pageSize = organizationState.membersPaging.pageSize) {
    return preservePagePosition(async () => {
      const root = byId('organizationTabContent');
      if (organizationState.members && !force && organizationState.membersLoadedSearch === organizationState.memberSearch && organizationState.membersPaging.page === page && organizationState.membersPaging.pageSize === pageSize) return renderMembers();
      root.innerHTML = loading('Đang tải thành viên...');
      const version = organizationState.version;
      try {
        const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
        if (organizationState.memberSearch.trim()) params.set('search', organizationState.memberSearch.trim());
        const data = await request('members', `/api/organizations/${organizationState.selectedOrganizationId}/members/page?${params}`);
        if (version !== organizationState.version) return;
        organizationState.members = data.items || [];
        organizationState.membersPaging = data;
        organizationState.membersLoadedSearch = organizationState.memberSearch;
        renderMembers();
      } catch (error) {
        if (error.name !== 'AbortError') root.innerHTML = errorState(error, 'members');
      }
    });
  }

  function renderMembers() {
    const root = byId('organizationTabContent');
    const canManage = capabilities().members;
    const actorRole = selectedOrganization()?.role;
    const members = organizationState.members || [];
    root.innerHTML = `<div class="organization-tab-toolbar"><div class="search-form"><div>${icon('search')}<input id="organizationMemberSearch" type="search" value="${escapeHtml(organizationState.memberSearch)}" placeholder="Tìm email, tên, vai trò..." aria-label="Tìm thành viên" /></div></div>${canManage ? `<button type="button" class="primary-button" data-add-organization-member>${icon('plus')}<span>Thêm thành viên</span></button>` : ''}</div>${members.length ? `<div class="table-scroll"><table class="data-table"><thead><tr><th>Thành viên</th><th>Vai trò</th><th>Trạng thái</th><th>Hạn mức tháng</th><th>Ngày tham gia</th><th></th></tr></thead><tbody>${members.map(member => {
      const canEdit = canManage && (member.role !== 'Owner' || actorRole === 'Owner');
      return `<tr><td><strong>${escapeHtml(member.displayName || 'Chưa đặt tên')}</strong><br><small>${escapeHtml(member.email)} · ${member.isProvisioningManaged ? 'Tự động từ gói' : 'Quản lý thủ công'}</small></td><td>${escapeHtml(member.role)}</td><td>${statusPill(member.status, member.status === 'Active' ? 'ready' : 'blocked')}</td><td>${member.monthlyBudgetLimit === null ? 'Không đặt' : escapeHtml(formatMoney(member.monthlyBudgetLimit))}</td><td>${escapeHtml(formatDate(member.joinedAtUtc))}</td><td>${canEdit ? `<button type="button" class="ghost-button" data-edit-organization-member="${escapeHtml(member.userId)}">Cập nhật</button>` : ''}</td></tr>`;
    }).join('')}</tbody></table></div>` : '<div class="empty-state">Không tìm thấy thành viên phù hợp.</div>'}`;
    if (members.length) root.insertAdjacentHTML('beforeend', paginationMarkup(organizationState.membersPaging, 'members', 'thành viên'));
    byId('organizationMemberSearch')?.addEventListener('input', event => {
      organizationState.memberSearch = event.target.value;
      window.clearTimeout(organizationState.memberSearchTimer);
      organizationState.memberSearchTimer = window.setTimeout(() => {
        loadMembers(false, 1, organizationState.membersPaging.pageSize).catch(error => toast(friendlyError(error), true));
      }, 250);
    });
  }

  async function loadUsage(force = false, page = organizationState.usagePaging.page, pageSize = organizationState.usagePaging.pageSize) {
    return preservePagePosition(async () => {
      const root = byId('organizationTabContent');
      if (organizationState.usage && !force && organizationState.usagePaging.page === page && organizationState.usagePaging.pageSize === pageSize) return renderUsage();
      root.innerHTML = loading('Đang tải budget và usage...');
      const version = organizationState.version;
      try {
        const [usage, members] = await Promise.all([
          request('usage', (() => {
            const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
            if (organizationState.usageFilters.provider) params.set('provider', organizationState.usageFilters.provider);
            if (organizationState.usageFilters.model) params.set('model', organizationState.usageFilters.model);
            if (organizationState.usageFilters.kind) params.set('kind', organizationState.usageFilters.kind);
            return `/api/organizations/${organizationState.selectedOrganizationId}/usage/page?${params}`;
          })()),
          organizationState.memberDirectory ? Promise.resolve(organizationState.memberDirectory) : request('usage-members', `/api/organizations/${organizationState.selectedOrganizationId}/members`)
        ]);
        if (version !== organizationState.version) return;
        organizationState.usage = usage;
        organizationState.usagePaging = { page: usage.itemsPage || page, pageSize: usage.itemsPageSize || pageSize, totalCount: usage.itemsTotalCount || 0, totalPages: usage.itemsTotalPages || 0, hasPrevious: (usage.itemsPage || page) > 1, hasNext: (usage.itemsPage || page) < (usage.itemsTotalPages || 0) };
        organizationState.memberDirectory = members;
        renderUsage();
      } catch (error) {
        if (error.name !== 'AbortError') root.innerHTML = errorState(error, 'usage');
      }
    });
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
    const filtered = data.items;
    const memberMap = new Map((organizationState.memberDirectory || organizationState.members || []).map(member => [member.userId, member.displayName || member.email]));
    root.innerHTML = `
      <div class="organization-metrics usage-metrics"><article><span>Budget tháng</span><strong>${escapeHtml(formatMoney(data.budgetLimit, data.currencyCode))}</strong><small>${escapeHtml(formatDate(data.periodStartsAtUtc))} → ${escapeHtml(formatDate(data.periodEndsAtUtc))}</small></article><article><span>Actual cost</span><strong>${escapeHtml(formatMoney(data.actualCost, data.currencyCode))}</strong><small>Đã quyết toán</small></article><article><span>Reserved cost</span><strong>${escapeHtml(formatMoney(data.reservedCost, data.currencyCode))}</strong><small>Đang giữ</small></article><article><span>Remaining</span><strong>${escapeHtml(formatMoney(data.remainingBudget, data.currencyCode))}</strong><small>Còn có thể reserve</small></article><article><span>Input token</span><strong>${escapeHtml(formatMetric(data.inputTokens))}</strong><small>Actual trong kỳ</small></article><article><span>Output token</span><strong>${escapeHtml(formatMetric(data.outputTokens))}</strong><small>Actual trong kỳ</small></article><article><span>Video</span><strong>${escapeHtml(formatMetric(data.videoSeconds, ' giây'))}</strong><small>Actual trong kỳ</small></article></div>
      <div class="budget-progress"><div><span>Đã dùng + đang giữ</span><strong>${escapeHtml(percent.toFixed(1))}%</strong></div><span class="budget-progress-track"><span class="${escapeHtml(progressStatus)}" style="width:${escapeHtml(percent)}%"></span></span></div>
      ${limit === 0 ? '<div class="organization-alert danger"><strong>AI đang bị khóa</strong><span>Budget 0 là trạng thái khóa, không phải không giới hạn.</span></div>' : ''}
      ${capabilities().billing ? `<form id="organizationBudgetForm" class="inline-budget-form"><label>Budget tháng mới (USD)<input id="organizationBudgetLimit" type="number" min="0" max="100000000" step="0.000001" value="${escapeHtml(data.budgetLimit)}" required /></label><button type="submit" class="primary-button">Cập nhật budget</button><small>Budget là hạn mức nội bộ; 0 sẽ khóa AI.</small><p class="form-message"></p></form>` : ''}
      <section class="usage-groups"><div class="section-heading"><div><span class="eyebrow">USAGE BREAKDOWN</span><h3>Theo provider, model và thành viên</h3></div></div>${data.groups?.length ? `<div class="table-scroll"><table class="data-table"><thead><tr><th>Provider / model</th><th>Thành viên</th><th>Chi phí</th><th>Input</th><th>Output</th><th>Video</th></tr></thead><tbody>${data.groups.map(group => `<tr><td><strong>${escapeHtml(group.providerCode)}</strong><br><small>${escapeHtml(group.modelCode)}</small></td><td>${escapeHtml(memberMap.get(group.userId) || group.userId)}</td><td>${escapeHtml(formatMoney(group.actualCost, data.currencyCode))}</td><td>${escapeHtml(formatMetric(group.inputTokens))}</td><td>${escapeHtml(formatMetric(group.outputTokens))}</td><td>${escapeHtml(formatMetric(group.videoSeconds, ' giây'))}</td></tr>`).join('')}</tbody></table></div>` : '<div class="empty-state">Chưa có usage Actual trong kỳ.</div>'}</section>
      <section class="usage-ledger"><div class="section-heading log-heading"><div><span class="eyebrow">LEDGER</span><h3>Đối soát reservation và actual</h3></div><div class="filters"><select data-usage-filter="provider"><option value="">Mọi provider</option>${providers.map(value => `<option ${value === organizationState.usageFilters.provider ? 'selected' : ''}>${escapeHtml(value)}</option>`).join('')}</select><select data-usage-filter="model"><option value="">Mọi model</option>${models.map(value => `<option ${value === organizationState.usageFilters.model ? 'selected' : ''}>${escapeHtml(value)}</option>`).join('')}</select><select data-usage-filter="kind"><option value="">Mọi loại</option>${kinds.map(value => `<option ${value === organizationState.usageFilters.kind ? 'selected' : ''}>${escapeHtml(value)}</option>`).join('')}</select></div></div>${filtered.length ? `<div class="table-scroll"><table class="data-table"><thead><tr><th>Thời gian</th><th>Loại</th><th>Provider / model</th><th>Project</th><th>Thành viên</th><th>Số tiền</th></tr></thead><tbody>${filtered.map(item => `<tr><td>${escapeHtml(formatDate(item.occurredAtUtc))}</td><td>${statusPill(item.entryKind, item.entryKind === 'Actual' ? 'ready' : item.entryKind === 'Reservation' ? 'warning' : 'blocked')}</td><td><strong>${escapeHtml(item.providerCode)}</strong><br><small>${escapeHtml(item.modelCode)}</small></td><td><code>${escapeHtml(item.projectId)}</code></td><td>${escapeHtml(memberMap.get(item.userId) || item.userId)}</td><td>${escapeHtml(formatMoney(item.amount, item.currencyCode))}</td></tr>`).join('')}</tbody></table></div>` : '<div class="empty-state">Không có ledger phù hợp bộ lọc.</div>'}</section>`;
    document.querySelectorAll('[data-usage-filter]').forEach(select => select.addEventListener('change', event => {
      organizationState.usageFilters[event.target.dataset.usageFilter] = event.target.value;
      loadUsage(true, 1, organizationState.usagePaging.pageSize).catch(error => toast(friendlyError(error), true));
    }));
    if (filtered.length) root.insertAdjacentHTML('beforeend', paginationMarkup(organizationState.usagePaging, 'usage', 'usage'));
    byId('organizationBudgetForm')?.addEventListener('submit', submitBudget);
  }

  async function loadProviders(force = false) {
    return preservePagePosition(async () => {
      const root = byId('organizationTabContent');
      if (organizationState.providers && organizationState.pricing && organizationState.videoPolicy !== undefined && organizationState.longFormVideoPolicy !== undefined && !force) return renderProviders();
      root.innerHTML = loading('Đang tải trạng thái credential và rate...');
      const version = organizationState.version;
      try {
        const [providers, pricing, videoPolicy, longFormVideoPolicy] = await Promise.all([
          request('providers', `/api/organizations/${organizationState.selectedOrganizationId}/providers`),
          organizationState.pricing ? Promise.resolve(organizationState.pricing) : request('pricing-detail', '/api/admin/ai-pricing'),
          request('video-policy', `/api/organizations/${organizationState.selectedOrganizationId}/video-policy`),
          request('long-form-video-policy', `/api/organizations/${organizationState.selectedOrganizationId}/video-policy?scope=LongForm`)
        ]);
        if (version !== organizationState.version) return;
        organizationState.providers = providers;
        organizationState.pricing = pricing;
        organizationState.videoPolicy = videoPolicy;
        organizationState.longFormVideoPolicy = longFormVideoPolicy;
        renderProviders();
      } catch (error) {
        if (error.name !== 'AbortError') root.innerHTML = errorState(error, 'providers');
      }
    });
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

  function isFalVeoRate(rate, model) {
    const metadata = rateMetadata(rate);
    return rate?.usageType === 'VideoSecond' &&
      metadata.resolution?.toLowerCase() === '720p' &&
      metadata.nativeAudio === true &&
      metadata.endpointId === model?.modelCode;
  }

  function configuredRates(providerCode, model) {
    const rates = activeRates(model);
    return providerCode === 'kling'
      ? rates.filter(isKlingNativeAudioRate)
      : providerCode === 'fal'
        ? rates.filter(rate => isFalVeoRate(rate, model))
        : rates;
  }

  function rateVariantLabel(providerCode, rate) {
    return providerCode === 'kling' && isKlingNativeAudioRate(rate)
      ? ' · 720p · Native Audio'
      : providerCode === 'fal'
        ? ' · 720p · Native Audio · 4/6/8s'
        : '';
  }

  function providerReadiness(catalogProvider, providerStatus, requiredForLongForm) {
    const policyModelId = organizationState.longFormVideoPolicy?.providerId === catalogProvider.providerId
      ? organizationState.longFormVideoPolicy.providerModelId
      : null;
    const eligibleModels = (catalogProvider.models || []).filter(model =>
      catalogProvider.providerCode === 'openai' ? model.modality === 'Text' : model.modality === 'Video');
    const model = eligibleModels.find(item => item.providerModelId === policyModelId) ||
      [...eligibleModels].sort((a, b) => Number(b.isEnabled && b.isDefault) - Number(a.isEnabled && a.isDefault) || Number(b.isEnabled) - Number(a.isEnabled))[0];
    const required = !requiredForLongForm
      ? []
      : catalogProvider.providerCode === 'openai'
      ? ['InputToken', 'OutputToken']
      : catalogProvider.providerCode === 'kling'
        ? ['VideoSecond']
        : catalogProvider.providerCode === 'byteplus'
          ? ['OutputToken']
          : catalogProvider.providerCode === 'fal'
            ? ['VideoSecond']
          : [];
    const configured = new Set(configuredRates(catalogProvider.providerCode, model).map(rate => rate.usageType));
    const missing = required.filter(value => !configured.has(value));
    const reasons = [];
    if (!requiredForLongForm) return { model, required, missing, reasons, ready: false, requiredForLongForm };
    if (Number(selectedOrganization()?.monthlyBudgetLimit) <= 0) reasons.push('budget_disabled');
    if (!catalogProvider.isEnabled) {
      reasons.push('provider_disabled');
      return { model, required, missing, reasons, ready: false, requiredForLongForm };
    }
    if (!model?.isEnabled) reasons.push('model_disabled');
    if (!providerStatus?.configured || providerStatus.credentialStatus !== 'Active') reasons.push('credential_missing');
    if (missing.length) reasons.push('pricing_not_configured');
    return { model, required, missing, reasons, ready: reasons.length === 0, requiredForLongForm };
  }

  function renderProviders() {
    const root = byId('organizationTabContent');
    const providerStatuses = new Map((organizationState.providers || []).map(item => [item.providerCode, item]));
    const catalog = (organizationState.pricing || []).filter(item => ['openai', 'kling', 'byteplus', 'fal'].includes(item.providerCode));
    const allVideoModels = catalog.flatMap(provider => provider.isEnabled
      ? (provider.models || []).filter(model => model.isEnabled && model.modality === 'Video').map(model => ({ provider, model }))
      : []);
    const renderPolicyPanel = (scope, title, currentPolicy, models) =>
      `<section class="provider-admin-card video-policy-card"><div class="provider-admin-heading"><span class="provider-logo">V</span><div><h3>${escapeHtml(title)}</h3><p>${scope === 'LongForm' ? 'Chọn provider tạo clip cho dự án Video dài. Fal/Veo chỉ được dùng tại đây.' : 'Áp dụng cho Video ngắn và workflow không phải Video dài.'}</p></div>${currentPolicy?.isActive ? statusPill(`Đang dùng · v${currentPolicy.policyVersion}`, 'ready') : statusPill('Chưa cấu hình', 'blocked')}</div>${currentPolicy ? `<dl><div><dt>Nhà cung cấp</dt><dd>${escapeHtml(currentPolicy.providerName)}</dd></div><div><dt>Model</dt><dd>${escapeHtml(currentPolicy.modelCode)}</dd></div><div><dt>Biến thể</dt><dd>${escapeHtml(currentPolicy.resolution)} · ${currentPolicy.nativeAudio ? 'Âm thanh trực tiếp' : 'Không có âm thanh trực tiếp'}</dd></div><div><dt>Phạm vi</dt><dd>${scope === 'LongForm' ? 'Video dài' : 'Mặc định / Video ngắn'}</dd></div><div><dt>Cập nhật</dt><dd>${escapeHtml(formatDate(currentPolicy.updatedAtUtc))}</dd></div></dl>` : '<p class="provider-ready-note">Chọn model đã được bật và có khóa API đang hoạt động.</p>'}${capabilities().credentials ? `<form class="inline-budget-form organization-video-policy-form" data-policy-scope="${scope}"><label>Provider và model video<select class="organization-video-policy-model" required ${models.length ? '' : 'disabled'}><option value="">Chọn provider / model</option>${models.map(({ provider, model }) => `<option value="${escapeHtml(model.providerModelId)}" ${currentPolicy?.providerModelId === model.providerModelId ? 'selected' : ''}>${escapeHtml(provider.displayName)} · ${escapeHtml(model.displayName)}</option>`).join('')}</select></label><button type="submit" class="primary-button" ${models.length ? '' : 'disabled'}>Áp dụng</button><small>${scope === 'LongForm' ? 'Veo: 720p · âm thanh trực tiếp · 4/6/8 giây · 16:9/9:16. ' : ''}Dự án cũ giữ nguyên model đã chọn trước đó.</small><p class="form-message"></p></form>` : '<p class="form-hint">Chỉ Owner hoặc Quản trị viên tổ chức được đổi policy video.</p>'}</section>`;
    const defaultModels = allVideoModels.filter(({ provider }) => provider.providerCode !== 'fal');
    const renderProviderCard = provider => {
      const status = providerStatuses.get(provider.providerCode);
      const requiredForLongForm = provider.providerCode === 'openai' ||
        organizationState.longFormVideoPolicy?.providerId === provider.providerId;
      const readiness = providerReadiness(provider, status, requiredForLongForm);
      const credentialControl = !capabilities().credentials
        ? '<p class="form-hint">Vai trò hiện tại chỉ được xem trạng thái.</p>'
        : provider.isEnabled
          ? `<button type="button" class="primary-button" data-configure-provider="${escapeHtml(provider.providerCode)}" data-provider-name="${escapeHtml(provider.displayName)}">${status?.configured ? 'Thay credential' : 'Cấu hình credential'}</button>`
          : `<button type="button" class="ghost-button" data-provider-unavailable="${escapeHtml(provider.providerCode)}" data-provider-name="${escapeHtml(provider.displayName)}">Xem cách kích hoạt</button>`;
      const readinessStatus = !readiness.requiredForLongForm
        ? statusPill('Không được chọn', 'warning')
        : readiness.ready
          ? statusPill('Sẵn sàng', 'ready')
          : statusPill('Chưa sẵn sàng', 'blocked');
      const readinessDetail = !readiness.requiredForLongForm
        ? '<p class="provider-ready-note">Không thuộc Policy Video dài hiện tại nên không phải điều kiện chặn.</p>'
        : readiness.reasons.length
          ? `<ul class="provider-blockers">${readiness.reasons.map(reason => `<li><span>${escapeHtml(readinessMessages[reason] || reason)}${reason === 'pricing_not_configured' ? ` Thiếu: ${escapeHtml(readiness.missing.join(', '))}.` : ''}</span>${renderReadinessAction(reason, provider.providerCode)}</li>`).join('')}</ul>`
          : '<p class="provider-ready-note">Đủ budget, credential Active, model và rate bắt buộc.</p>';
      const credentialLabel = !provider.isEnabled ? 'Provider đang tắt' : status?.credentialStatus === 'Active' ? 'Đang hoạt động' : status?.configured ? 'Cần kiểm tra lại' : 'Chưa cấu hình';
      return `<article class="provider-admin-card"><div class="provider-admin-heading"><span class="provider-logo">${escapeHtml(provider.displayName.slice(0, 1).toUpperCase())}</span><div><h3>${escapeHtml(provider.displayName)}</h3><p>${escapeHtml(readiness.model?.modelCode || 'Chưa có model')}</p></div>${readinessStatus}</div><dl><div><dt>Khóa API</dt><dd>${status?.configured ? `${escapeHtml(status.secretHint || '••••')} · phiên bản ${escapeHtml(status.credentialVersion)}` : 'Chưa cấu hình'}</dd></div><div><dt>Trạng thái</dt><dd>${escapeHtml(credentialLabel)}</dd></div><div><dt>Cập nhật</dt><dd>${escapeHtml(formatDate(status?.updatedAtUtc))}</dd></div><div><dt>Đơn giá bắt buộc</dt><dd>${escapeHtml(readiness.required.join(', ') || 'Không áp dụng')}</dd></div></dl>${readinessDetail}${credentialControl}</article>`;
    };
    const selectedVideoProviderId = organizationState.longFormVideoPolicy?.providerId;
    const requiredCatalog = catalog.filter(provider => provider.providerCode === 'openai' || provider.providerId === selectedVideoProviderId || (!selectedVideoProviderId && provider.providerCode === 'fal'));
    const otherCatalog = catalog.filter(provider => !requiredCatalog.includes(provider));
    const policyPanel = `<div class="policy-focus-layout">${renderPolicyPanel('LongForm', 'Policy Video dài', organizationState.longFormVideoPolicy, allVideoModels)}<details class="admin-disclosure"><summary>Thiết lập nâng cao cho Video ngắn</summary><div class="disclosure-body">${renderPolicyPanel('Default', 'Policy mặc định / Video ngắn', organizationState.videoPolicy, defaultModels)}</div></details></div>`;
    root.innerHTML = `${policyPanel}<section class="provider-focus-section"><div class="section-heading"><div><span class="eyebrow">PROVIDER CẦN THIẾT</span><h3>OpenAI và provider Video dài</h3></div></div><div class="provider-admin-grid">${requiredCatalog.map(renderProviderCard).join('')}</div></section>${otherCatalog.length ? `<details class="admin-disclosure provider-others"><summary>Provider khác — không ảnh hưởng khả năng chạy Video dài hiện tại</summary><div class="disclosure-body provider-admin-grid">${otherCatalog.map(renderProviderCard).join('')}</div></details>` : ''}${!catalog.length ? '<div class="empty-state">Chưa có catalog provider AI.</div>' : ''}`;
    document.querySelectorAll('.organization-video-policy-form').forEach(form => form.addEventListener('submit', submitVideoPolicy));
  }

  async function submitVideoPolicy(event) {
    event.preventDefault();
    const form = event.currentTarget;
    const button = form.querySelector('button[type="submit"]');
    const providerModelId = form.querySelector('.organization-video-policy-model').value;
    const scope = form.dataset.policyScope || 'Default';
    if (!providerModelId) return;
    const selected = (organizationState.pricing || []).flatMap(provider =>
      (provider.models || []).map(model => ({ provider, model })))
      .find(item => item.model.providerModelId === providerModelId);
    const credential = selected && (organizationState.providers || []).find(item => item.providerCode === selected.provider.providerCode);
    if (!selected || !credential?.configured || credential.credentialStatus !== 'Active') {
      form.querySelector('.form-message').textContent = `Chưa thể lưu policy. Hãy cấu hình và kiểm tra credential Active của ${selected?.provider.displayName || 'provider'} trước.`;
      return;
    }
    setBusy(button, true, 'Đang lưu...');
    try {
      const savedPolicy = await api(`/api/organizations/${organizationState.selectedOrganizationId}/video-policy`, {
        method: 'PUT',
        body: JSON.stringify({ providerModelId, resolution: '720p', nativeAudio: true, scope })
      });
      if (scope === 'LongForm') organizationState.longFormVideoPolicy = savedPolicy;
      else organizationState.videoPolicy = savedPolicy;
      toast('Đã cập nhật policy video. Dự án mới sẽ dùng policy này; dự án cũ giữ nguyên model.');
      organizationState.organizations = null;
      organizationState.setupOrganizations = null;
      organizationState.directoryLoaded = false;
      await loadOrganizations(true);
      renderOrganizationHeading();
      await loadProviders(true);
    } catch (error) {
      form.querySelector('.form-message').textContent = friendlyError(error);
    } finally {
      setBusy(button, false);
    }
  }

  async function loadAudit(force = false, page = organizationState.auditPaging.page, pageSize = organizationState.auditPaging.pageSize) {
    return preservePagePosition(async () => {
      const root = byId('organizationTabContent');
      if (!capabilities().audit) return renderOrganizationOverview();
      if (organizationState.audit && !force && organizationState.auditPaging.page === page && organizationState.auditPaging.pageSize === pageSize) return renderAudit();
      root.innerHTML = loading('Đang tải nhật ký tổ chức...');
      const version = organizationState.version;
      try {
        const data = await request('audit', `/api/organizations/${organizationState.selectedOrganizationId}/audit/page?page=${page}&pageSize=${pageSize}`);
        if (version !== organizationState.version) return;
        organizationState.audit = data.items || [];
        organizationState.auditPaging = data;
        renderAudit();
      } catch (error) {
        if (error.name !== 'AbortError') root.innerHTML = errorState(error, 'audit');
      }
    });
  }

  function renderAudit() {
    const data = organizationState.audit || [];
    byId('organizationTabContent').innerHTML = data.length ? `<div class="table-scroll"><table class="data-table audit-table"><thead><tr><th>Thời gian</th><th>Sự kiện</th><th>Người thao tác</th><th>Dữ liệu an toàn</th><th>Correlation ID</th></tr></thead><tbody>${data.map(item => `<tr><td>${escapeHtml(formatDate(item.occurredAtUtc))}</td><td><strong>${escapeHtml(auditLabels[item.eventType] || item.eventType)}</strong></td><td>${escapeHtml(item.actorDisplayName || item.actorEmail || item.actorUserId || 'Hệ thống')}</td><td><div class="audit-data">${Object.entries(item.data || {}).map(([key, value]) => `<span><b>${escapeHtml(key)}:</b> ${escapeHtml(value ?? 'null')}</span>`).join('') || '—'}</div></td><td><code>${escapeHtml(item.correlationId || '—')}</code></td></tr>`).join('')}</tbody></table></div>` : '<div class="empty-state">Chưa có sự kiện tổ chức.</div>';
    if (data.length) byId('organizationTabContent').insertAdjacentHTML('beforeend', paginationMarkup(organizationState.auditPaging, 'audit', 'nhật ký'));
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
    return preservePagePosition(async () => {
      const root = byId('costGuideCurrentRates');
      if (organizationState.pricing && !force) return renderCostGuide();
      root.innerHTML = loading('Đang đọc rate hiện hành...');
      try {
        organizationState.pricing = await request('cost-guide-pricing', '/api/admin/ai-pricing');
        renderCostGuide();
      } catch (error) {
        if (error.name !== 'AbortError') root.innerHTML = errorState(error, 'cost-guide');
      }
    });
  }

  function renderCostGuide() {
    const root = byId('costGuideCurrentRates');
    const providers = organizationState.pricing || [];
    const openAi = providers.find(provider => provider.providerCode === 'openai');
    const kling = providers.find(provider => provider.providerCode === 'kling');
    const fal = providers.find(provider => provider.providerCode === 'fal');
    const openAiModel = preferredGuideModel(openAi);
    const klingModel = preferredGuideModel(kling);
    const falModels = (fal?.models || []).filter(model => model.modality === 'Video');
    const inputRate = guideRate(openAiModel, 'InputToken');
    const outputRate = guideRate(openAiModel, 'OutputToken');
    const videoRate = guideRate(klingModel, 'VideoSecond', 'kling');
    const falRates = falModels.map(model => ({ model, rate: guideRate(model, 'VideoSecond', 'fal') }));
    const inputExample = tokenCostForGuide(inputRate, 10_000);
    const outputExample = tokenCostForGuide(outputRate, 2_000);
    const openAiExample = inputExample === null || outputExample === null ? null : inputExample + outputExample;
    const klingExample = videoRate?.unit === 'Second' ? Number(videoRate.unitPrice) * 10 : null;
    const falExamples = falRates.map(({ model, rate }) => ({ model, rate, cost: rate?.unit === 'Second' ? Number(rate.unitPrice) * 8 : null }));
    const rows = [
      renderGuideRateRow(openAi, openAiModel, 'InputToken', inputRate),
      renderGuideRateRow(openAi, openAiModel, 'OutputToken', outputRate),
      renderGuideRateRow(kling, klingModel, 'VideoSecond', videoRate),
      ...falRates.map(({ model, rate }) => renderGuideRateRow(fal, model, 'VideoSecond', rate))
    ].join('');
    root.innerHTML = `
      <div class="cost-guide-live-grid">
        <article><span>OPENAI · VÍ DỤ</span><h4>10.000 input + 2.000 output token</h4><strong>${openAiExample === null ? 'Chưa tính được' : escapeHtml(formatMoney(openAiExample, inputRate?.currencyCode || outputRate?.currencyCode || 'USD'))}</strong><small>${openAiExample === null ? 'Hãy cấu hình đủ InputToken và OutputToken.' : 'Tính bằng rate Active bên dưới.'}</small></article>
        <article><span>KLING · VÍ DỤ</span><h4>Video 720p · Native Audio · 10 giây</h4><strong>${klingExample === null ? 'Chưa tính được' : escapeHtml(formatMoney(klingExample, videoRate?.currencyCode || 'USD'))}</strong><small>${klingExample === null ? 'Hãy cấu hình đúng rate VideoSecond cho 720p Native Audio.' : 'Tính theo rate USD/giây của đúng biến thể 720p Native Audio.'}</small></article>
        ${falExamples.map(({ model, rate, cost }) => `<article><span>FAL / VEO · VÍ DỤ</span><h4>${escapeHtml(model.displayName)} · 8 giây</h4><strong>${cost === null ? 'Chưa tính được' : escapeHtml(formatMoney(cost, rate?.currencyCode || 'USD'))}</strong><small>Rate riêng theo endpoint · 720p · Native Audio.</small></article>`).join('')}
      </div>
      <div class="table-scroll"><table class="data-table cost-guide-table"><thead><tr><th>Provider</th><th>Model</th><th>Usage type</th><th>Đơn vị</th><th>Đơn giá Active</th><th>Hiệu lực từ</th></tr></thead><tbody>${rows}</tbody></table></div>`;
  }

  async function loadPricing(force = false) {
    return preservePagePosition(async () => {
      const root = byId('aiPricingCatalog');
      if (organizationState.pricing && !force) return renderPricing();
      root.innerHTML = loading('Đang tải bảng giá AI...');
      try {
        organizationState.pricing = await request('pricing', '/api/admin/ai-pricing');
        renderPricing();
      } catch (error) {
        if (error.name !== 'AbortError') root.innerHTML = errorState(error, 'pricing');
      }
    });
  }

  function requiredRates(providerCode) {
    return providerCode === 'openai'
      ? ['InputToken', 'OutputToken']
      : providerCode === 'kling'
        ? ['VideoSecond']
        : providerCode === 'byteplus'
          ? ['OutputToken']
          : providerCode === 'fal'
            ? ['VideoSecond']
          : [];
  }

  function renderPricing() {
    const root = byId('aiPricingCatalog');
    const priority = { openai: 0, fal: 1, kling: 2, byteplus: 3 };
    const providers = [...(organizationState.pricing || [])].sort((a, b) => (priority[a.providerCode] ?? 99) - (priority[b.providerCode] ?? 99));
    const usageLabel = value => value === 'InputToken' ? 'Token đầu vào' : value === 'OutputToken' ? 'Token đầu ra' : value === 'VideoSecond' ? 'Giây video' : value;
    const unitLabel = value => value === 'MillionTokens' ? '1 triệu token' : value === '1KTokens' ? '1.000 token' : value === 'Second' ? '1 giây' : value;
    const sections = providers.map(provider => {
      const expanded = organizationState.pricingExpandedProviders.has(provider.providerCode);
      const providerToggle = `<button type="button" class="${provider.isEnabled ? 'danger-button' : 'primary-button'}" data-toggle-ai-provider="${escapeHtml(provider.providerId)}" data-ai-provider-enabled="${provider.isEnabled ? 'false' : 'true'}">${provider.isEnabled ? 'Tắt provider' : 'Bật provider'}</button>`;
      const models = provider.models.map(model => {
        const rates = configuredRates(provider.providerCode, model);
        const configured = new Set(rates.map(rate => rate.usageType));
        const missing = requiredRates(provider.providerCode).filter(value => !configured.has(value));
        const stateControls = `<button type="button" class="${model.isEnabled ? 'danger-button' : 'ghost-button'}" data-toggle-ai-model="${escapeHtml(model.providerModelId)}" data-ai-model-enabled="${model.isEnabled ? 'false' : 'true'}">${model.isEnabled ? 'Tắt model' : 'Bật model'}</button>${model.isEnabled && !model.isDefault ? `<button type="button" class="ghost-button" data-default-ai-model="${escapeHtml(model.providerModelId)}">Đặt mặc định</button>` : ''}`;
        const variant = provider.providerCode === 'kling' ? ' · 720p · âm thanh trực tiếp' : provider.providerCode === 'fal' ? ' · 720p · âm thanh trực tiếp · đúng endpoint' : provider.providerCode === 'byteplus' ? ' · token video hoàn tất' : '';
        return `<article class="pricing-model"><div class="pricing-model-heading"><div><strong>${escapeHtml(model.displayName)}</strong><small>${escapeHtml(model.modelCode)} · ${escapeHtml(model.modality)} · ${model.isEnabled ? 'Đang bật' : 'Đang tắt'}${model.isDefault ? ' · Mặc định' : ''}</small></div><div class="inline-actions">${stateControls}<button type="button" class="primary-button" data-add-ai-rate="${escapeHtml(model.providerModelId)}" data-ai-rate-model="${escapeHtml(model.displayName)}" data-ai-rate-provider="${escapeHtml(provider.providerCode)}">${icon('plus')}<span>Thêm đơn giá</span></button></div></div>${missing.length ? `<div class="organization-alert warning"><strong>Thiếu đơn giá bắt buộc</strong><span>${escapeHtml(missing.map(usageLabel).join(', '))}${variant}</span></div>` : `<div class="organization-alert success"><strong>Đơn giá hợp lệ</strong><span>Model đã có đủ loại đơn giá bắt buộc đang hiệu lực.</span></div>`}${model.costRates.length ? `<div class="table-scroll"><table class="data-table"><thead><tr><th>Loại chi phí</th><th>Đơn vị tính</th><th>Đơn giá</th><th>Hiệu lực</th><th>Trạng thái</th><th></th></tr></thead><tbody>${model.costRates.map(rate => `<tr><td><strong>${escapeHtml(usageLabel(rate.usageType) + rateVariantLabel(provider.providerCode, rate))}</strong></td><td>${escapeHtml(unitLabel(rate.unit))}</td><td>${escapeHtml(formatMoney(rate.unitPrice, rate.currencyCode))}</td><td>${escapeHtml(formatDate(rate.effectiveFromUtc))}<br><small>đến ${escapeHtml(formatDate(rate.effectiveToUtc))}</small></td><td>${rate.isActive ? statusPill('Đang dùng', 'ready') : statusPill('Đã ngừng', 'warning')}</td><td>${rate.isActive ? `<button type="button" class="danger-button" data-deactivate-ai-rate="${escapeHtml(rate.costRateId)}">Ngừng sử dụng</button>` : ''}</td></tr>`).join('')}</tbody></table></div>` : '<div class="empty-state">Model chưa có đơn giá.</div>'}</article>`;
      }).join('');
      return `<section class="pricing-provider" data-pricing-provider="${escapeHtml(provider.providerCode)}"><div class="pricing-provider-heading"><button type="button" class="pricing-provider-summary" data-toggle-pricing-provider="${escapeHtml(provider.providerCode)}" aria-expanded="${expanded}"><span class="provider-logo">${escapeHtml(provider.displayName.slice(0, 1).toUpperCase())}</span><span><span class="eyebrow">${escapeHtml(provider.providerCode)}</span><strong>${escapeHtml(provider.displayName)}</strong><small>${provider.models.length} model · ${expanded ? 'Thu gọn' : 'Mở cấu hình'}</small></span></button><div class="inline-actions">${provider.isEnabled ? statusPill('Provider đang bật', 'ready') : statusPill('Provider đang tắt', 'blocked')}${providerToggle}</div></div><div class="pricing-model-list ${expanded ? '' : 'hidden'}">${models}</div></section>`;
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

  function allowedRateUsageTypes(providerCode) {
    if (providerCode === 'openai') return ['InputToken', 'OutputToken'];
    if (providerCode === 'byteplus') return ['OutputToken'];
    return ['VideoSecond'];
  }

  function syncRateUnit() {
    const video = byId('aiRateUsageType').value === 'VideoSecond';
    const unit = byId('aiRateUnit');
    if (video || unit.value === 'Second') unit.value = video ? 'Second' : 'MillionTokens';
    [...unit.options].forEach(option => option.disabled = video ? option.value !== 'Second' : option.value === 'Second');
    updateRatePreview();
  }

  function updateRatePreview() {
    const root = byId('aiRatePreview');
    const price = Number(byId('aiRatePrice').value);
    const usageType = byId('aiRateUsageType').value;
    const unit = byId('aiRateUnit').value;
    if (!Number.isFinite(price) || price <= 0) {
      root.innerHTML = '<span>Nhập đơn giá để xem ví dụ chi phí trước khi lưu.</span>';
      return;
    }
    const quantity = usageType === 'VideoSecond' ? 8 : usageType === 'InputToken' ? 10000 : 2000;
    const divisor = unit === 'MillionTokens' ? 1000000 : unit === '1KTokens' ? 1000 : 1;
    const cost = price * quantity / divisor;
    const label = usageType === 'VideoSecond' ? 'clip 8 giây' : usageType === 'InputToken' ? '10.000 input token' : '2.000 output token';
    root.innerHTML = `<span>Ví dụ ${escapeHtml(label)}</span><strong>${escapeHtml(formatMoney(cost, 'USD'))}</strong><small>Đây là phép tính minh họa từ đơn giá bạn vừa nhập.</small>`;
  }

  function openRateDialog(modelId, modelName, providerCode) {
    const form = byId('aiRateForm');
    form.reset();
    form.querySelector('.form-message').textContent = '';
    byId('aiRateModelId').value = modelId;
    byId('aiRateProviderCode').value = providerCode;
    byId('aiRateDialogTitle').textContent = `Tạo rate · ${modelName}`;
    byId('aiRateVariantHint').classList.toggle('hidden', providerCode !== 'kling' && providerCode !== 'fal');
    const allowedTypes = allowedRateUsageTypes(providerCode);
    const selectedModel = (organizationState.pricing || []).flatMap(provider => provider.models || []).find(model => model.providerModelId === modelId);
    const configured = new Set(configuredRates(providerCode, selectedModel).map(rate => rate.usageType));
    const initialType = allowedTypes.find(type => !configured.has(type)) || allowedTypes[0];
    [...byId('aiRateUsageType').options].forEach(option => option.disabled = !allowedTypes.includes(option.value));
    byId('aiRateUsageType').value = initialType;
    syncRateUnit();
    updateRatePreview();
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
      organizationState.memberDirectory = null;
      organizationState.membersLoadedSearch = '';
      organizationState.membersPaging = { page: 1, pageSize: organizationState.membersPaging.pageSize, totalCount: 0, totalPages: 0, hasPrevious: false, hasNext: false };
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
    const selectedModelId = byId('aiRateModelId').value;
    const selectedModel = (organizationState.pricing || []).flatMap(provider => provider.models || []).find(model => model.providerModelId === selectedModelId);
    const metadata = providerCode === 'kling'
      ? { source: source || null, resolution: '720p', nativeAudio: true }
      : providerCode === 'fal'
        ? { source: source || null, resolution: '720p', nativeAudio: true, endpointId: selectedModel?.modelCode || null, tier: selectedModel?.modelCode?.includes('/fast/') ? 'fast' : 'standard' }
      : source ? { source } : null;
    setBusy(button, true, 'Đang tạo...');
    try {
      await api(`/api/admin/ai-pricing/models/${byId('aiRateModelId').value}/rates`, { method: 'POST', body: JSON.stringify({ usageType: byId('aiRateUsageType').value, unit: byId('aiRateUnit').value, unitPrice: Number(byId('aiRatePrice').value), currencyCode: 'USD', effectiveFromUtc: null, metadataJson: metadata ? JSON.stringify(metadata) : null }) });
      byId('aiRateDialog').close();
      toast('Đã tạo rate mới và kết thúc rate Active cũ cùng loại.');
      organizationState.pricing = null;
      await loadPricing(true);
      organizationState.organizations = null;
      organizationState.setupOrganizations = null;
      organizationState.directoryLoaded = false;
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
      organizationState.setupOrganizations = null;
      organizationState.directoryLoaded = false;
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
      organizationState.setupOrganizations = null;
      organizationState.directoryLoaded = false;
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
      organizationState.setupOrganizations = null;
      organizationState.directoryLoaded = false;
      await loadPricing(true);
    } catch (error) {
      toast(friendlyError(error), true);
    }
  }

  async function refresh() {
    if (organizationState.scope === 'setup') return loadSetup(true).catch(() => {});
    if (organizationState.scope === 'pricing') return loadPricing(true).catch(() => {});
    if (organizationState.scope === 'cost-guide') return loadCostGuide(true).catch(() => {});
    if (organizationState.scope === 'pools') return loadPools(true).catch(() => {});
    const selectedId = organizationState.selectedOrganizationId;
    await loadOrganizations(true).catch(() => {});
    if (!selectedId) return;
    organizationState.selectedOrganizationId = selectedId;
    renderOrganizationHeading();
    if (organizationState.selectedTab === 'members') { organizationState.members = null; organizationState.membersPaging = { page: 1, pageSize: organizationState.membersPaging.pageSize, totalCount: 0, totalPages: 0, hasPrevious: false, hasNext: false }; }
    if (organizationState.selectedTab === 'usage') { organizationState.usage = null; organizationState.usagePaging = { page: 1, pageSize: organizationState.usagePaging.pageSize, totalCount: 0, totalPages: 0, hasPrevious: false, hasNext: false }; }
    if (organizationState.selectedTab === 'providers') organizationState.providers = null;
    if (organizationState.selectedTab === 'audit') { organizationState.audit = null; organizationState.auditPaging = { page: 1, pageSize: organizationState.auditPaging.pageSize, totalCount: 0, totalPages: 0, hasPrevious: false, hasNext: false }; }
    await selectTab(organizationState.selectedTab, true);
  }

  byId('addOrganizationButton').addEventListener('click', openCreateOrganization);
  byId('addOrganizationPoolButton').addEventListener('click', () => openPoolDialog());
  byId('manageLicensePlansButton').addEventListener('click', () => {
    shell.setSetupReturn(true);
    shell.navigate('plans', { keepSetupReturn: true });
  });
  byId('backToOrganizations').addEventListener('click', closeOrganization);
  byId('organizationDetailRefreshButton').addEventListener('click', () => byId('refreshButton').click());
  byId('organizationDetailSetupButton').addEventListener('click', () => {
    setTopbarVisible(true);
    shell.navigate('organizations', { organizationScope: 'setup', organizationMenuExpanded: true, keepSetupReturn: true });
  });
  const organizationScopeItems = [...document.querySelectorAll('[data-organization-scope]')];
  organizationScopeItems.forEach((button, index) => {
    button.addEventListener('click', () => shell.navigate('organizations', { organizationScope: button.dataset.organizationScope, organizationMenuExpanded: true }));
    button.addEventListener('keydown', event => {
      let targetIndex = null;
      if (event.key === 'ArrowDown' || event.key === 'ArrowRight') targetIndex = (index + 1) % organizationScopeItems.length;
      if (event.key === 'ArrowUp' || event.key === 'ArrowLeft') targetIndex = (index - 1 + organizationScopeItems.length) % organizationScopeItems.length;
      if (event.key === 'Home') targetIndex = 0;
      if (event.key === 'End') targetIndex = organizationScopeItems.length - 1;
      if (targetIndex === null) return;
      event.preventDefault();
      const target = organizationScopeItems[targetIndex];
      target.focus();
      shell.navigate('organizations', { organizationScope: target.dataset.organizationScope, organizationMenuExpanded: true });
    });
  });
  document.querySelectorAll('[data-organization-tab]').forEach(button => button.addEventListener('click', () => selectTab(button.dataset.organizationTab)));
  byId('organizationForm').addEventListener('submit', submitOrganization);
  byId('organizationMemberForm').addEventListener('submit', submitMember);
  byId('organizationPoolForm').addEventListener('submit', submitPool);
  byId('organizationPoolName').addEventListener('input', syncPoolCodeFromName);
  byId('organizationPoolOrganizationForm').addEventListener('submit', submitPoolOrganization);
  byId('organizationPoolPlanForm').addEventListener('submit', submitPoolPlan);
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
  byId('organizationSetup').addEventListener('click', event => {
    const next = event.target.closest('[data-setup-next]');
    if (next) runSetupAction(next.dataset.setupNext, next.dataset.setupId).catch(error => toast(friendlyError(error), true));
    const retry = event.target.closest('[data-organization-retry]');
    if (retry) loadSetup(true).catch(() => {});
  });
  byId('organizationPoolConsole').addEventListener('click', async event => {
    const poolPageButton = event.target.closest('[data-pagination="pools"] [data-page]');
    if (poolPageButton && !poolPageButton.disabled) {
      loadPools(false, Number(poolPageButton.dataset.page), organizationState.poolsPaging.pageSize).catch(error => toast(friendlyError(error), true));
      return;
    }
    if (event.target.closest('[data-close-pool]')) return closePool();
    const openSetup = event.target.closest('[data-open-pool-setup]');
    if (openSetup) return openPool(openSetup.dataset.openPoolSetup, true);
    const open = event.target.closest('[data-open-pool]');
    if (open) return openPool(open.dataset.openPool);
    if (event.target.closest('[data-show-pool-setup]')) {
      organizationState.poolSetupVisible = true;
      preservePagePosition(() => renderPools());
      return;
    }
    const edit = event.target.closest('[data-edit-pool]');
    if (edit) {
      const pool = organizationState.pools?.find(item => item.organizationPoolId === edit.dataset.editPool);
      organizationState.poolSetupVisible = true;
      if (pool) openPoolDialog(pool);
      return;
    }
    if (event.target.closest('[data-add-pool-organization]')) return openPoolOrganizationDialog();
    const editOrganization = event.target.closest('[data-edit-pool-organization]');
    if (editOrganization) return openPoolOrganizationDialog(editOrganization.dataset.editPoolOrganization);
    if (event.target.closest('[data-add-pool-plan]')) return openPoolPlanDialog().catch(error => toast(friendlyError(error), true));
    const editPlan = event.target.closest('[data-edit-pool-plan]');
    if (editPlan) return openPoolPlanDialog(editPlan.dataset.editPoolPlan).catch(error => toast(friendlyError(error), true));
    const checkOrganization = event.target.closest('[data-check-pool-organization]');
    if (checkOrganization) return checkPoolOrganizationReady(checkOrganization.dataset.checkPoolOrganization, checkOrganization);
    const removeOrganization = event.target.closest('[data-remove-pool-organization]');
    if (removeOrganization) {
      if (!confirm('Gỡ tổ chức khỏi pool? Chỉ thực hiện được khi không còn seat đang dùng hoặc giữ chỗ.')) return;
      try {
        await api(`/api/admin/organization-pools/${organizationState.selectedPoolId}/organizations/${removeOrganization.dataset.removePoolOrganization}`, { method: 'DELETE' });
      organizationState.pools = null;
      organizationState.setupPools = null;
        toast('Đã gỡ tổ chức khỏi pool.');
        await loadPools(true);
      } catch (error) { toast(friendlyError(error), true); }
      return;
    }
    const removePlan = event.target.closest('[data-remove-pool-plan]');
    if (removePlan) {
      if (!confirm('Gỡ ánh xạ gói khỏi pool?')) return;
      try {
        await api(`/api/admin/organization-pools/license-plans/${removePlan.dataset.removePoolPlan}`, { method: 'DELETE' });
        organizationState.pools = null;
        organizationState.setupPools = null;
        toast('Đã gỡ gói khỏi pool.');
        await loadPools(true);
      } catch (error) { toast(friendlyError(error), true); }
      return;
    }
    const retry = event.target.closest('[data-retry-assignment]');
    if (retry) {
      try {
        const result = await api(`/api/admin/organization-pools/assignments/${retry.dataset.retryAssignment}/retry`, { method: 'POST' });
        organizationState.pools = null;
        organizationState.setupPools = null;
        toast(result.message, result.paymentStatus !== 'Fulfilled');
        await loadPools(true);
      } catch (error) { toast(friendlyError(error), true); }
    }
  });
  byId('organizationPoolConsole').addEventListener('change', event => {
    const poolPageSize = event.target.closest('[data-pagination="pools"] [data-page-size]');
    if (poolPageSize) loadPools(false, 1, Number(poolPageSize.value)).catch(error => toast(friendlyError(error), true));
  });

  byId('organizationTable').addEventListener('click', event => {
    const pageButton = event.target.closest('[data-pagination="organizations"] [data-page]');
    if (pageButton && !pageButton.disabled) {
      loadOrganizations(false, Number(pageButton.dataset.page), organizationState.organizationsPaging.pageSize).catch(error => toast(friendlyError(error), true));
      return;
    }
    const open = event.target.closest('[data-open-organization]');
    if (open) openOrganization(open.dataset.openOrganization);
    const retry = event.target.closest('[data-organization-retry]');
    if (retry) loadOrganizations(true).catch(() => {});
  });
  byId('organizationTable').addEventListener('change', event => {
    const pageSize = event.target.closest('[data-pagination="organizations"] [data-page-size]');
    if (pageSize) loadOrganizations(false, 1, Number(pageSize.value)).catch(error => toast(friendlyError(error), true));
  });
  byId('organizationTabContent').addEventListener('click', event => {
    const memberPageButton = event.target.closest('[data-pagination="members"] [data-page]');
    if (memberPageButton && !memberPageButton.disabled) {
      loadMembers(false, Number(memberPageButton.dataset.page), organizationState.membersPaging.pageSize).catch(error => toast(friendlyError(error), true));
      return;
    }
    const auditPageButton = event.target.closest('[data-pagination="audit"] [data-page]');
    if (auditPageButton && !auditPageButton.disabled) {
      loadAudit(false, Number(auditPageButton.dataset.page), organizationState.auditPaging.pageSize).catch(error => toast(friendlyError(error), true));
      return;
    }
    const usagePageButton = event.target.closest('[data-pagination="usage"] [data-page]');
    if (usagePageButton && !usagePageButton.disabled) {
      loadUsage(false, Number(usagePageButton.dataset.page), organizationState.usagePaging.pageSize).catch(error => toast(friendlyError(error), true));
      return;
    }
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
  byId('organizationTabContent').addEventListener('change', event => {
    const memberPageSize = event.target.closest('[data-pagination="members"] [data-page-size]');
    if (memberPageSize) loadMembers(false, 1, Number(memberPageSize.value)).catch(error => toast(friendlyError(error), true));
    const auditPageSize = event.target.closest('[data-pagination="audit"] [data-page-size]');
    if (auditPageSize) loadAudit(false, 1, Number(auditPageSize.value)).catch(error => toast(friendlyError(error), true));
    const usagePageSize = event.target.closest('[data-pagination="usage"] [data-page-size]');
    if (usagePageSize) loadUsage(false, 1, Number(usagePageSize.value)).catch(error => toast(friendlyError(error), true));
  });
  byId('aiPricingCatalog').addEventListener('click', event => {
    const pricingToggle = event.target.closest('[data-toggle-pricing-provider]');
    if (pricingToggle) {
      preservePagePosition(() => {
        const providerCode = pricingToggle.dataset.togglePricingProvider;
        if (organizationState.pricingExpandedProviders.has(providerCode)) organizationState.pricingExpandedProviders.delete(providerCode);
        else organizationState.pricingExpandedProviders.add(providerCode);
        renderPricing();
      });
      return;
    }
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
    activate: (scope) => showScope(scope || organizationState.scope),
    refresh,
    openCreateOrganization,
    showSetup: () => showScope('setup')
  });
})();
