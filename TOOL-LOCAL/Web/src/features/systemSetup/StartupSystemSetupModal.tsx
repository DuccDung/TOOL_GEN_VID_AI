import { useEffect, useMemo, useRef, useState } from 'react';
import { CircleCheck, Download, LoaderCircle, LogOut, Package, ShieldCheck, TriangleAlert } from 'lucide-react';
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
  const canRetry = Boolean(operation && !['Accepted', 'Running'].includes(operation.state)
    && missing.every(component => operation.componentIds.includes(component.id)));
  const requiresResourceConfirmation = resourceWarning && canRetry;

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
  const knownDownloadBytes = missing.reduce((total, component) => total + (component.downloadBytes ?? 0), 0);
  const progress = repairing ? repairProgress : operation;
  const progressPercent = progress?.percent;
  const active = busy || repairing;
  const primaryLabel = repairRequired
    ? 'OK - Sửa bộ ứng dụng'
    : operation?.mode !== 'check' && operation ? 'OK - Thử lại' : 'OK - Cài đặt';
  const progressMessage = repairing
    ? repairProgress?.message
    : currentComponent
      ? `Đang xử lý: ${currentComponent.name}`
      : busy
        ? 'Đang chuẩn bị kiểm tra thành phần hệ thống…'
        : missing.some(component => component.errorCode)
          ? 'Kiểm tra chi tiết lỗi trong danh sách rồi thử cài đặt.'
          : 'Các thành phần còn thiếu sẽ được tải và kiểm tra tự động.';

  const install = () => {
    if (repairRequired) {
      repairApplication();
      return;
    }
    run(canRetry ? 'retry' : 'start', missing.map(component => component.id), requiresResourceConfirmation && resourceConfirmed);
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
            <span className="startup-setup-eyebrow">CHUẨN BỊ VIDEOMAKER</span>
            <h1 id="startup-setup-title">VideoMaker cần bổ sung thành phần</h1>
            <p id="startup-setup-summary">
              {!snapshot
                ? 'Đang tải trạng thái các thành phần trên máy này…'
                : `${missing.length} thành phần chưa sẵn sàng.${knownDownloadBytes > 0
                  ? ` Dung lượng tải đã biết khoảng ${formatBytes(knownDownloadBytes)}.`
                  : ''} Chọn cài đặt để tiếp tục hoặc hủy để thoát ứng dụng.`}
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
                    {labels[component.state]}
                  </span></td>
                  <td><span>{component.message}</span>
                    {(component.downloadBytes ?? 0) > 0 && <small>Tải xuống: {formatBytes(component.downloadBytes!)}</small>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <div className="startup-setup-progress" role="status" aria-live="polite">
          <div><span>{progressMessage}</span>{progressPercent != null && <strong>{Math.round(progressPercent)}%</strong>}</div>
          <progress aria-label="Tiến độ chuẩn bị VideoMaker" max={100} value={progressPercent ?? undefined} />
        </div>

        {requiresResourceConfirmation && !active && (
          <label className="startup-setup-resource-warning">
            <input type="checkbox" checked={resourceConfirmed} onChange={event => setResourceConfirmed(event.target.checked)} />
            <span><strong>Tài nguyên máy thấp hơn mức khuyến nghị cho Qwen.</strong>
              Tôi đã đóng các ứng dụng nặng và đồng ý thử kiểm tra với cấu hình hiện tại.</span>
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
            <button type="button" className="startup-setup-primary" disabled={!snapshot || active || missing.length === 0
              || (requiresResourceConfirmation && !resourceConfirmed)} onClick={install}>
              {active ? <LoaderCircle className="spin" size={17} /> : <Download size={17} />}{active ? 'Đang xử lý…' : primaryLabel}
            </button>
          </div>
        </footer>
      </section>
    </div>
  );
}
