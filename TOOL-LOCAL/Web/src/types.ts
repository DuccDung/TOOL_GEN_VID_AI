export type UserProfile = {
  userId: string;
  email: string;
  displayName?: string | null;
  accountStatus: string;
  roles: string[];
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
};

export type SceneCharacterSummary = {
  characterId: string;
  name: string;
  status: string;
  referencePreviewUrl?: string | null;
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
  videoProviderCode?: string | null;
  videoModelCode?: string | null;
  workflowStructureType?: string | null;
  effectiveGenerationLanguageCode?: string | null;
  requiresVietnameseContentRegeneration: boolean;
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
};

export type DashboardFeatures = {
  vietsubEnabled: boolean;
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
};

export type ProviderSettings = {
  openAiConfigured: boolean;
  openAiKeyHint?: string | null;
  openAiModel: string;
  videoConfigured: boolean;
  videoProviderCode?: string | null;
  videoModel: string;
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
  requestId?: string;
  payload?: T;
  error?: { code: string; message: string };
};

export type CreateProjectPayload = {
  topic: string;
  aspectRatio: string;
  languageCode: string;
};

export type CreateShortVideoPayload = {
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
