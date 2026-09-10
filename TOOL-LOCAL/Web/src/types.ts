export type UserProfile = {
  userId: string;
  email: string;
  displayName?: string | null;
  accountStatus: string;
  roles: string[];
};

export type VietsubStartCloudTranslationRequest = {
  expectedTrackId: string;
  expectedTrackRevision: number;
};

export type ProjectSummary = {
  projectId: string;
  organizationId?: string | null;
  name: string;
  topic: string;
  platform: string;
  aspectRatio: string;
  targetDurationSeconds: number;
  status: string;
  actualCost: number;
  budgetLimit?: number | null;
  updatedAtUtc: string;
};

export type OrganizationSummary = {
  organizationId: string;
  code: string;
  name: string;
  role: string;
  status: string;
  monthlyBudgetLimit: number;
  reservedCost: number;
  actualCost: number;
  remainingBudget: number;
  currencyCode: string;
  periodStartsAtUtc: string;
  periodEndsAtUtc: string;
};

export type PipelineStage = {
  code: string;
  title: string;
  subtitle: string;
  status: 'waiting' | 'processing' | 'completed' | 'failed';
  progressPercent: number;
  detailLines: string[];
};

export type RenderSummary = {
  status: string;
  progressPercent: number;
  completedScenes: number;
  totalScenes: number;
  estimatedSecondsRemaining?: number | null;
};

export type PreviewSummary = {
  url?: string | null;
  durationMs?: number | null;
  mimeType?: string | null;
};

export type LocalVoiceStatus = 'Preparing' | 'DetectingSpeech' | 'SeparatingAudio' | 'ConvertingVoice' | 'Mixing' |
  'Validating' | 'ReviewRequired' | 'Approved' | 'Rejected' | 'Failed' | 'Cancelled' | 'Stale' | 'Interrupted';
export type LocalVoiceAnchor = {
  id: string; characterId: string; sceneId: string; status: LocalVoiceStatus; previewUrl?: string | null; message?: string | null;
};
export type LocalVoiceJob = LocalVoiceAnchor & {
  errorCode?: string | null; anchorId?: string | null; sourceFingerprint: string;
  nativePreviewUrl?: string | null; nativeException: boolean;
};
export type LocalVoiceState = {
  projectId: string; enabled: boolean; running: boolean;
  runtime: { status: 'DISABLED' | 'NOT_INSTALLED' | 'INVALID' | 'READY'; message: string; fingerprint?: string | null };
  anchors: LocalVoiceAnchor[]; jobs: LocalVoiceJob[];
};
export type LocalVoiceAction = {
  projectId: string; sceneId?: string; jobId?: string; anchorId?: string; sceneIds?: string[]; confirmed?: boolean; reason?: string;
};

export type CharacterReferenceSummary = {
  characterReferenceId: string;
  referenceType: string;
  isPrimary: boolean;
  approvalStatus: string;
  previewUrl?: string | null;
  mimeType?: string | null;
};

export type CharacterSummary = {
  characterId: string;
  characterKey: string;
  version: number;
  name: string;
  role?: string | null;
  visualIdentity: string;
  wardrobe: string;
  immutableTraits: string[];
  forbiddenChanges: string[];
  status: string;
  sceneCount: number;
  primaryReference?: CharacterReferenceSummary | null;
  canEdit: boolean;
  canApprove: boolean;
  setupMessage?: string | null;
  voiceCode?: string | null;
  voiceSpeakingRate?: number | null;
};

export type SceneCharacterSummary = {
  characterId: string;
  name: string;
  status: string;
  referencePreviewUrl?: string | null;
};

export type SceneSpeechVerification = {
  speechVerificationReportId: string;
  status: 'Pending' | 'Processing' | 'Passed' | 'NeedsReview' | 'Failed' | string;
  transcript: string;
  normalizedTranscript: string;
  wordErrorRate: number;
  characterErrorRate: number;
  requiredTermRecall: number;
  missingRequiredTerms: string[];
  speechStartMs?: number | null;
  speechEndMs?: number | null;
  reviewApproved?: boolean;
  reviewReason?: string | null;
  reviewedAtUtc?: string | null;
  rowVersion?: string | null;
};

