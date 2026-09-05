export type TimelineMediaPhase =
  | 'missing'
  | 'requested'
  | 'ready'
  | 'loading'
  | 'loaded'
  | 'retry_wait'
  | 'failed_terminal';

export type TimelineMediaLoadState = {
  phase: TimelineMediaPhase;
  retryCount: number;
  epoch: number;
  revision: number;
  errorCode: string | null;
};

export const timelineMediaMaximumRetries = 2;

export const initialTimelineMediaLoadState = (
  available = false,
  revision = 0
): TimelineMediaLoadState => ({
  phase: available ? 'ready' : 'missing',
  retryCount: 0,
  epoch: 0,
  revision,
  errorCode: null
});

export const markTimelineMediaRequested = (
  current: TimelineMediaLoadState
): TimelineMediaLoadState => current.phase === 'missing' || current.phase === 'retry_wait'
  ? { ...current, phase: 'requested' }
  : current;

export const markTimelineMediaReady = (
  current: TimelineMediaLoadState,
  revision: number
): TimelineMediaLoadState => ({
  phase: 'ready',
  retryCount: current.revision === revision ? current.retryCount : 0,
  epoch: current.epoch + 1,
  revision,
  errorCode: null
});

export const markTimelineMediaLoading = (
  current: TimelineMediaLoadState
): TimelineMediaLoadState => current.phase === 'ready'
  ? { ...current, phase: 'loading' }
  : current;

export const markTimelineMediaLoaded = (
  current: TimelineMediaLoadState
): TimelineMediaLoadState => ({ ...current, phase: 'loaded', errorCode: null });

export const markTimelineMediaFailed = (
  current: TimelineMediaLoadState,
  errorCode: string
): TimelineMediaLoadState => {
  if (isTerminalTimelineMediaError(errorCode)) {
    return { ...current, phase: 'failed_terminal', errorCode };
  }
  const retryCount = current.retryCount + 1;
  return {
    ...current,
    phase: retryCount > timelineMediaMaximumRetries ? 'failed_terminal' : 'retry_wait',
    retryCount,
    errorCode
  };
};

export const retryTimelineMedia = (
  current: TimelineMediaLoadState
): TimelineMediaLoadState => current.phase === 'retry_wait'
  ? { ...current, phase: 'requested' }
  : current;

export const timelineMediaRetryDelay = (retryCount: number): number =>
  Math.min(2_000, 250 * 2 ** Math.max(0, retryCount - 1));

export const selectTimelineThumbnailIndices = (
  thumbnailCount: number,
  durationMilliseconds: number,
  visibleStartMilliseconds: number,
  visibleEndMilliseconds: number,
  overscan = 2
): number[] => {
  if (thumbnailCount <= 0 || durationMilliseconds <= 0) return [];
  const count = Math.max(1, Math.floor(thumbnailCount));
  const first = Math.max(
    0,
    Math.floor(visibleStartMilliseconds / durationMilliseconds * count) - overscan
  );
  const last = Math.min(
    count - 1,
    Math.ceil(visibleEndMilliseconds / durationMilliseconds * count) + overscan
  );
  return Array.from({ length: Math.max(0, last - first + 1) }, (_, offset) => first + offset);
};

export const prioritizeTimelineThumbnailIndices = (
  indices: number[],
  thumbnailCount: number,
  durationMilliseconds: number,
  viewportCenterMilliseconds: number
): number[] => [...new Set(indices)].sort((left, right) => {
  const leftTime = durationMilliseconds * (left + 0.5) / thumbnailCount;
  const rightTime = durationMilliseconds * (right + 0.5) / thumbnailCount;
  return Math.abs(leftTime - viewportCenterMilliseconds)
    - Math.abs(rightTime - viewportCenterMilliseconds);
});

export const isTerminalTimelineMediaError = (errorCode: string): boolean => [
  'vietsub_feature_disabled',
  'vietsub_media_context_mismatch',
  'vietsub_media_session_context_mismatch',
  'vietsub_media_project_session_required',
  'vietsub_media_method_invalid',
  'vietsub_media_route_invalid',
  'vietsub_media_source_changed',
  'vietsub_media_reference_invalid'
].includes(errorCode);

export const shouldResetTimelineMediaState = (
  previousMediaId: string | null | undefined,
  nextMediaId: string | null | undefined,
  previousSourceSha256?: string | null,
  nextSourceSha256?: string | null
) => previousMediaId !== nextMediaId
  || previousSourceSha256?.toLowerCase() !== nextSourceSha256?.toLowerCase();
