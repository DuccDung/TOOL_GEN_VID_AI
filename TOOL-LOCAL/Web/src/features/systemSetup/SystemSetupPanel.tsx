import { useState } from 'react';
import { postToHost } from '../../bridge';
import type { SystemSetupController } from './useSystemSetup';
import type { SetupComponentState } from './types';
import './systemSetup.css';

const labels: Record<SetupComponentState, string> = {
  UNKNOWN: 'Chưa kiểm tra', NOT_INSTALLED: 'Chưa cài', NEEDS_VERIFICATION: 'Cần kiểm tra',
  READY: 'Sẵn sàng', REPAIR_REQUIRED: 'Cần sửa', UNSUPPORTED: 'Không hỗ trợ', DISABLED: 'Đang tắt'
};
const bytes = (value: number) => `${(value / 1_000_000_000).toLocaleString('vi-VN', { maximumFractionDigits: 2 })} GB`;

export function SystemSetupPanel({ setup }: { setup: SystemSetupController }) {
  const [piper, setPiper] = useState(true);
  const [confirmed, setConfirmed] = useState(false);
  const { snapshot, busy, error, run, cancel, refresh } = setup;
  const components = snapshot?.components ?? [];
  const selected = components.filter(c => c.state !== 'DISABLED' && (c.id !== 'piper' || piper)).map(c => c.id);
  const operation = snapshot?.operation;
  const retryIds = components.filter(c => selected.includes(c.id) && c.state !== 'READY'
    && operation?.componentIds.includes(c.id)).map(c => c.id);
  const resourceWarning = retryIds.includes('qwen') && components.some(c => c.id === 'qwen'
    && c.errorCode === 'TRANSLATION_RESOURCE_CONFIRMATION_REQUIRED');
  const runAction = (mode: 'check' | 'start' | 'retry') => {
    run(mode, mode === 'retry' ? retryIds : selected, mode === 'retry' && resourceWarning && confirmed);
    setConfirmed(false);
  };
  return <section className="card system-setup" aria-labelledby="system-setup-title">
    <div><h2 id="system-setup-title">Setup hệ thống</h2>
      <p>Chuẩn bị OCR, dịch và giọng Việt trên máy này. Cài một lần, dùng chung cho các dự án.</p></div>
    <p className="setup-note">Qwen tải khoảng 2,50 GB. Piper cần model khoảng 63 MB cùng Python và các thư viện riêng.
      Lần đầu cần Internet; model đã tải đúng sẽ được dùng lại. Kiểm tra chỉ xác minh thành phần có sẵn.</p>
    {!snapshot && <div role="status">Chọn tổ chức để tải trạng thái Setup. <button type="button" onClick={refresh}>Tải lại</button></div>}
    <div className="setup-components">
      {components.map(component => <article key={component.id} className="setup-component">
        <div className="setup-component-heading"><strong>{component.name}</strong>
          <span className={`setup-state setup-state-${component.state.toLowerCase()}`}>{labels[component.state]}</span></div>
        <p>{component.message}</p>
        {component.downloadBytes != null && component.downloadBytes > 0 && <small>Model: {bytes(component.downloadBytes)}. </small>}
        {component.minimumFreeDiskBytes != null && <small>Dung lượng trống tối thiểu: {bytes(component.minimumFreeDiskBytes)}.</small>}
        {component.id === 'piper' && <label className="setup-choice"><input type="checkbox" checked={piper}
          disabled={busy || component.state === 'DISABLED'} onChange={e => setPiper(e.target.checked)} />Cài và kiểm tra giọng Việt</label>}
        {['ocr', 'media'].includes(component.id) && component.state === 'REPAIR_REQUIRED'
          && <button type="button" disabled={busy} onClick={() => postToHost('media.tools.install.prepare')}>Sửa bộ ứng dụng</button>}
      </article>)}
    </div>
    {busy && <div className="setup-progress" role="status" aria-live="polite">
      <strong>{components.find(c => c.id === operation?.currentComponent)?.name ?? 'Đang chuẩn bị Setup…'}</strong>
      <progress aria-label="Tiến độ thành phần hiện tại" max={100} value={operation?.percent ?? undefined} />
      {operation?.percent != null && <span>{Math.round(operation.percent)}%</span>}
      <p>Có thể chuyển trang; Setup vẫn tiếp tục. Đóng ứng dụng sẽ hủy lượt đang chạy.</p>
    </div>}
    {!busy && operation && <p role="status">{operation.allSelectedReady ? 'Các thành phần đã chọn đã sẵn sàng.'
      : operation.state === 'Cancelled' ? 'Đã hủy Setup. Có thể thử lại các thành phần chưa hoàn tất.'
      : operation.state === 'Interrupted' ? 'Lượt trước bị gián đoạn khi đóng ứng dụng. Hãy kiểm tra hoặc cài tiếp.'
      : 'Một số thành phần chưa sẵn sàng. Xem trạng thái từng thành phần rồi thử lại.'}</p>}
    {resourceWarning && !busy && <label className="setup-choice setup-warning"><input type="checkbox" checked={confirmed}
      onChange={e => setConfirmed(e.target.checked)} />Tôi đã đọc cảnh báo bộ nhớ và đồng ý thử kiểm tra Qwen với tài nguyên hiện tại.</label>}
    {error && <p role="alert" className="setup-warning">{error}</p>}
    <div className="setup-actions">
      <button type="button" disabled={busy || !selected.length} onClick={() => runAction('check')}>Kiểm tra hệ thống</button>
      <button type="button" className="primary-button" disabled={busy || !selected.length} onClick={() => runAction('start')}>Cài thành phần cần thiết</button>
      {busy && <button type="button" disabled={!operation} onClick={cancel}>Hủy Setup</button>}
      {!busy && operation && retryIds.length > 0 && <button type="button" onClick={() => runAction('retry')}>Thử lại phần chưa đạt</button>}
    </div>
  </section>;
}
