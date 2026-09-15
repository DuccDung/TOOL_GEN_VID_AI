import { useCallback, useEffect, useRef, useState } from 'react';
import { isHosted, postToHost, subscribeToHost } from '../../bridge';
import { emptyPublishingState, type PublishingImage, type PublishingPreview, type PublishingSchedule, type PublishingState } from './types';

type Envelope = { organizationId: string; action: string; data: unknown };
export function usePublishingModule(active: boolean, organizationId: string, userId: string) {
  const [state, setState] = useState(emptyPublishingState);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(false);
  const [image, setImage] = useState<{ image: PublishingImage; previewUrl: string } | null>(null);
  const [saved, setSaved] = useState<PublishingSchedule | null>(null);
  const [preview, setPreview] = useState<PublishingPreview | null>(null);
  const pending = useRef(new Map<string, { action: string; context: string; exclusive: boolean }>());
  const action = useRef<string | null>(null);
  const refreshId = useRef<string | null>(null);
  const context = `${userId}:${organizationId}`;
  const current = useRef(context); current.current = context;

  const send = useCallback((type: string, payload: object = {}, exclusive = true) => {
    if (!isHosted) { setError('Mở tính năng này trong ứng dụng VideoMaker để kết nối server.'); return; }
    if (!organizationId || !userId || exclusive && action.current) return;
    const id = postToHost(type, { ...payload, organizationId });
    pending.current.set(id, { action: type, context: current.current, exclusive });
    if (exclusive) { action.current = id; setBusy(true); setError(null); }
    return id;
  }, [organizationId, userId]);
  const refresh = useCallback(() => {
    if (refreshId.current || action.current) return;
    const id = send('publishing.state.get', {}, false);
    if (id) { refreshId.current = id; setLoading(true); }
  }, [send]);

  useEffect(() => {
    pending.current.clear(); action.current = null; refreshId.current = null;
    setState(emptyPublishingState); setImage(null); setSaved(null); setPreview(null); setError(null); setBusy(false); setLoading(false);
  }, [context]);
  useEffect(() => subscribeToHost(message => {
    if (!message.type.startsWith('publishing.') || !message.requestId) return;
    const operation = pending.current.get(message.requestId);
    if (!operation || operation.context !== current.current) return;
    pending.current.delete(message.requestId);
    if (action.current === message.requestId) { action.current = null; setBusy(false); }
    if (refreshId.current === message.requestId) { refreshId.current = null; setLoading(false); }
    if (message.error) { setError(message.error.message); return; }
    const envelope = message.payload as Envelope;
    if (envelope?.organizationId !== organizationId || envelope.action !== operation.action) return;
    switch (operation.action) {
      case 'publishing.state.get': setState(envelope.data as PublishingState); break;
      case 'publishing.image.select': if (envelope.data) setImage(envelope.data as typeof image); break;
      case 'publishing.schedule.save': setSaved(envelope.data as PublishingSchedule); refresh(); break;
      case 'publishing.run.preview': setPreview(envelope.data as PublishingPreview); break;
      case 'publishing.run.review': setPreview(null); refresh(); break;
      case 'publishing.schedule.change':
      case 'publishing.run.action':
      case 'publishing.connection.disconnect': refresh(); break;
    }
  }), [organizationId, refresh]);
  useEffect(() => {
    if (!active || !organizationId || !userId) return;
    refresh();
    const interval = setInterval(refresh, 30_000);
    return () => clearInterval(interval);
  }, [active, organizationId, userId, refresh]);

  return { state, error, busy, loading, image, saved, preview, hosted: isHosted, refresh, send,
    clearError: () => setError(null), closePreview: () => setPreview(null), resetSaved: () => setSaved(null) };
}
export type PublishingModule = ReturnType<typeof usePublishingModule>;
