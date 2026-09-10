import { useState } from 'react';
import type { ProjectDashboard } from '../../types';
import { ShortDialog } from './ShortVideoLibrary';
import { useShortVideoHost } from './useShortVideoHost';
import './outfitShortVideo.css';

export function ShortVideoVeoMigration({ project, organizationId, disabled, beforeMigrate }: {
  project: ProjectDashboard; organizationId: string; disabled: boolean; beforeMigrate?: () => Promise<unknown>;
}) {
  const request = useShortVideoHost(organizationId, project.project.projectId);
  const [open, setOpen] = useState(false);
  const [working, setWorking] = useState(false);
  const [error, setError] = useState('');
  const [duration, setDuration] = useState([4, 6, 8].includes(project.project.targetDurationSeconds) ? project.project.targetDurationSeconds : 8);
  const [ratio, setRatio] = useState(project.project.aspectRatio === '16:9' ? '16:9' : '9:16');
  return <div className="sv-warning" role="status">
    <p>Dự án đang dùng thiết lập video cũ. Chuyển sang Veo để tiếp tục tạo clip từ nội dung và ảnh đã lưu.</p>
    <button disabled={disabled || working} onClick={() => setOpen(true)}>Chuyển dự án sang Veo</button>
    {error && <p role="alert">{error}</p>}
    {open && <ShortDialog title="Chuyển video ngắn sang Veo" busy={working} onClose={() => setOpen(false)}>
      <div className="sv-dialog-body">
        <p>Nội dung và ảnh nguồn được giữ lại. Ảnh mặc thử đã duyệt được dùng tiếp khi giữ nguyên tỷ lệ; video cũ vẫn nằm trong lịch sử. Video mới cần báo giá và xác nhận riêng.</p>
        <label>Thời lượng <select aria-label="Thời lượng Veo" disabled={working} value={duration} onChange={e => setDuration(Number(e.target.value))}>{[4, 6, 8].map(n => <option key={n} value={n}>{n} giây</option>)}</select></label>
        <label>Tỷ lệ <select aria-label="Tỷ lệ Veo" disabled={working} value={ratio} onChange={e => setRatio(e.target.value)}><option value="9:16">Dọc · 9:16</option><option value="16:9">Ngang · 16:9</option></select></label>
        {ratio !== project.project.aspectRatio && <p className="sv-warning">Tỷ lệ thay đổi: cần tạo và duyệt ảnh đầu vào mới trước khi tạo video.</p>}
        <p>Thao tác chuyển thiết lập chưa gọi AI và chưa phát sinh phí.</p>
      </div>
      <footer><button disabled={working} onClick={() => setOpen(false)}>Hủy</button><button className="sv-primary" disabled={disabled || working} onClick={async () => {
        setWorking(true); setError('');
        try { await beforeMigrate?.(); await request('outfit.migrate', { confirmed: true, expectedProviderCode: project.videoProviderCode, durationSeconds: duration, aspectRatio: ratio }); setOpen(false); }
        catch (e) { setError((e as Error).message); setOpen(false); }
        finally { setWorking(false); }
      }}>{working ? 'Đang chuyển…' : 'Xác nhận chuyển sang Veo'}</button></footer>
    </ShortDialog>}
  </div>;
}
