import { useEffect, useRef, useState } from 'react';
import { Check, ChevronDown, Clock3, Copy, Film, ImageIcon, Lightbulb, LoaderCircle, Minus, Play, Plus, RefreshCw, ShieldCheck, Volume2 } from 'lucide-react';
import type { ProjectDashboard, ShortVideoAssetRef, ShortVideoCreatedNotice, ShortVideoDraft, ShortVideoDraftState, ShortVideoLibraryAsset, ShortVideoLibraryState, ShortVideoOutfitView, ShortVideoQuote } from '../../types';
import { navigateShortVideoTabs, ShortDialog, ShortVideoLibrary } from './ShortVideoLibrary';
import { useShortVideoHost } from './useShortVideoHost';
import './outfitShortVideo.css';
import { ShortVideoVeoMigration } from './ShortVideoVeoMigration';

const emptyDraft = (): ShortVideoDraft => ({ content: '', aspectRatio: '9:16', durationSeconds: 8, audioEnabled: true, character: null, outfit: null, background: '', motion: '', serverRevision: 0 });
const equalRef = (a: ShortVideoAssetRef | null, b: ShortVideoAssetRef) => a?.assetId === b.assetId && a.version === b.version;
const templates = [
  { name: 'Studio tối giản', text: 'Nhân vật đứng trong studio sáng, phông nền xám nhạt, thấy rõ toàn bộ trang phục. Nhân vật bước chậm về phía máy quay rồi xoay nhẹ, ánh sáng mềm, màu sắc tự nhiên.' },
  { name: 'Dạo phố cổ', text: 'Nhân vật bước chậm trên phố cổ Hội An lúc bình minh, đèn lồng lay nhẹ trong gió. Máy quay lùi mượt, giữ toàn thân trong khung hình, làm nổi bật chất liệu và màu sắc trang phục.' },
  { name: 'Giới thiệu chi tiết', text: 'Nhân vật đứng cạnh cửa sổ trong căn phòng sáng, nhẹ nhàng xoay người để giới thiệu trang phục. Máy quay tiến chậm, thể hiện rõ đường may và chất liệu, ánh sáng ban ngày dịu nhẹ.' },
];
type Applied = { projectId: string; view: ShortVideoOutfitView; library: ShortVideoLibraryState };

