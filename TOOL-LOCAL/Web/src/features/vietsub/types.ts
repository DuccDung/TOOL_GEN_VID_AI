import type { VietsubNoticeEvents } from './vietsubNoticeEvents';

export type VietsubModuleState = {
  cloudAvailability?: VietsubCloudAvailability | null;
  noticeEvents?: VietsubNoticeEvents;
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
  subtitleStyle: VietsubSubtitleStyle;
  audioMixSettings: VietsubAudioMixSettings;
  videoTransformSettings: VietsubVideoTransformSettings;
  subtitleNotice?: string | null;
  translationNotice?: string | null;
  translationResourceAlert?: VietsubTranslationResourceAlert | null;
  ocrSettings: VietsubOcrSettings;
  ocrRuntime?: VietsubOcrRuntimeStatus | null;
  ocrPreview?: VietsubOcrPreviewResult | null;
  translationRuntime?: VietsubTranslationRuntimeStatus | null;
  translationInstallProgress?: VietsubTranslationRuntimeInstallProgress | null;
  voiceWorkspace?: VietsubVoiceWorkspace | null;
  voiceRuntime?: VietsubVoiceRuntimeStatus | null;
  voiceInstallProgress?: VietsubVoiceRuntimeInstallProgress | null;
  voiceModels?: VietsubVoiceModelStatus[] | null;
  voiceModelInstallProgress?: VietsubVoiceModelInstallProgress | null;
  voiceNotice?: string | null;
  jobs: VietsubJobSummary[];
  activeJob?: VietsubJobSummary | null;
  ocrActivationRequest?: VietsubOcrActivationRequest | null;
  timelineMediaEvent?: VietsubTimelineMediaEvent | null;
};

