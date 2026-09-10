import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it, vi } from 'vitest';
import { TikTokPage } from './TikTokPage';
import type { useTikTokModule } from './useTikTokModule';

function render(feature: ReturnType<typeof useTikTokModule>['state']['feature'], error: string | null = null,
  creator: ReturnType<typeof useTikTokModule>['state']['creator'] = null,
  history: ReturnType<typeof useTikTokModule>['state']['history'] = null) {
  const module: ReturnType<typeof useTikTokModule> = {
    state: { feature, error, creator, media: null, upload: null, publish: null, loading: false, busy: false, uploadCompleted: false, publishFeedback: null,
      selectedConnectionId: feature.connection?.connectionId ?? null, jobs: [], history, historyLoading: false },
    refresh: vi.fn(), refreshCreator: vi.fn(), connect: vi.fn(), disconnect: vi.fn(), selectMedia: vi.fn(),
    clearMedia: vi.fn(), publish: vi.fn(), cancel: vi.fn(), clearError: vi.fn(), openPolicy: vi.fn(),
    hidePublishFeedback: vi.fn(), showPublishFeedback: vi.fn(), selectAccount: vi.fn(), loadHistory: vi.fn(), showJob: vi.fn(), newPublishIntent: vi.fn()
  };
  return renderToStaticMarkup(createElement(TikTokPage, { module }));
}

describe('TikTok verification readiness', () => {
  it('keeps account management and history reachable while the integration is disabled', () => {
    const html = render({ enabled: false, configured: true, unavailableReason: 'integration_disabled', connections: [{
      connectionId: 'a', creatorUsername: 'creator-a', creatorNickname: 'Creator A', scopes: ['video.publish'],
      accessTokenExpiresAtUtc: '', refreshTokenExpiresAtUtc: '', updatedAtUtc: ''
    }] });
    expect(html).toContain('Quản lý (');
    expect(html).toContain('Lịch sử đăng');
    expect(html).toContain('>Ngắt</button>');
    expect(html).not.toContain('Thêm tài khoản</button>');
  });
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

describe('TikTok history account identity', () => {
  const accounts = ['a', 'b'].map(connectionId => ({
    connectionId, creatorUsername: 'current-' + connectionId, creatorNickname: 'Current ' + connectionId,
    scopes: ['video.publish'], accessTokenExpiresAtUtc: '', refreshTokenExpiresAtUtc: '', updatedAtUtc: ''
  }));
  const legacyJob = {
    publishJobId: 'legacy-a', connectionId: 'a', creatorUsername: null, creatorNickname: null,
    status: 'PUBLISH_COMPLETE', isTerminal: true, uploadedBytes: 10, publicPostIds: [],
    createdAtUtc: '2026-09-09T06:05:20Z', updatedAtUtc: '2026-09-09T06:06:20Z'
  };
  const feature = { enabled: true, configured: true, connection: accounts[1], connections: accounts };
  const rows = (html: string) => [...html.matchAll(/<li>(.*?)<\/li>/g)].map(match => match[1]);

  it('shows each legacy job under its linked account, even when another account is selected', () => {
    const html = render(feature, null, null, {
      items: [legacyJob, { ...legacyJob, publishJobId: 'legacy-b', connectionId: 'b' }],
      page: 1, pageSize: 20, totalCount: 2
    });
    const historyRows = rows(html);
    expect(historyRows).toHaveLength(2);
    expect(historyRows[0]).toContain('<strong>@current-a</strong>');
    expect(historyRows[0]).not.toContain('@current-b');
    expect(historyRows[1]).toContain('<strong>@current-b</strong>');
    for (const row of historyRows) {
      expect(row).toContain('Đang hiển thị tên hiện tại');
      expect(row).toContain('Đã đăng');
      expect(row).not.toContain('Không có thông tin tài khoản lúc đăng');
    }
  });

  it('preserves the recorded username or nickname after the current account changes name', () => {
    const historyRows = rows(render(feature, null, null, {
      items: [{ ...legacyJob, creatorUsername: 'original-a' },
        { ...legacyJob, publishJobId: 'nickname-only', creatorNickname: 'Original Nickname' }],
      page: 1, pageSize: 20, totalCount: 2
    }));
    expect(historyRows[0]).toContain('<strong>@original-a</strong>');
    expect(historyRows[1]).toContain('<strong>Original Nickname</strong>');
    for (const row of historyRows) {
      expect(row).not.toContain('@current-');
      expect(row).not.toContain('Đang hiển thị tên hiện tại');
    }
  });

  it('does not attribute a job with an unknown connection to the selected account', () => {
    const historyRows = rows(render(feature, null, null, {
      items: [{ ...legacyJob, connectionId: 'missing' }, { ...legacyJob, publishJobId: 'no-connection', connectionId: null }],
      page: 1, pageSize: 20, totalCount: 2
    }));
    for (const row of historyRows) {
      expect(row).toContain('Không xác định được tài khoản');
      expect(row).not.toContain('@current-');
      expect(row).toContain('Đã đăng');
    }
  });
});
