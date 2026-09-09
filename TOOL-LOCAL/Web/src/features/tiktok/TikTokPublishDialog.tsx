import { useEffect, useRef, type KeyboardEvent } from 'react';
import { AlertCircle, Check, CheckCircle2, LoaderCircle, Upload, X } from 'lucide-react';
import type { TikTokModuleState } from './types';

type Props = { state: TikTokModuleState; onDismiss: () => void; onCancel: () => void };

export function TikTokPublishDialog({ state, onDismiss, onCancel }: Props) {
  const dialog = useRef<HTMLDialogElement>(null);
  const feedback = state.publishFeedback;
  const open = Boolean(feedback?.open);
  useEffect(() => {
    const element = dialog.current;
    if (open && element && !element.open) element.showModal();
    return () => { if (element?.open) element.close(); };
  }, [open]);

  if (!feedback) return null;
  const { phase } = feedback;
  const finished = phase === 'success' || phase === 'error' || phase === 'cancelled';
  const processing = phase === 'processing';
  const percent = Math.max(0, Math.min(100, state.upload?.percent ?? 0));
  const title = {
    preparing: 'Đang chuẩn bị bài đăng', uploading: 'Đang tải video lên TikTok',
    processing: 'TikTok đang xử lý video', success: 'Đã đăng thành công',
    error: 'Chưa thể hoàn tất bài đăng', cancelled: 'Đã dừng thao tác tải lên'
  }[phase];
  const detail = {
    preparing: 'Đang kiểm tra video và cài đặt bài đăng của bạn.',
    uploading: 'Giữ VideoMaker mở và kết nối mạng trong khi tải video.',
    processing: 'Video đã được gửi. TikTok có thể cần vài phút hoặc lâu hơn để xử lý. Bạn có thể thu nhỏ cửa sổ này.',
    success: 'TikTok đã xác nhận đăng video thành công trên tài khoản đã chọn.',
    error: feedback.message || 'Hãy kiểm tra kết nối và cài đặt bài đăng trước khi thử lại.',
    cancelled: state.publish ? 'Đã yêu cầu dừng tải lên. Bài đã gửi vẫn được theo dõi để xác nhận kết quả từ TikTok.' : 'Bạn có thể đóng thông báo để kiểm tra lại video và đăng khi sẵn sàng.'
  }[phase];
  const step = phase === 'success' ? 3 : processing ? 2 : phase === 'uploading' ? 1 : 0;

  return (
    <dialog ref={dialog} className={`tiktok-publish-dialog is-${phase}`} aria-modal="true" aria-labelledby="tiktokPublishTitle"
      aria-describedby="tiktokPublishDetail" onKeyDown={keepDialogFocus}
      onCancel={(event) => { event.preventDefault(); onDismiss(); }}>
      <button type="button" className="icon-button tiktok-dialog-close" aria-label={finished ? 'Đóng thông báo' : 'Thu nhỏ tiến trình'} onClick={onDismiss}><X size={18} /></button>
      <div className="tiktok-dialog-symbol" aria-hidden="true">
        {phase === 'success' ? <CheckCircle2 size={30} /> : phase === 'error' || phase === 'cancelled'
          ? <AlertCircle size={30} /> : <LoaderCircle size={30} className="spin" />}
      </div>
      <div role="status" aria-live="polite" aria-atomic="true">
        <h2 id="tiktokPublishTitle">{title}</h2>
        <p id="tiktokPublishDetail">{detail}</p>
      </div>
      {(state.media || state.feature.connection) && <div className="tiktok-dialog-summary">
        <Upload size={18} aria-hidden="true" />
        <div>
          {state.media && <strong title={state.media.fileName}>{state.media.fileName}</strong>}
          <span>Tài khoản: {state.creator?.creatorNickname || state.feature.connection?.creatorNickname || 'TikTok creator'}</span>
        </div>
      </div>}
      {!finished && <>
        <ol className="tiktok-publish-steps" aria-label="Các bước đăng video">
          {['Chuẩn bị', 'Tải video', 'TikTok xử lý'].map((label, index) => <li key={label} className={index <= step ? 'active' : ''} aria-current={index === step ? 'step' : undefined}>
            <span aria-hidden="true">{index < step ? <Check size={12} /> : index + 1}</span>{label}
          </li>)}
        </ol>
        {phase === 'uploading' && <div className="tiktok-upload-meter">
          <div><span>Đã tải lên</span><strong>{Math.round(percent)}%</strong></div>
          <div className="tiktok-progress-track" role="progressbar" aria-label="Tiến độ tải video" aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.round(percent)}>
            <i style={{ transform: `scaleX(${percent / 100})` }} />
          </div>
          {state.upload && <small>{formatBytes(state.upload.uploadedBytes)} / {formatBytes(state.upload.totalBytes)}</small>}
          {percent === 100 && <p className="tiktok-upload-note">Đã tải đủ dữ liệu, đang chờ TikTok xác nhận.</p>}
        </div>}
        {state.error && <p className="tiktok-dialog-warning" role="alert">{state.error} Tiến trình vẫn được kiểm tra tự động.</p>}
      </>}
      <div className="tiktok-dialog-actions">
        {!finished && !processing && <button type="button" className="tiktok-secondary-button" onClick={onCancel}>Hủy tải lên</button>}
        <button type="button" className={finished ? 'start-button' : 'tiktok-secondary-button'} onClick={onDismiss}>
          {phase === 'error' ? 'Đóng và kiểm tra lại' : finished ? 'Đóng' : 'Thu nhỏ'}
        </button>
      </div>
    </dialog>
  );
}

function keepDialogFocus(event: KeyboardEvent<HTMLDialogElement>) {
  if (event.key !== 'Tab') return;
  const buttons = event.currentTarget.querySelectorAll<HTMLButtonElement>('button:not(:disabled)');
  const first = buttons[0];
  const last = buttons[buttons.length - 1];
  if (event.shiftKey && document.activeElement === first) {
    event.preventDefault(); last?.focus();
  } else if (!event.shiftKey && document.activeElement === last) {
    event.preventDefault(); first?.focus();
  }
}

function formatBytes(bytes: number): string {
  if (bytes >= 1024 ** 3) return `${(bytes / 1024 ** 3).toFixed(2)} GB`;
  return `${(bytes / 1024 ** 2).toFixed(1)} MB`;
}
