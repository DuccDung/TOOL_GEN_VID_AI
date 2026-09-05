import { RefreshCw, TriangleAlert } from 'lucide-react';
import type {
  VietsubModuleState,
  VietsubOcrSettings,
  VietsubSubtitleCue,
  VietsubSubtitlePageQuery,
  VietsubTimelineCueUpdate,
  VietsubTimelineWindowQuery
} from './types';
import { VietsubEditorWorkspace } from './VietsubEditorWorkspace';
import { VietsubProjectLibrary } from './VietsubProjectLibrary';

export type VietsubPageProps = {
  state: VietsubModuleState;
  onRefresh: () => void;
  onCreateProject: (name: string) => void;
  onOpenProject: (projectId: string) => void;
  onRenameProject: (projectId: string, name: string) => void;
  onCloseProject: () => Promise<boolean>;
  onImportMedia: (mode: 'COPY' | 'LINK') => void;
  onUpdateOcrSettings: (settings: VietsubOcrSettings) => Promise<boolean>;
  onPreviewOcr: (settings: VietsubOcrSettings, timestampMilliseconds: number) => void;
  onStartOcr: (settings: VietsubOcrSettings) => void;
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
  onUpdateSubtitleCue: (cue: Pick<VietsubSubtitleCue, 'cueId' | 'originalText' | 'translatedText' | 'speaker'>) => Promise<boolean>;
  onUpdateTimelineCue: (update: VietsubTimelineCueUpdate) => Promise<boolean>;
  onSplitSubtitleCue: (cueId: string, positionMilliseconds: number) => void;
  onAlignSubtitleCue: (cueId: string, positionMilliseconds: number) => void;
  onDuplicateSubtitleCue: (cueId: string) => void;
  onDeleteSubtitleCue: (cueId: string) => void;
  onExportSrt: (mode: 'ORIGINAL' | 'TRANSLATED') => void;
  onRegisterBeforeLeave: (handler: () => Promise<boolean>) => () => void;
};

export function VietsubPage(props: VietsubPageProps) {
  const { state, onRefresh } = props;

  return (
    <div className={`page-shell vietsub-page ${state.selectedProject ? 'vietsub-page--editor' : 'vietsub-page--library'}`}>
      {state.errorMessage && (
        <section className="card vietsub-inline-error" role="alert">
          <TriangleAlert size={19} />
          <div><strong>Chưa hoàn tất thao tác</strong><p>{state.errorMessage}</p></div>
          <button type="button" onClick={onRefresh}><RefreshCw size={15} /> Thử lại</button>
        </section>
      )}

      {state.selectedProject ? (
        <VietsubEditorWorkspace {...props} project={state.selectedProject} />
      ) : (
        <VietsubProjectLibrary
          state={state}
          onRefresh={props.onRefresh}
          onCreateProject={props.onCreateProject}
          onOpenProject={props.onOpenProject}
          onRenameProject={props.onRenameProject}
        />
      )}
    </div>
  );
}