export type SceneSpeechVerificationQuote = {
  providerCode: string;
  modelCode: string;
  estimatedCost: number;
  currencyCode: string;
  billableAudioSeconds: number;
};

export type VoiceProfileSummary = {
  voiceProfileId: string;
  voiceProfileVersionId: string;
  scope: 'ProjectNarrator' | 'Character' | string;
  characterId?: string | null;
  version: number;
  providerCode: string;
  modelCode: string;
  voiceCode: string;
  providerVoiceCode: string;
  languageCode: string;
  speakingRate: number;
  snapshotHash: string;
  status: 'Draft' | 'Approved' | 'Superseded' | 'Revoked' | string;
  createdAtUtc: string;
  approvedAtUtc?: string | null;
  preview?: PreviewSummary | null;
  previewProviderRequestId?: string | null;
};

export type VoiceProfilePreviewQuote = {
  voiceProfileVersionId: string;
  providerCode: string;
  modelCode: string;
  estimatedCost: number;
  currencyCode: string;
  previewTextCharacters: number;
};

export type VoiceCatalogPreviewQuote = {
  voiceCode: string;
  speakingRate: number;
  providerCode: string;
  modelCode: string;
  estimatedCost: number;
  currencyCode: string;
  previewTextCharacters: number;
  contextProjectId: string;
};

export type VoiceCatalogPreviewPlayback = {
  voiceCode: string;
  speakingRate: number;
  previewUrl: string;
  durationMs: number;
  actualCost: number;
  currencyCode: string;
};

export type SceneVoiceQuote = {
  sceneId: string;
  voiceProfileVersionId: string;
  voiceSnapshotHash: string;
  providerCode: string;
  modelCode: string;
  estimatedCost: number;
  currencyCode: string;
  reusesExistingGeneration: boolean;
};

export type CanonicalVoiceQuote = {
  estimatedCost: number;
  currencyCode: string;
  newVoiceCount: number;
  reusedVoiceCount: number;
  scenes: SceneVoiceQuote[];
};

export type SceneSummary = {
  sceneId: string;
  sequenceNumber: number;
  timelineStartMs: number;
  timelineEndMs: number;
  durationMs: number;
  generationDurationMs: number;
  storyPurpose: string;
  narration?: string | null;
  visualDescription: string;
  prompt: string;
  status: string;
  canEdit: boolean;
  canGenerate: boolean;
  characters: SceneCharacterSummary[];
  characterSetupMessage?: string | null;
  preview?: PreviewSummary | null;
  lastErrorMessage?: string | null;
  lastErrorCode?: string | null;
  hasNarratedAudio?: boolean;
  speechMode: 'None' | 'OnCameraDialogue' | 'NativeVoiceOver';
  nativeAudioPresent: boolean;
  nativeAudioAudible: boolean;
  requiresAudioReview: boolean;
  canApproveNativeAudio: boolean;
  speakerCharacterName?: string | null;
  voiceStyle?: string | null;
  ambientAudio?: string | null;
  soundEffects?: string | null;
  speechStatus?: string;
  hasCanonicalVoicePreview?: boolean;
  canonicalVoicePreview?: PreviewSummary | null;
  speechVerification?: SceneSpeechVerification | null;
  voiceProfileVersionId?: string | null;
  voiceSnapshotHash?: string | null;
  speechPacing?: SceneSpeechPacingSummary | null;
};

export type SceneSpeechPacingSummary = {
  speechUnitCount: number;
  speakingRate: number;
  estimatedDurationSeconds: number;
  estimatedDurationRatio: number;
  targetMinimumSeconds: number;
  targetMaximumSeconds: number;
  estimatedStatus: 'TooShort' | 'Short' | 'OnTarget' | 'Long' | 'TooLong';
  actualDurationSeconds?: number | null;
  actualDurationRatio?: number | null;
  actualStatus?: 'TooShort' | 'Short' | 'OnTarget' | 'Long' | 'TooLong' | null;
};

export type SceneFirstFrameStatus = 'PendingReview' | 'Approved' | 'Rejected' | 'Superseded' | 'Invalidated';