export type VietsubCloudAvailability = {
  available: boolean;
  errorCode?: string | null;
  message?: string | null;
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

export type VietsubTranslationRuntimeStatus = {
  status: 'READY' | 'DISABLED' | 'NOT_INSTALLED' | 'INVALID' | 'UNSUPPORTED_HARDWARE' | 'BUSY';
  ready: boolean;
  engineId?: string | null;
  engineVersion?: string | null;
  sourceLanguages: string[];
  supportsSceneContext: boolean;
  supportsReviewPass: boolean;
  message: string;
  errorCode?: string | null;
  runtimeProfileId?: string | null;
  lowMemoryMode?: boolean;
  requiresResourceConfirmation?: boolean;
  resourceWarningCode?: string | null;
  resourceWarningMessage?: string | null;
};

export type VietsubTranslationRuntimeInstallProgress = {
  stage: string;
  percent: number;
  message: string;
  bytesProcessed: number;
  totalBytes: number;
};

export type VietsubTranslationResourceAlert = {
  errorCode: 'TRANSLATION_RESOURCE_CONFIRMATION_REQUIRED';
  title: string;
  message: string;
  action: 'TRANSLATE' | 'INSTALL';
  runMode?: 'CONTINUE' | 'RETRY_FAILED' | 'RESTART_UNLOCKED';
};

export type VietsubVoiceRuntimeStatus = {
  status: 'READY' | 'DISABLED' | 'NOT_INSTALLED' | 'INVALID' | 'BUSY';
  ready: boolean;
  engineId: string;
  engineVersion: string;
  modelId: string;
  modelVersion: string;
  voiceId: string;
  installedBytes: number;
  requiredBytes: number;
  message: string;
  errorCode?: string | null;
};

export type VietsubVoiceRuntimeInstallProgress = {
  stage: string;
  percent: number;
  message: string;
  bytesProcessed: number;
  totalBytes: number;
};

// Model status and actual synthesis readiness are separate.
export type VietsubVoiceModelStatus = {
  voiceId: string;
  displayName: string;
  engineId: string;
  modelId: string;
  modelVersion: string;
  status: 'READY' | 'NOT_INSTALLED' | 'INVALID' | 'DISABLED';
  installedBytes: number;
  requiredBytes: number;
  license: string;
  message: string;
  synthesisReady?: boolean;
  synthesisMessage?: string | null;
};

export type VietsubVoiceModelInstallProgress = {
  projectId: string;
  voiceId: string;
  stage: string;
  percent: number;
  message: string;
  bytesProcessed: number;
  totalBytes: number;
};

export type VietsubVoiceSettings = {
  engineId: string;
  modelId: string;
  voiceId: string;
  maximumPhraseGapMilliseconds: number;
  maximumPhraseDurationMilliseconds: number;
  maximumPhraseCharacters: number;
  maximumBorrowedGapMilliseconds: number;
  preferredMaximumTempo: number;
  maximumTempo: number;
  trimSilence: boolean;
};

export type VietsubVoiceCatalogItem = {
  voiceId: string;
  engineId: string;
  modelId: string;
  displayName: string;
  languageCode: string;
  gender: string;
  license: string;
};

export type VietsubVoiceArtifact = {
  artifactId: string;
  trackId: string;
  trackRevision: number;
  artifactKind: 'PHRASE' | 'TIMELINE';
  sizeBytes: number;
  sha256: string;
  durationMilliseconds: number;
  sampleRate: number;
  channels: number;
  status: string;
  timingStatus?: string | null;
  updatedAtUtc: string;
};

export type VietsubVoiceTimingDiagnostic = {
  phraseId: string;
  naturalDurationMilliseconds: number;
  targetDurationMilliseconds: number;
  borrowedGapMilliseconds: number;
  tempo: number;
  status: 'NATURAL' | 'BORROWED_GAP' | 'COMPRESSED' | 'REVIEW_REQUIRED';
  suggestedMaximumCharacters: number;
};

export type VietsubVoiceWorkspace = {
  requiresRebuild?: boolean;
  enabledCueCount?: number;
  settings: VietsubVoiceSettings;
  voices: VietsubVoiceCatalogItem[];
  timeline?: VietsubVoiceArtifact | null;
  timelinePlaybackUrl?: string | null;
  timingDiagnostics: VietsubVoiceTimingDiagnostic[];
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
  voiceEnabledCueCount?: number;
  voiceTranslatedCueCount?: number;
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
  voiceEnabled?: boolean;
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

export type VietsubSubtitleStylePreset = 'READABLE' | 'VERTICAL' | 'OUTLINE' | 'TIKTOK' | 'CINEMA' | 'YELLOW' | 'MINIMAL' | 'CUSTOM';

export type VietsubSubtitleAlignment = 'BOTTOM_LEFT' | 'BOTTOM_CENTER' | 'BOTTOM_RIGHT';

export type VietsubSubtitleVerticalPosition = 'TOP' | 'MIDDLE' | 'BOTTOM' | 'CUSTOM';

export type VietsubSubtitleStyle = {
  presetId: VietsubSubtitleStylePreset;
  fontFamily: 'Arial' | 'Segoe UI' | 'Tahoma' | 'Verdana' | 'Times New Roman';
  fontSizePercent: number;
  bold: boolean;
  italic: boolean;
  textColor: string;
  textOpacity: number;
  outlineColor: string;
  outlineOpacity: number;
  outlineWidthPercent: number;
  shadowColor: string;
  shadowOpacity: number;
  shadowOffsetPercent: number;
  backgroundEnabled: boolean;
  backgroundColor: string;
  backgroundOpacity: number;
  alignment: VietsubSubtitleAlignment;
  verticalPosition: VietsubSubtitleVerticalPosition;
  positionXPercent: number;
  positionYPercent: number;
  bottomMarginPercent: number;
  horizontalMarginPercent: number;
  maxWidthPercent: number;
  lineHeight: number;
  maxLines: 1 | 2 | 3;
};

export type VietsubAudioMixSettings = {
  originalVolume: number;
  translatedVoiceVolume: number;
  originalMuted: boolean;
  translatedVoiceMuted: boolean;
  autoDuckOriginal: boolean;
};

export type VietsubVideoTransformSettings = {
  flipHorizontal: boolean;
  flipVertical: boolean;
};

export type VietsubSubtitlePageQuery = {
  trackId?: string | null;
  offset: number;
  pageSize: number;
  search: string;
  status: VietsubSubtitleStatus;
  speaker: string;
};

export type VietsubTimelineCue = {
  voiceEnabled?: boolean;
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
