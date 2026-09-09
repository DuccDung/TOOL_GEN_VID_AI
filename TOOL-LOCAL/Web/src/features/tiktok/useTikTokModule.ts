import { useCallback, useEffect, useRef, useState } from 'react';
import { postToHost, subscribeToHost } from '../../bridge';
import type { HostMessage } from '../../types';
import type {
  TikTokCreatorInfo,
  TikTokFeatureState,
  TikTokMedia,
  TikTokModuleState,
  TikTokPublishPayload,
  TikTokPublishStatus,
  TikTokUploadProgress
} from './types';
import { formatTikTokFailure } from './tiktokFailure';

const initialState: TikTokModuleState = {
  feature: { enabled: false, configured: false, connection: null },
  creator: null,
  media: null,
  upload: null,
  publish: null,
  loading: false,
  busy: false,
  uploadCompleted: false,
  error: null,
  publishFeedback: null
};

export function useTikTokModule(enabled: boolean) {
  const creatorRequestId = useRef<string | null>(null);
  const publishRequestId = useRef<string | null>(null);
  const requestCreator = useCallback(() => {
    if (creatorRequestId.current) return;
    creatorRequestId.current = postToHost('tiktok.creator.get');
  }, []);
  const [state, setState] = useState<TikTokModuleState>({
    ...initialState,
    feature: { ...initialState.feature, enabled }
  });

  useEffect(() => {
    const unsubscribe = subscribeToHost((message: HostMessage) => {
      if (!message.type.startsWith('tiktok.')) return;
      if (message.type === 'tiktok.state' && message.payload) {
        const feature = message.payload as TikTokFeatureState;
        setState((current) => ({
          ...current,
          feature,
          creator: feature.connection ? current.creator : null,
          media: feature.connection ? current.media : null,
          publish: feature.connection ? feature.activePublish ?? current.publish : null,
          publishFeedback: !feature.connection ? null
            : feature.activePublish && feature.activePublish.publishJobId !== current.publish?.publishJobId
              ? feedbackForStatus(feature.activePublish, false, true) : current.publishFeedback,
          loading: false,
          busy: false,
          error: null
        }));
        return;
      }
      if (message.type === 'tiktok.creator' && message.payload) {
        const creator = message.payload as TikTokCreatorInfo;
        if (message.requestId === creatorRequestId.current) creatorRequestId.current = null;
        setState((current) => ({
          ...current,
          creator,
          publishFeedback: message.requestId === publishRequestId.current && creator.publishingIssue
            ? { phase: 'error', open: true, message: creator.publishingIssue.message } : current.publishFeedback,
          loading: false,
          busy: false,
          error: null
        }));
        return;
      }
      if (message.type === 'tiktok.media.selected' && message.payload) {
        setState((current) => ({
          ...current,
          media: message.payload as TikTokMedia,
          publish: null,
          upload: null,
          uploadCompleted: false,
          publishFeedback: null,
          busy: false,
          error: null
        }));
        return;
      }
      if (message.type === 'tiktok.media.cancelled') {
        setState((current) => ({ ...current, busy: false }));
        return;
      }
      if (message.type === 'tiktok.media.cleared') {
        setState((current) => ({ ...current, media: null, upload: null, publish: null, uploadCompleted: false, publishFeedback: null, busy: false }));
        return;
      }
      if (message.type === 'tiktok.publish.initialized' && message.payload) {
        const publishJobId = String((message.payload as { publishJobId: string }).publishJobId);
        setState((current) => ({
          ...current,
          publishFeedback: current.publishFeedback?.phase === 'cancelled' ? current.publishFeedback
            : { phase: 'uploading', open: current.publishFeedback?.open ?? true },
          publish: {
            publishJobId,
            status: 'PROCESSING_UPLOAD',
            uploadedBytes: 0,
            publicPostIds: [],
            updatedAtUtc: new Date().toISOString(),
            isTerminal: false
          }
        }));
        return;
      }
      if (message.type === 'tiktok.upload.progress' && message.payload) {
        setState((current) => ({ ...current, upload: message.payload as TikTokUploadProgress }));
        return;
      }
      if (message.type === 'tiktok.upload.completed') {
        setState((current) => ({ ...current, uploadCompleted: true,
          publishFeedback: { phase: 'processing', open: current.publishFeedback?.open ?? true } }));
        return;
      }
      if (message.type === 'tiktok.publish.status' && message.payload) {
        const publish = message.payload as TikTokPublishStatus;
        setState((current) => ({
          ...current,
          publish,
          publishFeedback: !publish.isTerminal && (current.publishFeedback?.phase === 'error' || current.publishFeedback?.phase === 'cancelled')
            ? current.publishFeedback : feedbackForStatus(publish, current.uploadCompleted, current.publishFeedback?.open ?? true),
          busy: !publish.isTerminal && !current.uploadCompleted,
          error: publish.status === 'FAILED'
            ? formatTikTokFailure(publish.failureReason)
            : !publish.isTerminal && current.publishFeedback?.phase === 'error' ? current.error : null
        }));
        return;
      }
      if (message.type === 'tiktok.operation.cancelled') {
        setState((current) => ({ ...current, busy: false,
          publishFeedback: current.publishFeedback && (current.publishFeedback.phase === 'preparing' || current.publishFeedback.phase === 'uploading')
            ? { phase: 'cancelled', open: true } : current.publishFeedback }));
        return;
      }
      if (message.type === 'tiktok.error') {
        if (message.requestId === creatorRequestId.current) creatorRequestId.current = null;
        setState((current) => ({
          ...current,
          loading: false,
          busy: false,
          error: message.error?.message ?? 'Không thể hoàn tất thao tác TikTok.',
          publishFeedback: message.requestId === publishRequestId.current
            ? { phase: message.error?.code === 'tiktok_operation_cancelled' ? 'cancelled' : 'error',
                open: true, message: message.error?.message ?? 'Không thể hoàn tất thao tác TikTok.' }
            : current.publishFeedback
        }));
      }
    });
    return () => { creatorRequestId.current = null; publishRequestId.current = null; unsubscribe(); };
  }, []);

  useEffect(() => {
    if (!enabled) {
      creatorRequestId.current = null;
      publishRequestId.current = null;
      setState((current) => ({ ...initialState, feature: { ...current.feature, enabled: false } }));
      return;
    }
    setState((current) => ({ ...current, loading: true, feature: { ...current.feature, enabled: true } }));
    postToHost('tiktok.state.get');
  }, [enabled]);

  useEffect(() => {
    if (!enabled || !state.publish || state.publish.isTerminal) return;
    const timer = window.setInterval(() => {
      postToHost('tiktok.publish.status.get', { publishJobId: state.publish?.publishJobId });
    }, 5000);
    return () => window.clearInterval(timer);
  }, [enabled, state.publish?.publishJobId, state.publish?.isTerminal]);

  const begin = (type: string, payload?: unknown) => {
    if (state.busy) return;
    setState((current) => ({ ...current, busy: true, error: null }));
    postToHost(type, payload);
  };

  return {
    state,
    refresh: () => {
      setState((current) => ({ ...current, loading: true, error: null }));
      postToHost('tiktok.refresh');
      if (state.feature.connection) requestCreator();
    },
    refreshCreator: requestCreator,
    connect: () => begin('tiktok.oauth.connect'),
    disconnect: () => begin('tiktok.oauth.disconnect'),
    selectMedia: () => begin('tiktok.media.select'),
    clearMedia: () => begin('tiktok.media.clear'),
    publish: (payload: TikTokPublishPayload) => {
      if (state.busy || (state.publish && !state.publish.isTerminal)) return;
      setState((current) => ({ ...current, busy: true, error: null, publish: null, upload: null,
        uploadCompleted: false, publishFeedback: { phase: 'preparing', open: true } }));
      publishRequestId.current = postToHost('tiktok.publish.start', payload);
    },
    hidePublishFeedback: () => setState((current) => ({ ...current,
      publishFeedback: current.publishFeedback ? { ...current.publishFeedback, open: false } : null })),
    showPublishFeedback: () => setState((current) => ({ ...current,
      publishFeedback: current.publishFeedback ? { ...current.publishFeedback, open: true } : null })),
    cancel: () => postToHost('tiktok.operation.cancel'),
    clearError: () => setState((current) => ({ ...current, error: null })),
    openPolicy: (policy: 'music' | 'branded') => postToHost('tiktok.policy.open', { policy })
  };
}

function feedbackForStatus(publish: TikTokPublishStatus, uploaded: boolean, open: boolean): NonNullable<TikTokModuleState['publishFeedback']> {
  if (publish.status === 'PUBLISH_COMPLETE') return { phase: 'success', open };
  if (publish.status === 'FAILED') return { phase: 'error', open, message: formatTikTokFailure(publish.failureReason) };
  return { phase: uploaded || publish.status !== 'PROCESSING_UPLOAD' ? 'processing' : 'uploading', open };
}