export function OutfitShortVideo({ project, organizationId, busy, enabled, onExport, onTextOnly, onNewDraft, onCreated, createdNotice }: {
  project: ProjectDashboard | null; organizationId: string; busy: boolean; enabled: boolean; onExport: () => void;
  onTextOnly?: () => void; onNewDraft?: () => void; onCreated?: () => void;
  createdNotice?: ShortVideoCreatedNotice | null;
}) {
  const projectId = project?.project.projectId;
  const request = useShortVideoHost(organizationId, projectId);
  const [draft, setDraft] = useState<ShortVideoDraft>(emptyDraft);
  const draftRef = useRef(draft); draftRef.current = draft;
  const persisted = useRef<ShortVideoDraftState>({ revision: 0, draft: null });
  const saving = useRef<Promise<ShortVideoDraftState> | null>(null);
  const [items, setItems] = useState<ShortVideoLibraryAsset[]>([]);
  const [selectionCache, setSelectionCache] = useState<ShortVideoLibraryAsset[]>([]);
  const [view, setView] = useState<ShortVideoOutfitView | null>(null);
  const [ready, setReady] = useState(false); const [working, setWorking] = useState(false);
  const [libraryBusy, setLibraryBusy] = useState(false);
  const [error, setError] = useState(''); const [notice, setNotice] = useState('');
  const [savedText, setSavedText] = useState('Đang tải thư viện…');
  const [advanced, setAdvanced] = useState(false); const [tab, setTab] = useState<'image' | 'video'>('image');
  const [quote, setQuote] = useState<ShortVideoQuote | null>(null);
  const [imageLoaded, setImageLoaded] = useState(false); const [imageChecked, setImageChecked] = useState(false);
  const [videoPlayed, setVideoPlayed] = useState(false); const [videoChecked, setVideoChecked] = useState(false);
  const [confirmation, setConfirmation] = useState<'change' | 'copy' | 'reload' | null>(null);
  const [suggestion, setSuggestion] = useState<number | null>(null);
  const [, tick] = useState(0);
  const alive = useRef(true);
  const consumedQuote = useRef<string | null>(null);
  useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []);
  const composition = view?.state.composition;
  const revision = view?.state.revision ?? 0;
  const scene = project?.scenes[0]; const preview = scene?.preview;
  const resumableVideo = Boolean(scene?.canResumeVideo && !preview?.url);
  const locked = !enabled || !organizationId || !ready || busy || working || libraryBusy || (Boolean(projectId) && !view?.state.enabled);
  const effectiveBackground = draft.background.trim() || draft.content.trim();
  const effectiveMotion = draft.motion.trim() || draft.content.trim();
  const findAsset = (reference: ShortVideoAssetRef | null) => items.find(item => equalRef(reference, item)) ?? selectionCache.find(item => equalRef(reference, item)) ?? null;
  const character = findAsset(draft.character); const outfit = findAsset(draft.outfit);
  const hasImages = Boolean((character || view?.state.character) && (outfit || view?.state.outfit));
  const needsMigration = Boolean(project?.videoProviderCode && project.videoProviderCode !== 'fal') || Boolean(project && (![4, 6, 8].includes(project.project.targetDurationSeconds) || !['9:16', '16:9'].includes(project.project.aspectRatio)));
  const inputError = needsMigration ? 'Chuyển dự án sang Veo trước khi tiếp tục.' : ![4, 6, 8].includes(draft.durationSeconds) || !['9:16', '16:9'].includes(draft.aspectRatio) ? 'Veo hỗ trợ 4/6/8 giây, tỷ lệ 9:16 hoặc 16:9. Hãy chọn lại thiết lập.' : !draft.content.trim() ? 'Nhập nội dung cảnh để bắt đầu.' : !hasImages ? 'Chọn một nhân vật và một bộ trang phục.' : effectiveBackground.length > 1500 ? 'Nội dung trên 1.500 ký tự: nhập bối cảnh riêng ngắn hơn trong phần thiết lập bên dưới.' : !effectiveMotion ? 'Nhập chuyển động cho video.' : '';
  const dirty = Boolean(projectId && view && (effectiveBackground !== view.state.background || effectiveMotion !== view.state.motion ||
    (character && character.image.sha256 !== view.state.character?.sha256) || (outfit && outfit.image.sha256 !== view.state.outfit?.sha256)));
  const quoteExpired = Boolean(quote && Date.parse(quote.expiresAtUtc.endsWith('Z') ? quote.expiresAtUtc : `${quote.expiresAtUtc}Z`) <= Date.now());

  function update(values: Partial<ShortVideoDraft>) {
    const next = { ...draftRef.current, ...values };
    if (JSON.stringify(next) === JSON.stringify(draftRef.current)) return;
    draftRef.current = next; setDraft(next); setSavedText('Chưa lưu thay đổi');
    setQuote(null); setImageChecked(false); setVideoChecked(false);
  }
  function adoptLibrary(library: ShortVideoLibraryState) {
    setItems(library.items); setSelectionCache(library.selections ?? []); persisted.current = library.draft;
  }
  async function reload() {
    setReady(false); setError(''); setWorking(true);
    try {
      const [library, result] = await Promise.all([
        request<ShortVideoLibraryState>('short-library.get'),
        projectId ? request<ShortVideoOutfitView>('outfit.get') : Promise.resolve(null),
      ]);
      if (!alive.current) return;
      if (result && result.state.projectId !== projectId) throw new Error('Dữ liệu trả về không khớp dự án hiện hành.');
      adoptLibrary(library); setView(result);
      const snapshot = project ? { ...emptyDraft(), content: project.project.topic ?? '', aspectRatio: project.project.aspectRatio as ShortVideoDraft['aspectRatio'], durationSeconds: project.project.targetDurationSeconds, audioEnabled: project.audioStrategy !== 'SilentOutput', serverRevision: result?.state.revision ?? 0 } : emptyDraft();
      if (result && result.state.revision > 0) {
        if (result.state.background === result.state.motion) snapshot.content = result.state.background;
        else { snapshot.background = result.state.background; snapshot.motion = result.state.motion; }
      }
      let recovered = library.draft.draft;
      if (project && recovered && recovered.serverRevision !== result?.state.revision) {
        recovered = null;
        setNotice('Thiết lập dự án đã thay đổi từ lần lưu nháp trước. Đã tải phiên bản đang có của dự án; hãy chọn lại ảnh thư viện nếu cần.');
      } else setNotice(library.draft.createdProjectId ? 'Bản nháp đã bắt đầu lưu thành dự án. Bấm “Tiếp tục lưu dự án” để phục hồi đúng dự án đó.' : '');
      const next = recovered ? { ...recovered, ...(project ? { aspectRatio: snapshot.aspectRatio, durationSeconds: snapshot.durationSeconds, audioEnabled: snapshot.audioEnabled } : {}) } : snapshot;
      setDraft(next); draftRef.current = next; setAdvanced(Boolean(next.background || next.motion));
      setSavedText(library.draft.draft ? 'Đã khôi phục bản nháp' : project ? 'Đã tải dự án' : 'Bản nháp mới');
      setQuote(null); setReady(true);
    } catch (e) { if (alive.current && (e as Error).name !== 'AbortError') { setError((e as Error).message); setSavedText('Chưa tải được dữ liệu'); } }
    finally { if (alive.current) setWorking(false); }
  }
  useEffect(() => { if (enabled && organizationId) void reload(); }, [enabled, organizationId, projectId, project?.videoProviderCode, project?.project.targetDurationSeconds, project?.project.aspectRatio]);
  useEffect(() => { if (!enabled) { setQuote(null); setConfirmation(null); setSuggestion(null); setReady(false); } }, [enabled]);
  useEffect(() => { setImageLoaded(false); setImageChecked(false); }, [composition?.compositionId, revision]);
  useEffect(() => { setVideoPlayed(false); setVideoChecked(false); }, [preview?.url, composition?.compositionId, revision]);
  useEffect(() => { if (preview?.url || resumableVideo) setTab('video'); }, [preview?.url, resumableVideo]);
  useEffect(() => { if (quote) { const timer = window.setInterval(() => tick(value => value + 1), 1000); return () => window.clearInterval(timer); } }, [quote]);
  useEffect(() => {
    if (!ready || createdNotice?.projectId !== projectId || createdNotice?.organizationId !== organizationId) return;
    if (createdNotice.message) setNotice(createdNotice.message);
    const initial = createdNotice.quote;
    if (initial && initial.revision === revision && initial.quoteId !== consumedQuote.current) { consumedQuote.current = initial.quoteId; setQuote(initial); }
  }, [createdNotice, ready, revision, projectId, organizationId]);
  async function flushDraft(): Promise<ShortVideoDraftState> {
    if (saving.current) { await saving.current; return flushDraft(); }
    const current = draftRef.current;
    if (JSON.stringify(persisted.current.draft) === JSON.stringify(current) || persisted.current.createdProjectId) return persisted.current;
    setSavedText('Đang lưu bản nháp…');
    const operation = request<ShortVideoDraftState>('short-library.save-draft', { revision: persisted.current.revision, draft: current });
    saving.current = operation;
    try { const result = await operation; persisted.current = result; if (alive.current) setSavedText('Đã lưu trên máy'); return result; }
    finally { saving.current = null; }
  }
  useEffect(() => {
    if (!ready || busy || working || libraryBusy || persisted.current.createdProjectId) return;
    const timer = window.setTimeout(() => { void flushDraft().catch(e => { if (alive.current && e.name !== 'AbortError') { setSavedText('Chưa lưu được bản nháp'); setError(e.message); } }); }, 650);
    return () => window.clearTimeout(timer);
  }, [draft, ready, busy, working, libraryBusy]);
  async function execute(work: () => Promise<void>) {
    if (working) return;
    setWorking(true); setError('');
    try { await work(); } catch (e) { if (alive.current && (e as Error).name !== 'AbortError') setError((e as Error).message); }
    finally { if (alive.current) setWorking(false); }
  }
  async function workflow(type: string, payload: object = {}) {
    await execute(async () => {
      await flushDraft();
      if (type === 'outfit.compose' || type === 'outfit.video') setQuote(null);
      if (type === 'outfit.video' || type === 'outfit.resume') setTab('video');
      const result = await request<ShortVideoOutfitView | ShortVideoQuote>(type, { ...payload, revision });
      if (!alive.current) return;
      if (type === 'outfit.quote') { setQuote(result as ShortVideoQuote); return; }
      const next = result as ShortVideoOutfitView;
      if (next?.state.projectId !== projectId) throw new Error('Kết quả không khớp dự án hiện hành.');
      setView(next); setQuote(null);
      if (type === 'outfit.video' || type === 'outfit.resume') setTab('video');
    });
  }
  async function saveAndQuote() {
    if (resumableVideo && !dirty) { await workflow('outfit.resume'); return; }
    setConfirmation(null);
    await execute(async () => {
      const saved = await flushDraft();
      if (!projectId || dirty) {
        const result = await request<Applied>(projectId ? 'short-library.apply' : 'short-library.create', { revision: saved.revision });
        if (!alive.current) return;
        if (!projectId) { onCreated?.(); return; }
        if (result.projectId !== projectId) throw new Error('Kết quả lưu không khớp dự án.');
        adoptLibrary(result.library); setView(result.view);
        if (result.library.draft.draft) { setDraft(result.library.draft.draft); draftRef.current = result.library.draft.draft; }
        setQuote(await request<ShortVideoQuote>('outfit.quote', { revision: result.view.state.revision, kind: 'Image' }));
      } else setQuote(await request<ShortVideoQuote>('outfit.quote', { revision, kind: composition?.status === 'Approved' ? 'Video' : 'Image' }));
    });
  }
  const canQuoteVideo = !locked && !dirty && !needsMigration && composition?.status === 'Approved';
  const videoApproved = canQuoteVideo && scene?.status === 'Approved';
  const mainLabel = persisted.current.createdProjectId && !project ? 'Tiếp tục lưu dự án' : !project ? 'Lưu dự án và xem trước' : dirty || !composition || composition.status === 'Rejected' ? 'Xem trước trang phục' : resumableVideo ? 'Tiếp tục / tải lại video đã gửi' : composition.status === 'Approved' ? `Tạo video ${draft.durationSeconds} giây` : 'Chờ duyệt ảnh mặc thử';
  const pendingReview = composition?.status === 'PendingReview' && !dirty;
  return <section className="outfit-short-page" aria-label="Video ngắn nhân vật và trang phục">
    <div className="sv-page-heading"><div><span className="sv-eyebrow">VIDEO NGẮN · VEO 3.1</span><h1>{project ? project.project.name : 'Tạo video với nhân vật của bạn'}</h1></div>
      <div className="sv-heading-actions"><span className="sv-save-status" role="status"><Check size={13} />{savedText}</span>{onTextOnly && !project && <button disabled={locked} onClick={() => void execute(async () => { await flushDraft(); onTextOnly(); })}>Chỉ dùng mô tả</button>}<button className="sv-icon-button" aria-label="Tải lại trạng thái" disabled={busy || working || !enabled || !organizationId} onClick={() => setConfirmation('reload')}><RefreshCw size={16} /></button></div>
    </div>
    {!enabled && <p role="status" className="sv-warning">Chế độ nhân vật và trang phục chưa được bật.</p>}
    {!organizationId && <p role="status" className="sv-warning">Chọn tổ chức để mở thư viện nhân vật và trang phục.</p>}
    {error && <div className="sv-error" role="alert">{error}<button disabled={busy || working} onClick={() => setConfirmation('reload')}>Tải lại trạng thái</button></div>}
    {notice && <p className="sv-warning" role="status">{notice}</p>}
    {view?.state.message && <p className="sv-warning" role="status">{view.state.message}</p>}
    {project && needsMigration && <ShortVideoVeoMigration project={project} organizationId={organizationId} disabled={busy || working || libraryBusy || !enabled} beforeMigrate={flushDraft} />}
    <div className="sv-composer-grid">
      <div className="sv-panel sv-input-panel">
        <header className="sv-panel-heading"><span className="sv-step-number">1</span><div><h2>Nhập nội dung cảnh</h2><p>Mô tả bối cảnh, hành động và phong cách bạn mong muốn.</p></div><button className="sv-suggestion" disabled={locked} onClick={() => setSuggestion(0)}><Lightbulb size={15} />Gợi ý nội dung</button></header>
        <label className="sv-prompt-label">Nội dung dùng để tạo video<textarea value={draft.content} maxLength={2000} disabled={locked || Boolean(persisted.current.createdProjectId)} placeholder="Ví dụ: Nhân vật bước chậm giữa phố cổ Hội An lúc bình minh, máy quay lùi mượt, ánh sáng tự nhiên…" onChange={event => update({ content: event.target.value })} /></label>
        <div className="sv-field-help"><span>Ảnh chọn bên dưới giúp xác định nhân vật và trang phục.</span><span>{draft.content.length.toLocaleString('vi-VN')}/2.000</span></div>
        <fieldset className="sv-ratio-field" disabled={locked || Boolean(project) || Boolean(persisted.current.createdProjectId)}><legend>Tỷ lệ khung hình</legend><div className="sv-ratios">{(['9:16', '16:9'] as const).map(ratio => <button key={ratio} aria-pressed={draft.aspectRatio === ratio} className={draft.aspectRatio === ratio ? 'is-selected' : ''} onClick={() => update({ aspectRatio: ratio })}><span className={`sv-ratio-icon ratio-${ratio.replace(':', '-')}`} /><span><strong>{ratio}</strong><small>{ratio === '9:16' ? 'Video dọc' : ratio === '16:9' ? 'Video ngang' : 'Video vuông'}</small></span></button>)}</div></fieldset>
        <ShortVideoLibrary available={enabled} items={items} selectedCharacter={character} selectedOutfit={outfit} characterPreview={view?.characterPreview} outfitPreview={view?.outfitPreview} disabled={locked || Boolean(persisted.current.createdProjectId)} request={async <T,>(type: string, payload?: object) => { setLibraryBusy(true); try { if (saving.current) await saving.current; return await request<T>(type, payload); } finally { if (alive.current) setLibraryBusy(false); } }}
          onChanged={(item, removedId) => { if (item) { setItems(current => current.some(old => old.assetId === item.assetId) ? current.map(old => old.assetId === item.assetId ? item : old) : [...current, item]); setSelectionCache(current => [...current.filter(old => !equalRef(old, item)), item]); } if (removedId) { setSelectionCache(current => [...current, ...items.filter(item => item.assetId === removedId)]); setItems(current => current.filter(item => item.assetId !== removedId)); } }}
          onSelect={item => { setSelectionCache(current => [...current.filter(old => !equalRef(old, item)), item]); update({ [item.kind === 'Character' ? 'character' : 'outfit']: { assetId: item.assetId, version: item.version } }); }} />
        <fieldset className="sv-duration" disabled={locked || Boolean(project) || Boolean(persisted.current.createdProjectId)}><legend>Thời lượng video</legend><span className="sv-duration-badge">{draft.durationSeconds} giây</span><div><button aria-label="Giảm thời lượng" disabled={draft.durationSeconds <= 4} onClick={() => update({ durationSeconds: Math.max(4, draft.durationSeconds - 2) })}><Minus size={16} /></button><input aria-label="Thời lượng video" type="range" min={4} max={8} step={2} value={draft.durationSeconds} onChange={event => update({ durationSeconds: Number(event.target.value) })} /><button aria-label="Tăng thời lượng" disabled={draft.durationSeconds >= 8} onClick={() => update({ durationSeconds: Math.min(8, draft.durationSeconds + 2) })}><Plus size={16} /></button></div><div className="sv-range-ticks"><span>4s</span><span>6s</span><span>8s</span></div></fieldset>
        <div className="sv-facts"><div><Clock3 size={19} /><span><small>Thời lượng đầu ra</small><strong>{draft.durationSeconds} giây</strong></span></div><div><Volume2 size={19} /><span><small>Âm thanh</small><strong>{draft.audioEnabled ? 'Âm thanh gốc, không lời thoại' : 'Xuất video im lặng'}</strong></span><button role="switch" aria-label="Giữ âm thanh khi xuất" aria-checked={draft.audioEnabled} className="sv-switch" disabled={locked || Boolean(project) || Boolean(persisted.current.createdProjectId)} onClick={() => update({ audioEnabled: !draft.audioEnabled })}><span /></button></div><div><ShieldCheck size={19} /><span><small>Kiểm soát chi phí</small><strong>Xác nhận giá trước khi tạo</strong></span></div></div>
        {project && <div className="sv-snapshot-note"><span>Tỷ lệ, thời lượng và âm thanh đã được lưu cùng dự án.</span>{onNewDraft && <button className="sv-text-button" disabled={locked || !draft.character || !draft.outfit} onClick={() => setConfirmation('copy')}><Copy size={13} />Tạo bản sao để đổi</button>}</div>}
        <button className="sv-advanced-toggle" aria-expanded={advanced} onClick={() => setAdvanced(!advanced)}><ChevronDown size={15} className={advanced ? 'is-open' : ''} />Bối cảnh / chuyển động riêng{(draft.background || draft.motion) && <span>Đang dùng</span>}</button>
        {advanced && <div className="sv-advanced"><label>Bối cảnh ảnh <small>(tối đa 1.500 ký tự)</small><textarea maxLength={1500} value={draft.background} disabled={locked || Boolean(persisted.current.createdProjectId)} placeholder="Để trống để dùng nội dung chung" onChange={event => update({ background: event.target.value })} /></label><label>Chuyển động trong video<textarea maxLength={2000} value={draft.motion} disabled={locked || Boolean(persisted.current.createdProjectId)} placeholder="Để trống để dùng nội dung chung" onChange={event => update({ motion: event.target.value })} /></label><details><summary>Xem nội dung sẽ được lưu</summary><p><strong>Bối cảnh:</strong> {effectiveBackground || 'Chưa nhập'}</p><p><strong>Chuyển động:</strong> {effectiveMotion || 'Chưa nhập'}</p></details></div>}
        {dirty && <p className="sv-warning">Đầu vào đã thay đổi. Khi lưu, bạn cần tạo và duyệt lại ảnh, video.</p>}
        <div className="sv-generate-footer"><div><small>{quote ? `Chi phí ${quote.kind === 'Image' ? 'ảnh mặc thử' : 'video'} ước tính` : 'Chi phí tạo ảnh / video'}</small><strong>{quote ? `${quote.estimatedCost.toLocaleString('vi-VN', { maximumFractionDigits: 6 })} ${quote.currencyCode}` : 'Xem báo giá trước khi tạo'}</strong></div><button className="sv-primary sv-main-action" disabled={locked || Boolean(inputError) || (pendingReview && !dirty)} onClick={() => dirty && composition ? setConfirmation('change') : void saveAndQuote()}>{working || busy ? <LoaderCircle size={18} className="sv-spin" /> : <Play size={18} fill="currentColor" />}{working || busy ? 'Đang xử lý…' : mainLabel}</button></div>
        {inputError && <p className="sv-help sv-input-hint">{inputError}</p>}
      </div>
      <aside className="sv-panel sv-result-panel">
        <header className="sv-panel-heading"><span className="sv-step-number">2</span><div><h2>Kết quả</h2><p>Xem và duyệt ảnh mặc thử trước khi tạo video.</p></div></header>
        <div className="sv-tabs" role="tablist" aria-label="Kết quả video ngắn" onKeyDown={navigateShortVideoTabs}><button role="tab" tabIndex={tab === 'image' ? 0 : -1} aria-selected={tab === 'image'} onClick={() => setTab('image')}><ImageIcon size={15} />Ảnh mặc thử</button><button role="tab" tabIndex={tab === 'video' ? 0 : -1} aria-selected={tab === 'video'} onClick={() => setTab('video')}><Film size={15} />Video</button></div>
        <div className="sv-preview" style={{ aspectRatio: draft.aspectRatio.replace(':', '/'), maxWidth: draft.aspectRatio === '9:16' ? '290px' : '100%' }}>
          {tab === 'image' && view?.compositionPreview ? <img className="outfit-composition" src={view.compositionPreview} alt="Nhân vật mặc trang phục đã phối" onLoad={() => setImageLoaded(true)} onError={() => { setImageLoaded(false); setError('Không tải được ảnh mặc thử. Hãy tải lại trạng thái.'); }} /> : tab === 'video' && !dirty && preview?.url ? <video key={preview.url} src={preview.url} controls preload="metadata" onEnded={() => setVideoPlayed(true)} aria-label="Video để kiểm tra nhân vật và trang phục" /> : <div className="sv-preview-empty">{tab === 'video' ? <Film size={37} /> : <ImageIcon size={37} />}<strong>{tab === 'video' ? 'Video sẽ xuất hiện tại đây' : 'Xem nhân vật mặc trang phục của bạn'}</strong><span>{tab === 'video' ? 'Duyệt ảnh mặc thử rồi tạo video.' : 'Chọn hai ảnh, nhập nội dung và xem báo giá để tạo ảnh mặc thử.'}</span>{(character || outfit) && <div className="sv-source-pair">{[character, outfit].map((item, index) => item && <img src={item.thumbnailUrl} alt={item.name} key={index} />)}</div>}</div>}
          {dirty && view?.compositionPreview && tab === 'image' && <span className="sv-stale-badge">Ảnh thuộc thiết lập trước</span>}
        </div>
        {(working || busy) && <p className="sv-processing" role="status"><LoaderCircle className="sv-spin" size={16} />Đang xử lý{project && project.overallProgressPercent > 0 ? ` · ${project.overallProgressPercent}%` : '…'}</p>}
        {(scene?.lastErrorMessage || project?.lastErrorMessage) && <p role="alert" className="sv-error">{scene?.lastErrorMessage || project?.lastErrorMessage}</p>}
        {tab === 'video' && resumableVideo && !dirty && <p role="status" className="sv-help">{scene?.videoRequestStatus === 'Completed' ? 'Video đã tạo xong, đang chờ tải về máy. Bấm tải lại video đã gửi để lấy kết quả.' : 'Tác vụ video đã được gửi. Bạn có thể tiếp tục theo dõi tác vụ này.'}</p>}
        {tab === 'image' && <div className="sv-result-actions">
          {composition && <p className={`sv-review-status ${composition.status === 'Approved' && !dirty ? 'is-approved' : ''}`}>{dirty ? 'Cần tạo ảnh từ đầu vào mới' : composition.status === 'Approved' ? '✓ Ảnh đã duyệt' : composition.status === 'Rejected' ? 'Ảnh đã từ chối' : 'Ảnh đang chờ duyệt'}</p>}
          {pendingReview && <><label className="sv-check"><input type="checkbox" disabled={locked || !imageLoaded} checked={imageChecked} onChange={event => setImageChecked(event.target.checked)} />Tôi đã xem ảnh và kiểm tra nhân vật, trang phục.</label><div className="sv-button-row"><button className="sv-primary" disabled={locked || needsMigration || !imageLoaded || !imageChecked} onClick={() => void workflow('outfit.approve', { compositionId: composition!.compositionId, confirmed: true })}><Check size={15} />Duyệt ảnh này</button><button disabled={locked || needsMigration} onClick={() => void workflow('outfit.reject', { compositionId: composition!.compositionId, confirmed: true })}>Từ chối ảnh</button></div></>}
          {composition && <button className="sv-text-button" disabled={locked || dirty || Boolean(inputError)} onClick={() => void workflow('outfit.quote', { kind: 'Image' })}><RefreshCw size={14} />Báo giá tạo lại ảnh</button>}
        </div>}
        {tab === 'video' && <div className="sv-result-actions">
          {!dirty && preview?.url && scene?.status === 'AudioReviewRequired' && <><label className="sv-check"><input type="checkbox" disabled={locked || !videoPlayed} checked={videoChecked} onChange={event => setVideoChecked(event.target.checked)} />Tôi đã xem hết video, nhân vật và trang phục đúng với ảnh đã duyệt.</label><button className="sv-primary" disabled={locked || !videoPlayed || !videoChecked} onClick={() => void workflow('outfit.approve-video', { confirmed: true })}>Duyệt video</button></>}
          <button disabled={!canQuoteVideo || !resumableVideo} onClick={() => void workflow('outfit.resume')}>Tiếp tục / tải lại video đã gửi</button>
          <div className="sv-button-row"><button disabled={!videoApproved} onClick={() => void workflow('outfit.render')}>Chuẩn bị MP4</button><button className="sv-primary" disabled={!videoApproved || project?.render.status !== 'Completed' || !project.preview?.url} onClick={onExport}>Xuất MP4</button></div>
        </div>}
        <div className="sv-assurances"><p><Check size={14} />Ảnh thư viện được lưu để dùng lại.</p><p><Check size={14} />Bạn duyệt ảnh và video trước khi xuất.</p><p><ShieldCheck size={14} />Mỗi lần tạo đều có báo giá và xác nhận riêng.</p></div>
      </aside>
    </div>
    {enabled && quote && <ShortDialog title={quote.kind === 'Image' ? 'Xác nhận tạo ảnh mặc thử' : 'Xác nhận tạo video'} busy={working || busy} onClose={() => setQuote(null)}><div className="sv-dialog-body sv-quote" role="region" aria-label="Xác nhận chi phí"><span>Chi phí ước tính cho {quote.kind === 'Image' ? 'ảnh mặc thử' : 'video'}</span><strong>{quote.estimatedCost.toLocaleString('vi-VN', { maximumFractionDigits: 6 })} {quote.currencyCode}</strong><p>{quote.modelCode} · {quote.resolution}</p><p>Chi phí thực tế được quyết toán theo mức sử dụng. Báo giá này chỉ áp dụng cho lần tạo {quote.kind === 'Image' ? 'ảnh' : 'video'} đang chọn.</p><p className={quoteExpired ? 'sv-error' : 'sv-help'}>{quoteExpired ? 'Báo giá đã hết hạn. Hãy đóng và lấy báo giá mới.' : `Hết hạn lúc ${new Date(quote.expiresAtUtc.endsWith('Z') ? quote.expiresAtUtc : `${quote.expiresAtUtc}Z`).toLocaleTimeString('vi-VN')}.`}</p></div><footer><button disabled={working || busy} onClick={() => setQuote(null)}>Đóng báo giá</button><button className="sv-primary" disabled={locked || dirty || quoteExpired || quote.revision !== revision} onClick={() => void workflow(quote.kind === 'Image' ? 'outfit.compose' : 'outfit.video', { quoteId: quote.quoteId, confirmed: true })}>Xác nhận chi phí và tạo {quote.kind === 'Image' ? 'ảnh' : 'video'}</button></footer></ShortDialog>}
    {enabled && confirmation && <ShortDialog title={confirmation === 'copy' ? 'Tạo bản sao dự án' : confirmation === 'reload' ? 'Tải lại trạng thái' : 'Lưu thay đổi đầu vào'} onClose={() => setConfirmation(null)} busy={working}><div className="sv-dialog-body"><p>{confirmation === 'copy' ? 'Tạo bản nháp mới với nội dung và hai ảnh đang chọn để đổi tỷ lệ, thời lượng hoặc âm thanh. Bản nháp mới chưa lưu thành dự án trước đó (nếu có) sẽ được thay thế.' : confirmation === 'reload' ? 'Tải lại dữ liệu đã lưu trên máy và trạng thái dự án. Các thay đổi chưa lưu sẽ được bỏ.' : 'Thay đổi ảnh hoặc nội dung sẽ yêu cầu tạo và duyệt lại ảnh, video. Kết quả cũ sẽ không được dùng để xuất bản mới.'}</p></div><footer><button disabled={working} onClick={() => setConfirmation(null)}>Hủy</button><button className="sv-primary" disabled={working} onClick={() => { if (confirmation === 'change') void saveAndQuote(); else if (confirmation === 'reload') { setConfirmation(null); void execute(async () => { if (saving.current) await saving.current; await reload(); }); } else void execute(async () => { await flushDraft(); await request('short-library.copy', { draft: draftRef.current }); setConfirmation(null); onNewDraft?.(); }); }}>Tiếp tục</button></footer></ShortDialog>}
    {enabled && suggestion !== null && <ShortDialog title="Gợi ý nội dung cảnh" onClose={() => setSuggestion(null)}><div className="sv-dialog-body"><p className="sv-help">Chọn một mẫu rồi chỉnh lại theo trang phục của bạn.</p><div className="sv-template-list">{templates.map((template, index) => <button aria-pressed={suggestion === index} key={template.name} onClick={() => setSuggestion(index)}>{template.name}</button>)}</div><p className="sv-template-preview">{templates[suggestion].text}</p></div><footer><button onClick={() => setSuggestion(null)}>Hủy</button><button className="sv-primary" disabled={locked} onClick={() => { update({ content: templates[suggestion].text }); setSuggestion(null); }}>Dùng nội dung này</button></footer></ShortDialog>}
  </section>;
}
