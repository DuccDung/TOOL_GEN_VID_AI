import { useEffect, useRef } from 'react';
import { ArrowRight, Check, Crown, LoaderCircle, Monitor, RefreshCw, Sparkles, TriangleAlert, X } from 'lucide-react';
import type { CurrentLicense, LicenseOffer } from '../../types';
import type { LicenseInformationView } from './useLicenseInformation';
import './licenseInformation.css';

export function LicenseInformationDialog({ view, license, offers, loading, error, onClose, onChangeView, onRetry }: {
  view: LicenseInformationView;
  license: CurrentLicense | null | undefined;
  offers: LicenseOffer[];
  loading: boolean;
  error: string | null;
  onClose: () => void;
  onChangeView: (view: LicenseInformationView) => void;
  onRetry: () => void;
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  const body = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const element = dialog.current!;
    const previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    element.showModal();
    return () => {
      element.close();
      if (previousFocus?.isConnected && !previousFocus.closest('[inert]')) previousFocus.focus();
    };
  }, []);
  useEffect(() => { body.current?.scrollTo({ top: 0, behavior: 'auto' }); }, [view]);

  const current = view === 'current';
  const active = Boolean(license?.hasActiveLicense);
  const sortedOffers = [...offers].sort((a, b) => a.displayOrder - b.displayOrder || a.priceVnd - b.priceVnd);

  return <dialog ref={dialog} className="license-information-dialog" aria-modal="true"
    aria-labelledby="license-information-title" aria-describedby="license-information-description"
    onKeyDown={event => {
      if (event.key !== 'Tab') return;
      const buttons = [...event.currentTarget.querySelectorAll<HTMLButtonElement>('button:not(:disabled)')];
      const first = buttons[0];
      const last = buttons.at(-1);
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault(); last?.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault(); first?.focus();
      }
    }}
    onCancel={event => { event.preventDefault(); onClose(); }}>
    <header className="license-information-heading">
      <span className="license-information-icon">{current ? <Crown size={25} /> : <Sparkles size={25} />}</span>
      <div>
        <span className="license-information-eyebrow">GÓI SỬ DỤNG</span>
        <h2 id="license-information-title">{current ? 'Thông tin gói' : 'Nâng cấp gói'}</h2>
        <p id="license-information-description">{current
          ? 'Thông tin gói đang gắn với tài khoản của bạn.'
          : 'Xem giá, thời hạn và quyền lợi của các gói đang được cung cấp.'}</p>
      </div>
      <button type="button" className="license-information-close" aria-label="Đóng thông tin gói" onClick={onClose}><X size={20} /></button>
    </header>

    <div className="license-information-body" ref={body}>
      {current ? <>
        <section className="license-information-summary">
          <div><span>Gói hiện tại</span><h3>{license?.planName || (active ? 'Gói đang sử dụng' : 'Chưa có gói')}</h3></div>
          <span className={`license-information-status ${active ? 'active' : ''}`}>{licenseStatus(license)}</span>
        </section>
        {!license ? <p className="license-information-message" role="status">Chưa nhận được thông tin gói. Hãy đóng cửa sổ và làm mới dữ liệu tài khoản.</p> : <>
          <dl className="license-information-details">
            <div><dt>Ngày bắt đầu</dt><dd>{formatLicenseDate(license.startsAtUtc)}</dd></div>
            <div><dt>Ngày hết hạn</dt><dd>{license.expiresAtUtc ? formatLicenseDate(license.expiresAtUtc) : active ? 'Không giới hạn' : 'Chưa có thông tin'}</dd></div>
            <div><dt>Thiết bị đã kích hoạt</dt><dd>{license.activeDeviceCount} / {license.maxActivatedDevices} thiết bị</dd></div>
            <div><dt>Thiết bị hiện tại</dt><dd>{license.currentDeviceActivated ? 'Đã kích hoạt' : 'Chưa kích hoạt'}</dd></div>
            {license.assignedOrganizationName && <div><dt>Tổ chức được cấp cùng gói</dt><dd>{license.assignedOrganizationName}</dd></div>}
          </dl>
          {license.accessMessage && license.accessState !== 'Active' && <p className="license-information-message">{license.accessMessage}</p>}
        </>}
      </> : <>
        {license?.planName && <div className="license-information-current"><Crown size={17} /><span>Gói hiện tại: <strong>{license.planName}</strong></span></div>}
        {loading ? <div className="license-information-feedback" role="status"><LoaderCircle size={24} className="spin" /><p>Đang tải các gói sử dụng…</p></div>
          : error ? <div className="license-information-feedback error" role="alert"><TriangleAlert size={25} /><p>{error}</p><button type="button" onClick={onRetry}><RefreshCw size={16} />Thử lại</button></div>
          : sortedOffers.length === 0 ? <div className="license-information-feedback" role="status"><Crown size={26} /><h3>Chưa có gói được mở bán</h3><p>Vui lòng liên hệ quản trị viên hoặc thử tải lại sau.</p><button type="button" onClick={onRetry}><RefreshCw size={16} />Tải lại</button></div>
          : <div className="license-information-offers">
            {sortedOffers.map(offer => <article className={`license-information-offer ${offer.planCode === license?.planCode ? 'current' : ''}`} key={offer.licensePlanId}>
              <div className="license-information-offer-heading"><span>{offer.durationDays} ngày sử dụng</span>{offer.planCode === license?.planCode && <span className="license-information-badge">Gói hiện tại</span>}</div>
              <h3>{offer.name}</h3>
              {offer.description && <p>{offer.description}</p>}
              <div className="license-information-price">{new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND', maximumFractionDigits: 0 }).format(offer.priceVnd)}</div>
              <div className="license-information-device"><Monitor size={16} />Tối đa {offer.maxActivatedDevices} thiết bị</div>
              <ul>{offer.marketingFeatures.map((feature, index) => <li key={index}><Check size={16} /><span>{feature}</span></li>)}</ul>
              <div className={`license-information-availability ${offer.organizationSeatAvailable ? 'available' : ''}`}>{offer.organizationSeatAvailable ? 'Đang còn chỗ' : 'Tạm hết chỗ'}</div>
            </article>)}
          </div>}
        {!loading && !error && sortedOffers.length > 0 && <p className="license-information-note">Liên hệ quản trị viên để được hỗ trợ nâng cấp hoặc gia hạn gói.</p>}
      </>}
    </div>

    <footer className="license-information-footer">
      <button type="button" className="license-information-secondary" onClick={onClose}>Đóng</button>
      <button type="button" className="license-information-primary" onClick={() => onChangeView(current ? 'offers' : 'current')}>
        {current ? <><Sparkles size={17} />Xem các gói nâng cấp<ArrowRight size={16} /></> : <><Crown size={17} />Xem gói hiện tại</>}
      </button>
    </footer>
  </dialog>;
}

function formatLicenseDate(value?: string | null): string {
  if (!value) return 'Chưa có thông tin';
  const timestamp = value.trim();
  const date = new Date(/(?:Z|[+-]\d{2}:\d{2})$/i.test(timestamp) ? timestamp : `${timestamp}Z`);
  return Number.isNaN(date.getTime()) ? 'Chưa có thông tin' : new Intl.DateTimeFormat('vi-VN', { dateStyle: 'long' }).format(date);
}

function licenseStatus(license: CurrentLicense | null | undefined): string {
  if (!license) return 'Chưa có thông tin';
  switch (license.accessState) {
    case 'Expired': return 'Đã hết hạn';
    case 'Suspended': return 'Tạm khóa';
    case 'Revoked': return 'Đã thu hồi';
    case 'Unavailable': return 'Chưa thể xác minh';
    case 'DeviceLimit': return 'Đã đạt giới hạn thiết bị';
    case 'SessionLimit': return 'Đã đạt giới hạn phiên';
  }
  return license.hasActiveLicense ? (license.status === 'Trial' ? 'Đang dùng thử' : 'Đang hoạt động') : 'Chưa có gói hoạt động';
}
