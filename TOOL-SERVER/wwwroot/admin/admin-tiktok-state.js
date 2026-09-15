/* Pure presentation rules shared by the Admin page and its regression tests. */
((root, factory) => {
  const rules = factory();
  if (typeof module === 'object' && module.exports) module.exports = rules;
  else root.videoMakerTikTokState = rules;
})(globalThis, () => {
  function describe(state, now = Date.now()) {
    const active = state.credentials.find(item => item.status === 'Active');
    const pending = state.credentials.find(item => item.status === 'Pending');
    const remaining = pending?.verificationExpiresAtUtc
      ? Math.max(0, Math.ceil((Date.parse(pending.verificationExpiresAtUtc) - now) / 1000)) : 0;
    const waiting = remaining > 0;
    const canRotate = (state.connectedAccountCount ?? state.connectedUserCount) === 0 && state.pendingPublishJobCount === 0;
    const common = { active, pending, remaining, waiting, canRotate };
    if (!state.adminManagementEnabled) return { ...common, tone: 'warning', title: 'Thao tác quản trị đang bị khóa', detail: 'Cấu hình môi trường hiện không cho phép thay đổi tại đây. Liên hệ người vận hành để kiểm tra.', action: 'refresh', actionLabel: 'Kiểm tra lại' };
    if (waiting) return { ...common, tone: 'info', title: pending.verificationRequestedByCurrentAdmin ? 'Đang chờ bạn xác minh trên Desktop' : 'Một Admin khác đang xác minh', detail: pending.verificationRequestedByCurrentAdmin ? 'Hoàn tất kết nối bằng đúng tài khoản Admin này trên VideoMaker Desktop. Trang sẽ tự cập nhật kết quả.' : 'Phiên xác minh thuộc một tài khoản Admin khác. Chờ người đó hoàn tất hoặc chờ phiên hết hạn.', action: 'refresh', actionLabel: 'Kiểm tra kết quả' };
    if (pending) return { ...common, tone: 'warning', title: pending.lastTestFailureCode ? 'Xác minh chưa thành công' : pending.verificationExpiresAtUtc ? 'Phiên xác minh đã hết hạn' : 'Thông tin đã lưu, cần xác minh', detail: canRotate ? 'Bắt đầu phiên xác minh, sau đó kết nối TikTok trên Desktop. Thông tin mới chỉ được sử dụng khi xác minh thành công.' : 'Cần ngắt các tài khoản TikTok và chờ bài đang xử lý kết thúc trước khi xác minh cấu hình mới.', action: canRotate ? 'verify' : 'guide', actionLabel: canRotate ? 'Bắt đầu xác minh' : 'Xem hướng dẫn thay cấu hình' };
    if (!active && !state.integrationEnabled) return { ...common, tone: 'info', title: 'Bắt đầu thiết lập TikTok', detail: 'Kết nối ứng dụng TikTok để người dùng VideoMaker có thể đăng video từ Desktop.', action: 'credential', actionLabel: 'Nhập thông tin ứng dụng' };
    if (!active) return { ...common, tone: 'info', title: 'TikTok đang dùng cấu hình môi trường', detail: 'Tính năng đã được bật bằng cấu hình server. Muốn quản lý tại đây, hãy thiết lập và xác minh thông tin ứng dụng.', action: 'credential', actionLabel: 'Thiết lập tại Admin' };
    return { ...common, tone: state.integrationEnabled ? 'success' : 'neutral', title: state.integrationEnabled ? 'TikTok đang hoạt động' : 'Đã xác minh · TikTok đang tắt', detail: state.integrationEnabled ? (state.auditedForPublicPosting ? 'Admin đã xác nhận điều kiện đăng công khai. Quyền đăng cụ thể vẫn do TikTok quyết định.' : 'Đang giới hạn bài đăng ở chế độ Chỉ mình tôi. Bạn có thể quản lý điều kiện đăng công khai bên dưới.') : 'Thông tin ứng dụng đã được xác minh. Mở cài đặt khi muốn cho phép người dùng kết nối và đăng video.', action: 'settings', actionLabel: 'Quản lý cài đặt' };
  }

  function settingsKey(state) {
    return JSON.stringify([state.credentials.find(item => item.status === 'Active')?.credentialId,
      state.adminManagementEnabled, state.integrationEnabled, state.auditedForPublicPosting, state.auditEvidence || '']);
  }

  function validateSettings(value) {
    const errors = {};
    if (value.auditedForPublicPosting) {
      if (!value.enabled) errors.enabled = 'Bật tính năng TikTok trước khi cho phép đăng công khai.';
      if (!value.confirmAuditApproval) errors.confirm = 'Bạn cần xác nhận TikTok đã phê duyệt đăng công khai.';
      const evidence = (value.auditEvidence || '').trim();
      if (evidence.length < 8 || evidence.length > 500 || /[\u0000-\u001f\u007f]/.test(evidence))
        errors.evidence = 'Nhập mã xét duyệt, ticket hoặc URL từ 8–500 ký tự, trên một dòng.';
    }
    return errors;
  }

  function failureMessage(code) {
    const messages = {
      invalid_client: 'Client Key hoặc Client Secret chưa đúng. Kiểm tra lại trong TikTok Developer Portal.',
      invalid_grant: 'Mã xác minh đã hết hạn hoặc không còn hợp lệ. Hãy bắt đầu một phiên xác minh mới.',
      scope_not_authorized: 'Ứng dụng hoặc tài khoản chưa được cấp quyền đăng video (video.publish).',
      provider_unavailable: 'Chưa kết nối được TikTok. Kiểm tra mạng rồi thử lại.',
      provider_timeout: 'TikTok phản hồi quá lâu. Bạn có thể thử xác minh lại.',
      tiktok_rotation_in_use: 'Còn tài khoản kết nối hoặc bài chưa kết thúc. Ngắt kết nối trên Desktop và chờ xử lý xong.',
      tiktok_credential_verification_expired: 'Phiên xác minh đã hết hạn. Hãy yêu cầu xác minh lại.',
      tiktok_admin_management_disabled: 'Cấu hình môi trường đang khóa thao tác quản trị TikTok.',
      tiktok_active_credential_required: 'Cần xác minh thông tin ứng dụng trước khi bật tính năng.'
    };
    return messages[code] || 'Chưa hoàn tất được thao tác. Kiểm tra thông tin ứng dụng, quyền video.publish và kết nối mạng trước khi thử lại.';
  }

  return { describe, settingsKey, validateSettings, failureMessage };
});
