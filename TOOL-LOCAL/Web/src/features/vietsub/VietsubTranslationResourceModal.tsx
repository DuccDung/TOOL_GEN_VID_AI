import { useEffect, useRef } from 'react';
import { Play, TriangleAlert, X } from 'lucide-react';
import type { VietsubTranslationResourceAlert } from './types';

export function VietsubTranslationResourceModal({
  alert,
  onDismiss,
  onContinue
}: {
  alert: VietsubTranslationResourceAlert;
  onDismiss: () => void;
  onContinue: () => void;
}) {
  const cancelButtonRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    const previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    cancelButtonRef.current?.focus();

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

  return (
    <div
      className="confirmation-overlay"
      role="presentation"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onDismiss();
      }}
    >
      <section
        className="confirmation-card service-error-card vietsub-translation-resource-modal"
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="vietsub-translation-resource-title"
        aria-describedby="vietsub-translation-resource-description vietsub-translation-resource-guidance"
      >
        <button
          className="confirmation-close"
          type="button"
          onClick={onDismiss}
          aria-label="Đóng cảnh báo tài nguyên"
        >
          <X size={18} />
        </button>
        <div className="confirmation-icon service-error-icon" aria-hidden="true">
          <TriangleAlert size={25} />
        </div>
        <span className="confirmation-eyebrow service-error-eyebrow">CẢNH BÁO TÀI NGUYÊN</span>
        <h2 id="vietsub-translation-resource-title">{alert.title}</h2>
        <p id="vietsub-translation-resource-description">{alert.message}</p>
        <div
          id="vietsub-translation-resource-guidance"
          className="confirmation-note vietsub-translation-resource-guidance"
        >
          <TriangleAlert size={17} />
          <span>
            Bạn có thể đóng bớt ứng dụng để giảm rủi ro. Nếu vẫn tiếp tục, VideoMaker sẽ thử nạp
            worker; tác vụ chỉ dừng khi model hoặc worker thực sự không thể chạy.
          </span>
        </div>
        <div className="confirmation-actions">
          <button
            ref={cancelButtonRef}
            className="confirmation-cancel"
            type="button"
            onClick={onDismiss}
          >
            Hủy
          </button>
          <button
            className="confirmation-submit service-error-submit"
            type="button"
            onClick={onContinue}
          >
            <Play size={17} /> Vẫn tiếp tục
          </button>
        </div>
      </section>
    </div>
  );
}
