import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it, vi } from 'vitest';
import { TikTokPage } from './TikTokPage';
import type { useTikTokModule } from './useTikTokModule';

function render(feature: ReturnType<typeof useTikTokModule>['state']['feature'], error: string | null = null,
  creator: ReturnType<typeof useTikTokModule>['state']['creator'] = null) {
  const module: ReturnType<typeof useTikTokModule> = {
    state: { feature, error, creator, media: null, upload: null, publish: null, loading: false, busy: false, uploadCompleted: false, publishFeedback: null },
    refresh: vi.fn(), refreshCreator: vi.fn(), connect: vi.fn(), disconnect: vi.fn(), selectMedia: vi.fn(),
    clearMedia: vi.fn(), publish: vi.fn(), cancel: vi.fn(), clearError: vi.fn(), openPolicy: vi.fn(),
    hidePublishFeedback: vi.fn(), showPublishFeedback: vi.fn()
  };
  return renderToStaticMarkup(createElement(TikTokPage, { module }));
}

describe('TikTok verification readiness', () => {
  it('explains private-account setup while retaining the connected identity and a retry action', () => {
    const html = render({ enabled: true, configured: true, connection: {
      connectionId: 'test', creatorUsername: 'creator-a', creatorNickname: 'Creator A', scopes: ['video.publish'],
      accessTokenExpiresAtUtc: '', refreshTokenExpiresAtUtc: '', updatedAtUtc: ''
    } }, null, { creatorUsername: 'creator-a', creatorNickname: 'Creator A', privacyLevelOptions: ['SELF_ONLY'],
      commentDisabled: false, duetDisabled: false, stitchDisabled: false, maximumVideoDurationSeconds: 600,
      publishingIssue: { code: 'tiktok_private_test_account_required', message: 'Safe setup requirement' }
    });
    expect(html).toContain('Cần chuẩn bị tài khoản trước khi đăng');
    expect(html).toContain('Tài khoản riêng tư');
    expect(html).toContain('Kiểm tra lại');
    expect(html).toContain('Creator A');
    expect(html).toContain('Chỉ mình tôi');
    expect(html).not.toContain('tiktok-error');
  });
  it('shows refresh failures even before the integration is ready', () => {
    const html = render({ enabled: false, configured: false }, 'Không kết nối được server thử nghiệm.');
    expect(html).toContain('Không kết nối được server thử nghiệm.');
    expect(html).toContain('Kiểm tra lại');
    expect(html).toContain('cùng server');
  });

  it.each([
    ['verification_other_account', 'Phiên xác minh thuộc tài khoản khác'],
    ['verification_expired', 'Phiên xác minh TikTok đã hết hạn'],
    ['emergency_disabled', 'TikTok đang bị khóa trên server'],
    ['integration_disabled', 'Tính năng TikTok đang tạm tắt']
  ])('explains %s without exposing a connect action', (unavailableReason, title) => {
    const html = render({ enabled: false, configured: true, unavailableReason });
    expect(html).toContain(title);
    expect(html).not.toContain('Kết nối TikTok</button>');
  });

  it('lets the requesting Admin connect while the integration is awaiting verification', () => {
    const html = render({ enabled: true, configured: true, isCredentialVerification: true });
    expect(html).toContain('Xác minh ứng dụng TikTok');
    expect(html).toContain('Cài đặt Admin sẽ mở sau khi xác minh thành công');
    expect(html).toContain('Kết nối TikTok');
    expect(html).not.toContain('disabled');
  });
});
