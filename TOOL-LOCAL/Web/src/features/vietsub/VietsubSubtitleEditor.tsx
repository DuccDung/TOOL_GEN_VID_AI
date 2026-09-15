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
  CircleAlert,
  CircleCheck,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  CopyPlus,
  Download,
  Ellipsis,
  FileText,
  ListFilter,
  LockKeyhole,
  LoaderCircle,
  Mic,
  MicOff,
  Palette,
  Scissors,
  Search,
  Trash2,
  Upload,
  Video,
  X
} from 'lucide-react';
import type {
  VietsubSubtitleCue,
  VietsubSubtitlePage,
  VietsubSubtitlePageQuery,
  VietsubSaveState,
  VietsubSubtitleStatus,
  VietsubSubtitleTrackSummary,
  VietsubSubtitleWorkspace
} from './types';
import { useCueListFollow } from './useCueListFollow';
import { VietsubNotice } from './VietsubNotice';

type VietsubSubtitleEditorProps = {
  onUpdateCueVoice?: (cueIds: string[], enabled: boolean, trackId: string, revision: number) => Promise<boolean>;
  voiceSelectionBusy?: boolean;
  workspace?: VietsubSubtitleWorkspace | null;
  page?: VietsubSubtitlePage | null;
  busy: boolean;
  notice?: string | null;
  noticeId?: number;
  playing?: boolean;
  navigationVersion?: number;
  sourceLanguageCode: string;
  activeCueId?: string | null;
  getPlayheadMilliseconds: () => number;
  selectedCueId?: string | null;
  selectedCueIndex?: number | null;
  timelineEditingCueId?: string | null;
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
  canExportVideo: boolean;
  onExportVideo: () => Promise<boolean>;
  onExportStateChange?: (exporting: boolean) => void;
  canDesignSubtitle: boolean;
  onOpenSubtitleDesigner: () => void;
  onSelectCue: (cueId: string, positionMilliseconds: number, cueIndex: number) => void;
  onSaveStateChange: (state: VietsubSaveState) => void;
};

export type VietsubSubtitleEditorHandle = {
  flushPendingEdits: () => Promise<boolean>;
  exportVideo: () => Promise<void>;
};

