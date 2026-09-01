import { useCallback, useEffect, useRef, useState } from 'react';
import { isHosted, postToHost, subscribeToHost } from '../../bridge';
import type { HostMessage } from '../../types';
import type {
  VietsubModuleState,
  VietsubSubtitleCue,
  VietsubSubtitlePageQuery
} from './types';

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
  subtitleNotice: null
};

export function useVietsubModule(featureEnabled: boolean) {
  const [state, setState] = useState<VietsubModuleState>(disabledState);
  const subtitleQueryRef = useRef<VietsubSubtitlePageQuery>(defaultSubtitleQuery);

  const refresh = useCallback(() => {
    if (!featureEnabled) return;
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
        setState((current) => ({
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
          selectedProject: payload.selectedProject ?? null,
          mediaImportProgress: null,
          subtitleWorkspace: payload.subtitleWorkspace ?? null,
          subtitlePage: payload.selectedProject ? current.subtitlePage : null
        }));
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

      if (message.type === 'vietsub.subtitle.changed') {
        const payload = message.payload as { resetPage?: boolean } | undefined;
        const query = {
          ...subtitleQueryRef.current,
          offset: payload?.resetPage ? 0 : subtitleQueryRef.current.offset
        };
        subtitleQueryRef.current = query;
        postToHost('vietsub.subtitle.page.get', query);
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
        setState((current) => ({
          ...current,
          initialized: true,
          loading: false,
          busy: false,
          activeOperationRequestId: null,
          errorCode: message.error?.code ?? 'vietsub_operation_failed',
          errorMessage: message.error?.message ?? 'Không thể tải không gian dịch phụ đề.'
        }));
      }
    });

    return unsubscribe;
  }, []);

  useEffect(() => {
    if (!featureEnabled) {
      setState(disabledState);
      return;
    }

    refresh();
  }, [featureEnabled, refresh]);

  const runProjectOperation = useCallback((type: string, payload?: unknown) => {
    if (!featureEnabled || state.busy) return;
    setState((current) => ({
      ...current,
      loading: true,
      busy: true,
      errorCode: null,
      errorMessage: null,
      subtitleNotice: null
    }));
    postToHost(type, payload);
  }, [featureEnabled, state.busy]);

  const createProject = useCallback((name: string) => {
    runProjectOperation('vietsub.project.create', { name });
  }, [runProjectOperation]);

  const openProject = useCallback((projectId: string) => {
    runProjectOperation('vietsub.project.open', { projectId });
  }, [runProjectOperation]);

  const renameProject = useCallback((projectId: string, name: string) => {
    runProjectOperation('vietsub.project.rename', { projectId, name });
  }, [runProjectOperation]);

  const closeProject = useCallback(() => {
    runProjectOperation('vietsub.project.close');
  }, [runProjectOperation]);

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

  const importSrt = useCallback((languageCode: string) => {
    runProjectOperation('vietsub.subtitle.import', { languageCode });
  }, [runProjectOperation]);

  const activateSubtitleTrack = useCallback((trackId: string) => {
    runProjectOperation('vietsub.subtitle.track.activate', { trackId });
  }, [runProjectOperation]);

  const updateSubtitleCue = useCallback((cue: Pick<
    VietsubSubtitleCue,
    'cueId' | 'originalText' | 'translatedText' | 'speaker'
  >) => {
    runProjectOperation('vietsub.subtitle.cue.update', cue);
  }, [runProjectOperation]);

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
    updateSubtitleCue,
    splitSubtitleCue,
    alignSubtitleCue,
    duplicateSubtitleCue,
    deleteSubtitleCue,
    exportSrt
  };
}
