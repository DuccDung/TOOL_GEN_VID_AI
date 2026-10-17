(() => {
  'use strict';
  const shell = window.videoMakerAdminShell;
  if (!shell || !document.getElementById('releaseDraftDialog')) return;
  const { api, escapeHtml: esc, formatDate, toast } = shell;
  const byId = id => document.getElementById(id);
  let endpoint = '/api/admin/desktop-release-preparations';
  const setupEndpoint = '/api/admin/desktop-bootstrappers';
  const dialog = byId('releaseDraftDialog'), form = byId('releaseDraftForm');
  const terminal = new Set(['Ready', 'Failed', 'Cancelled', 'Published']);
  const labels = { Draft: 'Đã lưu nháp', Queued: 'Đang chờ', Downloading: 'Đang tải gói', Inspecting: 'Đang kiểm tra', Ready: 'Kiểm tra đạt', Failed: 'Kiểm tra thất bại', Cancelled: 'Đã hủy', Published: 'Đã phát hành' };
  let current = null, requestId = null, uncertainCreate = null, epoch = 0, timer = null, pollController = null;
  let busy = false, conflict = false, active = false, listEpoch = 0, listTimer = null;
  let page = 1, pageSize = 20, catalog = [], catalogEpoch = 0, setupBusy = false, sessionEpoch = 0, configuration = null, configurationPromise = null;
  const standalone = () => configuration?.publicationMode === 'StandaloneCatalog';
  const direct = () => standalone() || configuration?.publicationMode === 'DirectCatalog';
  const configuredEndpoint = () => standalone() ? '/api/admin/desktop-standalone-releases' : direct() ? '/api/admin/desktop-direct-releases' : '/api/admin/desktop-release-preparations';
  const normalize = item => direct() && item ? { ...item, preparationId: item.releaseId, published: item.published || item.status === 'Published' } : item;
  const storageKey = () => 'vmReleasePreparationRequest:' + (shell.state.user?.userId || shell.state.user?.email || 'admin');
  const guid = () => crypto.randomUUID();
  const fields = ['releaseDraftVersion', 'releaseDraftUrl', 'releaseDraftNotes', 'releaseDraftMandatory'];
  const values = () => ({ version: byId('releaseDraftVersion').value.trim(), downloadUrl: byId('releaseDraftUrl').value.trim(),
    releaseNotes: byId('releaseDraftNotes').value || null, isMandatory: byId('releaseDraftMandatory').checked });
  const sameIdentity = () => current && values().version === current.version && values().downloadUrl === current.downloadUrl;
  const sameMetadata = () => current && (values().releaseNotes || '') === (current.releaseNotes || '') && values().isMandatory === current.isMandatory;
  const published = () => Boolean(current?.published || current?.status === 'Published');
  const statusLabel = item => item.published || item.status === 'Published' ? item.isActive ? 'Đang phân phối' : 'Đã ngừng phân phối' : labels[item.status] || 'Đang xác minh trạng thái';
  const pending = () => !direct() && current && !terminal.has(current.status);
  const bytes = value => {
    let number = Number(value || 0), unit = 0;
    const units = ['B', 'KB', 'MB', 'GB'];
    while (number >= 1024 && unit < units.length - 1) { number /= 1024; unit++; }
    return number.toFixed(unit ? 1 : 0) + ' ' + units[unit];
  };

  function stopPolling() {
    clearTimeout(timer); timer = null;
    pollController?.abort(); pollController = null;
  }
  function renderMode() {
    byId('manageBootstrapperButton').hidden = standalone();
    byId('releaseTable').hidden = standalone();
    byId('releaseCount').hidden = standalone();
    byId('releaseWorkspaceNotice').textContent = direct() ? 'Nhập phiên bản và link ZIP do Publish tạo. Setup tải và kiểm tra gói trên máy cài.' : 'Nhập phiên bản và link ZIP. Hệ thống kiểm tra gói trước khi cho phép phát hành.';
    byId('releasePreparationsTitle').textContent = direct() ? 'Bản nháp và phát hành' : 'Bản nháp và kiểm tra gói';
  }
  async function ensureConfiguration() {
    if (configuration) return configuration;
    if (!configurationPromise) {
      const expectedSession = sessionEpoch;
      configurationPromise = api(`${setupEndpoint}/status`).catch(error => {
        // Servers predating the mode endpoint can only use the inspection UI.
        // Network/server failures never select another publication mode.
        if (error.status === 404) return { publicationMode: 'ServerInspection' };
        throw error;
      }).then(result => {
        if (expectedSession !== sessionEpoch) return null;
        configuration = result;
        endpoint = configuredEndpoint();
        renderMode();
        return result;
      }).finally(() => { if (expectedSession === sessionEpoch) configurationPromise = null; });
    }
    return configurationPromise;
  }
  function clearErrors() {
    byId('releaseDraftErrors').hidden = true;
    byId('releaseDraftErrors').replaceChildren();
    for (const id of ['releaseDraftVersion', 'releaseDraftUrl']) {
      byId(id).removeAttribute('aria-invalid');
      byId(id + 'Error').hidden = true; byId(id + 'Error').textContent = '';
    }
  }
  function showError(message, fieldId) {
    clearErrors();
    const summary = byId('releaseDraftErrors');
    const node = document.createElement(fieldId ? 'a' : 'span'); node.textContent = message;
    if (fieldId) {
      node.href = '#' + fieldId;
      node.addEventListener('click', event => { event.preventDefault(); byId(fieldId).focus(); });
      byId(fieldId).setAttribute('aria-invalid', 'true');
      byId(fieldId + 'Error').textContent = message; byId(fieldId + 'Error').hidden = false;
    }
    summary.appendChild(node); summary.hidden = false;
    if (dialog.open) summary.focus();
  }
  function validate() {
    const data = values();
    if (!/^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/.test(data.version)) {
      showError('Nhập phiên bản theo dạng major.minor.patch, ví dụ 1.2.0.', 'releaseDraftVersion'); return false;
    }
    let url;
    try { url = new URL(data.downloadUrl); } catch { /* Report one actionable field error below. */ }
    if (!url || url.protocol !== 'https:' || url.username || url.password || url.search || url.hash || data.downloadUrl.length > 2048) {
      showError('Nhập link ZIP trực tiếp dùng HTTPS, không chứa tài khoản, query hoặc fragment.', 'releaseDraftUrl'); return false;
    }
    if (direct()) {
      const identity = /^VideoMaker-((?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*))-([1-9]\d*)-win-x64-([a-f0-9]{64})\.zip$/.exec(url.pathname.split('/').pop());
      if (!identity || identity[1] !== data.version || Number(identity[2]) > 2147483647) {
        showError('Dùng link ZIP do Publish tạo, có phiên bản, build và checksum trong tên file; phiên bản phải khớp ô đã nhập.', 'releaseDraftUrl'); return false;
      }
    }
    return true;
  }
  function controls() {
    const dirty = current && (!sameIdentity() || !sameMetadata());
    fields.forEach(id => { byId(id).disabled = busy || (published() && ['releaseDraftVersion', 'releaseDraftUrl'].includes(id)); });
    byId('releaseDraftSave').disabled = busy || (published() && !dirty) || conflict;
    byId('releaseDraftSave').textContent = busy ? 'Đang lưu…' : 'Lưu nháp';
    const ready = (direct() ? current?.status === 'Draft' || (published() && !current.isActive) : current?.status === 'Ready' || (published() && !current.isActive)) && current?.canPublish && !dirty && !conflict;
    byId('releaseDraftPublish').disabled = busy || !ready;
    byId('releaseDraftPublish').textContent = published() ? 'Phát hành lại' : 'Phát hành';
    byId('releaseDraftDeactivate').hidden = !published() || !current.isActive;
    byId('releaseDraftDeactivate').disabled = busy || conflict || Boolean(dirty);
    byId('releaseDraftCancel').hidden = !pending(); byId('releaseDraftCancel').disabled = busy || conflict;
    byId('releaseDraftRetry').hidden = direct() || !current || !['Failed', 'Cancelled'].includes(current.status);
    byId('releaseDraftRetry').disabled = busy || conflict || Boolean(dirty);
    byId('releaseDraftReload').hidden = !conflict;
    byId('releaseDraftReload').disabled = busy;
    byId('releaseDraftSetupLink').hidden = standalone() || !(current && !current.bootstrapper && !published());
    let hint = direct() ? 'Lưu thông tin phiên bản. Setup sẽ tải và kiểm tra gói trên máy cài.' : 'Lưu nháp để bắt đầu kiểm tra gói.';
    if (published()) hint = dirty ? 'Lưu thay đổi ghi chú hoặc chính sách cập nhật. Phiên bản và link ZIP được giữ cố định.' : current.isActive ? 'Phiên bản đang được phân phối. Có thể sửa ghi chú hoặc ngừng phát hành; thay gói cần tạo phiên bản mới.' : ready ? 'Phiên bản đã ngừng phân phối. Có thể phát hành lại cùng gói đã kiểm tra.' : current.errorMessage || 'Cần Setup chung tương thích và cấu hình hợp lệ trước khi phát hành lại.';
    else if (conflict) hint = 'Dữ liệu đã thay đổi ở phiên khác. Tải trạng thái mới trước khi tiếp tục.';
    else if (dirty) hint = direct() ? 'Có thay đổi chưa lưu. Lưu nháp trước khi phát hành.' : 'Có thay đổi chưa lưu. Lưu nháp và chờ kiểm tra đạt trước khi phát hành.';
    else if (pending()) hint = 'Hệ thống đang kiểm tra. Bạn có thể đóng cửa sổ và mở lại sau.';
    else if (current?.status === 'Ready' && !current.canPublish) hint = current.errorMessage || 'Gói đã kiểm tra, nhưng còn thiếu cấu hình phát hành. Kiểm tra Setup dùng chung.';
    else if (ready) hint = direct() ? 'Thông tin đã lưu. Có thể phát hành; Setup sẽ kiểm tra file khi cài.' : 'Gói đã kiểm tra và Setup dùng chung tương thích. Có thể phát hành.';
    else if (current) hint = direct() ? current.errorMessage || 'Cần Setup dùng chung từ 1.2.0 hỗ trợ cài trực tiếp trước khi phát hành.' : 'Hoàn tất kiểm tra gói trước khi phát hành.';
    if (direct() && published()) hint = dirty ? 'Lưu ghi chú hoặc chính sách cập nhật. Phiên bản và link ZIP được giữ cố định.' : current.isActive ? 'Thông tin phiên bản đang được phân phối. Setup tải và kiểm tra gói trên máy cài.' : ready ? 'Có thể phân phối lại cùng thông tin phiên bản.' : current.errorMessage || 'Cần Setup dùng chung từ 1.2.0 tương thích.';
    if (standalone() && current && !dirty && !conflict && !ready && !current.isActive) hint = current.errorMessage || 'Kiểm tra cấu hình API và nguồn VPS trước khi phát hành.';
    byId('releaseDraftHelp').textContent = direct() ? 'Nhập phiên bản và link ZIP do Publish tạo. Server lưu thông tin; Setup tải và kiểm tra ứng dụng, model trên máy cài.' : 'Setup dùng chung sẽ tải phiên bản đã phát hành. Bạn chỉ cần cung cấp phiên bản và link ZIP ứng dụng.';
    byId('releaseDraftPublishHint').textContent = hint;
    form.setAttribute('aria-busy', String(busy));
  }
  function renderCurrent() {
    const inspection = byId('releaseDraftInspection'); inspection.hidden = !current;
    if (!current) { controls(); return; }
    const stateLabel = statusLabel(current);
    byId('releaseDraftStatusTitle').textContent = direct() ? 'Thông tin phát hành' : 'Kiểm tra gói ứng dụng';
    byId('releaseDraftStatus').textContent = stateLabel;
    byId('releaseDraftStatus').className = 'status-pill ' + (['Ready', 'Published'].includes(current.status) ? 'status-healthy' : current.status === 'Failed' ? 'status-failed' : 'status-warning');
    const total = Number(current.totalBytes || 0), received = Number(current.bytesReceived || 0);
    const progress = byId('releaseDraftProgress');
    if (['Ready', 'Published'].includes(current.status)) progress.value = 100;
    else if (total > 0 && current.status === 'Downloading') progress.value = Math.min(99, received / total * 100);
    else progress.removeAttribute('value');
    progress.hidden = direct() || ['Failed', 'Cancelled'].includes(current.status);
    let text = stateLabel + '.';
    if (current.status === 'Downloading') text += ' ' + bytes(received) + (total ? ' / ' + bytes(total) : ' đã tải');
    if (byId('releaseDraftProgressText').textContent !== text) byId('releaseDraftProgressText').textContent = text;
    byId('releaseDraftReason').textContent = current.errorMessage || (direct() ? 'Server không tải hoặc kiểm ZIP. Setup kiểm file trước khi cài trên máy đích.' : pending() ? 'Tiến trình chạy trên server, đóng cửa sổ sẽ không hủy kiểm tra.' : '');
    const facts = [ ['Tên file', current.fileName], ['Phiên bản', current.version], ['Build', current.buildNumber],
      ['Dung lượng', current.sizeBytes ? bytes(current.sizeBytes) : null], ['SHA-256', current.sha256],
      ['Setup dùng chung', current.bootstrapper ? current.bootstrapper.fileName + ' · ' + current.bootstrapper.version : null], ['Cập nhật lúc', formatDate(current.updatedAtUtc)] ];
    byId('releaseDraftFacts').innerHTML = facts.filter(([, value]) => value !== null && value !== undefined && value !== '').map(([name, value]) => `<div><dt>${esc(name)}</dt><dd>${esc(value)}</dd></div>`).join('');
    byId('releaseDraftTechnical').hidden = direct() || !current.sha256;
    controls();
  }
  function fill(data) {
    current = normalize(data); data = current;
    byId('releaseDraftVersion').value = data.version || '';
    byId('releaseDraftUrl').value = data.downloadUrl || '';
    byId('releaseDraftNotes').value = data.releaseNotes || '';
    byId('releaseDraftMandatory').checked = Boolean(data.isMandatory);
    renderCurrent();
    if (data.status === 'Failed' && data.errorMessage && /version/i.test(data.errorCode || '')) showError(data.errorMessage, 'releaseDraftVersion');
  }
  async function rememberRequest(data) {
    // Persist a digest and UUID only. A different new draft must not inherit an uncertain request's UUID.
    const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(JSON.stringify(data)));
    const fingerprint = [...new Uint8Array(digest)].map(value => value.toString(16).padStart(2, '0')).join('');
    let saved = null;
    try { saved = JSON.parse(sessionStorage.getItem(storageKey()) || 'null'); } catch { /* Retain the in-memory operation when storage is unavailable. */ }
    requestId = saved?.fingerprint === fingerprint ? saved.requestId : uncertainCreate?.fingerprint === fingerprint ? uncertainCreate.requestId : guid();
    uncertainCreate = { requestId, fingerprint };
    try { sessionStorage.setItem(storageKey(), JSON.stringify(uncertainCreate)); } catch { /* Keep the same operation in memory. */ }
  }
  function forgetRequest() {
    uncertainCreate = null;
    try { sessionStorage.removeItem(storageKey()); } catch { /* No application state depends on browser storage. */ }
  }
  function schedulePoll() {
    stopPolling();
    if (!dialog.open || !current || !pending() || conflict) return;
    const expectedEpoch = epoch, id = current.preparationId, generation = current.generation;
    timer = setTimeout(async () => {
      if (!dialog.open || epoch !== expectedEpoch || busy) return schedulePoll();
      const controller = new AbortController(); pollController = controller;
      try {
        const result = await api(`${endpoint}/${encodeURIComponent(id)}`, { signal: controller.signal });
        if (controller.signal.aborted || !dialog.open || epoch !== expectedEpoch || current?.preparationId !== id || result.generation < generation) return;
        current = result; renderCurrent();
        if (result.status === 'Failed' && result.errorMessage && /version/i.test(result.errorCode || '')) showError(result.errorMessage, 'releaseDraftVersion');
        if (terminal.has(result.status)) { await refreshList(); if (result.releaseId) await shell.refreshReleases(); }
      } catch (error) {
        if (error.name !== 'AbortError' && epoch === expectedEpoch && dialog.open) byId('releaseDraftMessage').textContent = 'Chưa lấy được tiến độ. Hệ thống sẽ thử lại; bản nháp vẫn được giữ trên server.';
      } finally { if (epoch === expectedEpoch && dialog.open) schedulePoll(); }
    }, 1200);
  }
  async function open(id = null) {
    if (busy) { toast('Yêu cầu đang được lưu. Vui lòng chờ kết quả.', true); return; }
    stopPolling(); epoch++; const expectedEpoch = epoch, expectedSession = sessionEpoch;
    try { await ensureConfiguration(); }
    catch (error) { if (expectedSession === sessionEpoch) toast(error.message, true); return; }
    if (expectedEpoch !== epoch || expectedSession !== sessionEpoch) return;
    form.reset(); current = null; conflict = false; clearErrors();
    byId('releaseDraftMessage').textContent = ''; byId('releaseDraftTechnical').open = false;
    byId('releaseDraftTitle').textContent = id ? 'Chi tiết phiên bản' : 'Tạo phiên bản';
    requestId = guid();
    controls(); renderCurrent(); dialog.showModal();
    if (!id) { byId('releaseDraftVersion').focus(); return; }
    busy = true; controls();
    try {
      const data = await api(`${endpoint}/${encodeURIComponent(id)}`);
      if (epoch !== expectedEpoch) return;
      fill(data);
    } catch (error) { if (epoch === expectedEpoch) showError(error.message); }
    finally {
      if (expectedSession === sessionEpoch) busy = false;
      if (epoch === expectedEpoch) { controls(); schedulePoll(); }
    }
  }
  async function openForRelease(releaseId) {
    const expectedSession = sessionEpoch;
    try {
      await ensureConfiguration();
      const result = await api(direct() ? `${endpoint}/${encodeURIComponent(releaseId)}` : `${endpoint}/by-release/${encodeURIComponent(releaseId)}`);
      if (expectedSession !== sessionEpoch || !shell.state.adminReady) return;
      await open(normalize(result).preparationId);
    } catch (error) { toast(error.message, true); }
  }
  async function mutation(action) {
    if (busy || conflict || (action === 'save' && !validate())) return;
    if (action !== 'save' && !current) return;
    if (action === 'publish' && byId('releaseDraftPublish').disabled) return;
    if (action === 'retry' && (!sameIdentity() || !sameMetadata())) { showError('Lưu các thay đổi trước khi kiểm tra lại.'); return; }
    const expectedEpoch = epoch, expectedSession = sessionEpoch, id = current?.preparationId;
    const input = values();
    const data = action === 'save' ? { ...input, requestId, revision: current?.revision || null } : { revision: current.revision };
    const path = action === 'save' ? id ? `${endpoint}/${encodeURIComponent(id)}${standalone() ? '/save' : ''}` : endpoint : `${endpoint}/${encodeURIComponent(id)}/${action}`;
    busy = true; stopPolling(); clearErrors(); byId('releaseDraftMessage').textContent = ''; controls();
    try {
      if (action === 'save' && !id) { await rememberRequest(input); data.requestId = requestId; }
      if (expectedSession !== sessionEpoch) return;
      const result = await api(path, { method: action === 'save' && id && !standalone() ? 'PUT' : 'POST', body: JSON.stringify(data) });
      if (expectedSession !== sessionEpoch) return;
      if (action === 'save' && !id) forgetRequest();
      if (epoch === expectedEpoch) {
        current = normalize(result); conflict = false;
        if (action === 'save') fill(result); else renderCurrent();
        byId('releaseDraftMessage').textContent = action === 'publish' ? direct() ? 'Đã phát hành thông tin phiên bản.' : 'Đã phát hành phiên bản.' : action === 'deactivate' ? 'Đã ngừng phân phối phiên bản.' : action === 'cancel' ? result.status === 'Cancelled' ? 'Server đã xác nhận hủy kiểm tra.' : 'Đã gửi yêu cầu hủy. Đang chờ server xác nhận.' : 'Đã lưu bản nháp trên server.';
      }
      await refreshList();
      if (action === 'publish') { if (!standalone()) await shell.refreshReleases(); toast(direct() ? 'Đã phát hành thông tin phiên bản.' : 'Đã phát hành phiên bản.'); }
      if (action === 'deactivate') { if (!standalone()) await shell.refreshReleases(); toast('Đã ngừng phân phối phiên bản.'); }
    } catch (error) {
      if (epoch === expectedEpoch && dialog.open) {
        conflict = error.status === 409 && /revision|conflict|stale|changed/i.test(error.payload?.code || '');
        const code = error.payload?.code || '';
        const field = /version/i.test(code) ? 'releaseDraftVersion' : /url|source|prefix/i.test(code) ? 'releaseDraftUrl' : null;
        showError(error.message, field);
        if (conflict) stopPolling();
      }
    } finally {
      if (expectedSession === sessionEpoch) busy = false;
      if (epoch === expectedEpoch) { controls(); schedulePoll(); }
    }
  }
  async function refreshList() {
    const expected = ++listEpoch;
    byId('releasePreparationListMessage').textContent = 'Đang tải bản nháp…';
    try {
      await ensureConfiguration();
      if (expected !== listEpoch) return;
      const result = await api(`${endpoint}/page?page=${page}&pageSize=${pageSize}`);
      if (expected !== listEpoch) return;
      const items = (result.items || []).map(normalize);
      byId('releasePreparationListMessage').textContent = '';
      byId('releasePreparationList').innerHTML = items.length ? `<div class="release-preparation-list">${items.map(item => `<article class="release-preparation-entry"><div class="surface-heading"><h4>${esc(item.version)}</h4><span class="status-pill ${['Ready', 'Published'].includes(item.status) ? 'status-healthy' : item.status === 'Failed' ? 'status-failed' : 'status-warning'}">${esc(statusLabel(item))}</span></div><p class="release-preparation-url">${esc(item.downloadUrl)}</p>${item.errorMessage ? `<p class="form-message">${esc(item.errorMessage)}</p>` : ''}<div class="inline-actions"><small>${esc(formatDate(item.updatedAtUtc))}</small><button class="ghost-button" type="button" data-open-preparation="${esc(item.preparationId)}">${item.published ? 'Xem phiên bản' : 'Mở bản nháp'}</button></div></article>`).join('')}</div>${shell.paginationMarkup(result, 'release-preparations', 'bản nháp')}` : '<p class="empty-state">Chưa có bản nháp. Chọn Thêm phiên bản để bắt đầu.</p>';
      clearTimeout(listTimer);
      if (!direct() && active && items.some(item => !terminal.has(item.status))) listTimer = setTimeout(refreshList, 5000);
    } catch (error) {
      if (expected !== listEpoch) return;
      byId('releasePreparationListMessage').textContent = error.status === 404 ? 'Server chưa hỗ trợ form phát hành mới. Cần deploy server và migration tương ứng.' : error.message;
    }
  }
  function renderCatalog() {
    const activeSetup = catalog.find(item => item.isActive);
    let status = activeSetup ? `Đang dùng: ${activeSetup.bootstrapper.fileName} · ${activeSetup.bootstrapper.version}.` : 'Chưa có Setup dùng chung đang hoạt động.';
    if (configuration && (!configuration.sharedBootstrapperEnabled || (!direct() && !configuration.releasePreparationEnabled))) status += ' Tính năng phát hành bằng Setup chung đang tắt trong cấu hình server.';
    if (configuration && !configuration.configured) status += ' Server chưa đủ cấu hình địa chỉ API hoặc nguồn tải ứng dụng.';
    byId('bootstrapperStatus').textContent = status;
    byId('bootstrapperList').innerHTML = catalog.map(item => {
      const setup = item.bootstrapper;
      const compatible = !direct() || (setup.supportedCatalogProtocols || '3').split(',').includes('4');
      let download = null;
      try { const parsed = new URL(setup.downloadUrl, location.href); if (parsed.origin === location.origin && parsed.protocol === 'https:') download = parsed.href; } catch { /* Invalid links never become clickable. */ }
      return `<article class="release-preparation-entry"><div class="surface-heading"><h3>${esc(setup.fileName)}</h3><span class="status-pill ${item.isRevoked ? 'status-failed' : item.isActive ? 'status-healthy' : ''}">${item.isRevoked ? 'Đã thu hồi' : item.isActive ? 'Đang hoạt động' : 'Đã đăng ký'}</span></div><p>Phiên bản ${esc(setup.version)} · ${esc(setup.platform)} · ${esc(setup.channel)}</p><p class="release-preparation-url">API: ${esc(setup.apiBaseUrl)}</p><p class="input-help">Giao thức ${esc(setup.distributionProtocol)} · ${esc(setup.profile)} · ${esc(setup.modelBundleId)}${compatible ? '' : ' · Cần Setup từ 1.2.0 hỗ trợ cài trực tiếp.'}</p><div class="inline-actions">${download && !item.isRevoked ? `<a class="ghost-button" href="${esc(download)}" download>Tải Setup</a>` : ''}${!item.isActive && !item.isRevoked && compatible ? `<button class="primary-button" type="button" data-activate-bootstrapper="${esc(setup.bootstrapperId)}">Kích hoạt</button>` : ''}</div></article>`;
    }).join('');
  }
  async function loadCatalog() {
    const expected = ++catalogEpoch;
    byId('bootstrapperStatus').textContent = 'Đang tải danh sách Setup…';
    try {
      const [result, status] = await Promise.all([api(setupEndpoint), api(`${setupEndpoint}/status`)]);
      if (expected !== catalogEpoch) return;
      catalog = result; configuration = status; endpoint = configuredEndpoint(); renderMode(); renderCatalog();
    } catch (error) { if (expected === catalogEpoch) byId('bootstrapperStatus').textContent = error.message; }
  }
  async function openSetup() {
    byId('bootstrapperMessage').textContent = '';
    byId('bootstrapperDialog').showModal(); await loadCatalog();
  }
  function reset() {
    active = false; epoch++; listEpoch++; catalogEpoch++; sessionEpoch++; stopPolling(); clearTimeout(listTimer);
    current = null; conflict = false; busy = false; setupBusy = false; configuration = null; configurationPromise = null; uncertainCreate = null;
    form.reset(); clearErrors(); byId('releaseDraftMessage').textContent = ''; byId('releaseDraftFacts').replaceChildren(); renderCurrent();
    byId('bootstrapperForm').reset(); byId('bootstrapperForm').removeAttribute('aria-busy');
    byId('bootstrapperUpload').disabled = false; byId('bootstrapperFile').disabled = false;
    byId('bootstrapperStatus').textContent = ''; byId('bootstrapperMessage').textContent = '';
    byId('releasePreparationList').replaceChildren(); byId('bootstrapperList').replaceChildren(); catalog = [];
  }
  form.addEventListener('submit', event => { event.preventDefault(); mutation('save'); });
  fields.forEach(id => byId(id).addEventListener('input', () => { clearErrors(); controls(); }));
  byId('releaseDraftMandatory').addEventListener('change', controls);
  byId('releaseDraftPublish').addEventListener('click', () => mutation('publish'));
  byId('releaseDraftDeactivate').addEventListener('click', async () => {
    if (byId('releaseDraftDeactivate').disabled || busy) return;
    const expectedEpoch = epoch;
    if (await shell.confirm('Ngừng phân phối phiên bản này cho lượt cài và kiểm tra cập nhật mới?')) {
      if (expectedEpoch === epoch && dialog.open) mutation('deactivate');
    }
  });
  byId('releaseDraftCancel').addEventListener('click', () => mutation('cancel'));
  byId('releaseDraftRetry').addEventListener('click', () => mutation('retry'));
  byId('releaseDraftReload').addEventListener('click', () => current && open(current.preparationId));
  byId('releaseDraftSetupLink').addEventListener('click', openSetup);
  dialog.addEventListener('close', () => { stopPolling(); epoch++; });
  byId('refreshPreparationsButton').addEventListener('click', refreshList);
  byId('manageBootstrapperButton').addEventListener('click', openSetup);
  byId('releasePreparationList').addEventListener('click', event => {
    const button = event.target.closest('[data-open-preparation]');
    if (button) return open(button.dataset.openPreparation);
    const pagination = event.target.closest('[data-pagination="release-preparations"]');
    const next = event.target.closest('[data-page]');
    if (pagination && next && !next.disabled) { page = Number(next.dataset.page); refreshList(); }
  });
  byId('releasePreparationList').addEventListener('change', event => {
    if (event.target.matches('[data-page-size]')) { pageSize = Number(event.target.value); page = 1; refreshList(); }
  });
  byId('bootstrapperForm').addEventListener('submit', async event => {
    event.preventDefault();
    if (setupBusy) return;
    const file = byId('bootstrapperFile').files[0];
    if (!file || !/\.exe$/i.test(file.name) || file.size < 1 || file.size > 512 * 1024 * 1024) {
      byId('bootstrapperMessage').textContent = 'Chọn file Setup.exe có dữ liệu, tối đa 512 MiB.'; byId('bootstrapperFile').focus(); return;
    }
    const expectedSession = sessionEpoch;
    setupBusy = true; byId('bootstrapperUpload').disabled = true; byId('bootstrapperFile').disabled = true;
    byId('bootstrapperForm').setAttribute('aria-busy', 'true'); byId('bootstrapperMessage').textContent = 'Đang gửi và kiểm tra bộ cài…';
    try {
      const data = new FormData(); data.append('file', file);
      await api(setupEndpoint, { method: 'POST', body: data });
      if (expectedSession !== sessionEpoch) return;
      byId('bootstrapperForm').reset(); byId('bootstrapperMessage').textContent = 'Đã đăng ký Setup. Chọn Kích hoạt để sử dụng.'; await loadCatalog();
    } catch (error) { if (expectedSession === sessionEpoch) byId('bootstrapperMessage').textContent = error.message; }
    finally {
      if (expectedSession === sessionEpoch) {
        setupBusy = false; byId('bootstrapperUpload').disabled = false; byId('bootstrapperFile').disabled = false; byId('bootstrapperForm').removeAttribute('aria-busy');
      }
    }
  });
  byId('bootstrapperList').addEventListener('click', async event => {
    const button = event.target.closest('[data-activate-bootstrapper]');
    if (!button || setupBusy) return;
    const item = catalog.find(row => row.bootstrapper.bootstrapperId === button.dataset.activateBootstrapper);
    if (!item) return;
    const expectedSession = sessionEpoch;
    if (!await shell.confirm('Dùng Setup này cho các phiên bản tương thích được phát hành tiếp theo?')) return;
    if (expectedSession !== sessionEpoch) return;
    setupBusy = true; button.disabled = true;
    try {
      await api(`${setupEndpoint}/${encodeURIComponent(item.bootstrapper.bootstrapperId)}/activate`, { method: 'POST', body: JSON.stringify({ revision: item.revision }) });
      if (expectedSession !== sessionEpoch) return;
      await loadCatalog();
      if (dialog.open && current) {
        current = normalize(await api(`${endpoint}/${encodeURIComponent(current.preparationId)}`)); renderCurrent();
      }
      await refreshList();
    } catch (error) { if (expectedSession === sessionEpoch) { byId('bootstrapperMessage').textContent = error.message; await loadCatalog(); } }
    finally { if (expectedSession === sessionEpoch) { setupBusy = false; button.disabled = false; } }
  });
  window.videoMakerReleaseAdmin = Object.freeze({ open, openForRelease, openSetup, reset,
    activate: () => { active = true; refreshList(); },
    deactivate: () => { active = false; clearTimeout(listTimer); stopPolling(); } });
})();
