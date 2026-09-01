import { useRef, useState } from 'react';
import {
  AudioLines,
  Captions,
  Check,
  CheckCircle2,
  Copy,
  FileVideo2,
  FolderOpen,
  HardDrive,
  Languages,
  Link2,
  Mic2,
  Pencil,
  Plus,
  RefreshCw,
  TriangleAlert,
  X
} from 'lucide-react';
import type { LucideIcon } from 'lucide-react';
import type { VietsubModuleState, VietsubSubtitleCue, VietsubSubtitlePageQuery } from './types';
import { VietsubSubtitleEditor } from './VietsubSubtitleEditor';

type VietsubPageProps = {
  state: VietsubModuleState;
  onRefresh: () => void;
  onCreateProject: (name: string) => void;
  onOpenProject: (projectId: string) => void;
  onRenameProject: (projectId: string, name: string) => void;
  onCloseProject: () => void;
  onImportMedia: (mode: 'COPY' | 'LINK') => void;
  onImportSrt: (languageCode: string) => void;
  onActivateSubtitleTrack: (trackId: string) => void;
  onLoadSubtitlePage: (query: VietsubSubtitlePageQuery) => void;
  onUpdateSubtitleCue: (cue: Pick<VietsubSubtitleCue, 'cueId' | 'originalText' | 'translatedText' | 'speaker'>) => void;
  onSplitSubtitleCue: (cueId: string, positionMilliseconds: number) => void;
  onAlignSubtitleCue: (cueId: string, positionMilliseconds: number) => void;
  onDuplicateSubtitleCue: (cueId: string) => void;
  onDeleteSubtitleCue: (cueId: string) => void;
  onExportSrt: (mode: 'ORIGINAL' | 'TRANSLATED') => void;
};

const workflowSteps: Array<{
  title: string;
  description: string;
  icon: LucideIcon;
}> = [
  { title: 'Thêm video', description: 'Chọn video nguồn trong workspace Vietsub riêng.', icon: FileVideo2 },
  { title: 'Nhận dạng phụ đề', description: 'Nhận dạng lời nói hoặc chữ có sẵn trên khung hình.', icon: Captions },
  { title: 'Dịch sang tiếng Việt', description: 'Biên dịch, kiểm tra và chỉnh từng câu trước khi xuất.', icon: Languages },
  { title: 'Tạo giọng đọc', description: 'Ghép giọng tiếng Việt theo mốc thời gian của phụ đề.', icon: Mic2 },
  { title: 'Xuất thành phẩm', description: 'Xuất video, SRT hoặc bản nội dung đã hoàn thiện.', icon: AudioLines }
];

