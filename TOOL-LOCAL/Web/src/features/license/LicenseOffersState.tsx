import { Clock3, CreditCard, LoaderCircle, RefreshCw, TriangleAlert } from 'lucide-react';
import type { LicenseOffersStatus } from './useLicenseOffers';
import './licenseOffersState.css';

export function LicenseOffersState({ status, error, onRetry, disabled = false, titleId, descriptionId }: {
  status: LicenseOffersStatus;
  error: string | null;
  onRetry: () => void;
  disabled?: boolean;
  titleId?: string;
  descriptionId?: string;
}) {
  const loading = status === 'idle' || status === 'loading';
  const failed = status === 'error';
  return <div className={'license-offers-state' + (failed ? ' is-error' : '')}
    role={failed ? 'alert' : 'status'} aria-live={failed ? 'assertive' : 'polite'} aria-busy={loading}>
    <div className="license-offers-state-icon" aria-hidden="true">
      {loading ? <LoaderCircle size={44} className="spin" /> : failed ? <TriangleAlert size={44} /> : <>
        <CreditCard size={48} strokeWidth={1.8} />
        <span className="license-offers-state-clock"><Clock3 size={30} strokeWidth={1.8} /></span>
      </>}
    </div>
    <span className="license-offers-state-eyebrow">GÓI SỬ DỤNG</span>
    <h2 id={titleId}>{loading ? 'Đang tải các gói sử dụng' : failed
      ? 'Chưa thể tải danh sách gói' : 'Hiện chưa có gói sử dụng'}</h2>
    <p id={descriptionId}>{loading
      ? 'Vui lòng chờ trong khi hệ thống kiểm tra các gói đang được cung cấp.'
      : failed ? error || 'Vui lòng kiểm tra kết nối và thử lại.' : <>
        <span>Hệ thống hiện chưa cung cấp gói sử dụng.</span>
        <span>Vui lòng quay lại sau hoặc liên hệ hỗ trợ.</span>
      </>}</p>
    <button type="button" className="license-offers-state-action" disabled={loading || disabled} onClick={onRetry}>
      <RefreshCw size={19} className={loading ? 'spin' : undefined} aria-hidden="true" />
      {loading ? 'Đang kiểm tra…' : 'Kiểm tra lại'}
    </button>
  </div>;
}
