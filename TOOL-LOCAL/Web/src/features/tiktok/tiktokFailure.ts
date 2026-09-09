export function formatTikTokFailure(reason?: string | null): string {
  if (!reason) return 'TikTok không thể xuất bản video.';
  const known: Record<string, string> = {
    file_format_check_failed: 'Định dạng video không vượt qua kiểm tra của TikTok.',
    duration_check_failed: 'Thời lượng video không được tài khoản TikTok chấp nhận.',
    frame_rate_check_failed: 'Tốc độ khung hình không được TikTok chấp nhận.',
    picture_size_check_failed: 'Kích thước khung hình không được TikTok chấp nhận.',
    upload_expired: 'Phiên tải video đã hết hạn. Hãy chọn lại video và đăng lại.',
    status_timeout: 'TikTok chưa hoàn tất bài đăng trong thời gian cho phép. Hãy kiểm tra tài khoản trước khi thử lại.',
    connection_revoked: 'Kết nối TikTok đã bị ngắt trước khi bài đăng hoàn tất.',
    connection_replaced: 'Tài khoản TikTok đã được thay đổi trước khi bài đăng hoàn tất.'
  };
  return known[reason] ?? `TikTok từ chối bài đăng (${reason}).`;
}
