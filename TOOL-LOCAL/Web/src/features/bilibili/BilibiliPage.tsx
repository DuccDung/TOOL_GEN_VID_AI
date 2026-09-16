import { useEffect, useMemo, useState } from 'react';
import { AlertCircle, CheckCircle2, ChevronLeft, ChevronRight, Download, Film, FolderOpen, Link2, LoaderCircle, Search, Square, X } from 'lucide-react';
import type { BilibiliModule } from './useBilibiliModule';
import type { BilibiliJob } from './types';
import './bilibili.css';

const pageSize = 24;
const statusLabels: Record<BilibiliJob['status'], string> = {
  Queued: 'Chờ tải', Downloading: 'Đang tải', Processing: 'Đang ghép', Verifying: 'Đang kiểm tra',
  Completed: 'Hoàn tất', Failed: 'Lỗi tải', Cancelled: 'Đã hủy'
};
function duration(seconds?: number | null) {
  if (seconds == null) return '—';
  const total = Math.floor(seconds);
  return total >= 3600 ? `${Math.floor(total / 3600)}:${String(Math.floor(total / 60) % 60).padStart(2, '0')}:${String(total % 60).padStart(2, '0')}`
    : `${Math.floor(total / 60)}:${String(total % 60).padStart(2, '0')}`;
}
function size(bytes: number) { return `${(bytes / 1024 / 1024).toFixed(1)} MB`; }

