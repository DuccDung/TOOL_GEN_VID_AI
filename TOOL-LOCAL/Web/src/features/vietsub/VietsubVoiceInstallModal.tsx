import { useEffect, useRef, type KeyboardEvent as ReactKeyboardEvent } from 'react';
import { createPortal } from 'react-dom';
import { Download, HardDrive, ShieldCheck, Volume2, X } from 'lucide-react';

export function VietsubVoiceInstallModal({
  requiredBytes,
  modelVersion,
  onDismiss,
  onConfirm
}: {
  requiredBytes: number;
  modelVersion?: string | null;
  onDismiss: () => void;
  onConfirm: () => void;
}) {
  const dialogRef = useRef<HTMLElement>(null);
  const confirmButtonRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    const previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    confirmButtonRef.current?.focus();

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        onDismiss();
      }
    };
    window.addEventListener('keydown', handleKeyDown);

    return () => {
      document.body.style.overflow = previousOverflow;
      window.removeEventListener('keydown', handleKeyDown);
      previousFocus?.focus();
    };
  }, [onDismiss]);

  const keepFocusInside = (event: ReactKeyboardEvent<HTMLElement>) => {
    if (event.key !== 'Tab') return;
    const buttons = Array.from(
      dialogRef.current?.querySelectorAll<HTMLButtonElement>('button:not(:disabled)') ?? []
    );
    if (buttons.length === 0) return;
    const firstButton = buttons[0];
    const lastButton = buttons[buttons.length - 1];
    if (event.shiftKey && document.activeElement === firstButton) {
      event.preventDefault();
      lastButton.focus();
    } else if (!event.shiftKey && document.activeElement === lastButton) {
      event.preventDefault();
      firstButton.focus();
    }
  };

  const modal = (
    <div
      className="confirmation-overlay vietsub-voice-install-overlay"
      role="presentation"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onDismiss();
      }}
    >
      <section
        ref={dialogRef}
        id="vietsub-voice-install-dialog"
        className="confirmation-card confirmation-download vietsub-voice-install-modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby="vietsub-voice-install-title"
        aria-describedby="vietsub-voice-install-description vietsub-voice-install-privacy"
        onKeyDown={keepFocusInside}
      >
        <button
          className="confirmation-close"
          type="button"
          onClick={onDismiss}
          aria-label="Đóng hộp thoại cài giọng đọc"
        >
          <X size={18} />
        </button>

        <div className="confirmation-icon confirmation-icon-download" aria-hidden="true">
          <Volume2 size={25} />
        </div>
        <span className="confirmation-eyebrow vietsub-voice-install-eyebrow">THIẾT LẬP GIỌNG ĐỌC LOCAL</span>
        <h2 id="vietsub-voice-install-title">Cài Piper và giọng Việt?</h2>
        <p id="vietsub-voice-install-description">
          VideoMaker sẽ tải các thành phần đã được cố định phiên bản. Bạn chỉ có thể tạo giọng
          sau khi quá trình cài đặt và kiểm tra hoàn tất.
        </p>

        <div className="vietsub-voice-install-summary" aria-label="Thông tin gói cài đặt">
          <div className="vietsub-voice-install-item">
            <span aria-hidden="true"><Volume2 size={18} /></span>
            <div>
              <strong>Giọng nữ tiếng Việt</strong>
              <small>{formatVoiceModel(modelVersion)}</small>
            </div>
          </div>
          <div className="vietsub-voice-install-item">
            <span aria-hidden="true"><HardDrive size={18} /></span>
            <div>
              <strong>Dung lượng tải xuống</strong>
              <small>{formatDownloadSize(requiredBytes)}</small>
            </div>
          </div>
          <div className="vietsub-voice-install-item">
            <span aria-hidden="true"><ShieldCheck size={18} /></span>
            <div>
              <strong>Được kiểm tra trước khi dùng</strong>
              <small>Phiên bản và checksum phải khớp cấu hình đã khóa.</small>
            </div>
          </div>
        </div>

        <div id="vietsub-voice-install-privacy" className="confirmation-note confirmation-note-info">
          <ShieldCheck size={17} />
          <span>
            Piper chạy trực tiếp trên máy. Nội dung phụ đề và âm thanh tạo ra không được gửi lên Cloud.
          </span>
        </div>

        <div className="confirmation-actions">
          <button className="confirmation-cancel" type="button" onClick={onDismiss}>Để sau</button>
          <button
            ref={confirmButtonRef}
            className="confirmation-submit vietsub-voice-install-submit"
            type="button"
            onClick={onConfirm}
          >
            <Download size={17} /> Tải và cài đặt
          </button>
        </div>
      </section>
    </div>
  );

  return typeof document === 'undefined' ? modal : createPortal(modal, document.body);
}

function formatDownloadSize(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes <= 0) return 'Sẽ hiển thị khi bắt đầu tải';
  const megabytes = bytes / (1024 * 1024);
  return `Khoảng ${new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 1 }).format(megabytes)} MB`;
}

function formatVoiceModel(modelVersion?: string | null): string {
  if (!modelVersion) return 'Piper · VAIS1000 Medium';
  const displayVersion = modelVersion.replace(/^vi_VN-/i, '').replace(/@.+$/, '').replace(/-/g, ' ');
  return `Piper · ${displayVersion}`;
}
