export type PublishingPlatform = 'TikTok' | 'Facebook' | 'YouTube';
export type PublishingTarget = { platform: PublishingPlatform; connectionId: string; privacy: string; madeForKids: boolean; brandOrganic: boolean; brandContent: boolean; title?: string | null };
export type PublishingInput = {
  title: string; description: string; characterImageId: string; productImageId: string;
  startDate: string; endDate: string; publishTime: string; timeZoneId: string; weekdays: number;
  leadMinutes: number; latePolicy: 'SameDay' | 'Skip'; durationSeconds: number; aspectRatio: string;
  maximumCostPerRun: number; targets: PublishingTarget[];
};
export type PublishingImage = { imageId: string; role: 'Character' | 'Product'; info: { sha256: string; mimeType: string; sizeBytes: number; width: number; height: number } };
export type PublishingSchedule = { scheduleId: string; revision: number; status: string; input: PublishingInput; nextPublishAtUtc: string | null; updatedAtUtc: string };
export type PublishingDelivery = { deliveryId: string; platform: PublishingPlatform; accountName: string; status: string; postUrl: string | null; message: string | null };
export type PublishingRun = { runId: string; scheduleId: string; title: string; generateAtUtc: string; publishAtUtc: string; deadlineAtUtc: string;
  status: string; errorCode: string | null; message: string | null; projectId: string | null; estimatedCost: number;
  deliveries: PublishingDelivery[]; updatedAtUtc: string; mediaSha256: string | null; input: PublishingInput; canResume: boolean };
export type PublishingConnection = { connectionId: string; platform: PublishingPlatform; displayName: string; status: string };
export type PublishingState = { enabled: boolean; unavailableReason: string | null; schedules: PublishingSchedule[]; runs: PublishingRun[];
  connections: PublishingConnection[]; platforms: { platform: PublishingPlatform; configured: boolean; message: string | null }[] };
export type PublishingCreator = { connectionId: string; creatorNickname: string; privacyLevelOptions: string[]; maximumVideoDurationSeconds: number;
  publishingIssue?: { code: string; message: string } | null };
export type PublishingPreview = { run: PublishingRun; previewUrl: string; creators: PublishingCreator[] };
export const emptyPublishingState: PublishingState = { enabled: false, unavailableReason: null, schedules: [], runs: [], connections: [], platforms: [] };
export const publishingStatus: Record<string, string> = {
  Draft: 'Bản nháp', Active: 'Đang bật', Paused: 'Tạm dừng', Cancelled: 'Đã hủy', Completed: 'Hoàn tất',
  Queued: 'Chờ tạo', PreparingImage: 'Chuẩn bị ảnh', SubmittingImage: 'Đang tạo ảnh', ImageReady: 'Ảnh đã tạo',
  PreparingVideo: 'Chuẩn bị video', SubmittingVideo: 'Đang gửi tạo video', Generating: 'Đang tạo video', Validating: 'Kiểm tra video',
  AwaitingReview: 'Chờ duyệt TikTok', ReadyToPublish: 'Chờ giờ đăng', Publishing: 'Đang đăng', PartialFailure: 'Một phần chưa đăng',
  Failed: 'Thất bại', NeedsAttention: 'Cần xử lý', Skipped: 'Đã bỏ lượt', Pending: 'Chờ đăng', Initializing: 'Khởi tạo', Uploading: 'Đang tải',
  Transferring: 'Đang truyền', Uploaded: 'Đã tải lên', Processing: 'Nền tảng xử lý', Unknown: 'Cần đối soát'
};
