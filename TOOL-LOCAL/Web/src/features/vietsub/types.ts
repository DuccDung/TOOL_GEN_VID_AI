export type VietsubModuleState = {
  enabled: boolean;
  initialized: boolean;
  loading: boolean;
  busy: boolean;
  activeOperationRequestId?: string | null;
  stage: 'disabled' | 'loading' | 'shell_ready' | string;
  errorCode?: string | null;
  errorMessage?: string | null;
  projects: VietsubProjectSummary[];
  selectedProject?: VietsubProjectSummary | null;
  mediaImportProgress?: VietsubMediaImportProgress | null;
  subtitleWorkspace?: VietsubSubtitleWorkspace | null;
  subtitlePage?: VietsubSubtitlePage | null;
  subtitleNotice?: string | null;
};

export type VietsubMediaImportProgress = {
  bytesProcessed: number;
  totalBytes: number;
  percent: number;
  megabytesPerSecond: number;
};

export type VietsubMediaSummary = {
  mediaId: string;
  fileName: string;
  importMode: 'COPY' | 'LINK';
  sizeBytes: number;
  sha256: string;
  durationSeconds: number;
  width: number;
  height: number;
  framesPerSecond?: number | null;
  videoCodec?: string | null;
  audioCodec?: string | null;
  hasAudio: boolean;
  sourceAvailable: boolean;
  sourceChanged: boolean;
  sourceIssueCode?: string | null;
  playbackUrl: string;
  thumbnailUrls: string[];
};

export type VietsubProjectSummary = {
  projectId: string;
  name: string;
  status: string;
  sourceLanguageCode: string;
  targetLanguageCode: string;
  updatedAtUtc: string;
  needsRecovery: boolean;
  serverSynchronized: boolean;
  serverSyncErrorCode?: string | null;
  sourceVideo?: VietsubMediaSummary | null;
};

export type VietsubSubtitleWorkspace = {
  activeTrackId?: string | null;
  tracks: VietsubSubtitleTrackSummary[];
};

export type VietsubSubtitleTrackSummary = {
  trackId: string;
  displayName: string;
  languageCode: string;
  source: string;
  revision: number;
  cueCount: number;
  translatedCueCount: number;
  warningCueCount: number;
  updatedAtUtc: string;
};

export type VietsubSubtitleCue = {
  cueId: string;
  cueIndex: number;
  startMilliseconds: number;
  endMilliseconds: number;
  speaker: string;
  originalText: string;
  translatedText: string;
  originalLocked: boolean;
  translationLocked: boolean;
  qualityStatus?: string | null;
  warnings: string[];
  updatedAtUtc: string;
};

export type VietsubSubtitlePage = {
  trackId: string;
  trackRevision: number;
  offset: number;
  pageSize: number;
  totalCount: number;
  search: string;
  status: VietsubSubtitleStatus;
  speaker: string;
  speakers: string[];
  cues: VietsubSubtitleCue[];
};

export type VietsubSubtitleStatus = 'ALL' | 'PENDING' | 'TRANSLATED' | 'LOCKED' | 'WARNING';

export type VietsubSubtitlePageQuery = {
  trackId?: string | null;
  offset: number;
  pageSize: number;
  search: string;
  status: VietsubSubtitleStatus;
  speaker: string;
};