export type SceneFirstFrameSummary = {
  sceneFirstFrameId: string;
  sceneId: string;
  mediaAssetId: string;
  providerRequestId: string;
  version: number;
  status: SceneFirstFrameStatus;
  sourceCharacterReferenceId?: string | null;
  scenePlanVersion: number;
  scenePromptId: string;
  scenePromptVersion: number;
  aspectRatio: string;
  promptTemplateVersion: string;
  relativePath: string;
  mimeType: string;
  sha256: string;
  sizeBytes: number;
  width: number;
  height: number;
  isCurrent: boolean;
  staleReason?: string | null;
  rowVersion: string;
  createdAtUtc: string;
  approvedAtUtc?: string | null;
  invalidatedAtUtc?: string | null;
  previewUrl?: string | null;
};

export type SceneFirstFrameQuote = {
  providerCode: string;
  modelCode: string;
  aspectRatio: string;
  width: number;
  height: number;
  estimatedCost: number;
  currencyCode: string;
  sourceCharacterReferenceId?: string | null;
  sourceCharacterName?: string | null;
  scenePlanVersion: number;
  scenePromptId: string;
  scenePromptVersion: number;
};

export type ProjectAssetType = 'Background' | 'Prop' | 'Item';

export type ProjectAssetSummary = {
  projectAssetId: string;
  assetType: ProjectAssetType;
  name: string;
  canonicalDescription: string;
  status: 'Draft' | 'Locked';
  currentVersion: number;
  lockedAtUtc?: string | null;
  updatedAtUtc: string;
  concurrencyToken: string;
  sceneIds: string[];
  assetKey: string;
  sourceKind: 'Manual' | 'AiGenerated';
  sourcePlanVersion?: number | null;
  generatedByProviderRequestId?: string | null;
};

export type SceneAssetAssignment = {
  sceneId: string;
  projectAssetIds: string[];
  hasUnlockedAssets: boolean;
  isValid: boolean;
  backgroundCount: number;
  promptCharacters: number;
  promptLimit: number;
  blockers?: string[] | null;
  requiredPromptCharacters: number;
};

export type ProjectAssetLibrary = {
  projectId: string;
  canEdit: boolean;
  assets: ProjectAssetSummary[];
  sceneAssignments: SceneAssetAssignment[];
};

export type MediaToolStatus = {
  ready: boolean;
  errorCode?: string | null;
  message: string;
  ffmpegVersion?: string | null;
  ffprobeVersion?: string | null;
  checkedAtUtc: string;
};

export type ProjectContentSummary = {
  scriptVersion: number;
  title: string;
  scriptFullText: string;
  hook?: string | null;
  angle?: string | null;
  audience?: string | null;
  callToAction?: string | null;
};

export type ProjectDashboard = {
  project: ProjectSummary;
  languageCode: string;
  createdAtUtc: string;
  totalScenes: number;
  approvedScenes: number;
  failedScenes: number;
  pendingJobs: number;
  runningJobs: number;
  failedJobs: number;
  overallProgressPercent: number;
  pipeline: PipelineStage[];
  render: RenderSummary;
  characters: CharacterSummary[];
  scenes: SceneSummary[];
  preview?: PreviewSummary | null;
  lastErrorMessage?: string | null;
  voiceCode?: string | null;
  voiceSpeakingRate?: number | null;
  audioStrategy: 'ProviderNative' | 'KlingNative' | string;
  speechProductionPolicy: 'ProviderNativeVerified' | 'CanonicalVoice' | string;
  videoProviderCode?: string | null;
  videoModelCode?: string | null;
  workflowStructureType?: string | null;
  effectiveGenerationLanguageCode?: string | null;
  requiresVietnameseContentRegeneration: boolean;
  content?: ProjectContentSummary | null;
  voiceProfiles?: VoiceProfileSummary[] | null;
};

export type AiModel = {
  providerCode: string;
  providerName: string;
  modelCode: string;
  displayName: string;
  modality: string;
  isDefault: boolean;
};

export type DashboardState = {
  profile: UserProfile;
  organizations: OrganizationSummary[];
  selectedOrganizationId: string;
  projects: ProjectSummary[];
  selectedProject?: ProjectDashboard | null;
  assetLibrary?: ProjectAssetLibrary | null;
  models: AiModel[];
  providerStatus: GenerationProviderStatus;
  mediaTools: MediaToolStatus;
  license?: CurrentLicense | null;
  generationRunning: boolean;
  features: DashboardFeatures;
  sceneFirstFrames: SceneFirstFrameSummary[];
  contentLanguageFailure?: ContentLanguageFailureSummary | null;
};

