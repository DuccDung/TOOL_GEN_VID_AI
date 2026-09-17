import { useCallback, useEffect, useRef, useState } from 'react';
import type { SetStateAction } from 'react';
import { noticeEvent, trackNoticeChanges } from './vietsubNoticeEvents';
import { isHosted, postToHost, subscribeToHost } from '../../bridge';
import type { HostMessage, VietsubVideoExportProgress } from '../../types';
import type {
  VietsubModuleState,
  VietsubJobSummary,
  VietsubOcrSettings,
  VietsubAudioMixSettings,
  VietsubVideoTransformSettings,
  VietsubSubtitleCueUpdate,
  VietsubSubtitleStyle,
  VietsubSubtitlePageQuery,
  VietsubTimelineCueUpdate,
  VietsubTimelineWindowQuery
} from './types';
import { defaultVietsubSubtitleStyle } from './vietsubSubtitleStyle';
import { defaultVietsubAudioMixSettings } from './vietsubAudioMix';
import { defaultVietsubVideoTransformSettings } from './vietsubVideoTransform';
import {
  VIETSUB_JOB_ERROR_BACKOFF_MS,
  shouldRequestVietsubJobStatus,
  shouldWatchVietsubJob
} from './vietsubJobWatchdog';
import {
  createVietsubTranslationStartPayload,
  createVietsubTranslationResourceAlert,
  reduceVietsubTranslationInstallProgress,
  VIETSUB_TRANSLATION_OCR_REQUIRED_MESSAGE,
  type VietsubTranslationRunMode
} from './vietsubTranslation';

type PendingOperation = {
  resolve: (completed: boolean) => void;
};

type JobStatusRequest = {
  requestId: string;
  jobId: string;
  sentAt: number;
};

type TranslationResourceAction = {
  action: 'TRANSLATE' | 'INSTALL';
  runMode?: VietsubTranslationRunMode;
  executionPolicy?: import('../../types').VietsubTranslationExecutionPolicy;
  installAcceleration?: boolean;
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
  subtitleStyle: defaultVietsubSubtitleStyle,
  audioMixSettings: defaultVietsubAudioMixSettings,
  videoTransformSettings: defaultVietsubVideoTransformSettings,
  subtitleNotice: null,
  translationNotice: null,
  translationResourceAlert: null,
  ocrSettings: {
    languageCode: 'en',
    profile: 'BALANCED',
    region: { x: 0, y: 0.6, width: 1, height: 0.4 }
  },
  ocrRuntime: null,
  ocrPreview: null,
  translationRuntime: null,
  translationInstallProgress: null,
  voiceWorkspace: null,
  voiceRuntime: null,
  voiceInstallProgress: null,
  voiceModels: null,
  voiceModelInstallProgress: null,
  voiceNotice: null,
  jobs: [],
  activeJob: null,
  ocrActivationRequest: null,
  timelineMediaEvent: null
};

