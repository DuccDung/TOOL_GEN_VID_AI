import { useEffect, useRef, useState } from 'react';
import { postToHost, subscribeToHost } from '../../bridge';
import type { LocalVoiceAction, LocalVoiceAnchor, LocalVoiceJob, LocalVoiceState, ProjectDashboard } from '../../types';
import { canConvertScene, canUseAsVoiceSource, localVoiceStatusLabel, supportsLocalVoice } from './localVoicePolicy';
import './localVoice.css';

export function LocalVoicePanel({ project, busy }: { project: ProjectDashboard | null; busy: boolean }) {
  if (!project || !supportsLocalVoice(project)) return null;
  return <LocalVoiceWorkspace key={`${project.project.organizationId}/${project.project.projectId}`} project={project} busy={busy} />;
}

function LocalVoiceWorkspace({ project, busy }: { project: ProjectDashboard; busy: boolean }) {
  const projectId = project.project.projectId;
  const [state, setState] = useState<LocalVoiceState | null>(null);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState('');
  const [confirmed, setConfirmed] = useState(false);
  const [sourceId, setSourceId] = useState('');
  const [sourceConfirmed, setSourceConfirmed] = useState(false);
  const [selected, setSelected] = useState<string[]>([]);
  const [reason, setReason] = useState('');
  const [nativeConfirmed, setNativeConfirmed] = useState(false);
  const [cleanupConfirmed, setCleanupConfirmed] = useState(false);
  const requests = useRef(new Set<string>());
  const mutation = useRef<string | null>(null);
  const sequence = useRef(0);
  const lastSequence = useRef(0);
  const requestOrder = useRef(new Map<string, number>());
  const fetching = useRef<string | null>(null);
  const send = (type: string, payload: Partial<LocalVoiceAction> = {}, mutating = true) => {
    if (type === 'local-voice.get' && fetching.current) return;
    const id = postToHost(type, { ...payload, projectId });
    if (type === 'local-voice.get') fetching.current = id;
    requests.current.add(id); requestOrder.current.set(id, ++sequence.current);
    if (mutating) { mutation.current = id; setPending(true); setError(''); }
  };
  useEffect(() => {
    const stop = subscribeToHost(message => {
      if (message.type === 'local-voice.changed' && (message.payload as { projectId?: string })?.projectId === projectId) {
        send('local-voice.get', {}, false); return;
      }
      if (!message.requestId || !requests.current.delete(message.requestId)) return;
      if (message.requestId === fetching.current) fetching.current = null;
      const order = requestOrder.current.get(message.requestId) ?? 0;
      requestOrder.current.delete(message.requestId);
      if (message.requestId === mutation.current) { mutation.current = null; setPending(false); }
      if (message.type === 'operation.error') { setError(message.error?.message ?? 'Thao tác không hoàn tất.'); return; }
      if (message.type === 'local-voice.state') {
        const result = message.payload as LocalVoiceState;
        if (result.projectId === projectId && order >= lastSequence.current) {
          lastSequence.current = order; setState(result);
        }
      }
    });
    send('local-voice.get', {}, false);
    const timer = window.setInterval(() => send('local-voice.get', {}, false), 5000);
    return () => { stop(); window.clearInterval(timer); requests.current.clear(); requestOrder.current.clear(); fetching.current = null; };
  }, [projectId]);
  const locked = busy || pending || state?.running === true;
  const sources = project.scenes.filter(canUseAsVoiceSource);
  const selection = selected.filter(id => sources.some(scene => scene.sceneId === id));
  const convertIds = selection.filter(id => state && sources.some(scene => scene.sceneId === id && canConvertScene(scene, state)));
  const sceneName = (id: string) => `Cảnh ${project.scenes.find(scene => scene.sceneId === id)?.sequenceNumber ?? 'cũ'}`;
  const characterName = (id: string) => project.characters.find(character => character.characterId === id)?.name ?? 'Nhân vật';
  const review = (id: string, anchor: boolean, approve: boolean) => send(approve ? 'local-voice.approve' : 'local-voice.reject', {
    [anchor ? 'anchorId' : 'jobId']: id, confirmed: true,
  });
  return <section className="local-voice-panel" aria-label="Đồng nhất giọng Veo local">
    <h2>Đồng nhất giọng Veo · local</h2>
    <p>Giữ lời nói và hình của Veo; chỉ chuyển màu giọng theo mẫu đã duyệt. Không tạo lại lời bằng TTS, không gọi lip-sync Cloud.</p>
    <p role="status">{state?.runtime.message ?? 'Đang kiểm tra trạng thái…'}{pending || state?.running ? ' Đang xử lý trên máy; lần đầu có thể mất nhiều phút.' : ''}</p>
    {error && <p role="alert" className="local-voice-error">{error}</p>}
    {state && !state.enabled && <div className="local-voice-box">
      <label><input type="checkbox" checked={confirmed} onChange={event => setConfirmed(event.target.checked)} />
        Bật cho project này: cảnh thoại cần duyệt kết quả local hoặc xác nhận ngoại lệ native trước khi dựng.</label>
      <button disabled={locked || !confirmed || state.runtime.status === 'DISABLED'} onClick={() => send('local-voice.enable', { confirmed })}>Bật cho project</button>
      {state.runtime.status === 'DISABLED' && <p>Tính năng thử nghiệm đang tắt. Bật Features.VeoLocalVoiceConsistencyEnabled trong cấu hình desktop rồi khởi động lại; cần nghiệm thu giọng thật trước rollout.</p>}
    </div>}
    {state?.enabled && <>
      <div className="local-voice-actions">
        <label><input type="checkbox" checked={confirmed} onChange={event => setConfirmed(event.target.checked)} /> Cho phép tải component/model local từ nguồn đã ghim (vài GB, không phát sinh phí AI).</label>
        <button disabled={locked || !confirmed || state.runtime.status === 'DISABLED'} onClick={() => send('local-voice.install', { confirmed })}>
          {state.runtime.status === 'READY' ? 'Kiểm tra / cài lại runtime' : 'Cài runtime local'}</button>
        <button disabled={!pending && !state.running} onClick={() => send('local-voice.cancel', {}, false)}>Hủy tác vụ local</button>
      </div>
      <h3>1. Mẫu giọng cho nhân vật</h3>
      <p>Chọn clip Veo đã nghe duyệt, chỉ có một người nói; mẫu sạch cần ít nhất 1,5 giây lời nói. Đổi mẫu sẽ làm kết quả cũ hết hiệu lực.</p>
      <div className="local-voice-actions">
        <select aria-label="Cảnh làm mẫu giọng" value={sourceId} onChange={event => { setSourceId(event.target.value); setSourceConfirmed(false); }}>
          <option value="">Chọn cảnh native đã duyệt</option>
          {sources.map(scene => <option key={scene.sceneId} value={scene.sceneId}>{sceneName(scene.sceneId)} · {scene.characters[0].name}</option>)}
        </select>
        <label><input type="checkbox" checked={sourceConfirmed} onChange={event => setSourceConfirmed(event.target.checked)} /> Tôi có quyền dùng giọng này và đã xác nhận clip chỉ có một người nói.</label>
        <button disabled={locked || !sourceId || !sourceConfirmed || state.runtime.status !== 'READY'} onClick={() => send('local-voice.prepare-anchor', { sceneId: sourceId, confirmed: sourceConfirmed })}>Chuẩn bị mẫu</button>
      </div>
      <div className="local-voice-grid">{state.anchors.map(anchor => <ReviewCard key={`${anchor.id}/${anchor.status}`} item={anchor} anchor
        title={`${characterName(anchor.characterId)} · ${sceneName(anchor.sceneId)}`} locked={locked} review={review} />)}</div>
      <h3>2. Chọn cảnh để đổi giọng</h3>
      <p>Chạy lại chỉ xử lý file local, không tạo thêm clip Veo. Cảnh chưa đủ mẫu giọng đã duyệt không được đưa vào nhóm chạy.</p>
      {!sources.length && <p>Chưa có cảnh thoại một nhân vật với native audio đã duyệt.</p>}
      <div className="local-voice-scenes">{sources.map(scene => <label key={scene.sceneId}>
        <input type="checkbox" checked={selection.includes(scene.sceneId)} disabled={locked} onChange={event => { setNativeConfirmed(false); setSelected(previous => event.target.checked ? [...previous, scene.sceneId] : previous.filter(id => id !== scene.sceneId)); }} />
        {sceneName(scene.sceneId)} · {scene.characters[0].name}{!canConvertScene(scene, state) ? ' — cần mẫu giọng đã duyệt' : ''}
      </label>)}</div>
      <button disabled={locked || !convertIds.length || convertIds.length !== selection.length} onClick={() => send('local-voice.convert', { sceneIds: convertIds })}>Đổi giọng {convertIds.length} cảnh đã chọn</button>
      <details className="local-voice-box"><summary>Ngoại lệ: giữ nguyên giọng native</summary>
        <p>Chọn đúng một cảnh. Ngoại lệ được lưu với người duyệt, thời điểm và lý do; không phải fallback tự động.</p>
        <textarea aria-label="Lý do giữ native" maxLength={500} value={reason} onChange={event => { setReason(event.target.value); setNativeConfirmed(false); }} />
        <label><input type="checkbox" checked={nativeConfirmed} onChange={event => setNativeConfirmed(event.target.checked)} /> Tôi chấp nhận giọng native của cảnh đang chọn.</label>
        <button disabled={locked || selection.length !== 1 || !reason.trim() || !nativeConfirmed} onClick={() => send('local-voice.use-native', { sceneId: selection[0], confirmed: nativeConfirmed, reason })}>Xác nhận ngoại lệ native</button>
      </details>
      <h3>3. Nghe so sánh và duyệt kết quả</h3>
      <p>Kiểm tra câu chữ, màu giọng, khẩu hình và âm nền. Kiểm tra thời lượng tự động không thay thế việc nghe duyệt. Khi dựng, chỉ những cảnh có duyệt còn hiệu lực mới được chọn.</p>
      <p>{new Set(state.jobs.filter(job => job.status === 'Approved').map(job => job.sceneId)).size} cảnh thoại có kết quả duyệt còn hiệu lực. Các cảnh thoại còn lại không được đưa vào bản dựng.</p>
      <div className="local-voice-grid">{state.jobs.map(job => <ReviewCard key={`${job.id}/${job.status}`} item={job} anchor={false}
        title={`${sceneName(job.sceneId)} · ${characterName(job.characterId)}${job.nativeException ? ' · ngoại lệ native' : ''}`} locked={locked} review={review}
        retry={() => send('local-voice.convert', { sceneIds: [job.sceneId] })} />)}</div>
      <details className="local-voice-box"><summary>Dọn file trung gian</summary>
        <label><input type="checkbox" checked={cleanupConfirmed} onChange={event => setCleanupConfirmed(event.target.checked)} /> Xóa file trung gian của kết quả đã duyệt/từ chối; giữ native, mẫu giọng, video kết quả và lịch sử. File trung gian có thể tạo lại.</label>
        <button disabled={locked || !cleanupConfirmed} onClick={() => send('local-voice.cleanup', { confirmed: cleanupConfirmed })}>Dọn file trung gian local</button>
      </details>
    </>}
  </section>;
}

