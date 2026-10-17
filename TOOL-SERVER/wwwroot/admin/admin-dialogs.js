(() => {
  'use strict';
  const root = document.getElementById('adminActionDialog');
  const form = document.getElementById('adminActionForm');
  const input = document.getElementById('adminActionValue');
  let pending, previous;
  function finish(value) {
    const resolve = pending; pending = null;
    if (root.open) root.close();
    if (previous?.isConnected) previous.focus();
    resolve?.(value);
  }
  function ask(message, title, options = {}) {
    if (pending) return Promise.resolve(options.input ? null : false);
    previous = document.activeElement;
    document.getElementById('adminActionTitle').textContent = title;
    document.getElementById('adminActionMessage').textContent = message;
    document.getElementById('adminActionField').hidden = !options.input;
    input.required = Boolean(options.input); input.value = options.value || '30';
    const button = document.getElementById('adminActionConfirm');
    button.textContent = options.label || 'Xác nhận';
    button.className = options.danger ? 'danger-button' : 'primary-button';
    root.showModal();
    (options.input ? input : document.getElementById('adminActionCancel')).focus();
    return new Promise(resolve => { pending = resolve; });
  }
  form.addEventListener('submit', event => {
    event.preventDefault();
    if (!form.reportValidity()) return;
    finish(input.required ? Number(input.value) : true);
  });
  root.addEventListener('cancel', event => { event.preventDefault(); finish(input.required ? null : false); });
  root.addEventListener('close', () => { if (pending) finish(input.required ? null : false); });
  document.getElementById('adminActionCancel').addEventListener('click', () => finish(input.required ? null : false));
  window.videoMakerAdminDialogs = Object.freeze({
    confirm: (message, options = {}) => ask(message, options.title || 'Xác nhận thao tác', { ...options, danger: options.danger ?? true }),
    requestDays: () => ask('Chọn số ngày gia hạn thêm cho gói đang sử dụng.', 'Gia hạn gói', { input: true, label: 'Gia hạn', value: '30' }),
    cancel: () => finish(null)
  });
})();
