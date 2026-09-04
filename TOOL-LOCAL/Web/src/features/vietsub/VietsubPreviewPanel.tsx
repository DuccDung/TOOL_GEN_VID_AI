import type { RefObject } from 'react';
import {
  Captions,
  CaptionsOff,
  Copy,
  FileVideo2,
  HardDrive,
  Link2,
  Pause,
  Play,
  TriangleAlert,
  Volume2,
  VolumeX
} from 'lucide-react';
import type { VietsubMediaImportProgress, VietsubProjectSummary } from './types';

type VietsubPreviewPanelProps = {
  project: VietsubProjectSummary;
  videoRef: RefObject<HTMLVideoElement | null>;
  progress?: VietsubMediaImportProgress | null;
  busy: boolean;
  playheadMilliseconds: number;
  durationMilliseconds: number;
  playing: boolean;
  playbackRate: number;
  volume: number;
  muted: boolean;
  subtitlesVisible: boolean;
  activeSubtitleText?: string | null;
  onImportMedia: (mode: 'COPY' | 'LINK') => void;
  onPlayheadChange: (milliseconds: number) => void;
  onDurationChange: (milliseconds: number) => void;
  onPlayingChange: (playing: boolean) => void;
  onTogglePlaying: () => void;
  onSeek: (milliseconds: number) => void;
  onPlaybackRateChange: (rate: number) => void;
  onVolumeChange: (volume: number) => void;
  onToggleMuted: () => void;
  onToggleSubtitles: () => void;
};

export function VietsubPreviewPanel({
  project,
  videoRef,
  progress,
  busy,
  playheadMilliseconds,
  durationMilliseconds,
  playing,
  playbackRate,
  volume,
  muted,
  subtitlesVisible,
  activeSubtitleText,
  onImportMedia,
  onPlayheadChange,
  onDurationChange,
  onPlayingChange,
  onTogglePlaying,
  onSeek,
  onPlaybackRateChange,
  onVolumeChange,
  onToggleMuted,
  onToggleSubtitles
}: VietsubPreviewPanelProps) {
  const media = project.sourceVideo;

  return (
    <section className="card vietsub-editor-panel vietsub-preview-panel">
      <div className="vietsub-panel-heading vietsub-preview-heading">
        <div><span className="vietsub-eyebrow">XEM TRƯỚC</span><h3>{media?.fileName ?? 'Video nguồn'}</h3></div>
        {media && (
          <div className="vietsub-media-badges">
            <span><HardDrive size={14} /> {media.importMode === 'COPY' ? 'Đã sao chép' : 'Đang liên kết'}</span>
            <span>{formatDuration(media.durationSeconds)}</span>
            <span>{media.width} × {media.height}</span>
          </div>
        )}
      </div>

      {!media ? (
        <div className="vietsub-preview-empty">
          <div className="vietsub-preview-empty-icon"><FileVideo2 size={34} /></div>
          <strong>Thêm video để bắt đầu biên tập</strong>
          <p>Video gốc chỉ được đọc. Bạn có thể sao chép vào workspace hoặc liên kết tệp hiện có.</p>
          <div className="vietsub-preview-import-actions">
            <button type="button" disabled={busy} onClick={() => onImportMedia('COPY')}><Copy size={16} /> Sao chép vào dự án</button>
            <button type="button" disabled={busy} onClick={() => onImportMedia('LINK')}><Link2 size={16} /> Liên kết tệp</button>
          </div>
          {progress && (
            <div className="vietsub-import-progress">
              <div><strong>Đang kiểm tra và nhập video</strong><span>{progress.percent.toFixed(0)}%</span></div>
              <div className="vietsub-progress-track"><span style={{ width: `${progress.percent}%` }} /></div>
              <small>{formatBytes(progress.bytesProcessed)} / {formatBytes(progress.totalBytes)} · {progress.megabytesPerSecond.toFixed(1)} MB/s</small>
            </div>
          )}
        </div>
      ) : (
        <>
          <div className="vietsub-editor-video-stage">
            {media.playbackUrl ? (
              <>
                <video
                  ref={videoRef}
                  preload="metadata"
                  src={media.playbackUrl}
                  onClick={onTogglePlaying}
                  onLoadedMetadata={(event) => {
                    const video = event.currentTarget;
                    video.playbackRate = playbackRate;
                    video.volume = volume;
                    video.muted = muted;
                    onDurationChange(durationToMilliseconds(video.duration));
                  }}
                  onDurationChange={(event) => onDurationChange(durationToMilliseconds(event.currentTarget.duration))}
                  onTimeUpdate={(event) => onPlayheadChange(Math.round(event.currentTarget.currentTime * 1000))}
                  onSeeked={(event) => onPlayheadChange(Math.round(event.currentTarget.currentTime * 1000))}
                  onPlay={() => onPlayingChange(true)}
                  onPause={() => onPlayingChange(false)}
                  onEnded={() => onPlayingChange(false)}
                />
                {subtitlesVisible && activeSubtitleText?.trim() && (
                  <div className="vietsub-preview-subtitle-overlay" aria-live="off">
                    {activeSubtitleText}
                  </div>
                )}
              </>
            ) : (
              <div className="vietsub-media-unavailable">
                <TriangleAlert size={28} />
                <strong>Không thể mở video nguồn</strong>
                <p>Tệp đã bị di chuyển, xóa hoặc thay đổi sau khi liên kết.</p>
              </div>
            )}
          </div>
          {media.playbackUrl && (
            <div className="vietsub-playback-controls" aria-label="Điều khiển phát video">
              <button type="button" onClick={onTogglePlaying} aria-label={playing ? 'Tạm dừng' : 'Phát'}>
                {playing ? <Pause size={16} fill="currentColor" /> : <Play size={16} fill="currentColor" />}
              </button>
              <span className="vietsub-playback-time">{formatPlaybackTime(playheadMilliseconds)}</span>
              <input
                className="vietsub-playback-seek"
                type="range"
                min={0}
                max={Math.max(1, durationMilliseconds)}
                step={50}
                value={Math.min(playheadMilliseconds, Math.max(1, durationMilliseconds))}
                aria-label="Vị trí phát"
                onChange={(event) => onSeek(Number(event.target.value))}
              />
              <span className="vietsub-playback-time">{formatPlaybackTime(durationMilliseconds)}</span>
              <select
                value={playbackRate}
                aria-label="Tốc độ phát"
                onChange={(event) => onPlaybackRateChange(Number(event.target.value))}
              >
                {[0.5, 0.75, 1, 1.25, 1.5, 2].map((rate) => <option value={rate} key={rate}>{rate}×</option>)}
              </select>
              <button type="button" onClick={onToggleMuted} aria-label={muted ? 'Bật âm thanh' : 'Tắt âm thanh'}>
                {muted || volume === 0 ? <VolumeX size={16} /> : <Volume2 size={16} />}
              </button>
              <input
                className="vietsub-playback-volume"
                type="range"
                min={0}
                max={1}
                step={0.05}
                value={muted ? 0 : volume}
                aria-label="Âm lượng"
                onChange={(event) => onVolumeChange(Number(event.target.value))}
              />
              <button
                type="button"
                className={subtitlesVisible ? 'is-active' : ''}
                onClick={onToggleSubtitles}
                aria-label={subtitlesVisible ? 'Ẩn phụ đề dịch' : 'Hiện phụ đề dịch'}
                aria-pressed={subtitlesVisible}
              >
                {subtitlesVisible ? <Captions size={17} /> : <CaptionsOff size={17} />}
              </button>
            </div>
          )}
          <div className="vietsub-preview-meta">
            <span>Video {media.videoCodec?.toUpperCase() ?? 'không rõ'}</span>
            <span>{media.hasAudio ? `Audio ${media.audioCodec?.toUpperCase() ?? 'có sẵn'}` : 'Không có audio'}</span>
            <span>{formatBytes(media.sizeBytes)}</span>
            {media.framesPerSecond && <span>{media.framesPerSecond.toFixed(2)} fps</span>}
            {media.playbackUrl && <small>Space/K phát · J/L hoặc ←/→ tua 5 giây · M tắt tiếng</small>}
          </div>
          {(media.sourceChanged || !media.sourceAvailable) && (
            <div className="vietsub-media-warning">
              <TriangleAlert size={17} />
              <span>{sourceRecoveryMessage(media.sourceIssueCode, media.sourceChanged)}</span>
            </div>
          )}
        </>
      )}
    </section>
  );
}

