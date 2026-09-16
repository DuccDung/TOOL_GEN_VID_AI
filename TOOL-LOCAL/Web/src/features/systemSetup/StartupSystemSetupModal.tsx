import { useEffect, useMemo, useRef, useState } from 'react';
import { CircleCheck, Download, LoaderCircle, LogOut, Package, RefreshCw, ShieldCheck, TriangleAlert } from 'lucide-react';
import type { SystemSetupController } from './useSystemSetup';
import {
  isSystemSetupReady,
  needsApplicationRepair,
  requiredSetupComponents,
  type SetupComponentState
} from './types';
import './systemSetup.css';

const labels: Record<SetupComponentState, string> = {
  UNKNOWN: 'Chưa kiểm tra',
  NOT_INSTALLED: 'Chưa cài',
  NEEDS_VERIFICATION: 'Cần kiểm tra',
  READY: 'Sẵn sàng',
  REPAIR_REQUIRED: 'Cần sửa',
  UNSUPPORTED: 'Không hỗ trợ',
  DISABLED: 'Đang tắt'
};

const formatBytes = (value: number) => value >= 1024 ** 3
  ? `${(value / 1024 ** 3).toLocaleString('vi-VN', { maximumFractionDigits: 2 })} GB`
  : `${Math.ceil(value / 1024 ** 2).toLocaleString('vi-VN')} MB`;

