import { useState } from 'react';
import type { TikTokMedia } from './types';

export function TikTokVideoPreview({ media }: { media: TikTokMedia }) {
  const [failed, setFailed] = useState(false);
  const [attempt, setAttempt] = useState(0);
  const ratio = media.width > 0 && media.height > 0 ? media.width / media.height : 16 / 9;

  return (
    <div className="tiktok-preview-wrap" style={{ aspectRatio: ratio, maxWidth: `calc(var(--tiktok-preview-height, 360px) * ${ratio})` }}>
      <video
        key={attempt}
        controls
        playsInline
        preload="metadata"
        src={media.previewUrl}
        aria-label={`Xem trước ${media.fileName}`}
        onError={() => setFailed(true)}
      />
      {failed && (
        <div className="tiktok-preview-error" role="alert">
          <strong>Chưa thể phát video xem trước</strong>
          <p>Hãy thử tải lại. Nếu vẫn không phát được, chọn lại file hoặc dùng video MP4 mã hóa H.264.</p>
          <button className="start-button" type="button" onClick={() => {
            setFailed(false);
            setAttempt(current => current + 1);
          }}>Tải lại video</button>
        </div>
      )}
    </div>
  );
}
