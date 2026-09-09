(() => {
  const shell = window.videoMakerAdminShell, rules = window.videoMakerTikTokState;
  if (!shell || !rules) return;
  const { api, escapeHtml: esc, formatDate, icon, toast } = shell;
  const byId = id => document.getElementById(id);
  const root = byId('tiktokAdminConsole'), panel = document.querySelector('[data-panel="tiktok"]');
  const dialog = byId('tiktokCredentialDialog'), form = byId('tiktokCredentialForm');
  const confirmation = byId('tiktokConfirmDialog'), addButton = byId('addTikTokCredentialButton');
  let current = null, loading = null, readController = null;
  let busy = false, dirty = false, baseline = null, generation = 0, revision = 0;
  let pollTimer = null, clockTimer = null, renderedKey = '', checkedAt = null;
  const visible = () => !panel.classList.contains('hidden') && !document.hidden && Boolean(shell.state.accessToken);
  const managed = () => Boolean(current?.adminManagementEnabled);
  const canEdit = () => managed() && Boolean(rules.describe(current).active) && !rules.describe(current).waiting;

  function notify(message, error = false) {
    const element = byId('tiktokNotice');
    if (!element) return;
    element.className = `tt-notice ${error ? 'tt-warning' : 'tt-success'}`;
    element.textContent = message;
    element.hidden = !message;
  }

  function showError(error, target) {
    if (error.status === 401) { shell.showLogin('Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.'); return; }
    const message = error.status === 403 ? 'Tài khoản hiện tại không có quyền quản trị TikTok.' : rules.failureMessage(error.payload?.code);
    if (target) target.textContent = message;
    else notify(message, true);
  }

  function mount() {
    if (byId('tiktokStatusRegion')) return;
    root.innerHTML = `<p id="tiktokNotice" class="tt-notice" role="status" hidden></p><div id="tiktokStatusRegion"></div>
      <div class="tt-workspace"><section class="tt-card" aria-labelledby="tiktokSettingsTitle"><div id="tiktokSettingsRegion"></div></section>
      <section class="tt-card" aria-labelledby="tiktokCredentialsTitle"><div id="tiktokCredentialsRegion"></div></section></div>
      <details class="tt-card tt-help" id="tiktokSetupGuide"><summary>Hướng dẫn thiết lập và thông tin kết nối</summary><div id="tiktokGuideRegion"></div></details>`;
  }

  function render() {
    if (!current) return;
    mount();
    const view = rules.describe(current), key = JSON.stringify([current, view.waiting]);
    const focusedId = document.activeElement?.id;
    if (key !== renderedKey) {
      renderedKey = key;
      const step = view.pending ? 2 : view.active || current.integrationEnabled ? 3 : 1;
      byId('tiktokStatusRegion').innerHTML = `<section class="tt-hero tt-${view.tone}" aria-labelledby="tiktokStatusTitle">
        <div class="tt-hero-icon">${icon(view.waiting ? 'clock' : view.tone === 'success' ? 'circle-check' : 'video-up')}</div>
        <div class="tt-hero-copy"><span class="tt-eyebrow">TRẠNG THÁI KẾT NỐI</span><h3 id="tiktokStatusTitle" tabindex="-1">${esc(view.title)}</h3><p>${esc(view.detail)}</p>
        ${view.waiting ? `<p class="tt-countdown">${icon('clock')} Còn <strong id="tiktokCountdown" aria-live="off"></strong><span>· hết hạn ${esc(formatDate(view.pending.verificationExpiresAtUtc))}</span></p>` : ''}</div>
        <button id="tiktokNextAction" type="button" class="primary-button" data-tt-action="${view.action}" ${busy ? 'disabled' : ''}>${esc(view.actionLabel)}${icon('arrow-right')}</button></section>
      ${view.waiting && view.pending.verificationRequestedByCurrentAdmin ? `<div class="tt-desktop-guide"><strong>Hoàn tất trên VideoMaker Desktop</strong><ol><li>Mở Desktop và đăng nhập bằng <strong>đúng tài khoản Admin này</strong>.</li><li>Vào <strong>Đăng TikTok → Kết nối TikTok</strong>, rồi cấp quyền trong trình duyệt.</li><li>Quay lại đây để xem kết quả. Trang tự kiểm tra mỗi 15 giây khi đang mở.</li></ol>${view.pending.lastTestFailureCode ? `<p class="tt-field-error">${esc(rules.failureMessage(view.pending.lastTestFailureCode))}</p>` : ''}</div>` : ''}
      <ol class="tt-steps" aria-label="Các bước thiết lập TikTok">${['Nhập thông tin ứng dụng', 'Xác minh trên Desktop', 'Quản lý chế độ đăng'].map((label, index) => `<li class="${index + 1 < step ? 'is-done' : index + 1 === step ? 'is-current' : ''}" ${index + 1 === step ? 'aria-current="step"' : ''}><span>${index + 1 < step ? icon('circle-check') : index + 1}</span><div><strong>${label}</strong><small>${index === 0 ? 'Client Key và Client Secret' : index === 1 ? 'Xác nhận kết nối tài khoản' : 'Chỉ mình tôi hoặc công khai'}</small></div></li>`).join('')}</ol>
      <div class="tt-metrics"><article>${icon('users')}<div><strong>${esc(current.connectedUserCount)}</strong><span>Người dùng đã kết nối</span></div></article><article>${icon('users')}<div><strong>${esc(current.connectedAccountCount ?? current.connectedUserCount)}</strong><span>Tài khoản TikTok</span></div></article><article>${icon('video-up')}<div><strong>${esc(current.pendingPublishJobCount)}</strong><span>Bài đang xử lý</span></div></article><article>${icon('shield-check')}<div><strong>${!current.integrationEnabled ? 'Đang tạm tắt' : current.auditedForPublicPosting ? 'Đã xác nhận công khai' : 'Chỉ mình tôi'}</strong><span>Chế độ hoạt động hiện tại</span></div></article></div><p class="tt-updated" id="tiktokCheckedAt"></p>`;
      renderCredentials(view);
      byId('tiktokGuideRegion').innerHTML = `<ol><li>Trong TikTok Developer Portal, chọn ứng dụng của bạn và lấy <strong>Client Key</strong>, <strong>Client Secret</strong>.</li><li>Cấu hình Login Kit Desktop với địa chỉ chuyển hướng dưới đây và quyền <strong>${esc(current.scopes.join(', '))}</strong>.</li><li>Lưu thông tin tại Admin, yêu cầu xác minh và hoàn tất kết nối trên Desktop trong 15 phút.</li></ol>
        <div class="tt-copy-row"><div><span>Địa chỉ chuyển hướng (Redirect URI)</span><code id="tiktokRedirectValue">${esc(current.requiredRedirectUri)}</code></div><button class="ghost-button" type="button" data-tt-action="copy">Sao chép</button></div>
        <p>Thông tin ứng dụng được dùng chung cho VideoMaker. Mỗi người dùng kết nối tài khoản TikTok riêng trên Desktop.</p>
        <div id="tiktokRotationGuide" tabindex="-1"><strong>Khi cần thay thông tin ứng dụng</strong><p>Người dùng ngắt kết nối TikTok trên Desktop; chờ các bài đang xử lý kết thúc, sau đó xác minh bản mới. Bạn không thể ngắt hàng loạt tài khoản từ trang này.</p></div>
        <a href="https://developers.tiktok.com/" target="_blank" rel="noopener noreferrer">Mở TikTok Developer Portal ↗</a>`;
    }
    if (!byId('tiktokSettingsForm') || (!dirty && baseline !== rules.settingsKey(current))) renderSettings();
    updateControls(); updateClock();
    if (focusedId && document.activeElement === document.body && byId(focusedId)) byId(focusedId).focus({ preventScroll: true });
  }

  function credentialCard(item) {
    const labels = { Active: 'Đang sử dụng', Pending: 'Chờ xác minh', Retiring: 'Đang thay thế', Revoked: 'Đã thu hồi' };
    return `<article class="tt-credential"><div class="tt-card-heading"><strong>Phiên bản ${esc(item.version)}</strong><span class="tt-badge tt-${item.status === 'Active' ? 'success' : item.status === 'Pending' ? 'warning' : 'neutral'}">${esc(labels[item.status] || item.status)}</span></div>
      <dl class="tt-secret-hints"><div><dt>Client Key</dt><dd><code>${esc(item.clientKeyHint)}</code></dd></div><div><dt>Client Secret</dt><dd><code>${esc(item.secretHint)}</code></dd></div></dl>
      <p>Tạo ${esc(formatDate(item.createdAtUtc))}${item.activatedAtUtc ? `<br>Đã xác minh ${esc(formatDate(item.activatedAtUtc))}` : ''}${item.lastTestedAtUtc && !item.activatedAtUtc ? `<br>Kiểm tra gần nhất ${esc(formatDate(item.lastTestedAtUtc))}` : ''}</p>
      ${item.lastTestFailureCode ? `<div class="tt-inline-warning"><strong>Lần xác minh gần nhất chưa thành công</strong><p>${esc(rules.failureMessage(item.lastTestFailureCode))}</p><details><summary>Mã lỗi để đối chiếu</summary><code>${esc(item.lastTestFailureCode)}</code></details></div>` : ''}
      ${item.status === 'Pending' ? `<button id="ttRevoke-${esc(item.credentialId)}" type="button" class="tt-danger-link" data-tt-revoke="${esc(item.credentialId)}" ${!managed() || busy ? 'disabled' : ''}>Thu hồi bản đang chờ</button>` : ''}</article>`;
  }

  function renderCredentials(view) {
    const historyOpen = byId('tiktokCredentialHistory')?.open;
    const previous = current.credentials.filter(item => !['Active', 'Pending'].includes(item.status));
    byId('tiktokCredentialsRegion').innerHTML = `<div class="tt-card-heading"><div><span class="tt-eyebrow">ỨNG DỤNG TIKTOK</span><h3 id="tiktokCredentialsTitle">Thông tin kết nối</h3></div>${icon('key')}</div>
      <p class="tt-description">Chỉ hiển thị thông tin đã che. Client Secret đã lưu không thể xem lại.</p>
      ${view.active ? credentialCard(view.active) : '<p class="tt-empty">Chưa có bản đã xác minh trong Admin.</p>'}${view.pending ? credentialCard(view.pending) : ''}
      ${!view.canRotate ? `<div class="tt-inline-warning"><strong>Chưa thể xác minh bản thay thế</strong><p>Còn ${esc(current.connectedAccountCount ?? current.connectedUserCount)} tài khoản kết nối và ${esc(current.pendingPublishJobCount)} bài đang xử lý.</p><button type="button" class="tt-text-button" data-tt-action="guide">Xem cách chuẩn bị thay cấu hình</button></div>` : ''}
      ${previous.length ? `<details id="tiktokCredentialHistory" class="tt-history" ${historyOpen ? 'open' : ''}><summary>Các phiên bản trước (${previous.length})</summary><p>Hiển thị các phiên bản gần đây do server cung cấp.</p>${previous.map(credentialCard).join('')}</details>` : ''}`;
  }

  function renderSettings() {
    dirty = false; baseline = rules.settingsKey(current);
    byId('tiktokSettingsRegion').innerHTML = `<div class="tt-card-heading"><div><span class="tt-eyebrow">QUYỀN SỬ DỤNG</span><h3 id="tiktokSettingsTitle">Cài đặt đăng TikTok</h3></div>${icon('shield-check')}</div>
      <p class="tt-description">Áp dụng cho người dùng VideoMaker sau khi bạn lưu thay đổi.</p><form id="tiktokSettingsForm" novalidate>
      <fieldset id="tiktokSettingsFields"><legend class="tt-sr-only">Chính sách sử dụng TikTok</legend>
        <label class="tt-choice"><input id="tiktokIntegrationEnabled" type="checkbox" ${current.integrationEnabled ? 'checked' : ''} aria-describedby="tiktokEnabledError"/><span><strong>Cho phép sử dụng TikTok</strong><small>Người dùng có thể kết nối tài khoản và bắt đầu đăng video.</small></span></label><p id="tiktokEnabledError" class="tt-field-error"></p>
        <label class="tt-choice"><input id="tiktokAudited" type="checkbox" ${current.auditedForPublicPosting ? 'checked' : ''} /><span><strong>Cho phép đăng công khai</strong><small>Chỉ bật sau khi TikTok đã phê duyệt Content Posting API cho ứng dụng.</small></span></label><p class="tt-policy-explanation" id="tiktokPrivacyHint"></p>
        <div id="tiktokAuditFields" class="tt-audit-fields" ${current.auditedForPublicPosting ? '' : 'hidden'}>
          <label for="tiktokAuditEvidence">Bằng chứng phê duyệt <span aria-hidden="true">*</span></label><input id="tiktokAuditEvidence" maxlength="500" value="${esc(current.auditEvidence || '')}" aria-describedby="tiktokEvidenceHelp tiktokEvidenceError" /><small id="tiktokEvidenceHelp">Mã xét duyệt, ticket hoặc URL trong Developer Portal; 8–500 ký tự.</small><p id="tiktokEvidenceError" class="tt-field-error"></p>
          <label class="tt-choice"><input id="tiktokAuditConfirm" type="checkbox" aria-describedby="tiktokConfirmError"/><span>Tôi xác nhận TikTok đã phê duyệt quyền đăng công khai cho ứng dụng này.</span></label><p id="tiktokConfirmError" class="tt-field-error"></p>
        </div></fieldset><p id="tiktokSettingsBlocker" class="tt-inline-warning" hidden></p><p id="tiktokSettingsMessage" class="tt-form-message" role="status"></p>
        <div class="tt-form-actions"><button id="tiktokSaveSettings" class="primary-button" type="submit">Lưu cài đặt</button><button id="tiktokResetSettings" class="ghost-button" type="button" data-tt-action="reset-settings" hidden>Nạp lại cài đặt</button><span id="tiktokDirtyHint"></span></div></form>`;
  }

  function updateControls() {
    addButton.disabled = !managed() || busy;
    if (!byId('tiktokSettingsFields')) return;
    const conflict = dirty && baseline !== rules.settingsKey(current);
    byId('tiktokSettingsFields').disabled = !canEdit() || busy;
    const enabled = byId('tiktokIntegrationEnabled').checked, audited = byId('tiktokAudited').checked;
    byId('tiktokAudited').disabled = !enabled;
    byId('tiktokAuditFields').hidden = !audited;
    byId('tiktokAuditEvidence').disabled = !audited; byId('tiktokAuditConfirm').disabled = !audited;
    byId('tiktokPrivacyHint').textContent = !enabled ? 'Tắt tính năng sẽ chặn kết nối và bài đăng mới. Bài đã gửi vẫn được server theo dõi.' : audited ? 'Xác nhận tại đây ghi nhận phê duyệt của Admin; không thay thế việc TikTok xét duyệt ứng dụng.' : 'Bài đăng được giới hạn ở Chỉ mình tôi (SELF_ONLY).';
    byId('tiktokSettingsBlocker').hidden = canEdit();
    byId('tiktokSettingsBlocker').textContent = !managed() ? 'Thao tác quản trị đang bị khóa bởi cấu hình môi trường.' : rules.describe(current).waiting ? 'Cài đặt sẽ mở sau khi xác minh thành công. Trên Desktop, đăng nhập đúng tài khoản Admin đã mở phiên, vào Đăng TikTok → Kiểm tra lại → Kết nối TikTok.' : 'Xác minh thông tin ứng dụng trước khi mở cài đặt này.';
    byId('tiktokSaveSettings').disabled = !canEdit() || busy || !dirty || conflict;
    byId('tiktokSaveSettings').textContent = busy ? 'Đang xử lý…' : 'Lưu cài đặt';
    byId('tiktokResetSettings').hidden = !dirty; byId('tiktokResetSettings').disabled = busy;
    byId('tiktokDirtyHint').textContent = conflict ? 'Cấu hình trên server đã đổi. Nạp lại trước khi lưu.' : dirty ? 'Có thay đổi chưa lưu' : 'Đã đồng bộ';
    byId('tiktokDirtyHint').className = conflict ? 'tt-field-error' : '';
    root.querySelectorAll('[data-tt-revoke]').forEach(button => { button.disabled = busy || !managed(); });
    if (byId('tiktokNextAction')) byId('tiktokNextAction').disabled = busy;
    form.querySelectorAll('input, button').forEach(element => { element.disabled = busy; });
  }

  function updateClock() {
    if (!current) return;
    const view = rules.describe(current);
    if (byId('tiktokCountdown')) {
      if (!view.waiting) { renderedKey = ''; render(); void load(); return; }
      byId('tiktokCountdown').textContent = `${Math.floor(view.remaining / 60)}:${String(view.remaining % 60).padStart(2, '0')}`;
    }
    if (checkedAt && byId('tiktokCheckedAt')) byId('tiktokCheckedAt').textContent = `Kiểm tra gần nhất: ${formatDate(checkedAt)} · ${view.waiting ? 'Tự cập nhật khi trang đang mở' : 'Dùng Làm mới để kiểm tra lại'}`;
  }

  async function load() {
    if (busy || !visible()) return;
    if (loading) return loading;
    const stamp = generation, readRevision = revision;
    readController = new AbortController();
    const controller = readController, timeout = window.setTimeout(() => controller.abort(), 20000);
    root.setAttribute('aria-busy', 'true');
    loading = (async () => {
      try {
        const result = await api('/api/admin/tiktok', { signal: controller.signal });
        if (stamp !== generation || readRevision !== revision) return;
        const activated = current?.credentials.some(item => item.status === 'Pending' && result.credentials.some(next => next.credentialId === item.credentialId && next.status === 'Active'));
        current = result; checkedAt = new Date().toISOString(); render();
        notify(activated ? 'Xác minh thành công. Thông tin ứng dụng đã được kích hoạt; bài đăng vẫn giới hạn ở Chỉ mình tôi.' : '');
      } catch (error) {
        if (stamp !== generation || readRevision !== revision) return;
        if (!current) root.innerHTML = '<div class="tt-load-error"><strong>Chưa tải được cấu hình TikTok</strong><p>Kiểm tra kết nối rồi thử lại.</p><button type="button" class="ghost-button" data-tt-action="refresh">Thử lại</button></div>';
        if (current || error.status === 401) showError(error);
      } finally {
        window.clearTimeout(timeout);
        if (stamp === generation) { loading = null; readController = null; root.setAttribute('aria-busy', 'false'); }
      }
    })();
    return loading;
  }

  function ask(title, description, label, dangerous = false) {
    if (confirmation.open || busy) return Promise.resolve(false);
    byId('tiktokConfirmTitle').textContent = title; byId('tiktokConfirmDescription').textContent = description;
    const button = byId('tiktokConfirmAction');
    button.textContent = label; button.className = dangerous ? 'danger-button' : 'primary-button';
    confirmation.returnValue = ''; confirmation.showModal();
    return new Promise(resolve => confirmation.addEventListener('close', () => resolve(confirmation.returnValue === 'proceed'), { once: true }));
  }

  async function mutate(path, options, message, { resetSettings = false, credential = false } = {}) {
    if (busy || !managed()) return false;
    const stamp = generation;
    revision++; busy = true; updateControls(); notify('');
    try {
      const result = await api(path, options);
      if (stamp !== generation) return false;
      current = result; checkedAt = new Date().toISOString();
      if (resetSettings) { dirty = false; baseline = null; }
      if (credential) dialog.close();
      renderedKey = ''; render(); notify(message); toast(message);
      return true;
    } catch (error) {
      if (stamp === generation) showError(error, credential ? form.querySelector('.form-message') : resetSettings ? byId('tiktokSettingsMessage') : null);
      return false;
    } finally {
      if (stamp === generation) { busy = false; updateControls(); }
    }
  }

  function openCredentialDialog() {
    if (!managed() || busy) return;
    form.reset(); form.querySelector('.form-message').textContent = '';
    form.querySelectorAll('.tt-field-error').forEach(element => { element.textContent = ''; });
    form.querySelectorAll('[aria-invalid]').forEach(element => element.removeAttribute('aria-invalid'));
    byId('tiktokReplaceNote').hidden = !rules.describe(current).pending;
    dialog.showModal(); byId('tiktokClientKey').focus();
  }

  async function requestVerification() {
    const view = rules.describe(current);
    if (!managed() || !view.pending || !view.canRotate || view.waiting) return;
    const id = view.pending.credentialId, stamp = generation;
    if (!await ask('Bắt đầu xác minh trên Desktop?', 'Bạn có 15 phút để hoàn tất kết nối bằng đúng tài khoản Admin này trên Desktop. Tính năng TikTok sẽ tạm tắt; nếu phiên hết hạn, hãy kiểm tra lại cài đặt sử dụng.', 'Bắt đầu xác minh')) return;
    if (stamp !== generation || current?.credentials.find(item => item.status === 'Pending')?.credentialId !== id) return;
    const latest = rules.describe(current);
    if (!latest.canRotate || latest.waiting) return;
    await mutate(`/api/admin/tiktok/credentials/${encodeURIComponent(id)}/verification`, { method: 'POST' }, 'Đã mở phiên xác minh. Hãy hoàn tất kết nối trên VideoMaker Desktop.');
  }

  root.addEventListener('input', event => {
    if (!event.target.closest('#tiktokSettingsForm')) return;
    dirty = true; byId('tiktokSettingsMessage').textContent = '';
    if (event.target.id === 'tiktokIntegrationEnabled' && !event.target.checked) {
      byId('tiktokAudited').checked = false; byId('tiktokAuditConfirm').checked = false;
    }
    if (event.target.id === 'tiktokAudited') byId('tiktokAuditConfirm').checked = false;
    updateControls();
  });

  root.addEventListener('submit', async event => {
    if (event.target.id !== 'tiktokSettingsForm') return;
    event.preventDefault();
    if (busy || !dirty || !canEdit() || baseline !== rules.settingsKey(current)) return;
    const audited = byId('tiktokAudited').checked;
    const body = { enabled: byId('tiktokIntegrationEnabled').checked, auditedForPublicPosting: audited,
      confirmAuditApproval: audited && byId('tiktokAuditConfirm').checked,
      auditEvidence: audited ? byId('tiktokAuditEvidence').value.trim() : null };
    const errors = rules.validateSettings(body);
    const fields = [['enabled', 'tiktokEnabledError', 'tiktokIntegrationEnabled'], ['evidence', 'tiktokEvidenceError', 'tiktokAuditEvidence'], ['confirm', 'tiktokConfirmError', 'tiktokAuditConfirm']];
    fields.forEach(([key, errorId, inputId]) => { byId(errorId).textContent = errors[key] || ''; byId(inputId).setAttribute('aria-invalid', errors[key] ? 'true' : 'false'); });
    const first = fields.find(([key]) => errors[key]);
    if (first) { byId(first[2]).focus(); return; }
    if (current.integrationEnabled && !body.enabled) {
      const stamp = generation, key = baseline;
      if (!await ask('Tạm tắt tính năng TikTok?', 'Người dùng sẽ không thể kết nối hoặc bắt đầu bài đăng mới. Các bài đã gửi vẫn được theo dõi. Xác nhận đăng công khai sẽ được tắt cùng cài đặt này.', 'Tắt và lưu', true)) return;
      if (stamp !== generation || key !== rules.settingsKey(current)) return;
    }
    await mutate('/api/admin/tiktok/settings', { method: 'PUT', body: JSON.stringify(body) }, 'Đã lưu cài đặt TikTok.', { resetSettings: true });
  });

  form.addEventListener('submit', async event => {
    event.preventDefault();
    if (busy || !managed()) return;
    let invalid = null;
    for (const id of ['tiktokClientKey', 'tiktokClientSecret']) {
      const field = byId(id), value = field.value.trim();
      const valid = value.length >= 8 && value.length <= field.maxLength && !/[\u0000-\u001f\u007f]/.test(value);
      byId(`${id}Error`).textContent = valid ? '' : `Nhập ${id === 'tiktokClientKey' ? 'Client Key' : 'Client Secret'} từ 8–${field.maxLength} ký tự, không chứa ký tự điều khiển.`;
      field.setAttribute('aria-invalid', valid ? 'false' : 'true');
      if (!valid && !invalid) invalid = field;
    }
    if (invalid) { invalid.focus(); return; }
    const clientKey = byId('tiktokClientKey').value.trim(), clientSecret = byId('tiktokClientSecret').value.trim();
    document.getElementById('tiktokClientKey').value = '';
    document.getElementById('tiktokClientSecret').value = '';
    form.querySelector('.form-message').textContent = 'Đang lưu an toàn…';
    await mutate('/api/admin/tiktok/credentials', { method: 'POST', body: JSON.stringify({ clientKey, clientSecret }) }, 'Đã lưu thông tin ứng dụng. Bước tiếp theo: xác minh trên Desktop.', { credential: true });
  });

  dialog.addEventListener('close', () => { form.reset(); form.querySelector('.form-message').textContent = ''; });
  dialog.addEventListener('cancel', event => { if (busy) event.preventDefault(); });
  addButton.addEventListener('click', openCredentialDialog);
  root.addEventListener('click', async event => {
    const revoke = event.target.closest('[data-tt-revoke]');
    if (revoke && !busy && managed()) {
      const id = revoke.dataset.ttRevoke, stamp = generation;
      if (await ask('Thu hồi bản đang chờ?', 'Bản này sẽ không còn dùng được để xác minh. Cấu hình đã kích hoạt được giữ lại. Hành động thu hồi không thể hoàn tác.', 'Thu hồi bản chờ', true) && stamp === generation)
        await mutate(`/api/admin/tiktok/credentials/${encodeURIComponent(id)}`, { method: 'DELETE' }, 'Đã thu hồi bản đang chờ.');
      return;
    }
    const action = event.target.closest('[data-tt-action]')?.dataset.ttAction;
    if (!action || busy) return;
    if (action === 'refresh') return load();
    if (action === 'credential') return openCredentialDialog();
    if (action === 'verify') return requestVerification();
    if (action === 'settings') { byId('tiktokIntegrationEnabled').focus(); return; }
    if (action === 'guide') { byId('tiktokSetupGuide').open = true; byId('tiktokRotationGuide').focus(); return; }
    if (action === 'reset-settings') {
      const stamp = generation;
      if (await ask('Nạp lại cài đặt từ server?', 'Các thay đổi cài đặt chưa lưu trên trang này sẽ được bỏ.', 'Nạp lại') && stamp === generation) { renderSettings(); updateControls(); }
    }
    if (action === 'copy') {
      try { await navigator.clipboard.writeText(current.requiredRedirectUri); notify('Đã sao chép địa chỉ chuyển hướng.'); }
      catch { notify('Chưa sao chép được. Bạn có thể chọn và sao chép địa chỉ bên dưới.', true); }
    }
  });

  function deactivate() {
    window.clearInterval(pollTimer); window.clearInterval(clockTimer); pollTimer = null; clockTimer = null;
    if (dialog.open) dialog.close();
    if (confirmation.open) confirmation.close();
  }

  function reset() {
    generation++; revision++; deactivate(); readController?.abort();
    current = null; loading = null; busy = false; dirty = false; baseline = null; renderedKey = ''; checkedAt = null;
    form.reset(); addButton.disabled = true;
    root.setAttribute('aria-busy', 'false');
    root.innerHTML = '<div class="tt-loading" role="status">Đang tải cấu hình TikTok…</div>';
  }

  function activate() {
    deactivate();
    if (current) render();
    load();
    pollTimer = window.setInterval(() => { if (visible() && current && rules.describe(current).waiting && !confirmation.open) load(); }, 15000);
    clockTimer = window.setInterval(() => { if (visible()) updateClock(); }, 1000);
  }
  document.addEventListener('visibilitychange', () => { if (visible()) load(); });
  window.addEventListener('focus', () => { if (visible()) load(); });
  window.videoMakerTikTokAdmin = Object.freeze({ activate, deactivate, reset, refresh: load });
})();
