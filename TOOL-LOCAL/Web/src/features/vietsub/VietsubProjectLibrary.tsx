import { useState } from 'react';
import {
  AudioLines,
  Captions,
  Check,
  CheckCircle2,
  FileVideo2,
  FolderOpen,
  Languages,
  Mic2,
  Pencil,
  Plus,
  RefreshCw,
  X
} from 'lucide-react';
import type { LucideIcon } from 'lucide-react';
import type { VietsubModuleState } from './types';

type VietsubProjectLibraryProps = {
  state: VietsubModuleState;
  onRefresh: () => void;
  onCreateProject: (name: string) => void;
  onOpenProject: (projectId: string) => void;
  onRenameProject: (projectId: string, name: string) => void;
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

export function VietsubProjectLibrary({
  state,
  onRefresh,
  onCreateProject,
  onOpenProject,
  onRenameProject
}: VietsubProjectLibraryProps) {
  const [projectName, setProjectName] = useState('');
  const [renamingProjectId, setRenamingProjectId] = useState<string | null>(null);
  const [renameValue, setRenameValue] = useState('');

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
    <>
      <section className="vietsub-hero">
        <div className="vietsub-hero-icon"><Languages size={30} /></div>
        <div>
          <span className="vietsub-eyebrow">THƯ VIỆN DỰ ÁN VIETSUB</span>
          <h2>Dịch video thành nội dung tiếng Việt dễ xem, dễ chỉnh</h2>
          <p>Tạo hoặc mở một dự án để chuyển vào không gian biên tập riêng có preview, phụ đề và timeline.</p>
        </div>
        <span className={`vietsub-ready-badge ${state.loading ? 'loading' : ''}`}>
          {state.loading ? <RefreshCw className="spin" size={16} /> : <CheckCircle2 size={16} />}
          {state.loading ? 'Đang cập nhật' : 'Thư viện đã sẵn sàng'}
        </span>
      </section>

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
                const renaming = renamingProjectId === project.projectId;
                return (
                  <div className="vietsub-project-row" key={project.projectId}>
                    {renaming ? (
                      <div className="vietsub-project-rename">
                        <span className="vietsub-project-folder"><FolderOpen size={18} /></span>
                        <input
                          autoFocus
                          value={renameValue}
                          maxLength={120}
                          aria-label={`Tên mới cho ${project.name}`}
                          onChange={(event) => setRenameValue(event.target.value)}
                          onKeyDown={(event) => {
                            if (event.key === 'Enter') saveRename(project.projectId);
                            if (event.key === 'Escape') setRenamingProjectId(null);
                          }}
                        />
                      </div>
                    ) : (
                      <button
                        type="button"
                        className="vietsub-project-open"
                        disabled={state.loading || state.busy}
                        onClick={() => onOpenProject(project.projectId)}
                      >
                        <span className="vietsub-project-folder"><FolderOpen size={18} /></span>
                        <span>
                          <strong>{project.name}</strong>
                          <small>
                            {formatProjectDate(project.updatedAtUtc)} · {project.sourceLanguageCode.toUpperCase()} → VI · {project.serverSynchronized ? 'Đã đồng bộ' : 'Chờ đồng bộ'}
                          </small>
                        </span>
                      </button>
                    )}
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
    </>
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