export function VietsubPage({
  state,
  onRefresh,
  onCreateProject,
  onOpenProject,
  onRenameProject,
  onCloseProject,
  onImportMedia,
  onImportSrt,
  onActivateSubtitleTrack,
  onLoadSubtitlePage,
  onUpdateSubtitleCue,
  onSplitSubtitleCue,
  onAlignSubtitleCue,
  onDuplicateSubtitleCue,
  onDeleteSubtitleCue,
  onExportSrt
}: VietsubPageProps) {
  const [projectName, setProjectName] = useState('');
  const [renamingProjectId, setRenamingProjectId] = useState<string | null>(null);
  const [renameValue, setRenameValue] = useState('');
  const [playheadMilliseconds, setPlayheadMilliseconds] = useState(0);
  const videoRef = useRef<HTMLVideoElement | null>(null);

  const createProject = () => {
    const name = projectName.trim();
    if (!name || state.loading || state.busy) return;
    onCreateProject(name);
    setProjectName('');
  };

  const saveRename = (projectId: string) => {
    const name = renameValue.trim();
    if (!name || state.loading || state.busy) return;
    onRenameProject(projectId, name);
    setRenamingProjectId(null);
    setRenameValue('');
  };

  return (
    <div className="page-shell vietsub-page">
      <section className="vietsub-hero">
        <div className="vietsub-hero-icon"><Languages size={30} /></div>
        <div>
          <span className="vietsub-eyebrow">KHÔNG GIAN LÀM VIỆC RIÊNG</span>
          <h2>Dịch video thành nội dung tiếng Việt dễ xem, dễ chỉnh</h2>
          <p>Mỗi dự án phụ đề được lưu và xử lý độc lập, không trộn với dự án tạo video hiện tại.</p>
        </div>
        <span className={`vietsub-ready-badge ${state.loading ? 'loading' : ''}`}>
          {state.loading ? <RefreshCw className="spin" size={16} /> : <CheckCircle2 size={16} />}
          {state.loading ? 'Đang cập nhật' : 'Workspace đã sẵn sàng'}
        </span>
      </section>

      {state.errorMessage && (
        <section className="card vietsub-inline-error" role="alert">
          <TriangleAlert size={19} />
          <div><strong>Chưa hoàn tất thao tác</strong><p>{state.errorMessage}</p></div>
          <button type="button" onClick={onRefresh}><RefreshCw size={15} /> Thử lại</button>
        </section>
      )}

      {state.selectedProject?.needsRecovery && (
        <section className="card vietsub-recovery-banner">
          <TriangleAlert size={19} />
          <div>
            <strong>Dự án cần kiểm tra sau lần đóng trước</strong>
            <p>Manifest đã được phục hồi. Hãy kiểm tra nội dung gần nhất trước khi tiếp tục xử lý.</p>
          </div>
        </section>
      )}

      <section className="vietsub-project-layout">
        <article className="card vietsub-create-card">
          <span className="vietsub-eyebrow">DỰ ÁN MỚI</span>
          <h3>Tạo workspace phụ đề riêng</h3>
          <p>Video, subtitle, voice, cache và output sẽ nằm trong thư mục Vietsub độc lập.</p>
          <label>
            <span>Tên dự án</span>
            <input
              value={projectName}
              maxLength={120}
              disabled={state.loading || state.busy}
              onChange={(event) => setProjectName(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === 'Enter') createProject();
              }}
              placeholder="Ví dụ: Dịch video giới thiệu sản phẩm"
            />
          </label>
          <button
            type="button"
            className="start-button"
            disabled={!projectName.trim() || state.loading || state.busy}
            onClick={createProject}
          >
            <Plus size={17} /> Tạo dự án Vietsub
          </button>
        </article>

        <article className="card vietsub-project-list-card">
          <div className="vietsub-project-list-heading">
            <div><span className="vietsub-eyebrow">DỰ ÁN CỦA BẠN</span><h3>Tiếp tục công việc gần đây</h3></div>
            <button type="button" onClick={onRefresh} disabled={state.loading || state.busy} title="Làm mới">
              <RefreshCw className={state.loading ? 'spin' : ''} size={16} />
            </button>
          </div>

          {state.projects.length === 0 ? (
            <div className="vietsub-project-empty">
              <FolderOpen size={28} />
              <strong>Chưa có dự án Vietsub</strong>
              <p>Tạo dự án đầu tiên để bắt đầu dịch phụ đề.</p>
            </div>
          ) : (
            <div className="vietsub-project-list">
              {state.projects.map((project) => {
                const selected = state.selectedProject?.projectId === project.projectId;
                const renaming = renamingProjectId === project.projectId;
                return (
                  <div className={`vietsub-project-row ${selected ? 'selected' : ''}`} key={project.projectId}>
                    <button
                      type="button"
                      className="vietsub-project-open"
                      disabled={state.loading || state.busy || renaming}
                      onClick={() => onOpenProject(project.projectId)}
                    >
                      <span className="vietsub-project-folder"><FolderOpen size={18} /></span>
                      <span>
                        {renaming ? (
                          <input
                            autoFocus
                            value={renameValue}
                            maxLength={120}
                            onClick={(event) => event.stopPropagation()}
                            onChange={(event) => setRenameValue(event.target.value)}
                            onKeyDown={(event) => {
                              if (event.key === 'Enter') saveRename(project.projectId);
                              if (event.key === 'Escape') setRenamingProjectId(null);
                            }}
                          />
                        ) : <strong>{project.name}</strong>}
                        <small>
                          {formatProjectDate(project.updatedAtUtc)} · {project.sourceLanguageCode.toUpperCase()} → VI · {project.serverSynchronized ? 'Đã đồng bộ' : 'Chờ đồng bộ'}
                        </small>
                      </span>
                    </button>
                    <div className="vietsub-project-row-actions">
                      {renaming ? (
                        <>
                          <button type="button" disabled={!renameValue.trim()} onClick={() => saveRename(project.projectId)} title="Lưu tên"><Check size={15} /></button>
                          <button type="button" onClick={() => setRenamingProjectId(null)} title="Hủy"><X size={15} /></button>
                        </>
                      ) : (
                        <button
                          type="button"
                          disabled={state.loading || state.busy}
                          onClick={() => {
                            setRenamingProjectId(project.projectId);
                            setRenameValue(project.name);
                          }}
                          title="Đổi tên"
                        ><Pencil size={14} /></button>
                      )}
                    </div>
                  </div>
                );
              })}
            </div>
          )}
        </article>
      </section>

      {state.selectedProject && (
        <section className="card vietsub-selected-project">
          <div className="vietsub-selected-icon"><FolderOpen size={22} /></div>
          <div>
            <span className="vietsub-eyebrow">ĐANG MỞ</span>
            <strong>{state.selectedProject.name}</strong>
            <p>
              Workspace đang được khóa cho phiên này và sẽ tự lưu an toàn · {state.selectedProject.serverSynchronized ? 'Metadata đã đồng bộ' : 'Metadata sẽ tự đồng bộ lại khi server sẵn sàng'}.
            </p>
          </div>
          <button type="button" onClick={onCloseProject} disabled={state.loading || state.busy}>Đóng dự án</button>
        </section>
      )}

      {state.selectedProject && !state.selectedProject.sourceVideo && (
        <section className="card vietsub-media-import">
          <div className="vietsub-media-import-heading">
            <div className="vietsub-media-import-icon"><FileVideo2 size={24} /></div>
            <div>
              <span className="vietsub-eyebrow">VIDEO NGUỒN</span>
              <h3>Thêm video để bắt đầu làm phụ đề</h3>
              <p>Chọn cách lưu phù hợp. Video gốc luôn chỉ được đọc và không bao giờ bị ghi đè.</p>
            </div>
          </div>
          <div className="vietsub-import-options">
            <button type="button" disabled={state.loading || state.busy} onClick={() => onImportMedia('COPY')}>
              <span><Copy size={20} /></span>
              <strong>Sao chép vào dự án</strong>
              <small>Ổn định nhất khi tệp gốc bị di chuyển hoặc xóa.</small>
            </button>
            <button type="button" disabled={state.loading || state.busy} onClick={() => onImportMedia('LINK')}>
              <span><Link2 size={20} /></span>
              <strong>Liên kết tệp hiện có</strong>
              <small>Không tốn thêm dung lượng; cần giữ nguyên tệp gốc.</small>
            </button>
          </div>
          {state.mediaImportProgress && (
            <div className="vietsub-import-progress">
              <div><strong>Đang kiểm tra và nhập video</strong><span>{state.mediaImportProgress.percent.toFixed(0)}%</span></div>
              <div className="vietsub-progress-track"><span style={{ width: `${state.mediaImportProgress.percent}%` }} /></div>
              <small>{formatBytes(state.mediaImportProgress.bytesProcessed)} / {formatBytes(state.mediaImportProgress.totalBytes)} · {state.mediaImportProgress.megabytesPerSecond.toFixed(1)} MB/s</small>
            </div>
          )}
        </section>
      )}

      {state.selectedProject?.sourceVideo && (
        <section className="card vietsub-media-workspace">
          <div className="vietsub-media-preview">
            {state.selectedProject.sourceVideo.playbackUrl ? (
              <video
                ref={videoRef}
                controls
                preload="metadata"
                src={state.selectedProject.sourceVideo.playbackUrl}
                onTimeUpdate={(event) => setPlayheadMilliseconds(Math.round(event.currentTarget.currentTime * 1000))}
                onSeeked={(event) => setPlayheadMilliseconds(Math.round(event.currentTarget.currentTime * 1000))}
              />
            ) : (
              <div className="vietsub-media-unavailable"><TriangleAlert size={28} /><strong>Không thể mở video nguồn</strong><p>Tệp đã bị di chuyển, xóa hoặc thay đổi sau khi liên kết.</p></div>
            )}
            {state.selectedProject.sourceVideo.thumbnailUrls.length > 0 && (
              <div className="vietsub-thumbnail-strip" aria-label="Ảnh timeline">
                {state.selectedProject.sourceVideo.thumbnailUrls.map((url, index) => (
                  <img src={url} alt={`Mốc video ${index + 1}`} loading="lazy" key={url} />
                ))}
              </div>
            )}
          </div>
          <div className="vietsub-media-details">
            <span className="vietsub-eyebrow">VIDEO ĐÃ NHẬP</span>
            <h3>{state.selectedProject.sourceVideo.fileName}</h3>
            <div className="vietsub-media-badges">
              <span><HardDrive size={14} /> {state.selectedProject.sourceVideo.importMode === 'COPY' ? 'Đã sao chép' : 'Đang liên kết'}</span>
              <span>{formatDuration(state.selectedProject.sourceVideo.durationSeconds)}</span>
              <span>{state.selectedProject.sourceVideo.width} × {state.selectedProject.sourceVideo.height}</span>
            </div>
            <dl>
              <div><dt>Dung lượng</dt><dd>{formatBytes(state.selectedProject.sourceVideo.sizeBytes)}</dd></div>
              <div><dt>Video</dt><dd>{state.selectedProject.sourceVideo.videoCodec?.toUpperCase() ?? 'Không rõ'}</dd></div>
              <div><dt>Âm thanh</dt><dd>{state.selectedProject.sourceVideo.hasAudio ? state.selectedProject.sourceVideo.audioCodec?.toUpperCase() ?? 'Có' : 'Không có'}</dd></div>
              <div><dt>Khung hình</dt><dd>{state.selectedProject.sourceVideo.framesPerSecond ? `${state.selectedProject.sourceVideo.framesPerSecond.toFixed(2)} fps` : 'Không rõ'}</dd></div>
            </dl>
            {(state.selectedProject.sourceVideo.sourceChanged || !state.selectedProject.sourceVideo.sourceAvailable) && (
              <div className="vietsub-media-warning"><TriangleAlert size={17} /><span>Video nguồn không còn khớp lúc nhập. Hệ thống đã chặn phát và xử lý để bảo vệ dữ liệu dự án.</span></div>
            )}
          </div>
        </section>
      )}

      {state.selectedProject && (
        <VietsubSubtitleEditor
          workspace={state.subtitleWorkspace}
          page={state.subtitlePage}
          busy={state.loading || state.busy}
          notice={state.subtitleNotice}
          sourceLanguageCode={state.selectedProject.sourceLanguageCode}
          playheadMilliseconds={playheadMilliseconds}
          onImportSrt={onImportSrt}
          onActivateTrack={onActivateSubtitleTrack}
          onLoadPage={onLoadSubtitlePage}
          onUpdateCue={onUpdateSubtitleCue}
          onSplitCue={onSplitSubtitleCue}
          onAlignCue={onAlignSubtitleCue}
          onDuplicateCue={onDuplicateSubtitleCue}
          onDeleteCue={onDeleteSubtitleCue}
          onExportSrt={onExportSrt}
          onSeek={(positionMilliseconds) => {
            setPlayheadMilliseconds(positionMilliseconds);
            if (videoRef.current) {
              videoRef.current.currentTime = positionMilliseconds / 1000;
            }
          }}
        />
      )}

      <section className="vietsub-workflow" aria-label="Quy trình dịch phụ đề">
        {workflowSteps.map(({ title, description, icon: Icon }, index) => (
          <article className="card vietsub-step" key={title}>
            <div className="vietsub-step-number">{String(index + 1).padStart(2, '0')}</div>
            <div className="vietsub-step-icon"><Icon size={21} /></div>
            <strong>{title}</strong>
            <p>{description}</p>
          </article>
        ))}
      </section>

      <section className="card vietsub-boundary-note">
        <CheckCircle2 size={20} />
        <div>
          <strong>Tách biệt dữ liệu ngay từ đầu</strong>
          <p>Vietsub dùng project, workspace, job và trạng thái riêng; chỉ dùng chung tài khoản, tổ chức và giao diện VideoMaker.</p>
        </div>
      </section>
    </div>
  );
}

function formatProjectDate(value: string): string {
  if (!value) return 'Vừa cập nhật';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return 'Vừa cập nhật';
  return new Intl.DateTimeFormat('vi-VN', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit'
  }).format(date);
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
