import {
  Captions,
  CheckCircle2,
  Copy,
  FileVideo2,
  Languages,
  Link2,
  ScanText,
  Volume2
} from 'lucide-react';
import type { VietsubMediaImportProgress, VietsubProjectSummary, VietsubSubtitleWorkspace } from './types';

type VietsubSettingsPanelProps = {
  project: VietsubProjectSummary;
  subtitleWorkspace?: VietsubSubtitleWorkspace | null;
  progress?: VietsubMediaImportProgress | null;
  busy: boolean;
  onImportMedia: (mode: 'COPY' | 'LINK') => void;
};

export function VietsubSettingsPanel({
  project,
  subtitleWorkspace,
  progress,
  busy,
  onImportMedia
}: VietsubSettingsPanelProps) {
  const sourceReady = Boolean(project.sourceVideo?.sourceAvailable && !project.sourceVideo.sourceChanged);
  const activeTrack = subtitleWorkspace?.tracks.find((track) => track.trackId === subtitleWorkspace.activeTrackId);

  return (
    <aside className="card vietsub-editor-panel vietsub-settings-panel">
      <div className="vietsub-panel-heading">
        <span className="vietsub-eyebrow">QUY TRÌNH</span>
        <h3>Thiết lập dự án</h3>
      </div>

      <section className="vietsub-settings-section">
        <div className="vietsub-settings-section-title"><FileVideo2 size={16} /><strong>Video nguồn</strong></div>
        {project.sourceVideo ? (
          <div className={`vietsub-settings-state ${sourceReady ? 'ready' : 'warning'}`}>
            {sourceReady ? <CheckCircle2 size={15} /> : <FileVideo2 size={15} />}
            <div><strong>{project.sourceVideo.fileName}</strong><small>{sourceReady ? 'Sẵn sàng phát và xử lý' : 'Tệp nguồn cần được kiểm tra lại'}</small></div>
          </div>
        ) : (
          <>
            <p>Thêm video trước khi nhận dạng, quét OCR hoặc dựng thành phẩm.</p>
            <div className="vietsub-settings-actions">
              <button type="button" disabled={busy} onClick={() => onImportMedia('COPY')}><Copy size={14} /> Sao chép</button>
              <button type="button" disabled={busy} onClick={() => onImportMedia('LINK')}><Link2 size={14} /> Liên kết</button>
            </div>
          </>
        )}
        {progress && (
          <div className="vietsub-import-progress compact">
            <div><strong>Đang nhập video</strong><span>{progress.percent.toFixed(0)}%</span></div>
            <div className="vietsub-progress-track"><span style={{ width: `${progress.percent}%` }} /></div>
            <small>{formatBytes(progress.bytesProcessed)} / {formatBytes(progress.totalBytes)}</small>
          </div>
        )}
      </section>

      <section className="vietsub-settings-section">
        <div className="vietsub-settings-section-title"><Captions size={16} /><strong>Phụ đề nguồn</strong></div>
        <div className={`vietsub-settings-state ${activeTrack ? 'ready' : ''}`}>
          <Captions size={15} />
          <div>
            <strong>{activeTrack ? activeTrack.displayName : 'Chưa có track phụ đề'}</strong>
            <small>{activeTrack ? `${activeTrack.cueCount} cue · revision ${activeTrack.revision}` : 'Nhập SRT tại bảng Phụ đề để bắt đầu'}</small>
          </div>
        </div>
      </section>

      <section className="vietsub-settings-section is-upcoming" aria-label="Các công cụ sẽ được bật theo từng giai đoạn">
        <div><ScanText size={16} /><span><strong>Nhận dạng & OCR</strong><small>Job engine và model local chưa được cài trong phase này.</small></span></div>
        <div><Languages size={16} /><span><strong>Dịch tự động</strong><small>Hiện có thể nhập và chỉnh bản dịch thủ công theo cue.</small></span></div>
        <div><Volume2 size={16} /><span><strong>Giọng đọc & xuất video</strong><small>Sẽ được bật khi artifact và export pipeline sẵn sàng.</small></span></div>
      </section>
    </aside>
  );
}

function formatBytes(value: number): string {
  if (!Number.isFinite(value) || value <= 0) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  const index = Math.min(Math.floor(Math.log(value) / Math.log(1024)), units.length - 1);
  return `${(value / 1024 ** index).toFixed(index === 0 ? 0 : 1)} ${units[index]}`;
}