function ReviewCard({ item, anchor, title, locked, review, retry }: {
  item: LocalVoiceAnchor | LocalVoiceJob; anchor: boolean; title: string; locked: boolean;
  review: (id: string, anchor: boolean, approve: boolean) => void; retry?: () => void;
}) {
  const [listened, setListened] = useState(false);
  const [quality, setQuality] = useState(false);
  const nativePlayer = useRef<HTMLVideoElement>(null);
  const resultPlayer = useRef<HTMLVideoElement>(null);
  const native = 'nativePreviewUrl' in item ? item.nativePreviewUrl : null;
  return <article className="local-voice-box">
    <h4>{title}</h4><p>{localVoiceStatusLabel(item.status)}</p>
    {item.message && <p>{item.message}</p>}
    {native && <div><span>Bản native gốc</span><video ref={nativePlayer} aria-label={`${title} native`} controls preload="metadata" src={native} onPlay={() => resultPlayer.current?.pause()} /></div>}
    {item.previewUrl && (anchor ? <audio aria-label={`${title} mẫu giọng`} controls preload="metadata" src={item.previewUrl} onEnded={() => setListened(true)} /> :
      <div><span>Kết quả</span><video ref={resultPlayer} aria-label={`${title} kết quả`} controls preload="metadata" src={item.previewUrl} onPlay={() => nativePlayer.current?.pause()} onEnded={() => setListened(true)} /></div>)}
    {item.status === 'ReviewRequired' && <>
      <label><input type="checkbox" checked={quality} onChange={event => setQuality(event.target.checked)} />
        {anchor ? 'Mẫu sạch, đúng một giọng nhân vật và được phép sử dụng.' : 'Đúng câu chữ, đúng giọng mẫu, khẩu hình khớp và âm nền chấp nhận được.'}</label>
      {!listened && <p>Phát hết bản xem/nghe để mở nút duyệt.</p>}
      <div className="local-voice-actions"><button disabled={locked || !listened || !quality} onClick={() => review(item.id, anchor, true)}>Duyệt {anchor ? 'mẫu giọng' : 'kết quả'}</button>
        <button disabled={locked} onClick={() => review(item.id, anchor, false)}>Không đạt</button></div>
    </>}
    {retry && ['Failed', 'Cancelled', 'Interrupted', 'Rejected', 'Stale'].includes(item.status) && <button disabled={locked} onClick={retry}>Chạy lại local</button>}
  </article>;
}
