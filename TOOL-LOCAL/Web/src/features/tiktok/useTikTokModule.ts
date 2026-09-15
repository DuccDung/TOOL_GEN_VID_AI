import { useCallback, useEffect, useRef, useState } from 'react';
import { postToHost, subscribeToHost } from '../../bridge';
import type { HostMessage } from '../../types';
import type { TikTokCreatorInfo, TikTokFeatureState, TikTokMedia, TikTokModuleState,
  TikTokPublishPayload, TikTokPublishStatus, TikTokUploadProgress, TikTokPublishHistory } from './types';
import { formatTikTokFailure } from './tiktokFailure';
import { mergeTikTokJobs, acceptsCreatorResponse, selectTikTokAccount } from './tiktokAccountState';

const initialState: TikTokModuleState = {
  feature: { enabled: false, configured: false, connections: [], connection: null },
  selectedConnectionId: null, jobs: [], history: null, historyLoading: false,
  creator: null, media: null, upload: null, publish: null, loading: false, busy: false,
  uploadCompleted: false, error: null, publishFeedback: null
};
type Pending = { kind: string; connectionId?: string; page?: number };
const STATE_RESPONSE_TIMEOUT_MS = 35_000;

export function useTikTokModule(enabled: boolean) {
  const [state, setState] = useState<TikTokModuleState>({ ...initialState, feature: { ...initialState.feature, enabled } });
  const current = useRef(state);
  const pending = useRef(new Map<string, Pending>());
  const operationId = useRef<string | null>(null);
  const creatorRequest = useRef<{ id: string; connectionId: string } | null>(null);
  const stateRequestId = useRef<string | null>(null);
  const stateRequestTimer = useRef<number | null>(null);
  const historyRequestId = useRef<string | null>(null);
  const publishRequestId = useRef<string | null>(null);
  const errorSource = useRef<string | null>(null);
  const intents = useRef(new Map<string, string>());
  const update = useCallback((change: (value: TikTokModuleState) => TikTokModuleState) => {
    current.current = change(current.current);
    setState(current.current);
  }, []);
  const send = useCallback((type: string, payload: unknown, context: Pending) => {
    const id = postToHost(type, payload);
    pending.current.set(id, context);
    return id;
  }, []);
  const clearStateRequest = useCallback(() => {
    if (stateRequestTimer.current !== null) window.clearTimeout(stateRequestTimer.current);
    stateRequestTimer.current = null;
    stateRequestId.current = null;
  }, []);
  const refreshState = useCallback(() => {
    if (stateRequestId.current) return;
    const id = send('tiktok.state.get', undefined, { kind: 'state' });
    stateRequestId.current = id;
    stateRequestTimer.current = window.setTimeout(() => {
      if (stateRequestId.current !== id) return;
      pending.current.delete(id);
      clearStateRequest();
      errorSource.current = 'state';
      update(s => ({ ...s, loading: false, error: 'Không nhận được phản hồi TikTok. Vui lòng thử lại.' }));
    }, STATE_RESPONSE_TIMEOUT_MS);
  }, [send, clearStateRequest, update]);
  const requestCreator = useCallback(() => {
    const account = current.current.feature.connection;
    if (!account || account.status === 'Disconnected' || account.status === 'ReconnectRequired') return;
    if (creatorRequest.current?.connectionId === account.connectionId) return;
    const id = send('tiktok.creator.get', { connectionId: account.connectionId }, { kind: 'creator', connectionId: account.connectionId });
    creatorRequest.current = { id, connectionId: account.connectionId };
  }, [send]);
  const requestHistory = useCallback((page = 1) => {
    const connectionId = current.current.selectedConnectionId ?? undefined;
    update(s => ({ ...s, historyLoading: true }));
    historyRequestId.current = send('tiktok.history.get', { connectionId, page }, { kind: 'history', connectionId, page });
  }, [send, update]);

  useEffect(() => subscribeToHost((message: HostMessage) => {
    if (!message.type.startsWith('tiktok.') && message.type !== 'operation.error') return;
    const requestId = message.requestId ?? '';
    const context = pending.current.get(requestId);
    if (!context) return;
    const finish = () => {
      pending.current.delete(requestId);
      if (operationId.current === requestId) operationId.current = null;
    };
    if (message.type === 'tiktok.state' && message.payload) {
      if (context.kind === 'state' && stateRequestId.current !== requestId) { finish(); return; }
      if (context.kind === 'connect' || context.kind === 'disconnect') {
        // A read begun before the mutation must not restore the old account list.
        if (stateRequestId.current) pending.current.delete(stateRequestId.current);
        clearStateRequest();
      }
      if (stateRequestId.current === requestId) clearStateRequest();
      const feature = message.payload as TikTokFeatureState;
      const accounts = feature.connections ?? (feature.connection ? [feature.connection] : []);
      update(s => {
        const selected = feature.connectedConnectionId && context.kind === 'connect'
          ? feature.connectedConnectionId : selectTikTokAccount(accounts, s.selectedConnectionId);
        const changed = selected !== s.selectedConnectionId;
        const jobs = mergeTikTokJobs(s.jobs, [...(feature.recentPublishes ?? []), ...(feature.activePublishes ?? []), ...(feature.activePublish ? [feature.activePublish] : [])]);
        const publish = jobs.find(job => job.connectionId === selected && !job.isTerminal)
          ?? (s.publish?.connectionId === selected ? jobs.find(job => job.publishJobId === s.publish?.publishJobId) ?? s.publish : null);
        const feedbackJob = jobs.find(j => j.publishJobId === s.publishFeedback?.jobId);
        const feedback = !changed && feedbackJob && s.publishFeedback
          ? { ...feedbackForStatus(feedbackJob, s.uploadCompleted && feedbackJob.publishJobId === s.publish?.publishJobId,
              s.publishFeedback.open, s.publishFeedback.accountLabel), fileName: s.publishFeedback.fileName,
              canCancel: Boolean(s.busy && feedbackJob.publishJobId === s.publish?.publishJobId) }
          : changed ? null : s.publishFeedback;
        return { ...s, feature: { ...feature, connections: accounts, connection: accounts.find(a => a.connectionId === selected) ?? null },
          selectedConnectionId: selected, creator: changed ? null : s.creator, jobs, publish, publishFeedback: feedback,
          loading: false, busy: context.kind === 'connect' || context.kind === 'disconnect' ? false : s.busy,
          error: context.kind === 'connect' || context.kind === 'disconnect' || errorSource.current === 'state' ? null : s.error };
      });
      finish(); return;
    }
    if (message.type === 'tiktok.creator' && message.payload) {
      const creator = message.payload as TikTokCreatorInfo;
      const isPublish = requestId === publishRequestId.current && context.kind === 'publish';
      const expected = isPublish ? { id: requestId, connectionId: context.connectionId! } : creatorRequest.current;
      if (!acceptsCreatorResponse(expected, requestId, current.current.selectedConnectionId, creator.connectionId)) { finish(); return; }
      if (creatorRequest.current?.id === requestId) creatorRequest.current = null;
      update(s => ({ ...s, creator, feature: { ...s.feature, connection: s.feature.connection ? {
        ...s.feature.connection, creatorUsername: creator.creatorUsername, creatorNickname: creator.creatorNickname,
        avatarUrl: creator.avatarUrl ?? s.feature.connection.avatarUrl } : null }, loading: false, busy: isPublish ? false : s.busy, error: null,
        publishFeedback: isPublish && creator.publishingIssue ? { phase: 'error', open: true,
          message: creator.publishingIssue.message, connectionId: context.connectionId, accountLabel: accountLabel(s) } : s.publishFeedback }));
      finish(); return;
    }
    if (message.type === 'tiktok.history' && message.payload) {
      if (historyRequestId.current === requestId && context.connectionId === (current.current.selectedConnectionId ?? undefined)) {
        historyRequestId.current = null;
        const history = message.payload as TikTokPublishHistory;
        update(s => ({ ...s, history, historyLoading: false, jobs: mergeTikTokJobs(s.jobs, history.items) }));
      }
      finish(); return;
    }
    if (message.type === 'tiktok.media.selected' && message.payload) {
      intents.current.clear();
      update(s => ({ ...s, media: message.payload as TikTokMedia, upload: null, uploadCompleted: false, publishFeedback: null, busy: false, error: null }));
      finish(); return;
    }
    if (message.type === 'tiktok.media.cancelled' || message.type === 'tiktok.media.cleared') {
      if (message.type === 'tiktok.media.cleared') intents.current.clear();
      update(s => ({ ...s, media: message.type === 'tiktok.media.cleared' ? null : s.media, busy: false }));
      finish(); return;
    }
    if (message.type === 'tiktok.publish.initialized' && message.payload) {
      const data = message.payload as { publishJobId: string; connectionId: string };
      if (requestId !== publishRequestId.current || data.connectionId !== context.connectionId) return;
      const job: TikTokPublishStatus = { ...data, creatorUsername: current.current.creator?.creatorUsername,
        creatorNickname: current.current.creator?.creatorNickname, status: 'PROCESSING_UPLOAD', uploadedBytes: 0, publicPostIds: [], updatedAtUtc: new Date().toISOString(), isTerminal: false,
        provisional: true };
      update(s => ({ ...s, publish: job, jobs: mergeTikTokJobs(s.jobs, [job]), publishFeedback: { ...s.publishFeedback!, jobId: job.publishJobId, phase: 'uploading' } }));
      return;
    }
    if (message.type === 'tiktok.upload.progress' && message.payload) {
      const upload = message.payload as TikTokUploadProgress;
      if (requestId === publishRequestId.current && upload.connectionId === context.connectionId) update(s => ({ ...s, upload }));
      return;
    }
    if (message.type === 'tiktok.upload.completed') {
      if (requestId === publishRequestId.current) {
        operationId.current = null;
        update(s => ({ ...s, busy: false, uploadCompleted: true, publishFeedback: { ...s.publishFeedback!, canCancel: false, phase: 'processing' } }));
      }
      finish(); refreshState(); return;
    }
    if (message.type === 'tiktok.publish.status' && message.payload) {
      const publish = message.payload as TikTokPublishStatus;
      update(s => {
        const active = publish.connectionId === s.selectedConnectionId && (requestId === publishRequestId.current || publish.publishJobId === s.publish?.publishJobId);
        return { ...s, jobs: mergeTikTokJobs(s.jobs, [publish]), publish: active ? publish : s.publish,
          publishFeedback: active ? feedbackForStatus(publish, s.uploadCompleted, s.publishFeedback?.open ?? false, s.publishFeedback?.accountLabel) : s.publishFeedback };
      });
      finish(); return;
    }
    if (message.type === 'tiktok.operation.cancelled') { finish(); return; }
    if (message.type === 'tiktok.error' || message.type === 'operation.error') {
      if ((context.kind === 'creator' && creatorRequest.current?.id !== requestId) ||
          (context.kind === 'history' && historyRequestId.current !== requestId) ||
          (context.kind === 'state' && stateRequestId.current !== requestId)) { finish(); return; }
      const active = operationId.current === requestId;
      const relevant = context.kind === 'connect' || context.kind === 'disconnect' ||
        !context.connectionId || context.connectionId === current.current.selectedConnectionId;
      if (relevant) errorSource.current = context.kind;
      if (creatorRequest.current?.id === requestId) creatorRequest.current = null;
      if (stateRequestId.current === requestId) clearStateRequest();
      if (historyRequestId.current === requestId) historyRequestId.current = null;
      update(s => ({ ...s, busy: active ? false : s.busy, loading: context.kind === 'state' ? false : s.loading,
        historyLoading: context.kind === 'history' && relevant ? false : s.historyLoading,
        error: relevant ? message.error?.message ?? 'Không thể hoàn tất thao tác TikTok.' : s.error,
        publishFeedback: requestId === publishRequestId.current && relevant ? {
          ...s.publishFeedback, canCancel: false,
          phase: message.error?.code === 'tiktok_operation_cancelled' ? 'cancelled' : 'error', open: true,
          message: message.error?.message, connectionId: context.connectionId, accountLabel: accountLabel(s)
        } : s.publishFeedback }));
      finish();
    }
  }), [update, refreshState, clearStateRequest]);

  useEffect(() => {
    if (!enabled) {
      pending.current.clear(); operationId.current = null; creatorRequest.current = null;
      clearStateRequest(); historyRequestId.current = null; intents.current.clear();
      update(() => ({ ...initialState })); return;
    }
    update(s => ({ ...s, loading: true, feature: { ...s.feature, enabled: true } }));
    refreshState();
    const timer = window.setInterval(refreshState, 10_000);
    return () => { window.clearInterval(timer); pending.current.clear(); clearStateRequest(); };
  }, [enabled, refreshState, update, clearStateRequest]);

  useEffect(() => {
    creatorRequest.current = null;
    if (state.selectedConnectionId) requestCreator();
    if (enabled && state.feature.configured) requestHistory();
  }, [state.selectedConnectionId, state.feature.connection?.updatedAtUtc, enabled, state.feature.configured, requestCreator, requestHistory]);

  const begin = (kind: string, type: string, payload: unknown, connectionId?: string) => {
    if (operationId.current) return;
    update(s => ({ ...s, busy: true, error: null }));
    operationId.current = send(type, payload, { kind, connectionId });
  };
  return {
    state,
    refresh: () => { refreshState(); requestCreator(); },
    refreshCreator: requestCreator,
    connect: (targetConnectionId?: string) => begin('connect', 'tiktok.oauth.connect', { targetConnectionId }, targetConnectionId),
    disconnect: (connectionId?: string) => {
      const id = connectionId ?? current.current.selectedConnectionId;
      if (id) begin('disconnect', 'tiktok.oauth.disconnect', { connectionId: id }, id);
    },
    selectAccount: (connectionId: string) => {
      if (operationId.current) return;
      creatorRequest.current = null;
      update(s => ({ ...s, selectedConnectionId: connectionId || null,
        feature: { ...s.feature, connection: s.feature.connections?.find(a => a.connectionId === connectionId) ?? null },
        creator: null, history: null, upload: null, uploadCompleted: false, error: null, publishFeedback: null,
        publish: s.jobs.find(j => j.connectionId === connectionId && !j.isTerminal) ?? null }));
    },
    loadHistory: requestHistory,
    selectMedia: () => begin('media', 'tiktok.media.select', undefined),
    clearMedia: () => begin('media', 'tiktok.media.clear', undefined),
    publish: (payload: TikTokPublishPayload) => {
      const s = current.current;
      if (operationId.current || !s.selectedConnectionId || !s.media || (s.publish && !s.publish.isTerminal)) return;
      const key = JSON.stringify({ ...payload, connectionId: s.selectedConnectionId, mediaId: s.media.mediaId });
      const fileName = s.media.fileName;
      if (!intents.current.has(key) || s.publish?.isTerminal) intents.current.set(key, crypto.randomUUID());
      update(value => ({ ...value, busy: true, error: null, publish: null, upload: null, uploadCompleted: false,
        publishFeedback: { phase: 'preparing', open: true, connectionId: s.selectedConnectionId, accountLabel: accountLabel(s), fileName, canCancel: true } }));
      publishRequestId.current = send('tiktok.publish.start', { ...payload, connectionId: s.selectedConnectionId,
        mediaId: s.media.mediaId, clientRequestId: intents.current.get(key) }, { kind: 'publish', connectionId: s.selectedConnectionId });
      operationId.current = publishRequestId.current;
    },
    hidePublishFeedback: () => update(s => ({ ...s, publishFeedback: s.publishFeedback ? { ...s.publishFeedback, open: false } : null })),
    newPublishIntent: () => {
      if (operationId.current || current.current.publish && !current.current.publish.isTerminal) return;
      for (const key of intents.current.keys()) {
        if (JSON.parse(key).connectionId === current.current.selectedConnectionId) intents.current.delete(key);
      }
      update(s => ({ ...s, publish: null, upload: null, publishFeedback: null, error: null }));
    },
    showPublishFeedback: () => update(s => ({ ...s, publishFeedback: s.publishFeedback ? { ...s.publishFeedback, open: true } : null })),
    showJob: (job: TikTokPublishStatus) => {
      if (operationId.current) return;
      update(s => ({ ...s, publishFeedback: feedbackForStatus(job, job.status !== 'PROCESSING_UPLOAD', true,
        job.creatorUsername ? '@' + job.creatorUsername : job.creatorNickname ?? 'Chưa có thông tin tài khoản lúc đăng') }));
    },
    cancel: () => send('tiktok.operation.cancel', { operationRequestId: operationId.current }, { kind: 'cancel' }),
    clearError: () => update(s => ({ ...s, error: null })),
    openPolicy: (policy: 'music' | 'branded') => { postToHost('tiktok.policy.open', { policy }); }
  };
}

function accountLabel(state: TikTokModuleState) {
  return state.feature.connection?.creatorUsername ? '@' + state.feature.connection.creatorUsername : state.feature.connection?.creatorNickname ?? 'Tài khoản TikTok';
}
function feedbackForStatus(publish: TikTokPublishStatus, uploaded: boolean, open: boolean, accountLabel?: string): NonNullable<TikTokModuleState['publishFeedback']> {
  const context = { open, connectionId: publish.connectionId, accountLabel, jobId: publish.publishJobId, canCancel: false };
  if (publish.status === 'PUBLISH_COMPLETE') return { ...context, phase: 'success' };
  if (publish.status === 'FAILED') return { ...context, phase: 'error', message: formatTikTokFailure(publish.failureReason) };
  return { ...context, phase: uploaded || publish.status !== 'PROCESSING_UPLOAD' ? 'processing' : 'uploading' };
}