function formatBytes(value: number): string {
  if (!Number.isFinite(value) || value <= 0) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  const index = Math.min(Math.floor(Math.log(value) / Math.log(1024)), units.length - 1);
  return `${(value / 1024 ** index).toFixed(index === 0 ? 0 : 1)} ${units[index]}`;
}

function formatDuration(value: number): string {
  const seconds = Math.max(0, Math.round(value));
  const hours = Math.floor(seconds / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);
  const remainingSeconds = seconds % 60;
  return hours > 0
    ? `${hours}:${String(minutes).padStart(2, '0')}:${String(remainingSeconds).padStart(2, '0')}`
    : `${minutes}:${String(remainingSeconds).padStart(2, '0')}`;
}

function formatPlaybackTime(milliseconds: number): string {
  const totalSeconds = Math.max(0, Math.floor(milliseconds / 1000));
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;
  return hours > 0
    ? `${hours}:${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`
    : `${minutes}:${String(seconds).padStart(2, '0')}`;
}

function durationToMilliseconds(seconds: number): number {
  return Number.isFinite(seconds) && seconds > 0 ? Math.round(seconds * 1000) : 0;
}

function sourceRecoveryMessage(issueCode: string | null | undefined, changed: boolean): string {
  if (changed || issueCode === 'vietsub_media_source_changed') {
    return 'Video liên kết đã thay đổi so với lúc nhập. Hãy nhập lại bằng chế độ sao chép hoặc chọn lại đúng tệp nguồn.';
  }
  if (issueCode === 'vietsub_media_source_missing') {
    return 'Không còn tìm thấy video liên kết. Hãy đưa tệp về vị trí cũ hoặc nhập lại video nguồn.';
  }
  return 'Video nguồn không còn sẵn sàng. Hệ thống đã chặn phát và xử lý; hãy nhập lại video để tiếp tục.';
}
