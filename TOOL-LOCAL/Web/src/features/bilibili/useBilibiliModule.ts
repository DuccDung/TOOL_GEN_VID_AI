import { useCallback, useEffect, useRef, useState } from 'react';
import { isHosted, postToHost, subscribeToHost } from '../../bridge';
import type { BilibiliDownloadRequest, BilibiliJobRequest, BilibiliScanRequest } from '../../types';
import { applyBilibiliUpdate, initialBilibiliState } from './types';

export function useBilibiliModule(active: boolean, userId: string) {
  const [state, setState] = useState(initialBilibiliState);
  const [error, setError] = useState<string | null>(null);
  const [pendingAction, setPendingAction] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const pending = useRef(new Map<string, string>());
  const action = useRef<string | null>(null);
  const refreshId = useRef<string | null>(null);

  const send = useCallback((type: string, payload: unknown = {}, exclusive = false) => {
    if (!isHosted) { setError('Hãy mở chức năng này trong ứng dụng VideoMaker trên máy tính.'); return; }
    if (exclusive && action.current) return;
    const id = postToHost(type, payload);
    pending.current.set(id, type);
    if (exclusive) { action.current = id; setPendingAction(id); setError(null); }
    return id;
  }, []);
  const refresh = useCallback(() => {
    if (refreshId.current) return;
    const id = send('bilibili.state.get');
    if (id) { refreshId.current = id; setLoading(true); }
  }, [send]);

  useEffect(() => {
    pending.current.clear(); action.current = null; refreshId.current = null;
    setState(initialBilibiliState); setError(null); setPendingAction(null); setLoading(false);
  }, [userId]);

  useEffect(() => subscribeToHost(message => {
    if (!message.type.startsWith('bilibili.')) return;
    if (message.type === 'bilibili.ack' || message.type === 'bilibili.error') {
      const id = message.requestId ?? '';
      if (!pending.current.has(id)) return;
      pending.current.delete(id);
      if (action.current === id) { action.current = null; setPendingAction(null); }
      if (refreshId.current === id) { refreshId.current = null; setLoading(false); }
      if (message.error) setError(message.error.message);
      return;
    }
    setState(previous => applyBilibiliUpdate(previous, message.type, message.payload));
  }), []);

  useEffect(() => { if (active && userId) refresh(); }, [active, userId, refresh]);

  return {
    state, error, loading, hosted: isHosted, busy: state.operation !== 'Idle' || pendingAction !== null,
    refresh, dismissError: () => setError(null),
    install: () => send('bilibili.install', {}, true),
    scan: (url: string) => send('bilibili.scan', { url } satisfies BilibiliScanRequest, true),
    selectFolder: () => send('bilibili.folder.select', {}, true),
    openFolder: (jobId?: string) => send('bilibili.folder.open', { jobId } satisfies BilibiliJobRequest),
    download: (entryIds: string[], quality: string) => state.scan && send('bilibili.download', {
      scanId: state.scan.id, entryIds, quality
    } satisfies BilibiliDownloadRequest, true),
    retry: (jobId: string) => send('bilibili.retry', { jobId } satisfies BilibiliJobRequest, true),
    cancel: (jobId?: string) => send('bilibili.cancel', { jobId } satisfies BilibiliJobRequest)
  };
}
export type BilibiliModule = ReturnType<typeof useBilibiliModule>;
