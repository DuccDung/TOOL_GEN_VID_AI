export function describeTikTokUnavailable(reason?: string | null) {
  switch (reason) {
    case 'verification_other_account':
      return { title: 'Phiên xác minh thuộc tài khoản khác', detail: 'Đăng nhập Desktop bằng đúng tài khoản VideoMaker đã bấm Bắt đầu xác minh trong Admin, rồi bấm Kiểm tra lại.' };
    case 'verification_expired':
      return { title: 'Phiên xác minh TikTok đã hết hạn', detail: 'Quay lại Admin để bắt đầu phiên xác minh mới, sau đó bấm Kiểm tra lại tại đây và hoàn tất kết nối trong 15 phút.' };
    case 'emergency_disabled':
      return { title: 'TikTok đang bị khóa trên server', detail: 'Người vận hành cần kiểm tra cấu hình dừng khẩn cấp trước khi bạn có thể xác minh hoặc kết nối TikTok.' };
    case 'integration_disabled':
      return { title: 'Tính năng TikTok đang tạm tắt', detail: 'Admin cần bật Cho phép sử dụng TikTok và lưu cài đặt. Nếu đang thay thông tin ứng dụng, hãy hoàn tất xác minh bằng đúng tài khoản Admin trước.' };
    default:
      return { title: 'TikTok chưa sẵn sàng cho tài khoản này', detail: 'Nếu Admin đã bắt đầu xác minh, hãy dùng đúng tài khoản VideoMaker đó trên Desktop và bấm Kiểm tra lại. Nếu vẫn chưa kết nối được, kiểm tra Desktop và trang Admin có dùng cùng server hay không.' };
  }
}
