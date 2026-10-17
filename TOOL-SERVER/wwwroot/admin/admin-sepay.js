(() => {
  const shell = window.videoMakerAdminShell;
  const panel = document.querySelector('[data-panel="payments"]');
  if (!shell || !panel) return;
  const { api, escapeHtml: esc, icon, toast } = shell;
  const byId = id => document.getElementById(id);
  const endpoint = '/api/admin/payments/sepay';
  const banks = { VCB: 'Vietcombank', TCB: 'Techcombank', MB: 'MB Bank', ACB: 'ACB',
    BIDV: 'BIDV', CTG: 'VietinBank', TPB: 'TPBank', VPB: 'VPBank' };
  let data = null, editedId = null, editedRevision = null, formSettingsRevision = null;
  let dirty = false, generation = 0, reading = false, reader = null, mutation = null;
  const visible = () => !panel.classList.contains('hidden') && Boolean(shell.state.accessToken);

  function notice(message = '', error = true) {
    byId('sepayNotice').textContent = message;
    byId('sepayNotice').hidden = !message;
    byId('sepayNotice').classList.toggle('sepay-success', !error);
  }

  function showError(error, form = false) {
    if (error.status === 401) return shell.showLogin('Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.');
    const message = error.name === 'AbortError' || error.name === 'TimeoutError'
      ? 'Yêu cầu quá thời gian chờ. Vui lòng làm mới để kiểm tra kết quả.'
      : error.message || 'Không thể hoàn tất yêu cầu. Vui lòng thử lại.';
    if (form) byId('sepayFormMessage').textContent = message;
    else notice(message);
  }

  function syncBusy() {
    const busy = reading || Boolean(mutation) || !data;
    panel.setAttribute('aria-busy', String(reading || Boolean(mutation)));
    byId('sepayFields').disabled = busy;
    byId('sepayAdd').disabled = busy;
    byId('sepayEnabled').disabled = busy;
    byId('sepaySave').textContent = mutation ? 'Đang lưu…' : 'Lưu thay đổi';
    byId('sepayUseForNewOrders').disabled = Boolean(editedId && editedId === data?.selectedAccountId);
    panel.querySelectorAll('[data-sepay-select], [data-sepay-edit]').forEach(button => { button.disabled = busy; });
  }

  function syncBank() {
    const other = byId('sepayBank').value === 'other';
    byId('sepayOtherBank').hidden = !other;
    byId('sepayBankCode').required = other;
    byId('sepayBankName').required = other;
    byId('sepayBankCode').disabled = !other;
    byId('sepayBankName').disabled = !other;
  }

  function openForm(account = null, focus = false) {
    byId('sepayAccountForm').reset();
    editedId = account?.accountId || null;
    editedRevision = account?.revision || null;
    formSettingsRevision = data?.revision || null;
    dirty = false;
    byId('sepayFormTitle').textContent = account ? 'Chỉnh sửa tài khoản' : 'Thêm tài khoản';
    byId('sepayDisplayName').value = account?.displayName || '';
    byId('sepayBank').value = account && !banks[account.bankCode] ? 'other' : account?.bankCode || 'VCB';
    byId('sepayBankCode').value = account?.bankCode || '';
    byId('sepayBankName').value = account?.bankName || '';
    byId('sepayAccountNumber').value = account?.accountNumber || '';
    byId('sepayAccountName').value = account?.accountName || '';
    byId('sepayUseForNewOrders').checked = account ? account.accountId === data?.selectedAccountId : !data?.selectedAccountId;
    byId('sepayFormMessage').textContent = '';
    syncBank();
    syncBusy();
    if (focus) byId('sepayDisplayName').focus();
  }

  function render() {
    if (!data) return;
    const selected = data.accounts.find(account => account.accountId === data.selectedAccountId);
    byId('sepayEnabled').checked = data.enabled;
    byId('sepayStatus').textContent = data.enabled && selected
      ? `Đang nhận thanh toán qua ${selected.bankName} · ${selected.accountNumber}`
      : selected ? `Đã chọn ${selected.bankName} · ${selected.accountNumber}. Nhận thanh toán đang tắt.`
        : 'Chưa chọn tài khoản nhận tiền. Thêm tài khoản trước khi bật thanh toán.';
    byId('sepayAccounts').innerHTML = data.accounts.length
      ? `<div class="receiving-accounts">${data.accounts.map(account => {
        const selected = account.accountId === data.selectedAccountId;
        return `<article data-sepay-account="${esc(account.accountId)}" class="receiving-account ${selected ? 'sepay-selected' : ''}"><header><span class="bank-symbol">${icon('payment-card')}</span><div><h3>${esc(account.displayName)}</h3><p>${esc(account.bankName)} · ${esc(account.bankCode)}</p></div></header><strong class="account-number">${esc(account.accountNumber)}</strong><p>${esc(account.accountName)}</p><footer>${selected
          ? `<span class="status-pill ${data.enabled ? 'status-healthy' : ''}">${data.enabled ? 'Đang nhận tiền' : 'Đã chọn'}</span>`
          : `<button class="ghost-button" type="button" data-sepay-select="${esc(account.accountId)}">Chọn nhận tiền</button>`}<button class="text-button" type="button" data-sepay-edit="${esc(account.accountId)}" aria-label="Sửa ${esc(account.displayName)}">${icon('pencil')}<span>Sửa</span></button></footer></article>`;
      }).join('')}</div>`
      : '<div class="empty-state">Chưa có tài khoản nhận tiền. Bấm Thêm tài khoản để bắt đầu.</div>';
    syncBusy();
  }

  function receive(response, preserveDraft = false, accountId = null) {
    if (!response || !Array.isArray(response.accounts) || !response.revision || typeof response.enabled !== 'boolean')
      throw new Error('Không đọc được cấu hình thanh toán. Vui lòng làm mới để kiểm tra kết quả.');
    data = response;
    render();
    if (!preserveDraft || !dirty) openForm(data.accounts.find(account => account.accountId === (accountId || data.selectedAccountId)) || null);
  }

  async function load() {
    if (!visible() || mutation) return;
    reader?.abort();
    const current = ++generation;
    const controller = new AbortController();
    reader = controller;
    reading = true;
    notice();
    byId('sepayRetry').hidden = true;
    syncBusy();
    const timeout = setTimeout(() => controller.abort(), 30000);
    try {
      const response = await api(endpoint, { signal: controller.signal });
      if (current !== generation || !visible()) return;
      // Preserve both draft values and their original revisions, so refresh cannot hide a conflict.
      receive(response, true);
    } catch (error) {
      if (current !== generation || !visible()) return;
      showError(error);
      byId('sepayRetry').hidden = false;
    } finally {
      clearTimeout(timeout);
      if (current === generation) { reading = false; reader = null; syncBusy(); }
    }
  }

  async function change(path, method, body, form = false, preserveDraft = false) {
    if (!data || reading || mutation || !visible()) return;
    const current = generation, operation = Symbol();
    mutation = operation;
    notice();
    byId('sepayFormMessage').textContent = '';
    syncBusy();
    try {
      const response = await api(`${endpoint}${path}`, {
        method, body: JSON.stringify(body), signal: AbortSignal.timeout(30000)
      });
      if (current !== generation || !visible()) return;
      const saved = form && Array.isArray(response?.accounts)
        ? response.accounts.find(account => account.bankCode === body.bankCode && account.accountNumber === body.accountNumber) : null;
      receive(response, preserveDraft, saved?.accountId || editedId);
      if (preserveDraft && dirty) formSettingsRevision = response.revision;
      toast(form ? 'Đã lưu tài khoản nhận tiền.' : 'Đã cập nhật cấu hình thanh toán.');
    } catch (error) {
      if (current !== generation || !visible()) return;
      showError(error, form);
      byId('sepayEnabled').checked = data.enabled;
    } finally {
      if (mutation === operation) {
        mutation = null;
        syncBusy();
        if (current !== generation && visible()) await load();
      }
    }
  }

  byId('sepayAdd').addEventListener('click', () => { if (!mutation && data) openForm(null, true); });
  byId('sepayCancel').addEventListener('click', () => openForm(data?.accounts.find(account => account.accountId === data.selectedAccountId) || null));
  byId('sepayBank').addEventListener('change', syncBank);
  byId('sepayAccountForm').addEventListener('input', () => { dirty = true; byId('sepayFormMessage').textContent = ''; });
  byId('sepayAccountForm').addEventListener('change', () => { dirty = true; });
  byId('sepayAccountForm').addEventListener('submit', event => {
    event.preventDefault();
    if (!byId('sepayAccountForm').reportValidity()) return;
    const bank = byId('sepayBank').value;
    return change(editedId ? `/accounts/${encodeURIComponent(editedId)}` : '/accounts', editedId ? 'PUT' : 'POST', {
      displayName: byId('sepayDisplayName').value.trim(),
      bankCode: (bank === 'other' ? byId('sepayBankCode').value.trim() : bank).toUpperCase(),
      bankName: bank === 'other' ? byId('sepayBankName').value.trim() : banks[bank],
      accountNumber: byId('sepayAccountNumber').value.trim(), accountName: byId('sepayAccountName').value.trim(),
      useForNewOrders: byId('sepayUseForNewOrders').checked, settingsRevision: formSettingsRevision,
      revision: editedRevision
    }, true);
  });
  byId('sepayEnabled').addEventListener('change', () => change('/enabled', 'PUT', {
    enabled: byId('sepayEnabled').checked, revision: data?.revision
  }, false, true));
  byId('sepayAccounts').addEventListener('click', event => {
    if (!data || reading || mutation) return;
    const edit = event.target.closest('[data-sepay-edit]');
    if (edit) return openForm(data.accounts.find(account => account.accountId === edit.dataset.sepayEdit), true);
    const select = event.target.closest('[data-sepay-select]');
    if (select) return change('/selected-account', 'PUT', { accountId: select.dataset.sepaySelect, revision: data.revision });
  });
  byId('sepayRetry').addEventListener('click', load);
  byId('sepayWebhookUrl').value = `${location.origin}/api/payments/sepay/webhook`;
  byId('sepayCopyWebhook').addEventListener('click', async () => {
    try { await navigator.clipboard.writeText(byId('sepayWebhookUrl').value); toast('Đã sao chép URL webhook.'); }
    catch { byId('sepayWebhookUrl').focus(); byId('sepayWebhookUrl').select(); notice('Chọn URL rồi nhấn Ctrl+C để sao chép.', false); }
  });
  function deactivate() { generation++; reader?.abort(); reader = null; reading = false; syncBusy(); }
  function reset() {
    deactivate(); data = null; mutation = null; editedId = null; editedRevision = null; dirty = false;
    byId('sepayAccountForm').reset(); byId('sepayFormMessage').textContent = '';
    byId('sepayEnabled').checked = false;
    byId('sepayStatus').textContent = 'Chưa tải cấu hình thanh toán.';
    byId('sepayAccounts').textContent = '';
    byId('sepayRetry').hidden = true; notice(); syncBank(); syncBusy();
  }
  window.videoMakerSepayAdmin = { activate: load, refresh: load, deactivate, reset };
})();
