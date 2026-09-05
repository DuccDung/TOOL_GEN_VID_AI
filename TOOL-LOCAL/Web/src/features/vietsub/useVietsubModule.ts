import { useCallback, useEffect, useRef, useState } from 'react';
import { isHosted, postToHost, subscribeToHost } from '../../bridge';
import type { HostMessage } from '../../types';
import type {
  VietsubModuleState,
  VietsubJobSummary,
  VietsubOcrSettings,
  VietsubSubtitleCue,
  VietsubSubtitlePageQuery,
  VietsubTimelineCueUpdate,
  VietsubTimelineWindowQuery
} from './types';
import {
  VIETSUB_JOB_ERROR_BACKOFF_MS,
  shouldRequestVietsubJobStatus,
  shouldWatchVietsubJob
} from './vietsubJobWatchdog';

type PendingOperation = {
  resolve: (completed: boolean) => void;
};

type JobStatusRequest = {
  requestId: string;
  jobId: string;
  sentAt: number;
};

const defaultSubtitleQuery: VietsubSubtitlePageQuery = {
  trackId: null,
  offset: 0,
  pageSize: 50,
  search: '',
  status: 'ALL',
  speaker: ''
};

const disabledState: VietsubModuleState = {
  enabled: false,
  initialized: false,
  loading: false,
  busy: false,
  activeOperationRequestId: null,
  stage: 'disabled',
  errorCode: null,
  errorMessage: null,
  projects: [],
  selectedProject: null,
  mediaImportProgress: null,
  subtitleWorkspace: null,
  subtitlePage: null,
  timelineWindow: null,
  subtitleNotice: null,
  ocrSettings: {
    languageCode: 'en',
    profile: 'BALANCED',
    region: { x: 0, y: 0.6, width: 1, height: 0.4 }
  },
  ocrRuntime: null,
  ocrPreview: null,
  jobs: [],
  activeJob: null,
  ocrActivationRequest: null,
  timelineMediaEvent: null
};

