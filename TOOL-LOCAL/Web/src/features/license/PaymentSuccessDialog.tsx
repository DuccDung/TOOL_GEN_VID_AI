import { useEffect, useRef } from 'react';
import { ArrowRight, Check, Crown, LoaderCircle, LogOut, RefreshCw } from 'lucide-react';
import type { CurrentLicense, LicensePaymentCheckout } from '../../types';
import './paymentSuccess.css';

export function PaymentSuccessDialog({ payment, license, ready, busy, error, logoutBusy, onContinue, onRetry, onLogout }: {
  payment: LicensePaymentCheckout;
  license: CurrentLicense | null | undefined;
  ready: boolean;
  busy: boolean;
  error: string | null;
  logoutBusy: boolean;
  onContinue: () => void;
  onRetry: () => void;
  onLogout: () => void;
}) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const primaryRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    const dialog = dialogRef.current!;
    const previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    dialog.showModal();
    return () => {
      dialog.close();
      if (previousFocus?.isConnected && !previousFocus.closest('[inert]')) previousFocus.focus();
    };
  }, []);

  useEffect(() => {
    if (ready || error) primaryRef.current?.focus();
  }, [ready, error]);

  const currentPlan = license?.planCode === payment.planCode ? license : null;
  const startsAt = payment.startsAtUtc || currentPlan?.startsAtUtc;
  const expiresAt = payment.licenseExpiresAtUtc || currentPlan?.expiresAtUtc;
  const organizationName = payment.assignedOrganizationName || currentPlan?.assignedOrganizationName;

  return <dialog ref={dialogRef} className="payment-success-dialog" aria-modal="true"
    aria-labelledby="payment-success-title" aria-describedby="payment-success-description" tabIndex={-1}
    onCancel={event => event.preventDefault()}
    onKeyDown={event => {
      if (event.key !== 'Tab') return;
      const buttons = [...event.currentTarget.querySelectorAll<HTMLButtonElement>('button:not(:disabled)')];
      const first = buttons[0];
      const last = buttons.at(-1);
      if (!first) { event.preventDefault(); dialogRef.current?.focus(); }
      else if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus(); }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    }}>
    <header className="payment-success-brand">
      <span className="license-gate-brand-mark"><img src="/brand/taphoatool-logo.png" alt="" /></span>
      <span>taphoatool</span>
    </header>

    <div className="payment-success-body">
      <div className="payment-success-symbol" aria-hidden="true"><Check size={34} strokeWidth={2.8} /></div>
      <span className="payment-success-eyebrow">THANH TOÁN HOÀN TẤT</span>
      <h1 id="payment-success-title">Thanh toán thành công!</h1>
      <p id="payment-success-description">Cảm ơn bạn đã lựa chọn taphoatool.<br />Thanh toán của bạn đã được xác nhận.</p>

      <div className="payment-success-plan">
        <span className="payment-success-plan-icon" aria-hidden="true"><Crown size={23} /></span>
        <div><span>Gói sử dụng của bạn</span><strong>{payment.planName}</strong></div>
        <span className="payment-success-duration">{payment.durationDays} ngày</span>
      </div>

      <dl className="payment-success-details">
        <div className="payment-success-amount"><dt>Đã thanh toán</dt><dd>{new Intl.NumberFormat('vi-VN').format(payment.amountVnd)} đ</dd></div>
        {startsAt && <div><dt>Ngày bắt đầu</dt><dd>{formatPaymentDate(startsAt)}</dd></div>}
        {expiresAt && <div><dt>Ngày hết hạn</dt><dd>{formatPaymentDate(expiresAt)}</dd></div>}
        {organizationName && <div><dt>Tổ chức</dt><dd>{organizationName}</dd></div>}
        <div className="payment-success-order"><dt>Mã đơn hàng</dt><dd>{payment.orderCode}</dd></div>
      </dl>

      <p className={`payment-success-status${error && !ready ? ' has-error' : ''}`}
        role={error && !ready ? 'alert' : 'status'}>
        {ready ? <><Check size={16} aria-hidden="true" />Gói đã kích hoạt. Bạn có thể bắt đầu sử dụng.</>
          : error ? error : <><LoaderCircle size={16} className="spin" aria-hidden="true" />Đang hoàn tất kích hoạt gói cho thiết bị này…</>}
      </p>

      <button ref={primaryRef} type="button" className="payment-success-primary"
        disabled={logoutBusy || !ready && (busy || !error)}
        onClick={ready ? onContinue : onRetry}>
        {ready ? <>Vào ứng dụng<ArrowRight size={19} aria-hidden="true" /></>
          : busy || !error ? <><LoaderCircle size={18} className="spin" aria-hidden="true" />Đang kích hoạt gói…</>
            : <><RefreshCw size={18} aria-hidden="true" />Kiểm tra kích hoạt</>}
      </button>
      <p className="payment-success-footnote">Thông tin gói được lưu trong tài khoản của bạn.</p>
      {!ready && error && <button type="button" className="payment-success-logout" disabled={logoutBusy} onClick={onLogout}>
        <LogOut size={15} aria-hidden="true" />{logoutBusy ? 'Đang đăng xuất…' : 'Đăng xuất'}
      </button>}
    </div>
  </dialog>;
}

function formatPaymentDate(value: string) {
  const timestamp = value.trim();
  const utc = /(?:Z|[+-]\d{2}:\d{2})$/i.test(timestamp) ? timestamp : `${timestamp}Z`;
  const date = new Date(utc);
  return Number.isNaN(date.getTime()) ? 'Đang cập nhật' : date.toLocaleDateString('vi-VN', { day: '2-digit', month: '2-digit', year: 'numeric' });
}
