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
  timelineWindow?: VietsubTimelineWindow | null;
  subtitleNotice?: string | null;
  ocrSettings: VietsubOcrSettings;
  ocrRuntime?: VietsubOcrRuntimeStatus | null;
  ocrPreview?: VietsubOcrPreviewResult | null;
  jobs: VietsubJobSummary[];
  activeJob?: VietsubJobSummary | null;
  ocrActivationRequest?: VietsubOcrActivationRequest | null;
  timelineMediaEvent?: VietsubTimelineMediaEvent | null;
};

export type VietsubOcrRegion = {
  x: number;
  y: number;
  width: number;
  height: number;
};

export type VietsubOcrSettings = {
  languageCode: 'en' | 'zh';
  profile: 'FAST' | 'BALANCED' | 'ACCURATE';
  region: VietsubOcrRegion;
};

export type VietsubOcrRuntimeStatus = {
  ready: boolean;
  errorCode?: string | null;
  message: string;
  availableLanguages: string[];
};

export type VietsubOcrPreviewResult = {
  timestampMilliseconds: number;
  text: string;
  confidence: number;
  frameWidth: number;
  frameHeight: number;
};

export type VietsubJobStepSummary = {
  code: string;
  status: string;
  progressPercent: number;
  errorCode?: string | null;
  errorMessage?: string | null;
};

export type VietsubJobSummary = {
  id: string;
  projectId: string;
  type: string;
  status: 'PENDING' | 'RUNNING' | 'PAUSING' | 'PAUSED' | 'INTERRUPTED' | 'COMPLETED' | 'FAILED' | 'CANCELLED';
  progressPercent: number;
  statusMessage?: string | null;
  outputTrackId?: string | null;
  attemptCount: number;
  maxAttempts: number;
  errorCode?: string | null;
  errorMessage?: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  completedAtUtc?: string | null;
  steps: VietsubJobStepSummary[];
};

export type VietsubOcrActivationRequest = {
  jobId: string;
  outputTrackId: string;
  reasons: string[];
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
  timelineThumbnails: VietsubTimelineThumbnail[];
  waveformUrl?: string | null;
  waveformStatus: 'READY' | 'PENDING' | 'NO_AUDIO' | 'FAILED';
  rotationDegrees: number;
  thumbnailProfileVersion: number;
  thumbnailCount: number;
  waveformProfileVersion: number;
  waveformRevision: number;
};

export type VietsubTimelineThumbnail = {
  index: number;
  profileVersion: number;
  sourceSha256: string;
  url: string;
  revision: number;
  timestampMilliseconds: number;
  startMilliseconds: number;
  endMilliseconds: number;
};

export type VietsubTimelineMediaEvent = {
  sequence: number;
  kind: 'ready' | 'failed';
  resourceType: 'thumbnail' | 'waveform' | 'video' | 'unknown';
  mediaId?: string | null;
  sourceSha256?: string | null;
  profileVersion?: number | null;
  index?: number | null;
  url?: string | null;
  revision?: number | null;
  status?: VietsubMediaSummary['waveformStatus'] | null;
  errorCode?: string | null;
  correlationId?: string | null;
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

export type VietsubTimelineCue = {
  cueId: string;
  cueIndex: number;
  startMilliseconds: number;
  endMilliseconds: number;
  locked: boolean;
  qualityStatus?: string | null;
  hasWarnings: boolean;
  hasTranslation: boolean;
  previewText: string;
};

export type VietsubTimelineWindow = {
  trackId: string;
  trackRevision: number;
  windowStartMilliseconds: number;
  windowEndMilliseconds: number;
  truncated: boolean;
  cues: VietsubTimelineCue[];
};

export type VietsubTimelineWindowQuery = {
  trackId: string;
  windowStartMilliseconds: number;
  windowEndMilliseconds: number;
  maximumCues: number;
};

export type VietsubTimelineCueUpdate = {
  trackId: string;
  cueId: string;
  expectedTrackRevision: number;
  startMilliseconds: number;
  endMilliseconds: number;
};

export type VietsubSaveState = 'saved' | 'dirty' | 'saving' | 'error';
