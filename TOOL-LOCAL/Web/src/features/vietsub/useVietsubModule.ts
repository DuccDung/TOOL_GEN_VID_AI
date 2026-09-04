import { useCallback, useEffect, useRef, useState } from 'react';
import { isHosted, postToHost, subscribeToHost } from '../../bridge';
import type { HostMessage } from '../../types';
import type {
  VietsubModuleState,
  VietsubSubtitleCue,
  VietsubSubtitlePageQuery,
  VietsubTimelineCueUpdate,
  VietsubTimelineWindowQuery
} from './types';

type PendingOperation = {
  resolve: (completed: boolean) => void;
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
  subtitleNotice: null
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
        subtitleNotice: null
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
        };
        setState((current) => {
          const selectedProject = payload.selectedProject ?? null;
          const keepsCurrentEditor = Boolean(
            selectedProject
            && current.selectedProject?.projectId === selectedProject.projectId
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
            subtitlePage: keepsCurrentEditor ? current.subtitlePage : null,
            timelineWindow: keepsCurrentEditor ? current.timelineWindow : null,
            subtitleNotice: keepsCurrentEditor ? current.subtitleNotice : null
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
        setState((current) => {
          const errorCode = message.error?.code ?? 'vietsub_operation_failed';
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
      timelineQueryRef.current = null;
      timelineRequestIdRef.current = null;
      setState(disabledState);
      return;
    }

    if (organizationIdRef.current !== organizationId) {
      organizationIdRef.current = organizationId;
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
        subtitleNotice: null
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
    importSrt,
    activateSubtitleTrack,
    loadSubtitlePage,
    loadTimelineWindow,
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