export type DashboardFeatures = {
  vietsubEnabled: boolean;
  speechSynchronizationEnabled: boolean;
  tikTokEnabled: boolean;
};

export type DesktopFeatureSettings = {
  speechSynchronizationEnabled: boolean;
  activeSpeechSynchronizationEnabled: boolean;
  restartRequired: boolean;
};

export type CurrentLicense = {
  hasActiveLicense: boolean;
  userLicenseId?: string | null;
  planCode?: string | null;
  planName?: string | null;
  status?: string | null;
  startsAtUtc?: string | null;
  expiresAtUtc?: string | null;
  maxActivatedDevices: number;
  activeDeviceCount: number;
  offlineGraceHours: number;
  currentDeviceActivated: boolean;
  serverTimeUtc: string;
  leaseExpiresAtUtc?: string | null;
  heartbeatIntervalSeconds: number;
  accessState?: 'Active' | 'Missing' | 'Expired' | 'Suspended' | 'Revoked' | 'DeviceLimit' | 'SessionLimit' | 'Unavailable' | null;
  accessReasonCode?: string | null;
  accessMessage?: string | null;
  assignedOrganizationId?: string | null;
  assignedOrganizationName?: string | null;
};

export type LicenseInvalidatedMessage = { message: string; license?: CurrentLicense | null };

export type LicenseOffer = {
  licensePlanId: string;
  planCode: string;
  name: string;
  description?: string | null;
  priceVnd: number;
  durationDays: number;
  maxActivatedDevices: number;
  marketingFeatures: string[];
  displayOrder: number;
  organizationSeatAvailable: boolean;
  organizationPoolName?: string | null;
  availableOrganizationSeats?: number | null;
};

export type LicensePaymentCheckout = {
  orderCode: string;
  transferCode: string;
  planCode: string;
  planName: string;
  durationDays: number;
  amountVnd: number;
  receiverBankCode: string;
  receiverAccountNumber: string;
  receiverAccountName: string;
  transferContent: string;
  qrImageUrl: string;
  status: string;
  createdAtUtc: string;
  expiresAtUtc: string;
  serverTimeUtc: string;
  reusedExistingPayment: boolean;
  isPaid: boolean;
  isFulfilled: boolean;
  isExpired: boolean;
  assignedOrganizationId?: string | null;
  assignedOrganizationName?: string | null;
  provisioningStatus?: string | null;
};

export type LicensePaymentStatus = {
  orderCode: string;
  status: string;
  expiresAtUtc: string;
  serverTimeUtc: string;
  paidAtUtc?: string | null;
  fulfilledAtUtc?: string | null;
  isPaid: boolean;
  isFulfilled: boolean;
  isExpired: boolean;
  failureCode?: string | null;
  message?: string | null;
  assignedOrganizationId?: string | null;
  assignedOrganizationName?: string | null;
  provisioningStatus?: string | null;
};

export type CurrentLicensePayment = {
  payment?: LicensePaymentCheckout | null;
};

export type ProviderSettings = {
  openAiConfigured: boolean;
  openAiKeyHint?: string | null;
  openAiModel: string;
  videoConfigured: boolean;
  videoProviderCode?: string | null;
  videoModel: string;
};

export type OpenAiVoiceOption = {
  voiceCode: string;
  displayName: string;
};

