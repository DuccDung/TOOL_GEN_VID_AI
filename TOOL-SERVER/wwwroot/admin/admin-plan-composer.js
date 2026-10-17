(() => {
  'use strict';
  const shell = window.videoMakerAdminShell;
  const root = document.getElementById('licensePlanComposer');
  const list = document.getElementById('licensePlanList');
  if (!shell || !root || !list) return;
  const { api, escapeHtml: esc, icon, toast } = shell;
  const endpoint = '/api/admin/licenses/plan-composer';
  let opened = false, viewActive = false, epoch = 0, readVersion = 0, controller, timeout, writeController;
  let setup = null, loading = false, loadError = '', saving = false, pending = null, sourceId = null, creationId = null;
  let editId = null, editorSnapshot = null;
  const byId = id => document.getElementById(id);
  const identity = () => shell.state.user?.userId || shell.state.user?.email || '';
  const alive = (version, user) => opened && epoch === version && identity() === user && Boolean(shell.state.accessToken);
  const storageKey = user => 'vmAdminPlanPending:' + user;
  const initialLocation = new URL(location.href);
  let requestedFromLocation = ['create-plan', 'edit-plan'].includes(initialLocation.searchParams.get('view'));
  function setComposerVisible(visible) {
    byId('adminSearchForm').classList.toggle('hidden', visible);
    const url = new URL(location.href);
    if (visible) { url.searchParams.set('view', editId ? 'edit-plan' : 'create-plan');
      if (editId) url.searchParams.set('planId', editId); else url.searchParams.delete('planId'); }
    else if (['create-plan', 'edit-plan'].includes(url.searchParams.get('view'))) { url.searchParams.delete('view'); url.searchParams.delete('planId'); }
    history.replaceState(history.state, '', url.pathname + url.search + url.hash);
  }
  const readPending = () => {
    try {
      const value = JSON.parse(sessionStorage.getItem(storageKey(identity())) || 'null');
      return value?.creationId && typeof value.publish === 'boolean' ? value : null;
    } catch { return null; }
  };
  const isVietsub = () => byId('composerProduct')?.value === 'vietsub';
  const localOnly = () => isVietsub() && !cloudEnabled();
  const cloudEnabled = () => isVietsub() && byId('composerCloud').checked;
  const availability = group => !isVietsub() ? group : { ...group, ...(cloudEnabled() ? group.vietsubCloud : group.vietsubLocal) || {
    ready: false, availableSeats: 0, blockingMessage: 'Server chưa cung cấp điều kiện mở bán cho gói tool dịch. Hãy cập nhật server.' } };
  const selected = () => { const group = setup?.groups.find(group => group.organizationPoolId === byId('composerGroup').value); return group && availability(group); };
  const defaultFeatures = () => [byId('composerDevices').value + ' thiết bị được kích hoạt', 'Quét OCR và biên tập phụ đề',
    'Dịch Local trên máy', 'Tạo giọng Local', 'Xuất SRT / MP4', ...(cloudEnabled() ? ['Dịch Cloud qua OpenAI theo hạn mức'] : [])];
  const features = () => byId('composerFeatures').value.split(/\r?\n/).map(value => value.trim()).filter(Boolean);
  const money = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 0 });
  function priceValue() {
    const value = byId('composerPrice').value.trim();
    if (!value) return null;
    if (!/^\d+$/.test(value) && !/^\d{1,3}(?:\.\d{3})+$/.test(value)) return NaN;
    return Number(value.replace(/\./g, ''));
  }
  function formatPrice() {
    const input = byId('composerPrice'), value = priceValue();
    if (!Number.isSafeInteger(value) || value < 0 || value > 1000000000000) return;
    const start = input.selectionStart, end = input.selectionEnd;
    const digitsBefore = position => input.value.slice(0, position).replace(/\D/g, '').length;
    const first = digitsBefore(start ?? input.value.length), last = digitsBefore(end ?? input.value.length);
    input.value = money.format(value);
    if (document.activeElement === input && start != null) {
      const position = count => { let seen = 0; for (let i = 0; i < input.value.length; i++) {
        if (/\d/.test(input.value[i]) && ++seen === count) return i + 1;
      } return count ? input.value.length : 0; };
      input.setSelectionRange(position(first), position(last));
    }
  }
  function validatePrice() {
    const value = priceValue();
    byId('composerPrice').setCustomValidity(value == null || Number.isSafeInteger(value) && value >= 1 && value <= 1000000000000
      ? '' : 'Nhập giá từ 1 đến 1.000.000.000.000 đ, bằng số nguyên.');
  }
  const markup = () => [
    '<button type="button" class="text-button composer-back" id="composerBack">' + icon('arrow-left') + 'Quay lại danh sách</button>',
    '<header class="composer-heading"><h1 id="planComposerTitle">Tạo gói &amp; mở bán</h1><p>Nhập thông tin gói. Hệ thống tự liên kết nhóm nhận khách.</p></header>',
    '<div id="composerStatus" class="composer-status" role="status" aria-live="polite"></div>',
    '<div class="composer-grid"><form id="composerForm" class="composer-form" novalidate>',
    '<div class="composer-fields"><section class="composer-section composer-basic"><h3>1. Thông tin gói</h3><div class="composer-fields">',
    '<label for="composerProduct">Loại gói<select id="composerProduct"><option value="all-tools">Toàn bộ công cụ</option><option value="vietsub">Chỉ tool dịch / Vietsub</option></select></label>',
    '<label for="composerName">Tên gói<input id="composerName" maxlength="200" minlength="2" required placeholder="Ví dụ: Gói 30 ngày" autocomplete="off" /></label>',
    '<div class="composer-two-fields"><label for="composerDuration">Thời hạn sử dụng<div class="composer-input-unit"><input id="composerDuration" type="number" min="1" max="3650" step="1" value="30" /><span>ngày</span></div></label>',
    '<label for="composerPrice">Giá bán<div class="composer-input-unit"><input id="composerPrice" type="text" inputmode="numeric" maxlength="17" placeholder="Nhập giá bán" /><span>đ</span></div></label></div>',
    '<label for="composerDevices">Số thiết bị<select id="composerDevices">' + [1, 2, 3, 5, 10].map(value => '<option value="' + value + '">' + value + ' thiết bị</option>').join('') + '</select></label>',
    '</div></section><section id="composerRights" class="composer-section composer-rights hidden"><h3>2. Quyền sử dụng</h3>',
    '<ul class="composer-capabilities">' + ['Quét OCR', 'Dịch Local', 'Biên tập phụ đề', 'Tạo giọng Local', 'Xuất SRT / MP4'].map(text => '<li>' + icon('status-check', 'status-check') + '<span>' + text + '</span></li>').join('') + '</ul>',
    '<label class="composer-cloud-toggle" for="composerCloud"><span><strong>Cho phép dịch Cloud</strong><small>Cấu hình OpenAI và ngân sách khi bật.</small></span><input id="composerCloud" type="checkbox" role="switch" /></label>',
    '<div id="composerCloudSettings" class="composer-cloud-settings hidden"><p>Cloud dùng model dịch và key OpenAI của tổ chức. Chi phí được trừ theo hạn mức đã cấu hình.</p><button id="composerCloudSetup" class="text-button" type="button">Cấu hình OpenAI và chi phí ' + icon('arrow-right') + '</button></div>',
    '<p id="composerLocalNote" class="composer-local-note">Thanh toán → Kích hoạt license → Dùng Local trên máy. Không cần tổ chức, nhóm nhận khách hay API key. Runtime cần được cài và kiểm tra trên máy khách.</p>',
    '<p class="composer-locked-tools">' + icon('shield-check') + '<span>Tạo ảnh, video AI và các công cụ khác không được cấp quyền.</span></p>',
    '<p id="composerRightsLocked" class="form-hint hidden">Quyền của gói hiện có được giữ nguyên. Hãy nhân bản để tạo gói có quyền khác.</p></section>',
    '<section id="composerRouting" class="composer-section composer-routing"><h3>3. Phân bổ khách hàng</h3><div class="composer-fields">',
    '<label for="composerGroup">Nhóm nhận khách<select id="composerGroup"><option value="">Đang tải nhóm nhận khách…</option></select></label>',
    '<div class="composer-group-info"><div class="composer-group-head"><p id="composerGroupStatus"></p><button type="button" class="text-button composer-reload" id="composerReload" aria-label="Kiểm tra lại nhóm nhận khách và thanh toán" title="Kiểm tra lại nhóm nhận khách và thanh toán">' + icon('refresh-cw') + '</button></div><p>Khách được tự động phân bổ sau khi thanh toán.</p>',
    '<button type="button" class="text-button hidden" id="composerGroupSetup">Thiết lập nhóm nhận khách ' + icon('arrow-right') + '</button>',
    '<button type="button" class="text-button hidden" id="composerPaymentSetup">Thiết lập SePay ' + icon('arrow-right') + '</button>',
    '</div></div></section>',
    '<details class="composer-advanced"><summary>' + icon('settings') + '<span><strong>Thiết lập nâng cao</strong><small>Mô tả, quyền lợi và hạn mức AI</small></span>' + icon('chevron-down') + '</summary>',
    '<div class="composer-advanced-fields"><label for="composerDescription">Mô tả<textarea id="composerDescription" rows="2" maxlength="1000"></textarea></label>',
    '<label for="composerFeatures">Quyền lợi hiển thị <small>Mỗi dòng một mục, tối đa 12 mục.</small><textarea id="composerFeatures" rows="3" maxlength="2500"></textarea></label>',
    '<span id="composerBudgetPosition" class="hidden"></span><label for="composerMemberBudget" id="composerMemberBudgetField">Hạn mức AI mỗi khách / tháng<input id="composerMemberBudget" type="number" min="0" max="100000000" step="0.000001" placeholder="Theo chính sách tổ chức" /><small>Theo tiền tệ tổ chức. Để trống để dùng chính sách tổ chức; 0 khóa AI của khách.</small></label>',
    '<div class="composer-two-fields"><label for="composerSessions">Phiên đồng thời<input id="composerSessions" type="number" min="1" max="100" value="1" required /></label>',
    '<label for="composerOffline">Thời gian offline (giờ)<input id="composerOffline" type="number" min="0" max="8760" value="0" required /></label></div>',
    '<label for="composerOrder">Thứ tự hiển thị<input id="composerOrder" type="number" min="0" max="10000" value="0" required /></label>',
    '<label for="composerPlanStatus" id="composerPlanStatusField" class="hidden">Trạng thái gói<select id="composerPlanStatus"><option value="draft">Nháp — chưa cấp hoặc bán</option><option value="private">Riêng tư / không mở bán — cho phép cấp thủ công</option><option value="sale">Mở bán</option></select></label>',
    '<p class="form-hint">Mã gói được tạo tự động. Gói nhân bản giữ quyền của gói nguồn.</p></div></details>',
    '</div><p id="composerMessage" class="form-message" role="alert"></p></form>',
    '<aside class="composer-preview"><h3>Xem trước gói</h3><p>Bản xem trước của gói sử dụng</p><article class="composer-offer"><span id="composerPreviewDuration" class="composer-offer-eyebrow"></span>',
    '<h3 id="composerPreviewName"></h3><p id="composerPreviewDescription"></p><strong id="composerPreviewPrice" class="composer-offer-price"></strong><span id="composerPreviewPeriod"></span>',
    '<p id="composerPreviewSeats" class="composer-offer-seats"></p><button type="button" class="primary-button" disabled aria-label="Nút chọn gói chỉ để xem trước">Chọn gói này</button>',
    '<ul id="composerPreviewFeatures"></ul><span id="composerPreviewCloud" class="composer-cloud-pill hidden"></span></article><div id="composerChecks" class="composer-checks hidden"><h3>Kiểm tra mở bán</h3><ul id="composerCheckItems"></ul></div><div class="composer-shared"><strong>Nhóm dùng chung</strong><p>Các gói trong nhóm dùng chung số chỗ còn trống.</p></div></aside>',
    '<footer class="composer-actions"><span>Chỉ mở các chức năng được cấp trong gói.</span><div><button id="composerDraft" form="composerForm" type="submit" name="mode" value="draft" class="ghost-button">Lưu nháp</button>',
    '<button id="composerPublish" form="composerForm" type="submit" name="mode" value="publish" class="primary-button">Tạo và mở bán ' + icon('arrow-right') + '</button></div></footer></div>'
  ].join('');

  function fill(plan) {
    let rights = {}; try { rights = JSON.parse(plan?.featureFlagsJson || '{}'); } catch { rights = { productScope: 'invalid' }; }
    byId('composerProduct').value = plan?.productScope ?? rights.productScope ?? 'all-tools';
    byId('composerCloud').checked = plan?.vietsubCloudEnabled ?? (rights.productScope === 'vietsub' && rights.vietsubCloudEnabled === true);
    byId('composerName').value = plan?.name || '';
    byId('composerDuration').value = plan?.durationDays ?? plan?.defaultDurationDays ?? 30;
    byId('composerPrice').value = plan?.priceVnd ?? plan?.salePriceVnd ?? '';
    formatPrice();
    const devices = plan?.maxActivatedDevices ?? 1;
    if (![...byId('composerDevices').options].some(option => Number(option.value) === devices))
      byId('composerDevices').add(new Option(devices + ' thiết bị', String(devices)));
    byId('composerDevices').value = String(devices);
    byId('composerDescription').value = plan?.description || '';
    let marketing = plan?.marketingFeatures;
    if (!marketing) { try { marketing = JSON.parse(plan?.marketingFeaturesJson || '[]'); } catch { marketing = []; } }
    byId('composerFeatures').value = Array.isArray(marketing) ? marketing.join('\n') : '';
    byId('composerSessions').value = plan?.maxConcurrentSessions ?? 1;
    byId('composerOffline').value = plan?.offlineGraceHours ?? 0;
    byId('composerOrder').value = plan?.displayOrder ?? 0;
    byId('composerMemberBudget').value = plan?.memberMonthlyAiBudget ?? '';
    byId('composerPlanStatus').value = plan?.publish != null
      ? plan.isActive ? plan.publish ? 'sale' : 'private' : 'draft'
      : plan?.isActive ? plan.isPublic ? 'sale' : 'private' : 'draft';
  }

  function open(plan = null, editing = false) {
    controller?.abort(); clearTimeout(timeout); epoch++;
    opened = true; viewActive = false; setup = null; loadError = ''; saving = false;
    pending = readPending(); editId = pending?.planId ?? (editing ? plan?.licensePlanId : null);
    editorSnapshot = pending?.expectedUpdatedAtUtc ? { plan: { updatedAtUtc: pending.expectedUpdatedAtUtc } } : null;
    sourceId = editId ? null : pending?.sourcePlanId ?? plan?.licensePlanId ?? null;
    creationId = pending?.creationId || crypto.randomUUID();
    root.innerHTML = markup();
    fill(pending || plan);
    if (plan && !pending && !editId) byId('composerName').value = (plan.name + ' (bản sao)').slice(0, 200);
    byId('planComposerTitle').textContent = pending ? 'Tiếp tục lưu gói' : editId ? 'Chỉnh sửa gói sử dụng' : plan ? 'Nhân bản gói & mở bán' : 'Tạo gói & mở bán';
    byId('composerPlanStatusField').classList.toggle('hidden', !editId);
    byId('composerDraft').classList.toggle('hidden', Boolean(editId));
    byId('composerForm').addEventListener('submit', submit);
    byId('composerForm').addEventListener('input', sync);
    byId('composerForm').addEventListener('change', sync);
    byId('composerPrice').addEventListener('focus', () => {
      const input = byId('composerPrice'), value = priceValue();
      if (!Number.isSafeInteger(value) || value < 0 || value > 1000000000000) return;
      const start = input.selectionStart, end = input.selectionEnd;
      const first = input.value.slice(0, start ?? input.value.length).replace(/\D/g, '').length;
      const last = input.value.slice(0, end ?? input.value.length).replace(/\D/g, '').length;
      input.value = String(value);
      input.setSelectionRange(Math.min(first, input.value.length), Math.min(last, input.value.length));
    });
    byId('composerPrice').addEventListener('blur', () => { formatPrice(); sync(); });
    byId('composerBack').addEventListener('click', close);
    byId('composerReload').addEventListener('click', () => loadSetup(byId('composerGroup').value));
    byId('composerGroupSetup').addEventListener('click', async () => {
      const poolId = byId('composerGroup').value;
      shell.navigate('organizations', { organizationScope: 'pools' });
      try { if (poolId) await window.videoMakerOrganizationAdmin?.openPoolSetup(poolId, isVietsub() ? 'vietsub' : 'all-tools', cloudEnabled()); }
      catch (error) { toast(error.message, true); }
    });
    byId('composerPaymentSetup').addEventListener('click', () => shell.navigate('payments'));
    byId('composerCloudSetup').addEventListener('click', async () => {
      const group = selected(), target = group?.setupTarget;
      if (target?.target === 'cloud-settings') {
        byId('composerMessage').textContent = group.blockingMessage;
        toast(group.blockingMessage, true); return;
      }
      if (!target?.organizationId) { byId('composerGroupSetup').click(); return; }
      try {
        await window.videoMakerOrganizationAdmin?.openSetupTarget(target, { returnTo: {
          label: 'Tạo gói tool dịch', restore: async () => { await shell.navigate('plans'); activate(); }
        } });
      } catch (error) { toast(error.message, true); }
    });
    for (const id of ['composerProduct', 'composerCloud']) byId(id).addEventListener('change', () => {
      renderGroups(byId('composerGroup').value); sync();
    });
    shell.navigate('plans'); activate();
    if (pending) byId('composerMessage').textContent = 'Yêu cầu trước chưa được xác nhận. Gửi lại cùng yêu cầu để kiểm tra kết quả; hệ thống không tạo thêm gói.';
    sync(); loadSetup(pending?.organizationPoolId || '');
    byId('composerName').focus();
  }

  function activate() {
    if (!opened) return;
    const refresh = !viewActive && (setup || loadError) && !saving && !pending;
    viewActive = true;
    setComposerVisible(true);
    list.classList.add('hidden'); root.classList.remove('hidden'); shell.setTopbarVisible(false);
    byId('breadcrumbCurrent').textContent = editId ? 'Gói sử dụng / Chỉnh sửa' : 'Gói sử dụng / Tạo gói';
    if (refresh) loadSetup(byId('composerGroup').value);
  }
  function close() {
    if (saving) return;
    opened = false; viewActive = false; epoch++; controller?.abort(); clearTimeout(timeout);
    setComposerVisible(false);
    root.classList.add('hidden'); list.classList.remove('hidden'); shell.navigate('plans');
    byId('addPlanButton').focus();
  }
  function reset() {
    opened = false; viewActive = false; epoch++; readVersion++; controller?.abort(); writeController?.abort(); clearTimeout(timeout);
    saving = false; setup = null; pending = null; root.replaceChildren();
    byId('adminSearchForm').classList.remove('hidden');
    root.classList.add('hidden'); list.classList.remove('hidden');
  }

  async function loadSetup(preferred = '') {
    if (!opened || saving) return;
    controller?.abort(); clearTimeout(timeout); controller = new AbortController();
    const ownController = controller, version = epoch, read = ++readVersion, user = identity();
    loading = true; loadError = ''; sync();
    timeout = setTimeout(() => ownController.abort(), 30000);
    try {
      const needsEditor = editId && !editorSnapshot;
      const [value, editor] = await Promise.all([
        api(endpoint + '/setup', { signal: ownController.signal }, false),
        needsEditor ? api(endpoint + '/' + encodeURIComponent(editId), { signal: ownController.signal }, false) : null
      ]);
      if (!alive(version, user) || read !== readVersion) return;
      if (!Array.isArray(value?.groups) || typeof value.paymentsReady !== 'boolean') throw new Error('Dữ liệu thiết lập chưa hợp lệ.');
      setup = value;
      if (editor) {
        if (String(editor.plan?.licensePlanId).toLowerCase() !== String(editId).toLowerCase() || !editor.plan.updatedAtUtc) throw new Error('Không tải được phiên bản gói hiện hành.');
        editorSnapshot = editor; fill({ ...editor.plan, memberMonthlyAiBudget: editor.memberMonthlyAiBudget });
        preferred = editor.organizationPoolId || '';
      }
      renderGroups(preferred);
    } catch (error) {
      if (!alive(version, user) || read !== readVersion) return;
      setup = null;
      loadError = error.name === 'AbortError' ? 'Kiểm tra thiết lập quá thời gian. Hãy thử lại.' : error.message;
      if (error.status === 401) { shell.showLogin('Phiên đăng nhập đã hết hạn.'); return; }
      byId('composerGroup').innerHTML = '<option value="">Chưa tải được nhóm nhận khách</option>';
    } finally {
      if (alive(version, user) && read === readVersion) { clearTimeout(timeout); loading = false; sync(); }
    }
  }

  function renderGroups(preferred = '') {
    if (!setup) return;
    const groups = setup.groups.map(availability), ready = groups.filter(group => group.ready);
    const chosen = preferred || (editId ? '' : ready.length === 1 ? ready[0].organizationPoolId : groups.length === 1 ? groups[0].organizationPoolId : '');
    byId('composerGroup').innerHTML = '<option value="">Chọn nhóm nhận khách</option>' + groups.map(group =>
      '<option value="' + esc(group.organizationPoolId) + '">' + esc(group.name) + (group.ready ? '' : ' · cần thiết lập') + '</option>').join('');
    byId('composerGroup').value = chosen;
  }

  function sync() {
    if (!opened) return;
    root.classList.toggle('composer-vietsub', isVietsub());
    root.querySelector('.composer-heading p').textContent = localOnly()
      ? 'Khách thanh toán, nhận license và dùng Local trên máy. Không cần phân bổ tổ chức.'
      : 'Nhập thông tin gói. Hệ thống tự liên kết nhóm nhận khách.';
    const actions = root.querySelector('.composer-actions'), actionParent = isVietsub() ? root.querySelector('.composer-grid') : byId('composerForm');
    if (actions.parentElement !== actionParent) actionParent.append(actions);
    for (const id of ['composerRights', 'composerChecks', 'composerPreviewCloud']) byId(id).classList.toggle('hidden', !isVietsub());
    byId('composerRouting').classList.toggle('hidden', localOnly());
    root.querySelector('.composer-shared').classList.toggle('hidden', localOnly());
    root.querySelector('.composer-advanced summary small').textContent = localOnly()
      ? 'Mô tả và quyền lợi' : 'Mô tả, quyền lợi và hạn mức AI';
    byId('composerGroup').required = !localOnly() && (!editId || byId('composerPlanStatus').value === 'sale');
    // SePay remains accessible even while the allocation card is hidden.
    if (byId('composerStatus').nextElementSibling !== byId('composerPaymentSetup'))
      byId('composerStatus').after(byId('composerPaymentSetup'));
    byId('composerCloudSettings').classList.toggle('hidden', !cloudEnabled());
    const budgetField = byId('composerMemberBudgetField');
    if (cloudEnabled() && budgetField.parentElement !== byId('composerCloudSettings')) byId('composerCloudSettings').append(budgetField);
    else if (!cloudEnabled() && budgetField.parentElement !== byId('composerBudgetPosition').parentElement) byId('composerBudgetPosition').after(budgetField);
    byId('composerLocalNote').classList.toggle('hidden', cloudEnabled());
    byId('composerMemberBudgetField').classList.toggle('hidden', isVietsub() && !cloudEnabled());
    byId('composerRightsLocked').classList.toggle('hidden', !editId);
    if (!editId && !pending && !sourceId) byId('planComposerTitle').textContent = isVietsub() ? 'Tạo gói tool dịch' : 'Tạo gói & mở bán';
    const group = selected(), quotaBlocked = cloudEnabled() && byId('composerMemberBudget').value.trim() !== '' && Number(byId('composerMemberBudget').value) <= 0;
    const canPublish = !loading && !loadError && !quotaBlocked && setup?.paymentsReady && (localOnly() || group?.ready);
    const wantsSale = !editId || byId('composerPlanStatus').value === 'sale';
    const issue = loading ? (localOnly() ? 'Đang kiểm tra thanh toán…' : 'Đang kiểm tra nhóm nhận khách và thanh toán…') : loadError || (quotaBlocked ? 'Hạn mức mỗi khách bằng 0 sẽ khóa dịch Cloud. Hãy tăng hạn mức hoặc để trống theo ngân sách tổ chức.' : '') ||
      (localOnly() ? (!setup?.paymentsReady ? setup?.paymentMessage : '') : !setup?.groups.length ? 'Chưa có nhóm nhận khách. Bạn có thể lưu nháp và thiết lập nhóm sau.' :
        !group ? 'Chọn nhóm nhận khách để mở bán.' :
          !group.ready ? group.blockingMessage || 'Nhóm chưa sẵn sàng nhận khách.' :
            !setup.paymentsReady ? setup.paymentMessage : '');
    byId('composerStatus').className = 'composer-status' + (canPublish ? ' is-ready' : ' is-pending');
    const statusMarkup = icon(canPublish ? 'status-check' : 'shield-check', canPublish ? 'status-check' : '') + '<div><strong>' +
      (canPublish ? 'Hệ thống sẵn sàng' : 'Cần hoàn tất trước khi mở bán') + '</strong><span>' +
      esc(editId && !wantsSale ? 'Gói không mở bán ở trạng thái đã chọn. Bấm Lưu thay đổi để cập nhật.' : canPublish ? localOnly() ? 'Cấp license trực tiếp sau thanh toán · Không cần phân bổ tổ chức' : isVietsub() ? 'Quyền tool dịch đã chọn · Nhóm còn chỗ · Thanh toán đã bật' : 'AI đã được kiểm tra · Thanh toán đã bật' : issue) + '</span></div>';
    if (byId('composerStatus').innerHTML !== statusMarkup) byId('composerStatus').innerHTML = statusMarkup;
    byId('composerGroupStatus').innerHTML = group?.ready ? icon('status-check', 'status-check') + '<span>Sẵn sàng · Còn ' + esc(group.availableSeats) + ' chỗ</span>' :
      esc(group?.blockingMessage || (loadError ? loadError : loading ? 'Đang tải nhóm nhận khách…' : 'Chọn nhóm để xem điều kiện mở bán.'));
    byId('composerGroupStatus').classList.toggle('is-ready', Boolean(group?.ready));
    byId('composerGroupSetup').classList.toggle('hidden', loading || Boolean(loadError) || Boolean(group?.ready));
    byId('composerPaymentSetup').classList.toggle('hidden', !setup || setup.paymentsReady);
    byId('composerReload').disabled = saving || loading || Boolean(pending);
    byId('composerBack').disabled = saving;
    for (const element of byId('composerForm').querySelectorAll('input, textarea, select')) element.disabled = saving || Boolean(pending) || Boolean(editId && !editorSnapshot);
    if (editId) { byId('composerProduct').disabled = true; byId('composerCloud').disabled = true; }
    byId('composerGroup').disabled = saving || loading || Boolean(pending);
    byId('composerDraft').disabled = saving || Boolean(pending?.publish);
    byId('composerPublish').disabled = saving || (pending ? !editId && !pending.publish : editId ? !editorSnapshot || loading || Boolean(loadError) || wantsSale && !canPublish : !canPublish);
    byId('composerDraft').textContent = saving && !pending?.publish ? 'Đang lưu…' : pending && !pending.publish ? 'Thử lại lưu nháp' : 'Lưu nháp';
    byId('composerPublish').innerHTML = saving && (editId || pending?.publish) ? 'Đang lưu…' : pending && (editId || pending.publish) ? 'Thử lại yêu cầu đã gửi' : editId ? 'Lưu thay đổi ' + icon('check') : 'Tạo và mở bán ' + icon('arrow-right');
    const duration = Number(byId('composerDuration').value), devices = byId('composerDevices').value;
    validatePrice();
    const price = priceValue(), name = byId('composerName').value.trim() || 'Tên gói của bạn';
    byId('composerPreviewName').textContent = name;
    byId('composerPreviewDuration').textContent = isVietsub() ? 'CHỈ TOOL DỊCH' : duration > 0 ? 'GÓI ' + duration + ' NGÀY' : 'GÓI SỬ DỤNG';
    byId('composerPreviewDescription').textContent = byId('composerDescription').value.trim() ||
      (duration > 0 ? 'Quyền sử dụng ' + (isVietsub() ? 'tool dịch / Vietsub' : 'VideoMaker') + ' trong ' + duration + ' ngày.' : 'Nhập thời hạn sử dụng để xem trước.');
    byId('composerPreviewPrice').textContent = Number.isSafeInteger(price) && price > 0 && price <= 1000000000000 ? money.format(price) + ' đ' : 'Chưa nhập giá';
    byId('composerPreviewPeriod').textContent = duration > 0 ? 'cho ' + duration + ' ngày' : '';
    byId('composerPreviewSeats').innerHTML = canPublish && wantsSale ? icon('status-check', 'status-check') + '<span>' + (localOnly() ? 'Kích hoạt trực tiếp · Không cần tổ chức' : 'Còn ' + esc(group.availableSeats) + ' chỗ để đăng ký') + '</span>' : 'Chưa mở bán';
    const items = features();
    byId('composerPreviewFeatures').innerHTML = (items.length ? items : isVietsub() ? defaultFeatures() : [devices + ' thiết bị được kích hoạt', 'Tạo và dựng video bằng AI', 'Tự động kích hoạt sau thanh toán'])
      .map(value => '<li>' + icon('status-check', 'status-check') + '<span>' + esc(value) + '</span></li>').join('');
    byId('composerPreviewCloud').textContent = 'Dịch Cloud: ' + (cloudEnabled() ? 'Bật · theo hạn mức' : 'Tắt');
    const infoReady = byId('composerName').value.trim().length >= 2 && Number.isSafeInteger(price) && price > 0 &&
      Number.isInteger(duration) && duration >= 1 && duration <= 3650;
    byId('composerChecks').classList.toggle('is-ready', Boolean(canPublish && infoReady));
    byId('composerChecks').dataset.state = canPublish && infoReady ? 'Sẵn sàng mở bán' : 'Cần hoàn tất điều kiện mở bán';
    byId('composerMemberBudget').min = cloudEnabled() ? '0.000001' : '0';
    byId('composerCheckItems').innerHTML = [
      [infoReady, 'Thông tin gói hợp lệ'],
      ...(localOnly() ? [[true, 'Cấp license trực tiếp, không phân bổ tổ chức']] : [[Boolean(group?.ready), 'Nhóm còn chỗ nhận khách']]), [Boolean(setup?.paymentsReady), 'Thanh toán SePay sẵn sàng']
    ].map(([ready, text]) => '<li class="' + (ready ? 'is-ready' : '') + '">' + icon(ready ? 'status-check' : 'shield-check', ready ? 'status-check' : '') + '<span>' + text + '</span></li>').join('');
  }

  const optionalNumber = id => id === 'composerPrice' ? priceValue() : byId(id).value === '' ? null : Number(byId(id).value);
  async function submit(event) {
    event.preventDefault();
    if (saving) return;
    const status = byId('composerPlanStatus').value;
    const publish = pending ? pending.publish : editId ? status === 'sale' : event.submitter?.value === 'publish';
    const active = pending ? pending.isActive ?? pending.publish : editId ? ['sale', 'private'].includes(status) : publish;
    if (!pending) {
      byId('composerDuration').required = publish; byId('composerPrice').required = publish;
      byId('composerGroup').required = publish && active && !localOnly();
      byId('composerFeatures').setCustomValidity(features().length > 12 || features().some(item => item.length > 200) ? 'Tối đa 12 quyền lợi, mỗi mục tối đa 200 ký tự.' : '');
      if (!byId('composerForm').reportValidity()) return;
      if (publish && active && (!localOnly() && !selected()?.ready || !setup?.paymentsReady || loading || loadError)) {
        byId('composerMessage').textContent = 'Hãy hoàn tất điều kiện mở bán và kiểm tra lại.'; return;
      }
    }
    const request = pending || {
      creationId, name: byId('composerName').value.trim(), durationDays: optionalNumber('composerDuration'),
      priceVnd: optionalNumber('composerPrice'), maxActivatedDevices: Number(byId('composerDevices').value),
      organizationPoolId: localOnly() ? null : byId('composerGroup').value || null, publish,
      description: byId('composerDescription').value.trim() || null, marketingFeatures: features().length ? features() : null,
      maxConcurrentSessions: Number(byId('composerSessions').value), offlineGraceHours: Number(byId('composerOffline').value),
      displayOrder: Number(byId('composerOrder').value), memberMonthlyAiBudget: optionalNumber('composerMemberBudget'), sourcePlanId: sourceId
    };
    if (!pending && !editId) {
      request.productScope = byId('composerProduct').value;
      if (isVietsub()) {
        request.vietsubCloudEnabled = cloudEnabled();
        if (!cloudEnabled()) request.memberMonthlyAiBudget = 0;
        if (!features().length) request.marketingFeatures = defaultFeatures();
      }
    }
    if (editId && !pending) { request.planId = editId; request.expectedUpdatedAtUtc = editorSnapshot.plan.updatedAtUtc; request.isActive = active;
      request.expectedMappingUpdatedAtUtc = editorSnapshot.mappingUpdatedAtUtc ?? null; }
    const version = epoch, user = identity();
    try { sessionStorage.setItem(storageKey(user), JSON.stringify(request)); }
    catch { byId('composerMessage').textContent = 'Không thể giữ yêu cầu phục hồi trong phiên này. Hãy cho phép bộ nhớ phiên rồi thử lại.'; return; }
    pending = request; saving = true; byId('composerMessage').textContent = ''; sync();
    const ownWrite = new AbortController();
    writeController = ownWrite;
    const writeTimeout = setTimeout(() => ownWrite.abort(), 30000);
    try {
      const result = await api(endpoint, { method: 'POST', body: JSON.stringify(request), signal: ownWrite.signal }, false);
      if (!alive(version, user)) return;
      if (!result?.plan?.licensePlanId) throw new Error('Server chưa trả kết quả lưu gói hợp lệ.');
      sessionStorage.removeItem(storageKey(user)); pending = null; saving = false;
      shell.upsertPlan(result.plan); close();
      toast(result.replayed ? 'Đã khôi phục kết quả lưu gói trước đó.' : editId ? 'Đã lưu thay đổi gói sử dụng.' : result.published ? localOnly() ? 'Đã mở bán gói Local. Khách được cấp license trực tiếp sau thanh toán.' : 'Đã tạo gói và mở bán với nhóm nhận khách đã chọn.' : 'Đã lưu nháp. Gói chưa được cấp hoặc mở bán.');
    } catch (error) {
      if (!alive(version, user)) return;
      if (error.status === 401) { shell.showLogin('Phiên đăng nhập đã hết hạn.'); return; }
      if (error.status >= 400 && error.status < 500) {
        sessionStorage.removeItem(storageKey(user)); pending = null;
        byId('composerMessage').textContent = error.message;
      } else {
        byId('composerMessage').textContent = 'Chưa xác nhận được kết quả lưu. Bấm thử lại để gửi cùng yêu cầu; hệ thống không tạo thêm gói. ' + (error.message || '');
      }
    } finally {
      clearTimeout(writeTimeout);
      if (writeController === ownWrite) writeController = null;
      if (alive(version, user)) { saving = false; sync(); }
    }
  }
  window.videoMakerPlanComposer = Object.freeze({ open, edit: plan => open(plan, true), activate, deactivate: () => { viewActive = false; setComposerVisible(false); }, reset, close });
  function openRequestedLocation() {
    if (!requestedFromLocation || !shell.state.adminReady || !shell.state.accessToken || byId('adminShell').classList.contains('hidden')) return;
    requestedFromLocation = false;
    if (initialLocation.searchParams.get('view') === 'edit-plan' && initialLocation.searchParams.get('planId'))
      open({ licensePlanId: initialLocation.searchParams.get('planId') }, true);
    else open();
  }
  window.addEventListener('videomaker:admin-ready', openRequestedLocation);
  openRequestedLocation();
})();
