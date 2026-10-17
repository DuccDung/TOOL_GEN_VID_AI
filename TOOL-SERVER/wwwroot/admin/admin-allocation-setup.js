(() => {
  'use strict';
  const shell = window.videoMakerAdminShell;
  const root = document.getElementById('organizationPoolConsole');
  if (!shell || !root) return;
  const { api, escapeHtml: esc, icon, toast } = shell;
  const endpoint = '/api/admin/organization-pools';
  let context, generation = 0, controller, timer, step = 1, auxiliary, loading = false, busy = false;
  let loadError = '', error = '', draft = null, selectedOrganizationId = '', chosen = new Set();
  let returnVisible = false, operation = 0, memberBudget = '';
  let profile = 'all-tools', explicitProfile = false;
  const isLocal = () => profile === 'vietsub-local';
  const isVietsub = () => profile !== 'all-tools';
  const productCheck = org => profile === 'vietsub-cloud' ? org.vietsubCloud : org.vietsubLocal;
  const planProfile = plan => {
    let flags; try { flags = JSON.parse(plan.featureFlagsJson?.trim() || '{}'); } catch { return 'invalid'; }
    const scope = plan.productScope ?? flags?.productScope ?? 'all-tools';
    if (scope !== 'vietsub') return scope;
    return (plan.vietsubCloudEnabled ?? flags?.vietsubCloudEnabled) === true ? 'vietsub-cloud' : 'vietsub-local';
  };
  const profileFields = () => isVietsub() ? { productScope: 'vietsub', vietsubCloudEnabled: !isLocal() } : {};
  const profilePicker = () => `<label class="allocation-panel" for="allocationProductProfile">Kiểm tra và gắn gói theo loại<select id="allocationProductProfile" ${busy || loading ? 'disabled' : ''}>${[['all-tools', 'Toàn bộ công cụ'], ['vietsub-cloud', 'Tool dịch · Có Cloud']].map(([value, label]) => `<option value="${value}" ${profile === value ? 'selected' : ''}>${label}</option>`).join('')}</select></label>`;
  const identity = () => shell.state.user?.userId || shell.state.user?.email || '';
  const valid = (version, user) => version === generation && user === identity() && Boolean(shell.state.accessToken);
  const sellable = plan => plan.isActive && plan.isPublic && Number(plan.salePriceVnd) > 0 && Number(plan.defaultDurationDays) > 0 && Number(plan.defaultDurationDays) <= 3650;
  const ready = org => (isVietsub() ? productCheck(org)?.ready === true : org.isReady) && org.isAutoAssignmentEnabled && org.organizationStatus === 'Active';
  const free = org => Math.max(0, Number(org.seatCapacity) - Number(org.activeSeats) - Number(org.reservedSeats));
  const pill = (label, passed) => `<span class="allocation-status ${passed ? 'pass' : 'pending'}">${icon(passed ? 'circle-check' : 'triangle-alert')}${esc(label)}</span>`;
  const button = (label, action, disabled = false, primary = false) => `<button type="button" class="${primary ? 'primary-button' : 'ghost-button'}" data-allocation-action="${action}" ${disabled || busy ? 'disabled' : ''}>${label}</button>`;
  const steps = ['Nhóm phân bổ', 'Tổ chức', 'Sẵn sàng', 'Gói & mở bán'];
  const current = () => context?.detail;
  const selectedOrg = () => current()?.organizations.find(org => org.organizationId === selectedOrganizationId) || current()?.organizations[0];
  const paymentReady = () => auxiliary?.payment?.enabled && auxiliary.payment.accounts?.some(account => account.accountId === auxiliary.payment.selectedAccountId);
  const capacity = () => (current()?.organizations || []).filter(ready).reduce((sum, org) => sum + free(org), 0);
  const readinessCount = () => (current()?.organizations || []).filter(ready).length;
  const currentMappings = () => new Map((current()?.licensePlans || []).map(mapping => [mapping.licensePlanId, mapping]));
  const activeMappings = () => (current()?.licensePlans || []).filter(mapping => mapping.isActive && mapping.isSellable && planProfile(mapping) === profile);
  const selectionCount = () => activeMappings().length + [...chosen].filter(id => !currentMappings().get(id)?.isActive).length;

  function progress() {
    const done = [true, current().organizations.length > 0, readinessCount() > 0,
      current().pool.status === 'Active' && activeMappings().length > 0 && capacity() > 0 && paymentReady()];
    return `<nav class="allocation-progress" aria-label="Tiến độ thiết lập">${steps.map((label, i) => `<button type="button" data-allocation-step="${i + 1}" ${busy ? 'disabled' : ''} ${step === i + 1 ? 'aria-current="step"' : ''}><span class="allocation-step-number ${done[i] ? 'complete' : ''}">${done[i] ? icon('circle-check') : i + 1}</span><span>${i + 1}. ${label}</span></button>`).join('')}</nav>`;
  }

  function summary(pool) {
    const receiving = pool.status === 'Active' && pool.allocatableAvailableSeats > 0 && pool.activeLicensePlanCount > 0;
    return `<section class="allocation-summary"><div><h3>${esc(pool.name)}</h3>${pill(receiving ? 'Có chỗ nhận khách' : 'Cần thiết lập / kiểm tra', receiving)}</div><div><strong>${esc(pool.allocatableOrganizationCount ?? 0)}</strong><span>tổ chức sẵn sàng</span></div><div><strong>${esc(pool.allocatableAvailableSeats ?? 0)}</strong><span>chỗ có thể cấp</span></div><div><strong>${esc(pool.activeLicensePlanCount ?? 0)}</strong><span>gói hợp lệ đã gắn</span></div></section>`;
  }

  function overview() {
    const detail = current();
    const orgs = detail.organizations;
    const available = readinessCount() > 0 && capacity() > 0;
    const tasks = [
      ['Thêm tổ chức và sức chứa', orgs.length ? `${orgs.length} tổ chức đã được thêm. Có thể sửa hoặc thêm tổ chức khác.` : 'Nhóm chưa có tổ chức nào để nhận khách.', 2, 'Cấu hình tổ chức'],
      ['Kiểm tra điều kiện sẵn sàng', available ? `Có ${readinessCount()} tổ chức đạt kiểm tra, còn ${capacity()} chỗ.` : isLocal() ? 'Kiểm tra tổ chức hoạt động, tự phân bổ và số chỗ còn trống.' : isVietsub() ? 'Kiểm tra OpenAI dịch, bảng giá và ngân sách Cloud.' : 'Xác nhận ngân sách, OpenAI, chính sách video và bảng giá; kiểm tra số chỗ còn trống.', 3, 'Kiểm tra sẵn sàng'],
      ['Gắn gói và mở bán', `${activeMappings().length} gói hợp lệ đã gắn. Kiểm tra các gói còn thiếu và trạng thái nhóm.`, 4, 'Gắn gói']
    ];
    return `${summary(detail.pool)}<div class="allocation-columns"><section class="allocation-panel"><h3>Việc cần hoàn thành</h3>${tasks.map(([title, text, next, label]) => `<article class="allocation-task"><div><h4>${title}</h4><p>${esc(text)}</p></div><button type="button" class="ghost-button" data-allocation-step="${next}" ${busy ? 'disabled' : ''}>${label}${icon('arrow-right')}</button></article>`).join('')}</section><section class="allocation-panel"><h3>Điều kiện mở bán</h3><div class="allocation-checks">${pill(loading ? 'Đang kiểm tra SePay' : loadError ? 'Chưa xác minh SePay' : paymentReady() ? 'Thanh toán SePay đã bật' : 'Thanh toán SePay chưa sẵn sàng', paymentReady() && !loading && !loadError)}${pill(`${activeMappings().length} gói hợp lệ đã gắn`, activeMappings().length > 0)}${pill(available ? 'Có tổ chức sẵn sàng còn chỗ' : 'Chưa có tổ chức sẵn sàng còn chỗ', available)}${pill(detail.pool.status === 'Active' ? 'Nhóm đang hoạt động' : 'Nhóm đang ở bản nháp', detail.pool.status === 'Active')}</div>${button('Thiết lập SePay', 'payment')}<p class="allocation-note">Khách chỉ chọn mua được khi có gói hợp lệ, thanh toán sẵn sàng và tổ chức còn chỗ có thể cấp.</p></section></div>`;
  }

  function orgDraft(id) {
    const org = current()?.organizations.find(item => item.organizationId === id);
    return { organizationId: id || '', seatCapacity: org?.seatCapacity ?? 1, priority: org?.priority ?? 100, auto: org?.isAutoAssignmentEnabled ?? false };
  }

  function organizationForm() {
    const organizations = auxiliary?.organizations || [];
    if (!organizations.length) return `<section class="allocation-panel"><h3>Chọn tổ chức nhận khách</h3><p>${loading ? 'Đang tải tổ chức...' : loadError ? 'Chưa tải được danh sách tổ chức.' : 'Chưa có tổ chức để thêm vào nhóm.'}</p>${button('Mở danh sách tổ chức', 'directory')}</section>`;
    if (!draft) draft = orgDraft(organizations.find(org => org.status === 'Active')?.organizationId || organizations[0].organizationId);
    const linked = current().organizations.find(org => org.organizationId === draft.organizationId);
    const minimum = Math.max(1, Number(linked?.activeSeats || 0) + Number(linked?.reservedSeats || 0));
    const name = organizations.find(org => org.organizationId === draft.organizationId)?.name || '';
    return `<div class="allocation-columns"><form id="allocationOrganizationForm" class="allocation-panel"><h3>Chọn tổ chức nhận khách</h3><label for="allocationOrganization">Tổ chức</label><select id="allocationOrganization" name="organizationId" required ${busy ? 'disabled' : ''}>${organizations.map(org => `<option value="${esc(org.organizationId)}" ${org.organizationId === draft.organizationId ? 'selected' : ''}>${esc(org.name)} · ${esc(org.status)}${current().organizations.some(link => link.organizationId === org.organizationId) ? ' · Đã trong nhóm' : ''}</option>`).join('')}</select><p class="form-hint">Chọn tổ chức để thêm vào nhóm hoặc chỉnh sửa sức chứa hiện có.</p><label for="allocationCapacity">Sức chứa khách hàng</label><input id="allocationCapacity" name="seatCapacity" type="number" min="${minimum}" max="100000" value="${esc(draft.seatCapacity)}" required ${busy ? 'disabled' : ''}/><p class="form-hint">Tối thiểu ${minimum} chỗ. Đang dùng: ${esc(linked?.activeSeats ?? 0)} · Đang giữ: ${esc(linked?.reservedSeats ?? 0)}.</p><label class="allocation-toggle"><input name="auto" type="checkbox" ${draft.auto ? 'checked' : ''} ${busy ? 'disabled' : ''}/><span><strong>Tự động phân bổ khách</strong><small>Chỉ nhận khách sau khi tổ chức đạt kiểm tra sẵn sàng.</small></span></label><label for="allocationPriority">Thứ tự ưu tiên</label><input id="allocationPriority" name="priority" type="number" min="0" max="100000" value="${esc(draft.priority)}" required ${busy ? 'disabled' : ''}/><p class="form-hint">Số nhỏ hơn được ưu tiên trước khi cân bằng sức chứa.</p><p class="allocation-note">Thêm tổ chức chưa đồng nghĩa với sẵn sàng. ${isLocal() ? 'Gói Local chỉ kiểm tra tổ chức và sức chứa.' : isVietsub() ? 'Bước tiếp theo kiểm tra cấu hình OpenAI phục vụ dịch Cloud.' : 'Bước tiếp theo kiểm tra ngân sách, OpenAI, chính sách video và bảng giá.'}</p><div class="allocation-footer">${button('Quay lại', 'back')}<button class="primary-button" type="submit" ${busy || loading || loadError ? 'disabled' : ''}>${busy ? 'Đang lưu...' : 'Lưu và kiểm tra sẵn sàng →'}</button></div></form><section class="allocation-panel"><h3>Sau khi lưu</h3><h4>${esc(name)}</h4><div id="allocationOrganizationPreview">${preview()}</div>${pill(linked?.isReady ? 'Đã được kiểm tra bởi server' : 'Chưa kiểm tra sẵn sàng', linked?.isReady)}<p class="allocation-note">Khi chỉnh cấu hình, kết quả sẵn sàng được kiểm tra lại ở bước tiếp theo.</p></section></div>`;
  }

  function preview() {
    return `<p>Nhóm: ${esc(current().pool.name)}</p><p>Sức chứa: ${esc(draft?.seatCapacity ?? 0)} khách</p><p>Phân bổ tự động: ${draft?.auto ? 'Bật' : 'Tắt'}</p>`;
  }

  function productInspection(org) {
    const check = productCheck(org), passed = ready(org);
    const rows = [
      ['Tổ chức hoạt động', org.organizationStatus === 'Active', 'directory'],
      ['Tự động phân bổ', org.isAutoAssignmentEnabled, 'capacity'],
      ['Còn chỗ nhận khách', free(org) > 0, 'capacity']
    ];
    return `<label for="allocationInspectOrganization">Tổ chức đang kiểm tra<select id="allocationInspectOrganization" ${busy ? 'disabled' : ''}>${current().organizations.map(item => `<option value="${esc(item.organizationId)}" ${item.organizationId === org.organizationId ? 'selected' : ''}>${esc(item.organizationName)}</option>`).join('')}</select></label>
      <div class="allocation-notice ${passed ? 'pass' : ''}" role="status"><h3>${passed ? 'Tổ chức đủ điều kiện cho gói dịch' : 'Cần hoàn thành thiết lập gói dịch'}</h3><p>${esc(check?.message || 'Server chưa cung cấp kiểm tra gói dịch. Cần cập nhật server trước khi mở bán.')}</p></div>
      <section class="allocation-panel"><h3>Điều kiện nhận khách</h3>${rows.map(([label, ok, target]) => `<article class="allocation-condition"><strong>${label}</strong>${ok ? pill('Đạt', true) : button('Thiết lập →', 'fix-' + target)}</article>`).join('')}
      ${isLocal() ? '<p>Local không yêu cầu API key hoặc ngân sách AI. Máy khách vẫn cần cài và kiểm tra runtime/model.</p>' : `<article class="allocation-condition"><strong>OpenAI, model dịch, bảng giá và ngân sách</strong>${check?.ready ? pill('Đạt', true) : button('Sửa cấu hình dịch Cloud →', 'fix-translation')}</article>`}
      ${button('Kiểm tra lại', 'refresh')}</section><div class="allocation-footer">${button('Quay lại', 'back')}<button type="button" class="primary-button" data-allocation-step="4" ${busy || loading || loadError || !readinessCount() ? 'disabled' : ''}>Tiếp tục gắn gói →</button></div>`;
  }

  function inspection() {
    const org = selectedOrg();
    if (!org) return `<section class="allocation-panel"><h3>Chưa có tổ chức để kiểm tra</h3><p>Thêm tổ chức và cấu hình sức chứa trước.</p><button type="button" class="primary-button" data-allocation-step="2">Thêm tổ chức</button></section>`;
    selectedOrganizationId = org.organizationId;
    if (isVietsub()) return productInspection(org);
    const directory = auxiliary?.organizations.find(item => item.organizationId === org.organizationId);
    const openAi = directory?.aiReadiness?.find(item => item.providerCode?.toLowerCase() === 'openai');
    const video = directory?.aiReadiness?.find(item => item.providerCode?.toLowerCase() !== 'openai');
    const rows = [
      ['Trạng thái tổ chức', org.organizationStatus === 'Active', org.organizationStatus === 'Active' ? 'Đang hoạt động' : 'Tổ chức chưa hoạt động', 'directory'],
      ['Ngân sách AI', Number(directory?.monthlyBudgetLimit) > 0, directory ? `Hạn mức ${directory.monthlyBudgetLimit} ${directory.currencyCode || 'USD'}` : 'Chưa tải được ngân sách', 'budget'],
      ['OpenAI', openAi?.ready === true, openAi ? openAi.ready ? 'Nhà cung cấp, credential và bảng giá hợp lệ' : (openAi.blockingReasons || []).join(', ') || 'Cần kiểm tra cấu hình OpenAI' : 'Chưa có đánh giá OpenAI', 'openai'],
      ['Chính sách video', video?.ready === true, video ? video.ready ? 'Chính sách, credential và bảng giá hợp lệ' : (video.blockingReasons || []).join(', ') || 'Cần kiểm tra chính sách video' : 'Chưa có đánh giá chính sách video', 'video']
    ];
    const passed = ready(org);
    return `<div class="allocation-inspection-heading"><label for="allocationInspectOrganization">Tổ chức đang kiểm tra<select id="allocationInspectOrganization" ${busy ? 'disabled' : ''}>${current().organizations.map(item => `<option value="${esc(item.organizationId)}" ${item.organizationId === org.organizationId ? 'selected' : ''}>${esc(item.organizationName)}</option>`).join('')}</select></label>${button('Kiểm tra lại', 'inspect', loading || Boolean(loadError) || !org.isAutoAssignmentEnabled, true)}</div><div class="allocation-notice ${passed ? 'pass' : ''}"><h3>${passed ? 'Tổ chức đã vượt qua kiểm tra sẵn sàng' : 'Tổ chức chưa thể nhận khách'}</h3><p>${esc(org.readinessMessage || (passed ? 'Các điều kiện đã được server xác minh. Có thể tiếp tục gắn gói.' : 'Hoàn thành cấu hình bên dưới rồi bấm Kiểm tra lại để server xác minh.'))}</p>${!org.isAutoAssignmentEnabled ? '<p>Phân bổ tự động đang tắt. Hãy bật ở bước Tổ chức trước khi kiểm tra.</p>' : ''}</div><div class="allocation-columns"><section class="allocation-panel"><h3>Điều kiện cấp khách</h3>${rows.map(([label, ok, text, target]) => `<article class="allocation-condition"><div><strong>${label}</strong><p>${esc(text)}</p></div>${ok ? pill('Đạt', true) : button('Thiết lập →', 'fix-' + target)}</article>`).join('')}<p class="allocation-note">Các mục trên là hướng dẫn cấu hình. Trạng thái sẵn sàng cuối cùng do server xác minh khi kiểm tra.</p>${button('Bảng giá AI', 'fix-pricing')}</section><section class="allocation-panel"><h3>Sức chứa sau khi sẵn sàng</h3><strong class="allocation-large-number">${free(org)}</strong><p>chỗ còn trống trong cấu hình</p>${free(org) <= 0 ? button("Điều chỉnh sức chứa →", "fix-capacity") : ""}${pill(passed ? 'Đã kiểm tra sẵn sàng' : 'Chưa tính vào chỗ có thể cấp', passed)}<p>Đang dùng: ${esc(org.activeSeats)} · Đang giữ: ${esc(org.reservedSeats)}</p></section></div><div class="allocation-footer">${button('Quay lại', 'back')}<button type="button" class="primary-button" data-allocation-step="4" ${busy || loading || !readinessCount() ? 'disabled' : ''}>Tiếp tục gắn gói →</button></div>`;
  }

  function plansForm() {
    const plans = (auxiliary?.plans || []).filter(plan => planProfile(plan) === profile);
    const mappings = currentMappings();
    const available = capacity() > 0 && readinessCount() > 0;
    const enabled = available && paymentReady() && !loading && !loadError;
    return `<div class="allocation-notice ${enabled ? 'pass' : ''}"><h3>${enabled ? 'Tổ chức đã sẵn sàng nhận khách' : 'Còn điều kiện cần hoàn thành'}</h3><p>${readinessCount()} tổ chức đạt kiểm tra · ${capacity()} chỗ còn trống · ${loading || loadError ? 'SePay chưa xác minh' : paymentReady() ? 'SePay đã bật' : 'SePay chưa sẵn sàng'}</p></div><div class="allocation-columns"><form id="allocationPlansForm" class="allocation-panel"><h3>Chọn gói cho nhóm này</h3><p>Gói đang gắn được giữ nguyên. Sửa hoặc gỡ trong phần quản lý chi tiết.</p>${current().pool.status === 'Active' ? '<p class="allocation-note">Nhóm đang hoạt động. Gói mới có thể mở bán ngay khi liên kết được lưu; các gói được lưu lần lượt.</p>' : ''}<div class="table-scroll"><table class="data-table allocation-plan-table"><thead><tr><th>Chọn</th><th>Gói sử dụng</th><th>Giá / thời hạn</th><th>Trạng thái</th></tr></thead><tbody>${plans.map(plan => {
      const own = mappings.get(plan.licensePlanId);
      const other = auxiliary.mappings.get(plan.licensePlanId);
      const foreign = other && other.organizationPoolId !== current().pool.organizationPoolId;
      const existing = own?.isActive === true;
      const allowed = sellable(plan) && !foreign;
      return `<tr><td><input type="checkbox" name="plan" value="${esc(plan.licensePlanId)}" aria-label="Gắn ${esc(plan.name)}" ${existing || chosen.has(plan.licensePlanId) ? 'checked' : ''} ${busy || existing || !allowed ? 'disabled' : ''}/></td><td><strong>${esc(plan.name)}</strong><br><small>${esc(plan.planCode)}</small></td><td>${esc(new Intl.NumberFormat('vi-VN').format(plan.salePriceVnd ?? 0))} đ<br><small>${esc(plan.defaultDurationDays ?? '—')} ngày</small></td><td>${foreign ? `Đang ở nhóm ${esc(other.organizationPoolName)}. Quản lý liên kết trước khi chuyển.` : !sellable(plan) ? 'Gói chưa đủ điều kiện bán' : existing ? pill('Đã gắn', true) : 'Có thể gắn'}</td></tr>`;
    }).join('')}</tbody></table></div>${!plans.length ? '<p>Chưa có gói hoặc chưa tải được danh sách.</p>' : ''}<div ${isLocal() ? 'hidden' : ''}><label for="allocationMemberBudget">Hạn mức AI mỗi khách / tháng (không bắt buộc)</label><input id="allocationMemberBudget" name="budget" type="number" min="0" max="100000000" step="0.000001" value="${esc(memberBudget)}" placeholder="Theo ngân sách tổ chức" ${busy ? 'disabled' : ''}/><p class="form-hint">Áp dụng cho gói mới được gắn trong lần lưu này. Để trống để dùng chính sách tổ chức; 0 khóa AI của khách.</p></div>${isLocal() ? '<p>Gói Local không cấp ngân sách AI cho khách.</p>' : ''}<div class="allocation-footer">${button('Quay lại', 'back')}<button class="primary-button" type="submit" ${busy || !enabled || !selectionCount() ? 'disabled' : ''}>${busy ? 'Đang lưu...' : 'Lưu gói và bật nhóm phân bổ →'}</button></div></form><section class="allocation-panel"><h3>Kiểm tra trước khi mở bán</h3><div class="allocation-checks">${pill('Có tổ chức đạt kiểm tra', readinessCount() > 0)}${pill(`Còn ${capacity()} chỗ`, capacity() > 0)}<span id="allocationSelectedPlansStatus">${pill(`${selectionCount()} gói hợp lệ được chọn / đã gắn`, selectionCount() > 0)}</span>${pill(loading || loadError ? 'Chưa xác minh thanh toán SePay' : paymentReady() ? 'Thanh toán SePay đã bật' : 'Thanh toán SePay chưa sẵn sàng', paymentReady() && !loading && !loadError)}</div>${button('Thiết lập SePay', 'payment')}<p class="allocation-note">Sau khi lưu, khách có thể chọn gói theo số chỗ thực sự sẵn sàng. Chỗ được giữ khi tạo đơn thanh toán.</p><h4>Khách hàng sẽ thấy</h4><p>${current().pool.status === 'Active' && capacity() > 0 && activeMappings().length && paymentReady() && !loadError ? 'Gói đã gắn có chỗ để khách chọn mua.' : 'Chưa mở bán đầy đủ. Hoàn tất điều kiện và lưu cấu hình.'}</p></section></div>`;
  }

  function render(focus = false) {
    if (!context || !shell.state.accessToken) return;
    const detail = current();
    let guide;
    if (!detail) {
      guide = `<div class="allocation-intro"><span class="allocation-guide-refresh">${button('Làm mới', 'refresh')}</span><h2>Thiết lập phân bổ khách hàng</h2><p>Chọn nhóm để thêm tổ chức, kiểm tra sẵn sàng và gắn gói mở bán.</p></div><div class="allocation-pool-cards">${context.pools.map(pool => `<article>${summary(pool)}<button type="button" class="primary-button" data-open-pool-setup="${esc(pool.organizationPoolId)}">Tiếp tục thiết lập ${icon('arrow-right')}</button></article>`).join('')}</div>`;
    } else {
      guide = `<div class="allocation-intro"><span class="allocation-guide-refresh">${button('Làm mới', 'refresh')}</span><button type="button" class="text-button" data-close-pool>${icon('arrow-left')}Danh sách nhóm</button><h2 id="allocationTitle" tabindex="-1">${['Thiết lập phân bổ khách hàng', 'Thêm tổ chức và đặt sức chứa', 'Kiểm tra sẵn sàng', 'Gắn gói và mở bán'][step - 1]}</h2><p>${esc(detail.pool.name)} · Bước ${step}/4</p></div>${progress()}${loadError ? `<div class="allocation-error" role="alert">${esc(loadError)} ${button('Thử tải lại', 'reload')}</div>` : loading ? '<p class="allocation-loading" role="status">Đang xác minh tổ chức, gói, liên kết và SePay...</p>' : ''}${error ? `<div class="allocation-error" role="alert">${esc(error)}</div>` : ''}${[overview, organizationForm, inspection, plansForm][step - 1]()}`;
    }
    root.closest('.workspace-section')?.classList.add('allocation-container');
    const management = detail || context.pools.length ? `<details class="allocation-management"><summary>${detail ? 'Quản lý chi tiết, liên kết và nhật ký phân bổ' : 'Danh sách nhóm phân bổ'}</summary>${context.managementMarkup}</details>` : context.managementMarkup;
    root.innerHTML = `<div class="allocation-guide" aria-busy="${busy || loading}">${current() ? profilePicker() : ""}${guide}</div>${management}`;
    if (focus) document.getElementById('allocationTitle')?.focus({ preventScroll: true });
  }

  async function load() {
    if (!current() || busy) return;
    controller?.abort(); clearTimeout(timer);
    const version = ++generation, user = identity();
    controller = new AbortController();
    const signal = controller.signal;
    loading = true; loadError = ''; render();
    timer = setTimeout(() => controller?.abort(), 20000);
    try {
      const [organizations, plans, payment, pools] = await Promise.all([
        api('/api/organizations', { signal }), api('/api/admin/licenses/plans', { signal }),
        api('/api/admin/payments/sepay', { signal }), api(endpoint, { signal })
      ]);
      const details = await Promise.all(pools.filter(pool => pool.licensePlanCount > 0 && pool.organizationPoolId !== current()?.pool.organizationPoolId).map(pool => api(`${endpoint}/${pool.organizationPoolId}`, { signal })));
      if (!valid(version, user)) return;
      auxiliary = { organizations, plans, payment, mappings: new Map() };
      if (!explicitProfile) {
        const profiles = [...new Set(current().licensePlans.map(planProfile))];
        if (profiles.length === 1 && ['all-tools', 'vietsub-cloud'].includes(profiles[0])) profile = profiles[0];
      }
      details.forEach(detail => detail.licensePlans.forEach(mapping => auxiliary.mappings.set(mapping.licensePlanId, mapping)));
      current().licensePlans.forEach(mapping => auxiliary.mappings.set(mapping.licensePlanId, mapping));
      for (const id of chosen) {
        const mapping = auxiliary.mappings.get(id);
        if ((mapping && mapping.organizationPoolId !== current().pool.organizationPoolId) || !plans.some(plan => plan.licensePlanId === id && sellable(plan))) chosen.delete(id);
      }
    } catch (failure) {
      if (!valid(version, user)) return;
      if (failure.status === 401) shell.showLogin('Phiên đăng nhập đã hết hạn.');
      loadError = failure.name === 'AbortError' ? 'Tải cấu hình quá thời gian. Hãy thử lại.' : failure.message || 'Chưa tải được cấu hình.';
    } finally {
      if (valid(version, user)) { clearTimeout(timer); loading = false; render(); }
    }
  }

  async function mutate(action, nextStep) {
    if (busy || loading || loadError) return;
    const version = generation, user = identity(), callbacks = context.callbacks, ownOperation = ++operation;
    busy = true; error = '';
    root.querySelector('.allocation-guide')?.setAttribute('aria-busy', 'true');
    root.querySelectorAll('button, input, select').forEach(control => { control.disabled = true; });
    root.setAttribute('aria-busy', 'true');
    try {
      await action(() => valid(version, user));
      if (!valid(version, user)) return;
      step = nextStep; chosen.clear(); draft = null;
      toast('Đã lưu và xác minh cấu hình.');
    } catch (failure) {
      if (!valid(version, user)) return;
      if (failure.status === 401) shell.showLogin('Phiên đăng nhập đã hết hạn.');
      error = failure.message || 'Không thể lưu cấu hình. Hãy kiểm tra và thử lại.';
    } finally {
      if (ownOperation !== operation) return;
      busy = false; root.removeAttribute('aria-busy');
      if (valid(version, user)) await callbacks.refresh();
    }
  }

  root.addEventListener('change', event => {
    if (event.target.id === 'allocationProductProfile' && !busy && !loading) {
      profile = event.target.value; explicitProfile = true; chosen.clear(); memberBudget = ''; error = ''; render(); return;
    }
    if (event.target.id === 'allocationOrganization') { draft = orgDraft(event.target.value); render(); }
    if (event.target.id === 'allocationInspectOrganization') { selectedOrganizationId = event.target.value; error = ''; render(); }
    if (event.target.name === 'plan') {
      event.target.checked ? chosen.add(event.target.value) : chosen.delete(event.target.value);
      document.getElementById('allocationSelectedPlansStatus').innerHTML = pill(`${selectionCount()} gói hợp lệ được chọn / đã gắn`, selectionCount() > 0);
      document.querySelector('#allocationPlansForm [type=submit]').disabled = busy || loading || Boolean(loadError) || !capacity() || !paymentReady() || !selectionCount();
    }
  });
  root.addEventListener('input', event => {
    if (event.target.id === 'allocationMemberBudget') memberBudget = event.target.value;
    if (event.target.closest('#allocationOrganizationForm') && draft) {
      const form = event.target.form;
      draft = { organizationId: form.elements.organizationId.value, seatCapacity: form.elements.seatCapacity.value, priority: form.elements.priority.value, auto: form.elements.auto.checked };
      document.getElementById('allocationOrganizationPreview').innerHTML = preview();
    }
  });
  root.addEventListener('click', async event => {
    const next = event.target.closest('[data-allocation-step]');
    if (next && !busy) { step = Number(next.dataset.allocationStep); error = ''; render(true); return; }
    const control = event.target.closest('[data-allocation-action]');
    if (!control || control.disabled || busy) return;
    const action = control.dataset.allocationAction;
    if (action === 'back') { step = Math.max(1, step - 1); error = ''; render(true); return; }
    if (action === 'reload') return load();
    if (action === 'refresh') return context.callbacks.refresh();
    if (action === 'inspect') {
      const org = selectedOrg(), poolId = current().pool.organizationPoolId;
      return mutate(async () => api(`${endpoint}/${poolId}/organizations/${org.organizationId}`, { method: 'PUT', body: JSON.stringify({ organizationId: org.organizationId, seatCapacity: org.seatCapacity, priority: org.priority, isAutoAssignmentEnabled: true, isReady: true, readinessMessage: null, ...profileFields() }) }), 3);
    }
    returnVisible = true;
    let banner = document.getElementById('allocationReturn');
    if (!banner) { document.getElementById('adminMain').insertAdjacentHTML('afterbegin', '<div id="allocationReturn" class="allocation-return"><button type="button" class="ghost-button" data-allocation-return>← Quay lại thiết lập phân bổ</button></div>'); banner = document.getElementById('allocationReturn'); }
    banner.hidden = false;
    try {
      if (action === 'payment') return await shell.navigate('payments');
      if (action === 'fix-translation') {
        const check = productCheck(selectedOrg());
        if (check?.target === 'cloud-settings') { toast(check.message, true); return; }
        await context.callbacks.configure(selectedOrg()?.organizationId, 'translation', check);
      } else await context.callbacks.configure(selectedOrg()?.organizationId, action.replace('fix-', ''));
    } catch (failure) { toast(failure.message, true); }
  });
  document.addEventListener('click', async event => {
    if (!event.target.closest('[data-allocation-return]') || !context) return;
    returnVisible = false; document.getElementById('allocationReturn').hidden = true;
    await context.callbacks.returnToPool();
  });
  root.addEventListener('submit', event => {
    if (event.target.id === 'allocationOrganizationForm') {
      event.preventDefault();
      const form = event.target;
      const organizationId = form.elements.organizationId.value, poolId = current().pool.organizationPoolId;
      const request = { organizationId, seatCapacity: Number(form.elements.seatCapacity.value), priority: Number(form.elements.priority.value), isAutoAssignmentEnabled: form.elements.auto.checked, isReady: false, readinessMessage: null, ...profileFields() };
      selectedOrganizationId = organizationId;
      return mutate(async () => api(`${endpoint}/${poolId}/organizations/${organizationId}`, { method: 'PUT', body: JSON.stringify(request) }), 3);
    }
    if (event.target.id !== 'allocationPlansForm') return;
    event.preventDefault();
    const ids = [...chosen], text = event.target.elements.budget.value, budget = isLocal() ? 0 : text === '' ? null : Number(text);
    const poolId = current().pool.organizationPoolId;
    const startedActive = current().pool.status === 'Active';
    if (!ids.length && !activeMappings().length) { error = 'Chọn ít nhất một gói hợp lệ để mở bán.'; return render(); }
    return mutate(async alive => {
      // Re-read mappings before every save: a plan may have moved since this screen was opened.
      let saved = 0;
      try {
        const freshPools = await api(endpoint);
        const details = await Promise.all(freshPools.filter(pool => pool.licensePlanCount > 0).map(pool => api(`${endpoint}/${pool.organizationPoolId}`)));
        if (!alive()) return;
        const foreign = details.flatMap(detail => detail.licensePlans).find(mapping => ids.includes(mapping.licensePlanId) && mapping.organizationPoolId !== poolId);
        if (foreign) throw new Error(`Gói ${foreign.planName} đã thuộc nhóm ${foreign.organizationPoolName}. Tải lại và quản lý liên kết trước khi chuyển.`);
        const payment = await api('/api/admin/payments/sepay');
        if (!alive()) return;
        if (!payment.enabled || !payment.accounts?.some(account => account.accountId === payment.selectedAccountId)) throw new Error('Thanh toán SePay chưa sẵn sàng. Chưa bật nhóm phân bổ.');
        const fresh = await api(`${endpoint}/${poolId}`);
        if (!alive()) return;
        if (!fresh.organizations.some(org => ready(org) && free(org) > 0)) throw new Error('Không còn tổ chức sẵn sàng còn chỗ. Hãy kiểm tra lại trước khi mở bán.');
        const own = new Map(fresh.licensePlans.map(mapping => [mapping.licensePlanId, mapping]));
        for (const id of ids) {
          if (!alive()) return;
          if (own.get(id)?.isActive) continue;
          await api(`${endpoint}/license-plans/${id}`, { method: 'PUT', body: JSON.stringify({ organizationPoolId: poolId, defaultMemberMonthlyBudgetLimit: budget, isActive: true }) });
          saved++;
        }
        if (!alive()) return;
        const updated = await api(`${endpoint}/${poolId}`);
        if (!alive()) return;
        if (!updated.licensePlans.some(mapping => mapping.isActive && mapping.isSellable) || !updated.organizations.some(org => ready(org) && free(org) > 0)) throw new Error('Gói hoặc tổ chức không còn đủ điều kiện. Chưa bật nhóm phân bổ.');
        if (updated.pool.status !== 'Active') await api(`${endpoint}/${poolId}`, { method: 'PUT', body: JSON.stringify({ code: updated.pool.code, name: updated.pool.name, status: 'Active' }) });
      } catch (failure) {
        throw new Error(`${saved ? `Đã lưu ${saved} liên kết gói. ` : ''}${failure.message} Các liên kết đã lưu được giữ nguyên. ${startedActive ? 'Nhóm đang hoạt động; gói đã lưu có thể được mở bán.' : 'Nhóm nháp chỉ được bật sau khi hoàn tất.'}`);
      }
    }, 4);
  });

  window.videoMakerAllocationSetup = Object.freeze({
    setProductProfile(productScope, cloud = false) {
      profile = productScope === 'vietsub' && cloud ? 'vietsub-cloud' : 'all-tools';
      explicitProfile = true; chosen.clear(); memberBudget = ''; error = '';
    },
    goTo(nextStep, organizationId) {
      if (!context?.detail || busy || nextStep < 1 || nextStep > 4) return false;
      if (organizationId && !current().organizations.some(org => org.organizationId === organizationId)) return false;
      if (organizationId) selectedOrganizationId = organizationId;
      step = nextStep; error = ''; render(true); return true;
    },
    enhance(element, detail, pools, callbacks) {
      const id = detail?.pool.organizationPoolId || null;
      if (id !== current()?.pool.organizationPoolId) { step = 1; auxiliary = null; error = ''; draft = null; chosen.clear(); selectedOrganizationId = ''; memberBudget = ''; returnVisible = false; }
      context = { detail, pools, callbacks, managementMarkup: element.innerHTML };
      controller?.abort(); clearTimeout(timer); generation++; operation++; busy = false; loading = false;
      render();
      if (detail) load();
      const banner = document.getElementById('allocationReturn'); if (banner) banner.hidden = !returnVisible;
    }
  });
  new MutationObserver(() => {
    if (!document.getElementById('adminShell').classList.contains('hidden')) return;
    controller?.abort(); clearTimeout(timer); generation++; operation++; busy = false; auxiliary = null; context = null; profile = 'all-tools'; explicitProfile = false;
    const banner = document.getElementById('allocationReturn'); if (banner) banner.hidden = true;
  }).observe(document.getElementById('adminShell'), { attributes: true, attributeFilter: ['class'] });
})();
