import { useEffect, useRef } from 'react';
import type { RefObject } from 'react';
import {
  Captions,
  CaptionsOff,
  ChevronDown,
  Copy,
  FileVideo2,
  Gauge,
  Link2,
  Pause,
  Play,
  TriangleAlert,
  Volume2,
  VolumeX
} from 'lucide-react';
import type { VietsubMediaImportProgress, VietsubProjectSummary, VietsubSubtitleStyle } from './types';
import { VietsubSubtitleOverlay, useVideoContentRect } from './VietsubSubtitleOverlay';
import { defaultVietsubSubtitleStyle } from './vietsubSubtitleStyle';
import { VietsubNotice } from './VietsubNotice';

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
  playbackVolume?: number;
  muted: boolean;
  subtitlesVisible: boolean;
  activeSubtitleText?: string | null;
  subtitleStyle?: VietsubSubtitleStyle;
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
  playbackVolume = volume,
  muted,
  subtitlesVisible,
  activeSubtitleText,
  subtitleStyle = defaultVietsubSubtitleStyle,
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
  useEffect(() => {
    const video = videoRef.current;
    if (!video) return;
    video.volume = playbackVolume;
    video.muted = muted;
  }, [media?.playbackUrl, muted, playbackVolume, videoRef]);
  const stageRef = useRef<HTMLDivElement | null>(null);
  const contentRect = useVideoContentRect(
    stageRef,
    videoRef,
    media?.width ?? 16,
    media?.height ?? 9
  );

  return (
    <section className="card vietsub-editor-panel vietsub-preview-panel">
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
          <div ref={stageRef} className="vietsub-editor-video-stage">
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
                    video.volume = playbackVolume;
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
                  <VietsubSubtitleOverlay
                    text={activeSubtitleText}
                    style={subtitleStyle}
                    contentRect={contentRect}
                  />
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
              <label className="vietsub-playback-rate" title="Tốc độ phát">
                <Gauge size={14} aria-hidden="true" />
                <select
                  value={playbackRate}
                  aria-label="Tốc độ phát"
                  onChange={(event) => onPlaybackRateChange(Number(event.target.value))}
                >
                  {[0.5, 0.75, 1, 1.25, 1.5, 2].map((rate) => <option value={rate} key={rate}>{rate}×</option>)}
                </select>
                <ChevronDown size={12} aria-hidden="true" />
              </label>
              <button type="button" onClick={onToggleMuted} aria-label={muted ? 'Bật âm thanh gốc khi nghe thử' : 'Tắt âm thanh gốc khi nghe thử'}>
                {muted || volume === 0 ? <VolumeX size={16} /> : <Volume2 size={16} />}
              </button>
              <input
                className="vietsub-playback-volume"
                type="range"
                min={0}
                max={1}
                step={0.05}
                value={muted ? 0 : volume}
                aria-label="Âm lượng nghe thử của âm thanh gốc"
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
          {(media.sourceChanged || !media.sourceAvailable) && (
            <VietsubNotice className="vietsub-media-warning" icon={<TriangleAlert size={17} />}
              eventId={`${project.projectId}:${media.mediaId}:${media.sourceIssueCode}:${media.sourceChanged}:${media.sourceAvailable}`}>
              <span>{sourceRecoveryMessage(media.sourceIssueCode, media.sourceChanged)}</span>
            </VietsubNotice>
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
