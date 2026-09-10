import { useEffect, useRef, useState } from 'react';
import { ArrowLeft, ArrowRight, Film, FolderPlus, LoaderCircle, Play, X } from 'lucide-react';
import type { CreateProjectPayload, CreateShortVideoPayload, OpenAiVoiceOption } from '../../types';
import './newProjectDialog.css';

export type VideoProjectKind = 'short' | 'long';

export function NewProjectDialog({ organizationName, busy, error, speechSynchronizationEnabled, voiceOptions, onClose, onCreateLong, onCreateShort }: {
  organizationName: string;
  busy: boolean;
  error: string | null;
  speechSynchronizationEnabled: boolean;
  voiceOptions: OpenAiVoiceOption[];
  onClose: () => void;
  onCreateLong: (payload: CreateProjectPayload) => void;
  onCreateShort: (payload: CreateShortVideoPayload) => void;
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  const [kind, setKind] = useState<VideoProjectKind | null>(null);
  const [content, setContent] = useState('');
  const [aspectRatio, setAspectRatio] = useState<CreateShortVideoPayload['aspectRatio']>('9:16');
  const [duration, setDuration] = useState(8);
  const [audioEnabled, setAudioEnabled] = useState(true);
  const [speechPolicy, setSpeechPolicy] = useState<CreateProjectPayload['speechProductionPolicy']>('ProviderNativeVerified');
  const [voiceCode, setVoiceCode] = useState(voiceOptions.find(voice => voice.voiceCode === 'shimmer')?.voiceCode ?? voiceOptions[0]?.voiceCode ?? '');
  const [speakingRate, setSpeakingRate] = useState(1);
  useEffect(() => {
    const element = dialog.current!;
    const previousFocus = document.activeElement as HTMLElement | null;
    element.showModal();
    return () => { element.close(); previousFocus?.focus(); };
  }, []);
  useEffect(() => {
    if (kind) dialog.current?.querySelector<HTMLTextAreaElement>('textarea')?.focus();
  }, [kind]);
  const canonical = kind === 'long' && speechPolicy === 'CanonicalVoice';
  const valid = Boolean(kind && content.trim() && content.trim().length <= (kind === 'long' ? 300 : 2000) &&
    (!canonical || (speechSynchronizationEnabled && voiceOptions.some(voice => voice.voiceCode === voiceCode))));
  return <dialog ref={dialog} className="new-project-dialog" aria-labelledby="new-project-title" aria-describedby="new-project-description"
    onCancel={event => { event.preventDefault(); if (!busy) onClose(); }}>
    <div className="new-project-heading">
      <span className="new-project-icon"><FolderPlus size={24} /></span>
      <div><h2 id="new-project-title">{kind ? `Tạo dự án video ${kind === 'short' ? 'ngắn' : 'dài'}` : 'Bạn muốn tạo video nào?'}</h2>
        <p id="new-project-description">Dự án mới sẽ thuộc tổ chức <strong>{organizationName}</strong>.</p></div>
      <button type="button" className="new-project-close" aria-label="Đóng cửa sổ tạo dự án" disabled={busy} onClick={onClose}><X size={20} /></button>
    </div>
    {!kind ? <div className="new-project-types">
      <button type="button" disabled={busy} onClick={() => { setKind('short'); setAspectRatio('9:16'); }}>
        <span className="new-project-type-icon"><Play size={27} /></span><strong>Video ngắn</strong>
        <span>Một cảnh từ 5–15 giây, tạo trực tiếp từ nội dung bạn nhập.</span><small>Chọn video ngắn <ArrowRight size={16} /></small>
      </button>
      <button type="button" disabled={busy} onClick={() => { setKind('long'); setAspectRatio('16:9'); }}>
        <span className="new-project-type-icon"><Film size={27} /></span><strong>Video dài</strong>
        <span>Từ một chủ đề, xây dựng kịch bản nhiều cảnh và ghép thành video.</span><small>Chọn video dài <ArrowRight size={16} /></small>
      </button>
    </div> : <form onSubmit={event => {
      event.preventDefault();
      if (busy || !valid) return;
      if (kind === 'short') onCreateShort({ content: content.trim(), aspectRatio, durationSeconds: duration, audioEnabled });
      else onCreateLong({ topic: content.trim(), aspectRatio, languageCode: 'vi-VN', speechProductionPolicy: speechPolicy,
        voiceCode: canonical ? voiceCode : null, voiceSpeakingRate: canonical ? speakingRate : null });
    }}>
      <fieldset disabled={busy}>
        <label className="new-project-field">{kind === 'short' ? 'Nội dung cảnh' : 'Chủ đề video'}
          <textarea required maxLength={kind === 'short' ? 2000 : 300} value={content} onChange={event => setContent(event.target.value)}
            placeholder={kind === 'short' ? 'Mô tả chủ thể, bối cảnh, hành động và phong cách của cảnh…' : 'Ví dụ: Lợi ích của việc đạp xe mỗi ngày…'} />
        </label>
        <div className="new-project-options">
          <label className="new-project-field">Tỷ lệ khung hình<select value={aspectRatio} onChange={event => setAspectRatio(event.target.value as typeof aspectRatio)}>
            <option value="9:16">Dọc · 9:16</option><option value="16:9">Ngang · 16:9</option><option value="1:1">Vuông · 1:1</option>
          </select></label>
          {kind === 'short' ? <>
            <label className="new-project-field">Thời lượng<select value={duration} onChange={event => setDuration(Number(event.target.value))}>
              {Array.from({ length: 11 }, (_, index) => index + 5).map(seconds => <option key={seconds} value={seconds}>{seconds} giây</option>)}
            </select></label>
            <label className="new-project-audio"><input type="checkbox" checked={audioEnabled} onChange={event => setAudioEnabled(event.target.checked)} /> Giữ âm thanh trong video</label>
          </> : <label className="new-project-field">Âm thanh<select value={speechPolicy} onChange={event => setSpeechPolicy(event.target.value as typeof speechPolicy)}>
            <option value="ProviderNativeVerified">Provider Native Audio</option>
            {speechSynchronizationEnabled && <option value="CanonicalVoice">Canonical Voice</option>}
          </select></label>}
          {canonical && <>
            <label className="new-project-field">Giọng người dẫn<select required value={voiceCode} onChange={event => setVoiceCode(event.target.value)}>
              <option value="" disabled>Chọn giọng</option>{voiceOptions.map(voice => <option key={voice.voiceCode} value={voice.voiceCode}>{voice.displayName}</option>)}
            </select></label>
            <label className="new-project-field">Tốc độ đọc<select value={speakingRate} onChange={event => setSpeakingRate(Number(event.target.value))}>
              <option value={0.9}>Chậm · 0,9×</option><option value={1}>Tự nhiên · 1,0×</option><option value={1.1}>Nhanh · 1,1×</option>
            </select></label>
          </>}
        </div>
      </fieldset>
      {error && <p className="new-project-error" role="alert">{error}</p>}
      <div className="new-project-footer">
        <button type="button" className="new-project-back" disabled={busy} onClick={() => setKind(null)}><ArrowLeft size={16} /> Chọn lại loại video</button>
        <button type="submit" className="start-button" disabled={busy || !valid}>{busy ? <LoaderCircle className="spin" size={18} /> : <FolderPlus size={18} />}{busy ? 'Đang tạo dự án…' : 'Tạo dự án mới'}</button>
      </div>
    </form>}
  </dialog>;
}
