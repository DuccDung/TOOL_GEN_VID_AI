import { useEffect, useRef, useState, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { postToHost } from '../../bridge';
import { CalendarDays, Check, ChevronLeft, ChevronRight, CircleAlert, Clock3, ExternalLink, ImagePlus, Pause, Pencil, Play, Plus, RefreshCw, Video, X } from 'lucide-react';
import type { PublishingModule } from './usePublishingModule';
import { publishingStatus, type PublishingInput, type PublishingPreview, type PublishingSchedule, type PublishingTarget } from './types';
import './publishing.css';

const days = ['T2', 'T3', 'T4', 'T5', 'T6', 'T7', 'CN'];
const privacyNames: Record<string, string> = { PUBLIC_TO_EVERYONE: 'Mọi người', SELF_ONLY: 'Chỉ mình tôi', MUTUAL_FOLLOW_FRIENDS: 'Bạn bè', FOLLOWER_OF_CREATOR: 'Người theo dõi' };
const dateKey = (date: Date) => `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
function freshInput(): PublishingInput {
  const tomorrow = new Date(); tomorrow.setDate(tomorrow.getDate() + 1);
  return { title: '', description: '', characterImageId: '', productImageId: '', startDate: dateKey(tomorrow), endDate: dateKey(tomorrow),
    publishTime: '19:00', timeZoneId: Intl.DateTimeFormat().resolvedOptions().timeZone || 'Asia/Ho_Chi_Minh', weekdays: 127,
    leadMinutes: 120, latePolicy: 'SameDay', durationSeconds: 8, aspectRatio: '9:16', maximumCostPerRun: 0, targets: [] };
}
function formatTime(value: string | null, zone?: string) {
  if (!value) return '—';
  return new Intl.DateTimeFormat('vi-VN', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit', timeZone: zone }).format(new Date(value.endsWith('Z') ? value : value + 'Z'));
}
function Badge({ status, schedule = false }: { status: string; schedule?: boolean }) {
  const tone = ['Completed', 'Active'].includes(status) ? 'good' : ['Failed', 'NeedsAttention', 'PartialFailure', 'Unknown'].includes(status) ? 'error' : 'normal';
  return <span className={`pub-badge pub-badge-${tone}`}>{schedule && status === 'Completed' ? 'Đã xếp đủ lượt' : publishingStatus[status] ?? status}</span>;
}
function Modal({ title, children, close, busy = false }: { title: string; children: ReactNode; close: () => void; busy?: boolean }) {
  const ref = useRef<HTMLDivElement>(null);
  const closeRef = useRef(close); closeRef.current = close;
  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null;
    const element = ref.current!; element.focus();
    const handler = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !busy) { event.preventDefault(); closeRef.current(); }
      if (event.key !== 'Tab') return;
      const nodes = [...element.querySelectorAll<HTMLElement>('button:not(:disabled),input:not(:disabled),select:not(:disabled),textarea:not(:disabled),a[href],video[controls]')];
      if (!nodes.length) { event.preventDefault(); return; }
      if (event.shiftKey && (document.activeElement === nodes[0] || document.activeElement === element)) { event.preventDefault(); nodes.at(-1)?.focus(); }
      else if (!event.shiftKey && document.activeElement === nodes.at(-1)) { event.preventDefault(); nodes[0].focus(); }
    };
    element.addEventListener('keydown', handler);
    return () => { element.removeEventListener('keydown', handler); previous?.focus(); };
  }, [busy]);
  return createPortal(<div className="pub-overlay"><div ref={ref} className="pub-modal" role="dialog" aria-modal="true" aria-label={title} tabIndex={-1}>
    <div className="pub-modal-head"><h2>{title}</h2><button type="button" className="pub-icon" disabled={busy} aria-label="Đóng" onClick={close}><X size={20}/></button></div>{children}
  </div></div>, document.body);
}

export function PublishingPage({ module: m, onTikTok }: { module: PublishingModule; onTikTok: () => void }) {
  const [tab, setTab] = useState<'schedules' | 'runs'>('schedules');
  const [editing, setEditing] = useState<PublishingSchedule | 'new' | null>(null);
  const [activation, setActivation] = useState<PublishingSchedule | null>(null);
  const [consent, setConsent] = useState(false);
  const [month, setMonth] = useState(() => new Date(new Date().getFullYear(), new Date().getMonth(), 1));
  const [day, setDay] = useState<string | null>(null);
  const [showCalendar, setShowCalendar] = useState(false);
  const { state } = m;
  const activeCount = state.schedules.filter(x => x.status === 'Active').length;
  const blocked = state.runs.filter(x => ['NeedsAttention', 'Failed', 'PartialFailure'].includes(x.status)).length;
  useEffect(() => { if (m.saved) { setEditing(null); m.resetSaved(); } }, [m.saved]);
  const runs = day ? state.runs.filter(x => dateKey(new Date(x.publishAtUtc.endsWith('Z') ? x.publishAtUtc : x.publishAtUtc + 'Z')) === day) : state.runs;

  return <div className="publishing-page">
    <div className="pub-toolbar"><div><h2>Nội dung đúng lịch, kênh luôn hoạt động</h2><p>Chuẩn bị video từ nhân vật và sản phẩm, theo dõi từng lượt xuất bản.</p></div>
      <div className="pub-actions"><button className="pub-button" onClick={m.refresh} disabled={m.loading || m.busy}><RefreshCw size={16}/>Làm mới</button>
        <button className="pub-button pub-primary" onClick={() => { m.resetSaved(); setEditing('new'); }} disabled={!state.enabled || m.busy}><Plus size={17}/>Tạo lịch</button></div></div>
    {m.error && <div className="pub-notice pub-notice-error" role="alert"><CircleAlert size={18}/><span>{m.error}</span><button className="pub-icon" onClick={m.clearError} aria-label="Đóng thông báo"><X size={16}/></button></div>}
    {state.unavailableReason && <div className="pub-notice" role="status"><CircleAlert size={18}/><span>{state.unavailableReason}</span></div>}
    {!m.hosted && <div className="pub-notice">Mở app VideoMaker để lưu lịch và kết nối tài khoản đăng.</div>}
    <div className="pub-stats">
      {[['Lịch đang bật', activeCount, CalendarDays], ['Chờ duyệt TikTok', state.runs.filter(x => x.status === 'AwaitingReview').length, Clock3],
        ['Lượt hoàn tất', state.runs.filter(x => x.status === 'Completed').length, Check], ['Cần xử lý', blocked, CircleAlert]].map(([label, count, Icon]) => {
          const StatIcon = Icon as typeof CalendarDays;
          return <div className="pub-stat" key={String(label)}><span><StatIcon size={19}/>{String(label)}</span><strong>{String(count)}</strong></div>;
        })}
    </div>
    <section className="pub-card pub-connections" aria-label="Tài khoản xuất bản">
      <div><h3>Tài khoản xuất bản</h3><p>Facebook dùng Page. YouTube dùng kênh đã cấp quyền. TikTok quản lý ở mục riêng.</p></div>
      <div className="pub-platforms">{(['TikTok', 'Facebook', 'YouTube'] as const).map(platform => {
        const ready = state.platforms.find(x => x.platform === platform);
        const accounts = state.connections.filter(x => x.platform === platform);
        return <div className="pub-platform" key={platform}><div><strong>{platform}</strong><small>{accounts.length ? `${accounts.length} tài khoản đã kết nối` : 'Chưa có tài khoản'}</small></div>
          <button className="pub-button" disabled={m.busy || (platform !== 'TikTok' && (!state.enabled || !ready?.configured))} title={ready?.message ?? ''}
            onClick={() => platform === 'TikTok' ? onTikTok() : m.send('publishing.oauth.connect', { platform })}>{platform === 'TikTok' ? 'Quản lý' : 'Kết nối'}</button>
          {ready?.message && platform !== 'TikTok' && <small className="pub-platform-note">{ready.message}</small>}</div>;
      })}</div>
      {state.connections.filter(x => x.platform !== 'TikTok').length > 0 && <div className="pub-account-tags">{state.connections.filter(x => x.platform !== 'TikTok').map(x =>
        <span key={x.connectionId}>{x.platform} · {x.displayName}<button disabled={m.busy} onClick={() => m.send('publishing.connection.disconnect', { connectionId: x.connectionId })} aria-label={`Ngắt ${x.displayName}`}><X size={13}/></button></span>)}</div>}
    </section>
    <section className="pub-card pub-list">
      <div className="pub-list-head"><div className="pub-tabs" role="tablist" aria-label="Danh sách lịch">
        <button role="tab" aria-selected={tab === 'schedules'} onClick={() => setTab('schedules')}>Lịch đã tạo <span>{state.schedules.length}</span></button>
        <button role="tab" aria-selected={tab === 'runs'} onClick={() => setTab('runs')}>Lượt chạy <span>{state.runs.length}</span></button></div>
        <button className="pub-button" onClick={() => { setShowCalendar(!showCalendar); setTab('runs'); setDay(null); }}><CalendarDays size={16}/>{showCalendar ? 'Ẩn lịch tháng' : 'Lịch tháng'}</button></div>
      {showCalendar && <div className="pub-calendar"><div className="pub-calendar-head"><button className="pub-icon" aria-label="Tháng trước" onClick={() => setMonth(new Date(month.getFullYear(), month.getMonth() - 1, 1))}><ChevronLeft size={18}/></button>
        <strong>Tháng {month.getMonth() + 1}, {month.getFullYear()}</strong><button className="pub-icon" aria-label="Tháng sau" onClick={() => setMonth(new Date(month.getFullYear(), month.getMonth() + 1, 1))}><ChevronRight size={18}/></button>
        {day && <button className="pub-button" onClick={() => setDay(null)}>Tất cả ngày</button>}</div><div className="pub-calendar-grid">{days.map(x => <span className="pub-weekday" key={x}>{x}</span>)}
        {Array.from({ length: 42 }, (_, index) => {
          const date = new Date(month.getFullYear(), month.getMonth(), index - (month.getDay() + 6) % 7 + 1); const key = dateKey(date);
          const count = state.runs.filter(x => dateKey(new Date(x.publishAtUtc.endsWith('Z') ? x.publishAtUtc : x.publishAtUtc + 'Z')) === key).length;
          return <button key={key} className={`${date.getMonth() !== month.getMonth() ? 'pub-outside' : ''} ${day === key ? 'pub-selected' : ''}`}
            onClick={() => { setDay(key); setTab('runs'); }} aria-label={`${key}, ${count} lượt`}><span>{date.getDate()}</span>{count > 0 && <small>{count} lượt</small>}</button>;
        })}</div><p className="pub-muted">Lịch tháng hiển thị các lượt đã được server khởi tạo, theo múi giờ trên máy.</p></div>}
      {tab === 'schedules' ? state.schedules.length === 0 ? <Empty title="Chưa có lịch xuất bản" text="Tạo lịch đầu tiên với chủ đề, ảnh nhân vật và ảnh sản phẩm của bạn."/> :
        <div className="pub-rows">{state.schedules.map(schedule => <article className="pub-row" key={schedule.scheduleId}><div className="pub-row-icon"><CalendarDays size={21}/></div>
          <div className="pub-row-main"><div className="pub-row-title"><h3>{schedule.input.title}</h3><Badge status={schedule.status} schedule/></div>
            <p>{schedule.input.targets.map(x => x.platform).filter((x, i, a) => a.indexOf(x) === i).join(' · ')} · {schedule.input.durationSeconds} giây · {schedule.input.aspectRatio}</p>
            <small>Lượt tiếp: {formatTime(schedule.nextPublishAtUtc, schedule.input.timeZoneId)} · {schedule.input.timeZoneId} · Chuẩn bị trước {schedule.input.leadMinutes} phút</small></div>
          <div className="pub-actions"><button className="pub-icon" aria-label={`Sửa ${schedule.input.title}`} disabled={m.busy || schedule.status === 'Active'} title="Tạm dừng lịch trước khi sửa" onClick={() => setEditing(schedule)}><Pencil size={17}/></button>
            {schedule.status === 'Active' ? <button className="pub-button" disabled={m.busy} onClick={() => m.send('publishing.schedule.change', { scheduleId: schedule.scheduleId, expectedRevision: schedule.revision, action: 'Pause' })}><Pause size={15}/>Tạm dừng</button> :
              <button className="pub-button" disabled={m.busy || schedule.status === 'Cancelled'} onClick={() => { setActivation(schedule); setConsent(false); }}><Play size={15}/>Kích hoạt</button>}</div></article>)}</div> :
        runs.length === 0 ? <Empty title={day ? 'Ngày này chưa có lượt chạy' : 'Chưa có lượt chạy'} text="Đến giờ chuẩn bị, server tạo lượt chạy và cập nhật tiến độ tại đây."/> :
          <div className="pub-rows">{runs.map(run => <article className="pub-row pub-run" key={run.runId}><div className="pub-row-icon"><Video size={21}/></div><div className="pub-row-main">
            <div className="pub-row-title"><h3>{run.title}</h3><Badge status={run.status}/></div><p>Tạo: {formatTime(run.generateAtUtc, run.input.timeZoneId)} · Đăng: {formatTime(run.publishAtUtc, run.input.timeZoneId)}</p>
            {run.message && <p className="pub-run-message">{run.message}</p>}
            <div className="pub-deliveries">{run.deliveries.map(d => <span key={d.deliveryId} title={d.message ?? ''}>{d.platform} · {publishingStatus[d.status] ?? d.status}{d.postUrl &&
              <button className="pub-icon" aria-label={`Mở bài ${d.platform}`} onClick={() => m.send('publishing.post.open', { deliveryId: d.deliveryId })}><ExternalLink size={13}/></button>}</span>)}</div>
          </div><div className="pub-actions">{run.mediaSha256 && <button className="pub-button" disabled={m.busy} onClick={() => m.send('publishing.run.preview', { runId: run.runId })}><Play size={15}/>{run.status === 'AwaitingReview' ? 'Xem và duyệt' : 'Xem video'}</button>}
            {run.canResume && <button className="pub-button" disabled={m.busy} onClick={() => m.send('publishing.run.action', { runId: run.runId, action: 'Resume' })}>Tiếp tục</button>}
            {!['Completed', 'Cancelled', 'Failed', 'Skipped'].includes(run.status) && <button className="pub-icon" aria-label={`Hủy lượt ${run.title}`} disabled={m.busy} onClick={() => m.send('publishing.run.action', { runId: run.runId, action: 'Cancel' })}><X size={16}/></button>}</div></article>)}</div>}
    </section>
    <p className="pub-footer"><Clock3 size={15}/>Giữ app đăng nhập để duy trì quyền chạy lịch. Thời gian xử lý video và xuất bản còn phụ thuộc nền tảng.</p>
    {editing && <ScheduleEditor key={editing === 'new' ? 'new' : editing.scheduleId} module={m} schedule={editing === 'new' ? null : editing} close={() => setEditing(null)}/>}
    {activation && <Modal title="Kích hoạt lịch tự động" close={() => setActivation(null)} busy={m.busy}><div className="pub-modal-body"><h3>{activation.input.title}</h3>
      <p>Hệ thống sẽ tạo ảnh đầu cảnh và video {activation.input.durationSeconds} giây cho từng ngày đã chọn, bắt đầu trước giờ đăng {activation.input.leadMinutes} phút.</p>
      <div className="pub-notice">Giới hạn tổng báo giá: <strong>{activation.input.maximumCostPerRun.toFixed(2)} USD/lượt</strong>. Chi phí được quyết toán theo usage thực tế; ngân sách tổ chức vẫn áp dụng.</div>
      <label className="pub-consent"><input type="checkbox" checked={consent} onChange={e => setConsent(e.target.checked)}/><span>Tôi cho phép tự tạo và duyệt ảnh đầu cảnh, tạo video theo nội dung và ảnh đã chọn trong giới hạn báo giá trên. Facebook/YouTube tự đăng theo lịch; mỗi video TikTok sẽ chờ tôi xem trước và xác nhận đăng.</span></label>
      <p className="pub-muted">Việc duyệt ảnh tự động được ghi nhận là quyền thực hiện đã giao cho lịch. Bạn có thể tạm dừng lịch để ngăn bước tiếp theo.</p>
      <div className="pub-modal-actions"><button className="pub-button" disabled={m.busy} onClick={() => setActivation(null)}>Để sau</button><button className="pub-button pub-primary" disabled={!consent || m.busy}
        onClick={() => { m.send('publishing.schedule.change', { scheduleId: activation.scheduleId, expectedRevision: activation.revision, action: 'Activate', confirmAutomaticGeneration: true }); setActivation(null); }}>Kích hoạt lịch</button></div>
    </div></Modal>}
    {m.preview && <ReviewDialog key={m.preview.run.runId} value={m.preview} module={m}/>}
  </div>;
}

function Empty({ title, text }: { title: string; text: string }) { return <div className="pub-empty"><CalendarDays size={34}/><h3>{title}</h3><p>{text}</p></div>; }

function ScheduleEditor({ module: m, schedule, close }: { module: PublishingModule; schedule: PublishingSchedule | null; close: () => void }) {
  const [input, setInput] = useState<PublishingInput>(() => schedule ? structuredClone(schedule.input) : freshInput());
  const id = useRef(schedule?.scheduleId ?? crypto.randomUUID());
  const [previews, setPreviews] = useState<Record<string, string>>({});
  const [validation, setValidation] = useState<string | null>(null);
  const previousImage = useRef(m.image);
  const set = <K extends keyof PublishingInput>(key: K, value: PublishingInput[K]) => setInput(previous => ({ ...previous, [key]: value }));
  useEffect(() => {
    if (!m.image || m.image === previousImage.current) return;
    previousImage.current = m.image;
    const { image, previewUrl } = m.image;
    set(image.role === 'Character' ? 'characterImageId' : 'productImageId', image.imageId);
    setPreviews(previous => ({ ...previous, [image.role]: previewUrl }));
  }, [m.image]);
  const toggle = (connectionId: string) => {
    const connection = m.state.connections.find(x => x.connectionId === connectionId)!;
    if (connection.platform === 'Facebook' && !input.targets.some(x => x.connectionId === connectionId)) set('aspectRatio', '9:16');
    set('targets', input.targets.some(x => x.connectionId === connectionId) ? input.targets.filter(x => x.connectionId !== connectionId) :
      [...input.targets, { platform: connection.platform, connectionId, privacy: connection.platform === 'TikTok' ? 'review' : connection.platform === 'YouTube' ? 'private' : 'public', madeForKids: false, brandOrganic: false, brandContent: false }]);
  };
  const updateTarget = (id: string, change: Partial<PublishingTarget>) => set('targets', input.targets.map(x => x.connectionId === id ? { ...x, ...change } : x));
  return <Modal title={schedule ? 'Chỉnh sửa lịch' : 'Tạo lịch xuất bản'} close={close} busy={m.busy}><form className="pub-modal-body" onSubmit={e => {
    e.preventDefault(); setValidation(null);
    if (!input.characterImageId || !input.productImageId || !input.targets.length || !input.weekdays || input.maximumCostPerRun <= 0 || input.endDate < input.startDate) {
      setValidation('Chọn đủ hai ảnh, ít nhất một tài khoản, ngày thực hiện và giới hạn báo giá lớn hơn 0.'); return;
    }
    m.send('publishing.schedule.save', { scheduleId: id.current, expectedRevision: schedule?.revision ?? 0, input });
  }}><div className="pub-form-grid"><section><h3>1. Nội dung video</h3><label className="pub-field">Tiêu đề<input required maxLength={100} value={input.title} onChange={e => set('title', e.target.value)} placeholder="Ví dụ: Giới thiệu bộ chăm sóc da buổi sáng"/></label>
    <label className="pub-field">Mô tả nội dung và hành động<textarea required maxLength={1500} rows={5} value={input.description} onChange={e => set('description', e.target.value)} placeholder="Nhân vật cầm sản phẩm, giới thiệu trong không gian sáng tự nhiên. Mô tả rõ điều bạn muốn xuất hiện trong video."/></label>
    <div className="pub-image-grid">{(['Character', 'Product'] as const).map(role => <div className="pub-image" key={role}>
      {previews[role] ? <img src={previews[role]} alt={role === 'Character' ? 'Ảnh nhân vật' : 'Ảnh sản phẩm'}/> : <ImagePlus size={30}/>}
      <strong>{role === 'Character' ? 'Ảnh nhân vật' : 'Ảnh sản phẩm'}</strong><small>{(role === 'Character' ? input.characterImageId : input.productImageId) ? 'Đã lưu ảnh trên server' : 'PNG/JPEG · tối đa 10 MiB'}</small>
      <button type="button" className="pub-button" disabled={m.busy} onClick={() => m.send('publishing.image.select', { role })}>Chọn ảnh</button></div>)}</div>
    <div className="pub-fields-inline"><label className="pub-field">Thời lượng<select value={input.durationSeconds} onChange={e => set('durationSeconds', Number(e.target.value))}>{[4, 6, 8].map(x => <option key={x} value={x}>{x} giây</option>)}</select></label>
      <label className="pub-field">Tỷ lệ<select value={input.aspectRatio} onChange={e => set('aspectRatio', e.target.value)}><option value="9:16">9:16 · Video dọc</option><option value="16:9" disabled={input.targets.some(x => x.platform === 'Facebook')}>16:9 · Video ngang</option></select>{input.targets.some(x => x.platform === 'Facebook') && <small>Facebook Reels dùng video dọc 9:16.</small>}</label></div>
    <p className="pub-muted">Video ngắn Veo với âm thanh tự nhiên. Ảnh tham chiếu được lưu trên server để dùng cho lịch.</p>
    <h3>2. Tài khoản nhận video</h3>
    {!m.state.connections.length && <p className="pub-muted">Kết nối tài khoản ở màn hình lịch trước khi lưu.</p>}
    {m.state.connections.map(connection => <div className="pub-target" key={connection.connectionId}><label className="pub-check"><input type="checkbox" checked={input.targets.some(x => x.connectionId === connection.connectionId)} disabled={connection.status !== 'Connected'} onChange={() => toggle(connection.connectionId)}/>
      <span>{connection.platform} · {connection.displayName}</span></label>
      {input.targets.filter(x => x.connectionId === connection.connectionId).map(target => <div key={target.connectionId}>{target.platform === 'YouTube' ? <>
        <label className="pub-field">Quyền xem<select value={target.privacy} onChange={e => updateTarget(target.connectionId, { privacy: e.target.value })}><option value="private">Riêng tư</option><option value="unlisted">Không công khai</option><option value="public">Công khai</option></select></label>
        <label className="pub-check"><input type="checkbox" checked={target.madeForKids} onChange={e => updateTarget(target.connectionId, { madeForKids: e.target.checked })}/>Nội dung dành cho trẻ em</label></> :
        <small>{target.platform === 'TikTok' ? 'Quyền xem và nhãn thương mại được chọn khi duyệt từng video.' : 'Đăng công khai lên Page đã chọn.'}</small>}</div>)}</div>)}
  </section><section><h3>3. Thời gian thực hiện</h3><div className="pub-fields-inline"><label className="pub-field">Ngày bắt đầu<input type="date" required value={input.startDate} onChange={e => { set('startDate', e.target.value); if (input.endDate < e.target.value) set('endDate', e.target.value); }}/></label>
    <label className="pub-field">Ngày kết thúc<input type="date" required min={input.startDate} value={input.endDate} onChange={e => set('endDate', e.target.value)}/></label></div>
    <span className="pub-label">Thứ trong tuần</span><div className="pub-days">{days.map((name, i) => <button type="button" key={name} aria-pressed={Boolean(input.weekdays & 1 << i)} className={input.weekdays & 1 << i ? 'selected' : ''} onClick={() => set('weekdays', input.weekdays ^ 1 << i)}>{name}</button>)}</div>
    <p className="pub-muted">Để chạy một lần, chọn ngày bắt đầu và kết thúc giống nhau. Mỗi lịch tối đa 90 ngày.</p>
    <div className="pub-fields-inline"><label className="pub-field">Giờ đăng<input type="time" required value={input.publishTime} onChange={e => set('publishTime', e.target.value)}/></label>
      <label className="pub-field">Múi giờ<select value={input.timeZoneId} onChange={e => set('timeZoneId', e.target.value)}>{[...new Set([input.timeZoneId, 'Asia/Ho_Chi_Minh', 'Asia/Bangkok', 'Asia/Shanghai', 'UTC', 'America/New_York'])].map(x => <option key={x}>{x}</option>)}</select></label></div>
    <label className="pub-field">Bắt đầu tạo trước giờ đăng<select value={input.leadMinutes} onChange={e => set('leadMinutes', Number(e.target.value))}>{[30, 60, 120, 240, 720, 1440].map(x => <option key={x} value={x}>{x < 60 ? `${x} phút` : `${x / 60} giờ`}</option>)}</select></label>
    <label className="pub-field">Nếu tạo video xong muộn<select value={input.latePolicy} onChange={e => set('latePolicy', e.target.value as 'SameDay' | 'Skip')}><option value="SameDay">Đăng ngay khi xong, trong cùng ngày</option><option value="Skip">Bỏ lượt nếu quá giờ đăng 5 phút</option></select></label>
    <h3>4. Giới hạn tạo tự động</h3><label className="pub-field">Tổng báo giá tối đa mỗi lượt (USD)<input type="number" required min="0.01" max="100" step="0.01" value={input.maximumCostPerRun || ''} onChange={e => set('maximumCostPerRun', Number(e.target.value))} placeholder="Nhập giới hạn bạn cho phép"/></label>
    <div className="pub-notice">Hệ thống kiểm tra giá ảnh và video trước từng bước. Thiếu ngân sách hoặc vượt mức đã chọn thì dừng để bạn xử lý.</div>
    <p className="pub-muted">Lưu bản nháp chưa tạo video. Bạn xác nhận quyền tự động khi kích hoạt lịch. Lịch lặp sử dụng lại cùng mô tả và ảnh tham chiếu.</p>
  </section></div>{(validation || m.error) && <p className="pub-form-error" role="alert">{validation ?? m.error}</p>}<div className="pub-modal-actions"><button type="button" className="pub-button" disabled={m.busy} onClick={close}>Hủy</button><button className="pub-button pub-primary" disabled={m.busy}>{m.busy ? 'Đang xử lý…' : 'Lưu bản nháp'}</button></div></form></Modal>;
}

function ReviewDialog({ value, module: m }: { value: PublishingPreview; module: PublishingModule }) {
  const [targets, setTargets] = useState(() => value.run.input.targets.map(x => x.platform === 'TikTok' ? { ...x, privacy: '', title: value.run.title } : x));
  const [watched, setWatched] = useState(false);
  const [consent, setConsent] = useState(false);
  const awaiting = value.run.status === 'AwaitingReview';
  const ready = watched && consent && targets.filter(x => x.platform === 'TikTok').every(x => x.privacy && x.title?.trim() &&
    !value.creators.find(c => c.connectionId === x.connectionId)?.publishingIssue && !(x.brandContent && x.privacy === 'SELF_ONLY'));
  const update = (id: string, patch: Partial<PublishingTarget>) => setTargets(values => values.map(x => x.connectionId === id ? { ...x, ...patch } : x));
  return <Modal title={awaiting ? 'Xem video và duyệt đăng TikTok' : 'Video đã tạo'} close={m.closePreview} busy={m.busy}><div className="pub-modal-body"><div className="pub-review-grid">
    <video src={value.previewUrl} controls playsInline onEnded={() => setWatched(true)} preload="metadata" aria-label="Video theo lịch"/>
    <div><h3>{value.run.title}</h3><p>Giờ đăng: {formatTime(value.run.publishAtUtc, value.run.input.timeZoneId)}</p>
      {awaiting && targets.filter(x => x.platform === 'TikTok').map(target => <div className="pub-target" key={target.connectionId}><strong>{value.creators.find(x => x.connectionId === target.connectionId)?.creatorNickname ?? 'TikTok'}</strong>
        <label className="pub-field">Caption TikTok<textarea value={target.title ?? ''} maxLength={1000} onChange={e => update(target.connectionId, { title: e.target.value })}/></label>
        <label className="pub-field">Ai có thể xem video?<select value={target.privacy} onChange={e => update(target.connectionId, { privacy: e.target.value })}><option value="">Chọn quyền xem</option>
          {value.creators.find(x => x.connectionId === target.connectionId)?.privacyLevelOptions.map(x => <option value={x} key={x}>{privacyNames[x] ?? x}</option>)}</select></label>
        <label className="pub-check"><input type="checkbox" checked={target.brandOrganic} onChange={e => update(target.connectionId, { brandOrganic: e.target.checked })}/>Quảng bá thương hiệu của tôi</label>
        <label className="pub-check"><input type="checkbox" checked={target.brandContent} onChange={e => update(target.connectionId, { brandContent: e.target.checked })}/>Nội dung thương hiệu có tài trợ</label>
        <small>Video được đánh dấu do AI tạo. Bình luận, Duet và Stitch tắt.</small>
        {value.creators.find(x => x.connectionId === target.connectionId)?.publishingIssue && <p role="alert">{value.creators.find(x => x.connectionId === target.connectionId)?.publishingIssue?.message}</p>}
      </div>)}
      {awaiting && <><label className="pub-check"><input type="checkbox" checked={watched} onChange={e => setWatched(e.target.checked)}/>Tôi đã xem và duyệt video này.</label>
        <label className="pub-check"><input type="checkbox" checked={consent} onChange={e => setConsent(e.target.checked)}/>Tôi đồng ý gửi video lên TikTok theo cài đặt trên, xác nhận quyền sử dụng nội dung và âm thanh, đồng ý với Chính sách sử dụng âm nhạc và điều khoản nội dung thương hiệu của TikTok.</label>
        <div className="pub-actions"><button className="pub-button" type="button" onClick={() => postToHost('tiktok.policy.open', { policy: 'music' })}>Chính sách âm nhạc</button>
          <button className="pub-button" type="button" onClick={() => postToHost('tiktok.policy.open', { policy: 'branded' })}>Nội dung thương hiệu</button></div>
        <p className="pub-muted">Nếu đã qua giờ đăng, video được gửi trong thời hạn của lượt này. Bài gửi lên có thể cần thêm thời gian để xuất hiện.</p></>}
    </div></div>{m.error && <p className="pub-form-error" role="alert">{m.error}</p>}<div className="pub-modal-actions"><button className="pub-button" onClick={m.closePreview} disabled={m.busy}>Đóng</button>
      {awaiting && <button className="pub-button pub-primary" disabled={!ready || m.busy} onClick={() => m.send('publishing.run.review', { runId: value.run.runId, mediaSha256: value.run.mediaSha256, approve: true, targets })}>Xác nhận đăng theo lịch</button>}</div></div></Modal>;
}
