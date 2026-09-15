import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { CircleAlert, LoaderCircle, RefreshCw, X } from 'lucide-react';
import type { VietsubSubtitleCue } from './types';

export type VietsubTimelineCueEditTarget = {
  trackId: string;
  cueId: string;
  cueIndex: number;
  startMilliseconds: number;
  endMilliseconds: number;
  anchor: HTMLElement;
};

export type VietsubTimelineCueEditSnapshot = {
  revision: number;
  originalText: string;
  translatedText: string;
  speaker: string;
};

type Props = {
  target: VietsubTimelineCueEditTarget;
  cue: VietsubSubtitleCue | null;
  pageRevision: number | null;
  activeTrackRevision: number | null;
  busy: boolean;
  onSave: (
    target: VietsubTimelineCueEditTarget,
    snapshot: VietsubTimelineCueEditSnapshot,
    translatedText: string
  ) => Promise<boolean>;
  onRetry: () => void;
  onClose: () => void;
  onDirtyChange?: (dirty: boolean) => void;
};

type Placement = { left: number; top: number; side: 'above' | 'below' };

export function VietsubTimelineCueEditPopover({
  target,
  cue,
  pageRevision,
  activeTrackRevision,
  busy,
  onSave,
  onRetry,
  onClose,
  onDirtyChange
}: Props) {
  const [snapshot, setSnapshot] = useState<VietsubTimelineCueEditSnapshot | null>(null);
  const [draft, setDraft] = useState('');
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [confirmDiscard, setConfirmDiscard] = useState<'close' | 'reload' | null>(null);
  const [loadTimedOut, setLoadTimedOut] = useState(false);
  const [placement, setPlacement] = useState<Placement | null>(null);
  const popoverRef = useRef<HTMLDivElement | null>(null);
  const textareaRef = useRef<HTMLTextAreaElement | null>(null);
  const focusedTextareaRef = useRef(false);
  const currentCue = cue && pageRevision !== null && pageRevision === activeTrackRevision ? cue : null;
  const dirty = snapshot !== null && draft !== snapshot.translatedText;
  const stale = snapshot !== null && (
    activeTrackRevision !== snapshot.revision ||
    (pageRevision !== null && pageRevision !== snapshot.revision)
  );
  const needsReload = snapshot !== null && (!currentCue || stale);

  useEffect(() => {
    onDirtyChange?.(dirty);
    return () => onDirtyChange?.(false);
  }, [dirty, onDirtyChange]);

  useEffect(() => {
    if (!currentCue || pageRevision === null || saving) return;
    if (snapshot && dirty) return;
    if (snapshot && snapshot.revision === pageRevision
      && snapshot.originalText === currentCue.originalText
      && snapshot.translatedText === currentCue.translatedText
      && snapshot.speaker === currentCue.speaker) return;
    setSnapshot({
      revision: pageRevision,
      originalText: currentCue.originalText,
      translatedText: currentCue.translatedText,
      speaker: currentCue.speaker
    });
    setDraft(currentCue.translatedText);
    setMessage(null);
  }, [currentCue, pageRevision, snapshot, dirty, saving]);

  useEffect(() => {
    if (snapshot || currentCue) {
      setLoadTimedOut(false);
      return;
    }
    const timeout = window.setTimeout(() => setLoadTimedOut(true), 8_000);
    return () => window.clearTimeout(timeout);
  }, [snapshot, currentCue, target.cueId]);

  useEffect(() => {
    if (!snapshot || focusedTextareaRef.current) return;
    focusedTextareaRef.current = true;
    textareaRef.current?.focus();
  }, [snapshot]);

  useEffect(() => {
    if (snapshot) return;
    popoverRef.current?.focus();
  }, [snapshot]);

  useEffect(() => () => {
    if (target.anchor.isConnected) target.anchor.focus();
  }, [target.anchor]);

  const updatePlacement = useCallback(() => {
    const popover = popoverRef.current;
    if (!popover) return;
    const anchor = target.anchor.getBoundingClientRect();
    const width = popover.offsetWidth;
    const height = popover.offsetHeight;
    const margin = 12;
    const gap = 10;
    const left = Math.max(margin, Math.min(
      window.innerWidth - width - margin,
      anchor.left + anchor.width / 2 - width / 2
    ));
    const fitsAbove = anchor.top - height - gap >= margin;
    const fitsBelow = anchor.bottom + height + gap <= window.innerHeight - margin;
    const side = fitsAbove || !fitsBelow ? 'above' : 'below';
    const desiredTop = side === 'above' ? anchor.top - height - gap : anchor.bottom + gap;
    const top = Math.max(margin, Math.min(window.innerHeight - height - margin, desiredTop));
    setPlacement((current) => current?.left === left && current.top === top && current.side === side
      ? current : { left, top, side });
  }, [target.anchor]);

  useLayoutEffect(() => {
    updatePlacement();
    const observer = new ResizeObserver(updatePlacement);
    if (popoverRef.current) observer.observe(popoverRef.current);
    if (target.anchor.isConnected) observer.observe(target.anchor);
    window.addEventListener('resize', updatePlacement);
    window.addEventListener('scroll', updatePlacement, true);
    return () => {
      observer.disconnect();
      window.removeEventListener('resize', updatePlacement);
      window.removeEventListener('scroll', updatePlacement, true);
    };
  }, [target.anchor, updatePlacement]);

  const requestClose = () => {
    if (saving) return;
    if (dirty) {
      setConfirmDiscard('close');
      return;
    }
    onClose();
  };

  const requestReload = () => {
    if (saving) return;
    if (dirty) {
      setConfirmDiscard('reload');
      return;
    }
    setSnapshot(null);
    setDraft('');
    setMessage(null);
    setLoadTimedOut(false);
    onRetry();
  };

  const confirmDiscardDraft = () => {
    if (confirmDiscard === 'close') {
      onClose();
    } else if (confirmDiscard === 'reload') {
      setSnapshot(null);
      setDraft('');
      setMessage(null);
      setLoadTimedOut(false);
      focusedTextareaRef.current = false;
      onRetry();
    }
    setConfirmDiscard(null);
  };

  const save = async () => {
    if (!snapshot || needsReload || saving || busy || !dirty) return;
    const translatedText = draft.trim();
    if (!translatedText) {
      setMessage('Hãy nhập phụ đề tiếng Việt trước khi lưu.');
      return;
    }
    setSaving(true);
    setMessage(null);
    try {
      if (await onSave(target, snapshot, translatedText)) {
        onClose();
        return;
      }
      setMessage('Chưa lưu được phụ đề. Bản nháp vẫn ở đây để bạn thử lại.');
    } catch {
      setMessage('Chưa lưu được phụ đề. Bản nháp vẫn ở đây để bạn thử lại.');
    } finally {
      setSaving(false);
    }
  };

  const modal = (
    <div className="vietsub-timeline-cue-edit-overlay" onPointerDown={(event) => {
      if (event.target === event.currentTarget) requestClose();
    }}>
      <div
        ref={popoverRef}
        className={`vietsub-timeline-cue-edit-popover is-${placement?.side ?? 'above'}`}
        role="dialog"
        aria-modal="true"
        aria-labelledby="vietsub-timeline-cue-edit-title"
        tabIndex={-1}
        style={{
          left: placement?.left ?? 12,
          top: placement?.top ?? 12,
          visibility: placement ? 'visible' : 'hidden'
        }}
        onKeyDown={(event) => {
          if (event.key === 'Escape') {
            event.preventDefault();
            if (confirmDiscard) setConfirmDiscard(null);
            else requestClose();
          } else if ((event.ctrlKey || event.metaKey) && event.key === 'Enter') {
            event.preventDefault();
            void save();
          } else if (event.key === 'Tab') {
            const focusable = [...event.currentTarget.querySelectorAll<HTMLElement>(
              'button:not(:disabled), textarea:not(:disabled)'
            )];
            if (!focusable.length) return;
            const first = focusable[0];
            const last = focusable[focusable.length - 1];
            if (event.shiftKey && (document.activeElement === first || document.activeElement === event.currentTarget)) {
              event.preventDefault();
              last.focus();
            } else if (!event.shiftKey && document.activeElement === last) {
              event.preventDefault();
              first.focus();
            }
          }
        }}
      >
        <header className="vietsub-timeline-cue-edit-header">
          <div>
            <strong id="vietsub-timeline-cue-edit-title">Chỉnh sửa phụ đề · Câu {target.cueIndex + 1}</strong>
            <span>{formatCueTime(target.startMilliseconds)} – {formatCueTime(target.endMilliseconds)}</span>
          </div>
          <button type="button" className="vietsub-timeline-cue-edit-close" aria-label="Đóng chỉnh sửa phụ đề"
            disabled={saving} onClick={requestClose}><X size={17} /></button>
        </header>

        <div className="vietsub-timeline-cue-edit-body">
          {!snapshot ? (
            <div className="vietsub-timeline-cue-edit-loading" role="status">
              {loadTimedOut ? (
                <>
                  <CircleAlert size={19} />
                  <strong>Chưa tải được câu phụ đề này.</strong>
                  <button type="button" onClick={requestReload}><RefreshCw size={15} /> Tải lại</button>
                </>
              ) : (
                <><LoaderCircle size={20} className="spin" /><span>Đang tải nội dung phụ đề…</span></>
              )}
            </div>
          ) : (
            <>
              <div className="vietsub-timeline-cue-edit-meta">
                <span className={snapshot.translatedText.trim() ? 'is-translated' : 'is-pending'}>
                  {snapshot.translatedText.trim() ? 'Đã có phụ đề' : 'Chưa có phụ đề Việt'}
                </span>
                {currentCue && currentCue.warnings.length > 0 && <span className="is-warning">{currentCue.warnings.length} cảnh báo</span>}
              </div>
              <div className="vietsub-timeline-cue-edit-source">
                <span>Nội dung gốc</span>
                <p>{snapshot.originalText}</p>
              </div>
              <label className="vietsub-timeline-cue-edit-field">
                <span>Phụ đề tiếng Việt</span>
                <textarea ref={textareaRef} value={draft} rows={5} maxLength={10_000}
                  disabled={saving || busy}
                  readOnly={needsReload}
                  placeholder="Nhập phụ đề tiếng Việt…"
                  onChange={(event) => { setDraft(event.target.value); setMessage(null); setConfirmDiscard(null); }} />
                <small>{draft.length.toLocaleString('vi-VN')} / 10.000 ký tự · Ctrl+Enter để lưu</small>
              </label>
              {needsReload && <div className="vietsub-timeline-cue-edit-message is-warning" role="alert">
                Câu phụ đề đã thay đổi hoặc chưa tải lại. Tải bản mới trước khi lưu để tránh ghi đè.
                <button type="button" onClick={requestReload}><RefreshCw size={14} /> Tải bản mới</button>
              </div>}
              {message && <div className="vietsub-timeline-cue-edit-message is-error" role="alert"><CircleAlert size={15} /> {message}</div>}
            </>
          )}
          {confirmDiscard && <div className="vietsub-timeline-cue-edit-discard" role="alertdialog"
            aria-label="Xác nhận bỏ thay đổi phụ đề">
            <strong>Bỏ thay đổi chưa lưu?</strong>
            <span>Bản nháp của câu này sẽ không được lưu.</span>
            <div>
              <button type="button" onClick={() => setConfirmDiscard(null)}>Tiếp tục sửa</button>
              <button type="button" className="is-danger" onClick={confirmDiscardDraft}>Bỏ thay đổi</button>
            </div>
          </div>}
        </div>

        <footer className="vietsub-timeline-cue-edit-footer">
          <button type="button" className="is-secondary" disabled={saving} onClick={requestClose}>Hủy</button>
          <button type="button" className="is-primary"
            disabled={!snapshot || needsReload || !dirty || busy || saving || Boolean(confirmDiscard)}
            onClick={() => { void save(); }}>
            {saving && <LoaderCircle size={15} className="spin" />}
            {saving ? 'Đang lưu…' : 'Lưu phụ đề'}
          </button>
        </footer>
      </div>
    </div>
  );
  return typeof document === 'undefined' ? modal : createPortal(modal, document.body);
}

function formatCueTime(milliseconds: number): string {
  const totalSeconds = Math.floor(Math.max(0, milliseconds) / 1000);
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  const fraction = Math.max(0, milliseconds) % 1000;
  return `${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}.${String(fraction).padStart(3, '0')}`;
}