export type GenerationProviderStatus = {
  openAiReady: boolean;
  openAiModel?: string | null;
  openAiImageReady?: boolean;
  openAiImageModel?: string | null;
  openAiImageUnavailableCode?: string | null;
  openAiImageUnavailableMessage?: string | null;
  estimatedCharacterImageCost?: number | null;
  openAiVoiceReady?: boolean;
  openAiVoiceModel?: string | null;
  openAiVoiceUnavailableCode?: string | null;
  openAiVoiceUnavailableMessage?: string | null;
  estimatedSceneVoiceCost?: number | null;
  openAiTranscriptionReady?: boolean;
  openAiTranscriptionModel?: string | null;
  openAiTranscriptionUnavailableCode?: string | null;
  openAiTranscriptionUnavailableMessage?: string | null;
  estimatedSpeechVerificationCost?: number | null;
  canonicalVoiceEnabled?: boolean;
  speechVerificationEnabled?: boolean;
  canonicalVoiceReady?: boolean;
  canonicalVoiceUnavailableCode?: string | null;
  canonicalVoiceUnavailableMessage?: string | null;
  openAiVoiceOptions?: OpenAiVoiceOption[] | null;
  klingReady: boolean;
  klingModel?: string | null;
  klingUnavailableCode?: string | null;
  klingUnavailableMessage?: string | null;
  estimatedKlingCostPerSecond?: number | null;
  videoReady: boolean;
  videoProviderCode?: string | null;
  videoProviderName?: string | null;
  videoModel?: string | null;
  videoUnavailableCode?: string | null;
  videoUnavailableMessage?: string | null;
  estimatedVideoCostPerSecond?: number | null;
  videoNativeAudio?: boolean;
  videoResolution?: string | null;
  organizationId?: string | null;
  organizationName?: string | null;
  budgetLimit?: number;
  reservedCost?: number;
  actualCost?: number;
  remainingBudget?: number;
  currencyCode?: string | null;
};

export type HostMessage<T = unknown> = {
  type: string;
  requestId?: string | null;
  payload?: T;
  error?: { code: string; message: string; errors?: Record<string, string[]> | null };
};

export type ContentLanguageViolation = {
  field: string;
  reason: 'required' | 'language_invalid' | 'speech_too_short' | 'speech_too_long';
  estimatedDurationSeconds?: number | null;
  targetMinimumSeconds?: number | null;
  targetMaximumSeconds?: number | null;
};

export type ContentLanguageFailureSummary = {
  failedProviderRequestId: string;
  errorCode: string;
  message: string;
  violations: ContentLanguageViolation[];
  canRepair: boolean;
};

export type ContentRepairQuote = {
  failedProviderRequestId: string;
  providerCode: string;
  modelCode: string;
  violations: ContentLanguageViolation[];
  estimatedCost: number;
  currencyCode: string;
};

export type CreateProjectPayload = {
  organizationId?: string;
  topic: string;
  aspectRatio: string;
  languageCode: string;
  speechProductionPolicy: 'ProviderNativeVerified' | 'CanonicalVoice';
  voiceCode?: string | null;
  voiceSpeakingRate?: number | null;
};

export type CreateShortVideoPayload = {
  organizationId?: string;
  content: string;
  aspectRatio: '9:16' | '16:9' | '1:1';
  durationSeconds: number;
  audioEnabled: boolean;
};

export type UpdateScenePayload = {
  sceneId: string;
  narration: string;
  visualDescription: string;
  prompt: string;
  speechMode: 'None' | 'OnCameraDialogue' | 'NativeVoiceOver';
  voiceStyle?: string | null;
  ambientAudio?: string | null;
  soundEffects?: string | null;
};

export type UpdateCharacterPayload = {
  characterId: string;
  name: string;
  role: string;
  visualIdentity: string;
  wardrobe: string;
  immutableTraits: string[];
  forbiddenChanges: string[];
  voiceCode?: string | null;
  voiceSpeakingRate?: number | null;
};

export type CreateProjectAssetPayload = {
  assetType: ProjectAssetType;
  name: string;
  canonicalDescription: string;
};

export type UpdateProjectAssetPayload = CreateProjectAssetPayload & {
  projectAssetId: string;
  concurrencyToken: string;
};

export type DesktopRelease = {
  releaseId: string;
  productName: string;
  version: string;
  buildNumber: number;
  channel: string;
  platform: string;
  minimumSupportedVersion?: string | null;
  releaseNotes?: string | null;
  publishedAtUtc: string;
  fileName: string;
  downloadUrl: string;
  sizeBytes: number;
  sha256: string;
};

export type DesktopUpdateNotice = {
  isUpdateAvailable: boolean;
  isMandatory: boolean;
  release?: DesktopRelease | null;
};

export type DesktopUpdateProgress = {
  stage: string;
  percent: number;
  message: string;
};
