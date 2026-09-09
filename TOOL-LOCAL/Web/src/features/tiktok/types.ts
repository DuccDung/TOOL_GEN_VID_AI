export type TikTokConnection = {
  connectionId: string;
  creatorUsername: string;
  creatorNickname: string;
  scopes: string[];
  accessTokenExpiresAtUtc: string;
  refreshTokenExpiresAtUtc: string;
  updatedAtUtc: string;
};

export type TikTokFeatureState = {
  enabled: boolean;
  configured: boolean;
  connection?: TikTokConnection | null;
  activePublish?: TikTokPublishStatus | null;
  isCredentialVerification?: boolean;
  unavailableReason?: string | null;
};

export type TikTokCreatorInfo = {
  creatorUsername: string;
  creatorNickname: string;
  privacyLevelOptions: string[];
  commentDisabled: boolean;
  duetDisabled: boolean;
  stitchDisabled: boolean;
  maximumVideoDurationSeconds: number;
  publishingIssue?: { code: string; message: string } | null;
};

export type TikTokMedia = {
  mediaId: string;
  fileName: string;
  mimeType: string;
  sizeBytes: number;
  durationSeconds: number;
  width: number;
  height: number;
  framesPerSecond: number;
  videoCodec: string;
  previewUrl: string;
};

export type TikTokUploadProgress = {
  publishJobId: string;
  uploadedBytes: number;
  totalBytes: number;
  percent: number;
  completedChunks: number;
  totalChunks: number;
};

export type TikTokPublishStatus = {
  publishJobId: string;
  status: string;
  failureReason?: string | null;
  uploadedBytes: number;
  publicPostIds: string[];
  updatedAtUtc: string;
  isTerminal: boolean;
};

export type TikTokPublishPayload = {
  title: string;
  privacyLevel: string;
  allowComment: boolean;
  allowDuet: boolean;
  allowStitch: boolean;
  commercialContent: boolean;
  brandContent: boolean;
  brandOrganic: boolean;
  isAiGenerated: boolean;
  consentConfirmed: boolean;
};

export type TikTokModuleState = {
  feature: TikTokFeatureState;
  creator: TikTokCreatorInfo | null;
  media: TikTokMedia | null;
  upload: TikTokUploadProgress | null;
  publish: TikTokPublishStatus | null;
  loading: boolean;
  busy: boolean;
  uploadCompleted: boolean;
  error: string | null;
  publishFeedback: {
    phase: 'preparing' | 'uploading' | 'processing' | 'success' | 'error' | 'cancelled';
    open: boolean;
    message?: string | null;
  } | null;
};