export function useVietsubModule(featureEnabled: boolean, organizationId: string) {
  const [state, setRawState] = useState<VietsubModuleState>(disabledState);
  const setState = useCallback((update: SetStateAction<VietsubModuleState>) => {
    setRawState(current => trackNoticeChanges(current, typeof update === 'function' ? update(current) : update));
  }, []);
  const subtitleQueryRef = useRef<VietsubSubtitlePageQuery>(defaultSubtitleQuery);
  const subtitlePageRequestRef = useRef<string | null>(null);
  const timelineQueryRef = useRef<VietsubTimelineWindowQuery | null>(null);
  const timelineRequestIdRef = useRef<string | null>(null);
  const pendingOperationsRef = useRef(new Map<string, PendingOperation>());
  const exportRequestIdRef = useRef<string | null>(null);
  const beforeLeaveRef = useRef<(() => Promise<boolean>) | null>(null);
  const busyRef = useRef(false);
  const organizationIdRef = useRef(organizationId);
  const selectedProjectIdRef = useRef<string | null>(null);
  const lastJobUpdateAtRef = useRef(Date.now());
  const jobStatusRequestRef = useRef<JobStatusRequest | null>(null);
  const jobStatusBackoffUntilRef = useRef(0);
  const timelineMediaSequenceRef = useRef(0);
  const translationResourceActionRef = useRef<TranslationResourceAction | null>(null);
  const translationRuntimeStatusRef = useRef<VietsubModuleState['translationRuntime']>(null);
  const cloudWorkspaceRef = useRef(state.subtitleWorkspace);
  const cloudSubmittingRef = useRef(false);
  const cloudAvailabilityRequestRef = useRef<string | null>(null);
  cloudWorkspaceRef.current = state.subtitleWorkspace;

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
        subtitleStyle: defaultVietsubSubtitleStyle,
        audioMixSettings: defaultVietsubAudioMixSettings,
        videoTransformSettings: defaultVietsubVideoTransformSettings,
        subtitleNotice: null,
        translationNotice: null,
        translationResourceAlert: null,
        ocrSettings: disabledState.ocrSettings,
        ocrRuntime: null,
        ocrPreview: null,
        translationRuntime: null,
        translationInstallProgress: null,
        voiceWorkspace: null,
        voiceRuntime: null,
        voiceInstallProgress: null,
        voiceModels: null,
        voiceModelInstallProgress: null,
        voiceNotice: null,
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
          subtitleStyle?: VietsubModuleState['subtitleStyle'];
          audioMixSettings?: VietsubModuleState['audioMixSettings'];
          videoTransformSettings?: VietsubModuleState['videoTransformSettings'];
          voiceWorkspace?: VietsubModuleState['voiceWorkspace'];
          voiceRuntime?: VietsubModuleState['voiceRuntime'];
          ocrSettings?: VietsubModuleState['ocrSettings'];
          jobs?: VietsubModuleState['jobs'];
          activeJob?: VietsubModuleState['activeJob'];
        };
        cloudWorkspaceRef.current = payload.subtitleWorkspace ?? null;
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
          if (!keepsCurrentEditor) {
            exportRequestIdRef.current = null;
            subtitlePageRequestRef.current = null;
            subtitleQueryRef.current = defaultSubtitleQuery;
            translationResourceActionRef.current = null;
            translationRuntimeStatusRef.current = null;
          }
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
            subtitleStyle: payload.subtitleStyle ?? (
              keepsCurrentEditor ? current.subtitleStyle : defaultVietsubSubtitleStyle
            ),
            audioMixSettings: payload.audioMixSettings ?? (
              keepsCurrentEditor ? current.audioMixSettings : defaultVietsubAudioMixSettings
            ),
            videoTransformSettings: payload.videoTransformSettings ?? (
              keepsCurrentEditor ? current.videoTransformSettings : defaultVietsubVideoTransformSettings
            ),
            voiceWorkspace: payload.voiceWorkspace ?? null,
            voiceRuntime: payload.voiceRuntime ?? current.voiceRuntime,
            voiceModels: keepsCurrentEditor ? current.voiceModels : null,
            voiceModelInstallProgress: keepsCurrentEditor ? current.voiceModelInstallProgress : null,
            ocrSettings: payload.ocrSettings ?? current.ocrSettings,
            jobs: payload.jobs ?? [],
            activeJob: payload.activeJob ?? null,
            subtitlePage: keepsCurrentEditor ? current.subtitlePage : null,
            timelineWindow: keepsCurrentEditor ? current.timelineWindow : null,
            subtitleNotice: keepsCurrentEditor ? current.subtitleNotice : null,
            translationNotice: keepsCurrentEditor ? current.translationNotice : null,
            cloudAvailability: keepsCurrentEditor ? current.cloudAvailability : null,
            translationResourceAlert: keepsCurrentEditor ? current.translationResourceAlert : null,
            translationGpuFallbackAlert: keepsCurrentEditor ? current.translationGpuFallbackAlert : null,
            voiceNotice: keepsCurrentEditor ? current.voiceNotice : null,
            timelineMediaEvent: keepsCurrentMedia ? current.timelineMediaEvent : null
          };
        });
        return;
      }

      if (message.type === 'vietsub.subtitle.page' && message.payload) {
        if (!selectedProjectIdRef.current || message.requestId !== subtitlePageRequestRef.current) return;
        setState((current) => ({
          ...current,
          subtitlePage: message.payload as VietsubModuleState['subtitlePage'],
          loading: false
        }));
        return;
      }

      if ((message.type === 'vietsub.subtitle.style'
        || message.type === 'vietsub.subtitle.style.updated') && message.payload) {
        setState((current) => ({
          ...current,
          subtitleStyle: message.payload as VietsubSubtitleStyle
        }));
        return;
      }

      if (message.type === 'vietsub.audio.mix.updated' && message.payload) {
        setState((current) => ({
          ...current,
          audioMixSettings: message.payload as VietsubAudioMixSettings
        }));
        return;
      }

      if (message.type === 'vietsub.video.transform.updated' && message.payload) {
        setState((current) => ({
          ...current,
          videoTransformSettings: message.payload as VietsubVideoTransformSettings
        }));
        return;
      }

      if (message.type === 'vietsub.video.export.started') {
        if (!message.requestId || !pendingOperationsRef.current.has(message.requestId)) return;
        exportRequestIdRef.current = message.requestId;
        setState((current) => ({
          ...current,
          subtitleNotice: 'Đang kết xuất MP4 với phụ đề và cấu hình âm thanh hiện tại…'
        }));
        return;
      }

      if (message.type === 'vietsub.video.export.progress' && message.payload) {
        if (!message.requestId || message.requestId !== exportRequestIdRef.current
          || !pendingOperationsRef.current.has(message.requestId) || !selectedProjectIdRef.current) return;
        const payload = message.payload as VietsubVideoExportProgress;
        if (!Number.isFinite(payload.percent) || !['PREPARE', 'RENDER', 'VERIFY', 'COMPLETED'].includes(payload.stage)) return;
        const percent = Math.floor(Math.max(0, Math.min(100, payload.percent)));
        const label = payload.stage === 'VERIFY' ? 'Đang kiểm tra video thành phẩm'
          : payload.stage === 'PREPARE' ? 'Đang chuẩn bị xuất video' : 'Đang kết xuất MP4';
        setState(current => ({ ...current, subtitleNotice: `${label}… ${percent}%` }));
        return;
      }

      if (message.type === 'vietsub.video.export.completed' && message.payload) {
        if (message.requestId !== exportRequestIdRef.current) return;
        exportRequestIdRef.current = null;
        const payload = message.payload as { fileName?: string };
        setState((current) => ({
          ...current,
          subtitleNotice: `Đã xuất video thành công${payload.fileName ? `: ${payload.fileName}` : '.'}`
        }));
        return;
      }

      if (message.type === 'vietsub.video.export.cancelled') {
        if (!message.requestId || !pendingOperationsRef.current.has(message.requestId)
          || (exportRequestIdRef.current !== null && exportRequestIdRef.current !== message.requestId)) return;
        exportRequestIdRef.current = null;
        setState((current) => ({ ...current, subtitleNotice: null }));
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

      if (message.type === 'vietsub.translation.execution' && message.payload) {
        const execution = message.payload as import('../../types').VietsubTranslationExecutionStatus;
        if (execution.projectId !== selectedProjectIdRef.current || execution.organizationId !== organizationIdRef.current) return;
        setState(current => {
          if (current.activeJob?.id !== execution.jobId || current.activeJob.type !== 'TRANSLATE_LOCAL') return current;
          const showFallback = execution.cpuFallback === true && Boolean(execution.fallbackMessage)
            && current.translationGpuFallbackAlert?.jobId !== execution.jobId;
          return { ...current,
            translationRuntime: current.translationRuntime ? {
              ...current.translationRuntime, effectiveBackend: execution.effectiveBackend,
              deviceName: execution.deviceName, fallbackMessage: execution.fallbackMessage
            } : null,
            translationGpuFallbackAlert: showFallback
              ? { jobId: execution.jobId, message: execution.fallbackMessage!, dismissed: false }
              : current.translationGpuFallbackAlert
          };
        });
        return;
      }
      if (message.type === 'vietsub.translation.runtime.status' && message.payload) {
        const runtime = message.payload as NonNullable<VietsubModuleState['translationRuntime']>;
        translationRuntimeStatusRef.current = runtime;
        setState((current) => ({
          ...current,
          translationRuntime: runtime,
          translationInstallProgress: runtime.ready ? null : current.translationInstallProgress,
          translationNotice: runtime.ready && current.translationInstallProgress
            ? 'Engine dịch local đã được cài đặt và kiểm tra thành công.'
            : current.translationNotice
        }));
        return;
      }

      if (message.type === 'vietsub.cloud.availability' && message.payload) {
        const payload = message.payload as { projectId: string; organizationId: string; availability: VietsubModuleState['cloudAvailability'] };
        if (message.requestId === cloudAvailabilityRequestRef.current && payload.projectId === selectedProjectIdRef.current
          && payload.organizationId === organizationIdRef.current) {
          setState(current => ({ ...current, cloudAvailability: payload.availability }));
        }
        return;
      }
      if (message.type === 'vietsub.cloud.nothingToTranslate') {
        setState(current => ({ ...current, ...noticeEvent(current, 'translationNotice', message.requestId),
          translationNotice: 'Các câu đã có bản dịch hoặc đang được khóa. Không có câu cần dịch thêm.' }));
        return;
      }

      if (message.type === 'vietsub.translation.runtime.install.progress' && message.payload) {
        const progress = message.payload as NonNullable<VietsubModuleState['translationInstallProgress']>;
        const authoritativeRuntime = translationRuntimeStatusRef.current;
        if (progress.stage.toUpperCase() === 'READY' && authoritativeRuntime?.ready) {
          busyRef.current = false;
        }
        setState((current) => {
          const transition = reduceVietsubTranslationInstallProgress(
            current.translationRuntime,
            authoritativeRuntime,
            progress
          );
          return {
            ...current,
            busy: transition.clearBusy ? false : transition.terminal ? current.busy : true,
            loading: transition.clearBusy ? false : transition.terminal ? current.loading : true,
            activeOperationRequestId: transition.clearBusy
              ? null
              : current.activeOperationRequestId,
            translationInstallProgress: transition.progress,
            translationNotice: transition.clearBusy
              ? 'Engine dịch local đã được cài đặt và kiểm tra thành công.'
              : null,
            translationResourceAlert: null,
            translationRuntime: transition.runtime
          };
        });
        return;
      }

      if (message.type === 'vietsub.voice.runtime.status' && message.payload) {
        const runtime = message.payload as NonNullable<VietsubModuleState['voiceRuntime']>;
        setState((current) => {
          const wasInstalling = Boolean(current.voiceInstallProgress);
          return {
            ...current,
            voiceRuntime: runtime,
            voiceInstallProgress: null,
            voiceNotice: runtime.ready && wasInstalling
              ? 'Piper local đã được cài đặt và kiểm tra thành công.'
              : current.voiceNotice
          };
        });
        return;
      }

      if (message.type === 'vietsub.voice.runtime.install.progress' && message.payload) {
        const progress = message.payload as NonNullable<VietsubModuleState['voiceInstallProgress']>;
        setState((current) => ({
          ...current,
          busy: true,
          loading: true,
          voiceInstallProgress: progress,
          voiceNotice: null,
          voiceRuntime: current.voiceRuntime
            ? { ...current.voiceRuntime, status: 'BUSY', ready: false, message: progress.message }
            : current.voiceRuntime
        }));
        return;
      }

      if (message.type === 'vietsub.voice.models.status' && message.payload) {
        const payload = message.payload as { projectId: string; models: NonNullable<VietsubModuleState['voiceModels']> };
        if (payload.projectId !== selectedProjectIdRef.current || !Array.isArray(payload.models)) return;
        setState((current) => ({
          ...current,
          voiceModels: payload.models,
          voiceModelInstallProgress: null
        }));
        return;
      }

      if (message.type === 'vietsub.voice.model.install.progress' && message.payload) {
        const progress = message.payload as NonNullable<VietsubModuleState['voiceModelInstallProgress']>;
        if (progress.projectId !== selectedProjectIdRef.current) return;
        setState((current) => ({
          ...current,
          busy: true,
          loading: true,
          voiceModelInstallProgress: progress,
          voiceNotice: null
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
          const translationNotice = ['TRANSLATE_LOCAL', 'TRANSLATE_CLOUD'].includes(job.type)
            ? job.status === 'FAILED'
              ? job.errorMessage ?? 'Tác vụ dịch tiếng Việt thất bại.'
              : ['PENDING', 'RUNNING', 'PAUSING', 'PAUSED', 'INTERRUPTED'].includes(job.status)
                ? null
                : current.translationNotice
            : current.translationNotice;
          const translationResourceAlert = job.type === 'TRANSLATE_LOCAL' && job.status === 'FAILED'
            ? createVietsubTranslationResourceAlert(job.errorCode, job.errorMessage)
              ?? current.translationResourceAlert
            : current.translationResourceAlert;
          if (translationResourceAlert && job.type === 'TRANSLATE_LOCAL' && job.status === 'FAILED') {
            translationResourceActionRef.current = { action: 'TRANSLATE', runMode: 'CONTINUE' };
          }
          const voiceNotice = job.type === 'SYNTHESIZE_VOICE_LOCAL'
            ? job.status === 'FAILED'
              ? job.errorMessage ?? 'Tạo giọng tiếng Việt thất bại.'
              : ['PENDING', 'RUNNING', 'PAUSING', 'PAUSED', 'INTERRUPTED'].includes(job.status)
                ? null
                : current.voiceNotice
            : current.voiceNotice;
          const channel = ['TRANSLATE_LOCAL', 'TRANSLATE_CLOUD'].includes(job.type) ? 'translationNotice' : 'voiceNotice';
          const event = job.status === 'FAILED' && ['TRANSLATE_LOCAL', 'TRANSLATE_CLOUD', 'SYNTHESIZE_VOICE_LOCAL'].includes(job.type)
            ? noticeEvent(current, channel, `job:${job.id}:${job.attemptCount}:${job.status}`) : {};
          return { ...current, ...event, jobs, activeJob, translationNotice, translationResourceAlert, voiceNotice };
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
          ...noticeEvent(current, 'subtitleNotice', message.requestId),
          ocrActivationRequest: null,
          subtitleNotice: 'OCR đã hoàn thành và track nguồn mới đã được kích hoạt.'
        }));
        postToHost('vietsub.state.get');
        return;
      }

      if (message.type === 'vietsub.translation.completed') {
        translationResourceActionRef.current = null;
        setState((current) => ({
          ...current,
          ...noticeEvent(current, 'translationNotice', message.requestId),
          translationNotice: 'Đã hoàn thành dịch tiếng Việt.',
          translationResourceAlert: null
        }));
        return;
      }

      if (message.type === 'vietsub.voice.completed') {
        setState((current) => ({
          ...current,
          ...noticeEvent(current, 'voiceNotice', message.requestId),
          voiceNotice: 'Đã hoàn thành timeline giọng Việt.'
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
        subtitlePageRequestRef.current = postToHost('vietsub.subtitle.page.get', query);
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
          ...noticeEvent(current, 'subtitleNotice', message.requestId),
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
        const pendingTranslationAction = translationResourceActionRef.current;
        const translationResourceAlert = createVietsubTranslationResourceAlert(
          errorCode,
          message.error?.message,
          pendingTranslationAction?.action ?? 'TRANSLATE',
          pendingTranslationAction?.runMode ?? 'CONTINUE'
        );
        if (!translationResourceAlert && errorCode.startsWith('TRANSLATION_')) {
          translationResourceActionRef.current = null;
        }
        setState((current) => {
          const translationError = errorCode.startsWith('TRANSLATION_') || errorCode.startsWith('CLOUD_');
          const voiceModelCancelled = errorCode === 'vietsub_operation_cancelled'
            && Boolean(current.voiceModelInstallProgress);
          const voiceError = errorCode.startsWith('VOICE_') || voiceModelCancelled;
          const videoExportError = errorCode.startsWith('vietsub_export_');
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
            ...noticeEvent(current, translationError ? 'translationNotice' : voiceError ? 'voiceNotice'
              : videoExportError ? 'subtitleNotice' : 'errorMessage', message.requestId),
            initialized: true,
            loading: belongsToDifferentOperation ? current.loading : false,
            busy: belongsToDifferentOperation ? current.busy : false,
            activeOperationRequestId: belongsToDifferentOperation
              ? current.activeOperationRequestId
              : null,
            errorCode: translationError || voiceError || videoExportError ? null : errorCode,
            errorMessage: translationError || voiceError || videoExportError
              ? null
              : message.error?.message ?? 'Không thể tải không gian dịch phụ đề.',
            selectedProject: invalidatesEditor ? null : current.selectedProject,
            subtitleWorkspace: invalidatesEditor ? null : current.subtitleWorkspace,
            subtitlePage: invalidatesEditor ? null : current.subtitlePage,
            timelineWindow: invalidatesEditor ? null : current.timelineWindow,
            subtitleNotice: invalidatesEditor
              ? null
              : videoExportError
                ? message.error?.message ?? 'Không thể xuất video thành phẩm.'
                : current.subtitleNotice,
            translationNotice: invalidatesEditor
              ? null
              : translationError
                ? message.error?.message ?? 'Không thể bắt đầu dịch tiếng Việt.'
                : current.translationNotice,
            translationResourceAlert: invalidatesEditor
              ? null
              : translationResourceAlert ?? current.translationResourceAlert,
            translationGpuFallbackAlert: invalidatesEditor ? null : current.translationGpuFallbackAlert,
            translationInstallProgress: translationError ? null : current.translationInstallProgress,
            voiceNotice: invalidatesEditor
              ? null
              : voiceError
                ? voiceModelCancelled ? 'Tải model giọng local đã hủy.'
                  : message.error?.message ?? 'Không thể cài hoặc tạo giọng tiếng Việt.'
                : current.voiceNotice,
            voiceInstallProgress: voiceError ? null : current.voiceInstallProgress,
            voiceModelInstallProgress: voiceError ? null : current.voiceModelInstallProgress
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
      translationResourceActionRef.current = null;
      translationRuntimeStatusRef.current = null;
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
      translationResourceActionRef.current = null;
      translationRuntimeStatusRef.current = null;
      cloudAvailabilityRequestRef.current = null;
      cloudWorkspaceRef.current = null;
      setState((current) => ({
        ...current,
        cloudAvailability: null,
        translationGpuFallbackAlert: null,
        selectedProject: null,
        mediaImportProgress: null,
        subtitleWorkspace: null,
        subtitlePage: null,
        timelineWindow: null,
        subtitleStyle: defaultVietsubSubtitleStyle,
        audioMixSettings: defaultVietsubAudioMixSettings,
        videoTransformSettings: defaultVietsubVideoTransformSettings,
        subtitleNotice: null,
        translationNotice: null,
        translationResourceAlert: null,
        voiceWorkspace: null,
        voiceRuntime: null,
        voiceInstallProgress: null,
        voiceModels: null,
        voiceModelInstallProgress: null,
        voiceNotice: null,
        ocrSettings: disabledState.ocrSettings,
        ocrPreview: null,
        translationRuntime: null,
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

  const startTranslation = useCallback((runMode: VietsubTranslationRunMode = 'CONTINUE',
    executionPolicy?: import('../../types').VietsubTranslationExecutionPolicy) => {
    const payload = createVietsubTranslationStartPayload(state.subtitleWorkspace, runMode, false, executionPolicy);
    if (!payload) {
      translationResourceActionRef.current = null;
      setState((current) => ({
        ...current,
        errorCode: null,
        errorMessage: null,
        ...noticeEvent(current, 'translationNotice'),
        translationNotice: VIETSUB_TRANSLATION_OCR_REQUIRED_MESSAGE
      }));
      return;
    }
    const resourceWarning = createVietsubTranslationResourceAlert(
      state.translationRuntime?.resourceWarningCode,
      state.translationRuntime?.resourceWarningMessage,
      'TRANSLATE',
      runMode
    );
    translationResourceActionRef.current = { action: 'TRANSLATE', runMode, executionPolicy };
    if (state.translationRuntime?.requiresResourceConfirmation && resourceWarning) {
      setState((current) => ({
        ...current,
        translationNotice: null,
        translationResourceAlert: resourceWarning
      }));
      return;
    }
    setState((current) => ({
      ...current,
      translationNotice: null,
      translationResourceAlert: null
    }));
    runProjectOperation('vietsub.job.translate', payload);
  }, [runProjectOperation, state.subtitleWorkspace, state.translationRuntime]);

  const refreshCloudAvailability = useCallback(() => {
    if (!featureEnabled || !selectedProjectIdRef.current) return;
    setState(current => ({ ...current, cloudAvailability: null }));
    cloudAvailabilityRequestRef.current = postToHost('vietsub.cloud.availability');
  }, [featureEnabled]);

  const startCloudTranslation = useCallback(async () => {
    if (cloudSubmittingRef.current || busyRef.current) return;
    const projectId = selectedProjectIdRef.current;
    const orgId = organizationIdRef.current;
    cloudSubmittingRef.current = true;
    try {
      if (beforeLeaveRef.current && !await beforeLeaveRef.current()) return;
      if (selectedProjectIdRef.current !== projectId || organizationIdRef.current !== orgId) return;
      const payload = createVietsubTranslationStartPayload(cloudWorkspaceRef.current);
      if (!payload) {
        setState(current => ({ ...current, ...noticeEvent(current, 'translationNotice'),
          translationNotice: VIETSUB_TRANSLATION_OCR_REQUIRED_MESSAGE }));
        return;
      }
      setState(current => ({ ...current, translationNotice: null, translationResourceAlert: null }));
      runProjectOperation('vietsub.job.translate.cloud', {
        expectedTrackId: payload.expectedTrackId, expectedTrackRevision: payload.expectedTrackRevision
      } satisfies import('../../types').VietsubStartCloudTranslationRequest);
    } finally { cloudSubmittingRef.current = false; }
  }, [runProjectOperation]);

  const installTranslationRuntime = useCallback((installAcceleration = false) => {
    const resourceWarning = createVietsubTranslationResourceAlert(
      state.translationRuntime?.resourceWarningCode,
      state.translationRuntime?.resourceWarningMessage,
      'INSTALL'
    );
    translationResourceActionRef.current = { action: 'INSTALL', installAcceleration };
    if (state.translationRuntime?.requiresResourceConfirmation && resourceWarning) {
      setState((current) => ({
        ...current,
        translationNotice: null,
        translationResourceAlert: resourceWarning
      }));
      return;
    }
    setState((current) => ({
      ...current,
      translationNotice: null,
      translationResourceAlert: null
    }));
    translationRuntimeStatusRef.current = null;
    runProjectOperation('vietsub.translation.runtime.install', { confirmResourceWarning: false,
      ...(installAcceleration ? { installAcceleration: true } : {}) });
  }, [runProjectOperation, state.translationRuntime]);

  const startVoice = useCallback(() => {
    const activeTrackId = state.subtitleWorkspace?.activeTrackId;
    const activeTrack = state.subtitleWorkspace?.tracks.find((track) => track.trackId === activeTrackId);
    if (!activeTrackId || !activeTrack) {
      setState((current) => ({ ...current, ...noticeEvent(current, 'voiceNotice'),
        voiceNotice: 'Hãy chọn subtitle track đã dịch trước khi tạo giọng.' }));
      return;
    }
    setState((current) => ({ ...current, voiceNotice: null }));
    runProjectOperation('vietsub.job.voice', {
      expectedTrackId: activeTrackId,
      expectedTrackRevision: activeTrack.revision
    });
  }, [runProjectOperation, state.subtitleWorkspace]);

  const installVoiceRuntime = useCallback(() => {
    setState((current) => ({ ...current, voiceNotice: null }));
    runProjectOperation('vietsub.voice.runtime.install');
  }, [runProjectOperation]);

  const refreshVoiceModels = useCallback(() => {
    if (!featureEnabled || !selectedProjectIdRef.current) return;
    setState((current) => ({ ...current, voiceNotice: null }));
    postToHost('vietsub.voice.models.status');
  }, [featureEnabled, setState]);

  const installVoiceModel = useCallback((voiceId: string) => {
    const projectId = selectedProjectIdRef.current;
    if (!projectId || !state.voiceModels?.some(model => model.voiceId === voiceId
      && (model.status !== 'READY' || !model.synthesisReady))) return;
    setState((current) => ({ ...current, voiceNotice: null }));
    runProjectOperation('vietsub.voice.model.install', {
      expectedProjectId: projectId, voiceId
    } satisfies import('../../types').VietsubInstallVoiceModelRequest);
  }, [runProjectOperation, state.voiceModels]);

  const selectVoice = useCallback((voiceId: string): Promise<boolean> => {
    const projectId = selectedProjectIdRef.current;
    if (!projectId || !state.voiceModels?.some(model => model.voiceId === voiceId))
      return Promise.resolve(false);
    return runAwaitableOperation('vietsub.voice.select', {
      expectedProjectId: projectId, voiceId
    } satisfies import('../../types').VietsubSelectVoiceRequest);
  }, [runAwaitableOperation, state.voiceModels]);

  const dismissTranslationResourceAlert = useCallback(() => {
    translationResourceActionRef.current = null;
    setState((current) => ({ ...current, translationResourceAlert: null }));
  }, []);

  const dismissTranslationGpuFallbackAlert = useCallback(() => {
    setState(current => ({ ...current, translationGpuFallbackAlert: current.translationGpuFallbackAlert
      ? { ...current.translationGpuFallbackAlert, dismissed: true } : null }));
  }, []);

  const continueTranslationAfterResourceWarning = useCallback(() => {
    const pendingAction = translationResourceActionRef.current;
    translationResourceActionRef.current = null;
    if (!pendingAction) {
      setState((current) => ({ ...current, translationResourceAlert: null }));
      return;
    }

    setState((current) => ({
      ...current,
      translationNotice: null,
      translationResourceAlert: null
    }));
    if (pendingAction.action === 'INSTALL') {
      translationRuntimeStatusRef.current = null;
      runProjectOperation('vietsub.translation.runtime.install', { confirmResourceWarning: true,
        ...(pendingAction.installAcceleration ? { installAcceleration: true } : {}) });
      return;
    }

    const payload = createVietsubTranslationStartPayload(
      state.subtitleWorkspace,
      pendingAction.runMode ?? 'CONTINUE',
      true,
      pendingAction.executionPolicy
    );
    if (!payload) {
      setState((current) => ({
        ...current,
        ...noticeEvent(current, 'translationNotice'),
        translationNotice: VIETSUB_TRANSLATION_OCR_REQUIRED_MESSAGE
      }));
      return;
    }
    runProjectOperation('vietsub.job.translate', payload);
  }, [runProjectOperation, state.subtitleWorkspace]);

  const pauseJob = useCallback((jobId: string) => {
    runProjectOperation('vietsub.job.pause', { jobId });
  }, [runProjectOperation]);

  const resumeJob = useCallback((jobId: string) => {
    runProjectOperation('vietsub.job.resume', { jobId });
  }, [runProjectOperation]);

  const retryJob = useCallback((jobId: string) => {
    runProjectOperation('vietsub.job.retry', { jobId });
  }, [runProjectOperation]);

  const cancelJob = useCallback((jobId: string) => {
    runProjectOperation('vietsub.job.cancel', { jobId });
  }, [runProjectOperation]);

  const activateOcrTrack = useCallback((jobId: string, confirmImpact: boolean) => {
    postToHost('vietsub.ocr.track.activate', { jobId, confirmImpact });
  }, []);

  const loadSubtitlePage = useCallback((query: VietsubSubtitlePageQuery) => {
    if (!featureEnabled || !state.selectedProject) return;
    subtitleQueryRef.current = query;
    subtitlePageRequestRef.current = postToHost('vietsub.subtitle.page.get', query);
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
    subtitlePageRequestRef.current = postToHost('vietsub.subtitle.page.get', query);
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
    postToHost('vietsub.translation.runtime.status');
    postToHost('vietsub.voice.runtime.status');
    refreshCloudAvailability();
  }, [featureEnabled, state.selectedProject?.projectId, refreshCloudAvailability]);

  useEffect(() => {
    if (!createVietsubTranslationStartPayload(state.subtitleWorkspace)) return;
    setState((current) => current.translationNotice === VIETSUB_TRANSLATION_OCR_REQUIRED_MESSAGE
      ? { ...current, translationNotice: null }
      : current);
  }, [state.subtitleWorkspace]);

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

  const updateSubtitleCue = useCallback((cue: VietsubSubtitleCueUpdate) =>
    runAwaitableOperation('vietsub.subtitle.cue.update', cue), [runAwaitableOperation]);

  const updateSubtitleStyle = useCallback(async (
    style: VietsubSubtitleStyle,
    audioMixSettings: VietsubAudioMixSettings,
    videoTransformSettings: VietsubVideoTransformSettings
  ) => {
    const completed = await runAwaitableOperation('vietsub.subtitle.style.update', {
      style,
      audioMixSettings,
      videoTransformSettings
    });
    if (completed) {
      setState((current) => ({ ...current, subtitleStyle: style, audioMixSettings, videoTransformSettings }));
    }
    return completed;
  }, [runAwaitableOperation]);

  const updateCueVoice = useCallback(async (cueIds: string[], voiceEnabled: boolean,
    expectedTrackId: string, expectedTrackRevision: number) => {
    setState(current => ({ ...current, voiceNotice: null }));
    return runAwaitableOperation('vietsub.subtitle.voice.update', {
      cueIds, voiceEnabled, expectedTrackId, expectedTrackRevision
    });
  }, [runAwaitableOperation]);

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

  const exportVideo = useCallback(() => {
    return runAwaitableOperation('vietsub.video.export');
  }, [runAwaitableOperation]);

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
    startTranslation,
    startCloudTranslation,
    refreshCloudAvailability,
    installTranslationRuntime,
    startVoice,
    installVoiceRuntime,
    refreshVoiceModels,
    installVoiceModel,
    selectVoice,
    dismissTranslationResourceAlert,
    dismissTranslationGpuFallbackAlert,
    continueTranslationAfterResourceWarning,
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
    updateSubtitleStyle,
    updateCueVoice,
    updateTimelineCue,
    splitSubtitleCue,
    alignSubtitleCue,
    duplicateSubtitleCue,
    deleteSubtitleCue,
    exportSrt,
    exportVideo,
    registerBeforeLeave,
    prepareToLeaveEditor
  };
}