const VietsubSubtitleEditorComponent = forwardRef<VietsubSubtitleEditorHandle, VietsubSubtitleEditorProps>(function VietsubSubtitleEditor({
  onUpdateCueVoice,
  voiceSelectionBusy,
  workspace,
  page,
  busy,
  notice,
  noticeId = 0,
  playing = false,
  navigationVersion = 0,
  sourceLanguageCode,
  activeCueId,
  getPlayheadMilliseconds,
  selectedCueId,
  selectedCueIndex,
  timelineEditingCueId,
  onImportSrt,
  onActivateTrack,
  onLoadPage,
  onUpdateCue,
  onSplitCue,
  onAlignCue,
  onDuplicateCue,
  onDeleteCue,
  onExportSrt,
  canExportVideo,
  onExportVideo,
  onExportStateChange,
  canDesignSubtitle,
  onOpenSubtitleDesigner,
  onSelectCue,
  onSaveStateChange
}: VietsubSubtitleEditorProps, ref) {
  const [languageCode, setLanguageCode] = useState(
    sourceLanguageCode === 'auto' ? 'en' : sourceLanguageCode
  );
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState<VietsubSubtitleStatus>('ALL');
  const [speaker, setSpeaker] = useState('');
  const [filtersOpen, setFiltersOpen] = useState(false);
  const [saveState, setLocalSaveState] = useState<VietsubSaveState>('saved');
  const [exporting, setExporting] = useState(false);
  const [exportNotice, setExportNotice] = useState<{ text: string; id: number } | null>(null);
  const exportNoticeSequence = useRef(0);
  const exportingRef = useRef(false);
  useEffect(() => () => { exportingRef.current = false; }, []);
  const searchTrackRef = useRef<string | null>(null);
  const fileMenuRef = useRef<HTMLDetailsElement | null>(null);
  const cueListRef = useRef<HTMLDivElement | null>(null);
  const navigationRequestRef = useRef<string | null>(null);
  const cueFlushersRef = useRef(new Map<string, () => Promise<boolean>>());
  const cueSaveStatesRef = useRef(new Map<string, VietsubSaveState>());
  const activeTrackId = workspace?.activeTrackId ?? null;
  const pageRef = useRef(page);
  pageRef.current = page;
  const workspaceRef = useRef(workspace);
  workspaceRef.current = workspace;
  const [voiceSaving, setVoiceSaving] = useState(false);
  const voiceSavingRef = useRef(false);
  const [voiceSelectionNotice, setVoiceSelectionNotice] = useState<{ text: string; id: number } | null>(null);
  const voiceNoticeSequence = useRef(0);
  useEffect(() => { setVoiceSelectionNotice(null); },
    [activeTrackId, page?.offset, page?.search, page?.status, page?.speaker]);

  const publishSaveState = useCallback(() => {
    const states = [...cueSaveStatesRef.current.values()];
    const next = states.includes('error')
      ? 'error'
      : states.includes('saving')
        ? 'saving'
        : states.includes('dirty')
          ? 'dirty'
          : 'saved';
    setLocalSaveState(next);
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

  const flushPendingEdits = useCallback(async () => {
    for (const flush of [...cueFlushersRef.current.values()]) {
      if (!await flush()) return false;
    }
    return true;
  }, []);

  const exportVideo = useCallback(async () => {
    if (busy || voiceSelectionBusy || voiceSavingRef.current || exportingRef.current || !canExportVideo || !activeTrackId) return;
    const trackId = activeTrackId;
    exportingRef.current = true;
    setExporting(true);
    onExportStateChange?.(true);
    setExportNotice(null);
    try {
      if (!await flushPendingEdits() || !exportingRef.current || workspaceRef.current?.activeTrackId !== trackId) return;
      await onExportVideo();
    } catch {
      setExportNotice({ text: 'Chưa thể xuất video. Hãy kiểm tra các thay đổi đã lưu và thử lại.', id: ++exportNoticeSequence.current });
    } finally {
      exportingRef.current = false;
      setExporting(false);
      onExportStateChange?.(false);
    }
  }, [busy, voiceSelectionBusy, canExportVideo, activeTrackId, flushPendingEdits, onExportVideo, onExportStateChange]);

  useImperativeHandle(ref, () => ({ flushPendingEdits, exportVideo }), [flushPendingEdits, exportVideo]);

  const changeCueVoice = useCallback(async (cueId: string, enabled: boolean) => {
    if (!onUpdateCueVoice || voiceSavingRef.current || voiceSelectionBusy || busy) return;
    const trackId = pageRef.current?.trackId;
    voiceSavingRef.current = true;
    setVoiceSaving(true);
    setVoiceSelectionNotice(null);
    const showNotice = (text: string) => setVoiceSelectionNotice({ text, id: ++voiceNoticeSequence.current });
    try {
      if (!await flushPendingEdits()) return;
      const currentPage = pageRef.current;
      const currentTrack = workspaceRef.current?.tracks.find(track => track.trackId === trackId);
      if (!currentPage || !currentTrack || workspaceRef.current?.activeTrackId !== trackId
        || currentPage.trackId !== trackId || !currentPage.cues.some(cue => cue.cueId === cueId)) return;
      // The host publishes the updated track summary before completing a save; the paged query may arrive later.
      const revision = Math.max(currentPage.trackRevision, currentTrack.revision);
      const saved = await onUpdateCueVoice([cueId], enabled, currentPage.trackId, revision);
      if (workspaceRef.current?.activeTrackId !== trackId) return;
      showNotice(saved ? 'Đã lưu lựa chọn tạo giọng.' : 'Chưa lưu được lựa chọn. Hãy kiểm tra thông báo và thử lại.');
    } catch {
      showNotice('Chưa lưu được lựa chọn. Hãy thử lại.');
    } finally { voiceSavingRef.current = false; setVoiceSaving(false); }
  }, [busy, voiceSelectionBusy, onUpdateCueVoice, flushPendingEdits]);

  const load = (
    offset: number,
    overrides?: Partial<Pick<VietsubSubtitlePageQuery, 'search' | 'status' | 'speaker'>>
  ) => {
    if (!activeTrackId) return;
    void flushPendingEdits().then((saved) => {
      if (!saved) return;
      cueListRef.current?.scrollTo({ top: 0 });
      onLoadPage({
        trackId: activeTrackId,
        offset,
        pageSize: page?.pageSize ?? 50,
        search: overrides?.search ?? search,
        status: overrides?.status ?? status,
        speaker: overrides?.speaker ?? speaker
      });
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

  const clearFilters = () => {
    setStatus('ALL');
    setSpeaker('');
    load(0, { status: 'ALL', speaker: '' });
  };

  const changeTrack = (trackId: string) => {
    void flushPendingEdits().then((saved) => {
      if (!saved) return;
      setSearch('');
      setStatus('ALL');
      setSpeaker('');
      setFiltersOpen(false);
      onActivateTrack(trackId);
    });
  };

  useEffect(() => {
    if (!selectedCueId || selectedCueIndex === null || selectedCueIndex === undefined || !activeTrackId) {
      navigationRequestRef.current = null;
      return;
    }
    if (timelineEditingCueId === selectedCueId) {
      navigationRequestRef.current = null;
      return;
    }
    if (page?.trackId === activeTrackId && page.cues.some((cue) => cue.cueId === selectedCueId)) {
      navigationRequestRef.current = null;
      return;
    }
    const requestKey = `${activeTrackId}:${selectedCueId}:${selectedCueIndex}`;
    if (navigationRequestRef.current === requestKey) return;
    navigationRequestRef.current = requestKey;
    const pageSize = page?.pageSize ?? 50;
    setSearch('');
    setStatus('ALL');
    setSpeaker('');
    setFiltersOpen(false);
    void flushPendingEdits().then((saved) => {
      if (!saved) return;
      onLoadPage({
        trackId: activeTrackId,
        offset: Math.floor(Math.max(0, selectedCueIndex) / pageSize) * pageSize,
        pageSize,
        search: '',
        status: 'ALL',
        speaker: ''
      });
    });
  }, [activeTrackId, flushPendingEdits, onLoadPage, page, selectedCueId, selectedCueIndex, timelineEditingCueId]);

  const activeTrack = workspace?.tracks.find((track) => track.trackId === activeTrackId) ?? null;
  const pendingCount = Math.max(0, (activeTrack?.cueCount ?? 0) - (activeTrack?.translatedCueCount ?? 0));
  const activeFilterCount = Number(status !== 'ALL') + Number(speaker.length > 0);
  const hasSelectedCueOnPage = Boolean(selectedCueId && page?.cues.some((cue) => cue.cueId === selectedCueId));
  const targetCueId = (hasSelectedCueOnPage ? selectedCueId : activeCueId) ?? null;
  const heldCueId = useCueListFollow(cueListRef, `${activeTrackId}:${page?.offset}:${page?.search}:${page?.status}:${page?.speaker}`,
    targetCueId, `${selectedCueId ?? ''}:${navigationVersion}`, playing);
  const expandedCueId = heldCueId === undefined ? targetCueId : heldCueId;
  const closeFileMenu = () => fileMenuRef.current?.removeAttribute('open');

  return (
    <section className="card vietsub-subtitle-editor">
      <div className="vietsub-subtitle-toolbar">
        <div className="vietsub-subtitle-heading">
          <h3>Biên tập phụ đề</h3>
        </div>
        <div className="vietsub-subtitle-toolbar-actions">
          <button
            type="button"
            className="vietsub-subtitle-designer-trigger"
            disabled={busy || !canDesignSubtitle}
            title={canDesignSubtitle ? 'Thiết kế cách hiển thị phụ đề' : 'Hãy thêm video trước khi thiết kế phụ đề'}
            onClick={onOpenSubtitleDesigner}
          >
            <Palette size={15} /><span>Thiết kế phụ đề</span>
          </button>
          <details
            className="vietsub-subtitle-file-menu"
            ref={fileMenuRef}
            onBlur={(event) => {
              if (!event.currentTarget.contains(event.relatedTarget)) closeFileMenu();
            }}
          >
            <summary aria-label="Mở menu tệp phụ đề">
              <FileText size={15} /><span>Tệp phụ đề</span><ChevronDown size={13} />
            </summary>
            <div className="vietsub-subtitle-file-popover">
              <label>
                <span>Ngôn ngữ file nhập</span>
                <select
                  value={languageCode}
                  disabled={busy}
                  aria-label="Ngôn ngữ file phụ đề nhập"
                  onChange={(event) => setLanguageCode(event.target.value)}
                >
                  <option value="en">Tiếng Anh</option>
                  <option value="zh">Tiếng Trung</option>
                  <option value="vi">Tiếng Việt</option>
                  <option value="und">Không xác định</option>
                </select>
              </label>
              <button type="button" disabled={busy} onClick={() => { closeFileMenu(); onImportSrt(languageCode); }}>
                <Upload size={15} /><span><strong>Nhập file SRT</strong><small>Tạo nguồn phụ đề mới</small></span>
              </button>
              <div className="vietsub-subtitle-file-divider" />
              <button type="button" disabled={busy || !activeTrackId} onClick={() => { closeFileMenu(); onExportSrt('ORIGINAL'); }}>
                <Download size={15} /><span><strong>Xuất phụ đề gốc</strong><small>Lưu nội dung nhận dạng</small></span>
              </button>
              <button type="button" disabled={busy || !activeTrackId} onClick={() => { closeFileMenu(); onExportSrt('TRANSLATED'); }}>
                <Download size={15} /><span><strong>Xuất phụ đề tiếng Việt</strong><small>Lưu bản dịch hiện tại</small></span>
              </button>
            </div>
          </details>
          <button
            type="button"
            className="vietsub-subtitle-export-trigger"
            disabled={busy || voiceSelectionBusy || voiceSaving || exporting || !canExportVideo || !activeTrackId}
            aria-busy={exporting}
            title={canExportVideo && activeTrackId
              ? 'Xuất MP4 với phụ đề và cấu hình âm thanh hiện tại'
              : 'Hãy thêm video và chọn phụ đề trước khi xuất'}
            onClick={() => { closeFileMenu(); void exportVideo(); }}
          >
            {exporting ? <LoaderCircle size={15} className="spin" /> : <Video size={15} />}
            <span>{exporting ? 'Đang xuất…' : 'Xuất video'}</span>
          </button>
        </div>
        {activeTrack && (
          <div className="vietsub-subtitle-summary" aria-label="Tóm tắt phụ đề">
            <span>{activeTrack.cueCount} câu</span>
            <span className={pendingCount > 0 ? 'is-pending' : 'is-complete'}>
              {pendingCount > 0 ? `${pendingCount} chưa dịch` : 'Đã dịch xong'}
            </span>
            {activeTrack.warningCueCount > 0 && <span className="is-warning">{activeTrack.warningCueCount} cảnh báo</span>}
          </div>
        )}
      </div>

      {notice && <VietsubNotice eventId={noticeId} className="vietsub-subtitle-notice">{notice}</VietsubNotice>}
      {exportNotice && <VietsubNotice eventId={exportNotice.id} className="vietsub-subtitle-notice" role="alert">{exportNotice.text}</VietsubNotice>}

      {!workspace || workspace.tracks.length === 0 ? (
        <div className="vietsub-subtitle-empty">
          <FileText size={32} />
          <strong>Chưa có track phụ đề</strong>
          <p>Nhập một tệp SRT UTF-8 để bắt đầu chỉnh nội dung và bản dịch.</p>
        </div>
      ) : (
        <>
          <div className="vietsub-subtitle-controls">
            <label className="vietsub-subtitle-source">
              <span>Nguồn phụ đề</span>
              <select
                value={activeTrackId ?? ''}
                disabled={busy}
                onChange={(event) => changeTrack(event.target.value)}
              >
                {workspace.tracks.map((track) => (
                  <option value={track.trackId} key={track.trackId}>
                    {formatTrackLabel(track)}
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
            <button
              className={activeFilterCount > 0 ? 'vietsub-subtitle-filter-toggle is-active' : 'vietsub-subtitle-filter-toggle'}
              type="button"
              aria-expanded={filtersOpen}
              onClick={() => setFiltersOpen((current) => !current)}
            >
              <ListFilter size={15} /><span>Bộ lọc</span>
              {activeFilterCount > 0 && <b>{activeFilterCount}</b>}
            </button>
          </div>

          {filtersOpen && (
            <div className="vietsub-subtitle-filters">
              <label><span>Trạng thái</span>
                <select value={status} onChange={(event) => changeStatus(event.target.value as VietsubSubtitleStatus)}>
                  <option value="ALL">Tất cả</option>
                  <option value="PENDING">Chưa dịch</option>
                  <option value="TRANSLATED">Đã có bản dịch</option>
                  <option value="LOCKED">Đã khóa thủ công</option>
                  <option value="WARNING">Có cảnh báo</option>
                </select>
              </label>
              <label><span>Người nói</span>
                <select value={speaker} onChange={(event) => changeSpeaker(event.target.value)}>
                  <option value="">Tất cả</option>
                  {(page?.speakers ?? []).map((value) => <option value={value} key={value}>{formatSpeaker(value)}</option>)}
                </select>
              </label>
              <button type="button" disabled={activeFilterCount === 0} onClick={clearFilters}><X size={13} /> Xóa lọc</button>
            </div>
          )}

          <div className={`vietsub-subtitle-save-state is-${saveState}`} role="status" aria-live="polite">
            {saveState === 'saving' ? <LoaderCircle size={13} /> : saveState === 'error' ? <CircleAlert size={13} /> : <CircleCheck size={13} />}
            <span>{formatSaveState(saveState)}</span>
          </div>

          {voiceSelectionNotice && <VietsubNotice eventId={voiceSelectionNotice.id} className="vietsub-subtitle-notice">
            {voiceSelectionNotice.text}
          </VietsubNotice>}
          <div className="vietsub-cue-list" ref={cueListRef} tabIndex={0} aria-label="Danh sách câu phụ đề">
            {(page?.cues ?? []).map((cue) => (
              <VietsubCueRow
                key={`${activeTrackId}:${cue.cueId}`}
                cue={cue}
                voiceSelectionDisabled={busy || voiceSelectionBusy || voiceSaving}
                onSetVoiceEnabled={onUpdateCueVoice ? changeCueVoice : undefined}
                busy={busy}
                active={activeCueId === cue.cueId}
                selected={selectedCueId === cue.cueId}
                expanded={expandedCueId === cue.cueId}
                getPlayheadMilliseconds={getPlayheadMilliseconds}
                onUpdate={onUpdateCue}
                onSelect={onSelectCue}
                onSplit={onSplitCue}
                onAlign={onAlignCue}
                onDuplicate={onDuplicateCue}
                onDelete={onDeleteCue}
                onRegisterFlusher={registerCueFlusher}
                onSaveStateChange={reportCueSaveState}
              />
            ))}
            {page && page.cues.length === 0 && (
              <div className="vietsub-subtitle-empty compact">
                <Search size={25} /><strong>Không có câu phù hợp</strong><p>Hãy đổi bộ lọc hoặc từ khóa.</p>
              </div>
            )}
          </div>

          {page && page.totalCount > page.pageSize && (
            <div className="vietsub-subtitle-pagination">
              <span>
                Câu {page.totalCount === 0 ? 0 : page.offset + 1}–{Math.min(page.offset + page.cues.length, page.totalCount)} / {page.totalCount}
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
  voiceSelectionDisabled?: boolean;
  onSetVoiceEnabled?: (cueId: string, enabled: boolean) => void;
  cue: VietsubSubtitleCue;
  busy: boolean;
  active: boolean;
  selected: boolean;
  expanded: boolean;
  getPlayheadMilliseconds: () => number;
  onUpdate: VietsubSubtitleEditorProps['onUpdateCue'];
  onSelect: VietsubSubtitleEditorProps['onSelectCue'];
  onSplit: VietsubSubtitleEditorProps['onSplitCue'];
  onAlign: VietsubSubtitleEditorProps['onAlignCue'];
  onDuplicate: VietsubSubtitleEditorProps['onDuplicateCue'];
  onDelete: VietsubSubtitleEditorProps['onDeleteCue'];
  onRegisterFlusher: (cueId: string, flush: () => Promise<boolean>) => () => void;
  onSaveStateChange: (cueId: string, state: VietsubSaveState) => void;
};

const VietsubCueRow = memo(function VietsubCueRow({
  voiceSelectionDisabled,
  onSetVoiceEnabled,
  cue,
  busy,
  active,
  selected,
  expanded,
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
  const draftVersion = useRef(0);
  const pendingFlush = useRef<Promise<boolean> | null>(null);
  const latestFlushRef = useRef<() => Promise<boolean>>(() => Promise.resolve(true));
  const actionsMenuRef = useRef<HTMLDetailsElement | null>(null);
  const [rowSaveState, setRowSaveState] = useState<VietsubSaveState>('saved');
  const [validationMessage, setValidationMessage] = useState<string | null>(null);
  const [actionNotice, setActionNoticeState] = useState({ text: null as string | null, id: 0 });
  const setActionNotice = (text: string | null) => setActionNoticeState(current => ({ text, id: current.id + 1 }));
  const [confirmingDelete, setConfirmingDelete] = useState(false);

  const reportRowSaveState = useCallback((state: VietsubSaveState) => {
    setRowSaveState(state);
    onSaveStateChange(cue.cueId, state);
  }, [cue.cueId, onSaveStateChange]);

  useEffect(() => {
    if (dirty.current || pendingFlush.current) return;
    setDraft({
      originalText: cue.originalText,
      translatedText: cue.translatedText,
      speaker: cue.speaker
    });
    dirty.current = false;
    setRowSaveState('saved');
    setValidationMessage(null);
    setActionNotice(null);
    setConfirmingDelete(false);
  }, [cue.cueId, cue.originalText, cue.translatedText, cue.speaker]);

  const flush = useCallback((): Promise<boolean> => {
    if (pendingFlush.current) return pendingFlush.current;
    if (!dirty.current) return Promise.resolve(true);
    if (busy) return Promise.resolve(false);
    const originalText = draft.originalText.trim();
    const speaker = draft.speaker.trim();
    if (!originalText) {
      setValidationMessage('Nội dung gốc không được để trống.');
      reportRowSaveState('error');
      return Promise.resolve(false);
    }
    if (!speaker) {
      setValidationMessage('Hãy nhập tên người nói trước khi lưu.');
      reportRowSaveState('error');
      return Promise.resolve(false);
    }
    setValidationMessage(null);
    reportRowSaveState('saving');
    const savingVersion = draftVersion.current;
    const operation = onUpdate({
        cueId: cue.cueId,
        originalText,
        translatedText: draft.translatedText.trim(),
        speaker
      })
      .then((saved) => {
        if (saved) {
          dirty.current = draftVersion.current !== savingVersion;
          setValidationMessage(null);
          reportRowSaveState(dirty.current ? 'dirty' : 'saved');
          return true;
        }
        setValidationMessage('Không thể lưu thay đổi. Hãy thử lại.');
        reportRowSaveState('error');
        return false;
      })
      .catch(() => {
        setValidationMessage('Không thể lưu thay đổi. Hãy thử lại.');
        reportRowSaveState('error');
        return false;
      })
      .finally(() => {
        pendingFlush.current = null;
      });
    pendingFlush.current = operation;
    return operation;
  }, [busy, cue.cueId, draft, onUpdate, reportRowSaveState]);

  latestFlushRef.current = flush;
  const flushLatestDraft = useCallback(async () => {
    do {
      if (!await latestFlushRef.current()) return false;
    } while (dirty.current);
    return true;
  }, []);

  useEffect(
    () => onRegisterFlusher(cue.cueId, flushLatestDraft),
    [cue.cueId, flushLatestDraft, onRegisterFlusher]
  );

  useEffect(() => {
    if (!dirty.current || busy || rowSaveState === 'error') return;
    const timer = window.setTimeout(() => {
      void flush();
    }, 700);
    return () => window.clearTimeout(timer);
  }, [busy, draft, flush, rowSaveState]);

  const updateDraft = (patch: Partial<typeof draft>) => {
    dirty.current = true;
    draftVersion.current++;
    setValidationMessage(null);
    reportRowSaveState('dirty');
    setDraft((current) => ({ ...current, ...patch }));
  };

  const closeActionsMenu = () => actionsMenuRef.current?.removeAttribute('open');
  const splitAtPlayhead = () => {
    const playhead = getPlayheadMilliseconds();
    if (playhead <= cue.startMilliseconds + 100 || playhead >= cue.endMilliseconds - 100) {
      setActionNotice('Hãy đưa vị trí phát vào bên trong câu rồi thử tách lại.');
      closeActionsMenu();
      return;
    }
    setActionNotice(null);
    closeActionsMenu();
    onSplit(cue.cueId, playhead);
  };
  const alignToPlayhead = () => {
    const playhead = getPlayheadMilliseconds();
    if (playhead >= cue.endMilliseconds - 100) {
      setActionNotice('Vị trí phát phải nằm trước điểm kết thúc của câu.');
      closeActionsMenu();
      return;
    }
    setActionNotice(null);
    closeActionsMenu();
    onAlign(cue.cueId, playhead);
  };

  return (
    <article
      data-cue-id={cue.cueId}
      className={`vietsub-cue-row ${active ? 'active' : ''} ${selected ? 'selected' : ''} ${expanded ? 'is-expanded' : 'is-collapsed'}`}
    >
      <button
        className="vietsub-cue-summary"
        type="button"
        aria-pressed={selected}
        aria-expanded={expanded}
        onClick={() => onSelect(cue.cueId, cue.startMilliseconds, cue.cueIndex)}
      >
        <span className="vietsub-cue-number">{cue.cueIndex + 1}</span>
        <span className="vietsub-cue-time-range">{formatCueTime(cue.startMilliseconds)}–{formatCueTime(cue.endMilliseconds)}</span>
        <span className="vietsub-cue-preview">{draft.translatedText.trim() || draft.originalText.trim() || 'Câu phụ đề trống'}</span>
        <span className="vietsub-cue-summary-statuses">
          <span className={draft.translatedText.trim() ? 'vietsub-cue-status is-translated' : 'vietsub-cue-status is-pending'}>
            {draft.translatedText.trim() ? 'Đã dịch' : 'Chưa dịch'}
          </span>
          {onSetVoiceEnabled && <span className="vietsub-cue-status vietsub-cue-voice-status">
            {cue.voiceEnabled === false ? 'Không tạo giọng' : 'Tạo giọng'}
          </span>}
        </span>
        <ChevronDown className="vietsub-cue-expand-icon" size={15} />
      </button>

      {expanded && (
        <div className="vietsub-cue-editor">
          <div className="vietsub-cue-meta">
            <label className="vietsub-cue-speaker">
              <span>Người nói</span>
              <input
                value={draft.speaker}
                maxLength={80}
                readOnly={busy}
                placeholder="Ví dụ: Người nói 1"
                aria-label={`Người nói câu ${cue.cueIndex + 1}`}
                onChange={(event) => updateDraft({ speaker: event.target.value })}
                onBlur={() => { void flush(); }}
              />
            </label>
            <div className="vietsub-cue-badges">
              {(cue.originalLocked || cue.translationLocked) && <span><LockKeyhole size={11} /> Đã khóa</span>}
              {cue.warnings.length > 0 && <span className="warning">{cue.warnings.length} cảnh báo</span>}
            </div>
          </div>
          <div className="vietsub-cue-fields">
            <label>
              <span>Nội dung gốc</span>
              <textarea
                value={draft.originalText}
                maxLength={10_000}
                readOnly={busy}
                rows={2}
                onChange={(event) => updateDraft({ originalText: event.target.value })}
                onBlur={() => { void flush(); }}
              />
            </label>
            <label className="vietsub-cue-translation-field">
              <span>Tiếng Việt</span>
              <textarea
                value={draft.translatedText}
                maxLength={10_000}
                readOnly={busy}
                rows={3}
                placeholder="Nhập bản dịch tiếng Việt…"
                onChange={(event) => updateDraft({ translatedText: event.target.value })}
                onBlur={() => { void flush(); }}
              />
            </label>
          </div>

          {validationMessage && <div className="vietsub-cue-inline-message is-error" role="alert"><CircleAlert size={13} /> {validationMessage}</div>}
          {actionNotice.text && <VietsubNotice eventId={actionNotice.id} className="vietsub-cue-inline-message is-warning"
            icon={<CircleAlert size={13} />}>{actionNotice.text}</VietsubNotice>}

          <div className="vietsub-cue-footer">
            <span className={`vietsub-cue-save-state is-${rowSaveState}`}>
              {rowSaveState === 'saving' ? <LoaderCircle size={12} /> : rowSaveState === 'error' ? <CircleAlert size={12} /> : <CircleCheck size={12} />}
              {formatSaveState(rowSaveState)}
            </span>
            <details
              className="vietsub-cue-action-menu"
              ref={actionsMenuRef}
              onBlur={(event) => {
                if (!event.currentTarget.contains(event.relatedTarget)) closeActionsMenu();
              }}
            >
              <summary aria-label={`Mở thao tác cho câu ${cue.cueIndex + 1}`}><Ellipsis size={17} /> Thao tác</summary>
              <div>
                {onSetVoiceEnabled && <button type="button" disabled={voiceSelectionDisabled}
                  onClick={() => { closeActionsMenu(); onSetVoiceEnabled(cue.cueId, cue.voiceEnabled === false); }}>
                  {cue.voiceEnabled === false ? <Mic size={14} /> : <MicOff size={14} />}
                  <span><strong>{cue.voiceEnabled === false ? 'Bật tạo giọng' : 'Bỏ qua tạo giọng'}</strong></span>
                </button>}
                <button type="button" disabled={busy} onClick={splitAtPlayhead}><Scissors size={14} /><span><strong>Tách tại vị trí phát</strong><small>Chia câu thành hai đoạn</small></span></button>
                <button type="button" disabled={busy} onClick={alignToPlayhead}><AlignStartHorizontal size={14} /><span><strong>Căn điểm bắt đầu</strong><small>Dùng vị trí phát hiện tại</small></span></button>
                <button type="button" disabled={busy} onClick={() => { closeActionsMenu(); onDuplicate(cue.cueId); }}><CopyPlus size={14} /><span><strong>Nhân bản câu</strong><small>Tạo một bản sao liền kề</small></span></button>
                <button className="is-danger" type="button" disabled={busy} onClick={() => { closeActionsMenu(); setConfirmingDelete(true); }}><Trash2 size={14} /><span><strong>Xóa câu</strong><small>Không thể hoàn tác</small></span></button>
              </div>
            </details>
          </div>

          {confirmingDelete && (
            <div className="vietsub-cue-delete-confirm" role="alertdialog" aria-label={`Xác nhận xóa câu ${cue.cueIndex + 1}`}>
              <div><strong>Xóa câu phụ đề số {cue.cueIndex + 1}?</strong><span>Thao tác này không thể hoàn tác.</span></div>
              <button type="button" onClick={() => setConfirmingDelete(false)}>Hủy</button>
              <button className="is-danger" type="button" onClick={() => { setConfirmingDelete(false); onDelete(cue.cueId); }}>Xóa câu</button>
            </div>
          )}
        </div>
      )}
    </article>
  );
});

function formatSaveState(state: VietsubSaveState): string {
  if (state === 'dirty') return 'Chưa lưu';
  if (state === 'saving') return 'Đang lưu…';
  if (state === 'error') return 'Lưu thất bại';
  return 'Đã lưu';
}

function formatTrackLabel(track: VietsubSubtitleTrackSummary): string {
  const source = track.source === 'PADDLE_OCR_LOCAL'
    ? 'OCR'
    : track.source === 'IMPORTED_SRT'
      ? 'SRT'
      : 'Phụ đề';
  return `${source} ${formatLanguage(track.languageCode)} · ${track.cueCount} câu`;
}

function formatLanguage(languageCode: string): string {
  if (languageCode.toLowerCase().startsWith('zh')) return 'tiếng Trung';
  if (languageCode.toLowerCase().startsWith('en')) return 'tiếng Anh';
  if (languageCode.toLowerCase().startsWith('vi')) return 'tiếng Việt';
  return 'không xác định';
}

function formatSpeaker(speaker: string): string {
  const matched = /^speaker[_ -]?(\d+)$/i.exec(speaker.trim());
  return matched ? `Người nói ${matched[1]}` : speaker;
}

function formatCueTime(milliseconds: number): string {
  const totalSeconds = Math.max(0, Math.floor(milliseconds / 1000));
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;
  const millis = Math.max(0, Math.floor(milliseconds % 1000));
  return `${hours > 0 ? `${hours}:` : ''}${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}.${String(millis).padStart(3, '0')}`;
}
