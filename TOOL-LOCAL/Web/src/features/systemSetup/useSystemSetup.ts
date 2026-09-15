import { useCallback, useEffect, useRef, useState } from 'react';
import { postToHost, subscribeToHost } from '../../bridge';
import type { DesktopUpdateProgress } from '../../types';
import { acceptSetupSnapshot, setupRunning, type SetupRequest, type SetupSnapshot } from './types';

export function useSystemSetup(organizationId: string) {
  const [snapshot, setSnapshot] = useState<SetupSnapshot | null>(null);
  const [error, setError] = useState('');
  const [pending, setPending] = useState(false);
  const [repairProgress, setRepairProgress] = useState<DesktopUpdateProgress | null>(null);
  const [repairError, setRepairError] = useState('');
  const requests = useRef(new Map<string, string>());
  const pendingOperation = useRef<string | null>(null);
  const send = useCallback((type: string, payload?: unknown) => {
    const requestId = postToHost(type, payload);
    requests.current.set(requestId, type);
    if (requests.current.size > 100) requests.current.delete(requests.current.keys().next().value!);
    return requestId;
  }, []);
  const refresh = useCallback(() => { if (organizationId) send('system.setup.get'); }, [organizationId, send]);
  useEffect(() => {
    requests.current.clear();
    pendingOperation.current = null;
    setSnapshot(null); setPending(false); setError(''); setRepairProgress(null); setRepairError('');
    const unsubscribe = subscribeToHost(message => {
      const requestType = message.requestId ? requests.current.get(message.requestId) : undefined;
      if (requestType && message.requestId) requests.current.delete(message.requestId);
      if (message.type === 'operation.error' && requestType) {
        if (requestType !== 'system.setup.get') { pendingOperation.current = null; setPending(false); }
        setError(message.error?.message ?? 'Không thể thực hiện Setup.');
        return;
      }
      if (message.type === 'media.tools.install.progress') {
        setRepairProgress(message.payload as DesktopUpdateProgress);
        setRepairError('');
        return;
      }
      if (message.type === 'media.tools.install.failed') {
        setRepairProgress(null);
        setRepairError(String((message.payload as { message?: string })?.message ?? 'Không thể sửa bộ ứng dụng.'));
        return;
      }
      if (!['system.setup.status', 'system.setup.accepted', 'system.setup.progress', 'system.setup.completed'].includes(message.type)) return;
      const incoming = message.payload as SetupSnapshot | undefined;
      if (!incoming || !Array.isArray(incoming.components) || incoming.organizationId !== organizationId) return;
      // Only a response to our GET/START can establish a new native context/operation.
      const authoritative = Boolean(requestType && message.type !== 'system.setup.progress');
      setSnapshot(current => acceptSetupSnapshot(current, incoming, organizationId, authoritative));
      if (incoming.operation?.operationId === pendingOperation.current) {
        pendingOperation.current = null; setPending(false);
      }
    });
    refresh();
    return unsubscribe;
  }, [organizationId, refresh]);
  const busy = pending || setupRunning(snapshot?.operation);
  useEffect(() => {
    if (!organizationId) return;
    const timer = window.setInterval(refresh, busy ? 2500 : 6000);
    return () => window.clearInterval(timer);
  }, [busy, refresh, organizationId]);
  const run = useCallback((mode: 'check' | 'start' | 'retry', ids: string[], confirmResources = false) => {
    if (!snapshot || busy || !organizationId || ids.length === 0) return;
    const payload: SetupRequest = {
      operationId: crypto.randomUUID(), expectedOrganizationId: organizationId,
      contextGeneration: snapshot.contextGeneration, componentIds: ids,
      ...(mode === 'retry' ? { previousOperationId: snapshot.operation?.operationId } : {}),
      ...(confirmResources ? { resourceWarningAccepted: true,
        confirmedResourceProfileId: snapshot.components.find(c => c.id === 'qwen')?.resourceProfileId ?? undefined } : {})
    };
    pendingOperation.current = payload.operationId;
    setPending(true); setError(''); send(`system.setup.${mode}`, payload);
  }, [snapshot, busy, organizationId, send]);
  const cancel = useCallback(() => {
    if (snapshot?.operation && setupRunning(snapshot.operation))
      send('system.setup.cancel', { operationId: snapshot.operation.operationId });
  }, [snapshot, send]);
  const repairApplication = useCallback(() => {
    if (busy || repairProgress) return;
    setRepairError('');
    setRepairProgress({ stage: 'starting', percent: 0, message: 'Đang chuẩn bị package sửa chữa…' });
    postToHost('media.tools.install');
  }, [busy, repairProgress]);
  const exitApplication = useCallback(() => postToHost('system.setup.exit'), []);
  return { snapshot, busy, error, repairProgress, repairError, run, cancel, refresh, repairApplication, exitApplication };
}
export type SystemSetupController = ReturnType<typeof useSystemSetup>;
