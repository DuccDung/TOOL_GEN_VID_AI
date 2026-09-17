import { useEffect, useRef } from 'react';
import { createPortal } from 'react-dom';
import { Cpu } from 'lucide-react';

export function VietsubTranslationGpuFallbackModal({ message, onDismiss }: {
  message: string;
  onDismiss: () => void;
}) {
  const buttonRef = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    const previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    buttonRef.current?.focus();
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') { event.preventDefault(); onDismiss(); }
      if (event.key === 'Tab') { event.preventDefault(); buttonRef.current?.focus(); }
    };
    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.body.style.overflow = previousOverflow;
      document.removeEventListener('keydown', onKeyDown);
      if (previousFocus?.isConnected) previousFocus.focus();
    };
  }, [onDismiss]);

  const modal = <div className="confirmation-overlay vietsub-resource-modal-overlay" role="presentation">
    <section className="confirmation-card vietsub-translation-resource-modal" role="alertdialog" aria-modal="true"
      aria-labelledby="vietsub-gpu-fallback-title" aria-describedby="vietsub-gpu-fallback-message">
      <div className="confirmation-icon" aria-hidden="true"><Cpu size={25} /></div>
      <h2 id="vietsub-gpu-fallback-title">Đã chuyển sang dịch bằng CPU</h2>
      <p id="vietsub-gpu-fallback-message">{message}</p>
      <p>Không cần bấm dịch lại. Bạn có thể đóng thông báo này để theo dõi tiến độ.</p>
      <div className="confirmation-actions">
        <button ref={buttonRef} type="button" className="confirmation-submit" onClick={onDismiss}>Đã hiểu</button>
      </div>
    </section>
  </div>;
  return typeof document === 'undefined' ? modal : createPortal(modal, document.body);
}