export function StartupSystemSetupModal({ setup }: { setup: SystemSetupController }) {
  const cardRef = useRef<HTMLElement>(null);
  const checkedContextRef = useRef<string | null>(null);
  const [resourceConfirmed, setResourceConfirmed] = useState(false);
  const { snapshot, busy, error, repairProgress, repairError, run, repairApplication, exitApplication } = setup;
  const components = useMemo(() => requiredSetupComponents(snapshot), [snapshot]);
  const missing = useMemo(() => components.filter(component => component.state !== 'READY'), [components]);
  const ready = isSystemSetupReady(snapshot);
  const repairRequired = needsApplicationRepair(snapshot);
  const operation = snapshot?.operation;
  const repairing = repairProgress !== null;
  const resourceWarning = missing.some(component =>
    component.id === 'qwen' && component.errorCode === 'TRANSLATION_RESOURCE_CONFIRMATION_REQUIRED');
  const onlyResourceWarning = resourceWarning && missing.length === 1;
  const canRetry = Boolean(operation && !['Accepted', 'Running'].includes(operation.state)
    && missing.every(component => operation.componentIds.includes(component.id)));
  const requiresResourceConfirmation = resourceWarning && canRetry && !repairRequired;

  useEffect(() => {
    if (!snapshot || ready || busy || repairing || missing.length === 0) return;
    if (checkedContextRef.current === snapshot.contextGeneration) return;
    checkedContextRef.current = snapshot.contextGeneration;
    run('check', missing.map(component => component.id));
  }, [snapshot, ready, busy, repairing, missing, run]);

  useEffect(() => {
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    const card = cardRef.current;
    card?.focus();

    const focusInside = (preferLast = false) => {
      const currentCard = cardRef.current;
      if (!currentCard) return;
      const focusable = Array.from(currentCard.querySelectorAll<HTMLElement>(
        'button:not(:disabled), input:not(:disabled), [href], [tabindex]:not([tabindex="-1"])'
      ));
      const target = preferLast ? focusable.at(-1) : focusable[0];
      (target ?? currentCard).focus();
    };

    const trapFocus = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        event.stopPropagation();
        return;
      }
      if (event.key !== 'Tab' || !cardRef.current) return;
      const focusable = Array.from(cardRef.current.querySelectorAll<HTMLElement>(
        'button:not(:disabled), input:not(:disabled), [href], [tabindex]:not([tabindex="-1"])'
      ));
      if (focusable.length === 0) {
        event.preventDefault();
        cardRef.current.focus();
        return;
      }
      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (!cardRef.current.contains(document.activeElement)) {
        event.preventDefault();
        focusInside(event.shiftKey);
      } else if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    };

    const containFocus = (event: FocusEvent) => {
      const currentCard = cardRef.current;
      if (currentCard && event.target instanceof Node && !currentCard.contains(event.target)) {
        event.stopPropagation();
        focusInside();
      }
    };

    window.addEventListener('keydown', trapFocus, true);
    window.addEventListener('focusin', containFocus, true);
    return () => {
      document.body.style.overflow = previousOverflow;
      window.removeEventListener('keydown', trapFocus, true);
      window.removeEventListener('focusin', containFocus, true);
    };
  }, []);

  if (ready) return null;

  const currentComponent = components.find(component => component.id === operation?.currentComponent);
  const knownModelBytes = missing.filter(component => component.state === 'NOT_INSTALLED')
    .reduce((total, component) => total + (component.downloadBytes ?? 0), 0);
  const progress = repairing ? repairProgress : operation;
  const progressPercent = progress?.percent;
  const active = busy || repairing;
  const primaryLabel = repairRequired
    ? 'Sửa bộ ứng dụng'
    : onlyResourceWarning ? 'Kiểm tra lại'
      : missing.some(component => ['UNKNOWN', 'NOT_INSTALLED', 'REPAIR_REQUIRED'].includes(component.state))
        ? 'Cài và kiểm tra' : 'Kiểm tra lại';
  const progressMessage = repairing
    ? repairProgress?.message
    : currentComponent
      ? `Đang xử lý: ${currentComponent.name}`
      : busy
        ? 'Đang chuẩn bị kiểm tra thành phần hệ thống…'
        : resourceWarning
          ? 'Qwen cần được kiểm tra lại trước khi sử dụng.'
          : missing.some(component => component.errorCode)
            ? 'Xem lỗi từng thành phần rồi thử lại.'
            : 'Thành phần còn thiếu sẽ được cài và kiểm tra; model hợp lệ sẽ được dùng lại.';

  const start = (confirmResources = false) => {
    if (repairRequired) {
      repairApplication();
      return;
    }
    run(canRetry ? 'retry' : 'start', missing.map(component => component.id), confirmResources);
    setResourceConfirmed(false);
  };

  return (
    <div className="startup-setup-overlay" role="presentation">
      <section
        ref={cardRef}
        className="startup-setup-dialog"
        role="dialog"
        tabIndex={-1}
        aria-modal="true"
        aria-labelledby="startup-setup-title"
        aria-describedby="startup-setup-summary"
      >
        <header className="startup-setup-header">
          <span className="startup-setup-icon" aria-hidden="true"><Package size={25} /></span>
          <div>
            <span className="startup-setup-eyebrow">CHUẨN BỊ TAPHOATOOL</span>
            <h1 id="startup-setup-title">{onlyResourceWarning
              ? 'Qwen cần kiểm tra lại bộ nhớ'
              : repairRequired ? 'taphoatool cần sửa thành phần' : 'taphoatool cần bổ sung thành phần'}</h1>
            <p id="startup-setup-summary">
              {!snapshot
                ? 'Đang tải trạng thái các thành phần trên máy này…'
                : onlyResourceWarning
                  ? 'Model Qwen đã có, nhưng lượt kiểm tra dừng vì RAM hoặc bộ nhớ khả dụng thấp. Bạn có thể kiểm tra lại hoặc xác nhận để thử với tài nguyên hiện tại.'
                  : `${missing.length} thành phần chưa sẵn sàng.${knownModelBytes > 0
                    ? ` Dung lượng model cần chuẩn bị khoảng ${formatBytes(knownModelBytes)}.`
                    : ''} Chọn ${primaryLabel.toLowerCase()} để tiếp tục, hoặc hủy để thoát ứng dụng.`}
            </p>
          </div>
        </header>

        <div className="startup-setup-table-wrap">
          <table className="startup-setup-table">
            <thead><tr><th>Thành phần</th><th>Trạng thái</th><th>Chi tiết</th></tr></thead>
            <tbody>
              {!snapshot && <tr><td colSpan={3} className="startup-setup-loading">
                <LoaderCircle className="spin" size={18} /> Đang kiểm tra cấu hình máy…
              </td></tr>}
              {components.map(component => (
                <tr key={component.id} className={`startup-setup-row state-${component.state.toLowerCase()}`}>
                  <td><strong>{component.name}</strong>{component.version && <small>Phiên bản {component.version}</small>}</td>
                  <td><span className="startup-setup-state">
                    {component.state === 'READY' ? <CircleCheck size={15} /> : component.state === 'UNKNOWN'
                      ? <LoaderCircle className={active ? 'spin' : ''} size={15} /> : <TriangleAlert size={15} />}
                    {component.id === 'qwen' && component.errorCode === 'TRANSLATION_RESOURCE_CONFIRMATION_REQUIRED'
                      ? 'Cần xác nhận bộ nhớ' : labels[component.state]}
                  </span></td>
                  <td><span>{component.id === 'qwen'
                    && component.errorCode === 'TRANSLATION_RESOURCE_CONFIRMATION_REQUIRED'
                    ? 'Model đã có; RAM hoặc bộ nhớ khả dụng thấp khi kiểm tra Qwen. Có thể giải phóng bộ nhớ rồi thử lại, hoặc xác nhận để thử tiếp.'
                    : component.message}</span>
                    {component.state === 'NOT_INSTALLED' && (component.downloadBytes ?? 0) > 0
                      && <small>Dung lượng model: {formatBytes(component.downloadBytes!)}</small>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <div className="startup-setup-progress" role="status" aria-live="polite">
          <div><span>{progressMessage}</span>{progressPercent != null && <strong>{Math.round(progressPercent)}%</strong>}</div>
          {active && <progress aria-label="Tiến độ chuẩn bị taphoatool" max={100} value={progressPercent ?? undefined} />}
        </div>

        {requiresResourceConfirmation && !active && (
          <label className="startup-setup-resource-warning">
            <input type="checkbox" checked={resourceConfirmed} onChange={event => setResourceConfirmed(event.target.checked)} />
            <span><strong>Bạn vẫn có thể thử dùng Qwen với RAM hiện tại.</strong>
              Tôi hiểu Qwen có thể chạy chậm, treo hoặc hết bộ nhớ. Ứng dụng vẫn phải kiểm tra model và worker trước khi cho sử dụng.</span>
          </label>
        )}

        {(error || repairError) && <div className="startup-setup-error" role="alert">
          <TriangleAlert size={17} /><span>{repairError || error}</span>
        </div>}

        <footer className="startup-setup-footer">
          <div className="startup-setup-assurance"><ShieldCheck size={16} /> Package và model được xác minh trước khi sử dụng.</div>
          <div className="startup-setup-actions">
            <button type="button" className="startup-setup-exit" onClick={exitApplication}>
              <LogOut size={16} /> Hủy và thoát
            </button>
            {requiresResourceConfirmation && <button type="button" className="startup-setup-continue"
              disabled={!resourceConfirmed || active} onClick={() => start(true)}>
              Vẫn thử dùng Qwen
            </button>}
            <button type="button" className="startup-setup-primary" disabled={!snapshot || active || missing.length === 0}
              onClick={() => start()}>
              {active ? <LoaderCircle className="spin" size={17} />
                : primaryLabel === 'Kiểm tra lại' ? <RefreshCw size={17} /> : <Download size={17} />}
              {active ? 'Đang xử lý…' : primaryLabel}
            </button>
          </div>
        </footer>
      </section>
    </div>
  );
}
