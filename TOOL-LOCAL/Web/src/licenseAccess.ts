import type { CurrentLicense } from './types';

export function isLicenseLocked(license: CurrentLicense | null | undefined): boolean {
  return Boolean(license && (!license.hasActiveLicense || !license.currentDeviceActivated ||
    (license.accessState != null && license.accessState !== 'Active')));
}

export function licenseAccessTitle(license: CurrentLicense): string {
  if (license.accessReasonCode === 'concurrent_session_limit' || license.accessState === 'SessionLimit') {
    return 'Đã đạt giới hạn phiên sử dụng';
  }
  switch (license.accessState) {
    case 'DeviceLimit': return 'Đã đạt giới hạn thiết bị';
    case 'Expired': return 'Gói sử dụng đã hết hạn';
    case 'Suspended': return 'Gói sử dụng đang bị tạm khóa';
    case 'Revoked': return 'Gói sử dụng đã bị thu hồi';
    case 'Unavailable': return 'Chưa thể xác minh quyền sử dụng';
    default: return 'Chọn gói để bắt đầu';
  }
}
