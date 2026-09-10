import { useEffect, useState } from 'react';
import { Check, Clock3, Film, ImageIcon, LoaderCircle, Play, ShieldCheck, Volume2 } from 'lucide-react';
import type { CreateShortVideoPayload, GenerationProviderStatus, MediaToolStatus, ProjectDashboard, SceneFirstFrameSummary, SceneSummary, ShortVideoQuote } from '../../types';
import { useShortVideoHost } from './useShortVideoHost';
import { ShortDialog } from './ShortVideoLibrary';
import { ShortVideoVeoMigration } from './ShortVideoVeoMigration';
import './outfitShortVideo.css';

export function TextShortVideo({ project, organizationId, providerStatus, mediaTools, busy, outfitEnabled, onCreateOutfit,
  onCreate, frames, onRequestImage, onApproveImage, onRejectImage, onDownloadImage,
  onApproveVideo, onRender, onExport, imageError }: {
  project: ProjectDashboard | null; organizationId: string; providerStatus: GenerationProviderStatus; mediaTools: MediaToolStatus;
  busy: boolean; outfitEnabled: boolean; onCreateOutfit: () => void;
  onCreate: (payload: CreateShortVideoPayload) => void;
  frames: SceneFirstFrameSummary[]; imageError?: string;
  onRequestImage: (scene: SceneSummary, regenerate: boolean) => void;
  onApproveImage: (frame: SceneFirstFrameSummary) => void; onRejectImage: (frame: SceneFirstFrameSummary) => void;
  onDownloadImage: (frame: SceneFirstFrameSummary) => void; onApproveVideo: (sceneId: string, confirmed: boolean) => void;
  onRender: () => void; onExport: () => void;
}) {
  const request = useShortVideoHost(organizationId, project?.project.projectId);
  const [quote, setQuote] = useState<ShortVideoQuote | null>(null);
  const [, tick] = useState(0);
  useEffect(() => { if (!quote) return; const timer = window.setInterval(() => tick(n => n + 1), 1000); return () => window.clearInterval(timer); }, [quote]);
  const quoteExpired = Boolean(quote && Date.parse(quote.expiresAtUtc.endsWith('Z') ? quote.expiresAtUtc : `${quote.expiresAtUtc}Z`) <= Date.now());
  const [working, setWorking] = useState(false); const [error, setError] = useState('');
  async function run(type: string, payload = {}) {
    if (working || busy) return;
    setWorking(true); setError('');
    try {
      const result = await request<ShortVideoQuote>(type, payload);
      if (type === 'outfit.text-quote') setQuote(result);
    } catch (e) { if ((e as Error).name !== 'AbortError') setError((e as Error).message); }
    finally { setWorking(false); }
  }
  const [content, setContent] = useState(project?.project.topic ?? '');
  const [ratio, setRatio] = useState<CreateShortVideoPayload['aspectRatio']>((project?.project.aspectRatio as CreateShortVideoPayload['aspectRatio']) ?? '9:16');
  const [duration, setDuration] = useState(project?.project.targetDurationSeconds ?? 8);
  const [audio, setAudio] = useState(project?.audioStrategy !== 'SilentOutput');
  const [tab, setTab] = useState<'image' | 'video'>('image');
  const [imageLoaded, setImageLoaded] = useState(false); const [imageChecked, setImageChecked] = useState(false);
  const [videoPlayed, setVideoPlayed] = useState(false); const [videoChecked, setVideoChecked] = useState(false);
  const scene = project?.scenes[0];
  const frame = frames.filter(f => f.sceneId === scene?.sceneId).sort((a, b) => b.version - a.version)[0];
  const preview = scene?.preview;
  const resumableVideo = Boolean(scene?.canResumeVideo && !preview?.url);
  useEffect(() => { if (preview?.url || resumableVideo) setTab('video'); }, [preview?.url, resumableVideo]);
  const legacy = Boolean(project && ((project.videoProviderCode && project.videoProviderCode !== 'fal') || ![4, 6, 8].includes(duration) || !['9:16', '16:9'].includes(ratio)));
  const approvedImage = Boolean(frame?.status === 'Approved' && frame.isCurrent && frame.previewUrl);
  const locked = busy || working || !organizationId;
  const canGenerate = !locked && !legacy && approvedImage && mediaTools.ready && providerStatus.videoReady && Boolean(scene);
  useEffect(() => { setImageLoaded(false); setImageChecked(false); setQuote(null); }, [frame?.sceneFirstFrameId, frame?.previewUrl]);
  useEffect(() => { setVideoPlayed(false); setVideoChecked(false); }, [preview?.url]);
  const payload: CreateShortVideoPayload = { content: content.trim(), aspectRatio: ratio, durationSeconds: duration, audioEnabled: audio };
  return <section className="outfit-short-page" aria-label="Video ngắn từ nội dung">
    <div className="sv-page-heading"><div><span className="sv-eyebrow">VIDEO NGẮN · VEO 3.1</span><h1>{project?.project.name ?? 'Tạo video từ nội dung'}</h1></div>
      {outfitEnabled && <button disabled={locked} onClick={onCreateOutfit}>Nhân vật + trang phục</button>}</div>
    {project && legacy && <ShortVideoVeoMigration project={project} organizationId={organizationId} disabled={locked} />}
    <div className="sv-composer-grid"><div className="sv-panel sv-input-panel">
      <header className="sv-panel-heading"><span className="sv-step-number">1</span><div><h2>Nhập nội dung cảnh</h2><p>Tạo ảnh đầu vào, duyệt ảnh rồi tạo clip Veo.</p></div></header>
      <label className="sv-prompt-label">Nội dung dùng để tạo video<textarea maxLength={2000} disabled={locked || Boolean(project)} value={content} onChange={e => setContent(e.target.value)} placeholder="Mô tả chủ thể, bối cảnh và chuyển động bạn mong muốn…" /></label>
      <div className="sv-field-help"><span>OpenAI tạo ảnh từ nội dung; Veo tạo chuyển động từ ảnh đã duyệt.</span><span>{content.length}/2.000</span></div>
      <fieldset className="sv-ratio-field" disabled={locked || Boolean(project)}><legend>Tỷ lệ khung hình</legend><div className="sv-ratios">{(['9:16', '16:9'] as const).map(r => <button key={r} className={r === ratio ? 'is-selected' : ''} aria-pressed={r === ratio} onClick={() => setRatio(r)}><span className={`sv-ratio-icon ratio-${r.replace(':', '-')}`} /><strong>{r}</strong></button>)}</div></fieldset>
      <fieldset className="sv-duration" disabled={locked || Boolean(project)}><legend>Thời lượng video</legend><div className="sv-ratios">{[4, 6, 8].map(n => <button key={n} aria-pressed={n === duration} className={n === duration ? 'is-selected' : ''} onClick={() => setDuration(n)}>{n} giây</button>)}</div></fieldset>
      <div className="sv-facts"><div><Clock3 size={19} /><strong>{duration} giây</strong></div><div><Volume2 size={19} /><span>{audio ? 'Âm thanh môi trường' : 'Xuất video im lặng'}</span><button className="sv-switch" role="switch" aria-label="Giữ âm thanh khi xuất" aria-checked={audio} disabled={locked || Boolean(project)} onClick={() => setAudio(!audio)}><span /></button></div><div><ShieldCheck size={19} /><span>Xác nhận giá trước khi tạo</span></div></div>
      {!mediaTools.ready && <p className="sv-warning">{mediaTools.message}</p>}
      {!providerStatus.videoReady && <p className="sv-warning">{providerStatus.videoUnavailableMessage ?? 'Veo chưa sẵn sàng.'}</p>}
      {!project ? <div className="sv-generate-footer"><span>Lưu dự án chưa phát sinh phí AI.</span><button className="sv-primary" disabled={locked || !content.trim() || content.length > 2000} onClick={() => onCreate(payload)}>Lưu dự án và xem trước</button></div> : <div className="sv-generate-footer"><div><small>Chi phí video Veo ước tính</small><strong>{providerStatus.estimatedVideoCostPerSecond ? `${(providerStatus.estimatedVideoCostPerSecond * duration).toLocaleString('vi-VN')} ${providerStatus.currencyCode}` : 'Xem báo giá trước khi tạo'}</strong></div><button className="sv-primary" disabled={!canGenerate} onClick={() => { if (resumableVideo) setTab('video'); void run(resumableVideo ? 'outfit.text-resume' : 'outfit.text-quote'); }}><Play size={17} />{resumableVideo ? 'Tiếp tục / tải lại video đã gửi' : `Tạo video ${duration} giây`}</button></div>}
      {project && !approvedImage && <p className="sv-help">Tạo và duyệt ảnh đầu vào ở khung Kết quả để tiếp tục.</p>}
    </div><aside className="sv-panel sv-result-panel">
      <header className="sv-panel-heading"><span className="sv-step-number">2</span><div><h2>Kết quả</h2><p>Duyệt ảnh và video trước khi xuất MP4.</p></div></header>
      <div className="sv-tabs"><button aria-pressed={tab === 'image'} onClick={() => setTab('image')}><ImageIcon size={15} />Ảnh đầu vào</button><button aria-pressed={tab === 'video'} onClick={() => setTab('video')}><Film size={15} />Video</button></div>
      <div className="sv-preview" style={{ aspectRatio: ratio.replace(':', '/'), maxWidth: ratio === '9:16' ? 290 : '100%' }}>
        {tab === 'image' && frame?.previewUrl ? <img src={frame.previewUrl} alt="Ảnh đầu vào Veo" onLoad={() => setImageLoaded(true)} onError={() => setImageLoaded(false)} /> : tab === 'video' && preview?.url ? <video src={preview.url} controls onEnded={() => setVideoPlayed(true)} /> : <div className="sv-preview-empty"><ImageIcon size={36} /><strong>{tab === 'image' ? 'Ảnh đầu vào sẽ xuất hiện tại đây' : 'Video sẽ xuất hiện tại đây'}</strong><span>{project ? 'Xem báo giá để tạo ảnh từ nội dung đã lưu.' : 'Nhập nội dung và lưu dự án để bắt đầu.'}</span></div>}
      </div>
      {(busy || working) && <p className="sv-processing" role="status"><LoaderCircle size={16} className="sv-spin" />Đang xử lý…</p>}
      {error && <p className="sv-error" role="alert">{error}</p>}
      {imageError && <p className="sv-error" role="alert">{imageError}</p>}
      {(scene?.lastErrorMessage || project?.lastErrorMessage) && <p className="sv-error" role="alert">{scene?.lastErrorMessage || project?.lastErrorMessage}</p>}
      {tab === 'video' && resumableVideo && <p role="status" className="sv-help">{scene?.videoRequestStatus === 'Completed' ? 'Video đã tạo xong, đang chờ tải về máy. Bấm tải lại video đã gửi để lấy kết quả.' : 'Tác vụ video đã được gửi. Bạn có thể tiếp tục theo dõi tác vụ này.'}</p>}
      {tab === 'image' && project && scene && <div className="sv-result-actions">
        {approvedImage && <p className="sv-review-status is-approved">✓ Ảnh đã duyệt</p>}
        {frame?.staleReason && <p className="sv-warning">{frame.staleReason}</p>}
        {frame?.status === 'PendingReview' && frame.isCurrent && <><label className="sv-check"><input type="checkbox" checked={imageChecked} disabled={locked || !imageLoaded} onChange={e => setImageChecked(e.target.checked)} />Tôi đã xem và kiểm tra ảnh đầu vào.</label><div className="sv-button-row"><button className="sv-primary" disabled={locked || !imageLoaded || !imageChecked || legacy} onClick={() => onApproveImage(frame)}>Duyệt ảnh này</button><button disabled={locked} onClick={() => onRejectImage(frame)}>Từ chối ảnh</button></div></>}
        {frame && !frame.previewUrl && <button disabled={locked} onClick={() => onDownloadImage(frame)}>Tải lại ảnh đã tạo</button>}
        <button disabled={locked || legacy || !providerStatus.openAiImageReady || !providerStatus.videoReady} onClick={() => onRequestImage(scene, Boolean(frame))}>{frame ? 'Báo giá tạo lại ảnh' : 'Xem báo giá tạo ảnh'}</button>
      </div>}
      {tab === 'video' && scene && <div className="sv-result-actions">{scene.status === 'AudioReviewRequired' && <><label className="sv-check"><input type="checkbox" checked={videoChecked} disabled={locked || !videoPlayed} onChange={e => setVideoChecked(e.target.checked)} />Tôi đã xem hết video và kiểm tra hình, âm thanh.</label><button disabled={locked || !videoPlayed || !videoChecked} onClick={() => onApproveVideo(scene.sceneId, true)}>Duyệt video</button></>}
        <button disabled={locked || legacy || !approvedImage || !resumableVideo} onClick={() => void run('outfit.text-resume')}>Tiếp tục / tải lại video đã gửi</button><div className="sv-button-row"><button disabled={locked || scene.status !== 'Approved' || legacy} onClick={onRender}>Chuẩn bị MP4</button><button className="sv-primary" disabled={locked || scene.status !== 'Approved' || project?.render.status !== 'Completed' || !project.preview?.url || legacy} onClick={onExport}>Xuất MP4</button></div></div>}
      <div className="sv-assurances"><p><Check size={14} />Báo giá riêng cho ảnh và video.</p><p><Check size={14} />Ảnh đã duyệt được dùng làm khung hình đầu tiên.</p></div>
    </aside></div>
    {quote && <ShortDialog title="Xác nhận tạo video Veo" busy={locked} onClose={() => setQuote(null)}><div className="sv-dialog-body sv-quote"><span>Chi phí video ước tính</span><strong>{quote.estimatedCost.toLocaleString('vi-VN', { maximumFractionDigits: 6 })} {quote.currencyCode}</strong><p>{quote.modelCode} · {quote.resolution} · {duration} giây</p>{quoteExpired && <p className="sv-error">Báo giá đã hết hạn. Hãy đóng và lấy báo giá mới.</p>}<p>Báo giá gắn với ảnh đã duyệt và thiết lập hiện hành. Mỗi lần tạo video mới cần xác nhận riêng.</p></div><footer><button disabled={locked} onClick={() => setQuote(null)}>Đóng báo giá</button><button className="sv-primary" disabled={locked || !approvedImage || quoteExpired} onClick={() => { const current = quote; setQuote(null); setTab('video'); void run('outfit.text-video', { quoteId: current.quoteId, confirmed: true }); }}>Xác nhận chi phí và tạo video</button></footer></ShortDialog>}
  </section>;
}
