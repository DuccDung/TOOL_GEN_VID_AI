// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { HostMessage } from '../../types';
import { useTikTokModule } from './useTikTokModule';

const bridge = vi.hoisted(() => ({
  listeners: new Set<(message: HostMessage) => void>(),
  posts: [] as { type: string; requestId: string }[],
  sequence: 0
}));

vi.mock('../../bridge', () => ({
  postToHost(type: string) {
    const requestId = `tiktok-${++bridge.sequence}`;
    bridge.posts.push({ type, requestId });
    return requestId;
  },
  subscribeToHost(listener: (message: HostMessage) => void) {
    bridge.listeners.add(listener);
    return () => bridge.listeners.delete(listener);
  }
}));

let module: ReturnType<typeof useTikTokModule>;
function Probe() {
  module = useTikTokModule(true);
  return <div data-loading={module.state.loading} data-error={module.state.error ?? ''} />;
}

describe('TikTok state loading', () => {
  let root: Root;
  let container: HTMLDivElement;
  const stateRequests = () => bridge.posts.filter(post => post.type === 'tiktok.state.get');
  const emit = async (message: HostMessage) => {
    await act(async () => bridge.listeners.forEach(listener => listener(message)));
  };

  beforeEach(async () => {
    vi.useFakeTimers();
    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
    bridge.listeners.clear(); bridge.posts = []; bridge.sequence = 0;
    container = document.createElement('div'); document.body.append(container);
    root = createRoot(container);
    await act(async () => root.render(<Probe />));
  });

  afterEach(async () => {
    await act(async () => root.unmount());
    container.remove();
    vi.useRealTimers(); vi.unstubAllGlobals();
  });

  it('releases loading on a Setup gate error for its own request and recovers on retry', async () => {
    const first = stateRequests()[0];
    expect(module.state.loading).toBe(true);
    await emit({ type: 'operation.error', requestId: 'unrelated',
      error: { code: 'system_setup_required', message: 'Setup chưa hoàn tất.' } });
    expect(module.state.loading).toBe(true);

    await emit({ type: 'operation.error', requestId: first.requestId,
      error: { code: 'system_setup_required', message: 'Setup chưa hoàn tất.' } });
    expect(module.state.loading).toBe(false);
    expect(module.state.error).toBe('Setup chưa hoàn tất.');

    await act(async () => module.refresh());
    const retry = stateRequests().at(-1)!;
    expect(retry.requestId).not.toBe(first.requestId);
    await emit({ type: 'tiktok.state', requestId: retry.requestId,
      payload: { enabled: true, configured: false, connections: [], connection: null } });
    expect(module.state.loading).toBe(false);
    expect(module.state.error).toBeNull();
    expect(module.state.feature.enabled).toBe(true);
  });

  it('stops the spinner when the host loses a reply and ignores a late stale reply', async () => {
    const first = stateRequests()[0];
    await act(async () => vi.advanceTimersByTime(34_999));
    expect(stateRequests()).toHaveLength(1);
    expect(module.state.loading).toBe(true);

    await act(async () => vi.advanceTimersByTime(1));
    expect(module.state.loading).toBe(false);
    expect(module.state.error).toContain('Không nhận được phản hồi TikTok');

    await act(async () => module.refresh());
    const retry = stateRequests().at(-1)!;
    expect(retry.requestId).not.toBe(first.requestId);
    await emit({ type: 'tiktok.state', requestId: first.requestId,
      payload: { enabled: true, configured: true, connections: [], connection: null } });
    expect(module.state.feature.configured).toBe(false);

    await emit({ type: 'tiktok.state', requestId: retry.requestId,
      payload: { enabled: true, configured: false, connections: [], connection: null } });
    expect(module.state.error).toBeNull();
    expect(module.state.loading).toBe(false);
  });

  it('shows the server-confirmed result after upload despite an earlier server timestamp', async () => {
    await act(async () => vi.setSystemTime(new Date('2026-09-15T12:00:00Z')));
    const connectionId = 'account-a';
    await emit({ type: 'tiktok.state', requestId: stateRequests()[0].requestId,
      payload: { enabled: true, configured: true, connections: [{ connectionId, creatorUsername: 'creator',
        creatorNickname: 'Creator', scopes: ['video.publish'], accessTokenExpiresAtUtc: '2026-09-16T00:00:00Z',
        refreshTokenExpiresAtUtc: '2026-10-01T00:00:00Z', updatedAtUtc: '2026-09-15T11:00:00Z' }] } });

    await act(async () => module.selectMedia());
    const mediaRequest = bridge.posts.at(-1)!;
    await emit({ type: 'tiktok.media.selected', requestId: mediaRequest.requestId,
      payload: { mediaId: 'media-a', fileName: 'clip.mp4', mimeType: 'video/mp4', sizeBytes: 100,
        durationSeconds: 10, width: 720, height: 1280, framesPerSecond: 30, videoCodec: 'h264', previewUrl: '' } });
    await act(async () => module.publish({ title: 'Clip', privacyLevel: 'SELF_ONLY', allowComment: false,
      allowDuet: false, allowStitch: false, commercialContent: false, brandContent: false,
      brandOrganic: false, isAiGenerated: false, consentConfirmed: true }));
    const publishRequest = bridge.posts.at(-1)!;
    await emit({ type: 'tiktok.publish.initialized', requestId: publishRequest.requestId,
      payload: { publishJobId: 'job-a', connectionId } });
    expect(module.state.publish?.provisional).toBe(true);
    await emit({ type: 'tiktok.upload.completed', requestId: publishRequest.requestId,
      payload: { publishJobId: 'job-a', connectionId } });
    expect(module.state.publishFeedback?.phase).toBe('processing');

    const completed = { publishJobId: 'job-a', connectionId, status: 'PUBLISH_COMPLETE',
      uploadedBytes: 100, publicPostIds: ['post-a'], updatedAtUtc: '2026-09-15T11:30:00Z', isTerminal: true };
    await emit({ type: 'tiktok.state', requestId: stateRequests().at(-1)!.requestId,
      payload: { enabled: true, configured: true, connections: module.state.feature.connections,
        activePublishes: [], recentPublishes: [completed] } });
    expect(module.state.publish?.status).toBe('PUBLISH_COMPLETE');
    expect(module.state.publishFeedback?.phase).toBe('success');
    expect(module.state.publishFeedback?.fileName).toBe('clip.mp4');
  });
});
