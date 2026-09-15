import { RefreshCw, TriangleAlert } from 'lucide-react';
import type {
  VietsubModuleState,
  VietsubAudioMixSettings,
  VietsubOcrSettings,
  VietsubSubtitleCueUpdate,
  VietsubSubtitleStyle,
  VietsubVideoTransformSettings,
  VietsubSubtitlePageQuery,
  VietsubTimelineCueUpdate,
  VietsubTimelineWindowQuery
} from './types';
import { VietsubEditorWorkspace } from './VietsubEditorWorkspace';
import { VietsubProjectLibrary } from './VietsubProjectLibrary';
import { VietsubTranslationResourceModal } from './VietsubTranslationResourceModal';
import type { VietsubTranslationRunMode } from './vietsubTranslation';
import { VietsubNotice } from './VietsubNotice';

export type VietsubPageProps = {
  state: VietsubModuleState;
  onUpdateCueVoice?: (cueIds: string[], enabled: boolean, trackId: string, revision: number) => Promise<boolean>;
  onRefresh: () => void;
  onCreateProject: (name: string) => void;
  onOpenProject: (projectId: string) => void;
  onRenameProject: (projectId: string, name: string) => void;
  onCloseProject: () => Promise<boolean>;
  onImportMedia: (mode: 'COPY' | 'LINK') => void;
  onUpdateOcrSettings: (settings: VietsubOcrSettings) => Promise<boolean>;
  onPreviewOcr: (settings: VietsubOcrSettings, timestampMilliseconds: number) => void;
  onStartOcr: (settings: VietsubOcrSettings) => void;
  onStartTranslation: (runMode?: VietsubTranslationRunMode) => void;
  onStartCloudTranslation?: () => void;
  onRefreshCloudAvailability?: () => void;
  onInstallTranslationRuntime: () => void;
  onStartVoice: () => void;
  onInstallVoiceRuntime: () => void;
  onRefreshVoiceModels?: () => void;
  onInstallVoiceModel?: (voiceId: string) => void;
  onSelectVoice?: (voiceId: string) => Promise<boolean>;
  onDismissTranslationResourceAlert: () => void;
  onContinueTranslationAfterResourceWarning: () => void;
  onPauseJob: (jobId: string) => void;
  onResumeJob: (jobId: string) => void;
  onRetryJob: (jobId: string) => void;
  onCancelJob: (jobId: string) => void;
  onActivateOcrTrack: (jobId: string, confirmImpact: boolean) => void;
  onImportSrt: (languageCode: string) => void;
  onActivateSubtitleTrack: (trackId: string) => void;
  onLoadSubtitlePage: (query: VietsubSubtitlePageQuery) => void;
  onLoadTimelineWindow: (query: VietsubTimelineWindowQuery) => void;
  onRequestTimelineThumbnails: (sourceSha256: string, indices: number[]) => void;
  onRequestTimelineWaveform: (sourceSha256: string) => void;
  onUpdateSubtitleCue: (cue: VietsubSubtitleCueUpdate) => Promise<boolean>;
  onUpdateSubtitleStyle: (
    style: VietsubSubtitleStyle,
    audioMixSettings: VietsubAudioMixSettings,
    videoTransformSettings: VietsubVideoTransformSettings
  ) => Promise<boolean>;
  onUpdateTimelineCue: (update: VietsubTimelineCueUpdate) => Promise<boolean>;
  onSplitSubtitleCue: (cueId: string, positionMilliseconds: number) => void;
  onAlignSubtitleCue: (cueId: string, positionMilliseconds: number) => void;
  onDuplicateSubtitleCue: (cueId: string) => void;
  onDeleteSubtitleCue: (cueId: string) => void;
  onExportSrt: (mode: 'ORIGINAL' | 'TRANSLATED') => void;
  onExportVideo: () => Promise<boolean>;
  onCancelOperation: () => void;
  onRegisterBeforeLeave: (handler: () => Promise<boolean>) => () => void;
};

export function VietsubPage(props: VietsubPageProps) {
  const { state, onRefresh } = props;

  return (
    <div className={`page-shell vietsub-page ${state.selectedProject ? 'vietsub-page--editor' : 'vietsub-page--library'}`}>
      {state.errorMessage && (
        <VietsubNotice key={state.selectedProject?.projectId ?? 'library'} eventId={state.noticeEvents?.errorMessage?.id ?? 0}
          className="card vietsub-inline-error" role="alert" icon={<TriangleAlert size={19} />}
          actions={<button type="button" onClick={onRefresh}><RefreshCw size={15} /> Thử lại</button>}>
          <div><strong>Chưa hoàn tất thao tác</strong><p>{state.errorMessage}</p></div>
        </VietsubNotice>
      )}

      {state.selectedProject ? (
        <VietsubEditorWorkspace key={state.selectedProject.projectId} {...props} project={state.selectedProject} />
      ) : (
        <VietsubProjectLibrary
          state={state}
          onRefresh={props.onRefresh}
          onCreateProject={props.onCreateProject}
          onOpenProject={props.onOpenProject}
          onRenameProject={props.onRenameProject}
        />
      )}

      {state.translationResourceAlert && (
        <VietsubTranslationResourceModal
          alert={state.translationResourceAlert}
          onDismiss={props.onDismissTranslationResourceAlert}
          onContinue={props.onContinueTranslationAfterResourceWarning}
        />
      )}
    </div>
  );
}
