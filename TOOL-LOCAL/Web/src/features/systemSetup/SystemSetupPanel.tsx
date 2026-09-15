import { useState } from 'react';
import { AudioLines, Film, Languages, ScanText, Settings2, ShieldCheck } from 'lucide-react';
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
  const availableComponents = components.filter(c => c.state !== 'DISABLED');
  const readyCount = availableComponents.filter(c => c.state === 'READY').length;
  const allReady = !!snapshot && availableComponents.length > 0 && readyCount === availableComponents.length;
  const status = !snapshot ? 'Chưa tải' : busy ? 'Đang xử lý' : allReady ? 'Sẵn sàng'
    : availableComponents.length ? `${readyCount}/${availableComponents.length} sẵn sàng` : 'Chưa có thành phần';
  const runAction = (mode: 'check' | 'start' | 'retry', confirmResources = false) => {
    run(mode, mode === 'retry' ? retryIds : selected, mode === 'retry' && resourceWarning && confirmResources);
    setConfirmed(false);
  };
  return <section className="card desktop-setting-card system-setup" aria-labelledby="system-setup-title">
    <div className="desktop-setting-heading setup-heading">
      <span className="desktop-setting-icon"><Settings2 size={22} aria-hidden="true" /></span>
      <div>
        <span className="api-eyebrow">HỆ THỐNG · THÀNH PHẦN LOCAL</span>
        <h2 id="system-setup-title">Setup hệ thống</h2>
        <p>Kiểm tra FFmpeg, PaddleOCR, Qwen và Piper trên máy này. Cài một lần, dùng chung cho các dự án.</p>
      </div>
      <span className={`desktop-setting-status ${allReady && !busy ? 'active' : snapshot ? 'pending' : ''}`}>{status}</span>
    </div>
    {!snapshot && <div className="setup-empty" role="status">Chọn tổ chức để tải trạng thái Setup.
      <button type="button" onClick={refresh}>Tải lại</button></div>}
    <div className="setup-components">
      {components.map(component => {
        const Icon = component.id === 'media' ? Film : component.id === 'ocr' ? ScanText
          : component.id === 'qwen' ? Languages : component.id === 'piper' ? AudioLines : Settings2;
        return <article key={component.id} className={`setup-component setup-component-${component.state.toLowerCase()}`}>
          <span className="setup-component-icon"><Icon size={19} aria-hidden="true" /></span>
          <div className="setup-component-body">
            <strong>{component.name}</strong>
            <p>{component.id === 'qwen' && component.errorCode === 'TRANSLATION_RESOURCE_CONFIRMATION_REQUIRED'
              ? 'Model đã có; RAM hoặc bộ nhớ khả dụng thấp khi kiểm tra Qwen. Có thể giải phóng bộ nhớ rồi thử lại, hoặc xác nhận để thử tiếp.'
              : component.message}</p>
            {(component.downloadBytes != null && component.downloadBytes > 0 || component.minimumFreeDiskBytes != null)
              && <div className="setup-component-meta">
                {component.downloadBytes != null && component.downloadBytes > 0 && <small>Model: {bytes(component.downloadBytes)}</small>}
                {component.minimumFreeDiskBytes != null && <small>Dung lượng trống tối thiểu: {bytes(component.minimumFreeDiskBytes)}</small>}
              </div>}
            {component.id === 'piper' && <label className="setup-piper-choice">
              <span>Cài và kiểm tra giọng Việt</span>
              <input type="checkbox" checked={piper} disabled={busy || component.state === 'DISABLED'}
                onChange={e => setPiper(e.target.checked)} />
              <i aria-hidden="true"><b /></i>
            </label>}
            {['ocr', 'media'].includes(component.id) && component.state === 'REPAIR_REQUIRED'
              && <button type="button" className="setup-repair-action" disabled={busy}
                onClick={() => postToHost('media.tools.install.prepare')}>Sửa bộ ứng dụng</button>}
          </div>
          <span className={`setup-state setup-state-${component.state.toLowerCase()}`}>{component.id === 'qwen'
            && component.errorCode === 'TRANSLATION_RESOURCE_CONFIRMATION_REQUIRED'
            ? 'Cần xác nhận bộ nhớ' : labels[component.state]}</span>
        </article>;
      })}
    </div>
    {busy && <div className="setup-progress" role="status" aria-live="polite">
      <strong>{components.find(c => c.id === operation?.currentComponent)?.name ?? 'Đang chuẩn bị Setup…'}</strong>
      <progress aria-label="Tiến độ thành phần hiện tại" max={100} value={operation?.percent ?? undefined} />
      {operation?.percent != null && <span>{Math.round(operation.percent)}%</span>}
      <p>Có thể chuyển trang; Setup vẫn tiếp tục. Đóng ứng dụng sẽ hủy lượt đang chạy.</p>
    </div>}
    {!busy && operation && <p role="status" className="setup-operation-note">{operation.allSelectedReady ? 'Các thành phần đã chọn đã sẵn sàng.'
      : operation.state === 'Cancelled' ? 'Đã hủy Setup. Có thể thử lại các thành phần chưa hoàn tất.'
      : operation.state === 'Interrupted' ? 'Lượt trước bị gián đoạn khi đóng ứng dụng. Hãy kiểm tra hoặc cài tiếp.'
      : 'Một số thành phần chưa sẵn sàng. Xem trạng thái từng thành phần rồi thử lại.'}</p>}
    {resourceWarning && !busy && <label className="setup-choice setup-warning"><input type="checkbox" checked={confirmed}
      onChange={e => setConfirmed(e.target.checked)} />Tôi hiểu Qwen có thể chạy chậm, treo hoặc hết bộ nhớ khi thử với RAM hiện tại. Model và worker vẫn được kiểm tra trước khi sử dụng.</label>}
    {error && <p role="alert" className="setup-warning">{error}</p>}
    <div className="setup-actions">
      <button type="button" disabled={busy || !selected.length} onClick={() => runAction('check')}>Kiểm tra hệ thống</button>
      <button type="button" className="primary-button" disabled={busy || !selected.length} onClick={() => runAction('start')}>Cài thành phần cần thiết</button>
      {busy && <button type="button" disabled={!operation} onClick={cancel}>Hủy Setup</button>}
      {!busy && operation && retryIds.length > 0 && <button type="button" onClick={() => runAction('retry')}>Kiểm tra lại phần chưa đạt</button>}
      {resourceWarning && !busy && operation && retryIds.length > 0 && <button type="button"
        disabled={!confirmed} onClick={() => runAction('retry', true)}>Vẫn thử dùng Qwen</button>}
    </div>
    <div className="desktop-setting-server-note setup-assurance">
      <ShieldCheck size={18} aria-hidden="true" />
      <div>
        <strong>Cài một lần, dùng chung trên máy này</strong>
        <p>Qwen tải khoảng 2,50 GB. Piper cần model khoảng 63 MB cùng Python và các thư viện riêng.
          Lần đầu cần Internet; model đã tải đúng được dùng lại. Kiểm tra chỉ xác minh thành phần có sẵn.</p>
      </div>
    </div>
  </section>;
}
