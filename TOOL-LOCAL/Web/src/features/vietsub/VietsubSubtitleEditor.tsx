import {
  forwardRef,
  memo,
  useCallback,
  useEffect,
  useImperativeHandle,
  useRef,
  useState
} from 'react';
import {
  AlignStartHorizontal,
  ChevronLeft,
  ChevronRight,
  CopyPlus,
  Download,
  FileText,
  LockKeyhole,
  Scissors,
  Search,
  Trash2,
  Upload
} from 'lucide-react';
import type {
  VietsubSubtitleCue,
  VietsubSubtitlePage,
  VietsubSubtitlePageQuery,
  VietsubSaveState,
  VietsubSubtitleStatus,
  VietsubSubtitleWorkspace
} from './types';

type VietsubSubtitleEditorProps = {
  workspace?: VietsubSubtitleWorkspace | null;
  page?: VietsubSubtitlePage | null;
  busy: boolean;
  notice?: string | null;
  sourceLanguageCode: string;
  activeCueId?: string | null;
  getPlayheadMilliseconds: () => number;
  selectedCueId?: string | null;
  onImportSrt: (languageCode: string) => void;
  onActivateTrack: (trackId: string) => void;
  onLoadPage: (query: VietsubSubtitlePageQuery) => void;
  onUpdateCue: (cue: Pick<
    VietsubSubtitleCue,
    'cueId' | 'originalText' | 'translatedText' | 'speaker'
  >) => Promise<boolean>;
  onSplitCue: (cueId: string, positionMilliseconds: number) => void;
  onAlignCue: (cueId: string, positionMilliseconds: number) => void;
  onDuplicateCue: (cueId: string) => void;
  onDeleteCue: (cueId: string) => void;
  onExportSrt: (mode: 'ORIGINAL' | 'TRANSLATED') => void;
  onSelectCue: (cueId: string, positionMilliseconds: number) => void;
  onSaveStateChange: (state: VietsubSaveState) => void;
};

export type VietsubSubtitleEditorHandle = {
  flushPendingEdits: () => Promise<boolean>;
};