export function useVietsubModule(featureEnabled: boolean, organizationId: string) {
  const [state, setState] = useState<VietsubModuleState>(disabledState);
  const subtitleQueryRef = useRef<VietsubSubtitlePageQuery>(defaultSubtitleQuery);
  const timelineQueryRef = useRef<VietsubTimelineWindowQuery | null>(null);
  const timelineRequestIdRef = useRef<string | null>(null);
  const pendingOperationsRef = useRef(new Map<string, PendingOperation>());
  const beforeLeaveRef = useRef<(() => Promise<boolean>) | null>(null);
  const busyRef = useRef(false);
  const organizationIdRef = useRef(organizationId);
  const selectedProjectIdRef = useRef<string | null>(null);
  const lastJobUpdateAtRef = useRef(Date.now());
  const jobStatusRequestRef = useRef<JobStatusRequest | null>(null);
  const jobStatusBackoffUntilRef = useRef(0);
  const timelineMediaSequenceRef = useRef(0);

  const refresh = useCallback(() => {
    if (!featureEnabled) return;
    busyRef.current = true;
    setState((current) => ({
      ...current,
      enabled: true,
      loading: true,
      stage: current.initialized ? current.stage : 'loading',
      errorCode: null,
      errorMessage: null
    }));
    if (isHosted) {
      postToHost('vietsub.state.get');
    } else {
      setState({
        enabled: true,
        initialized: true,
        loading: false,
        busy: false,
        activeOperationRequestId: null,
        stage: 'shell_ready',
        errorCode: null,
        errorMessage: null,
        projects: [],
        selectedProject: null,
        mediaImportProgress: null,
        subtitleWorkspace: null,
        subtitlePage: null,
        timelineWindow: null,
        subtitleNotice: null,
        ocrSettings: disabledState.ocrSettings,
        ocrRuntime: null,
        ocrPreview: null,
        jobs: [],
        activeJob: null,
        ocrActivationRequest: null
      });
    }
  }, [featureEnabled]);

  const cancel = useCallback(() => {
    if (!featureEnabled || !state.busy) return;
    postToHost('vietsub.operation.cancel');
  }, [featureEnabled, state.busy]);

  useEffect(() => {
    const unsubscribe = subscribeToHost((message: HostMessage) => {
      if (message.type === 'vietsub.state' && message.payload) {
        const payload = message.payload as {
          enabled?: boolean;
          busy?: boolean;
          activeOperationRequestId?: string | null;
          stage?: string;
          projects?: VietsubModuleState['projects'];
          selectedProject?: VietsubModuleState['selectedProject'];
          subtitleWorkspace?: VietsubModuleState['subtitleWorkspace'];
          ocrSettings?: VietsubModuleState['ocrSettings'];
          jobs?: VietsubModuleState['jobs'];
          activeJob?: VietsubModuleState['activeJob'];
        };
        setState((current) => {
          const selectedProject = payload.selectedProject ?? null;
          selectedProjectIdRef.current = selectedProject?.projectId ?? null;
          const keepsCurrentEditor = Boolean(
            selectedProject
            && current.selectedProject?.projectId === selectedProject.projectId
          );
          const keepsCurrentMedia = Boolean(
            keepsCurrentEditor
            && selectedProject?.sourceVideo?.mediaId
            && selectedProject.sourceVideo.mediaId === current.selectedProject?.sourceVideo?.mediaId
          );
          busyRef.current = payload.busy ?? false;

          return {
            ...current,
            enabled: payload.enabled ?? true,
            initialized: true,
            loading: false,
            busy: payload.busy ?? false,
            activeOperationRequestId: payload.activeOperationRequestId ?? null,
            stage: payload.stage ?? 'shell_ready',
            errorCode: null,
            errorMessage: null,
            projects: payload.projects ?? [],
            selectedProject,
            mediaImportProgress: null,
            subtitleWorkspace: payload.subtitleWorkspace ?? null,
            ocrSettings: payload.ocrSettings ?? current.ocrSettings,
            jobs: payload.jobs ?? [],
            activeJob: payload.activeJob ?? null,
            subtitlePage: keepsCurrentEditor ? current.subtitlePage : null,
            timelineWindow: keepsCurrentEditor ? current.timelineWindow : null,
            subtitleNotice: keepsCurrentEditor ? current.subtitleNotice : null,
            timelineMediaEvent: keepsCurrentMedia ? current.timelineMediaEvent : null
          };
        });
        return;
      }

      if (message.type === 'vietsub.subtitle.page' && message.payload) {
        setState((current) => ({
          ...current,
          subtitlePage: message.payload as VietsubModuleState['subtitlePage'],
          loading: false
        }));
        return;
      }

      if (message.type === 'vietsub.timeline.window' && message.payload) {
        const window = message.payload as NonNullable<VietsubModuleState['timelineWindow']>;
        const query = timelineQueryRef.current;
        if (message.requestId === timelineRequestIdRef.current
          && query
          && query.trackId === window.trackId
          && query.windowStartMilliseconds === window.windowStartMilliseconds
          && query.windowEndMilliseconds === window.windowEndMilliseconds) {
          timelineRequestIdRef.current = null;
          setState((current) => ({ ...current, timelineWindow: window }));
        }
        return;
      }

      if (message.type === 'vietsub.ocr.runtime.status' && message.payload) {
        setState((current) => ({
          ...current,
          ocrRuntime: message.payload as VietsubModuleState['ocrRuntime']
        }));
        return;
      }

      if (message.type === 'vietsub.ocr.settings' && message.payload) {
        setState((current) => ({
          ...current,
          ocrSettings: message.payload as VietsubOcrSettings
        }));
        return;
      }

      if (message.type === 'vietsub.ocr.preview' && message.payload) {
        setState((current) => ({
          ...current,
          ocrPreview: message.payload as VietsubModuleState['ocrPreview']
        }));
        return;
      }

      if ((message.type === 'vietsub.job.changed' || message.type === 'vietsub.job.status') && message.payload) {
        const job = message.payload as VietsubJobSummary;
        if (selectedProjectIdRef.current && job.projectId !== selectedProjectIdRef.current) return;
        lastJobUpdateAtRef.current = Date.now();
        if (jobStatusRequestRef.current?.jobId === job.id) {
          jobStatusRequestRef.current = null;
          jobStatusBackoffUntilRef.current = 0;
        }
        setState((current) => {
          const jobs = [job, ...current.jobs.filter((item) => item.id !== job.id)]
            .sort((left, right) => right.updatedAtUtc.localeCompare(left.updatedAtUtc));
          const activeJob = ['PENDING', 'RUNNING', 'PAUSING', 'PAUSED', 'INTERRUPTED', 'FAILED'].includes(job.status)
            ? job
            : current.activeJob?.id === job.id
              ? null
              : current.activeJob;
          return { ...current, jobs, activeJob };
        });
        return;
      }

      if (message.type === 'vietsub.ocr.activation.required' && message.payload) {
        setState((current) => ({
          ...current,
          ocrActivationRequest: message.payload as NonNullable<VietsubModuleState['ocrActivationRequest']>
        }));
        return;
      }

      if (message.type === 'vietsub.ocr.completed') {
        setState((current) => ({
          ...current,
          ocrActivationRequest: null,
          subtitleNotice: 'OCR đã hoàn thành và track nguồn mới đã được kích hoạt.'
        }));
        postToHost('vietsub.state.get');
        return;
      }

      if (message.type === 'vietsub.subtitle.changed') {
        const payload = message.payload as { resetPage?: boolean } | undefined;
        const query = {
          ...subtitleQueryRef.current,
          offset: payload?.resetPage ? 0 : subtitleQueryRef.current.offset
        };
        subtitleQueryRef.current = query;
        postToHost('vietsub.subtitle.page.get', query);
        if (timelineQueryRef.current) {
          timelineRequestIdRef.current = postToHost(
            'vietsub.timeline.window.get',
            timelineQueryRef.current
          );
        }
        return;
      }

      if (message.type === 'vietsub.operation.completed' && message.requestId) {
        const pending = pendingOperationsRef.current.get(message.requestId);
        if (pending) {
          pendingOperationsRef.current.delete(message.requestId);
          pending.resolve(true);
        }
        return;
      }

      if (message.type === 'vietsub.subtitle.export.completed') {
        const payload = message.payload as { fileName?: string } | undefined;
        setState((current) => ({
          ...current,
          subtitleNotice: payload?.fileName
            ? `Đã xuất ${payload.fileName}`
            : 'Đã xuất phụ đề SRT.'
        }));
        return;
      }

      if (message.type === 'vietsub.media.import.progress' && message.payload) {
        setState((current) => ({
          ...current,
          loading: true,
          busy: true,
          mediaImportProgress: message.payload as VietsubModuleState['mediaImportProgress']
        }));
        return;
      }

      if (message.type === 'vietsub.media.selection.cancelled') {
        setState((current) => ({
          ...current,
          loading: false,
          mediaImportProgress: null
        }));
        return;
      }

      if (message.type === 'vietsub.timeline.thumbnail.ready' && message.payload) {
        const payload = message.payload as {
          mediaId?: string;
          sourceSha256?: string;
          profileVersion?: number;
          index?: number;
          url?: string;
          revision?: number;
          timestampMilliseconds?: number;
          startMilliseconds?: number;
          endMilliseconds?: number;
        };
        setState((current) => {
          const media = current.selectedProject?.sourceVideo;
          if (!media
            || payload.mediaId !== media.mediaId
            || payload.sourceSha256?.toLowerCase() !== media.sha256.toLowerCase()
            || !Number.isInteger(payload.index)
            || !payload.url) return current;
          const thumbnail = {
            index: payload.index!,
            profileVersion: payload.profileVersion ?? media.thumbnailProfileVersion,
            sourceSha256: payload.sourceSha256!,
            url: payload.url,
            revision: payload.revision ?? 0,
            timestampMilliseconds: payload.timestampMilliseconds ?? 0,
            startMilliseconds: payload.startMilliseconds ?? 0,
            endMilliseconds: payload.endMilliseconds ?? 1
          };
          const timelineThumbnails = [
            ...media.timelineThumbnails.filter((item) => item.index !== thumbnail.index),
            thumbnail
          ].sort((left, right) => left.index - right.index);
          const sequence = ++timelineMediaSequenceRef.current;
          return {
            ...current,
            selectedProject: {
              ...current.selectedProject!,
              sourceVideo: { ...media, timelineThumbnails }
            },
            timelineMediaEvent: {
              sequence,
              kind: 'ready',
              resourceType: 'thumbnail',
              mediaId: media.mediaId,
              sourceSha256: media.sha256,
              profileVersion: thumbnail.profileVersion,
              index: thumbnail.index,
              url: thumbnail.url,
              revision: thumbnail.revision
            }
          };
        });
        return;
      }

      if (message.type === 'vietsub.timeline.waveform.ready' && message.payload) {
        const payload = message.payload as {
          mediaId?: string;
          sourceSha256?: string;
          profileVersion?: number;
          status?: 'READY' | 'PENDING' | 'NO_AUDIO' | 'FAILED';
          url?: string | null;
          revision?: number;
        };
        setState((current) => {
          const media = current.selectedProject?.sourceVideo;
          if (!media
            || payload.mediaId !== media.mediaId
            || payload.sourceSha256?.toLowerCase() !== media.sha256.toLowerCase()) return current;
          const sequence = ++timelineMediaSequenceRef.current;
          return {
            ...current,
            selectedProject: {
              ...current.selectedProject!,
              sourceVideo: {
                ...media,
                waveformStatus: payload.status ?? 'READY',
                waveformUrl: payload.url ?? null,
                waveformProfileVersion: payload.profileVersion ?? media.waveformProfileVersion,
                waveformRevision: payload.revision ?? media.waveformRevision
              }
            },
            timelineMediaEvent: {
              sequence,
              kind: 'ready',
              resourceType: 'waveform',
              mediaId: media.mediaId,
              sourceSha256: media.sha256,
              profileVersion: payload.profileVersion,
              url: payload.url,
              revision: payload.revision,
              status: payload.status ?? 'READY'
            }
          };
        });
        return;
      }

      if (message.type === 'vietsub.timeline.thumbnail.failed'
        || message.type === 'vietsub.timeline.waveform.failed'
        || message.type === 'vietsub.media.load.failed') {
        const payload = (message.payload ?? {}) as {
          resourceType?: 'thumbnail' | 'waveform' | 'video' | 'unknown';
          profileVersion?: number | null;
          index?: number | null;
          errorCode?: string | null;
          correlationId?: string | null;
        };
        setState((current) => ({
          ...current,
          timelineMediaEvent: {
            sequence: ++timelineMediaSequenceRef.current,
            kind: 'failed',
            resourceType: payload.resourceType ?? (
              message.type.includes('thumbnail') ? 'thumbnail' : 'waveform'
            ),
            mediaId: current.selectedProject?.sourceVideo?.mediaId ?? null,
            sourceSha256: current.selectedProject?.sourceVideo?.sha256 ?? null,
            profileVersion: payload.profileVersion,
            index: payload.index,
            errorCode: payload.errorCode ?? 'vietsub_media_unknown_error',
            correlationId: payload.correlationId
          }
        }));
        return;
      }

      if (message.type === 'vietsub.thumbnail.ready' || message.type === 'vietsub.waveform.ready') {
        postToHost('vietsub.state.get');
        return;
      }


      if (message.type === 'vietsub.subtitle.selection.cancelled') {
        setState((current) => ({
          ...current,
          loading: false,
          subtitleNotice: null
        }));
        return;
      }

      if (message.type === 'vietsub.error') {
        let failedPendingOperation = false;
        const failedJobStatusRequest = message.requestId === jobStatusRequestRef.current?.requestId;
        if (failedJobStatusRequest) {
          jobStatusRequestRef.current = null;
          jobStatusBackoffUntilRef.current = Date.now() + VIETSUB_JOB_ERROR_BACKOFF_MS;
        }
        if (message.requestId === timelineRequestIdRef.current) {
          timelineRequestIdRef.current = null;
          timelineQueryRef.current = null;
        }
        if (message.requestId) {
          const pending = pendingOperationsRef.current.get(message.requestId);
          if (pending) {
            pendingOperationsRef.current.delete(message.requestId);
            pending.resolve(false);
            failedPendingOperation = true;
          }
        }
        const errorCode = message.error?.code ?? 'vietsub_operation_failed';
        if (failedJobStatusRequest
          && !['OCR_ACCESS_DENIED', 'OCR_LICENSE_REQUIRED', 'vietsub_job_not_found'].includes(errorCode)) {
          return;
        }
        setState((current) => {
          const invalidatesEditor = errorCode === 'vietsub_project_not_found'
            || errorCode === 'vietsub_access_denied';
          const belongsToDifferentOperation = Boolean(
            message.requestId
            && current.activeOperationRequestId
            && current.activeOperationRequestId !== message.requestId
            && !failedPendingOperation
          );
          if (!belongsToDifferentOperation) busyRef.current = false;

          return {
            ...current,
            initialized: true,
            loading: belongsToDifferentOperation ? current.loading : false,
            busy: belongsToDifferentOperation ? current.busy : false,
            activeOperationRequestId: belongsToDifferentOperation
              ? current.activeOperationRequestId
              : null,
            errorCode,
            errorMessage: message.error?.message ?? 'Không thể tải không gian dịch phụ đề.',
            selectedProject: invalidatesEditor ? null : current.selectedProject,
            subtitleWorkspace: invalidatesEditor ? null : current.subtitleWorkspace,
            subtitlePage: invalidatesEditor ? null : current.subtitlePage,
            timelineWindow: invalidatesEditor ? null : current.timelineWindow,
            subtitleNotice: invalidatesEditor ? null : current.subtitleNotice
          };
        });
      }
    });

    return () => {
      unsubscribe();
      for (const pending of pendingOperationsRef.current.values()) pending.resolve(false);
      pendingOperationsRef.current.clear();
    };
  }, []);

  useEffect(() => {
    if (!featureEnabled) {
      busyRef.current = false;
      selectedProjectIdRef.current = null;
      jobStatusRequestRef.current = null;
      timelineQueryRef.current = null;
      timelineRequestIdRef.current = null;
      setState(disabledState);
      return;
    }

    if (organizationIdRef.current !== organizationId) {
      organizationIdRef.current = organizationId;
      selectedProjectIdRef.current = null;
      jobStatusRequestRef.current = null;
      subtitleQueryRef.current = defaultSubtitleQuery;
      timelineQueryRef.current = null;
      timelineRequestIdRef.current = null;
      setState((current) => ({
        ...current,
        selectedProject: null,
        mediaImportProgress: null,
        subtitleWorkspace: null,
        subtitlePage: null,
        timelineWindow: null,
        subtitleNotice: null,
        ocrSettings: disabledState.ocrSettings,
        ocrPreview: null,
        jobs: [],
        activeJob: null,
        ocrActivationRequest: null,
        timelineMediaEvent: null
      }));
    }

    refresh();
  }, [featureEnabled, organizationId, refresh]);

  const runProjectOperation = useCallback((type: string, payload?: unknown) => {
    if (!featureEnabled || busyRef.current) return;
    busyRef.current = true;
    setState((current) => ({
      ...current,
      loading: true,
      busy: true,
      errorCode: null,
      errorMessage: null,
      subtitleNotice: null
    }));
    postToHost(type, payload);
  }, [featureEnabled]);

  const runAwaitableOperation = useCallback((type: string, payload?: unknown): Promise<boolean> => {
    if (!featureEnabled || busyRef.current) return Promise.resolve(false);
    busyRef.current = true;
    setState((current) => ({
      ...current,
      loading: true,
      busy: true,
      errorCode: null,
      errorMessage: null,
      subtitleNotice: null
    }));
    if (!isHosted) {
      busyRef.current = false;
      setState((current) => ({ ...current, loading: false, busy: false }));
      return Promise.resolve(true);
    }
    const requestId = postToHost(type, payload);
    return new Promise<boolean>((resolve) => {
      pendingOperationsRef.current.set(requestId, { resolve });
    });
  }, [featureEnabled]);

  const createProject = useCallback((name: string) => {
    runProjectOperation('vietsub.project.create', { name });
  }, [runProjectOperation]);

  const openProject = useCallback((projectId: string) => {
    runProjectOperation('vietsub.project.open', { projectId });
  }, [runProjectOperation]);

  const renameProject = useCallback((projectId: string, name: string) => {
    runProjectOperation('vietsub.project.rename', { projectId, name });
  }, [runProjectOperation]);

  const closeProject = useCallback(
    () => runAwaitableOperation('vietsub.project.close'),
    [runAwaitableOperation]
  );

  const importMedia = useCallback((mode: 'COPY' | 'LINK') => {
    runProjectOperation('vietsub.media.import', { mode });
  }, [runProjectOperation]);

  const updateOcrSettings = useCallback(
    (settings: VietsubOcrSettings) => runAwaitableOperation('vietsub.ocr.region.update', settings),
    [runAwaitableOperation]
  );

  const previewOcr = useCallback((settings: VietsubOcrSettings, timestampMilliseconds: number) => {
    runProjectOperation('vietsub.ocr.preview', { ...settings, timestampMilliseconds });
  }, [runProjectOperation]);

  const startOcr = useCallback((settings: VietsubOcrSettings) => {
    runProjectOperation('vietsub.job.ocr', settings);
  }, [runProjectOperation]);

  const pauseJob = useCallback((jobId: string) => {
    postToHost('vietsub.job.pause', { jobId });
  }, []);

  const resumeJob = useCallback((jobId: string) => {
    postToHost('vietsub.job.resume', { jobId });
  }, []);

  const retryJob = useCallback((jobId: string) => {
    postToHost('vietsub.job.retry', { jobId });
  }, []);

  const cancelJob = useCallback((jobId: string) => {
    postToHost('vietsub.job.cancel', { jobId });
  }, []);

  const activateOcrTrack = useCallback((jobId: string, confirmImpact: boolean) => {
    postToHost('vietsub.ocr.track.activate', { jobId, confirmImpact });
  }, []);

  const loadSubtitlePage = useCallback((query: VietsubSubtitlePageQuery) => {
    if (!featureEnabled || !state.selectedProject) return;
    subtitleQueryRef.current = query;
    postToHost('vietsub.subtitle.page.get', query);
  }, [featureEnabled, state.selectedProject]);

  useEffect(() => {
    const activeTrackId = state.subtitleWorkspace?.activeTrackId;
    if (!featureEnabled || !state.selectedProject || !activeTrackId) {
      return;
    }
    if (state.subtitlePage?.trackId === activeTrackId) {
      return;
    }
    const query = { ...defaultSubtitleQuery, trackId: activeTrackId };
    subtitleQueryRef.current = query;
    postToHost('vietsub.subtitle.page.get', query);
  }, [
    featureEnabled,
    state.selectedProject?.projectId,
    state.subtitleWorkspace?.activeTrackId,
    state.subtitlePage?.trackId
  ]);

  useEffect(() => {
    subtitleQueryRef.current = defaultSubtitleQuery;
    timelineQueryRef.current = null;
    timelineRequestIdRef.current = null;
  }, [state.selectedProject?.projectId]);

  useEffect(() => {
    if (!featureEnabled) return;
    postToHost('vietsub.ocr.runtime.status');
  }, [featureEnabled, state.selectedProject?.projectId]);

  useEffect(() => {
    const activeJob = state.activeJob;
    const projectId = state.selectedProject?.projectId;
    if (!featureEnabled
      || !isHosted
      || !activeJob
      || activeJob.projectId !== projectId
      || !shouldWatchVietsubJob(activeJob.status)) {
      jobStatusRequestRef.current = null;
      return;
    }

    lastJobUpdateAtRef.current = Date.now();
    const timer = window.setInterval(() => {
      const now = Date.now();
      const pendingRequest = jobStatusRequestRef.current;
      if (!shouldRequestVietsubJobStatus(
        now,
        lastJobUpdateAtRef.current,
        pendingRequest?.sentAt ?? null,
        jobStatusBackoffUntilRef.current
      )) return;

      const requestId = postToHost('vietsub.job.status', { jobId: activeJob.id });
      jobStatusRequestRef.current = {
        requestId,
        jobId: activeJob.id,
        sentAt: now
      };
    }, 1_000);

    return () => {
      window.clearInterval(timer);
      if (jobStatusRequestRef.current?.jobId === activeJob.id) {
        jobStatusRequestRef.current = null;
      }
    };
  }, [
    featureEnabled,
    state.selectedProject?.projectId,
    state.activeJob?.id,
    state.activeJob?.projectId,
    state.activeJob?.status
  ]);

  const importSrt = useCallback((languageCode: string) => {
    runProjectOperation('vietsub.subtitle.import', { languageCode });
  }, [runProjectOperation]);

  const activateSubtitleTrack = useCallback((trackId: string) => {
    runProjectOperation('vietsub.subtitle.track.activate', { trackId });
  }, [runProjectOperation]);

  const updateSubtitleCue = useCallback((cue: Pick<
    VietsubSubtitleCue,
    'cueId' | 'originalText' | 'translatedText' | 'speaker'
  >) => runAwaitableOperation('vietsub.subtitle.cue.update', cue), [runAwaitableOperation]);

  const loadTimelineWindow = useCallback((query: VietsubTimelineWindowQuery) => {
    if (!featureEnabled || !state.selectedProject) return;
    const normalized = {
      ...query,
      windowStartMilliseconds: Math.max(0, Math.round(query.windowStartMilliseconds)),
      windowEndMilliseconds: Math.max(1, Math.round(query.windowEndMilliseconds)),
      maximumCues: Math.max(1, Math.min(500, Math.round(query.maximumCues)))
    };
    const previous = timelineQueryRef.current;
    timelineQueryRef.current = normalized;
    if (previous
      && previous.trackId === normalized.trackId
      && previous.windowStartMilliseconds === normalized.windowStartMilliseconds
      && previous.windowEndMilliseconds === normalized.windowEndMilliseconds
      && previous.maximumCues === normalized.maximumCues) return;
    timelineRequestIdRef.current = postToHost('vietsub.timeline.window.get', normalized);
  }, [featureEnabled, state.selectedProject]);

  const requestTimelineThumbnails = useCallback((sourceSha256: string, indices: number[]) => {
    const media = state.selectedProject?.sourceVideo;
    if (!featureEnabled
      || !media
      || media.sha256.toLowerCase() !== sourceSha256.toLowerCase()) return;
    const normalized = [...new Set(indices)]
      .filter((index) => Number.isInteger(index) && index >= 0 && index < media.thumbnailCount)
      .slice(0, 64);
    if (normalized.length === 0) return;
    postToHost('vietsub.timeline.thumbnails.request', { sourceSha256, indices: normalized });
  }, [featureEnabled, state.selectedProject]);

  const requestTimelineWaveform = useCallback((sourceSha256: string) => {
    const media = state.selectedProject?.sourceVideo;
    if (!featureEnabled
      || !media
      || media.sha256.toLowerCase() !== sourceSha256.toLowerCase()) return;
    postToHost('vietsub.timeline.waveform.request', { sourceSha256 });
  }, [featureEnabled, state.selectedProject]);

  const updateTimelineCue = useCallback(
    (update: VietsubTimelineCueUpdate) => runAwaitableOperation('vietsub.timeline.cue.update', update),
    [runAwaitableOperation]
  );

  const registerBeforeLeave = useCallback((handler: () => Promise<boolean>) => {
    beforeLeaveRef.current = handler;
    return () => {
      if (beforeLeaveRef.current === handler) beforeLeaveRef.current = null;
    };
  }, []);

  const prepareToLeaveEditor = useCallback(
    () => beforeLeaveRef.current?.() ?? Promise.resolve(true),
    []
  );

  const splitSubtitleCue = useCallback((cueId: string, positionMilliseconds: number) => {
    runProjectOperation('vietsub.subtitle.cue.split', { cueId, positionMilliseconds });
  }, [runProjectOperation]);

  const alignSubtitleCue = useCallback((cueId: string, positionMilliseconds: number) => {
    runProjectOperation('vietsub.subtitle.cue.align-start', { cueId, positionMilliseconds });
  }, [runProjectOperation]);

  const duplicateSubtitleCue = useCallback((cueId: string) => {
    runProjectOperation('vietsub.subtitle.cue.duplicate', { cueId });
  }, [runProjectOperation]);

  const deleteSubtitleCue = useCallback((cueId: string) => {
    runProjectOperation('vietsub.subtitle.cue.delete', { cueId });
  }, [runProjectOperation]);

  const exportSrt = useCallback((mode: 'ORIGINAL' | 'TRANSLATED') => {
    runProjectOperation('vietsub.subtitle.export', { mode });
  }, [runProjectOperation]);

  return {
    state,
    refresh,
    cancel,
    createProject,
    openProject,
    renameProject,
    closeProject,
    importMedia,
    updateOcrSettings,
    previewOcr,
    startOcr,
    pauseJob,
    resumeJob,
    retryJob,
    cancelJob,
    activateOcrTrack,
    importSrt,
    activateSubtitleTrack,
    loadSubtitlePage,
    loadTimelineWindow,
    requestTimelineThumbnails,
    requestTimelineWaveform,
    updateSubtitleCue,
    updateTimelineCue,
    splitSubtitleCue,
    alignSubtitleCue,
    duplicateSubtitleCue,
    deleteSubtitleCue,
    exportSrt,
    registerBeforeLeave,
    prepareToLeaveEditor
  };
}