export function BilibiliPage({ module }: { module: BilibiliModule }) {
  const { state, busy, loading, error } = module;
  const [url, setUrl] = useState('');
  const [query, setQuery] = useState('');
  const [selected, setSelected] = useState(new Set<string>());
  const [quality, setQuality] = useState('best');
  const [page, setPage] = useState(1);
  const [queuePage, setQueuePage] = useState(1);
  const filtered = useMemo(() => state.entries.filter(entry => `${entry.title} ${entry.uploader ?? ''}`.toLocaleLowerCase('vi').includes(query.toLocaleLowerCase('vi'))), [state.entries, query]);
  const pages = Math.max(1, Math.ceil(filtered.length / pageSize));
  const visible = filtered.slice((Math.min(page, pages) - 1) * pageSize, Math.min(page, pages) * pageSize);
  const completed = state.jobs.filter(job => job.status === 'Completed').length;
  const queuePages = Math.max(1, Math.ceil(state.jobs.length / 10));
  const queue = state.jobs.slice((Math.min(queuePage, queuePages) - 1) * 10, Math.min(queuePage, queuePages) * 10);
  const canSelect = !busy && state.entries.length > 0;
  const allSelected = filtered.length > 0 && filtered.every(entry => selected.has(entry.id));
  useEffect(() => { setSelected(new Set()); setPage(1); setQuery(''); }, [state.scan?.id]);
  const toggle = (id: string) => setSelected(previous => {
    const next = new Set(previous); if (next.has(id)) next.delete(id); else next.add(id); return next;
  });

  return <section className="bili-page" aria-label="Tải video Bilibili">
    <div className="bili-intro">
      <div className="bili-brand-icon"><Download size={25} /></div>
      <div><h1>Video từ Bilibili, ngay trên máy bạn</h1><p>Dán link video hoặc kênh, chọn những video bạn muốn giữ lại.</p></div>
      <span className="bili-local-badge">Lưu trên máy</span>
    </div>

    {!module.hosted && <div className="bili-notice" role="status">Mở trong ứng dụng taphoatool để quét và tải video.</div>}
    {error && <div className="bili-notice bili-error" role="alert"><AlertCircle size={18} /><span>{error}</span>
      <button type="button" onClick={module.dismissError} aria-label="Đóng thông báo"><X size={16} /></button></div>}

    <div className="bili-card bili-source">
      <form onSubmit={event => { event.preventDefault(); if (!busy && state.runtimeReady && url.trim()) module.scan(url.trim()); }}>
        <label htmlFor="bili-url">Link video hoặc kênh Bilibili</label>
        <div className="bili-link-row"><div className="bili-link-input"><Link2 size={19} /><input id="bili-url" type="url" required maxLength={2048}
          value={url} onChange={event => setUrl(event.target.value)} disabled={busy} placeholder="https://www.bilibili.com/video/… hoặc https://space.bilibili.com/…" /></div>
          <button className="bili-primary" type="submit" disabled={!module.hosted || loading || busy || !state.runtimeReady || !url.trim()}>
            {state.operation === 'Scanning' ? <LoaderCircle className="bili-spin" size={17} /> : <Search size={17} />} Quét video</button>
          {state.operation === 'Scanning' && <button type="button" className="bili-button" onClick={() => module.cancel()}><Square size={14} /> Dừng quét</button>}
        </div>
        <p className="bili-hint">Hỗ trợ link video, trang kênh và link rút gọn b23.tv. Quét kênh sẽ đọc lần lượt các trang danh sách.</p>
      </form>
      {!state.runtimeReady && <div className="bili-setup"><div><strong>Chuẩn bị cho lần tải đầu tiên</strong><p>Tải công cụ khoảng 18 MB một lần. Video được xử lý trên máy bằng FFmpeg.</p></div>
        <button className="bili-button" type="button" disabled={!module.hosted || busy || loading} onClick={module.install}>
          {state.operation === 'Installing' && <LoaderCircle className="bili-spin" size={16} />} {state.operation === 'Installing' ? 'Đang chuẩn bị…' : 'Chuẩn bị công cụ tải'}</button>
        {state.operation === 'Installing' && <button className="bili-button" type="button" onClick={() => module.cancel()}>Hủy</button>}
      </div>}
    </div>

    <div className="bili-layout">
      <div className="bili-card bili-results">
        <div className="bili-section-title"><div><h2>Danh sách video <span>{state.entries.length.toLocaleString('vi')}</span></h2>
          <p role="status">{state.scan?.message ?? 'Video bạn tìm được sẽ xuất hiện ở đây.'}</p></div>
          {state.operation === 'Scanning' && <LoaderCircle className="bili-spin" size={19} />}
          {state.scan?.complete && <CheckCircle2 className="bili-success-icon" size={20} />}
        </div>
        {state.scan && !state.scan.complete && state.scan.status !== 'Scanning' && <div className="bili-partial" role="status">
          Danh sách chưa đầy đủ. Bạn vẫn có thể tải các video đã tìm được hoặc quét lại.</div>}
        <div className="bili-filter-row"><label className="bili-filter"><Search size={16} /><input aria-label="Tìm trong danh sách video" placeholder="Tìm tiêu đề hoặc tên kênh…"
          value={query} onChange={event => { setQuery(event.target.value); setPage(1); }} /></label>
          <label className="bili-select-all"><input type="checkbox" checked={allSelected} disabled={!canSelect || !filtered.length}
            onChange={() => setSelected(previous => { const next = new Set(previous); filtered.forEach(entry => allSelected ? next.delete(entry.id) : next.add(entry.id)); return next; })} />
            Chọn tất cả {filtered.length > 0 ? `(${filtered.length})` : ''}</label></div>
        {visible.length ? <div className="bili-video-list">{visible.map(entry => <label key={entry.id} className={`bili-video-row ${selected.has(entry.id) ? 'selected' : ''}`}>
          <input type="checkbox" aria-label={`Chọn ${entry.title}`} checked={selected.has(entry.id)} disabled={!canSelect} onChange={() => toggle(entry.id)} />
          <div className="bili-thumbnail">{entry.thumbnailUrl ? <img src={entry.thumbnailUrl} loading="lazy" referrerPolicy="no-referrer" alt="" onError={event => { event.currentTarget.style.display = 'none'; }} /> : <Film size={25} />}
            <span>{duration(entry.durationSeconds)}</span></div>
          <div className="bili-video-copy"><strong title={entry.title}>{entry.title}</strong><span>{entry.uploader || 'Bilibili'}</span><small>{entry.url}</small></div>
        </label>)}</div> : <div className="bili-empty"><Film size={38} /><h3>{query ? 'Không tìm thấy video phù hợp' : 'Bắt đầu bằng một đường link'}</h3>
          <p>{query ? 'Thử tìm với từ khóa khác.' : 'Dán link ở phía trên để lấy thông tin video hoặc quét toàn bộ kênh.'}</p></div>}
        {pages > 1 && <div className="bili-pagination"><button type="button" className="bili-button" aria-label="Trang video trước" disabled={page <= 1} onClick={() => setPage(value => value - 1)}><ChevronLeft size={16} /></button>
          <span>Trang {Math.min(page, pages)} / {pages}</span><button type="button" className="bili-button" aria-label="Trang video tiếp" disabled={page >= pages} onClick={() => setPage(value => value + 1)}><ChevronRight size={16} /></button></div>}
        <div className="bili-download-bar"><span>Đã chọn <strong>{selected.size}</strong> video</span><button type="button" className="bili-primary"
          disabled={!module.hosted || busy || !state.runtimeReady || selected.size === 0} onClick={() => module.download([...selected], quality)}><Download size={17} /> Tải các video đã chọn</button></div>
      </div>

      <aside className="bili-side">
        <div className="bili-card bili-options"><h2>Tùy chọn tải</h2><label htmlFor="bili-quality">Chất lượng</label>
          <select id="bili-quality" value={quality} onChange={event => setQuality(event.target.value)} disabled={busy}>
            <option value="best">Tốt nhất hiện có</option><option value="1080">Tối đa 1080p</option><option value="720">Tối đa 720p</option><option value="480">Tối đa 480p</option></select>
          <p className="bili-hint">MP4 có hình và âm thanh. Chất lượng tùy video công khai cung cấp; tối đa 4 GiB/video.</p>
          <label>Thư mục lưu</label><div className="bili-folder"><FolderOpen size={18} /><span>{state.folderLabel}</span></div>
          <div className="bili-folder-actions"><button className="bili-button" type="button" disabled={!module.hosted || busy} onClick={module.selectFolder}>Đổi thư mục</button>
            <button className="bili-button" type="button" disabled={!module.hosted} onClick={() => module.openFolder()}>Mở thư mục</button></div>
          <p className="bili-hint">Mặc định: thư mục Videos / Bilibili của Windows.</p>
        </div>
        <div className="bili-card bili-queue"><div className="bili-section-title"><div><h2>Hàng đợi tải</h2><p>{completed} / {state.jobs.length} hoàn tất</p></div>
          {state.operation === 'Downloading' && <button className="bili-text-button" type="button" onClick={() => module.cancel()}>Hủy tất cả</button>}</div>
          {!state.jobs.length ? <div className="bili-queue-empty"><Download size={27} /><p>Chọn video để thêm vào hàng đợi.</p></div> : queue.map(job => {
            const active = ['Queued', 'Downloading', 'Processing', 'Verifying'].includes(job.status);
            return <div className={`bili-job ${job.status.toLowerCase()}`} key={job.id}>
              <strong title={job.video.title}>{job.video.title}</strong><div className="bili-job-status"><span>{statusLabels[job.status]}</span><span>{Math.round(job.percent)}%</span></div>
              <progress max={100} value={job.percent} aria-label={`Tiến độ tải ${job.video.title}`} />
              {job.bytesPerSecond != null && <small>{size(job.downloadedBytes)} · {size(job.bytesPerSecond)}/s</small>}
              {job.message && <p>{job.message}</p>}
              {job.fileName && <small className="bili-filename" title={job.fileName}>{job.fileName}</small>}
              <div className="bili-job-actions">{active ? <button type="button" className="bili-text-button" onClick={() => module.cancel(job.id)}>Hủy</button>
                : job.status === 'Completed' ? <button type="button" className="bili-text-button" onClick={() => module.openFolder(job.id)}>Mở thư mục</button>
                : <button type="button" className="bili-text-button" disabled={busy} onClick={() => module.retry(job.id)}>Thử lại</button>}</div>
            </div>;
          })}
          {queuePages > 1 && <div className="bili-pagination"><button className="bili-button" type="button" aria-label="Trang hàng đợi trước" disabled={queuePage <= 1} onClick={() => setQueuePage(value => value - 1)}><ChevronLeft size={16} /></button>
            <span>{queuePage} / {queuePages}</span><button className="bili-button" type="button" aria-label="Trang hàng đợi tiếp" disabled={queuePage >= queuePages} onClick={() => setQueuePage(value => value + 1)}><ChevronRight size={16} /></button></div>}
          <p className="bili-hint bili-session-note">Hàng đợi của phiên hiện tại. Giữ ứng dụng mở trong lúc tải.</p>
        </div>
      </aside>
    </div>
  </section>;
}