const VietsubSubtitleEditorComponent = forwardRef<VietsubSubtitleEditorHandle, VietsubSubtitleEditorProps>(function VietsubSubtitleEditor({
  workspace,
  page,
  busy,
  notice,
  sourceLanguageCode,
  activeCueId,
  getPlayheadMilliseconds,
  selectedCueId,
  onImportSrt,
  onActivateTrack,
  onLoadPage,
  onUpdateCue,
  onSplitCue,
  onAlignCue,
  onDuplicateCue,
  onDeleteCue,
  onExportSrt,
  onSelectCue,
  onSaveStateChange
}: VietsubSubtitleEditorProps, ref) {
  const [languageCode, setLanguageCode] = useState(
    sourceLanguageCode === 'auto' ? 'en' : sourceLanguageCode
  );
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState<VietsubSubtitleStatus>('ALL');
  const [speaker, setSpeaker] = useState('');
  const searchTrackRef = useRef<string | null>(null);
  const cueFlushersRef = useRef(new Map<string, () => Promise<boolean>>());
  const cueSaveStatesRef = useRef(new Map<string, VietsubSaveState>());
  const activeTrackId = workspace?.activeTrackId ?? null;

  const publishSaveState = useCallback(() => {
    const states = [...cueSaveStatesRef.current.values()];
    const next = states.includes('error')
      ? 'error'
      : states.includes('saving')
        ? 'saving'
        : states.includes('dirty')
          ? 'dirty'
          : 'saved';
    onSaveStateChange(next);
  }, [onSaveStateChange]);

  const reportCueSaveState = useCallback((cueId: string, state: VietsubSaveState) => {
    if (state === 'saved') cueSaveStatesRef.current.delete(cueId);
    else cueSaveStatesRef.current.set(cueId, state);
    publishSaveState();
  }, [publishSaveState]);

  const registerCueFlusher = useCallback((cueId: string, flush: () => Promise<boolean>) => {
    cueFlushersRef.current.set(cueId, flush);
    return () => {
      if (cueFlushersRef.current.get(cueId) === flush) cueFlushersRef.current.delete(cueId);
      cueSaveStatesRef.current.delete(cueId);
    };
  }, []);

  useImperativeHandle(ref, () => ({
    flushPendingEdits: async () => {
      for (const flush of [...cueFlushersRef.current.values()]) {
        if (!await flush()) return false;
      }
      return true;
    }
  }), []);

  const load = (
    offset: number,
    overrides?: Partial<Pick<VietsubSubtitlePageQuery, 'search' | 'status' | 'speaker'>>
  ) => {
    if (!activeTrackId) return;
    onLoadPage({
      trackId: activeTrackId,
      offset,
      pageSize: page?.pageSize ?? 50,
      search: overrides?.search ?? search,
      status: overrides?.status ?? status,
      speaker: overrides?.speaker ?? speaker
    });
  };

  useEffect(() => {
    if (!activeTrackId) {
      searchTrackRef.current = null;
      return;
    }
    if (searchTrackRef.current !== activeTrackId) {
      searchTrackRef.current = activeTrackId;
      return;
    }
    const timer = window.setTimeout(() => load(0, { search }), 350);
    return () => window.clearTimeout(timer);
    // Track and filter changes intentionally create a fresh paged query.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeTrackId, search]);

  const changeStatus = (value: VietsubSubtitleStatus) => {
    setStatus(value);
    load(0, { status: value });
  };
  const changeSpeaker = (value: string) => {
    setSpeaker(value);
    load(0, { speaker: value });
  };

  return (
    <section className="card vietsub-subtitle-editor">
      <div className="vietsub-subtitle-toolbar">
        <div>
          <span className="vietsub-eyebrow">PHỤ ĐỀ & BẢN DỊCH</span>
          <h3>Biên tập theo từng cue</h3>
          <p>SRT được lưu trong SQLite riêng; danh sách tải theo trang để giữ giao diện mượt với video dài.</p>
        </div>
        <div className="vietsub-subtitle-file-actions">
          <select
            value={languageCode}
            disabled={busy}
            aria-label="Ngôn ngữ SRT nguồn"
            onChange={(event) => setLanguageCode(event.target.value)}
          >
            <option value="en">English</option>
            <option value="zh">中文</option>
            <option value="vi">Tiếng Việt</option>
            <option value="und">Không xác định</option>
          </select>
          <button type="button" disabled={busy} onClick={() => onImportSrt(languageCode)}>
            <Upload size={15} /> Nhập SRT
          </button>
          <button type="button" disabled={busy || !activeTrackId} onClick={() => onExportSrt('ORIGINAL')}>
            <Download size={15} /> SRT gốc
          </button>
          <button type="button" disabled={busy || !activeTrackId} onClick={() => onExportSrt('TRANSLATED')}>
            <Download size={15} /> SRT tiếng Việt
          </button>
        </div>
      </div>

      {notice && <div className="vietsub-subtitle-notice">{notice}</div>}

      {!workspace || workspace.tracks.length === 0 ? (
        <div className="vietsub-subtitle-empty">
          <FileText size={32} />
          <strong>Chưa có track phụ đề</strong>
          <p>Nhập một tệp SRT UTF-8 để bắt đầu chỉnh nội dung và bản dịch.</p>
        </div>
      ) : (
        <>
          <div className="vietsub-subtitle-trackbar">
            <label>
              <span>Track đang chỉnh</span>
              <select
                value={activeTrackId ?? ''}
                disabled={busy}
                onChange={(event) => onActivateTrack(event.target.value)}
              >
                {workspace.tracks.map((track) => (
                  <option value={track.trackId} key={track.trackId}>
                    {track.displayName} · {track.languageCode.toUpperCase()} · {track.cueCount} cue
                  </option>
                ))}
              </select>
            </label>
            <label className="vietsub-subtitle-search">
              <Search size={15} />
              <input
                value={search}
                maxLength={200}
                placeholder="Tìm nội dung hoặc người nói"
                onChange={(event) => setSearch(event.target.value)}
              />
            </label>
            <select value={status} onChange={(event) => changeStatus(event.target.value as VietsubSubtitleStatus)}>
              <option value="ALL">Tất cả trạng thái</option>
              <option value="PENDING">Chưa dịch</option>
              <option value="TRANSLATED">Đã có bản dịch</option>
              <option value="LOCKED">Đã khóa thủ công</option>
              <option value="WARNING">Có cảnh báo</option>
            </select>
            <select value={speaker} onChange={(event) => changeSpeaker(event.target.value)}>
              <option value="">Tất cả người nói</option>
              {(page?.speakers ?? []).map((value) => <option value={value} key={value}>{value}</option>)}
            </select>
          </div>

          <div className="vietsub-cue-list" aria-live="polite">
            {(page?.cues ?? []).map((cue) => (
              <VietsubCueRow
                key={`${cue.cueId}-${page?.trackRevision ?? 0}`}
                cue={cue}
                busy={busy}
                active={activeCueId === cue.cueId}
                selected={selectedCueId === cue.cueId}
                getPlayheadMilliseconds={getPlayheadMilliseconds}
                onUpdate={onUpdateCue}
                onSelect={onSelectCue}
                onSplit={onSplitCue}
                onAlign={onAlignCue}
                onDuplicate={onDuplicateCue}
                onDelete={onDeleteCue}
                onRegisterFlusher={registerCueFlusher}
                onSaveStateChange={(next) => reportCueSaveState(cue.cueId, next)}
              />
            ))}
            {page && page.cues.length === 0 && (
              <div className="vietsub-subtitle-empty compact">
                <Search size={25} /><strong>Không có cue phù hợp</strong><p>Hãy đổi bộ lọc hoặc từ khóa.</p>
              </div>
            )}
          </div>

          {page && (
            <div className="vietsub-subtitle-pagination">
              <span>
                {page.totalCount === 0 ? 0 : page.offset + 1}–{Math.min(page.offset + page.cues.length, page.totalCount)} / {page.totalCount} cue
              </span>
              <div>
                <button type="button" disabled={busy || page.offset <= 0} onClick={() => load(Math.max(0, page.offset - page.pageSize))}>
                  <ChevronLeft size={15} /> Trang trước
                </button>
                <button
                  type="button"
                  disabled={busy || page.offset + page.pageSize >= page.totalCount}
                  onClick={() => load(page.offset + page.pageSize)}
                >
                  Trang sau <ChevronRight size={15} />
                </button>
              </div>
            </div>
          )}
        </>
      )}
    </section>
  );
});

export const VietsubSubtitleEditor = memo(VietsubSubtitleEditorComponent);

type VietsubCueRowProps = {
  cue: VietsubSubtitleCue;
  busy: boolean;
  active: boolean;
  selected: boolean;
  getPlayheadMilliseconds: () => number;
  onUpdate: VietsubSubtitleEditorProps['onUpdateCue'];
  onSelect: VietsubSubtitleEditorProps['onSelectCue'];
  onSplit: VietsubSubtitleEditorProps['onSplitCue'];
  onAlign: VietsubSubtitleEditorProps['onAlignCue'];
  onDuplicate: VietsubSubtitleEditorProps['onDuplicateCue'];
  onDelete: VietsubSubtitleEditorProps['onDeleteCue'];
  onRegisterFlusher: (cueId: string, flush: () => Promise<boolean>) => () => void;
  onSaveStateChange: (state: VietsubSaveState) => void;
};

function VietsubCueRow({
  cue,
  busy,
  active,
  selected,
  getPlayheadMilliseconds,
  onUpdate,
  onSelect,
  onSplit,
  onAlign,
  onDuplicate,
  onDelete,
  onRegisterFlusher,
  onSaveStateChange
}: VietsubCueRowProps) {
  const [draft, setDraft] = useState({
    originalText: cue.originalText,
    translatedText: cue.translatedText,
    speaker: cue.speaker
  });
  const dirty = useRef(false);
  const pendingFlush = useRef<Promise<boolean> | null>(null);

  useEffect(() => {
    setDraft({
      originalText: cue.originalText,
      translatedText: cue.translatedText,
      speaker: cue.speaker
    });
    dirty.current = false;
  }, [cue.cueId, cue.originalText, cue.translatedText, cue.speaker]);

  const flush = useCallback((): Promise<boolean> => {
    if (pendingFlush.current) return pendingFlush.current;
    if (!dirty.current) return Promise.resolve(true);
    if (busy) return Promise.resolve(false);
    const originalText = draft.originalText.trim();
    const speaker = draft.speaker.trim();
    if (!originalText || !speaker) {
      onSaveStateChange('error');
      return Promise.resolve(false);
    }
    onSaveStateChange('saving');
    const operation = onUpdate({
        cueId: cue.cueId,
        originalText,
        translatedText: draft.translatedText.trim(),
        speaker
      })
      .then((saved) => {
        if (saved) {
          dirty.current = false;
          onSaveStateChange('saved');
          return true;
        }
        onSaveStateChange('error');
        return false;
      })
      .finally(() => {
        pendingFlush.current = null;
      });
    pendingFlush.current = operation;
    return operation;
  }, [busy, cue.cueId, draft, onSaveStateChange, onUpdate]);

  useEffect(
    () => onRegisterFlusher(cue.cueId, flush),
    [cue.cueId, flush, onRegisterFlusher]
  );

  useEffect(() => {
    if (!dirty.current || busy) return;
    const timer = window.setTimeout(() => {
      void flush();
    }, 700);
    return () => window.clearTimeout(timer);
  }, [busy, draft, flush]);

  const updateDraft = (patch: Partial<typeof draft>) => {
    dirty.current = true;
    onSaveStateChange('dirty');
    setDraft((current) => ({ ...current, ...patch }));
  };
  return (
    <article className={`vietsub-cue-row ${active ? 'active' : ''} ${selected ? 'selected' : ''}`}>
      <button
        className="vietsub-cue-time"
        type="button"
        aria-pressed={selected}
        onClick={() => onSelect(cue.cueId, cue.startMilliseconds)}
      >
        <strong>#{cue.cueIndex + 1}</strong>
        <span>{formatCueTime(cue.startMilliseconds)}</span>
        <small>→ {formatCueTime(cue.endMilliseconds)}</small>
      </button>
      <div className="vietsub-cue-fields">
        <div className="vietsub-cue-meta">
          <input
            value={draft.speaker}
            maxLength={80}
            disabled={busy}
            aria-label={`Người nói cue ${cue.cueIndex + 1}`}
            onChange={(event) => updateDraft({ speaker: event.target.value })}
            onBlur={() => { void flush(); }}
          />
          <span className={cue.translatedText ? 'translated' : 'pending'}>
            {cue.translatedText ? 'Đã dịch' : 'Chưa dịch'}
          </span>
          {(cue.originalLocked || cue.translationLocked) && <span><LockKeyhole size={12} /> Đã khóa</span>}
          {cue.warnings.length > 0 && <span className="warning">{cue.warnings.length} cảnh báo</span>}
        </div>
        <label>
          <span>Nội dung gốc</span>
          <textarea
            value={draft.originalText}
            maxLength={10_000}
            disabled={busy}
            rows={2}
            onChange={(event) => updateDraft({ originalText: event.target.value })}
            onBlur={() => { void flush(); }}
          />
        </label>
        <label>
          <span>Tiếng Việt</span>
          <textarea
            value={draft.translatedText}
            maxLength={10_000}
            disabled={busy}
            rows={2}
            placeholder="Nhập bản dịch tiếng Việt…"
            onChange={(event) => updateDraft({ translatedText: event.target.value })}
            onBlur={() => { void flush(); }}
          />
        </label>
      </div>
      <div className="vietsub-cue-actions">
        <button
          type="button"
          disabled={busy}
          title="Tách tại playhead"
          onClick={() => {
            const playhead = getPlayheadMilliseconds();
            if (playhead > cue.startMilliseconds + 100 && playhead < cue.endMilliseconds - 100) {
              onSplit(cue.cueId, playhead);
            }
          }}
        >
          <Scissors size={15} />
        </button>
        <button
          type="button"
          disabled={busy}
          title="Căn bắt đầu vào playhead"
          onClick={() => {
            const playhead = getPlayheadMilliseconds();
            if (playhead < cue.endMilliseconds - 100) onAlign(cue.cueId, playhead);
          }}
        >
          <AlignStartHorizontal size={15} />
        </button>
        <button type="button" disabled={busy} title="Nhân bản cue" onClick={() => onDuplicate(cue.cueId)}>
          <CopyPlus size={15} />
        </button>
        <button
          type="button"
          disabled={busy}
          title="Xóa cue"
          onClick={() => {
            if (window.confirm(`Xóa cue #${cue.cueIndex + 1}?`)) onDelete(cue.cueId);
          }}
        >
          <Trash2 size={15} />
        </button>
      </div>
    </article>
  );
}

function formatCueTime(milliseconds: number): string {
  const totalSeconds = Math.max(0, Math.floor(milliseconds / 1000));
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;
  const millis = Math.max(0, Math.floor(milliseconds % 1000));
  return `${hours > 0 ? `${hours}:` : ''}${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}.${String(millis).padStart(3, '0')}`;
}
