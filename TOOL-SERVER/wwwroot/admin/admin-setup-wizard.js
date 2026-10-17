(() => {
  'use strict';
  const shell = window.videoMakerAdminShell;
  const root = document.getElementById('adminSetupCenter');
  if (!shell || !root) return;
  const { escapeHtml: esc, icon } = shell;
  const base = '/api/admin/setup';
  let bootstrap, draft, readiness, active = false, busy = false, epoch = 0, message = '', pending = null;
  let actor = '', selectedOrganization = null, rates = [], unsavedForm = null, dirty = false;
  let displayStep = null;
  const currentStep = () => displayStep || draft?.step || 1;
  const identity = () => shell.state.user?.userId || shell.state.user?.id || shell.state.user?.email || '';
  const storageKey = () => `vmAdminSetup:${actor}`;
  const alive = (version, user) => epoch === version && actor === user && identity() === user && shell.state.adminReady;
  async function api(url, options = {}, retry = true) {
    const version = epoch, user = actor, controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), url.endsWith('/connections') ? 60000 : 30000);
    try {
      const response = await shell.api(url, { ...options, signal: controller.signal }, options.method && options.method !== 'GET' ? false : retry);
      if (!alive(version, user)) throw new DOMException('Phiên quản trị đã thay đổi.', 'AbortError');
      return response;
    } finally { clearTimeout(timeout); }
  }
  const uuid = () => crypto.randomUUID();
  const path = suffix => `${base}/drafts/${draft.draftId}${suffix || ''}`;
  function remember() {
    // Safe IDs/command only. Form values live on the server; keys never enter browser storage.
    sessionStorage.setItem(storageKey(), JSON.stringify({ draftId: draft?.draftId, pending }));
  }
  function reset() {
    epoch++; actor = ''; bootstrap = draft = readiness = pending = null; rates = []; busy = active = false; message = '';
    unsavedForm = null; dirty = false;
    displayStep = null;
    root.replaceChildren(); document.getElementById('adminShell')?.classList.remove('setup-layout');
  }
  function deactivate() {
    active = false;
    root.querySelectorAll('input[type="password"]').forEach(input => { input.value = ''; });
    document.getElementById('adminShell')?.classList.remove('setup-layout');
  }
  async function activate() {
    if (!shell.state.adminReady || !identity()) return;
    if (actor && actor !== identity()) reset();
    actor = identity(); active = true;
    document.getElementById('adminShell').classList.add('setup-layout');
    shell.setPageMeta('TỔ CHỨC & AI', 'Thiết lập mở bán', 'Cấu hình một lần, dùng cho nhiều gói.');
    render();
    await guarded(async () => {
      bootstrap = await api(`${base}/bootstrap`);
      if (!draft) {
        let saved; try { saved = JSON.parse(sessionStorage.getItem(storageKey()) || '{}'); } catch { saved = {}; }
        if (saved.draftId) {
          try { draft = await api(`${base}/drafts/${saved.draftId}`); pending = saved.pending || null; }
          catch (e) { if (e.status !== 404 && e.status !== 403) throw e; }
        }
      }
      if (draft) { selectedOrganization = draft.organizationId; await loadReadiness(); }
    });
  }
  async function guarded(action) {
    if (busy) return;
    const version = epoch, user = actor; busy = true; message = ''; render();
    try { await action(); }
    catch (error) {
      if (!alive(version, user)) return;
      if (error.status === 401) { shell.showLogin('Phiên đăng nhập đã hết hạn.'); return; }
      message = error.message || 'Không thể hoàn tất thao tác. Hãy thử lại.';
    } finally { if (alive(version, user)) { busy = false; if (active) render(); } }
  }
  async function loadReadiness() {
    const version = epoch, user = actor;
    const next = await api(path('/readiness'));
    if (!alive(version, user)) return;
    readiness = next;
    rates = next.rates.map(rate => dirty ? rates.find(r => r.modelId === rate.modelId && r.usageType === rate.usageType) || { ...rate } : { ...rate });
  }
  async function ensureDraft() {
    if (draft) return;
    let saved; try { saved = JSON.parse(sessionStorage.getItem(storageKey()) || '{}'); } catch { saved = {}; }
    const id = saved.draftId || uuid(); const version = epoch, user = actor;
    sessionStorage.setItem(storageKey(), JSON.stringify({ draftId: id, pending: null }));
    const next = await api(`${base}/drafts`, { method: 'POST', body: JSON.stringify({ draftId: id, organizationId: selectedOrganization }) });
    if (!alive(version, user)) return;
    draft = next; remember();
  }
  function collect() {
    const form = { ...(draft?.form || unsavedForm || { organizationName: '', organizationBudget: 0, memberBudget: null, capacity: 10,
      planName: '', priceVnd: null, durationDays: 30, maxDevices: 1, videoModelId: null }) };
    for (const field of root.querySelectorAll('[data-form]')) {
      const key = field.dataset.form;
      if (field.type === 'number') form[key] = field.value === '' ? null : Number(field.value);
      else if (key === 'marketingFeatures') form[key] = field.value.split('\n').map(x => x.trim()).filter(Boolean);
      else form[key] = field.value || (key === 'videoModelId' ? null : '');
    }
    const mode = root.querySelector('#setupMemberMode');
    if (mode?.value === 'shared') form.memberBudget = null;
    return form;
  }
  async function save(form, step = draft?.step || 1) {
    await ensureDraft();
    const version = epoch, user = actor;
    const request = { revision: draft.revision, step, form };
    let next;
    try { next = await api(path(), { method: 'PUT', body: JSON.stringify(request) }); }
    catch (error) {
      if (error.status >= 400 && error.status < 500) throw error;
      const recovered = await api(path());
      if (JSON.stringify(recovered.form) !== JSON.stringify(form) || recovered.step !== step) throw error;
      next = recovered;
    }
    if (!alive(version, user)) return;
    draft = next; displayStep = null; dirty = false; unsavedForm = null; remember();
  }
  async function command(kind, body = {}, secret = false) {
    const version = epoch, user = actor;
    const request = pending?.kind === kind && pending.body ? { ...pending.body, ...(secret ? { apiKey: body.apiKey } : {}) } : { operationId: uuid(), revision: draft.revision, ...body };
    const { apiKey: transientKey, ...safeRequest } = request;
    pending = { kind, operationId: request.operationId, body: secret ? safeRequest : request, needsKey: secret }; remember();
    try {
      const result = await api(path(`/${kind}`), { method: 'POST', body: JSON.stringify(request) }, false);
      if (!alive(version, user)) return;
      draft = result.draft; pending = null; dirty = false; remember();
      if (result.plan) shell.upsertPlan(result.plan);
      return result;
    } catch (error) {
      if (!alive(version, user)) return;
      if (error.status >= 400 && error.status < 500 && error.payload?.code !== 'operation_running') { pending = null; remember(); }
      else message = 'Chưa xác nhận kết quả. Kiểm tra thao tác trước khi tiếp tục.';
      throw error;
    }
  }
  async function reconcile() {
    const op = await api(path(`/operations/${pending.operationId}`));
    if (op.state === 'Succeeded') { draft = await api(path()); pending = null; remember(); await loadReadiness(); message = 'Đã khôi phục kết quả đã lưu.'; }
    else if (op.state === 'Failed') { pending = null; remember(); await loadReadiness(); message = 'Thao tác chưa được lưu. Có thể nhập lại và kiểm tra kết nối.'; }
    else message = 'Server đang kiểm tra kết nối. Hãy kiểm tra lại sau ít giây.';
  }
  function openSepay() {
    const setupUrl = new URL(window.location.href);
    setupUrl.searchParams.set('view', 'organizations'); setupUrl.searchParams.set('scope', 'setup');
    history.replaceState({ vmSetup: true }, '', setupUrl);
    const paymentUrl = new URL(setupUrl); paymentUrl.searchParams.set('view', 'payments'); paymentUrl.searchParams.delete('scope');
    history.pushState({ vmSetup: true }, '', paymentUrl);
    shell.navigate('payments');
  }
  const input = (key, label, options = '') => `<label>${esc(label)}<input data-form="${key}" ${options} value="${esc((draft?.form || unsavedForm)?.[key] ?? '')}" /></label>`;
  function summary() {
    const models = [...(readiness?.models || bootstrap?.models || []).filter(m => m.modality !== 'Video'),
      ...(bootstrap?.models || []).filter(m => m.modality === 'Video' && m.modelId === (draft?.form || unsavedForm)?.videoModelId)];
    return `<aside class="setup-summary"><section class="setup-card"><h3>AI đã chọn</h3><div class="setup-model-summary">${['Text', 'Image', 'Video'].map(modality => {
      const m = models.find(item => item.modality === modality);
      return `<div>${icon(modality === 'Text' ? 'activity' : modality === 'Image' ? 'monitor' : 'video-up')}<span><strong>${esc(m?.name || 'Chưa cấu hình')}</strong><small>${modality === 'Text' ? 'Viết nội dung' : modality === 'Image' ? 'Tạo ảnh đầu cảnh' : 'Tạo video · 720p · Native Audio'}</small></span></div>`;
    }).join('')}</div></section><section class="setup-card"><h3>Hệ thống tự liên kết</h3><div class="setup-link-flow">Gói → Nhóm nhận khách → Tổ chức</div><p>Khi thanh toán thành công, khách được cấp gói và phân bổ vào tổ chức còn chỗ. Không cần thêm khách thủ công.</p></section><div class="setup-banner">${icon('wallet')}<span>Giá bán gói tính bằng <strong>VND</strong>.<br />Đơn giá AI và ngân sách tính bằng <strong>USD</strong>.<br />Ngân sách là giới hạn chi phí, không phải số tiền khách nạp.</span></div></aside>`;
  }
  function advanced() {
    return `<details class="setup-advanced"><summary>Thiết lập nâng cao ${icon('chevron-down')}</summary>${bootstrap?.drafts?.length ? `<label class="setup-draft-picker">Tiếp tục bản nháp<select id="setupResume"><option value="">Chọn bản nháp đã lưu</option>${bootstrap.drafts.filter(d => !['Published', 'PlanDraftSaved', 'Cancelled'].includes(d.state)).map(d => `<option value="${esc(d.draftId)}">${esc(d.form.organizationName || 'Tổ chức mới')} · Bước ${d.step}</option>`).join('')}</select></label>` : ''}<div><button type="button" class="text-button" data-advanced="pricing">Provider, model & bảng giá</button><button type="button" class="text-button" data-advanced="directory">Thành viên, usage & nhật ký</button><button type="button" class="text-button" data-advanced="pools">Nhóm nhận khách</button><button type="button" class="text-button" data-advanced="cost-guide">Cách tính chi phí</button></div></details>`;
  }

  function correctionButton(check) {
    if (check.ready) return '';
    const labels = { budget: 'Thiết lập ngân sách', member_budget: 'Sửa hạn mức khách', policy: 'Chọn model video',
      allocation: 'Thiết lập nhóm nhận khách', payments: 'Mở cấu hình SePay', configuration_version: 'Tải lại cấu hình' };
    const label = labels[check.code] || (check.code.startsWith('credential_') ? `Nhập key ${check.code.endsWith('openai') ? 'OpenAI' : 'Fal'}`
      : check.code.startsWith('pricing_') ? 'Cấu hình đơn giá' : check.code.startsWith('model_') ? 'Mở cấu hình model'
      : check.code.startsWith('host_') ? 'Mở cấu hình provider' : 'Tải lại trạng thái');
    return `<button type="button" class="readiness-setup-link" data-setup-fix="${esc(check.code)}">${esc(label)} <span aria-hidden="true">→</span></button>`;
  }

  function currentCorrections(step) {
    const checks = (readiness?.checks || []).filter(check => !check.ready && check.step === step);
    return checks.length && step !== 3 ? `<section class="setup-card"><h3>Điều kiện cần hoàn tất</h3><ul class="setup-checks">${checks.map(check => `<li class="blocked">${icon('triangle-alert')}<span>${esc(check.label)}<small>${esc(check.message || '')}</small></span>${correctionButton(check)}</li>`).join('')}</ul></section>` : '';
  }

  async function openCorrection(code) {
    if (busy || pending || !active) return;
    const check = readiness?.checks.find(item => item.code === code);
    if (!check) return;
    const form = collect(), version = epoch, user = actor;
    if (draft) draft.form = form; else unsavedForm = form;
    let step, selector;
    if (code.startsWith('credential_')) { step = 1; selector = `#setupKey-${code.slice('credential_'.length)}`; }
    else if (code.startsWith('pricing_')) {
      const parts = code.split('_'), model = readiness.models.find(item => item.modality === parts[1]);
      const index = rates.findIndex(rate => rate.modelId === model?.modelId && rate.usageType === parts[2]);
      if (index >= 0) { step = 2; selector = `[data-rate="${index}"]`; }
    } else if (['budget', 'member_budget', 'allocation'].includes(code)) {
      step = 2; selector = code === 'budget' ? '[data-form="organizationBudget"]' : code === 'member_budget' ? '#setupMemberMode' : '[data-form="capacity"]';
    } else if (code === 'policy') { step = 1; selector = '[data-form="videoModelId"]'; }
    if (step) {
      displayStep = step; render(); window.videoMakerOrganizationAdmin?.focusSetupTarget(root.querySelector(selector)); return;
    }
    if (code === 'configuration_version') {
      message = 'Cấu hình tổ chức đã thay đổi. Hãy tạo thiết lập mới từ tổ chức hiện tại để rà soát; không ghi đè bằng cấu hình của bản nháp cũ.';
      render(); window.videoMakerOrganizationAdmin?.focusSetupTarget(root.querySelector('[data-new]')); return;
    }
    if (!code.startsWith('model_') && !code.startsWith('host_') && code !== 'payments') return guarded(loadReadiness);
    const snapshot = { form, dirty, rates: rates.map(rate => ({ ...rate })), step: currentStep(), revision: draft?.revision, draftId: draft?.draftId };
    const restore = async () => {
      if (!alive(version, user)) return;
      await shell.navigate('organizations', { organizationScope: 'setup', keepSetupReturn: true });
      if (!alive(version, user)) return;
      await guarded(async () => {
        if (snapshot.draftId) {
          const saved = await api(`${base}/drafts/${snapshot.draftId}`);
          if (saved.revision !== snapshot.revision) throw new Error('Bản nháp đã thay đổi ở nơi khác. Tải lại cấu hình trước khi tiếp tục; nội dung chưa lưu vẫn được giữ.');
          draft = { ...saved, form: snapshot.form };
        } else unsavedForm = snapshot.form;
        dirty = snapshot.dirty; rates = snapshot.rates; displayStep = snapshot.step;
        if (draft) await loadReadiness();
      });
    };
    const modality = code.slice('model_'.length), model = (readiness?.models || bootstrap?.models || []).find(item => item.modality === modality);
    const providerCode = code.startsWith('host_') ? code.slice('host_'.length) : modality === 'Video' ? 'fal' : 'openai';
    const target = code === 'payments' ? 'payments' : code.startsWith('host_') ? 'provider' : model ? 'model' : 'catalog';
    await window.videoMakerOrganizationAdmin.openSetupTarget({ target, organizationId: draft?.organizationId || selectedOrganization,
      providerCode, modelId: model?.modelId }, { returnTo: { label: 'Thiết lập mở bán', restore } });
  }
  function stepOne() {
    const videos = (bootstrap?.models || []).filter(m => m.modality === 'Video');
    return `<section class="setup-card"><h2>Tổ chức vận hành</h2><p>Chọn tổ chức có quyền quản lý hoặc tạo tổ chức mới để giữ kết nối AI và ngân sách.</p>${input('organizationName', 'Tên tổ chức', `maxlength="200" required ${draft?.organizationId ? 'readonly' : ''}`)}<small class="input-help">Tổ chức mới bắt đầu với ngân sách 0 và chưa nhận khách tự động.</small></section>${['openai', 'fal'].map(code => {
      const c = readiness?.connections.find(item => item.providerCode === code);
      return `<section class="setup-card"><h2>${code === 'openai' ? 'OpenAI' : 'Fal / Veo 3.1'}</h2><p>${code === 'openai' ? 'Viết nội dung và tạo ảnh đầu cảnh. Model mặc định áp dụng toàn hệ thống.' : 'Tạo video 720p với Native Audio theo policy của tổ chức.'}</p><span class="status-pill ${c?.active ? 'status-healthy' : 'status-warning'}">${c?.active ? `Đã kết nối · v${c.version} · ${esc(c.hint)}` : 'Chưa kết nối'}</span><div class="setup-connection"><label>API key ${code === 'openai' ? 'OpenAI' : 'Fal'}<input id="setupKey-${code}" type="password" minlength="8" maxlength="10000" autocomplete="new-password" spellcheck="false" /></label><small class="input-help">${shell.aiApiKeyLink(code)}</small><div class="setup-inline-actions"><button type="button" class="ghost-button" data-connect="${code}">${c?.active ? 'Kiểm tra và thay key' : 'Kiểm tra và kết nối'}</button><small class="input-help">Key được kiểm tra rồi mã hóa trên server; không thể xem lại.</small></div></div>${code === 'fal' ? `<label>Model tạo video<select data-form="videoModelId" required><option value="">Chọn model Veo</option>${videos.map(m => `<option value="${esc(m.modelId)}" ${(draft?.form || unsavedForm)?.videoModelId === m.modelId ? 'selected' : ''}>${esc(m.name)}</option>`).join('')}</select></label>${videos.length ? '' : '<small class="input-help warning-text">Fal/model đang tắt. Bật trong Thiết lập nâng cao trước.</small>'}` : ''}</section>`;
    }).join('')}${advanced()}`;
  }
  function rateForm() {
    const models = readiness?.models || [];
    if (!models.length) return '<p>Chưa có model hợp lệ. Hoàn tất Kết nối AI trước.</p>';
    return models.map(m => `<div class="setup-rate-row"><div class="setup-rate-model">${icon(m.modality === 'Text' ? 'activity' : m.modality === 'Image' ? 'monitor' : 'video-up')}<span><strong>${esc(m.name)}</strong><small>${m.modality === 'Text' ? 'Viết nội dung' : m.modality === 'Image' ? 'Tạo ảnh đầu cảnh' : 'Tạo video'}</small></span></div><div class="setup-rate-fields">${rates.map((r, index) => r.modelId === m.modelId ? `<label><small>${r.usageType === 'InputToken' ? 'Đầu vào · USD / triệu token' : r.usageType === 'OutputToken' ? 'Đầu ra · USD / triệu token' : 'Giá video · USD / giây'}</small><input type="number" data-rate="${index}" min="0.00000001" max="1000000" step="0.00000001" required placeholder="Nhập đơn giá" value="${esc(r.price ?? '')}" /></label>` : '').join('')}${m.modality === 'Video' ? '<span class="tag blue setup-variant">720p · Native Audio</span>' : ''}</div></div>`).join('');
  }
  function stepTwo() {
    return `<section class="setup-card"><h2>Đơn giá AI</h2><p>Dùng chung toàn hệ thống. Nhập giá từ provider; giữ giá hiện tại nếu không thay đổi.</p>${rateForm()}</section><section class="setup-card"><h2>Ngân sách & sức chứa</h2><div class="form-grid" style="margin-top:12px">${input('organizationBudget', 'Ngân sách tổ chức / tháng (USD)', 'type="number" min="0" max="100000000" step="0.000001" required')}<label>Hạn mức mỗi khách / tháng (USD)<div class="setup-member-inputs"><select id="setupMemberMode"><option value="limit" ${draft?.form.memberBudget != null ? 'selected' : ''}>Hạn mức riêng</option><option value="shared" ${draft?.form.memberBudget == null ? 'selected' : ''}>Dùng chung</option></select><input aria-label="Hạn mức mỗi khách (USD)" data-form="memberBudget" type="number" min="0" max="100000000" step="0.000001" value="${esc(draft?.form.memberBudget ?? '')}" ${draft?.form.memberBudget == null ? 'disabled' : 'required'} /></div></label>${input('capacity', 'Số khách tối đa', 'type="number" min="1" max="100000" step="1" required')}<label>Nhóm nhận khách<input readonly value="${draft?.poolId ? 'Nhóm đã liên kết với tổ chức' : `Tự tạo cho ${esc(draft?.form.organizationName || 'tổ chức')}`}" /></label></div><small class="input-help warning-text">Ngân sách hoặc hạn mức bằng 0 sẽ khóa AI. Hạn mức khách không thể thay thế ngân sách tổ chức.</small></section>${advanced()}`;
  }
  function stepThree() {
    return `<section class="setup-card"><h2>Tạo gói & mở bán</h2><p>Khách mua gói sẽ được xếp vào nhóm nhận khách của tổ chức đã chọn.</p><div class="form-grid">${input('planName', 'Tên gói', 'maxlength="200" required class="full"')}${input('priceVnd', 'Giá bán (VND)', 'type="number" min="1" max="1000000000000" step="1" required')}${input('durationDays', 'Thời hạn (ngày)', 'type="number" min="1" max="3650" step="1" required')}${input('maxDevices', 'Số thiết bị', 'type="number" min="1" max="1000" step="1" required')}<label class="full">Mô tả<input data-form="description" maxlength="1000" value="${esc(draft?.form.description || '')}" /></label><label class="full">Quyền lợi hiển thị (mỗi dòng một mục)<textarea data-form="marketingFeatures" rows="3">${esc((draft?.form.marketingFeatures || []).join('\n'))}</textarea><small class="input-help">Nội dung hiển thị trên gói; quyền sử dụng được quản lý theo chính sách hiện có.</small></label></div></section><section class="setup-card"><h2>Kiểm tra trước khi mở bán</h2><p>Còn ${esc(readiness?.availableSeats ?? '—')} chỗ · Ngân sách khả dụng ${esc(readiness?.remainingBudget ?? '—')} USD.</p><button type="button" class="ghost-button" data-check>Kiểm tra lại</button><ul class="setup-checks">${(readiness?.checks || []).map(c => `<li class="${c.ready ? '' : 'blocked'}">${icon(c.ready ? 'circle-check' : 'clock')}<span>${esc(c.label)}<small>${esc(c.message || 'Sẵn sàng')}</small></span>${correctionButton(c)}</li>`).join('')}</ul></section><section class="setup-card"><h2>Thanh toán SePay</h2><p>${readiness?.paymentsReady ? 'SePay đã sẵn sàng theo cấu hình hiện tại. Quản lý tài khoản và bật/tắt nhận thanh toán tại trang SePay.' : 'Chưa sẵn sàng nhận thanh toán. Lưu nháp và mở trang SePay để cấu hình.'}</p><button type="button" class="ghost-button" data-sepay>Mở cấu hình SePay ${icon('arrow-right')}</button></section>${advanced()}`;
  }
  function render() {
    if (!active) return;
    if (draft?.state === 'Published' || draft?.state === 'PlanDraftSaved') {
      root.innerHTML = `<div class="setup-wizard"><section class="setup-card setup-success">${icon('circle-check')}<h2>${draft.state === 'Published' ? 'Đã tạo gói & mở bán' : 'Đã lưu gói nháp'}</h2><p>${esc(draft.form.planName)} · ${esc(draft.form.organizationName)}</p><div class="setup-inline-actions"><button type="button" class="primary-button" data-new-plan>Tạo gói khác</button><button type="button" class="ghost-button" data-plans>Xem gói sử dụng</button></div></section></div>`; return;
    }
    const step = currentStep();
    root.innerHTML = `<div class="setup-wizard"><div class="setup-selector"><label>Tổ chức<select id="setupOrganization" ${busy || draft ? 'disabled' : ''}><option value="">Tạo tổ chức mới</option>${(bootstrap?.organizations || []).map(o => `<option value="${esc(o.organizationId)}" ${(draft?.organizationId || selectedOrganization) === o.organizationId ? 'selected' : ''}>${esc(o.name)}</option>`).join('')}${draft?.organizationId && !bootstrap?.organizations?.some(o => o.organizationId === draft.organizationId) ? `<option value="${esc(draft.organizationId)}" selected>${esc(draft.form.organizationName)}</option>` : ''}</select></label><button type="button" class="text-button" data-new ${busy || pending ? 'disabled' : ''}>Thiết lập mới</button></div><ol class="setup-steps" aria-label="Các bước thiết lập">${['Kết nối AI', 'Chi phí & khách hàng', 'Tạo gói & mở bán'].map((label, index) => `<li ${index + 1 === step ? 'aria-current="step"' : ''} class="${index + 1 < step ? 'complete' : ''}"><b>${index + 1 < step ? '✓' : index + 1}</b><span>${label}</span></li>`).join('')}</ol>${pending ? `<div class="setup-banner warning setup-unknown"><span>Chưa xác nhận kết quả thao tác. Kiểm tra trước khi lưu tiếp.</span><button type="button" class="ghost-button" data-reconcile>Kiểm tra thao tác</button>${pending.needsKey ? '<label>Nhập lại key đã gửi<input type="password" id="setupRetryKey" autocomplete="new-password" minlength="8" maxlength="10000" /></label>' : ''}${pending.body ? '<button type="button" class="ghost-button" data-retry>Thử lại cùng yêu cầu</button>' : ''}</div>` : ''}<form id="adminSetupWizardForm" novalidate><fieldset ${busy || pending ? 'disabled' : ''} aria-busy="${busy}" style="border:0;padding:0;margin:0;min-width:0"><div class="setup-columns"><div class="setup-form-column">${step === 1 ? stepOne() : step === 2 ? stepTwo() : stepThree()}${currentCorrections(step)}</div>${summary()}</div><p id="setupWizardMessage" class="form-message" role="alert">${esc(message)}</p>${message && !pending ? '<button type="button" class="text-button" data-reload>Tải lại cấu hình</button>' : ''}<footer class="setup-footer"><button type="button" class="ghost-button" data-back ${step === 1 ? 'disabled' : ''}>${icon('arrow-left')}Quay lại</button><span class="setup-status" role="status">${busy ? 'Đang lưu / kiểm tra…' : dirty ? 'Có thay đổi chưa lưu' : draft ? `Bản nháp đã lưu · Bước ${step}/3` : 'Bắt đầu thiết lập'}</span><button type="button" class="ghost-button setup-save" data-save>Lưu nháp</button>${step === 3 ? `<button type="button" class="ghost-button" data-plan-draft>Lưu gói nháp</button><button type="submit" class="primary-button" ${!readiness?.ready ? 'disabled' : ''}>Tạo gói & mở bán ${icon('arrow-right')}</button>` : `<button type="submit" class="primary-button">Lưu và tiếp tục ${icon('arrow-right')}</button>`}</footer></fieldset></form></div>`;
  }
  function valid() { return root.querySelector('#adminSetupWizardForm')?.reportValidity(); }
  function retainInputs(event) {
    if (event.target.matches('[data-form], #setupMemberMode')) {
      if (draft) draft.form = collect(); else unsavedForm = collect();
      dirty = true;
    }
    if (event.target.matches('[data-rate]')) { rates[Number(event.target.dataset.rate)].price = event.target.value === '' ? null : Number(event.target.value); dirty = true; }
  }
  root.addEventListener('input', retainInputs);
  root.addEventListener('change', event => {
    if (event.target.id === 'setupOrganization') {
      selectedOrganization = event.target.value || null;
      if (selectedOrganization) guarded(async () => { await ensureDraft(); await loadReadiness(); });
    }
    if (event.target.id === 'setupMemberMode') {
      const field = root.querySelector('[data-form="memberBudget"]'); field.disabled = event.target.value === 'shared'; field.required = !field.disabled;
    }
    retainInputs(event);
    if (event.target.matches('[data-form="videoModelId"]')) render();
    if (event.target.id === 'setupResume' && event.target.value) {
      const id = event.target.value;
      guarded(async () => {
        if (dirty && !await shell.confirm('Tải bản nháp đã lưu và bỏ các thay đổi chưa lưu trên form?')) return;
        draft = await api(`${base}/drafts/${id}`); pending = null; dirty = false; remember(); await loadReadiness();
      });
    }
  });
  root.addEventListener('click', event => {
    const button = event.target.closest('button'); if (!button || button.disabled || busy) return;
    const form = collect(), step = currentStep();
    if (button.dataset.setupFix) return openCorrection(button.dataset.setupFix);
    if (button.hasAttribute('data-plans')) return shell.navigate('plans');
    if (button.hasAttribute('data-new') || button.hasAttribute('data-new-plan')) {
      const reuse = button.hasAttribute('data-new-plan');
      return guarded(async () => {
        if (draft && !reuse && !await shell.confirm('Tạo thiết lập mới? Hãy lưu nháp trước nếu cần giữ nội dung đang nhập.')) return;
        selectedOrganization = reuse ? draft?.organizationId : null; draft = readiness = pending = null; rates = []; remember();
        if (reuse) { await ensureDraft(); await save(draft.form, 3); await loadReadiness(); }
      });
    }
    if (button.hasAttribute('data-reconcile')) return guarded(reconcile);
    if (button.hasAttribute('data-retry')) {
      const p = pending, key = root.querySelector('#setupRetryKey')?.value;
      if (p.needsKey && (!key || key.length < 8)) { message = 'Nhập lại key đã gửi để thử lại cùng thao tác.'; render(); return; }
      return guarded(async () => { await command(p.kind, { ...p.body, ...(p.needsKey ? { apiKey: key } : {}) }, p.needsKey); await loadReadiness(); });
    }
    if (pending) return;
    if (button.hasAttribute('data-reload')) return guarded(async () => {
      if (dirty && !await shell.confirm('Tải lại bản mới từ server và bỏ thay đổi chưa lưu?')) return;
      if (draft) draft = await api(path());
      dirty = false; unsavedForm = null; bootstrap = await api(`${base}/bootstrap`);
      if (draft) await loadReadiness();
    });
    if (button.hasAttribute('data-save')) return guarded(async () => { await save(form, step); message = 'Đã lưu bản nháp. Chưa mở bán hoặc bật nhóm mới nhận khách.'; });
    if (button.hasAttribute('data-back')) return guarded(async () => { await save(form, Math.max(1, step - 1)); displayStep = null; await loadReadiness(); });
    if (button.hasAttribute('data-check')) return guarded(async () => { await save(form, step); await loadReadiness(); });
    if (button.hasAttribute('data-sepay')) return guarded(async () => { await save(form, step); openSepay(); });
    if (button.dataset.advanced) return guarded(async () => { await save(form, step); shell.navigate('organizations', { organizationScope: button.dataset.advanced }); });
    if (button.dataset.connect) {
      const code = button.dataset.connect, key = root.querySelector(`#setupKey-${code}`).value;
      if (!key || key.length < 8) { message = 'Nhập API key ít nhất 8 ký tự để kiểm tra.'; render(); return; }
      return guarded(async () => {
        await save(form, step);
        if (!draft.organizationId) await command('organization');
        await command('connections', { providerCode: code, apiKey: key, expectedVersion: readiness?.connections.find(c => c.providerCode === code)?.version || 0 }, true);
        await loadReadiness();
      });
    }
    if (button.hasAttribute('data-plan-draft')) {
      if (!valid()) return;
      return guarded(async () => { await save(form, 3); await command('publish', { publish: false, expectedReadiness: '' }); });
    }
  });
  root.addEventListener('submit', event => {
    event.preventDefault(); if (busy || pending || !valid()) return;
    const form = collect(), step = currentStep();
    const editedRates = rates.map((rate, index) => ({ ...rate, price: Number(root.querySelector(`[data-rate="${index}"]`)?.value ?? rate.price) }));
    guarded(async () => {
      if (step === 2) {
        const changed = editedRates.filter((r, index) => r.price !== readiness.rates[index]?.price);
        if (changed.length && !await shell.confirm(`Thay đổi đơn giá AI toàn hệ thống, ảnh hưởng tất cả tổ chức đang dùng các model này:\n${changed.map(r => `${readiness.models.find(m => m.modelId === r.modelId)?.name} · ${r.usageType}: ${readiness.rates.find(x => x.modelId === r.modelId && x.usageType === r.usageType)?.price ?? 'Chưa có'} → ${r.price} USD / ${r.unit === 'Second' ? 'giây' : '1 triệu token'}`).join('\n')}\nXác nhận lưu đơn giá mới?`)) return;
        await save(form, step);
        await command('pricing', { confirmGlobalChange: changed.length > 0, rates: editedRates });
        await command('configuration'); await loadReadiness();
      } else if (step === 1) {
        await save(form, step);
        if (!draft.organizationId) await command('organization');
        await loadReadiness();
        if (!readiness.connections.every(c => c.active) || !form.videoModelId) throw new Error('Kết nối OpenAI, Fal và chọn model Veo trước khi tiếp tục.');
        if (!['Text', 'Image', 'Video'].every(modality => readiness.models.some(model => model.modality === modality)))
          throw new Error('Bật model nội dung, tạo ảnh và Veo trong Quản lý AI & tổ chức trước khi tiếp tục.');
        await save(form, 2); await loadReadiness();
      } else {
        await save(form, 3); await loadReadiness();
        if (!readiness.ready) throw new Error('Cấu hình chưa sẵn sàng. Hoàn tất các điều kiện còn thiếu trước khi mở bán.');
        if (!await shell.confirm(`Tạo và mở bán “${form.planName}” với giá ${form.priceVnd.toLocaleString('vi-VN')} VND? Khách thanh toán sẽ được tự động xếp vào tổ chức.`)) return;
        await command('publish', { publish: true, expectedReadiness: readiness.fingerprint });
      }
    });
  });
  window.videoMakerAdminSetup = Object.freeze({ activate, deactivate, reset, build: 'admin-setup-20261014.1' });
  window.addEventListener('popstate', event => {
    if (!event.state?.vmSetup || !shell.state.adminReady) return;
    const url = new URL(window.location.href);
    if (url.searchParams.get('view') === 'payments') shell.navigate('payments');
    else shell.navigate('organizations', { organizationScope: 'setup' });
  });
  const location = new URL(window.location.href);
  window.addEventListener('videomaker:admin-ready', () => {
    if (location.searchParams.get('view') === 'organizations' || location.searchParams.get('view') === 'setup')
      shell.navigate('organizations', { organizationScope: location.searchParams.get('scope') || 'setup' });
    else if (location.searchParams.get('view') === 'payments') shell.navigate('payments');
  }, { once: true });
})();
