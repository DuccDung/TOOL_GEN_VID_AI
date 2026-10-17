import { useCallback, useEffect, useRef, useState } from 'react';
import { postToHost, subscribeToHost } from '../../bridge';
import type { DesktopRepairFailure, DesktopUpdateProgress } from '../../types';
import { acceptSetupSnapshot, isCompleteSetupSnapshot, setupRunning, type SetupRequest, type SetupSnapshot } from './types';

const acknowledgementTimeoutMs = 15000;
type PendingSetupRequest = { type: string; payload: SetupRequest };
type SentSetupRequest = { type: string; operationId?: string; sequence: number };

export function useSystemSetup(organizationId: string, userId = '') {
  const [snapshot, setSnapshot] = useState<SetupSnapshot | null>(null);
  const snapshotRef = useRef<SetupSnapshot | null>(null);
  const [error, setError] = useState('');
  const [pending, setPending] = useState(false);
  const [connectionLost, setConnectionLost] = useState(false);
  const connectionLostRef = useRef(false);
  const responseError = useRef(false);
  const [acknowledgementAttempt, setAcknowledgementAttempt] = useState(0);
  const [repairProgress, setRepairProgress] = useState<DesktopUpdateProgress | null>(null);
  const [repairError, setRepairError] = useState('');
  const requests = useRef(new Map<string, SentSetupRequest>());
  const ownedResponses = useRef(new Map<string, string>());
  const requestSequence = useRef(0);
  const lastSuccessfulGet = useRef(0);
  const pendingOperation = useRef<string | null>(null);
  const pendingRequest = useRef<PendingSetupRequest | null>(null);
  const repairRequest = useRef<string | null>(null);
  const contextKey = `${userId}:${organizationId}`;
  const activeContext = useRef(contextKey);
  // The controller outlives the startup modal. Remounts and repeated status events
  // must not restart an automatic preparation that failed or was cancelled.
  const preparedContexts = useRef(new Set<string>());
  const rememberResponse = useCallback((requestId: string, type: string) => {
    ownedResponses.current.set(requestId, type);
    if (ownedResponses.current.size <= 512) return;
    // Keep mutation IDs across context changes and completed operations. Frequent
    // polling must not evict an install ID while a slow native probe is running.
    const oldestGet = [...ownedResponses.current].find(([, command]) => command === 'system.setup.get');
    ownedResponses.current.delete(oldestGet?.[0] ?? ownedResponses.current.keys().next().value!);
  }, []);
  const ownsResponse = useCallback((requestId?: string | null) => Boolean(requestId && ownedResponses.current.has(requestId)), []);
  const send = useCallback((type: string, payload?: unknown) => {
    const requestId = postToHost(type, payload);
    rememberResponse(requestId, type);
    const operationId = (payload as Partial<SetupRequest> | undefined)?.operationId;
    requests.current.set(requestId, { type, operationId, sequence: ++requestSequence.current });
    if (requests.current.size > 100) requests.current.delete(requests.current.keys().next().value!);
    return requestId;
  }, [rememberResponse]);
  const refresh = useCallback(() => { if (organizationId) send('system.setup.get'); }, [organizationId, send]);
  useEffect(() => {
    activeContext.current = contextKey;
    requests.current.clear();
    pendingOperation.current = null;
    pendingRequest.current = null;
    repairRequest.current = null;
    snapshotRef.current = null;
    connectionLostRef.current = false;
    responseError.current = false;
    preparedContexts.current.clear();
    setConnectionLost(false);
    setSnapshot(null); setPending(false); setError(''); setRepairProgress(null); setRepairError('');
    const unsubscribe = subscribeToHost(message => {
      const request = message.requestId ? requests.current.get(message.requestId) : undefined;
      if (request && message.requestId) requests.current.delete(message.requestId);
      if (message.type === 'operation.error' && repairRequest.current && message.requestId === repairRequest.current) {
        repairRequest.current = null;
        setRepairProgress(null);
        setRepairError(message.error?.message ?? 'Không thể sửa bộ ứng dụng.');
        return;
      }
      if (message.type === 'operation.error' && request) {
        if (request.type === 'system.setup.get' && request.sequence < lastSuccessfulGet.current) return;
        if (request.type !== 'system.setup.get') {
          if (request.operationId !== pendingOperation.current
            && request.operationId !== snapshotRef.current?.operation?.operationId) return;
          if (request.operationId === pendingOperation.current) {
            pendingOperation.current = null; pendingRequest.current = null; setPending(false);
          }
        }
        connectionLostRef.current = false; setConnectionLost(false);
        setError(message.error?.message ?? 'Không thể thực hiện Setup.');
        return;
      }
      if (message.type === 'media.tools.install.progress') {
        if (!repairRequest.current || message.requestId !== repairRequest.current) return;
        setRepairProgress(message.payload as DesktopUpdateProgress);
        setRepairError('');
        return;
      }
      if (message.type === 'media.tools.install.failed') {
        if (!repairRequest.current || message.requestId !== repairRequest.current) return;
        repairRequest.current = null;
        setRepairProgress(null);
        setRepairError(String((message.payload as Partial<DesktopRepairFailure>)?.message ?? 'Không thể sửa bộ ứng dụng.'));
        return;
      }
      if (!['system.setup.status', 'system.setup.accepted', 'system.setup.progress', 'system.setup.completed'].includes(message.type)) return;
      // Correlated replies from an earlier user or a settled request cannot establish state.
      if (message.requestId && !request) return;
      const incoming = message.payload;
      if (!isCompleteSetupSnapshot(incoming)) {
        if (request) {
          snapshotRef.current = null;
          setSnapshot(null);
          responseError.current = true;
          setError('Trạng thái Setup chưa đầy đủ hoặc không hợp lệ. Hãy tải lại trạng thái; ứng dụng vẫn được khóa.');
        }
        return;
      }
      if (incoming.organizationId !== organizationId) return;
      // Only a response to our GET/START can establish a new native context/operation.
      const authoritative = Boolean(request && message.type !== 'system.setup.progress');
      const current = snapshotRef.current;
      if (!current && !authoritative) return;
      const accepted = acceptSetupSnapshot(current, incoming, organizationId, authoritative);
      if (accepted !== incoming) return;
      snapshotRef.current = accepted;
      setSnapshot(accepted);
      if (request?.type === 'system.setup.get') lastSuccessfulGet.current = request.sequence;
      if ((connectionLostRef.current || responseError.current)
        && (!pendingOperation.current || incoming.operation?.operationId === pendingOperation.current)) {
        connectionLostRef.current = false; setConnectionLost(false); setError('');
        responseError.current = false;
      }
      if (incoming.operation?.operationId === pendingOperation.current) {
        pendingOperation.current = null; pendingRequest.current = null; setPending(false);
      }
      // A GET/terminal event can settle a request whose ACK was lost. Remove every
      // replay of that operation so a late rejection cannot affect a later run.
      if (incoming.operation) {
        for (const [id, sent] of requests.current) {
          if (sent.operationId === incoming.operation.operationId) requests.current.delete(id);
        }
      }
    });
    refresh();
    const initialResponseTimer = organizationId ? window.setTimeout(() => {
      if (snapshotRef.current) return;
      connectionLostRef.current = true; setConnectionLost(true);
      setError('Chưa nhận trạng thái Setup từ ứng dụng. Hãy tải lại trạng thái hoặc thoát và mở lại ứng dụng.');
    }, acknowledgementTimeoutMs) : undefined;
    return () => { unsubscribe(); window.clearTimeout(initialResponseTimer); };
  }, [organizationId, userId, contextKey, refresh]);
  useEffect(() => {
    if (!pending || connectionLost) return;
    const timer = window.setTimeout(() => {
      if (!pendingOperation.current) return;
      connectionLostRef.current = true; setConnectionLost(true);
      setError('Chưa nhận xác nhận Setup. Chọn Đối soát trạng thái để kiểm tra và gửi lại đúng lượt cũ; không tạo lượt cài mới.');
    }, acknowledgementTimeoutMs);
    return () => window.clearTimeout(timer);
  }, [pending, connectionLost, acknowledgementAttempt]);
  const busy = pending || setupRunning(snapshot?.operation);
  useEffect(() => {
    if (!organizationId) return;
    const timer = window.setInterval(refresh, busy ? 2500 : 6000);
    return () => window.clearInterval(timer);
  }, [busy, refresh, organizationId]);
  const run = useCallback((mode: 'check' | 'start' | 'retry', ids: string[], confirmResources = false) => {
    if (!snapshot || busy || pendingOperation.current || repairRequest.current || !organizationId || ids.length === 0) return;
    const payload: SetupRequest = {
      operationId: crypto.randomUUID(), expectedOrganizationId: organizationId,
      contextGeneration: snapshot.contextGeneration, componentIds: ids,
      ...(mode === 'retry' ? { previousOperationId: snapshot.operation?.operationId } : {}),
      ...(confirmResources ? { resourceWarningAccepted: true,
        confirmedResourceProfileId: snapshot.components.find(c => c.id === 'qwen')?.resourceProfileId ?? undefined } : {})
    };
    pendingOperation.current = payload.operationId;
    pendingRequest.current = { type: `system.setup.${mode}`, payload };
    connectionLostRef.current = false; setConnectionLost(false);
    setPending(true); setError(''); send(`system.setup.${mode}`, payload);
  }, [snapshot, busy, organizationId, send]);
  useEffect(() => {
    if (!snapshot || !snapshot.startupRequired || snapshot.organizationId !== organizationId) return;
    const key = `${organizationId}:${snapshot.contextGeneration}`;
    const operation = snapshot.operation;
    if (operation && operation.mode !== 'check' && operation.componentIds.includes('piper')) {
      preparedContexts.current.add(key);
      return;
    }
    if (busy || pendingOperation.current || repairRequest.current || error || preparedContexts.current.has(key)) return;
    if (!operation || operation.mode !== 'check' || !['Completed', 'PartiallyCompleted', 'Failed'].includes(operation.state)) return;
    const piper = snapshot.components.find(component => component.id === 'piper');
    if (piper?.state !== 'NOT_INSTALLED' || !piper.canInstall || !piper.canPrepareOffline) return;
    preparedContexts.current.add(key);
    run('start', ['piper']);
  }, [snapshot, organizationId, busy, error, run]);
  const cancel = useCallback(() => {
    const operationId = pendingOperation.current
      ?? (snapshot?.operation && setupRunning(snapshot.operation) ? snapshot.operation.operationId : null);
    if (operationId) send('system.setup.cancel', { operationId });
  }, [snapshot, send]);
  const reconcile = useCallback(() => {
    refresh();
    const previous = pendingRequest.current;
    if (!previous || !connectionLostRef.current) return;
    // The host treats the operation ID and exact payload idempotently. A new ID
    // would risk starting another install while the original ACK is missing.
    send(previous.type, previous.payload);
    connectionLostRef.current = false; setConnectionLost(false); setError('');
    setAcknowledgementAttempt(value => value + 1);
  }, [refresh, send]);
  const repairApplication = useCallback(() => {
    if (busy || repairProgress || repairRequest.current) return;
    setRepairError('');
    setRepairProgress({ stage: 'starting', percent: 0, message: 'Đang chuẩn bị package sửa chữa…' });
    repairRequest.current = postToHost('media.tools.install');
    rememberResponse(repairRequest.current, 'media.tools.install');
  }, [busy, repairProgress, rememberResponse]);
  const exitApplication = useCallback(() => postToHost('system.setup.exit'), []);
  return { snapshot: activeContext.current === contextKey ? snapshot : null, busy, error, connectionLost,
    repairProgress, repairError, run, cancel, refresh, reconcile, ownsResponse, repairApplication, exitApplication };
}
export type SystemSetupController = ReturnType<typeof useSystemSetup>;
