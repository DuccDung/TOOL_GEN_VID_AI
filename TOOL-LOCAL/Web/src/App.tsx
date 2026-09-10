import { type ReactNode, useEffect, useId, useLayoutEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import {
  ArrowLeft,
  ArrowRight,
  Bell,
  Bot,
  CalendarDays,
  Check,
  CircleCheck,
  ChevronDown,
  CircleHelp,
  Clapperboard,
  Clock3,
  CreditCard,
  Crown,
  Copy,
  Database,
  Download,
  FileText,
  Film,
  FolderOpen,
  Gauge,
  Home,
  Image as ImageIcon,
  KeyRound,
  LayoutGrid,
  Languages,
  Library,
  Link2,
  ListVideo,
  LockKeyhole,
  LoaderCircle,
  LogOut,
  MapPin,
  Menu,
  Package,
  PanelLeftClose,
  PanelLeftOpen,
  Pause,
  Pencil,
  Play,
  Plus,
  RefreshCw,
  Search,
  Save,
  Settings,
  ShieldCheck,
  Sparkles,
  TriangleAlert,
  Trash2,
  UnlockKeyhole,
  Upload,
  UserRound,
  Users,
  Volume2,
  VolumeX,
  WandSparkles,
  X
} from 'lucide-react';
import type { LucideIcon } from 'lucide-react';
import googleLogo from '@lobehub/icons-static-svg/icons/google-color.svg';
import klingLogo from '@lobehub/icons-static-svg/icons/kling-color.svg';
import openAiLogo from '@lobehub/icons-static-svg/icons/openai.svg';
import pikaLogo from '@lobehub/icons-static-svg/icons/pika.svg';
import runwayLogo from '@lobehub/icons-static-svg/icons/runway.svg';
import { isHosted, postToHost, subscribeToHost } from './bridge';
import { isLicenseLocked, licenseAccessTitle } from './licenseAccess';
import {
  formatContentLanguageError,
  formatViolation,
  parseContentLanguageFailure,
  restoreContentLanguageFailure,
  shortProviderRequestId,
  type ContentLanguageFailureView
} from './contentLanguageError';
import { VietsubPage } from './features/vietsub/VietsubPage';
import { useVietsubModule } from './features/vietsub/useVietsubModule';
import { getSceneFirstFrameAssetBlocker } from './sceneAssetValidation';
import { buildSpeechTranscriptDiff, type SpeechDiffSegment } from './speechTranscriptDiff';
import { assessSpeechPacing } from './speechPacing';
import { getAvailableVoiceOptions, resolveVoiceOption, voiceDisplayName } from './voiceCatalog';
import {
  getCanonicalVoiceJourney,
  getStoryboardActionSummary,
  isCanonicalSpeechScene,
  needsCanonicalVoicePreparation
} from './videoWorkflowUx';
import type {
  AiModel,
  CharacterSummary,
  CreateProjectAssetPayload,
  CreateProjectPayload,
  CreateShortVideoPayload,
  DashboardState,
  DesktopRelease,
  DesktopFeatureSettings,
  DesktopUpdateNotice,
  DesktopUpdateProgress,
  GenerationProviderStatus,
  HostMessage,
  CurrentLicensePayment,
  ContentRepairQuote,
  LicenseOffer,
  LicenseInvalidatedMessage,
  LicensePaymentCheckout,
  LicensePaymentStatus,
  MediaToolStatus,
  OpenAiVoiceOption,
  OrganizationSummary,
  PipelineStage,
  ProjectDashboard,
  ProjectAssetLibrary,
  ProjectAssetSummary as ProjectTextAsset,
  ProjectAssetType,
  ProjectSummary,
  ProviderSettings,
  SceneFirstFrameQuote,
  SceneFirstFrameSummary,
  SceneSpeechVerificationQuote,
  SceneSummary,
  CanonicalVoiceQuote,
  VoiceProfilePreviewQuote,
  VoiceProfileSummary,
  VoiceCatalogPreviewPlayback,
  VoiceCatalogPreviewQuote,
  UpdateScenePayload,
  UpdateCharacterPayload,
  UpdateProjectAssetPayload,
} from './types';

type Page = 'create' | 'longVideo' | 'shortVideo' | 'projects' | 'vietsub' | 'apiKeys' | 'settings';
type LongVideoStepId = 'setup' | 'content' | 'assets' | 'storyboard' | 'export';
type LongVideoStep = {
  id: LongVideoStepId;
  label: string;
  shortLabel: string;
  description: string;
  icon: LucideIcon;
};
type StoryboardFilter = 'all' | 'pending' | 'processing' | 'review' | 'approved' | 'failed';
type Toast = { id: number; message: string; error?: boolean };
type ConfirmationIntent = 'default' | 'download';
type ConfirmationRequest = {
  eyebrow: string;
  title: string;
  description: string;
  note?: string;
  intent?: ConfirmationIntent;
  noteTone?: 'warning' | 'info';
  confirmLabel: string;
  onConfirm: () => void;
};
type ServiceError = {
  title: string;
  description: string;
};
type SceneSaveState = {
  sceneId: string;
  status: 'saving' | 'succeeded' | 'failed';
  message?: string;
};
type PendingSceneSave = {
  requestId: string;
  sceneId: string;
};
type LicenseRequestKind = 'offers' | 'current' | 'create' | 'status' | 'refresh';
type PendingVoiceCatalogPreview = {
  voiceCode: string;
  speakingRate: number;
  previewKey: string;
};
type VoiceCatalogPreviewControls = {
  speakingRate: number;
  previews: Record<string, VoiceCatalogPreviewPlayback>;
  pendingKey: string | null;
  onPreview: (voiceCode: string, speakingRate: number) => void;
  contextProjectName?: string;
};

function resolveSelectedProjectPage(workflowStructureType?: string | null): Extract<Page, 'shortVideo' | 'longVideo'> {
  return workflowStructureType === 'DirectShortVideo' ? 'shortVideo' : 'longVideo';
}

function isSelectedProjectResponse(pendingRequestId: string | null, responseRequestId?: string | null): boolean {
  return pendingRequestId !== null && pendingRequestId === responseRequestId;
}

const pageHeaders: Record<Page, { title: string; subtitle: string }> = {
  create: {
    title: 'Tạo video mới',
    subtitle: 'Nhập chủ đề hoặc ý tưởng, AI sẽ giúp bạn tạo video hoàn chỉnh chỉ với vài bước.'
  },
  longVideo: {
    title: 'Tạo Video Dài',
    subtitle: 'Tạo nội dung, chia cảnh, đồng bộ nhân vật và dựng video hoàn chỉnh.'
  },
  shortVideo: {
    title: 'Tạo video ngắn',
    subtitle: 'Nhập nội dung hình ảnh và tạo trực tiếp một clip Kling từ 1 đến 15 giây với Native Audio.'
  },
  projects: {
    title: 'Dự án của tôi',
    subtitle: 'Quản lý và tiếp tục các dự án video đã tạo.'
  },
  vietsub: {
    title: 'Dịch phụ đề',
    subtitle: 'Tạo phụ đề tiếng Việt, giọng đọc và video hoàn chỉnh trong một workspace riêng.'
  },
  apiKeys: {
    title: 'API AI tổ chức',
    subtitle: 'Trạng thái OpenAI, provider video và ngân sách do tổ chức quản lý tập trung.'
  },
  settings: {
    title: 'Cài đặt',
    subtitle: 'Điều chỉnh các tính năng cục bộ áp dụng cho ứng dụng trên máy này.'
  }
};

const emptyState: DashboardState = {
  profile: {
    userId: '',
    email: '',
    displayName: 'Đang tải tài khoản',
    accountStatus: '',
    roles: []
  },
  organizations: [],
  selectedOrganizationId: '',
  projects: [],
  selectedProject: null,
  assetLibrary: null,
  models: [],
  providerStatus: {
    openAiReady: false,
    openAiModel: null,
    openAiVoiceOptions: [],
    klingReady: false,
    klingModel: null,
    videoReady: false,
    videoModel: null
  },
  mediaTools: {
    ready: false,
    errorCode: 'media_tool_check_pending',
    message: 'Đang kiểm tra FFmpeg và FFprobe.',
    ffmpegVersion: null,
    ffprobeVersion: null,
    checkedAtUtc: ''
  },
  generationRunning: false,
  features: {
    vietsubEnabled: false,
    speechSynchronizationEnabled: false
  },
  sceneFirstFrames: [],
  contentLanguageFailure: null
};
type PendingFirstFrameQuote = {
  scene: SceneSummary;
  attempt: number;
  regenerate: boolean;
};

type SceneFirstFrameOperation = {
  sceneId: string;
  status: 'generating' | 'failed';
  message?: string;
};

const primaryMenu: Array<{
  label: string;
  icon: LucideIcon;
  page?: Page;
  feature?: keyof DashboardState['features'];
}> = [
  { label: 'Dashboard', icon: Home, page: 'create' },
  { label: 'Dự án của tôi', icon: FolderOpen, page: 'projects' },
  { label: 'Tạo Video Dài', icon: Film, page: 'longVideo' },
  { label: 'Tạo Video Ngắn', icon: Play, page: 'shortVideo' },
  { label: 'Dịch phụ đề', icon: Languages, page: 'vietsub', feature: 'vietsubEnabled' },
  { label: 'Nhân vật AI', icon: Users },
  { label: 'Thư viện video', icon: Library },
  { label: 'Lịch sử render', icon: Clock3 },
  { label: 'Lên lịch xuất bản', icon: CalendarDays }
];

const secondaryMenu: Array<{ label: string; icon: LucideIcon; page?: Page }> = [
  { label: 'AI Models', icon: Bot },
  { label: 'API AI tổ chức', icon: KeyRound, page: 'apiKeys' },
  { label: 'Tài nguyên', icon: Database },
  { label: 'Cài đặt', icon: Settings, page: 'settings' },
  { label: 'Thanh toán', icon: CreditCard },
  { label: 'Hướng dẫn', icon: CircleHelp }
];

const sidebarCollapsedStorageKey = 'videomaker.sidebar.collapsed';

const stageIcons: Record<string, LucideIcon> = {
  research: Search,
  script: FileText,
  scenes: LayoutGrid,
  video: Film,
  render: Clapperboard
};

const stageColors: Record<string, string> = {
  research: '#2fa66f',
  script: '#f59e0b',
  scenes: '#3978d2',
  video: '#7854b7',
  render: '#ef5d57'
};

const longVideoSteps: LongVideoStep[] = [
  {
    id: 'setup',
    label: 'Thiết lập dự án',
    shortLabel: 'Thiết lập',
    description: 'Chọn chủ đề, tỉ lệ khung hình, ngôn ngữ và tạo workspace.',
    icon: Settings
  },
  {
    id: 'content',
    label: 'Nội dung & kịch bản',
    shortLabel: 'Nội dung',
    description: 'Sinh content plan, kịch bản và cấu trúc các cảnh bằng OpenAI.',
    icon: FileText
  },
  {
    id: 'assets',
    label: 'Tài sản nhất quán',
    shortLabel: 'Tài sản',
    description: 'Khóa nhân vật và thư viện text bối cảnh, đạo cụ, item cho từng cảnh.',
    icon: ImageIcon
  },
  {
    id: 'storyboard',
    label: 'Storyboard & clip',
    shortLabel: 'Storyboard',
    description: 'Chỉnh từng cảnh, tạo clip, nghe và duyệt Native Audio.',
    icon: LayoutGrid
  },
  {
    id: 'export',
    label: 'Duyệt & xuất video',
    shortLabel: 'Xuất video',
    description: 'Kiểm tra các cảnh đã duyệt và dựng video cuối bằng FFmpeg.',
    icon: Clapperboard
  }
];

const storyboardFilters: Array<{ id: StoryboardFilter; label: string }> = [
  { id: 'all', label: 'Tất cả' },
  { id: 'pending', label: 'Chưa tạo' },
  { id: 'processing', label: 'Đang xử lý' },
  { id: 'review', label: 'Cần duyệt' },
  { id: 'approved', label: 'Đã duyệt' },
  { id: 'failed', label: 'Lỗi' }
];

type ModelDisplay = {
  id: string;
  name: string;
  provider: string;
  description: string;
  secondary: string;
  brand: 'kling' | 'google' | 'runway' | 'pika' | 'sora' | 'generic';
  badge?: string;
  configured: boolean;
};

function App() {
  const [dashboard, setDashboard] = useState<DashboardState>(emptyState);
  const latestDashboardRef = useRef(dashboard);
  const [page, setPage] = useState<Page>('create');
  const [busy, setBusy] = useState(true);
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const [sidebarCollapsed, setSidebarCollapsed] = useState(() => {
    if (typeof window === 'undefined') return false;
    try {
      return window.localStorage.getItem(sidebarCollapsedStorageKey) === 'true';
    } catch {
      return false;
    }
  });
  const [toasts, setToasts] = useState<Toast[]>([]);
  const [updateNotice, setUpdateNotice] = useState<DesktopUpdateNotice | null>(null);
  const [updateProgress, setUpdateProgress] = useState<DesktopUpdateProgress | null>(null);
  const [updateError, setUpdateError] = useState<string | null>(null);
  const [providerSettings, setProviderSettings] = useState<ProviderSettings | null>(null);
  const [desktopSettings, setDesktopSettings] = useState<DesktopFeatureSettings>({
    speechSynchronizationEnabled: false,
    activeSpeechSynchronizationEnabled: false,
    restartRequired: false
  });
  const [confirmation, setConfirmation] = useState<ConfirmationRequest | null>(null);
  const [serviceError, setServiceError] = useState<ServiceError | null>(null);
  const [contentGenerationError, setContentGenerationError] = useState<string | null>(null);
  const [contentLanguageFailure, setContentLanguageFailure] = useState<ContentLanguageFailureView | null>(null);
  const [characterImageBusyId, setCharacterImageBusyId] = useState<string | null>(null);
  const [assetConfirmBusyId, setAssetConfirmBusyId] = useState<string | null>(null);
  const [mediaInstallProgress, setMediaInstallProgress] = useState<DesktopUpdateProgress | null>(null);
  const [sceneSaveState, setSceneSaveState] = useState<SceneSaveState | null>(null);
  const [licenseOffers, setLicenseOffers] = useState<LicenseOffer[]>([]);
  const [licenseCheckout, setLicenseCheckout] = useState<LicensePaymentCheckout | null>(null);
  const [licensePaymentStatus, setLicensePaymentStatus] = useState<LicensePaymentStatus | null>(null);
  const [licensePaymentBusy, setLicensePaymentBusy] = useState(false);
  const [licensePaymentError, setLicensePaymentError] = useState<string | null>(null);
  const [logoutBusy, setLogoutBusy] = useState(false);
  const logoutRequestRef = useRef<string | null>(null);
  const [firstFramePreview, setFirstFramePreview] = useState<SceneFirstFrameSummary | null>(null);
  const [firstFrameOperation, setFirstFrameOperation] = useState<SceneFirstFrameOperation | null>(null);
  const [voiceCatalogPreviews, setVoiceCatalogPreviews] = useState<Record<string, VoiceCatalogPreviewPlayback>>({});
  const [voiceCatalogPreviewPendingKey, setVoiceCatalogPreviewPendingKey] = useState<string | null>(null);
  const pendingSceneSaveRef = useRef<PendingSceneSave | null>(null);
  const pendingFirstFrameQuoteRef = useRef(new Map<string, PendingFirstFrameQuote>());
  const pendingFirstFrameOperationRef = useRef(new Map<string, string>());
  const pendingContentRepairQuoteRef = useRef(new Map<string, string>());
  const pendingSpeechVerificationQuoteRef = useRef(new Map<string, SceneSummary>());
  const pendingVoicePreviewQuoteRef = useRef(new Map<string, VoiceProfileSummary>());
  const pendingVoiceCatalogPreviewQuoteRef = useRef(new Map<string, PendingVoiceCatalogPreview>());
  const pendingVoiceCatalogPreviewOperationRef = useRef(new Map<string, PendingVoiceCatalogPreview>());
  const pendingVideoVoiceQuoteRef = useRef(new Map<string, string[]>());
  const vietsub = useVietsubModule(
    dashboard.features.vietsubEnabled,
    dashboard.selectedOrganizationId
  );
  const selectedProjectRequestRef = useRef<string | null>(null);
  const licenseRequestsRef = useRef(new Map<string, LicenseRequestKind>());
  const licenseBootstrapRequestedRef = useRef(false);
  const licenseStatusInFlightRef = useRef(false);

  useLayoutEffect(() => {
    latestDashboardRef.current = dashboard;
  }, [dashboard]);

  const notify = (message: string, error = false) => {
    const id = Date.now();
    setToasts((current) => [...current, { id, message, error }]);
    window.setTimeout(() => setToasts((current) => current.filter((item) => item.id !== id)), 3600);
  };

  useEffect(() => {
    const restored = restoreContentLanguageFailure(dashboard.contentLanguageFailure);
    setContentLanguageFailure(restored);
    setContentGenerationError(restored ? restored.message : null);
  }, [dashboard.selectedProject?.project.projectId, dashboard.contentLanguageFailure]);

  useEffect(() => {
    setVoiceCatalogPreviews({});
    setVoiceCatalogPreviewPendingKey(null);
    pendingVoiceCatalogPreviewQuoteRef.current.clear();
    pendingVoiceCatalogPreviewOperationRef.current.clear();
  }, [dashboard.selectedOrganizationId, dashboard.selectedProject?.project.projectId]);

  const postLicenseRequest = <T,>(kind: LicenseRequestKind, type: string, payload?: T) => {
    const requestId = postToHost(type, payload);
    licenseRequestsRef.current.set(requestId, kind);
    return requestId;
  };

  const requestLicenseBootstrap = () => {
    if (licenseBootstrapRequestedRef.current) return;
    licenseBootstrapRequestedRef.current = true;
    setLicensePaymentError(null);
    postLicenseRequest('offers', 'license.offers.get');
    postLicenseRequest('current', 'license.payment.current.get');
  };

  const requestLogout = () => {
    if (logoutRequestRef.current) return;
    setLicensePaymentError(null);
    setLogoutBusy(true);
    logoutRequestRef.current = postToHost('auth.logout');
  };

  useEffect(() => {
    const unsubscribe = subscribeToHost((message: HostMessage) => {
      if (message.type === 'dashboard.state' && message.payload) {
        const nextDashboard = message.payload as DashboardState;
        setDashboard((current) => isLicenseLocked(nextDashboard.license) ? {
          ...current,
          profile: nextDashboard.profile,
          license: nextDashboard.license
        } : {
          ...nextDashboard,
          features: nextDashboard.features ?? { vietsubEnabled: false, speechSynchronizationEnabled: false },
          sceneFirstFrames: nextDashboard.sceneFirstFrames ?? [],
          contentLanguageFailure: nextDashboard.contentLanguageFailure ?? null
        });
        setDesktopSettings((current) => current.restartRequired
          ? current
          : {
              speechSynchronizationEnabled: nextDashboard.features?.speechSynchronizationEnabled ?? false,
              activeSpeechSynchronizationEnabled: nextDashboard.features?.speechSynchronizationEnabled ?? false,
              restartRequired: false
            });
        if (!nextDashboard.generationRunning) setCharacterImageBusyId(null);
        setAssetConfirmBusyId(null);
        setBusy(false);
        if (isSelectedProjectResponse(selectedProjectRequestRef.current, message.requestId)) {
          selectedProjectRequestRef.current = null;
          setPage(resolveSelectedProjectPage(nextDashboard.selectedProject?.workflowStructureType));
        }
        if (message.requestId && licenseRequestsRef.current.get(message.requestId) === 'refresh') {
          licenseRequestsRef.current.delete(message.requestId);
          setLicensePaymentBusy(false);
        }
        if (nextDashboard.license && !isLicenseLocked(nextDashboard.license)) {
          licenseBootstrapRequestedRef.current = false;
          licenseStatusInFlightRef.current = false;
          licenseRequestsRef.current.clear();
          setLicenseCheckout(null);
          setLicensePaymentStatus(null);
          setLicensePaymentError(null);
          setLicensePaymentBusy(false);
        } else if (nextDashboard.license?.accessState === 'Missing' || nextDashboard.license?.accessState === 'Expired') {
          requestLicenseBootstrap();
        }
        return;
      }

      if (message.type === 'license.offers' && message.payload) {
        if (message.requestId) licenseRequestsRef.current.delete(message.requestId);
        setLicenseOffers(message.payload as LicenseOffer[]);
        return;
      }

      if (message.type === 'license.payment.current' && message.payload) {
        if (message.requestId) licenseRequestsRef.current.delete(message.requestId);
        const current = message.payload as CurrentLicensePayment;
        if (current.payment) {
          setLicenseCheckout(current.payment);
          setLicensePaymentStatus(null);
        }
        return;
      }

      if (message.type === 'license.payment.checkout' && message.payload) {
        if (message.requestId) licenseRequestsRef.current.delete(message.requestId);
        setLicenseCheckout(message.payload as LicensePaymentCheckout);
        setLicensePaymentStatus(null);
        setLicensePaymentError(null);
        setLicensePaymentBusy(false);
        return;
      }

      if (message.type === 'license.payment.status' && message.payload) {
        const status = message.payload as LicensePaymentStatus;
        licenseStatusInFlightRef.current = status.isFulfilled;
        if (message.requestId && !status.isFulfilled) licenseRequestsRef.current.delete(message.requestId);
        setLicensePaymentStatus(status);
        setLicensePaymentError(null);
        setLicensePaymentBusy(status.isFulfilled);
        return;
      }

      if (message.type === 'license.activated' && message.payload) {
        if (message.requestId) licenseRequestsRef.current.delete(message.requestId);
        licenseStatusInFlightRef.current = false;
        setLicensePaymentBusy(false);
        const license = message.payload as DashboardState['license'];
        setDashboard((current) => ({ ...current, license }));
        notify(license?.assignedOrganizationName
          ? `Gói và tổ chức ${license.assignedOrganizationName} đã sẵn sàng.`
          : 'Gói sử dụng đã sẵn sàng.');
        return;
      }

      if (message.type === 'scene.first-frame.quote' && message.payload && message.requestId) {
        const pending = pendingFirstFrameQuoteRef.current.get(message.requestId);
        pendingFirstFrameQuoteRef.current.delete(message.requestId);
        setBusy(false);
        if (!pending) return;
        const quote = message.payload as SceneFirstFrameQuote;
        setConfirmation({
          eyebrow: pending.regenerate ? 'XÁC NHẬN SINH LẠI FIRST-FRAME' : 'XÁC NHẬN TẠO FIRST-FRAME',
          title: `${pending.regenerate ? 'Sinh lại' : 'Tạo'} first-frame cho cảnh ${pending.scene.sequenceNumber}?`,
          description: `${quote.providerCode}/${quote.modelCode} sẽ tạo ảnh ${quote.width}×${quote.height} (${quote.aspectRatio}) cho cảnh ${pending.scene.sequenceNumber}.${quote.sourceCharacterName ? ` Nguồn nhận diện: ${quote.sourceCharacterName}.` : ' Đây là cảnh B-roll không dùng ảnh nhân vật.'}`,
          note: `Đây là request AI có phí. Server sẽ giữ khoảng ${formatMoney(quote.estimatedCost, quote.currencyCode)} theo rate Active trước khi gọi OpenAI.`,
          confirmLabel: pending.regenerate ? 'Sinh lại first-frame' : 'Tạo first-frame bằng AI',
          onConfirm: () => {
            setBusy(true);
            setFirstFrameOperation({ sceneId: pending.scene.sceneId, status: 'generating' });
            const operationRequestId = postToHost('scene.first-frame.generate', {
              sceneId: pending.scene.sceneId,
              attempt: pending.attempt
            });
            pendingFirstFrameOperationRef.current.set(operationRequestId, pending.scene.sceneId);
          }
        });
        return;
      }

      if (message.type === 'scene.speech.verify.quote' && message.payload && message.requestId) {
        const scene = pendingSpeechVerificationQuoteRef.current.get(message.requestId);
        pendingSpeechVerificationQuoteRef.current.delete(message.requestId);
        setBusy(false);
        if (!scene) return;
        const quote = message.payload as SceneSpeechVerificationQuote;
        setConfirmation({
          eyebrow: 'XÁC NHẬN KIỂM TRA LỜI NÓI',
          title: `Chạy ASR cho cảnh ${scene.sequenceNumber}?`,
          description: `${quote.providerCode}/${quote.modelCode} sẽ đối chiếu audio với lời đã khóa của cảnh. Thời lượng tính phí: ${quote.billableAudioSeconds} giây.`,
          note: `Đây là request AI có phí. Server sẽ giữ khoảng ${formatMoney(quote.estimatedCost, quote.currencyCode)} theo rate Active trước khi gửi audio.`,
          confirmLabel: 'Kiểm tra transcript',
          onConfirm: () => {
            setBusy(true);
            postToHost('scene.speech.verify', { sceneId: scene.sceneId });
          }
        });
        return;
      }

      if (message.type === 'voice-profile.preview.quote' && message.payload && message.requestId) {
        const version = pendingVoicePreviewQuoteRef.current.get(message.requestId);
        pendingVoicePreviewQuoteRef.current.delete(message.requestId);
        setBusy(false);
        if (!version) return;
        const quote = message.payload as VoiceProfilePreviewQuote;
        setConfirmation({
          eyebrow: 'XÁC NHẬN NGHE THỬ GIỌNG',
          title: `Tạo preview voice version ${version.version}?`,
          description: `${quote.providerCode}/${quote.modelCode} sẽ đọc một câu mẫu tiếng Việt bằng ${voiceDisplayName(version.voiceCode)}, tốc độ ${version.speakingRate}×.`,
          note: `Đây là request AI có phí. Server sẽ giữ khoảng ${formatMoney(quote.estimatedCost, quote.currencyCode)} theo rate Active trước outbound.`,
          confirmLabel: 'Tạo audio preview',
          onConfirm: () => {
            setBusy(true);
            postToHost('voice-profile.preview', {
              voiceProfileVersionId: version.voiceProfileVersionId,
              expectedVoiceSnapshotHash: version.snapshotHash
            });
          }
        });
        return;
      }


      if (message.type === 'voice-catalog.preview.quote' && message.payload && message.requestId) {
        const pending = pendingVoiceCatalogPreviewQuoteRef.current.get(message.requestId);
        pendingVoiceCatalogPreviewQuoteRef.current.delete(message.requestId);
        setBusy(false);
        setVoiceCatalogPreviewPendingKey(null);
        if (!pending) return;
        const quote = message.payload as VoiceCatalogPreviewQuote;
        if (!quote.contextProjectId ||
            voiceCatalogPreviewKey(quote.voiceCode, quote.speakingRate) !== pending.previewKey) {
          notify('Báo giá nghe thử không khớp giọng đã chọn.', true);
          return;
        }
        setConfirmation({
          eyebrow: 'XÁC NHẬN NGHE THỬ GIỌNG',
          title: `Tạo mẫu giọng ${voiceDisplayName(pending.voiceCode)}?`,
          description: `${quote.providerCode}/${quote.modelCode} sẽ đọc một câu mẫu tiếng Việt ngắn bằng tốc độ ${pending.speakingRate}×. Sau khi tải về, bạn có thể phát lại mẫu này trong modal mà không gọi AI thêm.`,
          note: `Đây là request AI có phí. Server sẽ giữ khoảng ${formatMoney(quote.estimatedCost, quote.currencyCode)} theo rate Active trước khi gọi OpenAI.`,
          confirmLabel: 'Tạo và nghe thử',
          onConfirm: () => {
            setBusy(true);
            setVoiceCatalogPreviewPendingKey(pending.previewKey);
            const operationRequestId = postToHost('voice-catalog.preview', {
              voiceCode: pending.voiceCode,
              speakingRate: pending.speakingRate,
              contextProjectId: quote.contextProjectId
            });
            pendingVoiceCatalogPreviewOperationRef.current.set(operationRequestId, pending);
          }
        });
        return;
      }

      if (message.type === 'generation.video.quote' && message.payload && message.requestId) {
        const sceneIds = pendingVideoVoiceQuoteRef.current.get(message.requestId);
        pendingVideoVoiceQuoteRef.current.delete(message.requestId);
        setBusy(false);
        if (!sceneIds) return;
        confirmGenerateVideos(sceneIds, message.payload as CanonicalVoiceQuote);
        return;
      }

      if (message.type === 'generation.content.repair.quote' && message.payload && message.requestId) {
        const failedProviderRequestId = pendingContentRepairQuoteRef.current.get(message.requestId);
        pendingContentRepairQuoteRef.current.delete(message.requestId);
        setBusy(false);
        if (!failedProviderRequestId) return;

        const quote = message.payload as ContentRepairQuote;
        if (quote.failedProviderRequestId.toLowerCase() !== failedProviderRequestId.toLowerCase()) {
          notify('Báo giá sửa nội dung không khớp request đã chọn.', true);
          return;
        }
        setConfirmation({
          eyebrow: 'XÁC NHẬN SỬA CONTENT PLAN',
          title: `Sửa ${quote.violations.length} trường bằng AI?`,
          description: `${quote.providerCode}/${quote.modelCode} sẽ sửa các trường bị rỗng, chưa đạt tiếng Việt hoặc chưa khớp nhịp lời. Cấu trúc, thứ tự cảnh, thời lượng và mapping tài sản/nhân vật phải được giữ nguyên.`,
          note: `Đây là một request OpenAI riêng có phí. Server sẽ giữ khoảng ${formatMoney(quote.estimatedCost, quote.currencyCode)} theo rate Active trước khi gọi provider. Mỗi failed plan chỉ có tối đa một lượt sửa.`,
          confirmLabel: 'Sửa các trường bằng AI',
          onConfirm: () => {
            setBusy(true);
            postToHost('generation.content.repair', { failedProviderRequestId });
          }
        });
        return;
      }

      if (message.type === 'generation.content.repaired') {
        setContentGenerationError(null);
        setContentLanguageFailure(null);
        return;
      }

      if (message.type === 'voice-catalog.previewed' && message.payload && message.requestId) {
        const pending = pendingVoiceCatalogPreviewOperationRef.current.get(message.requestId);
        pendingVoiceCatalogPreviewOperationRef.current.delete(message.requestId);
        setBusy(false);
        setVoiceCatalogPreviewPendingKey(null);
        if (!pending) return;
        const preview = message.payload as VoiceCatalogPreviewPlayback;
        if (voiceCatalogPreviewKey(preview.voiceCode, preview.speakingRate) !== pending.previewKey) {
          notify('Audio nghe thử không khớp giọng đã chọn.', true);
          return;
        }
        setVoiceCatalogPreviews((current) => ({ ...current, [pending.previewKey]: preview }));
        return;
      }

      if (message.type === 'operation.error') {
        if (message.requestId && message.requestId === logoutRequestRef.current) {
          logoutRequestRef.current = null;
          setLogoutBusy(false);
          setLicensePaymentError(message.error?.message ?? 'Chưa thể đăng xuất. Vui lòng thử lại.');
          return;
        }
        const operationErrorMessage = formatContentLanguageError(message.error);
        const recoverableContentFailure = parseContentLanguageFailure(message.error);
        if (recoverableContentFailure) {
          setContentGenerationError(operationErrorMessage);
          setContentLanguageFailure(recoverableContentFailure);
        }
        if (message.requestId) pendingFirstFrameQuoteRef.current.delete(message.requestId);
        if (message.requestId) pendingContentRepairQuoteRef.current.delete(message.requestId);
        if (message.requestId) pendingSpeechVerificationQuoteRef.current.delete(message.requestId);
        if (message.requestId) pendingVoicePreviewQuoteRef.current.delete(message.requestId);
        const failedVoiceCatalogPreview = message.requestId
          ? pendingVoiceCatalogPreviewQuoteRef.current.get(message.requestId) ??
            pendingVoiceCatalogPreviewOperationRef.current.get(message.requestId)
          : undefined;
        if (message.requestId) pendingVoiceCatalogPreviewQuoteRef.current.delete(message.requestId);
        if (message.requestId) pendingVoiceCatalogPreviewOperationRef.current.delete(message.requestId);
        if (failedVoiceCatalogPreview) setVoiceCatalogPreviewPendingKey(null);
        if (message.requestId) pendingVideoVoiceQuoteRef.current.delete(message.requestId);
        const failedFirstFrameSceneId = message.requestId
          ? pendingFirstFrameOperationRef.current.get(message.requestId)
          : undefined;
        if (failedFirstFrameSceneId) {
          pendingFirstFrameOperationRef.current.delete(message.requestId!);
          setFirstFrameOperation({
            sceneId: failedFirstFrameSceneId,
            status: 'failed',
            message: message.error?.message ?? 'Không thể tạo first-frame.'
          });
        }
        if (isSelectedProjectResponse(selectedProjectRequestRef.current, message.requestId)) {
          selectedProjectRequestRef.current = null;
        }
        const licenseRequestKind = message.requestId
          ? licenseRequestsRef.current.get(message.requestId)
          : undefined;
        if (licenseRequestKind) {
          if (message.requestId) licenseRequestsRef.current.delete(message.requestId);
          if (licenseRequestKind === 'status') licenseStatusInFlightRef.current = false;
          setLicensePaymentBusy(false);
          setLicensePaymentError(message.error?.message ?? 'Không thể hoàn tất thao tác thanh toán.');
          return;
        }
        const pendingSceneSave = pendingSceneSaveRef.current;
        if (pendingSceneSave && pendingSceneSave.requestId === message.requestId) {
          pendingSceneSaveRef.current = null;
          setSceneSaveState({
            sceneId: pendingSceneSave.sceneId,
            status: 'failed',
            message: message.error?.message ?? 'Không thể lưu cảnh. Nội dung bạn vừa sửa vẫn được giữ lại.'
          });
        }
        setBusy(false);
        setCharacterImageBusyId(null);
        setAssetConfirmBusyId(null);
        if (message.error?.code === 'scene_asset_confirmation_stale' || message.error?.code === 'project_asset_changed') {
          postToHost('dashboard.refresh');
        }
        if (message.error?.code === 'provider_temporarily_unavailable') {
          setServiceError({
            title: 'Máy chủ đang bảo trì',
            description: 'Hệ thống AI đang bảo trì hoặc tạm thời gián đoạn. Vui lòng thử lại sau.'
          });
          return;
        }
        if (message.error?.code === 'speech_verification_disabled' || message.error?.code === 'canonical_voice_disabled') {
          setConfirmation({
            eyebrow: 'SERVER CHƯA CHO PHÉP',
            title: message.error.code === 'speech_verification_disabled'
              ? 'ASR đang tắt trên máy chủ'
              : 'Canonical Voice đang tắt trên máy chủ',
            description: message.error.message,
            note: 'Người dùng có thể bật phần Desktop trong Cài đặt, nhưng cờ server, model và bảng giá phải do quản trị viên hệ thống chuẩn bị.',
            noteTone: 'info',
            confirmLabel: 'Mở Cài đặt',
            onConfirm: () => {
              setPage('settings');
              setBusy(true);
              postToHost('desktop.settings.get');
            }
          });
          return;
        }
        notify(operationErrorMessage, true);
        return;
      }

      if (message.type === 'operation.notice') {
        const completedFirstFrameSceneId = message.requestId
          ? pendingFirstFrameOperationRef.current.get(message.requestId)
          : undefined;
        if (completedFirstFrameSceneId) {
          pendingFirstFrameOperationRef.current.delete(message.requestId!);
          setFirstFrameOperation((current) => current?.sceneId === completedFirstFrameSceneId ? null : current);
        }
        const pendingSceneSave = pendingSceneSaveRef.current;
        if (pendingSceneSave && pendingSceneSave.requestId === message.requestId) {
          pendingSceneSaveRef.current = null;
          setSceneSaveState({ sceneId: pendingSceneSave.sceneId, status: 'succeeded' });
        }
        notify(String((message.payload as { message?: string })?.message ?? 'Đã cập nhật.'));
        return;
      }

      if (message.type === 'providers.settings' && message.payload) {
        setProviderSettings(message.payload as ProviderSettings);
        setBusy(false);
        return;
      }

      if ((message.type === 'desktop.settings' || message.type === 'desktop.settings.updated') && message.payload) {
        const nextSettings = message.payload as DesktopFeatureSettings;
        setDesktopSettings(nextSettings);
        setBusy(false);
        if (message.type === 'desktop.settings.updated') {
          notify(nextSettings.restartRequired
            ? 'Đã lưu cài đặt. Hãy đóng và mở lại VideoMaker để áp dụng.'
            : 'Cài đặt đồng bộ lời nói đã được cập nhật.');
        }
        return;
      }

      if (message.type === 'license.invalidated') {
        const payload = message.payload as LicenseInvalidatedMessage | undefined;
        const reason = payload?.message ?? 'License không còn hiệu lực.';
        setDashboard((current) => payload?.license
          ? { ...current, license: payload.license }
          : current.license
            ? { ...current, license: { ...current.license, leaseExpiresAtUtc: null, accessState: 'Unavailable', accessMessage: reason } }
            : current);
        setLicenseCheckout(null);
        setLicensePaymentError(null);
        setLicensePaymentBusy(false);
        licenseBootstrapRequestedRef.current = false;
        return;
      }

      if (message.type === 'update.available') {
        setUpdateNotice(message.payload as DesktopUpdateNotice);
        setUpdateProgress(null);
        setUpdateError(null);
        return;
      }

      if (message.type === 'update.none') {
        setUpdateNotice(null);
        return;
      }

      if (message.type === 'update.progress') {
        setUpdateProgress(message.payload as DesktopUpdateProgress);
        setUpdateError(null);
        return;
      }

      if (message.type === 'update.failed') {
        setUpdateProgress(null);
        setUpdateError(String((message.payload as { message?: string })?.message ?? 'Không thể áp dụng bản cập nhật.'));
        return;
      }

      if (message.type === 'media.tools.install.progress') {
        setMediaInstallProgress(message.payload as DesktopUpdateProgress);
        setBusy(true);
        return;
      }

      if (message.type === 'media.tools.install.available') {
        const release = message.payload as DesktopRelease;
        setBusy(false);
        setConfirmation({
          eyebrow: 'BỘ XỬ LÝ VIDEO',
          title: 'Cài lại FFmpeg và FFprobe?',
          description: `VideoMaker sẽ dùng package ${release.version} (build ${release.buildNumber}, ${formatUpdateSize(release.sizeBytes)}) từ máy chủ để sửa chữa trọn bộ công cụ media.`,
          note: 'Package sẽ được kiểm tra kích thước, SHA-256, manifest và license. Ứng dụng sẽ khởi động lại; thao tác không gọi provider AI và không phát sinh chi phí.',
          confirmLabel: 'Cài bộ xử lý video',
          onConfirm: () => {
            setBusy(true);
            setMediaInstallProgress({ stage: 'starting', percent: 0, message: 'Đang bắt đầu cài đặt...' });
            postToHost('media.tools.install');
          }
        });
        return;
      }

      if (message.type === 'media.tools.install.failed') {
        setMediaInstallProgress(null);
        setBusy(false);
        notify(String((message.payload as { message?: string })?.message ?? 'Không thể cài bộ xử lý video.'), true);
      }
    });

    if (isHosted) {
      postToHost('app.ready');
    } else {
      setBusy(false);
    }

    return unsubscribe;
  }, []);

  useEffect(() => {
    if (page === 'vietsub' && !dashboard.features.vietsubEnabled) {
      setPage('create');
    }
  }, [dashboard.features.vietsubEnabled, page]);

  useEffect(() => {
    try {
      window.localStorage.setItem(sidebarCollapsedStorageKey, String(sidebarCollapsed));
    } catch {
      // Sidebar preference is optional; continue normally when storage is unavailable.
    }
  }, [sidebarCollapsed]);

  const requestLicensePaymentStatus = () => {
    if (!licenseCheckout || licenseStatusInFlightRef.current) return;
    licenseStatusInFlightRef.current = true;
    postLicenseRequest('status', 'license.payment.status', { orderCode: licenseCheckout.orderCode });
  };

  useEffect(() => {
    if (!licenseCheckout || licensePaymentStatus?.isFulfilled || licensePaymentStatus?.isExpired) return;
    const initialTimer = window.setTimeout(requestLicensePaymentStatus, 1200);
    const pollTimer = window.setInterval(requestLicensePaymentStatus, 5000);
    return () => {
      window.clearTimeout(initialTimer);
      window.clearInterval(pollTimer);
    };
  }, [licenseCheckout?.orderCode, licensePaymentStatus?.isFulfilled, licensePaymentStatus?.isExpired]);

  const createLicensePayment = (licensePlanId: string) => {
    if (licensePaymentBusy) return;
    setLicensePaymentBusy(true);
    setLicensePaymentError(null);
    postLicenseRequest('create', 'license.payment.create', {
      licensePlanId,
      idempotencyKey: crypto.randomUUID()
    });
  };

  const retryLicenseBootstrap = () => {
    licenseBootstrapRequestedRef.current = false;
    setLicensePaymentError(null);
    requestLicenseBootstrap();
  };

  const resetExpiredLicensePayment = () => {
    licenseStatusInFlightRef.current = false;
    setLicenseCheckout(null);
    setLicensePaymentStatus(null);
    setLicensePaymentError(null);
  };

  const handleNavigation = (label: string, target?: Page) => {
    setSidebarOpen(false);
    if (target) {
      setPage(target);
      if (target === 'apiKeys') postToHost('providers.settings.get');
      if (target === 'settings') {
        setBusy(true);
        postToHost('desktop.settings.get');
      }
      return;
    }

    notify(`${label} đang được phát triển.`);
  };

  const navigateToSpeechSynchronizationSettings = () => {
    setPage('settings');
    setBusy(true);
    postToHost('desktop.settings.get');
  };

  const showSpeechSynchronizationDisabled = (requestedAction: string) => {
    setConfirmation({
      eyebrow: 'TÍNH NĂNG ĐANG TẮT',
      title: 'Bật đồng bộ lời nói trên máy này?',
      description: `${requestedAction} cần bật quy trình đồng bộ lời nói trong cài đặt Desktop.`,
      note: 'Công tắc Desktop chỉ mở giao diện và workflow trên máy này. ASR/TTS vẫn phải được quản trị viên cho phép trên server trước khi phát sinh chi phí.',
      noteTone: 'info',
      confirmLabel: 'Đi tới Cài đặt',
      onConfirm: navigateToSpeechSynchronizationSettings
    });
  };

  const selectProject = (projectId: string) => {
    setBusy(true);
    selectedProjectRequestRef.current = postToHost('project.select', { projectId });
  };

  const createProject = (payload: CreateProjectPayload) => {
    setBusy(true);
    postToHost('project.create', payload);
  };

  const requestShortVideo = (payload: CreateShortVideoPayload) => {
    if (busy || dashboard.generationRunning) return;
    const content = payload.content.trim();
    if (!content || content.length > 2000) {
      notify('Nội dung video phải có từ 1 đến 2.000 ký tự.', true);
      return;
    }
    if (!Number.isInteger(payload.durationSeconds) || payload.durationSeconds < 5 || payload.durationSeconds > 15) {
      notify('Thời lượng video phải nằm trong khoảng 5–15 giây.', true);
      return;
    }
    if (!dashboard.selectedOrganizationId) {
      notify('Hãy chọn tổ chức trước khi tạo video.', true);
      return;
    }
    if (!dashboard.mediaTools.ready) {
      notify(dashboard.mediaTools.message || 'FFmpeg và FFprobe chưa sẵn sàng.', true);
      return;
    }
    const status = dashboard.providerStatus;
    if (!status.videoReady) {
      notify(status.videoUnavailableMessage ?? 'Kling chưa sẵn sàng cho tổ chức hiện tại.', true);
      return;
    }
    if (status.videoProviderCode?.toLowerCase() !== 'kling') {
      notify('Màn hình này chỉ dùng Kling. Hãy chọn Kling làm video policy của tổ chức.', true);
      return;
    }

    const providerDurationSeconds = payload.durationSeconds;
    const estimatedCost = status.estimatedVideoCostPerSecond && status.estimatedVideoCostPerSecond > 0
      ? status.estimatedVideoCostPerSecond * providerDurationSeconds
      : null;
    const contentPreview = content.length > 360 ? `${content.slice(0, 359).trimEnd()}…` : content;
    setConfirmation({
      eyebrow: 'XÁC NHẬN TẠO VIDEO NGẮN',
      title: `Tạo một clip Kling ${payload.durationSeconds} giây?`,
      description: `${status.videoProviderName ?? 'Kling'} · ${status.videoModel ?? 'Model theo policy'} · ${status.videoResolution ?? '720p'} · ${payload.aspectRatio} · ${payload.audioEnabled ? 'Giữ Native Audio' : 'Video đầu ra không âm thanh'}\n\n${contentPreview}`,
      note: estimatedCost
        ? `Chi phí ước tính ${formatMoney(estimatedCost, status.currencyCode ?? 'USD')} cho ${providerDurationSeconds} giây provider.${payload.audioEnabled ? '' : ' Kling vẫn dùng variant Native Audio và tính phí như cũ; VideoMaker sẽ loại bỏ hoàn toàn audio khỏi file đầu ra.'} Server vẫn kiểm tra rate Active, budget và quyền. Luồng này không gọi OpenAI.`
        : `Server sẽ quote rate Active, giữ budget của tổ chức và kiểm tra quyền trước khi gọi Kling.${payload.audioEnabled ? '' : ' Kling vẫn dùng variant Native Audio và tính phí như cũ; VideoMaker sẽ loại bỏ hoàn toàn audio khỏi file đầu ra.'} Luồng này không gọi OpenAI.`,
      confirmLabel: `Tạo clip ${payload.durationSeconds} giây`,
      onConfirm: () => {
        setBusy(true);
        postToHost('short-video.generate', {
          content,
          aspectRatio: payload.aspectRatio,
          durationSeconds: payload.durationSeconds,
          audioEnabled: payload.audioEnabled
        });
      }
    });
  };

  const generateContent = () => {
    if (!dashboard.selectedProject || dashboard.generationRunning) return;
    setContentGenerationError(null);
    setContentLanguageFailure(null);
    setBusy(true);
    postToHost('generation.content');
  };

  const requestContentRepair = () => {
    const failure = contentLanguageFailure;
    if (!dashboard.selectedProject || dashboard.generationRunning || !failure?.canRepair || !failure.providerRequestId) {
      return;
    }

    setBusy(true);
    const requestId = postToHost('generation.content.repair.quote', {
      failedProviderRequestId: failure.providerRequestId
    });
    pendingContentRepairQuoteRef.current.set(requestId, failure.providerRequestId);
  };

  const renderFinalVideo = () => {
    const project = dashboard.selectedProject;
    if (!project || dashboard.generationRunning) return;
    const silentOutput = project.audioStrategy === 'SilentOutput';
    const canonicalOutput = project.speechProductionPolicy === 'CanonicalVoice';
    if (!dashboard.mediaTools.ready) {
      notify(dashboard.mediaTools.message || 'FFmpeg và FFprobe chưa sẵn sàng.', true);
      return;
    }
    if (project.totalScenes === 0 || project.approvedScenes === 0) {
      notify('Hãy tạo và duyệt ít nhất một cảnh trước khi dựng video.', true);
      return;
    }
    const omittedSceneCount = Math.max(0, project.totalScenes - project.approvedScenes);
    const omittedSceneNote = omittedSceneCount > 0
      ? ` ${omittedSceneCount} cảnh chưa duyệt sẽ không được đưa vào bản dựng này.`
      : '';
    setConfirmation({
      eyebrow: 'XÁC NHẬN DỰNG VIDEO CUỐI',
      title: project.preview?.url ? 'Dựng lại video hoàn chỉnh?' : 'Dựng video hoàn chỉnh?',
      description: silentOutput
        ? `${project.approvedScenes} clip đã duyệt sẽ được ghép đúng thứ tự. Video đầu ra sẽ không chứa audio stream.${omittedSceneNote}`
        : canonicalOutput
          ? `${project.approvedScenes} clip đã duyệt có Canonical Voice sẽ được ghép đúng thứ tự.${omittedSceneNote}`
          : `${project.approvedScenes} clip đã duyệt sẽ được ghép đúng thứ tự và giữ nguyên Native Audio của provider.${omittedSceneNote}`,
      note: silentOutput
        ? 'FFmpeg sẽ kiểm tra lại hình, thời lượng và xác nhận file đầu ra không có audio stream.'
        : 'FFmpeg sẽ kiểm tra lại hình, audio stream, mức âm lượng và thời lượng trước khi công nhận video đầu ra.',
      confirmLabel: 'Bắt đầu dựng video',
      onConfirm: () => {
        setBusy(true);
        postToHost('render.final');
      }
    });
  };

  const exportFinalVideo = () => {
    const project = dashboard.selectedProject;
    if (!project?.preview?.url) {
      notify('Hãy dựng xong video hoàn chỉnh trước khi xuất file MP4.', true);
      return;
    }
    if (busy || dashboard.generationRunning) return;
    setBusy(true);
    postToHost('final-video.export');
  };

  const confirmGenerateVideos = (sceneIds: string[], canonicalVoiceQuote?: CanonicalVoiceQuote | null) => {
    const currentDashboard = latestDashboardRef.current;
    const project = currentDashboard.selectedProject;
    if (!project || currentDashboard.generationRunning || sceneIds.length === 0) return;
    if (project.requiresVietnameseContentRegeneration) {
      notify('Dự án Video Dài này còn nội dung tiếng Anh. Hãy sinh lại nội dung tiếng Việt trước khi tạo clip.', true);
      return;
    }
    if (!currentDashboard.mediaTools.ready) {
      notify(currentDashboard.mediaTools.message || 'FFmpeg và FFprobe chưa sẵn sàng.', true);
      return;
    }
    const longFormProviderCode = project.videoProviderCode?.toLowerCase();
    if (longFormProviderCode === 'fal' && !['16:9', '9:16'].includes(project.project.aspectRatio)) {
      notify('Veo chỉ hỗ trợ dự án Video Dài tỷ lệ 16:9 hoặc 9:16. Hãy tạo dự án với tỷ lệ phù hợp.', true);
      return;
    }
    const selectedSceneIds = new Set(sceneIds);
    const enforceKlingLongFormSpeechPolicy = project.workflowStructureType === 'OpenAiStructuredPlan' &&
      ['kling', 'fal'].includes(longFormProviderCode ?? '');
    const selectedScenes = project.scenes.filter((scene) => selectedSceneIds.has(scene.sceneId));
    if (selectedScenes.length !== selectedSceneIds.size) {
      notify('Danh sách cảnh đã thay đổi. Hãy chọn lại cảnh cần tạo.', true);
      return;
    }
    const resumableScenes = selectedScenes.filter(sceneNeedsLocalCompletion);
    const newRequestScenes = selectedScenes.filter((scene) => !sceneNeedsLocalCompletion(scene));
    const canonicalVoicePreparationScenes = newRequestScenes.filter((scene) =>
      needsCanonicalVoicePreparation(scene, project.speechProductionPolicy));
    const videoRequestScenes = newRequestScenes.filter((scene) =>
      !needsCanonicalVoicePreparation(scene, project.speechProductionPolicy));
    if (longFormProviderCode === 'fal') {
      const scenesWithoutApprovedFirstFrame = newRequestScenes.filter((scene) =>
        !currentDashboard.sceneFirstFrames.some((frame) =>
          frame.sceneId === scene.sceneId && frame.status === 'Approved' && frame.isCurrent && Boolean(frame.previewUrl)));
      if (scenesWithoutApprovedFirstFrame.length > 0) {
        notify(
          `Cảnh ${scenesWithoutApprovedFirstFrame.map((scene) => scene.sequenceNumber).join(', ')} chưa có first-frame đúng tỷ lệ đã duyệt và còn tồn tại trên máy. Hãy tạo, xem và duyệt first-frame trước khi gửi sang Veo.`,
          true
        );
        return;
      }
    }
    const blockedAssetScenes = newRequestScenes.filter(
      (scene) => !areSceneAssetsReady(scene.sceneId, currentDashboard.assetLibrary ?? null)
    );
    if (blockedAssetScenes.length > 0) {
      const hasInvalidSelection = blockedAssetScenes.some((scene) =>
        currentDashboard.assetLibrary?.sceneAssignments.find((assignment) => assignment.sceneId === scene.sceneId)?.isValid === false);
      notify(
        hasInvalidSelection
          ? `Cảnh ${blockedAssetScenes.map((scene) => scene.sequenceNumber).join(', ')} có lựa chọn tài sản không hợp lệ. Hãy sửa trong Storyboard trước khi tạo clip.`
          : `Cảnh ${blockedAssetScenes.map((scene) => scene.sequenceNumber).join(', ')} đang dùng tài sản text chưa khóa. Hãy duyệt và khóa tài sản trước khi tạo clip.`,
        true
      );
      return;
    }
    const isDownloadOnly = resumableScenes.length === selectedScenes.length;
    const isVoicePreparationOnly = canonicalVoicePreparationScenes.length === selectedScenes.length;
    const isMixedOperation = [
      resumableScenes.length,
      canonicalVoicePreparationScenes.length,
      videoRequestScenes.length
    ].filter((count) => count > 0).length > 1;
    const totalSeconds = Math.ceil(selectedScenes.reduce((total, scene) => total + scene.durationMs, 0) / 1000);
    const newVideoRequestSeconds = Math.ceil(videoRequestScenes.reduce(
      (total, scene) => total + (scene.generationDurationMs ?? scene.durationMs),
      0) / 1000);
    const spokenSceneCount = selectedScenes.filter((scene) => scene.speechMode !== 'None').length;
    const spokenPreview = selectedScenes
      .map((scene) => {
        const durationSeconds = Math.ceil(scene.durationMs / 1000);
        const speech = scene.speechMode === 'None'
          ? 'Không có lời nói'
          : `${speechModeLabel(scene.speechMode, enforceKlingLongFormSpeechPolicy)}: “${scene.narration?.trim() || 'chưa có nội dung'}”`;
        const operation = sceneNeedsLocalCompletion(scene)
          ? 'Tải clip đã tạo'
          : needsCanonicalVoicePreparation(scene, project.speechProductionPolicy)
            ? 'Chuẩn bị WAV; chưa tạo video'
            : 'Tạo clip mới';
        return `${isMixedOperation ? `${operation} · ` : ''}Cảnh ${scene.sequenceNumber} (${durationSeconds}s) — ${speech}`;
      })
      .join('\n');
    const retryCount = videoRequestScenes.filter((scene) => scene.status === 'NativeAudioInvalid').length;
    const estimatedVideoCost = newVideoRequestSeconds > 0 && currentDashboard.providerStatus.estimatedVideoCostPerSecond
      ? currentDashboard.providerStatus.estimatedVideoCostPerSecond * newVideoRequestSeconds
      : null;
    const videoCostNote = videoRequestScenes.length === 0
      ? ''
      : estimatedVideoCost
        ? `Chi phí video ước tính ${formatMoney(estimatedVideoCost, currentDashboard.providerStatus.currencyCode ?? 'USD')} theo rate Active hiện tại; server sẽ quote và giữ budget chính xác trước outbound.`
        : 'Chi phí video được server quote theo rate Active và giữ trong budget tổ chức trước outbound.';
    const canonicalCostNote = canonicalVoicePreparationScenes.length > 0
      ? canonicalVoiceQuote
        ? `Canonical Voice: ${canonicalVoiceQuote.newVoiceCount} WAV mới, ${canonicalVoiceQuote.reusedVoiceCount} WAV dùng lại; chi phí TTS ước tính ${formatMoney(canonicalVoiceQuote.estimatedCost, canonicalVoiceQuote.currencyCode)}.`
        : 'Canonical Voice sẽ có request TTS riêng trước video.'
      : '';
    const costNote = [videoCostNote, canonicalCostNote].filter(Boolean).join(' ');
    const languageNote = ' Bạn phải nghe và duyệt từng clip trước khi dựng video cuối.';
    const retryNote = retryCount > 0
      ? ` ${retryCount} clip có Native Audio không đạt sẽ được tạo lại bằng prompt ưu tiên lời thoại và phát sinh chi phí provider mới.`
      : '';
    const providerLabel = currentDashboard.providerStatus.videoProviderName ?? currentDashboard.providerStatus.videoProviderCode ?? 'Provider do server chọn';
    const videoAudioLabel = project.speechProductionPolicy === 'CanonicalVoice'
      ? 'Video nền · Canonical WAV được ghép cục bộ'
      : 'Native Audio';

    if (isVoicePreparationOnly) {
      setConfirmation({
        eyebrow: 'XÁC NHẬN CHUẨN BỊ GIỌNG ĐỌC',
        title: `Chuẩn bị WAV cho ${selectedScenes.length} cảnh?`,
        description: `Thao tác này chỉ tạo hoặc tải lại Canonical WAV cho ${selectedScenes.length} cảnh. Video chưa được gửi sang ${providerLabel} ở bước này.\n\n${spokenPreview}`,
        note: `${canonicalCostNote} Sau khi WAV hoàn tất kiểm tra kỹ thuật, nút “Tạo video nền” sẽ xuất hiện. WAV hiện hành được dùng lại, không cần bước duyệt riêng.`,
        confirmLabel: `Tạo ${selectedScenes.length} bản đọc WAV`,
        onConfirm: () => {
          setBusy(true);
          postToHost('generation.video', { sceneIds });
        }
      });
      return;
    }

    if (isDownloadOnly) {
      setConfirmation({
        intent: 'download',
        noteTone: 'info',
        eyebrow: 'XÁC NHẬN TẢI CLIP',
        title: `Tải ${selectedScenes.length} clip đã tạo về máy?`,
        description: `Video đã hoàn thành trên server và đang chờ lưu về máy.\n${providerLabel} · ${currentDashboard.providerStatus.videoModel ?? 'Model theo policy'} · ${currentDashboard.providerStatus.videoResolution ?? '720p'} · ${videoAudioLabel}\nTổng thời lượng: khoảng ${totalSeconds} giây\n\n${spokenPreview}`,
        note: 'VideoMaker sẽ tiếp tục từ provider request hiện có, chỉ tải và kiểm tra clip bằng FFmpeg; không gửi yêu cầu tạo video mới và không phát sinh chi phí provider mới. Sau khi tải xong, bạn cần nghe và duyệt hình cùng Native Audio.',
        confirmLabel: `Tải ${selectedScenes.length} clip`,
        onConfirm: () => {
          setBusy(true);
          postToHost('generation.video', { sceneIds });
        }
      });
      return;
    }

    if (isMixedOperation) {
      const operationSummary = [
        resumableScenes.length > 0 ? `${resumableScenes.length} clip sẽ được tải về` : '',
        canonicalVoicePreparationScenes.length > 0
          ? `${canonicalVoicePreparationScenes.length} cảnh chỉ chuẩn bị WAV, chưa gửi video`
          : '',
        videoRequestScenes.length > 0 ? `${videoRequestScenes.length} clip sẽ được tạo mới` : ''
      ].filter(Boolean).join('; ');
      setConfirmation({
        eyebrow: 'XÁC NHẬN TIẾP TỤC QUY TRÌNH',
        title: `Tiếp tục xử lý ${selectedScenes.length} clip video?`,
        description: `${providerLabel} · ${currentDashboard.providerStatus.videoModel ?? 'Model theo policy'} · ${currentDashboard.providerStatus.videoResolution ?? '720p'}\n${operationSummary}.${videoRequestScenes.length > 0 ? ` Phần video mới dài khoảng ${newVideoRequestSeconds} giây.` : ''}\n\n${spokenPreview}`,
        note: `${resumableScenes.length > 0 ? `${resumableScenes.length} clip tải lại không phát sinh chi phí provider mới. ` : ''}${costNote} Cảnh chuẩn bị WAV phải hoàn tất kiểm tra kỹ thuật và nghe duyệt trước khi có thể tạo video.${languageNote}${retryNote}`,
        confirmLabel: `Tiếp tục ${selectedScenes.length} clip`,
        onConfirm: () => {
          setBusy(true);
          postToHost('generation.video', { sceneIds });
        }
      });
      return;
    }

    setConfirmation({
      eyebrow: 'XÁC NHẬN TẠO VIDEO',
      title: retryCount === selectedScenes.length
        ? `Tạo lại ${selectedScenes.length} clip với prompt ưu tiên lời thoại?`
        : project.speechProductionPolicy === 'CanonicalVoice'
          ? `Tạo video nền cho ${selectedScenes.length} cảnh?`
          : `Tạo ${selectedScenes.length} clip video?`,
      description: `${providerLabel} · ${currentDashboard.providerStatus.videoModel ?? 'Model theo policy'} · ${currentDashboard.providerStatus.videoResolution ?? '720p'} · ${videoAudioLabel}\nTổng thời lượng: khoảng ${totalSeconds} giây · ${spokenSceneCount}/${selectedScenes.length} cảnh có lời nói\n\n${spokenPreview}`,
      note: `${costNote}${languageNote}${retryNote}`,
      confirmLabel: retryCount === selectedScenes.length
        ? `Tạo lại ${selectedScenes.length} clip`
        : project.speechProductionPolicy === 'CanonicalVoice'
          ? `Tạo ${selectedScenes.length} video nền`
          : `Tạo ${selectedScenes.length} clip`,
      onConfirm: () => {
        setBusy(true);
        postToHost('generation.video', { sceneIds });
      }
    });
  };

  const generateVideos = (sceneIds: string[]) => {
    const project = dashboard.selectedProject;
    if (!project || dashboard.generationRunning || sceneIds.length === 0) return;
    const hasCanonicalVoiceToPrepare = project.scenes.some((scene) =>
      sceneIds.includes(scene.sceneId) &&
      needsCanonicalVoicePreparation(scene, project.speechProductionPolicy));
    if (!hasCanonicalVoiceToPrepare) {
      confirmGenerateVideos(sceneIds, null);
      return;
    }
    if (!dashboard.features.speechSynchronizationEnabled) {
      showSpeechSynchronizationDisabled('Tạo Canonical Voice và ghép lời vào video');
      return;
    }
    if (!dashboard.providerStatus.canonicalVoiceReady) {
      notify(
        dashboard.providerStatus.canonicalVoiceUnavailableMessage ??
          'Canonical Voice chưa đủ TTS, rate, budget hoặc feature flag trên server.',
        true
      );
      return;
    }
    setBusy(true);
    const requestId = postToHost('generation.video.quote', { sceneIds });
    pendingVideoVoiceQuoteRef.current.set(requestId, sceneIds);
  };

  const requestContentRegeneration = () => {
    const project = dashboard.selectedProject;
    if (!project || dashboard.generationRunning) return;
    const requiresVietnamese = project.requiresVietnameseContentRegeneration;
    setConfirmation({
      eyebrow: 'XÁC NHẬN SINH LẠI',
      title: requiresVietnamese ? 'Sinh lại nội dung tiếng Việt?' : 'Sinh lại content có nhân vật?',
      description: requiresVietnamese
        ? 'OpenAI sẽ tạo phiên bản kịch bản mới hoàn toàn bằng tiếng Việt, chia lại cảnh, lời nói, hồ sơ nhân vật và tài sản cho Kling.'
        : 'AI sẽ tạo một phiên bản kịch bản mới, chia lại các cảnh và bổ sung hồ sơ nhân vật để dùng xuyên suốt video.',
      note: 'Thao tác này có thể phát sinh chi phí OpenAI theo rate đang Active của tổ chức.',
      confirmLabel: requiresVietnamese ? 'Sinh lại bằng tiếng Việt' : 'Tiếp tục sinh lại',
      onConfirm: generateContent
    });
  };

  const updateScene = (payload: UpdateScenePayload) => {
    if (!dashboard.selectedProject || dashboard.generationRunning) return;
    const requestId = postToHost('scene.update', payload);
    pendingSceneSaveRef.current = { requestId, sceneId: payload.sceneId };
    setSceneSaveState({ sceneId: payload.sceneId, status: 'saving' });
    setBusy(true);
  };

  const clearSceneSaveFailure = (sceneId: string) => {
    setSceneSaveState((current) =>
      current?.sceneId === sceneId && current.status === 'failed' ? null : current);
  };

  const approveSceneNativeAudio = (sceneId: string, playbackConfirmed: boolean, speechReviewReason?: string) => {
    if (!dashboard.selectedProject || dashboard.generationRunning) return;
    const scene = dashboard.selectedProject.scenes.find((candidate) => candidate.sceneId === sceneId);
    const verification = scene?.speechVerification;
    const canonicalSpeech = dashboard.selectedProject.speechProductionPolicy === 'CanonicalVoice' &&
      scene?.speechMode !== 'None';
    const longFormProject = dashboard.selectedProject.workflowStructureType === 'OpenAiStructuredPlan';
    const needsReviewOverride = !longFormProject &&
      !canonicalSpeech &&
      verification?.status === 'NeedsReview' &&
      !verification.reviewApproved;
    if (needsReviewOverride && (!verification?.rowVersion || (speechReviewReason?.trim().length ?? 0) < 10)) {
      notify('Hãy nhập lý do chấp nhận transcript lệch, tối thiểu 10 ký tự.', true);
      return;
    }
    setBusy(true);
    postToHost('scene.native-audio.approve', {
      sceneId,
      playbackConfirmed,
      speechVerificationReportId: needsReviewOverride ? verification?.speechVerificationReportId : null,
      speechReviewReason: needsReviewOverride ? speechReviewReason?.trim() : null,
      speechVerificationRowVersion: needsReviewOverride ? verification?.rowVersion : null
    });
  };

  const unapproveSceneAudio = (sceneId: string) => {
    if (!dashboard.selectedProject || dashboard.generationRunning) return;
    setBusy(true);
    postToHost('scene.audio.unapprove', { sceneId });
  };

  const requestSceneSpeechVerification = (scene: SceneSummary) => {
    if (!dashboard.selectedProject || dashboard.generationRunning) return;
    if (!dashboard.features.speechSynchronizationEnabled) {
      showSpeechSynchronizationDisabled('Báo giá và kiểm tra transcript bằng ASR');
      return;
    }
    setBusy(true);
    const requestId = postToHost('scene.speech.verify.quote', { sceneId: scene.sceneId });
    pendingSpeechVerificationQuoteRef.current.set(requestId, scene);
  };

  const createVoiceProfileDraft = (
    scope: 'ProjectNarrator' | 'Character',
    characterId: string | null,
    voiceCode: string,
    speakingRate: number
  ) => {
    if (!dashboard.selectedProject || dashboard.generationRunning) return;
    setBusy(true);
    postToHost('voice-profile.draft', { scope, characterId, voiceCode, speakingRate });
  };

  const requestVoiceProfilePreview = (version: VoiceProfileSummary) => {
    if (!dashboard.selectedProject || dashboard.generationRunning || version.status !== 'Draft') return;
    setBusy(true);
    const requestId = postToHost('voice-profile.preview.quote', {
      voiceProfileVersionId: version.voiceProfileVersionId,
      expectedVoiceSnapshotHash: version.snapshotHash
    });
    pendingVoicePreviewQuoteRef.current.set(requestId, version);
  };

  const requestVoiceCatalogPreview = (voiceCode: string, speakingRate: number) => {
    if (dashboard.generationRunning || busy) return;
    const previewKey = voiceCatalogPreviewKey(voiceCode, speakingRate);
    if (voiceCatalogPreviews[previewKey]) return;
    const pending = { voiceCode, speakingRate, previewKey };
    setBusy(true);
    setVoiceCatalogPreviewPendingKey(previewKey);
    const requestId = postToHost('voice-catalog.preview.quote', { voiceCode, speakingRate });
    pendingVoiceCatalogPreviewQuoteRef.current.set(requestId, pending);
  };

  const approveVoiceProfile = (version: VoiceProfileSummary, playbackConfirmed: boolean) => {
    if (!dashboard.selectedProject || dashboard.generationRunning) return;
    if (!playbackConfirmed) {
      notify('Hãy phát và nghe audio preview trước khi duyệt giọng.', true);
      return;
    }
    setConfirmation({
      eyebrow: 'XÁC NHẬN KHÓA GIỌNG',
      title: `Duyệt voice version ${version.version}?`,
      description: `Các cảnh ${version.scope === 'ProjectNarrator' ? 'voice-over' : 'có nhân vật tương ứng'} sẽ dùng đúng snapshot ${version.snapshotHash.slice(0, 12)}.`,
      note: 'Nếu đang có version được duyệt, version cũ sẽ thành Superseded và các audio/render phụ thuộc sẽ mất hiệu lực.',
      confirmLabel: 'Duyệt và khóa giọng',
      onConfirm: () => {
        setBusy(true);
        postToHost('voice-profile.approve', {
          voiceProfileVersionId: version.voiceProfileVersionId,
          expectedVoiceSnapshotHash: version.snapshotHash,
          playbackConfirmed: true
        });
      }
    });
  };

  const supersedeVoiceProfile = (version: VoiceProfileSummary) => {
    if (!dashboard.selectedProject || dashboard.generationRunning) return;
    setConfirmation({
      eyebrow: 'XÁC NHẬN HỦY PHIÊN BẢN GIỌNG',
      title: version.status === 'Approved' ? 'Hủy duyệt giọng đang khóa?' : 'Bỏ bản nháp giọng?',
      description: version.status === 'Approved'
        ? 'Các audio và narrated video dùng voice version này sẽ không còn được phép render.'
        : 'Bản nháp sẽ chuyển sang Revoked và không thể preview hoặc duyệt tiếp.',
      note: 'File media cũ vẫn được giữ để truy vết; hệ thống chỉ bỏ trạng thái sẵn sàng.',
      confirmLabel: version.status === 'Approved' ? 'Hủy duyệt' : 'Bỏ bản nháp',
      onConfirm: () => {
        setBusy(true);
        postToHost('voice-profile.supersede', {
          voiceProfileVersionId: version.voiceProfileVersionId,
          expectedVoiceSnapshotHash: version.snapshotHash
        });
      }
    });
  };

  const updateCharacter = (payload: UpdateCharacterPayload) => {
    if (!dashboard.selectedProject || dashboard.generationRunning) return;
    setBusy(true);
    postToHost('character.update', payload);
  };

  const createProjectAsset = (payload: CreateProjectAssetPayload) => {
    if (!dashboard.selectedProject || dashboard.generationRunning) return;
    setBusy(true);
    postToHost('project-asset.create', payload);
  };

  const synchronizeProjectAssets = () => {
    if (!dashboard.selectedProject || dashboard.generationRunning) return;
    setBusy(true);
    postToHost('project-asset.materialize');
  };

  const approveAiProjectAssets = () => {
    const library = dashboard.assetLibrary;
    if (!dashboard.selectedProject || !library || dashboard.generationRunning) return;
    const assignedIds = new Set(library.sceneAssignments.flatMap((assignment) => assignment.projectAssetIds));
    const assets = library.assets.filter((asset) =>
      asset.sourceKind === 'AiGenerated' &&
      asset.status === 'Draft' &&
      assignedIds.has(asset.projectAssetId));
    if (assets.length === 0) {
      notify('Không còn tài sản AI đang dùng cần duyệt.', false);
      return;
    }
    setConfirmation({
      eyebrow: 'DUYỆT TÍNH NHẤT QUÁN',
      title: `Duyệt và khóa ${assets.length} tài sản AI?`,
      description: 'Hệ thống sẽ kiểm tra từng cảnh, giới hạn một bối cảnh và độ dài prompt Kling trước khi khóa đồng thời toàn bộ tài sản AI đang được dùng.',
      note: 'Thao tác này không gọi OpenAI hoặc Kling nên không phát sinh chi phí provider.',
      confirmLabel: 'Duyệt & khóa tài sản AI',
      onConfirm: () => {
        setBusy(true);
        postToHost('project-assets.approve-ai', {
          assets: assets.map((asset) => ({
            projectAssetId: asset.projectAssetId,
            concurrencyToken: asset.concurrencyToken
          }))
        });
      }
    });
  };

  const updateProjectAsset = (payload: UpdateProjectAssetPayload) => {
    if (!dashboard.selectedProject || dashboard.generationRunning) return;
    setBusy(true);
    postToHost('project-asset.update', payload);
  };

  const lockProjectAsset = (asset: ProjectTextAsset) => {
    if (!dashboard.selectedProject || dashboard.generationRunning) return;
    setBusy(true);
    postToHost('project-asset.lock', {
      projectAssetId: asset.projectAssetId,
      concurrencyToken: asset.concurrencyToken
    });
  };

  const unlockProjectAsset = (asset: ProjectTextAsset) => {
    if (!dashboard.selectedProject || dashboard.generationRunning) return;
    setConfirmation({
      eyebrow: 'MỞ KHÓA TÀI SẢN',
      title: `Mở khóa “${asset.name}”?`,
      description: asset.sceneIds.length > 0
        ? `Tài sản đang được gắn vào ${asset.sceneIds.length} cảnh. Các cảnh đó sẽ tạm thời không thể tạo clip mới cho đến khi text được khóa lại.`
        : 'Bạn có thể chỉnh sửa mô tả sau khi mở khóa.',
      note: 'Clip đã tạo trước đó không bị thay đổi. Lần tạo clip tiếp theo sẽ dùng phiên bản mới sau khi bạn khóa lại.',
      confirmLabel: 'Mở khóa để chỉnh sửa',
      onConfirm: () => {
        setBusy(true);
        postToHost('project-asset.unlock', {
          projectAssetId: asset.projectAssetId,
          concurrencyToken: asset.concurrencyToken
        });
      }
    });
  };

  const deleteProjectAsset = (asset: ProjectTextAsset) => {
    if (!dashboard.selectedProject || dashboard.generationRunning) return;
    setConfirmation({
      eyebrow: 'XÓA TÀI SẢN NHÁP',
      title: `Xóa “${asset.name}”?`,
      description: 'Chỉ tài sản nháp chưa từng khóa và chưa gắn vào cảnh mới có thể xóa.',
      note: 'Thao tác xóa không thể hoàn tác.',
      confirmLabel: 'Xóa tài sản',
      onConfirm: () => {
        setBusy(true);
        postToHost('project-asset.delete', {
          projectAssetId: asset.projectAssetId,
          concurrencyToken: asset.concurrencyToken
        });
      }
    });
  };

  const updateSceneAssets = (sceneId: string, projectAssetIds: string[]) => {
    if (!dashboard.selectedProject || dashboard.generationRunning) return;
    setBusy(true);
    postToHost('scene.assets.update', { sceneId, projectAssetIds });
  };

  const confirmSceneAssets = (sceneId: string) => {
    const library = dashboard.assetLibrary;
    if (!dashboard.selectedProject || !library || dashboard.generationRunning) return;
    const assignment = library.sceneAssignments.find((item) => item.sceneId === sceneId);
    const assignedIds = new Set(assignment?.projectAssetIds ?? []);
    const assets = library.assets.filter((asset) => assignedIds.has(asset.projectAssetId));
    if (assets.length === 0 || assets.every((asset) => asset.status === 'Locked')) return;
    setAssetConfirmBusyId(sceneId);
    setBusy(true);
    postToHost('scene.assets.confirm', {
      sceneId,
      assets: assets.map((asset) => ({
        projectAssetId: asset.projectAssetId,
        concurrencyToken: asset.concurrencyToken
      }))
    });
  };

  const selectCharacterReference = (characterId: string) => {
    if (!dashboard.selectedProject || dashboard.generationRunning) return;
    postToHost('character.reference.select', { characterId });
  };

  const generateCharacterReference = (character: CharacterSummary) => {
    const project = dashboard.selectedProject;
    if (!project || dashboard.generationRunning || characterImageBusyId) return;
    if (project.requiresVietnameseContentRegeneration) {
      notify('Hãy sinh lại nội dung tiếng Việt trước khi tạo ảnh nhân vật cho dự án Kling.', true);
      return;
    }
    const status = dashboard.providerStatus;
    if (!status.openAiImageReady) {
      notify(status.openAiImageUnavailableMessage ?? 'GPT-Image-2 chưa sẵn sàng cho tổ chức này.', true);
      return;
    }
    const estimatedCost = status.estimatedCharacterImageCost;
    setConfirmation({
      eyebrow: 'XÁC NHẬN TẠO ẢNH NHÂN VẬT',
      title: character.primaryReference ? `Sinh lại ảnh cho ${character.name}?` : `Tạo ảnh AI cho ${character.name}?`,
      description: 'GPT-Image-2 sẽ tạo một ảnh tham chiếu PNG 1024×1024, chất lượng medium từ hồ sơ nhân vật đã lưu.',
      note: estimatedCost && estimatedCost > 0
        ? `Server sẽ giữ khoảng ${formatMoney(estimatedCost, status.currencyCode ?? 'USD')} theo rate Active trước khi gọi OpenAI.`
        : 'Chi phí được server tính theo rate Active và giữ trong budget tổ chức trước khi gọi OpenAI.',
      confirmLabel: character.primaryReference ? 'Sinh lại ảnh' : 'Tạo ảnh bằng AI',
      onConfirm: () => {
        setCharacterImageBusyId(character.characterId);
        postToHost('character.reference.generate', { characterId: character.characterId });
      }
    });
  };

  const approveCharacter = (characterId: string) => {
    if (!dashboard.selectedProject || dashboard.generationRunning) return;
    setBusy(true);
    postToHost('character.approve', { characterId });
  };

  const requestSceneFirstFrame = (scene: SceneSummary, regenerate: boolean) => {
    const project = dashboard.selectedProject;
    if (!project || dashboard.generationRunning || busy) return;
    if (project.videoProviderCode?.toLowerCase() !== 'fal') {
      notify('First-frame riêng chỉ áp dụng cho dự án đang dùng Fal/Veo.', true);
      return;
    }
    if (!dashboard.providerStatus.openAiImageReady) {
      notify(dashboard.providerStatus.openAiImageUnavailableMessage ?? 'GPT-Image-2 chưa sẵn sàng.', true);
      return;
    }
    const assetBlocker = getSceneFirstFrameAssetBlocker(scene.sceneId, dashboard.assetLibrary ?? null);
    if (assetBlocker) {
      notify(`Cảnh ${scene.sequenceNumber} chưa thể tạo first-frame. ${assetBlocker}`, true);
      return;
    }
    const frames = dashboard.sceneFirstFrames.filter((frame) => frame.sceneId === scene.sceneId);
    const attempt = regenerate ? Math.max(0, ...frames.map((frame) => frame.version)) + 1 : 1;
    setFirstFrameOperation((current) => current?.sceneId === scene.sceneId ? null : current);
    setBusy(true);
    const requestId = postToHost('scene.first-frame.quote', { sceneId: scene.sceneId });
    pendingFirstFrameQuoteRef.current.set(requestId, { scene, attempt, regenerate });
  };

  const approveSceneFirstFrame = (frame: SceneFirstFrameSummary) => {
    if (dashboard.generationRunning || busy) return;
    setBusy(true);
    postToHost('scene.first-frame.approve', {
      sceneId: frame.sceneId,
      frameId: frame.sceneFirstFrameId,
      rowVersion: frame.rowVersion
    });
  };

  const rejectSceneFirstFrame = (frame: SceneFirstFrameSummary) => {
    if (dashboard.generationRunning || busy) return;
    setBusy(true);
    postToHost('scene.first-frame.reject', {
      sceneId: frame.sceneId,
      frameId: frame.sceneFirstFrameId,
      rowVersion: frame.rowVersion
    });
  };

  const retrySceneFirstFrameDownload = (frame: SceneFirstFrameSummary) => {
    if (dashboard.generationRunning || busy) return;
    setBusy(true);
    postToHost('scene.first-frame.download', {
      sceneId: frame.sceneId,
      frameId: frame.sceneFirstFrameId
    });
  };

  const generationBusy = busy || dashboard.generationRunning;
  const pageBusy = page === 'vietsub'
    ? vietsub.state.loading || vietsub.state.busy
    : generationBusy;
  const licenseLocked = isLicenseLocked(dashboard.license);
  const checkMediaTools = () => {
    if (generationBusy) return;
    setBusy(true);
    postToHost('media.tools.check');
  };

  const requestMediaToolInstall = () => {
    if (generationBusy) return;
    setBusy(true);
    postToHost('media.tools.install.prepare');
  };

  const updateSpeechSynchronizationSetting = (enabled: boolean) => {
    if (busy) return;
    setBusy(true);
    postToHost('desktop.settings.update', { speechSynchronizationEnabled: enabled });
  };

  return (
    <div className={`app-shell ${sidebarCollapsed ? 'sidebar-collapsed' : ''}`}>
      <Sidebar
        dashboard={dashboard}
        page={page}
        open={sidebarOpen}
        collapsed={sidebarCollapsed}
        onClose={() => setSidebarOpen(false)}
        onToggle={() => setSidebarCollapsed((current) => !current)}
        onNavigate={handleNavigation}
        onLogout={requestLogout}
        onUnavailable={notify}
      />

      <main className="app-main">
        <Header
          dashboard={dashboard}
          page={page}
          busy={pageBusy}
          onMenu={() => setSidebarOpen(true)}
          onCreate={() => setPage('longVideo')}
          onRefresh={() => {
            if (page === 'vietsub') {
              vietsub.refresh();
            } else {
              setBusy(true);
              postToHost('dashboard.refresh');
            }
          }}
          onSelectProject={selectProject}
          onSelectOrganization={async (organizationId) => {
            if (page === 'vietsub' && vietsub.state.selectedProject) {
              const flushed = await vietsub.prepareToLeaveEditor();
              if (!flushed) return;
              const closed = await vietsub.closeProject();
              if (!closed) return;
            }
            selectedProjectRequestRef.current = null;
            setBusy(true);
            postToHost('organization.select', { organizationId });
          }}
          onUnavailable={notify}
        />

        {page === 'vietsub' ? (
          <VietsubPage
            state={vietsub.state}
            onRefresh={vietsub.refresh}
            onCreateProject={vietsub.createProject}
            onOpenProject={vietsub.openProject}
            onRenameProject={vietsub.renameProject}
            onCloseProject={vietsub.closeProject}
            onImportMedia={vietsub.importMedia}
            onUpdateOcrSettings={vietsub.updateOcrSettings}
            onPreviewOcr={vietsub.previewOcr}
            onStartOcr={vietsub.startOcr}
            onStartTranslation={vietsub.startTranslation}
            onStartCloudTranslation={vietsub.startCloudTranslation}
            onRefreshCloudAvailability={vietsub.refreshCloudAvailability}
            onInstallTranslationRuntime={vietsub.installTranslationRuntime}
            onStartVoice={vietsub.startVoice}
            onInstallVoiceRuntime={vietsub.installVoiceRuntime}
            onDismissTranslationResourceAlert={vietsub.dismissTranslationResourceAlert}
            onContinueTranslationAfterResourceWarning={vietsub.continueTranslationAfterResourceWarning}
            onPauseJob={vietsub.pauseJob}
            onResumeJob={vietsub.resumeJob}
            onRetryJob={vietsub.retryJob}
            onCancelJob={vietsub.cancelJob}
            onActivateOcrTrack={vietsub.activateOcrTrack}
            onImportSrt={vietsub.importSrt}
            onActivateSubtitleTrack={vietsub.activateSubtitleTrack}
            onLoadSubtitlePage={vietsub.loadSubtitlePage}
            onLoadTimelineWindow={vietsub.loadTimelineWindow}
            onRequestTimelineThumbnails={vietsub.requestTimelineThumbnails}
            onRequestTimelineWaveform={vietsub.requestTimelineWaveform}
            onUpdateSubtitleCue={vietsub.updateSubtitleCue}
            onUpdateSubtitleStyle={vietsub.updateSubtitleStyle}
            onUpdateCueVoice={vietsub.updateCueVoice}
            onUpdateTimelineCue={vietsub.updateTimelineCue}
            onSplitSubtitleCue={vietsub.splitSubtitleCue}
            onAlignSubtitleCue={vietsub.alignSubtitleCue}
            onDuplicateSubtitleCue={vietsub.duplicateSubtitleCue}
            onDeleteSubtitleCue={vietsub.deleteSubtitleCue}
            onExportSrt={vietsub.exportSrt}
            onExportVideo={vietsub.exportVideo}
            onCancelOperation={vietsub.cancel}
            onRegisterBeforeLeave={vietsub.registerBeforeLeave}
          />
        ) : page === 'projects' ? (
          <ProjectsPage projects={dashboard.projects} onSelect={selectProject} onCreate={() => setPage('longVideo')} />
        ) : page === 'shortVideo' ? (
          <ShortVideoPage
            project={dashboard.selectedProject?.workflowStructureType === 'DirectShortVideo'
              ? dashboard.selectedProject
              : null}
            providerStatus={dashboard.providerStatus}
            mediaTools={dashboard.mediaTools}
            hasOrganization={Boolean(dashboard.selectedOrganizationId)}
            busy={generationBusy}
            onGenerate={requestShortVideo}
            onOpenSetup={() => setPage('apiKeys')}
            onCheckMediaTools={checkMediaTools}
          />
        ) : page === 'apiKeys' ? (
          <ApiKeysPage
            settings={providerSettings ?? {
              openAiConfigured: false,
              openAiModel: '',
              videoConfigured: false,
              videoProviderCode: null,
              videoModel: ''
            }}
            providerStatus={dashboard.providerStatus}
            organization={dashboard.organizations.find(
              (organization) => organization.organizationId === dashboard.selectedOrganizationId
            ) ?? null}
            license={dashboard.license ?? ({} as NonNullable<DashboardState['license']>)}
            busy={busy}
            onTest={(providerCode) => postToHost('providers.settings.test', { providerCode })}
          />
        ) : page === 'settings' ? (
          <DesktopSettingsPage
            settings={desktopSettings}
            busy={busy}
            onSpeechSynchronizationChange={updateSpeechSynchronizationSetting}
          />
        ) : page === 'longVideo' ? (
          <LongVideoPage
            project={dashboard.selectedProject ?? null}
            contentGenerationError={contentGenerationError}
            contentLanguageFailure={contentLanguageFailure}
            assetLibrary={dashboard.assetLibrary ?? null}
            sceneFirstFrames={dashboard.sceneFirstFrames}
            firstFrameOperation={firstFrameOperation}
            providerStatus={dashboard.providerStatus}
            mediaTools={dashboard.mediaTools}
            speechSynchronizationEnabled={dashboard.features.speechSynchronizationEnabled}
            busy={generationBusy}
            onCreate={createProject}
            onGenerateContent={generateContent}
            onRegenerateContent={requestContentRegeneration}
            onRepairContent={requestContentRepair}
            onGenerateVideo={generateVideos}
            onRequestSceneFirstFrame={requestSceneFirstFrame}
            onApproveSceneFirstFrame={approveSceneFirstFrame}
            onRejectSceneFirstFrame={rejectSceneFirstFrame}
            onRetrySceneFirstFrameDownload={retrySceneFirstFrameDownload}
            onPreviewSceneFirstFrame={setFirstFramePreview}
            onRenderFinalVideo={renderFinalVideo}
            onExportFinalVideo={exportFinalVideo}
            onApproveSceneNativeAudio={approveSceneNativeAudio}
            onUnapproveSceneAudio={unapproveSceneAudio}
            onVerifySceneSpeech={requestSceneSpeechVerification}
            onCreateVoiceProfile={createVoiceProfileDraft}
            onPreviewVoiceProfile={requestVoiceProfilePreview}
            voiceCatalogPreviews={voiceCatalogPreviews}
            voiceCatalogPreviewPendingKey={voiceCatalogPreviewPendingKey}
            onPreviewCatalogVoice={requestVoiceCatalogPreview}
            onApproveVoiceProfile={approveVoiceProfile}
            onSupersedeVoiceProfile={supersedeVoiceProfile}
            onInstallMediaTools={requestMediaToolInstall}
            onCheckMediaTools={checkMediaTools}
            onUpdateScene={updateScene}
            sceneSaveState={sceneSaveState}
            onClearSaveFailure={clearSceneSaveFailure}
            onUpdateCharacter={updateCharacter}
            onSelectCharacterReference={selectCharacterReference}
            onGenerateCharacterReference={generateCharacterReference}
            onApproveCharacter={approveCharacter}
            onCreateProjectAsset={createProjectAsset}
            onSynchronizeProjectAssets={synchronizeProjectAssets}
            onApproveAiProjectAssets={approveAiProjectAssets}
            onUpdateProjectAsset={updateProjectAsset}
            onLockProjectAsset={lockProjectAsset}
            onUnlockProjectAsset={unlockProjectAsset}
            onDeleteProjectAsset={deleteProjectAsset}
            onUpdateSceneAssets={updateSceneAssets}
            onConfirmSceneAssets={confirmSceneAssets}
            characterImageBusyId={characterImageBusyId}
            assetConfirmBusyId={assetConfirmBusyId}
            onOpenImageSetup={() => setPage('apiKeys')}
            onUnavailable={notify}
          />
        ) : (
          <DashboardPage
            project={dashboard.selectedProject ?? null}
            contentGenerationError={contentGenerationError}
            contentLanguageFailure={contentLanguageFailure}
            models={dashboard.models}
            providerStatus={dashboard.providerStatus}
            mediaTools={dashboard.mediaTools}
            speechSynchronizationEnabled={dashboard.features.speechSynchronizationEnabled}
            voiceCatalogPreviews={voiceCatalogPreviews}
            voiceCatalogPreviewPendingKey={voiceCatalogPreviewPendingKey}
            onPreviewCatalogVoice={requestVoiceCatalogPreview}
            busy={generationBusy}
            onCreate={createProject}
            onGenerateContent={generateContent}
            onRegenerateContent={requestContentRegeneration}
            onRepairContent={requestContentRepair}
            onGenerateVideo={generateVideos}
            onRenderFinalVideo={renderFinalVideo}
            onExportFinalVideo={exportFinalVideo}
            onApproveSceneNativeAudio={approveSceneNativeAudio}
            onUnapproveSceneAudio={unapproveSceneAudio}
            onVerifySceneSpeech={requestSceneSpeechVerification}
            onInstallMediaTools={requestMediaToolInstall}
            onCheckMediaTools={checkMediaTools}
            onUpdateScene={updateScene}
            sceneSaveState={sceneSaveState}
            onClearSaveFailure={clearSceneSaveFailure}
            onUpdateCharacter={updateCharacter}
            onSelectCharacterReference={selectCharacterReference}
            onGenerateCharacterReference={generateCharacterReference}
            onApproveCharacter={approveCharacter}
            characterImageBusyId={characterImageBusyId}
            onOpenImageSetup={() => setPage('apiKeys')}
            onUnavailable={notify}
          />
        )}
      </main>

      {confirmation && (
        <ConfirmationModal
          eyebrow={confirmation.eyebrow}
          title={confirmation.title}
          description={confirmation.description}
          note={confirmation.note}
          intent={confirmation.intent}
          noteTone={confirmation.noteTone}
          confirmLabel={confirmation.confirmLabel}
          onCancel={() => setConfirmation(null)}
          onConfirm={() => {
            const action = confirmation.onConfirm;
            setConfirmation(null);
            action();
          }}
        />
      )}

      {firstFramePreview?.previewUrl && (
        <div className="first-frame-lightbox" role="dialog" aria-modal="true" aria-label="Xem first-frame Veo" onClick={() => setFirstFramePreview(null)}>
          <div onClick={(event) => event.stopPropagation()}>
            <button type="button" aria-label="Đóng" onClick={() => setFirstFramePreview(null)}><X size={18} /></button>
            <img src={firstFramePreview.previewUrl} alt="First-frame Veo" />
            <span>{firstFramePreview.width}×{firstFramePreview.height} · {firstFramePreview.aspectRatio} · bản {firstFramePreview.version}</span>
          </div>
        </div>
      )}

      {serviceError && (
        <ServiceErrorModal
          title={serviceError.title}
          description={serviceError.description}
          onClose={() => setServiceError(null)}
        />
      )}

      {updateNotice?.release && (
        <UpdateModal
          notice={updateNotice}
          progress={updateProgress}
          error={updateError}
          hasRunningJob={Boolean(dashboard.selectedProject?.runningJobs)}
          onApply={() => {
            setUpdateError(null);
            setUpdateProgress({ stage: 'starting', percent: 0, message: 'Đang bắt đầu cập nhật...' });
            postToHost('update.apply');
          }}
          onDismiss={() => {
            postToHost('update.dismiss');
            setUpdateNotice(null);
          }}
          onExit={() => postToHost('update.exit')}
        />
      )}

      {mediaInstallProgress && (
        <MediaToolInstallModal progress={mediaInstallProgress} />
      )}

      {licenseLocked && dashboard.license && (
        <LicenseGateOverlay
          license={dashboard.license}
          offers={licenseOffers}
          checkout={licenseCheckout}
          paymentStatus={licensePaymentStatus}
          busy={licensePaymentBusy}
          logoutBusy={logoutBusy}
          error={licensePaymentError}
          onSelectPlan={createLicensePayment}
          onRefreshStatus={requestLicensePaymentStatus}
          onResetExpired={resetExpiredLicensePayment}
          onRetry={retryLicenseBootstrap}
          onCheckAgain={() => {
            if (licensePaymentBusy || logoutRequestRef.current) return;
            setLicensePaymentError(null);
            setLicensePaymentBusy(true);
            postLicenseRequest('refresh', 'license.refresh');
          }}
          onLogout={requestLogout}
        />
      )}

      <div className="toast-stack" aria-live="polite">
        {toasts.map((toast) => (
          <div className={`toast ${toast.error ? 'toast-error' : ''}`} key={toast.id}>
            {toast.error ? <TriangleAlert size={18} /> : <ShieldCheck size={18} />}
            <span>{toast.message}</span>
            <button onClick={() => setToasts((items) => items.filter((item) => item.id !== toast.id))}>
              <X size={15} />
            </button>
          </div>
        ))}
      </div>
    </div>
  );
}

function LicenseGateOverlay({
  license,
  offers,
  checkout: pendingCheckout,
  paymentStatus,
  busy,
  logoutBusy,
  error,
  onSelectPlan,
  onRefreshStatus,
  onResetExpired,
  onRetry,
  onCheckAgain,
  onLogout
}: {
  license: NonNullable<DashboardState['license']>;
  offers: LicenseOffer[];
  checkout: LicensePaymentCheckout | null;
  paymentStatus: LicensePaymentStatus | null;
  busy: boolean;
  logoutBusy: boolean;
  error: string | null;
  onSelectPlan: (licensePlanId: string) => void;
  onRefreshStatus: () => void;
  onResetExpired: () => void;
  onRetry: () => void;
  onCheckAgain: () => void;
  onLogout: () => void;
}) {
  const cardRef = useRef<HTMLElement>(null);
  const [nowMs, setNowMs] = useState(Date.now());
  const [serverClockOffsetMs, setServerClockOffsetMs] = useState(0);
  const [copiedField, setCopiedField] = useState<string | null>(null);
  const canPurchase = license.accessState === 'Missing' || license.accessState === 'Expired' || !license.accessState;
  const checkout = canPurchase ? pendingCheckout : null;
  const sessionLimit = license.accessState === 'SessionLimit' || license.accessReasonCode === 'concurrent_session_limit';

  useEffect(() => {
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';

    const focusInside = (preferLast = false) => {
      const card = cardRef.current;
      if (!card) return;
      const focusable = Array.from(card.querySelectorAll<HTMLElement>(
        'button:not(:disabled), [href], input:not(:disabled), [tabindex]:not([tabindex="-1"])'
      ));
      const target = preferLast ? focusable.at(-1) : focusable[0];
      if (target) target.focus();
      else card.focus();
    };

    focusInside();

    const trapFocus = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        event.stopPropagation();
        return;
      }
      if (event.key !== 'Tab' || !cardRef.current) return;
      const focusable = Array.from(cardRef.current.querySelectorAll<HTMLElement>(
        'button:not(:disabled), [href], input:not(:disabled), [tabindex]:not([tabindex="-1"])'
      ));
      if (focusable.length === 0) {
        event.preventDefault();
        return;
      }
      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (!cardRef.current.contains(document.activeElement)) {
        event.preventDefault();
        focusInside(event.shiftKey);
      } else if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    };
    const containFocus = (event: FocusEvent) => {
      const card = cardRef.current;
      if (card && event.target instanceof Node && !card.contains(event.target)) {
        event.stopPropagation();
        focusInside();
      }
    };
    window.addEventListener('keydown', trapFocus, true);
    window.addEventListener('focusin', containFocus, true);
    return () => {
      document.body.style.overflow = previousOverflow;
      window.removeEventListener('keydown', trapFocus, true);
      window.removeEventListener('focusin', containFocus, true);
    };
  }, []);

  useEffect(() => {
    const frame = window.requestAnimationFrame(() => {
      const card = cardRef.current;
      if (!card) return;
      card.scrollTo({ top: 0, behavior: 'auto' });
      if (card.contains(document.activeElement)) return;
      const target = card.querySelector<HTMLElement>(
        'button:not(:disabled), [href], input:not(:disabled), [tabindex]:not([tabindex="-1"])'
      );
      if (target) target.focus();
      else card.focus();
    });
    return () => window.cancelAnimationFrame(frame);
  }, [checkout?.orderCode]);

  useEffect(() => {
    if (!checkout) return;
    setServerClockOffsetMs(parseServerUtc(checkout.serverTimeUtc) - Date.now());
    setNowMs(Date.now());
    const timer = window.setInterval(() => setNowMs(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, [checkout?.orderCode]);

  const remainingSeconds = checkout
    ? Math.max(0, Math.floor((parseServerUtc(checkout.expiresAtUtc) - (nowMs + serverClockOffsetMs)) / 1000))
    : 0;
  const isExpired = Boolean(checkout &&
    !(checkout.isPaid || paymentStatus?.isPaid) &&
    (checkout.isExpired || paymentStatus?.isExpired || remainingSeconds <= 0));
  const isFulfilled = Boolean(checkout &&
    (checkout.isFulfilled || paymentStatus?.isFulfilled));
  const isPaid = Boolean(checkout &&
    (checkout.isPaid || paymentStatus?.isPaid));
  const assignedOrganizationName = paymentStatus?.assignedOrganizationName || checkout?.assignedOrganizationName;
  const provisioningStatus = paymentStatus?.provisioningStatus || checkout?.provisioningStatus;

  const copyValue = async (field: string, value: string) => {
    try {
      await navigator.clipboard.writeText(value);
    } catch {
      const textarea = document.createElement('textarea');
      textarea.value = value;
      textarea.style.position = 'fixed';
      textarea.style.opacity = '0';
      document.body.appendChild(textarea);
      textarea.select();
      document.execCommand('copy');
      textarea.remove();
    }
    setCopiedField(field);
    window.setTimeout(() => setCopiedField((current) => current === field ? null : current), 1600);
  };

  return (
    <div className="license-gate-overlay" role="presentation">
      <section
        ref={cardRef}
        className={`license-gate-card ${checkout ? 'has-checkout' : ''} ${!canPurchase ? 'has-access-error' : ''}`}
        role="dialog"
        tabIndex={-1}
        aria-modal="true"
        aria-labelledby="license-gate-title"
        aria-describedby="license-gate-description"
      >
        <header className="license-gate-header">
          <div className="license-gate-brand"><Crown size={21} /><span>VIDEOMAKER</span></div>
          {canPurchase && <button type="button" className="license-gate-logout" disabled={logoutBusy} onClick={onLogout}>
            {logoutBusy ? <LoaderCircle className="spin" size={16} /> : <LogOut size={16} />}{logoutBusy ? 'Đang đăng xuất…' : 'Đăng xuất'}
          </button>}
        </header>

        {!checkout ? (
          <div className="license-offer-view">
            <div className="license-gate-intro">
              <span className="license-gate-icon"><LockKeyhole size={26} /></span>
              <div>
                <span className="license-gate-eyebrow">QUYỀN SỬ DỤNG</span>
                <h1 id="license-gate-title">
                  {licenseAccessTitle(license)}
                </h1>
                <p id="license-gate-description">
                  {license.accessMessage || 'Bạn cần một gói đang hoạt động để sử dụng các tính năng VideoMaker.'}
                </p>
              </div>
            </div>

            {canPurchase ? (
              <>
                <div className="license-plan-grid">
                  {offers.map((offer, index) => {
                    const recommended = index === 1 || (offers.length === 1 && index === 0);
                    return (
                      <article className={`license-plan-option ${recommended ? 'recommended' : ''}`} key={offer.licensePlanId}>
                        <div className="license-plan-heading">
                          <span className="license-plan-code">Gói {offer.durationDays} ngày</span>
                          {recommended && <span className="license-plan-badge">Phổ biến</span>}
                        </div>
                        <h2>{offer.name}</h2>
                        <p>{offer.description || `Quyền sử dụng VideoMaker trong ${offer.durationDays} ngày.`}</p>
                        <div className="license-plan-price"><strong>{formatVnd(offer.priceVnd)}</strong><span>cho {offer.durationDays} ngày</span></div>
                        <div className={`license-seat-availability ${offer.organizationSeatAvailable ? 'available' : 'unavailable'}`}>
                          {offer.organizationSeatAvailable
                            ? `${offer.organizationPoolName || 'Cụm tổ chức'} · còn ${offer.availableOrganizationSeats ?? 0} chỗ`
                            : 'Tạm hết tổ chức sẵn sàng'}
                        </div>
                        <button type="button" disabled={busy || !offer.organizationSeatAvailable} onClick={() => onSelectPlan(offer.licensePlanId)}>
                          {busy ? <><LoaderCircle className="spin" size={17} />Đang tạo mã</> : <>Chọn gói này<ArrowRight size={17} /></>}
                        </button>
                        <ul>
                          {(offer.marketingFeatures.length ? offer.marketingFeatures : [
                            `${offer.maxActivatedDevices} thiết bị được kích hoạt`,
                            'Sử dụng đầy đủ quy trình tạo và dựng video',
                            'Tự động kích hoạt sau khi thanh toán'
                          ]).map((feature) => <li key={feature}><Check size={16} />{feature}</li>)}
                        </ul>
                      </article>
                    );
                  })}
                </div>
                {!offers.length && !error && <div className="license-loading"><LoaderCircle className="spin" size={22} />Đang tải các gói sử dụng...</div>}
              </>
            ) : (
              <>
                <div className="license-support-notice"><TriangleAlert size={20} /><span>
                  {sessionLimit
                    ? 'Tài khoản đang có phiên đăng nhập khác được ưu tiên sử dụng. Bạn có thể đăng xuất tại đây để quay về màn hình đăng nhập, hoặc đăng xuất phiên khác rồi kiểm tra lại.'
                    : license.accessState === 'DeviceLimit'
                      ? 'Hãy giải phóng thiết bị không còn sử dụng rồi kiểm tra lại, hoặc đăng xuất để đổi tài khoản.'
                      : 'Bạn có thể kiểm tra lại hoặc đăng xuất để đổi tài khoản. Nếu tình trạng tiếp diễn, hãy liên hệ quản trị viên.'}
                </span></div>
                <div className="license-access-actions">
                  <button type="button" className="license-gate-logout" disabled={busy || logoutBusy} onClick={onCheckAgain}>
                    <RefreshCw className={busy ? 'spin' : ''} size={17} />{busy ? 'Đang kiểm tra…' : 'Kiểm tra lại'}
                  </button>
                  <button type="button" className="license-gate-logout license-access-logout" disabled={logoutBusy} onClick={onLogout}>
                    {logoutBusy ? <LoaderCircle className="spin" size={17} /> : <LogOut size={17} />}{logoutBusy ? 'Đang đăng xuất…' : 'Đăng xuất'}
                  </button>
                </div>
              </>
            )}

            {error && <div className="license-payment-error" role="alert"><TriangleAlert size={18} /><span>{error}</span>{canPurchase && <button type="button" onClick={onRetry}>Thử lại</button>}</div>}
          </div>
        ) : (
          <div className="license-checkout-view">
            <div className="license-checkout-heading">
              <button type="button" className="license-back-button" disabled={busy && isFulfilled} onClick={onResetExpired}><ArrowLeft size={17} />Các gói</button>
              <div>
                <span className="license-gate-eyebrow">THANH TOÁN QUA SEPAY</span>
                <h1 id="license-gate-title">Thanh toán {checkout.planName}</h1>
                <p id="license-gate-description">Quét QR hoặc chuyển khoản đúng số tiền và nội dung bên dưới.</p>
              </div>
            </div>

            <div className="license-checkout-layout">
              <div className="license-qr-panel">
                <div className="license-qr-frame">
                  <img src={checkout.qrImageUrl} referrerPolicy="no-referrer" alt={`QR thanh toán ${checkout.planName}`} />
                </div>
                <span className={`license-payment-state ${isFulfilled ? 'success' : isExpired ? 'expired' : ''}`}>
                  {isFulfilled ? <><CircleCheck size={17} />Đã nhận thanh toán</> :
                    isExpired ? <><Clock3 size={17} />Mã đã hết hạn</> :
                      isPaid ? <><LoaderCircle className="spin" size={17} />Đang cấp tổ chức</> :
                        busy ? <><LoaderCircle className="spin" size={17} />Đang kiểm tra</> :
                        <><RefreshCw size={17} />Đang chờ thanh toán</>}
                </span>
                {!isPaid && !isExpired && !isFulfilled && <strong className="license-countdown">{formatCountdown(remainingSeconds)}</strong>}
              </div>

              <div className="license-transfer-panel">
                <div className="license-transfer-summary"><span>Tổng thanh toán</span><strong>{formatVnd(checkout.amountVnd)}</strong><small>{checkout.durationDays} ngày sử dụng</small></div>
                <TransferRow label="Ngân hàng" value={checkout.receiverBankCode} />
                <TransferRow label="Số tài khoản" value={checkout.receiverAccountNumber} action={
                  <button type="button" onClick={() => copyValue('account', checkout.receiverAccountNumber)}><Copy size={15} />{copiedField === 'account' ? 'Đã chép' : 'Sao chép'}</button>
                } />
                <TransferRow label="Chủ tài khoản" value={checkout.receiverAccountName} />
                <TransferRow label="Nội dung chuyển khoản" value={checkout.transferContent} emphasis action={
                  <button type="button" onClick={() => copyValue('content', checkout.transferContent)}><Copy size={15} />{copiedField === 'content' ? 'Đã chép' : 'Sao chép'}</button>
                } />
                {assignedOrganizationName && (
                  <TransferRow label="Tổ chức được cấp" value={assignedOrganizationName} />
                )}
                {provisioningStatus && (
                  <TransferRow label="Trạng thái phân bổ" value={provisioningStatus} />
                )}
                <div className="license-transfer-warning"><TriangleAlert size={17} /><span>Chuyển đúng số tiền và nội dung để hệ thống tự động nhận diện giao dịch.</span></div>
                {(paymentStatus?.message || error) && (
                  <div className={`license-status-message ${error ? 'error' : ''}`}>{error || paymentStatus?.message}</div>
                )}
                <div className="license-checkout-actions">
                  {isExpired ? (
                    <button type="button" className="license-primary-action" onClick={onResetExpired}>Tạo mã thanh toán mới</button>
                  ) : (
                    <button type="button" className="license-primary-action" disabled={busy || isFulfilled} onClick={onRefreshStatus}>
                      {busy || isFulfilled ? <><LoaderCircle className="spin" size={17} />Đang kích hoạt</> : <><RefreshCw size={17} />Tôi đã thanh toán</>}
                    </button>
                  )}
                </div>
              </div>
            </div>
          </div>
        )}
      </section>
    </div>
  );
}

function TransferRow({
  label,
  value,
  emphasis = false,
  action
}: {
  label: string;
  value: string;
  emphasis?: boolean;
  action?: ReactNode;
}) {
  return (
    <div className={`license-transfer-row ${emphasis ? 'emphasis' : ''}`}>
      <div><span>{label}</span><strong>{value}</strong></div>
      {action}
    </div>
  );
}

function formatVnd(value: number) {
  return `${new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 0 }).format(value)} đ`;
}

function parseServerUtc(value: string) {
  const timestamp = value.trim();
  const hasTimeZone = /(?:Z|[+-]\d{2}:\d{2})$/i.test(timestamp);
  return new Date(hasTimeZone ? timestamp : `${timestamp}Z`).getTime();
}

function formatCountdown(totalSeconds: number) {
  const minutes = Math.floor(totalSeconds / 60).toString().padStart(2, '0');
  const seconds = Math.max(0, totalSeconds % 60).toString().padStart(2, '0');
  return `${minutes}:${seconds}`;
}

function ConfirmationModal({
  eyebrow,
  title,
  description,
  note,
  intent = 'default',
  noteTone = 'warning',
  confirmLabel,
  onCancel,
  onConfirm
}: {
  eyebrow: string;
  title: string;
  description: string;
  note?: string;
  intent?: ConfirmationIntent;
  noteTone?: 'warning' | 'info';
  confirmLabel: string;
  onCancel: () => void;
  onConfirm: () => void;
}) {
  const confirmButtonRef = useRef<HTMLButtonElement>(null);
  const ConfirmationIcon = intent === 'download' ? Download : WandSparkles;
  const NoteIcon = noteTone === 'info' ? CircleCheck : TriangleAlert;

  useEffect(() => {
    const previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    confirmButtonRef.current?.focus();

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        onCancel();
      }
    };
    window.addEventListener('keydown', handleKeyDown);

    return () => {
      document.body.style.overflow = previousOverflow;
      window.removeEventListener('keydown', handleKeyDown);
      previousFocus?.focus();
    };
  }, [onCancel]);

  return (
    <div
      className="confirmation-overlay"
      role="presentation"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onCancel();
      }}
    >
      <section
        className={`confirmation-card confirmation-${intent}`}
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="confirmation-title"
        aria-describedby="confirmation-description"
      >
        <button className="confirmation-close" type="button" onClick={onCancel} aria-label="Đóng hộp thoại">
          <X size={18} />
        </button>
        <div className={`confirmation-icon confirmation-icon-${intent}`} aria-hidden="true">
          <ConfirmationIcon size={25} />
        </div>
        <span className="confirmation-eyebrow">{eyebrow}</span>
        <h2 id="confirmation-title">{title}</h2>
        <p id="confirmation-description">{description}</p>
        {note && (
          <div className={`confirmation-note confirmation-note-${noteTone}`}>
            <NoteIcon size={17} />
            <span>{note}</span>
          </div>
        )}
        <div className="confirmation-actions">
          <button className="confirmation-cancel" type="button" onClick={onCancel}>Hủy</button>
          <button ref={confirmButtonRef} className="confirmation-submit" type="button" onClick={onConfirm}>
            <ConfirmationIcon size={17} /> {confirmLabel}
          </button>
        </div>
      </section>
    </div>
  );
}

function ServiceErrorModal({
  title,
  description,
  onClose
}: {
  title: string;
  description: string;
  onClose: () => void;
}) {
  const closeButtonRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    const previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    closeButtonRef.current?.focus();

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        onClose();
      }
    };
    window.addEventListener('keydown', handleKeyDown);

    return () => {
      document.body.style.overflow = previousOverflow;
      window.removeEventListener('keydown', handleKeyDown);
      previousFocus?.focus();
    };
  }, [onClose]);

  return (
    <div
      className="confirmation-overlay"
      role="presentation"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onClose();
      }}
    >
      <section
        className="confirmation-card service-error-card"
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="service-error-title"
        aria-describedby="service-error-description"
      >
        <button className="confirmation-close" type="button" onClick={onClose} aria-label="Đóng thông báo">
          <X size={18} />
        </button>
        <div className="confirmation-icon service-error-icon" aria-hidden="true">
          <TriangleAlert size={25} />
        </div>
        <span className="confirmation-eyebrow service-error-eyebrow">Thông báo hệ thống</span>
        <h2 id="service-error-title">{title}</h2>
        <p id="service-error-description">{description}</p>
        <div className="confirmation-actions">
          <button ref={closeButtonRef} className="confirmation-submit service-error-submit" type="button" onClick={onClose}>
            Đã hiểu
          </button>
        </div>
      </section>
    </div>
  );
}

function UpdateModal({
  notice,
  progress,
  error,
  hasRunningJob,
  onApply,
  onDismiss,
  onExit
}: {
  notice: DesktopUpdateNotice;
  progress: DesktopUpdateProgress | null;
  error: string | null;
  hasRunningJob: boolean;
  onApply: () => void;
  onDismiss: () => void;
  onExit: () => void;
}) {
  const release = notice.release!;
  const busy = progress !== null;
  return (
    <div className="update-overlay" role="dialog" aria-modal="true" aria-labelledby="update-title">
      <section className="update-card">
        <div className="update-icon"><RefreshCw size={25} /></div>
        <div className="update-copy">
          <span className="update-eyebrow">{notice.isMandatory ? 'CẬP NHẬT BẮT BUỘC' : 'PHIÊN BẢN MỚI'}</span>
          <h2 id="update-title">VideoMaker {release.version}</h2>
          <p>Build {release.buildNumber} · {release.channel} · {formatUpdateSize(release.sizeBytes)}</p>
        </div>
        {release.releaseNotes && <div className="update-notes">{release.releaseNotes}</div>}
        {hasRunningJob && (
          <div className="update-warning"><TriangleAlert size={17} /><span>Một tác vụ đang chạy. Hãy chờ tác vụ hoàn tất trước khi cập nhật.</span></div>
        )}
        {progress && (
          <div className="update-progress">
            <div><span>{progress.message}</span><strong>{progress.percent}%</strong></div>
            <div className="update-progress-track"><i style={{ width: `${Math.max(0, Math.min(100, progress.percent))}%` }} /></div>
          </div>
        )}
        {error && <div className="update-error"><TriangleAlert size={17} />{error}</div>}
        <div className="update-actions">
          {!notice.isMandatory && <button className="update-secondary" disabled={busy} onClick={onDismiss}>Để sau</button>}
          {notice.isMandatory && <button className="update-secondary" disabled={busy} onClick={onExit}>Thoát</button>}
          <button className="update-primary" disabled={busy || hasRunningJob} onClick={onApply}>
            {busy ? <><LoaderCircle className="spin" size={17} />Đang cập nhật</> : <>Cập nhật ngay<ArrowRight size={17} /></>}
          </button>
        </div>
      </section>
    </div>
  );
}

function formatUpdateSize(bytes: number) {
  return bytes >= 1024 * 1024
    ? `${(bytes / 1024 / 1024).toFixed(1)} MB`
    : `${(bytes / 1024).toFixed(1)} KB`;
}

function MediaToolInstallModal({ progress }: { progress: DesktopUpdateProgress }) {
  return (
    <div className="update-overlay" role="dialog" aria-modal="true" aria-labelledby="media-install-title">
      <section className="update-card media-install-card">
        <div className="update-icon"><Download size={25} /></div>
        <div className="update-copy">
          <span className="update-eyebrow">CÀI BỘ XỬ LÝ VIDEO</span>
          <h2 id="media-install-title">Đang sửa chữa FFmpeg</h2>
          <p>Đừng đóng VideoMaker trong lúc package đang được tải và kiểm tra.</p>
        </div>
        <div className="update-progress">
          <div><span>{progress.message}</span><strong>{progress.percent}%</strong></div>
          <div className="update-progress-track"><i style={{ width: `${Math.max(0, Math.min(100, progress.percent))}%` }} /></div>
        </div>
        <div className="media-install-note">
          <ShieldCheck size={17} />
          <span>VideoMaker chỉ sử dụng package đã có manifest, license và SHA-256 hợp lệ.</span>
        </div>
      </section>
    </div>
  );
}

function Sidebar({
  dashboard,
  page,
  open,
  collapsed,
  onClose,
  onToggle,
  onNavigate,
  onLogout,
  onUnavailable
}: {
  dashboard: DashboardState;
  page: Page;
  open: boolean;
  collapsed: boolean;
  onClose: () => void;
  onToggle: () => void;
  onNavigate: (label: string, page?: Page) => void;
  onLogout: () => void;
  onUnavailable: (message: string) => void;
}) {
  const profile = dashboard.profile;
  const displayName = profile.displayName || profile.email || 'Tài khoản';
  const initials = displayName
    .split(/\s+/)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase())
    .join('');

  return (
    <>
      <button className={`sidebar-scrim ${open ? 'visible' : ''}`} onClick={onClose} aria-label="Đóng menu" />
      <aside className={`sidebar ${open ? 'open' : ''} ${collapsed ? 'collapsed' : ''}`}>
        <div className="brand">
          <div className="brand-mark"><Clapperboard size={25} /></div>
          <div className="brand-copy"><strong>VideoMaker</strong><span>Tự động tạo video</span></div>
          <button
            className="sidebar-toggle"
            type="button"
            onClick={onToggle}
            aria-expanded={!collapsed}
            aria-label={collapsed ? 'Mở rộng thanh menu' : 'Thu gọn thanh menu'}
            title={collapsed ? 'Mở rộng thanh menu' : 'Thu gọn thanh menu'}
          >
            {collapsed ? <PanelLeftOpen size={17} /> : <PanelLeftClose size={17} />}
          </button>
        </div>

        <button
          className="new-video-button"
          type="button"
          onClick={() => onNavigate('Tạo Video Dài', 'longVideo')}
          aria-label="Tạo video mới"
          title={collapsed ? 'Tạo video mới' : undefined}
        >
          <Plus size={18} /> <span className="new-video-label">Tạo video mới</span>
        </button>

        <nav className="sidebar-nav">
          {primaryMenu
            .filter(({ feature }) => !feature || dashboard.features[feature])
            .map(({ label, icon: Icon, page: target }) => (
              <button
                className={target === page ? 'active' : ''}
                key={label}
                onClick={() => onNavigate(label, target)}
                aria-label={label}
                title={collapsed ? label : undefined}
              >
                <Icon size={18} /><span>{label}</span>
              </button>
            ))}
          <div className="nav-divider" />
          {secondaryMenu.map(({ label, icon: Icon, page: target }) => (
            <button
              className={target === page ? 'active' : ''}
              key={label}
              onClick={() => onNavigate(label, target)}
              aria-label={label}
              title={collapsed ? label : undefined}
            >
              <Icon size={18} /><span>{label}</span>
            </button>
          ))}
        </nav>

        <div className="plan-card">
          <div><Crown size={17} /><strong>{dashboard.license?.planName || 'Chưa có gói'}</strong></div>
          <p>{dashboard.license?.hasActiveLicense
            ? `Hiệu lực đến ${formatDateOnly(dashboard.license.expiresAtUtc)} · ${dashboard.license.activeDeviceCount}/${dashboard.license.maxActivatedDevices} thiết bị`
            : 'Tài khoản chưa có license đang hoạt động.'}</p>
          <button onClick={() => onUnavailable('Vui lòng liên hệ quản trị viên để thay đổi gói.')}>Thông tin gói</button>
        </div>

        <div className="profile-card">
          <div className="avatar">{initials || <UserRound size={20} />}</div>
          <div className="profile-copy"><strong>{displayName}</strong><span>{profile.email}</span></div>
          <button className="profile-action" onClick={onLogout} title="Đăng xuất" aria-label="Đăng xuất"><LogOut size={17} /></button>
        </div>
      </aside>
    </>
  );
}

function Header({
  dashboard,
  page,
  busy,
  onMenu,
  onCreate,
  onRefresh,
  onSelectProject,
  onSelectOrganization,
  onUnavailable
}: {
  dashboard: DashboardState;
  page: Page;
  busy: boolean;
  onMenu: () => void;
  onCreate: () => void;
  onRefresh: () => void;
  onSelectProject: (id: string) => void;
  onSelectOrganization: (id: string) => void;
  onUnavailable: (message: string) => void;
}) {
  const pageHeader = pageHeaders[page];

  return (
    <header className="topbar">
      <button className="mobile-menu" onClick={onMenu} aria-label="Mở menu"><Menu size={21} /></button>
      <div className="topbar-heading">
        <h1>{pageHeader.title}</h1>
        <p>{pageHeader.subtitle}</p>
      </div>
      <div className="topbar-spacer" />
      {dashboard.organizations.length > 0 && (
        <label className="project-picker">
          <span>Tổ chức</span>
          <select
            value={dashboard.selectedOrganizationId}
            disabled={busy}
            onChange={(event) => onSelectOrganization(event.target.value)}
          >
            {dashboard.organizations.map((organization) => (
              <option key={organization.organizationId} value={organization.organizationId}>{organization.name}</option>
            ))}
          </select>
          <ChevronDown size={15} />
        </label>
      )}
      {page === 'projects' && (
        <button className="start-button topbar-create-button" onClick={onCreate}>
          <Plus size={17} /> <span>Tạo video mới</span>
        </button>
      )}
      {page !== 'apiKeys' && page !== 'settings' && page !== 'shortVideo' && page !== 'vietsub' && dashboard.projects.length > 0 && (
        <label className="project-picker">
          <span>Dự án</span>
          <select
            value={dashboard.selectedProject?.project.projectId ?? ''}
            onChange={(event) => onSelectProject(event.target.value)}
          >
            <option value="" disabled>Chọn dự án</option>
            {dashboard.projects.map((project) => <option key={project.projectId} value={project.projectId}>{project.name}</option>)}
          </select>
          <ChevronDown size={15} />
        </label>
      )}
      <button className="icon-button" onClick={onRefresh} disabled={busy} title="Làm mới dữ liệu">
        <RefreshCw size={19} className={busy ? 'spin' : ''} />
      </button>
      <button className="icon-button" onClick={() => onUnavailable('Thông báo đang được phát triển.')} title="Thông báo">
        <Bell size={20} />
      </button>
      <button className="upgrade-button" onClick={() => onUnavailable('Nâng cấp gói đang được phát triển.')}>
        <Sparkles size={17} /> Nâng cấp gói
      </button>
    </header>
  );
}

function ShortVideoPage({
  project,
  providerStatus,
  mediaTools,
  hasOrganization,
  busy,
  onGenerate,
  onOpenSetup,
  onCheckMediaTools
}: {
  project: ProjectDashboard | null;
  providerStatus: GenerationProviderStatus;
  mediaTools: MediaToolStatus;
  hasOrganization: boolean;
  busy: boolean;
  onGenerate: (payload: CreateShortVideoPayload) => void;
  onOpenSetup: () => void;
  onCheckMediaTools: () => void;
}) {
  const [content, setContent] = useState('');
  const [aspectRatio, setAspectRatio] = useState<CreateShortVideoPayload['aspectRatio']>('9:16');
  const [durationSeconds, setDurationSeconds] = useState(15);
  const [audioEnabled, setAudioEnabled] = useState(true);
  const scene = project?.scenes[0] ?? null;
  const preview = scene?.preview ?? project?.preview ?? null;
  const projectAspectRatio = project?.project.aspectRatio;
  const previewAspectRatio: CreateShortVideoPayload['aspectRatio'] =
    projectAspectRatio === '9:16' || projectAspectRatio === '16:9' || projectAspectRatio === '1:1'
      ? projectAspectRatio
      : aspectRatio;
  const klingSelected = providerStatus.videoProviderCode?.toLowerCase() === 'kling';
  const klingReady = providerStatus.videoReady && klingSelected;
  const canGenerate = Boolean(
    content.trim() &&
    content.length <= 2000 &&
    hasOrganization &&
    klingReady &&
    mediaTools.ready &&
    !busy
  );
  const providerDurationSeconds = Math.max(3, durationSeconds);
  const estimatedCost = providerStatus.estimatedVideoCostPerSecond && providerStatus.estimatedVideoCostPerSecond > 0
    ? providerStatus.estimatedVideoCostPerSecond * providerDurationSeconds
    : null;
  const progress = project
    ? Math.round(Math.max(project.overallProgressPercent, preview?.url ? 100 : 0))
    : 0;

  const submit = () => {
    if (!canGenerate) return;
    onGenerate({ content: content.trim(), aspectRatio, durationSeconds, audioEnabled });
  };

  return (
    <div className="page-shell short-video-page">
      <div className="short-video-layout">
        <section className="card short-video-form-card">
          <div className="short-video-section-heading">
            <span>1</span>
            <div><h3>Nhập nội dung cảnh</h3><p>Mô tả rõ chủ thể, bối cảnh, hành động, góc máy, ánh sáng và phong cách mong muốn.</p></div>
          </div>

          <label className="short-video-prompt-field">
            <span>Nội dung dùng để tạo video</span>
            <textarea
              autoFocus
              maxLength={2000}
              value={content}
              disabled={busy}
              onChange={(event) => setContent(event.target.value)}
              placeholder="Ví dụ: Một cô gái mặc áo dài xanh bước chậm giữa phố cổ Hội An lúc bình minh, máy quay dolly lùi mượt, đèn lồng lay nhẹ trong gió, phong cách điện ảnh chân thực..."
            />
            <small><span>Nội dung này được gửi thẳng vào prompt Kling, không qua OpenAI.</span><strong>{content.length}/2.000</strong></small>
          </label>

          <div className="short-video-ratio-block">
            <span>Tỷ lệ khung hình</span>
            <div>
              {(['9:16', '16:9', '1:1'] as const).map((ratio) => (
                <button
                  type="button"
                  className={aspectRatio === ratio ? 'selected' : ''}
                  disabled={busy}
                  key={ratio}
                  onClick={() => setAspectRatio(ratio)}
                >
                  <i className={`ratio-shape ratio-${ratio.replace(':', '-')}`} />
                  <strong>{ratio}</strong>
                  <small>{ratio === '9:16' ? 'Video dọc' : ratio === '16:9' ? 'Video ngang' : 'Video vuông'}</small>
                </button>
              ))}
            </div>
          </div>

          <div className="short-video-duration-block">
            <div className="short-video-duration-heading">
              <span>Thời lượng video</span>
              <output>{durationSeconds} giây</output>
            </div>
            <div className="short-video-duration-control">
              <button
                type="button"
                disabled={busy || durationSeconds <= 5}
                aria-label="Giảm một giây"
                onClick={() => setDurationSeconds((current) => Math.max(5, current - 1))}
              >−</button>
              <input
                type="range"
                min="5"
                max="15"
                step="1"
                value={durationSeconds}
                disabled={busy}
                aria-label="Thời lượng video từ 5 đến 15 giây"
                onChange={(event) => setDurationSeconds(Number(event.target.value))}
              />
              <button
                type="button"
                disabled={busy || durationSeconds >= 15}
                aria-label="Tăng một giây"
                onClick={() => setDurationSeconds((current) => Math.min(15, current + 1))}
              >+</button>
            </div>
            <div className="short-video-duration-scale"><span>5s</span><span>10s</span><span>15s</span></div>
          </div>

          <div className="short-video-specs">
            <div><Clock3 size={17} /><span><small>Thời lượng đầu ra</small><strong>{durationSeconds} giây</strong></span></div>
            <button
              type="button"
              className={`short-video-audio-option ${audioEnabled ? 'enabled' : 'muted'}`}
              role="switch"
              aria-checked={audioEnabled}
              disabled={busy}
              onClick={() => setAudioEnabled((current) => !current)}
            >
              {audioEnabled ? <Volume2 size={17} /> : <VolumeX size={17} />}
              <span>
                <small>Âm thanh</small>
                <strong>{audioEnabled ? 'Native Audio, không lời thoại' : 'Tắt âm thanh đầu ra'}</strong>
              </span>
              <i aria-hidden="true"><b /></i>
            </button>
            <div><ShieldCheck size={17} /><span><small>Kiểm soát chi phí</small><strong>Rate và budget tổ chức</strong></span></div>
          </div>
          {!audioEnabled && (
            <p className="short-video-audio-note">
              <VolumeX size={14} /> File kết quả sẽ không có audio stream. Chi phí Kling không giảm vì provider vẫn dùng variant Native Audio.
            </p>
          )}

          {!hasOrganization && (
            <div className="short-video-readiness warning"><TriangleAlert size={17} /><span>Hãy chọn tổ chức trước khi tạo video.</span></div>
          )}
          {hasOrganization && !klingReady && (
            <div className="short-video-readiness warning">
              <TriangleAlert size={17} />
              <span>{providerStatus.videoReady && !klingSelected
                ? 'Video policy hiện tại không phải Kling. Hãy chọn Kling cho tổ chức.'
                : providerStatus.videoUnavailableMessage ?? 'Kling chưa sẵn sàng cho tổ chức hiện tại.'}</span>
              <button type="button" onClick={onOpenSetup}>Kiểm tra AI</button>
            </div>
          )}
          {!mediaTools.ready && (
            <div className="short-video-readiness warning">
              <TriangleAlert size={17} /><span>{mediaTools.message}</span>
              <button type="button" disabled={busy} onClick={onCheckMediaTools}>Kiểm tra lại</button>
            </div>
          )}

          <div className="short-video-submit-row">
            <div>
              <span>Chi phí Kling ước tính</span>
              <strong>{estimatedCost
                ? formatMoney(estimatedCost, providerStatus.currencyCode ?? 'USD')
                : 'Server sẽ báo giá theo rate Active'}</strong>
            </div>
            <button className="start-button short-video-submit" disabled={!canGenerate} onClick={submit}>
              {busy ? <LoaderCircle className="spin" size={19} /> : <Play size={18} fill="currentColor" />}
              {busy ? 'Đang tạo video...' : `Tạo video ${durationSeconds} giây`}
            </button>
          </div>
        </section>

        <aside className="card short-video-result-card">
          <div className="short-video-section-heading compact">
            <span>2</span>
            <div><h3>Kết quả</h3><p>Clip được tải qua proxy server và lưu vào workspace.</p></div>
          </div>

          <div
            className={`short-video-preview ratio-preview-${previewAspectRatio.replace(':', '-')}`}
            data-aspect-ratio={previewAspectRatio}
          >
            {preview?.url ? (
              <video controls preload="metadata" src={preview.url} />
            ) : (
              <div className="short-video-preview-empty">
                {busy ? <LoaderCircle className="spin" size={32} /> : <Film size={34} />}
                <strong>{busy ? 'Kling đang xử lý clip...' : 'Video sẽ xuất hiện tại đây'}</strong>
                <span>{busy ? 'Bạn có thể theo dõi tiến trình mà không cần rời màn hình.' : `Nhập nội dung, chọn thời lượng và bấm Tạo video ${durationSeconds} giây.`}</span>
              </div>
            )}
          </div>

          {project ? (
            <div className="short-video-result-meta">
              <div><span>Trạng thái</span><strong>{translateProjectStatus(project.project.status)}</strong></div>
              <div><span>Cảnh</span><strong>1 cảnh · {project.project.targetDurationSeconds} giây</strong></div>
              <div><span>Model</span><strong>{providerStatus.videoModel ?? 'Kling theo policy'}</strong></div>
              <div><span>Âm thanh</span><strong>{project.audioStrategy === 'SilentOutput' ? 'Đã tắt' : 'Native Audio'}</strong></div>
              <ProgressBar value={progress} />
              {scene?.lastErrorMessage && (
                <p className="short-video-result-error"><TriangleAlert size={15} /> {scene.lastErrorMessage}</p>
              )}
              {preview?.url && (
                <p className="short-video-result-success"><CircleCheck size={15} /> {project.audioStrategy === 'SilentOutput'
                  ? 'Clip không âm thanh đã được lưu vào workspace.'
                  : 'Clip đã tải về workspace. Hãy phát để kiểm tra hình và Native Audio.'}</p>
              )}
            </div>
          ) : (
            <div className="short-video-empty-notes">
              <p><Check size={14} /> Không sinh content bằng OpenAI.</p>
              <p><Check size={14} /> Không tạo nhân vật hay lời thoại tự động.</p>
              <p><Check size={14} /> Gateway vẫn kiểm tra quyền, rate và ngân sách.</p>
            </div>
          )}
        </aside>
      </div>
    </div>
  );
}

function DashboardPage({
  project,
  contentGenerationError,
  contentLanguageFailure,
  models,
  providerStatus,
  mediaTools,
  speechSynchronizationEnabled,
  voiceCatalogPreviews,
  voiceCatalogPreviewPendingKey,
  onPreviewCatalogVoice,
  busy,
  onCreate,
  onGenerateContent,
  onRegenerateContent,
  onRepairContent,
  onGenerateVideo,
  onRenderFinalVideo,
  onExportFinalVideo,
  onApproveSceneNativeAudio,
  onUnapproveSceneAudio,
  onVerifySceneSpeech,
  onInstallMediaTools,
  onCheckMediaTools,
  onUpdateScene,
  sceneSaveState,
  onClearSaveFailure,
  onUpdateCharacter,
  onSelectCharacterReference,
  onGenerateCharacterReference,
  onApproveCharacter,
  characterImageBusyId,
  onOpenImageSetup,
  onUnavailable
}: {
  project: ProjectDashboard | null;
  contentGenerationError: string | null;
  contentLanguageFailure: ContentLanguageFailureView | null;
  models: AiModel[];
  providerStatus: GenerationProviderStatus;
  mediaTools: MediaToolStatus;
  speechSynchronizationEnabled: boolean;
  voiceCatalogPreviews: Record<string, VoiceCatalogPreviewPlayback>;
  voiceCatalogPreviewPendingKey: string | null;
  onPreviewCatalogVoice: (voiceCode: string, speakingRate: number) => void;
  busy: boolean;
  onCreate: (payload: CreateProjectPayload) => void;
  onGenerateContent: () => void;
  onRegenerateContent: () => void;
  onRepairContent: () => void;
  onGenerateVideo: (sceneIds: string[]) => void;
  onRenderFinalVideo: () => void;
  onExportFinalVideo: () => void;
  onApproveSceneNativeAudio: (sceneId: string, playbackConfirmed: boolean, speechReviewReason?: string) => void;
  onUnapproveSceneAudio: (sceneId: string) => void;
  onVerifySceneSpeech: (scene: SceneSummary) => void;
  onInstallMediaTools: () => void;
  onCheckMediaTools: () => void;
  onUpdateScene: (payload: UpdateScenePayload) => void;
  sceneSaveState: SceneSaveState | null;
  onClearSaveFailure: (sceneId: string) => void;
  onUpdateCharacter: (payload: UpdateCharacterPayload) => void;
  onSelectCharacterReference: (characterId: string) => void;
  onGenerateCharacterReference: (character: CharacterSummary) => void;
  onApproveCharacter: (characterId: string) => void;
  characterImageBusyId: string | null;
  onOpenImageSetup: () => void;
  onUnavailable: (message: string) => void;
}) {
  const ignoreSceneAssetUpdate = (_sceneId: string, _projectAssetIds: string[]) => undefined;

  return (
    <div className="page-shell">
      <div className="workspace-grid">
        <section className="workspace-main">
          <CreateVideoCard
            busy={busy}
            speechSynchronizationEnabled={speechSynchronizationEnabled}
            voiceOptions={getAvailableVoiceOptions(providerStatus.openAiVoiceOptions)}
            voiceCatalogPreviews={voiceCatalogPreviews}
            voiceCatalogPreviewPendingKey={voiceCatalogPreviewPendingKey}
            onPreviewCatalogVoice={onPreviewCatalogVoice}
            voicePreviewProjectName={project?.project.name}
            onCreate={onCreate}
          />
          <GenerationActions
            project={project}
            errorMessage={contentGenerationError}
            languageFailure={contentLanguageFailure}
            providerStatus={providerStatus}
            busy={busy}
            onGenerateContent={onGenerateContent}
            onRegenerateContent={onRegenerateContent}
            onRepairContent={onRepairContent}
          />
          <CharacterSection
            project={project}
            providerStatus={providerStatus}
            busy={busy}
            imageBusyId={characterImageBusyId}
            onRegenerateContent={onRegenerateContent}
            onUpdate={onUpdateCharacter}
            onSelectReference={onSelectCharacterReference}
            onGenerateReference={onGenerateCharacterReference}
            onApprove={onApproveCharacter}
            onOpenImageSetup={onOpenImageSetup}
          />
          <StoryboardSection
            project={project}
            assetLibrary={null}
            providerStatus={providerStatus}
            mediaTools={mediaTools}
            busy={busy}
            onGenerateVideo={onGenerateVideo}
            onApproveNativeAudio={onApproveSceneNativeAudio}
            onUnapproveAudio={onUnapproveSceneAudio}
            onVerifySceneSpeech={onVerifySceneSpeech}
            onInstallMediaTools={onInstallMediaTools}
            onCheckMediaTools={onCheckMediaTools}
            onUpdateScene={onUpdateScene}
            sceneSaveState={sceneSaveState}
            onClearSaveFailure={onClearSaveFailure}
            onUpdateSceneAssets={ignoreSceneAssetUpdate}
            onConfirmSceneAssets={() => undefined}
            assetConfirmBusyId={null}
          />
          <WorkflowCard project={project} />
          <PipelineDetails project={project} onUnavailable={onUnavailable} />
          <ModelsSection models={models} />
        </section>
        <aside className="workspace-side">
          <PreviewCard project={project} />
          <ProjectInfoCard project={project} />
          <RenderProgressCard
            project={project}
            busy={busy}
            mediaToolsReady={mediaTools.ready}
            onRender={onRenderFinalVideo}
            onExport={onExportFinalVideo}
            onUnavailable={onUnavailable}
          />
        </aside>
      </div>
    </div>
  );
}

function LongVideoPage({
  project,
  contentGenerationError,
  contentLanguageFailure,
  assetLibrary,
  sceneFirstFrames,
  firstFrameOperation,
  providerStatus,
  mediaTools,
  speechSynchronizationEnabled,
  busy,
  onCreate,
  onGenerateContent,
  onRegenerateContent,
  onRepairContent,
  onGenerateVideo,
  onRequestSceneFirstFrame,
  onApproveSceneFirstFrame,
  onRejectSceneFirstFrame,
  onRetrySceneFirstFrameDownload,
  onPreviewSceneFirstFrame,
  onRenderFinalVideo,
  onExportFinalVideo,
  onApproveSceneNativeAudio,
  onUnapproveSceneAudio,
  onVerifySceneSpeech,
  onCreateVoiceProfile,
  onPreviewVoiceProfile,
  voiceCatalogPreviews,
  voiceCatalogPreviewPendingKey,
  onPreviewCatalogVoice,
  onApproveVoiceProfile,
  onSupersedeVoiceProfile,
  onInstallMediaTools,
  onCheckMediaTools,
  onUpdateScene,
  sceneSaveState,
  onClearSaveFailure,
  onUpdateCharacter,
  onSelectCharacterReference,
  onGenerateCharacterReference,
  onApproveCharacter,
  onCreateProjectAsset,
  onSynchronizeProjectAssets,
  onApproveAiProjectAssets,
  onUpdateProjectAsset,
  onLockProjectAsset,
  onUnlockProjectAsset,
  onDeleteProjectAsset,
  onUpdateSceneAssets,
  onConfirmSceneAssets,
  characterImageBusyId,
  assetConfirmBusyId,
  onOpenImageSetup,
  onUnavailable
}: {
  project: ProjectDashboard | null;
  contentGenerationError: string | null;
  contentLanguageFailure: ContentLanguageFailureView | null;
  assetLibrary: ProjectAssetLibrary | null;
  sceneFirstFrames: SceneFirstFrameSummary[];
  firstFrameOperation: SceneFirstFrameOperation | null;
  providerStatus: GenerationProviderStatus;
  mediaTools: MediaToolStatus;
  speechSynchronizationEnabled: boolean;
  busy: boolean;
  onCreate: (payload: CreateProjectPayload) => void;
  onGenerateContent: () => void;
  onRegenerateContent: () => void;
  onRepairContent: () => void;
  onGenerateVideo: (sceneIds: string[]) => void;
  onRequestSceneFirstFrame: (scene: SceneSummary, regenerate: boolean) => void;
  onApproveSceneFirstFrame: (frame: SceneFirstFrameSummary) => void;
  onRejectSceneFirstFrame: (frame: SceneFirstFrameSummary) => void;
  onRetrySceneFirstFrameDownload: (frame: SceneFirstFrameSummary) => void;
  onPreviewSceneFirstFrame: (frame: SceneFirstFrameSummary) => void;
  onRenderFinalVideo: () => void;
  onExportFinalVideo: () => void;
  onApproveSceneNativeAudio: (sceneId: string, playbackConfirmed: boolean, speechReviewReason?: string) => void;
  onUnapproveSceneAudio: (sceneId: string) => void;
  onVerifySceneSpeech: (scene: SceneSummary) => void;
  onCreateVoiceProfile: (scope: 'ProjectNarrator' | 'Character', characterId: string | null, voiceCode: string, speakingRate: number) => void;
  onPreviewVoiceProfile: (version: VoiceProfileSummary) => void;
  voiceCatalogPreviews: Record<string, VoiceCatalogPreviewPlayback>;
  voiceCatalogPreviewPendingKey: string | null;
  onPreviewCatalogVoice: (voiceCode: string, speakingRate: number) => void;
  onApproveVoiceProfile: (version: VoiceProfileSummary, playbackConfirmed: boolean) => void;
  onSupersedeVoiceProfile: (version: VoiceProfileSummary) => void;
  onInstallMediaTools: () => void;
  onCheckMediaTools: () => void;
  onUpdateScene: (payload: UpdateScenePayload) => void;
  sceneSaveState: SceneSaveState | null;
  onClearSaveFailure: (sceneId: string) => void;
  onUpdateCharacter: (payload: UpdateCharacterPayload) => void;
  onSelectCharacterReference: (characterId: string) => void;
  onGenerateCharacterReference: (character: CharacterSummary) => void;
  onApproveCharacter: (characterId: string) => void;
  onCreateProjectAsset: (payload: CreateProjectAssetPayload) => void;
  onSynchronizeProjectAssets: () => void;
  onApproveAiProjectAssets: () => void;
  onUpdateProjectAsset: (payload: UpdateProjectAssetPayload) => void;
  onLockProjectAsset: (asset: ProjectTextAsset) => void;
  onUnlockProjectAsset: (asset: ProjectTextAsset) => void;
  onDeleteProjectAsset: (asset: ProjectTextAsset) => void;
  onUpdateSceneAssets: (sceneId: string, projectAssetIds: string[]) => void;
  onConfirmSceneAssets: (sceneId: string) => void;
  characterImageBusyId: string | null;
  assetConfirmBusyId: string | null;
  onOpenImageSetup: () => void;
  onUnavailable: (message: string) => void;
}) {
  const suggestedStep = getSuggestedLongVideoStep(project);
  const projectId = project?.project.projectId ?? '';
  const voiceOptions = getAvailableVoiceOptions(providerStatus.openAiVoiceOptions);
  const [activeStep, setActiveStep] = useState<LongVideoStepId>(suggestedStep);
  const [assetTab, setAssetTab] = useState<'characters' | ProjectAssetType>('characters');

  useEffect(() => {
    setActiveStep(getSuggestedLongVideoStep(project));
  }, [projectId, suggestedStep]);

  const activeStepIndex = longVideoSteps.findIndex((step) => step.id === activeStep);
  const projectStageIndex = longVideoSteps.findIndex((step) => step.id === suggestedStep);
  const activeStepDefinition = longVideoSteps[activeStepIndex] ?? longVideoSteps[0];
  const previousStep = activeStepIndex > 0 ? longVideoSteps[activeStepIndex - 1] : null;
  const nextStep = activeStepIndex < longVideoSteps.length - 1 ? longVideoSteps[activeStepIndex + 1] : null;
  const canMoveNext = Boolean(nextStep && isLongVideoStepAvailable(nextStep.id, project));

  const renderStepContent = () => {
    if (activeStep === 'setup') {
      return <>
        {project && (
          <section className="card long-video-selected-project">
            <span className="long-video-selected-icon"><FolderOpen size={19} /></span>
            <div><small>DỰ ÁN ĐANG CHỌN</small><strong>{project.project.name}</strong><p>Bạn có thể chuyển sang bước Nội dung để tiếp tục, hoặc nhập chủ đề mới bên dưới để tạo workspace khác.</p></div>
            <button onClick={() => setActiveStep('content')}>Tiếp tục dự án <ArrowRight size={15} /></button>
          </section>
        )}
        <CreateVideoCard
          busy={busy}
          speechSynchronizationEnabled={speechSynchronizationEnabled}
          voiceOptions={voiceOptions}
          voiceCatalogPreviews={voiceCatalogPreviews}
          voiceCatalogPreviewPendingKey={voiceCatalogPreviewPendingKey}
          onPreviewCatalogVoice={onPreviewCatalogVoice}
          voicePreviewProjectName={project?.project.name}
          onCreate={onCreate}
        />
      </>;
    }

    if (activeStep === 'content') {
      return <>
        <LongVideoContentSummary project={project} providerStatus={providerStatus} />
        {project && <LongVideoContentScenes scenes={project.scenes} />}
        <GenerationActions
          project={project}
          errorMessage={contentGenerationError}
          languageFailure={contentLanguageFailure}
          providerStatus={providerStatus}
          busy={busy}
          onGenerateContent={onGenerateContent}
          onRegenerateContent={onRegenerateContent}
          onRepairContent={onRepairContent}
        />
        <WorkflowCard project={project} />
        <PipelineDetails project={project} onUnavailable={onUnavailable} />
      </>;
    }

    if (activeStep === 'assets') {
      return <>
        <div className="long-video-asset-tabs" aria-label="Loại tài sản nhất quán">
          <button className={assetTab === 'characters' ? 'active' : ''} onClick={() => setAssetTab('characters')}><Users size={16} /> Nhân vật <span>{project?.characters.length ?? 0}</span></button>
          <button className={assetTab === 'Background' ? 'active' : ''} onClick={() => setAssetTab('Background')}><MapPin size={16} /> Bối cảnh <span>{countAssets(assetLibrary, 'Background')}</span></button>
          <button className={assetTab === 'Prop' ? 'active' : ''} onClick={() => setAssetTab('Prop')}><Package size={16} /> Đạo cụ <span>{countAssets(assetLibrary, 'Prop')}</span></button>
          <button className={assetTab === 'Item' ? 'active' : ''} onClick={() => setAssetTab('Item')}><Database size={16} /> Item <span>{countAssets(assetLibrary, 'Item')}</span></button>
        </div>
        {assetTab === 'characters' ? (
          <>
            {speechSynchronizationEnabled && project?.speechProductionPolicy === 'CanonicalVoice' && (
              <VoiceProfilesSection
                project={project}
                busy={busy}
                voiceOptions={voiceOptions}
                voiceCatalogPreviews={voiceCatalogPreviews}
                voiceCatalogPreviewPendingKey={voiceCatalogPreviewPendingKey}
                onPreviewCatalogVoice={onPreviewCatalogVoice}
                onCreateDraft={onCreateVoiceProfile}
                onPreview={onPreviewVoiceProfile}
                onApprove={onApproveVoiceProfile}
                onSupersede={onSupersedeVoiceProfile}
              />
            )}
            <CharacterSection
              project={project}
              providerStatus={providerStatus}
              voiceCatalogPreviews={voiceCatalogPreviews}
              voiceCatalogPreviewPendingKey={voiceCatalogPreviewPendingKey}
              onPreviewCatalogVoice={onPreviewCatalogVoice}
              busy={busy}
              imageBusyId={characterImageBusyId}
              onRegenerateContent={onRegenerateContent}
              onUpdate={onUpdateCharacter}
              onSelectReference={onSelectCharacterReference}
              onGenerateReference={onGenerateCharacterReference}
              onApprove={onApproveCharacter}
              onOpenImageSetup={onOpenImageSetup}
            />
          </>
        ) : (
          <ProjectAssetLibrarySection
            project={project}
            library={assetLibrary}
            assetType={assetTab}
            busy={busy}
            onCreate={onCreateProjectAsset}
            onSynchronize={onSynchronizeProjectAssets}
            onApproveAi={onApproveAiProjectAssets}
            onUpdate={onUpdateProjectAsset}
            onLock={onLockProjectAsset}
            onUnlock={onUnlockProjectAsset}
            onDelete={onDeleteProjectAsset}
          />
        )}
      </>;
    }

    if (activeStep === 'storyboard') {
      return <StoryboardSection
        project={project}
        assetLibrary={assetLibrary}
        sceneFirstFrames={sceneFirstFrames}
        firstFrameOperation={firstFrameOperation}
        providerStatus={providerStatus}
        mediaTools={mediaTools}
        busy={busy}
        onGenerateVideo={onGenerateVideo}
        onRequestSceneFirstFrame={onRequestSceneFirstFrame}
        onApproveSceneFirstFrame={onApproveSceneFirstFrame}
        onRejectSceneFirstFrame={onRejectSceneFirstFrame}
        onRetrySceneFirstFrameDownload={onRetrySceneFirstFrameDownload}
        onPreviewSceneFirstFrame={onPreviewSceneFirstFrame}
        onApproveNativeAudio={onApproveSceneNativeAudio}
        onUnapproveAudio={onUnapproveSceneAudio}
        onVerifySceneSpeech={onVerifySceneSpeech}
        onInstallMediaTools={onInstallMediaTools}
        onCheckMediaTools={onCheckMediaTools}
        onUpdateScene={onUpdateScene}
        sceneSaveState={sceneSaveState}
        onClearSaveFailure={onClearSaveFailure}
        onUpdateSceneAssets={onUpdateSceneAssets}
        onConfirmSceneAssets={onConfirmSceneAssets}
        assetConfirmBusyId={assetConfirmBusyId}
      />;
    }

    return <>
      <LongVideoExportOverview project={project} mediaTools={mediaTools} />
      <RenderProgressCard
        project={project}
        busy={busy}
        mediaToolsReady={mediaTools.ready}
        onRender={onRenderFinalVideo}
        onExport={onExportFinalVideo}
        onUnavailable={onUnavailable}
      />
    </>;
  };

  return (
    <div className="page-shell long-video-page">
      <nav className="long-video-stepper" aria-label="Quy trình tạo video dài">
        {longVideoSteps.map((step, index) => {
          const Icon = step.icon;
          const available = isLongVideoStepAvailable(step.id, project);
          const completed = isLongVideoStepCompleted(step.id, project);
          const isProjectStage = index === projectStageIndex;
          const isProgressed = index < projectStageIndex;
          return (
            <button
              key={step.id}
              className={[
                activeStep === step.id ? 'active' : '',
                completed ? 'completed' : '',
                isProjectStage ? 'project-current' : '',
                isProgressed ? 'progressed' : ''
              ].filter(Boolean).join(' ')}
              disabled={!available}
              onClick={() => setActiveStep(step.id)}
              aria-current={activeStep === step.id ? 'step' : undefined}
              aria-label={`Bước ${index + 1}: ${step.label}${isProjectStage ? ' — Giai đoạn hiện tại của dự án' : ''}`}
            >
              <span className="long-video-step-number">{completed ? <Check size={15} strokeWidth={3} /> : <Icon size={16} />}</span>
              <span className="long-video-step-copy"><small>Bước {index + 1}</small><strong>{step.shortLabel}</strong></span>
              {index < longVideoSteps.length - 1 && (
                <span className={`long-video-step-line ${isProgressed ? 'progressed' : ''}`} aria-hidden="true">
                  <span className="long-video-step-light" />
                </span>
              )}
            </button>
          );
        })}
      </nav>

      <div className="long-video-layout">
        <section className="long-video-main">
          <header className="long-video-step-header">
            <span>BƯỚC {activeStepIndex + 1} / {longVideoSteps.length}</span>
            <h2>{activeStepDefinition.label}</h2>
            <p>{activeStepDefinition.description}</p>
          </header>
          <div className="long-video-step-content">{renderStepContent()}</div>
          <footer className="long-video-navigation">
            <button
              className="long-video-back"
              disabled={!previousStep}
              onClick={() => previousStep && setActiveStep(previousStep.id)}
            ><ArrowLeft size={16} /> Quay lại</button>
            <span>Bước {activeStepIndex + 1} trên {longVideoSteps.length}</span>
            <button
              className="long-video-next"
              disabled={!canMoveNext}
              onClick={() => nextStep && setActiveStep(nextStep.id)}
            >{nextStep ? `Tiếp tục: ${nextStep.shortLabel}` : 'Đã đến bước cuối'} <ArrowRight size={16} /></button>
          </footer>
        </section>
        <aside className="long-video-side">
          <PreviewCard project={project} />
          <ProjectInfoCard project={project} />
          <LongVideoReadinessCard providerStatus={providerStatus} mediaTools={mediaTools} project={project} />
        </aside>
      </div>
    </div>
  );
}

function getSuggestedLongVideoStep(project: ProjectDashboard | null): LongVideoStepId {
  if (!project) return 'setup';
  if (project.totalScenes === 0) return 'content';
  const charactersReady = project.characters.every(
    (character) => character.status === 'Approved' && Boolean(character.primaryReference?.previewUrl)
  );
  if (!charactersReady) return 'assets';
  if (project.approvedScenes < project.totalScenes) return 'storyboard';
  return 'export';
}

function isLongVideoStepAvailable(step: LongVideoStepId, project: ProjectDashboard | null): boolean {
  if (step === 'setup') return true;
  if (!project) return false;
  if (step === 'content') return true;
  return project.totalScenes > 0;
}

function isLongVideoStepCompleted(
  step: LongVideoStepId,
  project: ProjectDashboard | null
): boolean {
  if (!project) return false;
  if (step === 'setup') return true;
  if (step === 'content') return Boolean(project.content);
  if (step === 'assets') {
    return project.totalScenes > 0 && project.characters.every(
      (character) => character.status === 'Approved' && Boolean(character.primaryReference?.previewUrl)
    );
  }
  if (step === 'storyboard') {
    return project.totalScenes > 0 && project.approvedScenes === project.totalScenes;
  }
  return Boolean(project.preview?.url);
}

function LongVideoContentSummary({
  project,
  providerStatus
}: {
  project: ProjectDashboard | null;
  providerStatus: GenerationProviderStatus;
}) {
  if (!project) {
    return <section className="card long-video-blocked-step"><FileText size={30} /><h3>Chưa có dự án</h3><p>Quay lại bước Thiết lập và tạo project trước khi sinh nội dung.</p></section>;
  }

  const content = project.content;
  const hasSavedContent = Boolean(content?.scriptFullText.trim());
  const contentFailed = !hasSavedContent && project.project.status === 'Failed';
  const statusLabel = hasSavedContent
    ? 'Đã lưu kịch bản'
    : contentFailed
      ? 'Sinh nội dung thất bại'
      : 'Chưa có nội dung đã lưu';
  const statusTone = hasSavedContent ? 'ready' : contentFailed ? 'error' : 'draft';

  return (
    <section className="card long-video-content-summary">
      <div className="long-video-summary-heading"><div><span>NỘI DUNG HIỆN HÀNH</span><h3>{content?.title ?? project.project.topic}</h3></div><strong className={statusTone}>{statusLabel}</strong></div>
      <div className="long-video-summary-grid">
        <div><small>Ngôn ngữ nội dung</small><strong>{project.effectiveGenerationLanguageCode?.toLowerCase().startsWith('vi') && ['kling', 'fal'].includes(project.videoProviderCode?.toLowerCase() ?? '')
          ? `Tiếng Việt (bắt buộc cho Video Dài dùng ${project.videoProviderCode?.toLowerCase() === 'fal' ? 'Veo' : 'Kling'})`
          : formatLanguage(project.effectiveGenerationLanguageCode ?? project.languageCode)}</strong></div>
        <div><small>Thời lượng mục tiêu</small><strong>{formatDuration(project.project.targetDurationSeconds)}</strong></div>
        <div><small>Số cảnh</small><strong>{project.totalScenes}</strong></div>
        <div><small>OpenAI model</small><strong>{providerStatus.openAiModel ?? 'Chưa cấu hình'}</strong></div>
      </div>
      {hasSavedContent && content ? (
        <div className="long-video-saved-content">
          <div className="long-video-content-metadata">
            <div><small>Hook</small><p>{content.hook || 'Chưa có'}</p></div>
            <div><small>Góc triển khai</small><p>{content.angle || 'Chưa có'}</p></div>
            <div><small>Đối tượng người xem</small><p>{content.audience || 'Chưa có'}</p></div>
            <div><small>Kêu gọi hành động</small><p>{content.callToAction || 'Chưa có'}</p></div>
          </div>
          <article className="long-video-script-content">
            <header>
              <div><span>KỊCH BẢN ĐÃ TẠO</span><h4>{content.title}</h4></div>
              <strong>Phiên bản {content.scriptVersion}</strong>
            </header>
            <p>{content.scriptFullText}</p>
          </article>
          {project.totalScenes === 0 && (
            <p className="long-video-scene-plan-warning">Kịch bản đã được lưu nhưng dự án chưa có scene plan để hiển thị.</p>
          )}
        </div>
      ) : (
        <div className={`long-video-content-empty${contentFailed ? ' error' : ''}`}>
          <FileText size={24} />
          <div>
            <strong>{contentFailed ? 'Lần sinh trước thất bại, dự án chưa có content đã lưu' : 'Dự án chưa có content đã lưu'}</strong>
            <p>{project.lastErrorMessage ?? 'Hãy tạo nội dung để lưu content plan và kịch bản cho dự án này.'}</p>
          </div>
        </div>
      )}
      {project.requiresVietnameseContentRegeneration && (
        <p className="long-video-language-warning">Nội dung hiện tại còn tiếng Anh và sẽ bị chặn trước khi gọi provider video. Hãy dùng hành động “Sinh lại nội dung tiếng Việt”.</p>
      )}
    </section>
  );
}

function LongVideoContentScenes({ scenes }: { scenes: SceneSummary[] }) {
  const sceneIdsKey = scenes.map((scene) => scene.sceneId).join('|');
  const [expandedSceneIds, setExpandedSceneIds] = useState<Set<string>>(
    () => new Set(scenes.map((scene) => scene.sceneId))
  );

  useEffect(() => {
    setExpandedSceneIds(new Set(scenes.map((scene) => scene.sceneId)));
  }, [sceneIdsKey]);

  if (scenes.length === 0) {
    return (
      <section className="card long-video-content-scenes empty">
        <FileText size={28} />
        <div>
          <span>CHI TIẾT KỊCH BẢN</span>
          <h3>Chưa có cảnh để hiển thị</h3>
          <p>Sinh nội dung bằng OpenAI để xem lời dẫn, hình ảnh, prompt và âm thanh của từng cảnh tại đây.</p>
        </div>
      </section>
    );
  }

  const allExpanded = scenes.every((scene) => expandedSceneIds.has(scene.sceneId));
  const toggleAllScenes = () => {
    setExpandedSceneIds(allExpanded
      ? new Set<string>()
      : new Set(scenes.map((scene) => scene.sceneId)));
  };
  const toggleScene = (sceneId: string) => {
    setExpandedSceneIds((current) => {
      const next = new Set(current);
      if (next.has(sceneId)) next.delete(sceneId);
      else next.add(sceneId);
      return next;
    });
  };

  return (
    <section className="card long-video-content-scenes">
      <header className="content-scenes-header">
        <div>
          <span>CHI TIẾT KỊCH BẢN</span>
          <h2>{scenes.length} cảnh trong scene plan hiện hành</h2>
          <p>Kiểm tra nội dung, hình ảnh, prompt và ý đồ âm thanh trước khi chuyển sang bước chuẩn hóa tài sản.</p>
        </div>
        <button type="button" onClick={toggleAllScenes} aria-expanded={allExpanded}>
          <ListVideo size={15} /> {allExpanded ? 'Thu gọn tất cả' : 'Mở tất cả'}
        </button>
      </header>

      <div className="content-scenes-list">
        {scenes.map((scene) => {
          const expanded = expandedSceneIds.has(scene.sceneId);
          const status = sceneStatus(scene);
          const spokenText = scene.narration?.trim();
          const durationSeconds = Math.max(0, Math.round(scene.durationMs / 1000));
          const generationDurationSeconds = Math.max(0, Math.round(scene.generationDurationMs / 1000));

          return (
            <article className={`content-scene-card ${expanded ? 'expanded' : ''}`} key={scene.sceneId}>
              <button
                type="button"
                className="content-scene-summary"
                aria-expanded={expanded}
                aria-controls={`content-scene-${scene.sceneId}`}
                onClick={() => toggleScene(scene.sceneId)}
              >
                <span className="content-scene-number">{scene.sequenceNumber}</span>
                <span className="content-scene-heading">
                  <small>CẢNH {scene.sequenceNumber}</small>
                  <strong>{scene.storyPurpose?.trim() || `Nội dung cảnh ${scene.sequenceNumber}`}</strong>
                </span>
                <span className="content-scene-timing">
                  <small>{formatTimeline(scene.timelineStartMs)}–{formatTimeline(scene.timelineEndMs)}</small>
                  <strong>{durationSeconds} giây</strong>
                </span>
                <span className={`content-scene-status ${status.tone}`}>{status.label}</span>
                <ChevronDown size={17} className="content-scene-chevron" />
              </button>

              {expanded && (
                <div className="content-scene-details" id={`content-scene-${scene.sceneId}`}>
                  <div className="content-scene-meta">
                    <span><Clock3 size={13} /> Nội dung {durationSeconds}s</span>
                    <span><Film size={13} /> Provider {generationDurationSeconds}s</span>
                    {generationDurationSeconds > durationSeconds && (
                      <span><Clock3 size={13} /> Cắt đuôi {generationDurationSeconds - durationSeconds}s trước khi duyệt</span>
                    )}
                    <span><Volume2 size={13} /> {speechModeLabel(scene.speechMode)}</span>
                  </div>

                  <div className="content-scene-character-row">
                    <strong><Users size={14} /> Nhân vật</strong>
                    <div>
                      {scene.characters.length > 0
                        ? scene.characters.map((character) => <span key={character.characterId}>{character.name}</span>)
                        : <small>Không có nhân vật cố định trong cảnh.</small>}
                    </div>
                  </div>

                  <div className="content-scene-copy-grid">
                    <section className="content-scene-copy-block speech">
                      <div>
                        <span>LỜI THOẠI / LỜI DẪN</span>
                        <small>{scene.speakerCharacterName ? `Người nói: ${scene.speakerCharacterName}` : speechModeLabel(scene.speechMode)}</small>
                      </div>
                      {spokenText
                        ? <ExpandableSceneText text={spokenText} collapseAt={260} />
                        : <p className="content-scene-placeholder">Cảnh không có lời nói; chỉ sử dụng âm thanh môi trường và hiệu ứng phù hợp.</p>}
                      {spokenText && <SpeechPacingIndicator scene={scene} />}
                    </section>

                    <section className="content-scene-copy-block visual">
                      <div><span>MÔ TẢ HÌNH ẢNH</span><small>Hành động, bối cảnh và máy quay</small></div>
                      <ExpandableSceneText
                        text={scene.visualDescription?.trim() || 'Chưa có mô tả hình ảnh.'}
                        collapseAt={360}
                      />
                    </section>

                    <section className="content-scene-copy-block prompt">
                      <div><span>PROMPT SINH VIDEO</span><small>Prompt hiệu lực của scene plan</small></div>
                      <ExpandableSceneText
                        text={scene.prompt?.trim() || 'Chưa có prompt sinh video.'}
                        collapseAt={480}
                      />
                    </section>
                  </div>

                  <div className="content-scene-audio-grid">
                    <div><small>Phong cách giọng</small><strong>{scene.voiceStyle?.trim() || 'Tự nhiên, rõ ràng'}</strong></div>
                    <div><small>Âm thanh môi trường</small><strong>{scene.ambientAudio?.trim() || 'Phù hợp với bối cảnh'}</strong></div>
                    <div><small>Hiệu ứng âm thanh</small><strong>{scene.soundEffects?.trim() || 'Không yêu cầu riêng'}</strong></div>
                  </div>
                </div>
              )}
            </article>
          );
        })}
      </div>
    </section>
  );
}

function LongVideoExportOverview({ project, mediaTools }: { project: ProjectDashboard | null; mediaTools: MediaToolStatus }) {
  const totalScenes = project?.totalScenes ?? 0;
  const approvedScenes = project?.approvedScenes ?? 0;
  const ready = approvedScenes > 0 && mediaTools.ready;
  const allScenesApproved = totalScenes > 0 && approvedScenes === totalScenes;
  return (
    <section className="card long-video-export-overview">
      <div className={`long-video-export-icon ${ready ? 'ready' : ''}`}>{ready ? <CircleCheck size={25} /> : <Clapperboard size={25} />}</div>
      <div><span>KIỂM TRA TRƯỚC KHI XUẤT</span><h3>{ready ? allScenesApproved ? 'Dự án đã sẵn sàng để dựng video cuối' : 'Có thể dựng video từ các cảnh đã duyệt' : 'Hoàn tất các điều kiện còn thiếu'}</h3><p>{totalScenes > 0 ? `Đã duyệt ${approvedScenes}/${totalScenes} cảnh. ${approvedScenes > 0 && approvedScenes < totalScenes ? 'Bản dựng chỉ gồm các cảnh đã duyệt. ' : ''}${mediaTools.ready ? 'FFmpeg và FFprobe đã sẵn sàng.' : mediaTools.message}` : 'Dự án chưa có cảnh để dựng video.'}</p></div>
      <strong className={ready ? 'ready' : 'waiting'}>{ready ? 'Sẵn sàng' : 'Chưa sẵn sàng'}</strong>
    </section>
  );
}

function LongVideoReadinessCard({
  providerStatus,
  mediaTools,
  project
}: {
  providerStatus: GenerationProviderStatus;
  mediaTools: MediaToolStatus;
  project: ProjectDashboard | null;
}) {
  const hasScenePlan = Boolean(project && project.totalScenes > 0);
  const hasCharacters = Boolean(project && project.characters.length > 0);
  const charactersReady = Boolean(hasScenePlan && project && project.characters.every(
    (character) => character.status === 'Approved' && Boolean(character.primaryReference?.previewUrl)
  ));
  const characterLabel = !hasScenePlan
    ? 'Chưa có'
    : !hasCharacters
      ? 'Không yêu cầu'
      : charactersReady
        ? 'Đã khóa'
        : 'Cần hoàn tất';
  return (
    <section className="card side-card long-video-readiness-card">
      <h2>Điều kiện workflow</h2>
      <div><span><Bot size={15} /> OpenAI Content</span><strong className={providerStatus.openAiReady ? 'ready' : 'missing'}>{providerStatus.openAiReady ? 'Sẵn sàng' : 'Thiếu cấu hình'}</strong></div>
      <div><span><Film size={15} /> Video Provider</span><strong className={providerStatus.videoReady ? 'ready' : 'missing'}>{providerStatus.videoReady ? 'Sẵn sàng' : 'Thiếu cấu hình'}</strong></div>
      {project?.speechProductionPolicy === 'CanonicalVoice' && (
        <div><span><Volume2 size={15} /> Canonical TTS</span><strong className={providerStatus.openAiVoiceReady ? 'ready' : 'missing'}>{providerStatus.openAiVoiceReady ? 'Sẵn sàng' : 'Thiếu cấu hình'}</strong></div>
      )}
      <div><span><Users size={15} /> Nhân vật</span><strong className={charactersReady ? 'ready' : 'waiting'}>{characterLabel}</strong></div>
      <div><span><Clapperboard size={15} /> FFmpeg</span><strong className={mediaTools.ready ? 'ready' : 'missing'}>{mediaTools.ready ? 'Sẵn sàng' : 'Cần kiểm tra'}</strong></div>
    </section>
  );
}

function GenerationActions({
  project,
  errorMessage,
  languageFailure,
  providerStatus,
  busy,
  onGenerateContent,
  onRegenerateContent,
  onRepairContent
}: {
  project: ProjectDashboard | null;
  errorMessage: string | null;
  languageFailure: ContentLanguageFailureView | null;
  providerStatus: GenerationProviderStatus;
  busy: boolean;
  onGenerateContent: () => void;
  onRegenerateContent: () => void;
  onRepairContent: () => void;
}) {
  if (!project) return null;
  const hasContent = project.totalScenes > 0;
  if (hasContent && !project.requiresVietnameseContentRegeneration && !errorMessage) return null;
  const requiresVietnamese = project.requiresVietnameseContentRegeneration;

  return (
    <section className="card generation-actions">
      <div>
        <span className="generation-eyebrow">API GENERATION</span>
        <h2>{requiresVietnamese ? 'Sinh lại nội dung tiếng Việt' : 'Tạo nội dung và prompt'}</h2>
        <p>{requiresVietnamese
          ? 'Dự án video dài dùng provider Native Audio còn dữ liệu tiếng Anh. OpenAI cần tạo một version tiếng Việt mới trước khi sinh clip.'
          : 'OpenAI sẽ viết hook, kịch bản, chia cảnh và tạo prompt có cấu trúc.'}</p>
        {errorMessage && (
          <div className="generation-content-error" role="alert">
            <TriangleAlert size={15} />
            <div>
              <strong>{languageFailure?.message ?? errorMessage}</strong>
              {languageFailure && languageFailure.violations.length > 0 && (
                <ul>
                  {languageFailure.violations.map((violation) => (
                    <li key={`${violation.field}:${violation.reason}`}>{formatViolation(violation)}</li>
                  ))}
                </ul>
              )}
              {languageFailure?.providerRequestId && (
                <small>Mã đối chiếu: {shortProviderRequestId(languageFailure.providerRequestId)}</small>
              )}
              {languageFailure?.canRepair && (
                <button type="button" disabled={busy || !providerStatus.openAiReady} onClick={onRepairContent}>
                  <WandSparkles size={14} /> Sửa các trường bằng AI
                </button>
              )}
              {languageFailure && !languageFailure.canRepair && (
                <small>{languageFailure.code === 'content_failure_schema_not_ready'
                  ? 'Server chưa có migration 4.1.2; hãy liên hệ vận hành, không thử repair.'
                  : 'Bản lỗi này không thể phục hồi; hãy tạo lại toàn bộ nội dung.'}</small>
              )}
            </div>
          </div>
        )}
      </div>
      <div className="generation-provider-state">
        <span className={providerStatus.openAiReady ? 'ready' : 'missing'}>
          OpenAI · {providerStatus.openAiReady ? providerStatus.openAiModel : 'chưa được cấu hình'}
        </span>
        <span className={providerStatus.videoReady ? 'ready' : 'missing'}>
          Video · {providerStatus.videoReady ? `${providerStatus.videoProviderName ?? providerStatus.videoProviderCode} / ${providerStatus.videoModel}` : 'chưa được cấu hình'}
        </span>
      </div>
      <button
        disabled={busy || !providerStatus.openAiReady}
        onClick={errorMessage || requiresVietnamese ? onRegenerateContent : onGenerateContent}
      >
        {busy ? <LoaderCircle className="spin" size={18} /> : <WandSparkles size={18} />}
        {errorMessage ? 'Tạo lại toàn bộ' : requiresVietnamese ? 'Sinh lại nội dung tiếng Việt' : 'Tạo nội dung & chia cảnh'}
      </button>
    </section>
  );
}

function voiceCatalogPreviewKey(voiceCode: string, speakingRate: number): string {
  return `${voiceCode.trim().toLowerCase()}@${speakingRate.toFixed(3)}`;
}

function VoicePickerField({
  value,
  options,
  disabled = false,
  allowInherited = false,
  previewControls,
  onChange
}: {
  value: string;
  options: OpenAiVoiceOption[];
  disabled?: boolean;
  allowInherited?: boolean;
  previewControls?: VoiceCatalogPreviewControls;
  onChange: (voiceCode: string) => void;
}) {
  const [open, setOpen] = useState(false);
  const selected = resolveVoiceOption(value, options);
  const unavailable = disabled || options.length === 0;
  const displayName = value
    ? selected?.displayName ?? voiceDisplayName(value, options)
    : 'Dùng giọng narrator của dự án';

  return (
    <>
      <button
        type="button"
        className="voice-picker-trigger"
        aria-haspopup="dialog"
        aria-expanded={open}
        disabled={unavailable}
        onClick={() => setOpen(true)}
      >
        <span className="voice-picker-trigger-icon"><Volume2 size={15} /></span>
        <span className="voice-picker-trigger-copy">
          <strong>{displayName}</strong>
          <small>{options.length > 0 ? `${options.length} giọng OpenAI` : 'Chưa có catalog giọng'}</small>
        </span>
        <ChevronDown size={15} />
      </button>
      {open && (
        <VoicePickerModal
          selectedVoiceCode={value}
          options={options}
          allowInherited={allowInherited}
          previewControls={previewControls}
          onApply={(voiceCode) => {
            onChange(voiceCode);
            setOpen(false);
          }}
          onClose={() => setOpen(false)}
        />
      )}
    </>
  );
}

function VoicePickerModal({
  selectedVoiceCode,
  options,
  allowInherited,
  previewControls,
  onApply,
  onClose
}: {
  selectedVoiceCode: string;
  options: OpenAiVoiceOption[];
  allowInherited: boolean;
  previewControls?: VoiceCatalogPreviewControls;
  onApply: (voiceCode: string) => void;
  onClose: () => void;
}) {
  const titleId = useId();
  const descriptionId = useId();
  const dialogRef = useRef<HTMLDivElement>(null);
  const audioRef = useRef<HTMLAudioElement>(null);
  const selectedOption = resolveVoiceOption(selectedVoiceCode, options);
  const [pendingVoiceCode, setPendingVoiceCode] = useState(
    selectedVoiceCode === '' && allowInherited
      ? ''
      : selectedOption?.voiceCode ?? options[0]?.voiceCode ?? ''
  );
  const [requestedPreviewKey, setRequestedPreviewKey] = useState<string | null>(null);
  const [activePreviewKey, setActivePreviewKey] = useState<string | null>(null);
  const [playingPreviewKey, setPlayingPreviewKey] = useState<string | null>(null);
  const activePreview = activePreviewKey && previewControls
    ? previewControls.previews[activePreviewKey] ?? null
    : null;

  useEffect(() => {
    if (!requestedPreviewKey || !previewControls?.previews[requestedPreviewKey]) return;
    setActivePreviewKey(requestedPreviewKey);
    setRequestedPreviewKey(null);
  }, [previewControls?.previews, requestedPreviewKey]);

  useEffect(() => {
    const audio = audioRef.current;
    if (!audio || !activePreview?.previewUrl) return;
    audio.load();
    void audio.play().catch(() => setPlayingPreviewKey(null));
  }, [activePreview?.previewUrl]);

  const toggleVoicePreview = (option: OpenAiVoiceOption) => {
    if (!previewControls) return;
    const previewKey = voiceCatalogPreviewKey(option.voiceCode, previewControls.speakingRate);
    const cachedPreview = previewControls.previews[previewKey];
    if (!cachedPreview) {
      setRequestedPreviewKey(previewKey);
      previewControls.onPreview(option.voiceCode, previewControls.speakingRate);
      return;
    }
    const audio = audioRef.current;
    if (activePreviewKey === previewKey && audio) {
      if (audio.paused) {
        void audio.play().catch(() => setPlayingPreviewKey(null));
      } else {
        audio.pause();
      }
      return;
    }
    setActivePreviewKey(previewKey);
  };

  useLayoutEffect(() => {
    const previouslyFocused = document.activeElement instanceof HTMLElement
      ? document.activeElement
      : null;
    const dialog = dialogRef.current;
    const preferred = dialog?.querySelector<HTMLElement>('[aria-checked="true"]');
    (preferred ?? dialog)?.focus();

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        onClose();
        return;
      }
      if (event.key !== 'Tab' || !dialog) return;

      const focusable = Array.from(dialog.querySelectorAll<HTMLElement>(
        'button:not(:disabled), [href], input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex]:not([tabindex="-1"])'
      ));
      if (focusable.length === 0) {
        event.preventDefault();
        dialog.focus();
        return;
      }
      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    };

    document.addEventListener('keydown', handleKeyDown);
    return () => {
      document.removeEventListener('keydown', handleKeyDown);
      previouslyFocused?.focus();
    };
  }, [onClose]);

  return createPortal(
    <div
      className="voice-picker-overlay"
      role="presentation"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onClose();
      }}
    >
      <div
        ref={dialogRef}
        className="voice-picker-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={descriptionId}
        tabIndex={-1}
      >
        <header className="voice-picker-header">
          <div>
            <span>OPENAI · TEXT TO SPEECH</span>
            <h2 id={titleId}>Chọn giọng đọc</h2>
            <p id={descriptionId}>Chọn một giọng dựng sẵn. Chọn giọng là miễn phí; tạo audio nghe thử sẽ báo giá trước.</p>
          </div>
          <button type="button" className="voice-picker-close" aria-label="Đóng danh sách giọng" onClick={onClose}>
            <X size={18} />
          </button>
        </header>

        <div className="voice-picker-grid" role="radiogroup" aria-label="Danh sách giọng OpenAI">
          {allowInherited && (
            <div className={`voice-picker-option inherited${pendingVoiceCode === '' ? ' selected' : ''}`}>
              <button
                type="button"
                className="voice-picker-option-select"
                role="radio"
                aria-checked={pendingVoiceCode === ''}
                onClick={() => setPendingVoiceCode('')}
              >
                <span className="voice-picker-option-icon"><Users size={17} /></span>
                <span><strong>Dùng giọng narrator</strong><small>Kế thừa giọng chung của dự án</small></span>
                {pendingVoiceCode === '' && <CircleCheck size={18} />}
              </button>
            </div>
          )}
          {options.map((option) => {
            const selected = pendingVoiceCode === option.voiceCode;
            const previewKey = previewControls
              ? voiceCatalogPreviewKey(option.voiceCode, previewControls.speakingRate)
              : null;
            const previewReady = Boolean(previewKey && previewControls?.previews[previewKey]);
            const previewBusy = Boolean(previewKey && previewControls?.pendingKey === previewKey);
            const previewPlaying = Boolean(previewKey && playingPreviewKey === previewKey);
            return (
              <div className={`voice-picker-option${selected ? ' selected' : ''}`} key={option.voiceCode}>
                <button
                  type="button"
                  className="voice-picker-option-select"
                  role="radio"
                  aria-checked={selected}
                  onClick={() => setPendingVoiceCode(option.voiceCode)}
                >
                  <span className="voice-picker-option-icon"><Volume2 size={17} /></span>
                  <span><strong>{option.displayName}</strong><small>{option.voiceCode} · OpenAI built-in</small></span>
                  {selected && <CircleCheck size={18} />}
                </button>
                {previewControls && (
                  <button
                    type="button"
                    className={`voice-picker-preview${previewReady ? ' ready' : ''}`}
                    aria-label={`${previewPlaying ? 'Tạm dừng' : 'Nghe thử'} giọng ${option.displayName}`}
                    disabled={previewBusy || (previewControls.pendingKey !== null && !previewBusy)}
                    onClick={() => toggleVoicePreview(option)}
                  >
                    {previewBusy
                      ? <LoaderCircle className="spin" size={14} />
                      : previewPlaying
                        ? <Pause size={14} fill="currentColor" />
                        : <Play size={14} fill="currentColor" />}
                    <span>{previewBusy ? 'Đang tạo' : previewPlaying ? 'Tạm dừng' : previewReady ? 'Phát lại' : 'Nghe thử'}</span>
                  </button>
                )}
              </div>
            );
          })}
        </div>

        {activePreview && (
          <audio
            ref={audioRef}
            className="voice-picker-audio"
            preload="metadata"
            src={activePreview.previewUrl}
            onPlay={() => setPlayingPreviewKey(activePreviewKey)}
            onPause={() => setPlayingPreviewKey(null)}
            onEnded={() => setPlayingPreviewKey(null)}
          />
        )}

        <footer className="voice-picker-footer">
          <p><CircleHelp size={14} /> {previewControls?.contextProjectName
            ? `Nghe thử dùng câu mẫu tiếng Việt ngắn, luôn báo giá trước và ghi nhận chi phí vào dự án đang chọn: ${previewControls.contextProjectName}.`
            : 'Có thể nghe thử trước khi tạo dự án. Mẫu audio mới luôn được báo giá trước và ghi nhận qua ngữ cảnh hệ thống ẩn.'}</p>
          <div>
            <button type="button" className="voice-picker-cancel" onClick={onClose}>Hủy</button>
            <button
              type="button"
              className="voice-picker-apply"
              disabled={!allowInherited && !pendingVoiceCode}
              onClick={() => onApply(pendingVoiceCode)}
            >
              <Check size={16} /> Áp dụng giọng
            </button>
          </div>
        </footer>
      </div>
    </div>,
    document.body
  );
}

function VoiceProfilesSection({
  project,
  busy,
  voiceOptions,
  voiceCatalogPreviews,
  voiceCatalogPreviewPendingKey,
  onPreviewCatalogVoice,
  onCreateDraft,
  onPreview,
  onApprove,
  onSupersede
}: {
  project: ProjectDashboard;
  busy: boolean;
  voiceOptions: OpenAiVoiceOption[];
  voiceCatalogPreviews: Record<string, VoiceCatalogPreviewPlayback>;
  voiceCatalogPreviewPendingKey: string | null;
  onPreviewCatalogVoice: (voiceCode: string, speakingRate: number) => void;
  onCreateDraft: (scope: 'ProjectNarrator' | 'Character', characterId: string | null, voiceCode: string, speakingRate: number) => void;
  onPreview: (version: VoiceProfileSummary) => void;
  onApprove: (version: VoiceProfileSummary, playbackConfirmed: boolean) => void;
  onSupersede: (version: VoiceProfileSummary) => void;
}) {
  const versions = project.voiceProfiles ?? [];
  const hasNarratorScenes = project.scenes.some((scene) => scene.speechMode === 'NativeVoiceOver');
  const speakingCharacterIds = new Set(project.scenes
    .filter((scene) => scene.speechMode === 'OnCameraDialogue')
    .flatMap((scene) => scene.characters.map((character) => character.characterId)));
  const targets = [
    ...(hasNarratorScenes ? [{
      key: 'narrator',
      label: 'Narrator của dự án',
      scope: 'ProjectNarrator' as const,
      characterId: null,
      defaultVoiceCode: project.voiceCode ?? 'female-sweet',
      defaultRate: project.voiceSpeakingRate ?? 1
    }] : []),
    ...project.characters
      .filter((character) => speakingCharacterIds.has(character.characterId))
      .map((character) => ({
        key: character.characterId,
        label: `Nhân vật ${character.name}`,
        scope: 'Character' as const,
        characterId: character.characterId,
        defaultVoiceCode: character.voiceCode ?? project.voiceCode ?? 'female-sweet',
        defaultRate: character.voiceSpeakingRate ?? project.voiceSpeakingRate ?? 1
      }))
  ];

  return (
    <section className="card voice-profile-section">
      <header className="voice-profile-header">
        <div>
          <span className="generation-eyebrow">CANONICAL VOICE · NGUỒN GIỌNG BẤT BIẾN</span>
          <h2>Khóa giọng trước khi tạo video</h2>
          <p>Mỗi narrator/nhân vật phải có một version đã nghe preview và duyệt. Đổi giọng sẽ làm mất hiệu lực audio/render cũ.</p>
        </div>
        <span className="voice-policy-badge"><Volume2 size={14} /> CanonicalVoice</span>
      </header>
      {targets.length === 0 ? (
        <div className="voice-profile-empty"><CircleHelp size={16} /> Nội dung hiện tại chưa có cảnh cần lời nói.</div>
      ) : (
        <div className="voice-profile-grid">
          {targets.map((target) => (
            <VoiceProfileTargetCard
              key={target.key}
              label={target.label}
              scope={target.scope}
              characterId={target.characterId}
              defaultVoiceCode={target.defaultVoiceCode}
              defaultRate={target.defaultRate}
              voiceOptions={voiceOptions}
              voiceCatalogPreviews={voiceCatalogPreviews}
              voiceCatalogPreviewPendingKey={voiceCatalogPreviewPendingKey}
              onPreviewCatalogVoice={onPreviewCatalogVoice}
              versions={versions.filter((version) =>
                version.scope === target.scope &&
                (target.characterId === null
                  ? !version.characterId
                  : version.characterId?.toLowerCase() === target.characterId.toLowerCase()))}
              busy={busy}
              onCreateDraft={onCreateDraft}
              onPreview={onPreview}
              onApprove={onApprove}
              onSupersede={onSupersede}
            />
          ))}
        </div>
      )}
    </section>
  );
}

function VoiceProfileTargetCard({
  label,
  scope,
  characterId,
  defaultVoiceCode,
  defaultRate,
  voiceOptions,
  voiceCatalogPreviews,
  voiceCatalogPreviewPendingKey,
  onPreviewCatalogVoice,
  versions,
  busy,
  onCreateDraft,
  onPreview,
  onApprove,
  onSupersede
}: {
  label: string;
  scope: 'ProjectNarrator' | 'Character';
  characterId: string | null;
  defaultVoiceCode: string;
  defaultRate: number;
  voiceOptions: OpenAiVoiceOption[];
  voiceCatalogPreviews: Record<string, VoiceCatalogPreviewPlayback>;
  voiceCatalogPreviewPendingKey: string | null;
  onPreviewCatalogVoice: (voiceCode: string, speakingRate: number) => void;
  versions: VoiceProfileSummary[];
  busy: boolean;
  onCreateDraft: (scope: 'ProjectNarrator' | 'Character', characterId: string | null, voiceCode: string, speakingRate: number) => void;
  onPreview: (version: VoiceProfileSummary) => void;
  onApprove: (version: VoiceProfileSummary, playbackConfirmed: boolean) => void;
  onSupersede: (version: VoiceProfileSummary) => void;
}) {
  const approved = versions.find((version) => version.status === 'Approved') ?? null;
  const draft = versions.find((version) => version.status === 'Draft') ?? null;
  const [voiceCode, setVoiceCode] = useState(defaultVoiceCode);
  const [speakingRate, setSpeakingRate] = useState(defaultRate);
  const [previewPlayed, setPreviewPlayed] = useState(false);

  useEffect(() => {
    setVoiceCode(draft?.voiceCode ?? approved?.voiceCode ?? defaultVoiceCode);
    setSpeakingRate(draft?.speakingRate ?? approved?.speakingRate ?? defaultRate);
    setPreviewPlayed(false);
  }, [draft?.voiceProfileVersionId, approved?.voiceProfileVersionId, defaultVoiceCode, defaultRate]);

  return (
    <article className={`voice-profile-card${approved ? ' approved' : ''}`}>
      <div className="voice-profile-title">
        <div><small>{scope === 'ProjectNarrator' ? 'PROJECT NARRATOR' : 'CHARACTER VOICE'}</small><strong>{label}</strong></div>
        <span className={approved ? 'ready' : draft ? 'draft' : 'missing'}>
          {approved ? <ShieldCheck size={13} /> : <Clock3 size={13} />}
          {approved ? `Đã khóa v${approved.version}` : draft ? `Bản nháp v${draft.version}` : 'Chưa khóa'}
        </span>
      </div>
      <div className="voice-profile-controls">
        <div className="voice-profile-control">
          <span>Giọng</span>
          <VoicePickerField
            value={voiceCode}
            options={voiceOptions}
            disabled={busy || Boolean(draft)}
            previewControls={{
              speakingRate,
              previews: voiceCatalogPreviews,
              pendingKey: voiceCatalogPreviewPendingKey,
              onPreview: onPreviewCatalogVoice
            }}
            onChange={setVoiceCode}
          />
        </div>
        <label>Tốc độ<select disabled={busy || Boolean(draft)} value={speakingRate} onChange={(event) => setSpeakingRate(Number(event.target.value))}>
          <option value={0.9}>Chậm · 0,9×</option>
          <option value={1}>Tự nhiên · 1,0×</option>
          <option value={1.1}>Nhanh · 1,1×</option>
        </select></label>
      </div>
      {draft ? (
        <div className="voice-profile-version">
          <div className="voice-profile-version-meta">
            <span>{draft.providerCode}/{draft.modelCode}</span>
            <span>snapshot {draft.snapshotHash.slice(0, 12)}</span>
          </div>
          {draft.preview?.url ? (
            <>
              <audio controls preload="metadata" src={draft.preview.url} onEnded={() => setPreviewPlayed(true)} />
              <small>{previewPlayed ? 'Đã nghe hết preview · có thể duyệt' : 'Hãy nghe hết preview trước khi duyệt.'}</small>
            </>
          ) : (
            <div className="voice-preview-missing"><TriangleAlert size={14} /> Chưa có audio preview trên máy.</div>
          )}
          <div className="voice-profile-actions">
            <button type="button" disabled={busy} onClick={() => onPreview(draft)}><WandSparkles size={14} /> {draft.preview?.url ? 'Tạo lại preview' : 'Tạo preview'}</button>
            <button type="button" className="voice-approve" disabled={busy || !draft.preview?.url || !previewPlayed} onClick={() => onApprove(draft, previewPlayed)}><ShieldCheck size={14} /> Duyệt giọng</button>
            <button type="button" className="voice-cancel" disabled={busy} onClick={() => onSupersede(draft)}><X size={14} /> Bỏ nháp</button>
          </div>
        </div>
      ) : (
        <div className="voice-profile-actions">
          <button type="button" disabled={busy} onClick={() => onCreateDraft(scope, characterId, voiceCode, speakingRate)}>
            <Plus size={14} /> {approved ? 'Tạo version mới' : 'Tạo bản nháp giọng'}
          </button>
          {approved && <button type="button" className="voice-cancel" disabled={busy} onClick={() => onSupersede(approved)}><X size={14} /> Hủy duyệt</button>}
        </div>
      )}
      {approved && (
        <div className="voice-approved-summary">
          <CircleCheck size={14} /> {voiceDisplayName(approved.voiceCode, voiceOptions)} · {approved.speakingRate}× · v{approved.version} · {approved.snapshotHash.slice(0, 12)}
        </div>
      )}
    </article>
  );
}

function CharacterSection({
  project,
  providerStatus,
  voiceCatalogPreviews = {},
  voiceCatalogPreviewPendingKey = null,
  onPreviewCatalogVoice,
  busy,
  imageBusyId,
  onRegenerateContent,
  onUpdate,
  onSelectReference,
  onGenerateReference,
  onApprove,
  onOpenImageSetup
}: {
  project: ProjectDashboard | null;
  providerStatus: GenerationProviderStatus;
  voiceCatalogPreviews?: Record<string, VoiceCatalogPreviewPlayback>;
  voiceCatalogPreviewPendingKey?: string | null;
  onPreviewCatalogVoice?: (voiceCode: string, speakingRate: number) => void;
  busy: boolean;
  imageBusyId: string | null;
  onRegenerateContent: () => void;
  onUpdate: (payload: UpdateCharacterPayload) => void;
  onSelectReference: (characterId: string) => void;
  onGenerateReference: (character: CharacterSummary) => void;
  onApprove: (characterId: string) => void;
  onOpenImageSetup: () => void;
}) {
  if (!project || project.scenes.length === 0) return null;
  const characters = project.characters ?? [];
  const readyCount = characters.filter(
    (character) => character.status === 'Approved' && Boolean(character.primaryReference?.previewUrl)
  ).length;

  return (
    <section className="card character-section">
      <header className="character-section-header">
        <div>
          <span className="generation-eyebrow">NHÂN VẬT &amp; PHONG CÁCH</span>
          <h2>Khóa nhân vật xuyên suốt các cảnh</h2>
          <p>
            Hồ sơ và ảnh chính được dùng lại khi tạo từng clip để hạn chế đổi khuôn mặt, tóc và trang phục.
          </p>
        </div>
        {characters.length > 0 && (
          <span className={readyCount === characters.length ? 'character-ready-count ready' : 'character-ready-count'}>
            <ShieldCheck size={15} /> {readyCount}/{characters.length} nhân vật đã khóa
          </span>
        )}
      </header>

      {characters.length === 0 ? (
        <div className="character-empty">
          <div className="character-empty-icon"><Users size={25} /></div>
          <div>
            <strong>Content hiện tại chưa có hồ sơ nhân vật</strong>
            <p>Dự án được tạo bằng phiên bản content cũ. Hãy sinh lại content để AI tách nhân vật và gán vào từng cảnh.</p>
          </div>
          <button
            type="button"
            disabled={busy}
            onClick={onRegenerateContent}
          >
            <WandSparkles size={16} /> Sinh lại content có nhân vật
          </button>
        </div>
      ) : (
        <div className="character-grid">
          {characters.map((character) => (
            <CharacterCard
              key={character.characterId}
              character={character}
              providerStatus={providerStatus}
              voiceCatalogPreviews={voiceCatalogPreviews}
              voiceCatalogPreviewPendingKey={voiceCatalogPreviewPendingKey}
              onPreviewCatalogVoice={onPreviewCatalogVoice}
              busy={busy}
              imageBusy={imageBusyId === character.characterId}
              onUpdate={onUpdate}
              onSelectReference={onSelectReference}
              onGenerateReference={onGenerateReference}
              onApprove={onApprove}
              onOpenImageSetup={onOpenImageSetup}
            />
          ))}
        </div>
      )}
    </section>
  );
}

function CharacterCard({
  character,
  providerStatus,
  voiceCatalogPreviews,
  voiceCatalogPreviewPendingKey,
  onPreviewCatalogVoice,
  busy,
  imageBusy,
  onUpdate,
  onSelectReference,
  onGenerateReference,
  onApprove,
  onOpenImageSetup
}: {
  character: CharacterSummary;
  providerStatus: GenerationProviderStatus;
  voiceCatalogPreviews: Record<string, VoiceCatalogPreviewPlayback>;
  voiceCatalogPreviewPendingKey: string | null;
  onPreviewCatalogVoice?: (voiceCode: string, speakingRate: number) => void;
  busy: boolean;
  imageBusy: boolean;
  onUpdate: (payload: UpdateCharacterPayload) => void;
  onSelectReference: (characterId: string) => void;
  onGenerateReference: (character: CharacterSummary) => void;
  onApprove: (characterId: string) => void;
  onOpenImageSetup: () => void;
}) {
  const voiceOptions = getAvailableVoiceOptions(providerStatus.openAiVoiceOptions);
  const [editing, setEditing] = useState(false);
  const [name, setName] = useState(character.name);
  const [role, setRole] = useState(character.role ?? '');
  const [visualIdentity, setVisualIdentity] = useState(character.visualIdentity);
  const [wardrobe, setWardrobe] = useState(character.wardrobe);
  const [immutableTraits, setImmutableTraits] = useState(character.immutableTraits.join('\n'));
  const [forbiddenChanges, setForbiddenChanges] = useState(character.forbiddenChanges.join('\n'));
  const [voiceCode, setVoiceCode] = useState(character.voiceCode ?? '');
  const [voiceSpeakingRate, setVoiceSpeakingRate] = useState(character.voiceSpeakingRate ?? 1);
  const locked = character.status === 'Approved';
  const valid = name.trim() && visualIdentity.trim() && wardrobe.trim() &&
    parseCharacterRules(immutableTraits).length > 0 && parseCharacterRules(forbiddenChanges).length > 0;

  useEffect(() => {
    setName(character.name);
    setRole(character.role ?? '');
    setVisualIdentity(character.visualIdentity);
    setWardrobe(character.wardrobe);
    setImmutableTraits(character.immutableTraits.join('\n'));
    setForbiddenChanges(character.forbiddenChanges.join('\n'));
    setVoiceCode(character.voiceCode ?? '');
    setVoiceSpeakingRate(character.voiceSpeakingRate ?? 1);
    if (!character.canEdit) setEditing(false);
  }, [character]);

  const save = () => {
    if (!valid || busy) return;
    onUpdate({
      characterId: character.characterId,
      name: name.trim(),
      role: role.trim(),
      visualIdentity: visualIdentity.trim(),
      wardrobe: wardrobe.trim(),
      immutableTraits: parseCharacterRules(immutableTraits),
      forbiddenChanges: parseCharacterRules(forbiddenChanges),
      voiceCode: voiceCode || null,
      voiceSpeakingRate: voiceCode ? voiceSpeakingRate : null
    });
    setEditing(false);
  };

  return (
    <article className={`character-card${locked ? ' locked' : ''}`}>
      <div className="character-reference-pane">
        {character.primaryReference?.previewUrl ? (
          <img src={character.primaryReference.previewUrl} alt={`Ảnh tham chiếu ${character.name}`} />
        ) : (
          <div className="character-reference-placeholder">
            <UserRound size={34} />
            <strong>Chưa có ảnh chuẩn</strong>
            <small>JPEG/PNG · tối đa 10 MB · từ 300×300 px</small>
          </div>
        )}
        <span className={`character-lock-badge ${locked ? 'ready' : 'draft'}`}>
          {locked ? <ShieldCheck size={13} /> : <Clock3 size={13} />}
          {locked ? 'Đã khóa' : 'Chờ thiết lập'}
        </span>
        {character.canEdit && (
          <div className="character-reference-actions">
            <button
              type="button"
              className="character-ai-image"
              disabled={busy || !providerStatus.openAiImageReady}
              onClick={() => onGenerateReference(character)}
            >
              {imageBusy ? <LoaderCircle className="spin" size={15} /> : <WandSparkles size={15} />}
              {imageBusy ? 'Đang tạo ảnh...' : character.primaryReference ? 'Sinh lại ảnh' : 'Tạo ảnh bằng AI'}
            </button>
            <small className="character-image-model">GPT-Image-2 · 1024×1024 · PNG</small>
            {!providerStatus.openAiImageReady && (
              <button type="button" className="character-image-setup" disabled={busy} onClick={onOpenImageSetup}>
                {imageSetupLabel(providerStatus.openAiImageUnavailableCode)}
              </button>
            )}
            <button type="button" className="character-upload" disabled={busy} onClick={() => onSelectReference(character.characterId)}>
              <Upload size={15} /> {character.primaryReference ? 'Thay bằng ảnh khác' : 'Chọn ảnh tham chiếu'}
            </button>
          </div>
        )}
      </div>

      <div className="character-profile-pane">
        <div className="character-title-row">
          <div>
            <span>{character.characterKey} · phiên bản {character.version}</span>
            <h3>{character.name}</h3>
            <p>{character.role || 'Nhân vật chính'} · xuất hiện trong {character.sceneCount} cảnh</p>
          </div>
          {character.canEdit && !editing && (
            <button type="button" className="character-edit" disabled={busy} onClick={() => setEditing(true)}>
              <Pencil size={14} /> Chỉnh hồ sơ
            </button>
          )}
        </div>

        {editing ? (
          <div className="character-edit-form">
            <label>Tên<input maxLength={200} value={name} onChange={(event) => setName(event.target.value)} /></label>
            <label>Vai trò<input maxLength={200} value={role} onChange={(event) => setRole(event.target.value)} /></label>
            <label className="wide">Nhận diện hình ảnh<textarea maxLength={4000} value={visualIdentity} onChange={(event) => setVisualIdentity(event.target.value)} /></label>
            <label className="wide">Trang phục và phụ kiện<textarea maxLength={4000} value={wardrobe} onChange={(event) => setWardrobe(event.target.value)} /></label>
            <label>Đặc điểm cố định<textarea value={immutableTraits} onChange={(event) => setImmutableTraits(event.target.value)} /></label>
            <label>Không được thay đổi<textarea value={forbiddenChanges} onChange={(event) => setForbiddenChanges(event.target.value)} /></label>
            <div className="character-edit-control">
              <span>Giọng nhân vật</span>
              <VoicePickerField
                value={voiceCode}
                options={voiceOptions}
                disabled={busy}
                allowInherited
                previewControls={onPreviewCatalogVoice ? {
                  speakingRate: voiceSpeakingRate,
                  previews: voiceCatalogPreviews,
                  pendingKey: voiceCatalogPreviewPendingKey,
                  onPreview: onPreviewCatalogVoice
                } : undefined}
                onChange={setVoiceCode}
              />
            </div>
            <label>
              Tốc độ giọng
              <select disabled={!voiceCode} value={voiceSpeakingRate} onChange={(event) => setVoiceSpeakingRate(Number(event.target.value))}>
                <option value={0.9}>Chậm · 0,9×</option>
                <option value={1}>Tự nhiên · 1,0×</option>
                <option value={1.1}>Nhanh · 1,1×</option>
              </select>
            </label>
            <div className="character-edit-actions">
              <button type="button" className="scene-cancel" disabled={busy} onClick={() => setEditing(false)}><X size={14} /> Hủy</button>
              <button type="button" className="scene-save" disabled={busy || !valid} onClick={save}><Save size={14} /> Lưu hồ sơ</button>
            </div>
          </div>
        ) : (
          <>
            <div className="character-profile-grid">
              <div><span>Nhận diện cố định</span><p>{character.visualIdentity}</p></div>
              <div><span>Trang phục</span><p>{character.wardrobe}</p></div>
              <div><span>Giọng nhân vật</span><p>{character.voiceCode ? `${voiceDisplayName(character.voiceCode, voiceOptions)} · ${character.voiceSpeakingRate ?? 1}×` : 'Dùng giọng narrator của dự án'}</p></div>
            </div>
            <div className="character-rule-row">
              <div><span>Đặc điểm khóa</span>{character.immutableTraits.map((trait) => <small key={trait}>{trait}</small>)}</div>
              <div><span>Không được thay đổi</span>{character.forbiddenChanges.map((rule) => <small key={rule}>{rule}</small>)}</div>
            </div>
          </>
        )}

        {character.setupMessage && <div className="character-setup-message"><TriangleAlert size={14} /> {character.setupMessage}</div>}
        {!locked && (
          <button type="button" className="character-approve" disabled={busy || !character.canApprove} onClick={() => onApprove(character.characterId)}>
            <LockKeyhole size={15} /> Khóa nhân vật cho các cảnh
          </button>
        )}
      </div>
    </article>
  );
}

function imageSetupLabel(code?: string | null): string {
  if (code === 'pricing_not_configured') return 'Thiếu rate · mở cấu hình pricing';
  if (code === 'organization_budget_exceeded') return 'Thiếu budget · xem cấu hình tổ chức';
  return 'Thiếu credential/model · mở API AI tổ chức';
}

function imageSetupGuidance(code?: string | null): string {
  if (code === 'pricing_not_configured') return 'Global Admin mở Admin Console → Tổ chức & AI → Bảng giá AI → gpt-image-2 và nhập đủ InputToken/OutputToken.';
  if (code === 'organization_budget_exceeded') return 'Owner hoặc BillingManager mở Admin Console → Tổ chức → Ngân sách & sử dụng để tăng budget hoặc hạn mức thành viên.';
  return 'Owner hoặc OrganizationAdmin mở Admin Console → Tổ chức → API AI để cấu hình credential OpenAI; Global Admin kiểm tra model gpt-image-2.';
}

function parseCharacterRules(value: string): string[] {
  return [...new Set(value.split(/[\n,]+/).map((item) => item.trim()).filter(Boolean))].slice(0, 12);
}

function ProjectAssetLibrarySection({
  project,
  library,
  assetType,
  busy,
  onSynchronize,
  onApproveAi,
  onCreate,
  onUpdate,
  onLock,
  onUnlock,
  onDelete
}: {
  project: ProjectDashboard | null;
  library: ProjectAssetLibrary | null;
  assetType: ProjectAssetType;
  busy: boolean;
  onSynchronize: () => void;
  onApproveAi: () => void;
  onCreate: (payload: CreateProjectAssetPayload) => void;
  onUpdate: (payload: UpdateProjectAssetPayload) => void;
  onLock: (asset: ProjectTextAsset) => void;
  onUnlock: (asset: ProjectTextAsset) => void;
  onDelete: (asset: ProjectTextAsset) => void;
}) {
  const [creating, setCreating] = useState(false);
  const [name, setName] = useState('');
  const [canonicalDescription, setCanonicalDescription] = useState('');
  const assets = library?.assets.filter((asset) => asset.assetType === assetType) ?? [];
  const lockedCount = assets.filter((asset) => asset.status === 'Locked').length;
  const aiGeneratedCount = assets.filter((asset) => asset.sourceKind === 'AiGenerated').length;
  const canEdit = Boolean(project && library?.canEdit);
  const assignedIds = new Set(library?.sceneAssignments.flatMap((assignment) => assignment.projectAssetIds) ?? []);
  const aiDraftCount = library?.assets.filter((asset) =>
    asset.sourceKind === 'AiGenerated' && asset.status === 'Draft' && assignedIds.has(asset.projectAssetId)).length ?? 0;
  const invalidSceneCount = library?.sceneAssignments.filter((assignment) => !assignment.isValid).length ?? 0;
  const readySceneCount = library?.sceneAssignments.filter((assignment) =>
    assignment.isValid && !assignment.hasUnlockedAssets).length ?? 0;

  useEffect(() => {
    setCreating(false);
    setName('');
    setCanonicalDescription('');
  }, [assetType, project?.project.projectId]);

  if (!project) {
    return <section className="card project-assets-empty"><Package size={30} /><strong>Hãy chọn hoặc tạo dự án trước</strong><p>Thư viện text được lưu riêng cho từng dự án video dài.</p></section>;
  }

  const submitCreate = () => {
    if (!canEdit || busy || !name.trim() || !canonicalDescription.trim()) return;
    onCreate({
      assetType,
      name: name.trim(),
      canonicalDescription: canonicalDescription.trim()
    });
    setCreating(false);
    setName('');
    setCanonicalDescription('');
  };

  return (
    <section className="card project-assets-section">
      <header className="project-assets-header">
        <div>
          <span className="generation-eyebrow">KIỂM TRA TÍNH NHẤT QUÁN · {assetTypeLabel(assetType).toUpperCase()}</span>
          <h2>Kiểm tra mô tả trước khi tạo clip</h2>
          <p>Mỗi cảnh có tài sản dùng đúng một bối cảnh. Đạo cụ và item là tùy chọn; cảnh không cần tài sản vẫn có thể để trống.</p>
        </div>
        <div className="project-assets-header-actions">
          <span>{lockedCount}/{assets.length} đã khóa · {aiGeneratedCount} AI</span>
          {canEdit && aiDraftCount > 0 && <button type="button" disabled={busy || invalidSceneCount > 0} onClick={onApproveAi}><ShieldCheck size={15} /> Duyệt & khóa {aiDraftCount} tài sản AI</button>}
          {canEdit && <button type="button" disabled={busy} onClick={() => setCreating(true)}><Plus size={15} /> Tạo {assetTypeLabel(assetType).toLowerCase()}</button>}
        </div>
      </header>

      <div className={`project-assets-readiness ${invalidSceneCount > 0 ? 'blocked' : 'ready'}`}>
        <div><strong>{readySceneCount}/{project.scenes.length} cảnh sẵn sàng về tài sản</strong><span>{invalidSceneCount > 0 ? `${invalidSceneCount} cảnh cần sửa lựa chọn` : aiDraftCount > 0 ? `${aiDraftCount} đề xuất AI đang chờ duyệt` : 'Các lựa chọn hiện tại hợp lệ'}</span></div>
        {invalidSceneCount > 0 && <small>Vào bước Storyboard để sửa cảnh đang chọn thiếu hoặc thừa bối cảnh.</small>}
      </div>

      {canEdit && project.scenes.length > 0 && (
        <details className="project-assets-advanced">
          <summary>Tùy chọn nâng cao</summary>
          <p>Chỉ dùng khi cần khôi phục lại đề xuất từ content plan đã lưu. Dữ liệu khóa hiện có vẫn được giữ nguyên.</p>
          <button type="button" className="secondary" disabled={busy} onClick={onSynchronize}><RefreshCw size={15} /> Khôi phục đề xuất AI</button>
        </details>
      )}

      {creating && (
        <div className="project-asset-create-form">
          <label>Tên {assetTypeLabel(assetType).toLowerCase()}<input autoFocus maxLength={160} value={name} onChange={(event) => setName(event.target.value)} placeholder={assetNamePlaceholder(assetType)} /></label>
          <label>Mô tả chuẩn<textarea maxLength={2000} value={canonicalDescription} onChange={(event) => setCanonicalDescription(event.target.value)} placeholder={assetDescriptionPlaceholder(assetType)} /></label>
          <div><small>{canonicalDescription.length}/2000 ký tự</small><button type="button" className="scene-cancel" disabled={busy} onClick={() => setCreating(false)}><X size={14} /> Hủy</button><button type="button" className="scene-save" disabled={busy || !name.trim() || !canonicalDescription.trim()} onClick={submitCreate}><Save size={14} /> Lưu nháp</button></div>
        </div>
      )}

      {assets.length > 0 ? (
        <div className="project-assets-list">
          {assets.map((asset) => (
            <ProjectAssetCard
              key={asset.projectAssetId}
              asset={asset}
              sceneNumbers={asset.sceneIds
                .map((sceneId) => project.scenes.find((scene) => scene.sceneId === sceneId)?.sequenceNumber)
                .filter((sequence): sequence is number => sequence !== undefined)
                .sort((left, right) => left - right)}
              busy={busy}
              canEdit={canEdit}
              onUpdate={onUpdate}
              onLock={onLock}
              onUnlock={onUnlock}
              onDelete={onDelete}
            />
          ))}
        </div>
      ) : !creating && (
        <div className="project-assets-empty inline"><Package size={27} /><strong>Chưa có {assetTypeLabel(assetType).toLowerCase()}</strong><p>Tạo hồ sơ text, kiểm tra mô tả rồi khóa trước khi gắn vào cảnh.</p></div>
      )}
      {!canEdit && library && <div className="project-assets-view-only"><ShieldCheck size={15} /> Vai trò hiện tại chỉ được xem thư viện tài sản.</div>}
    </section>
  );
}

function ProjectAssetCard({
  asset,
  sceneNumbers,
  busy,
  canEdit,
  onUpdate,
  onLock,
  onUnlock,
  onDelete
}: {
  asset: ProjectTextAsset;
  sceneNumbers: number[];
  busy: boolean;
  canEdit: boolean;
  onUpdate: (payload: UpdateProjectAssetPayload) => void;
  onLock: (asset: ProjectTextAsset) => void;
  onUnlock: (asset: ProjectTextAsset) => void;
  onDelete: (asset: ProjectTextAsset) => void;
}) {
  const [editing, setEditing] = useState(false);
  const [name, setName] = useState(asset.name);
  const [canonicalDescription, setCanonicalDescription] = useState(asset.canonicalDescription);
  const locked = asset.status === 'Locked';

  useEffect(() => {
    setName(asset.name);
    setCanonicalDescription(asset.canonicalDescription);
    setEditing(false);
  }, [asset.concurrencyToken]);

  const save = () => {
    if (busy || !name.trim() || !canonicalDescription.trim()) return;
    onUpdate({
      projectAssetId: asset.projectAssetId,
      assetType: asset.assetType,
      name: name.trim(),
      canonicalDescription: canonicalDescription.trim(),
      concurrencyToken: asset.concurrencyToken
    });
  };

  return (
    <article className={`project-asset-card ${locked ? 'locked' : 'draft'}`}>
      <header>
        <div className="project-asset-title"><span className="project-asset-icon">{asset.assetType === 'Background' ? <MapPin size={17} /> : asset.assetType === 'Prop' ? <Package size={17} /> : <Database size={17} />}</span><div><small>{assetTypeLabel(asset.assetType)} · {locked ? `phiên bản ${asset.currentVersion}` : 'bản nháp'} {asset.sourceKind === 'AiGenerated' && <em>AI đề xuất</em>}</small><h3>{asset.name}</h3></div></div>
        <span className={`project-asset-status ${locked ? 'locked' : 'draft'}`}>{locked ? <LockKeyhole size={13} /> : <Pencil size={13} />}{locked ? 'Đã khóa' : 'Chưa khóa'}</span>
      </header>

      {editing ? (
        <div className="project-asset-edit-form">
          <label>Tên<input maxLength={160} value={name} onChange={(event) => setName(event.target.value)} /></label>
          <label>Mô tả chuẩn<textarea maxLength={2000} value={canonicalDescription} onChange={(event) => setCanonicalDescription(event.target.value)} /></label>
          <div><small>{canonicalDescription.length}/2000 ký tự</small><button type="button" className="scene-cancel" disabled={busy} onClick={() => setEditing(false)}><X size={14} /> Hủy</button><button type="button" className="scene-save" disabled={busy || !name.trim() || !canonicalDescription.trim()} onClick={save}><Save size={14} /> Lưu thay đổi</button></div>
        </div>
      ) : (
        <p className="project-asset-description">{asset.canonicalDescription}</p>
      )}

      {sceneNumbers.length > 0 && <div className="project-asset-scenes"><span>Áp dụng:</span>{sceneNumbers.map((sequence) => <small key={sequence}>Cảnh {sequence}</small>)}</div>}

      <footer>
        <span className={asset.sceneIds.length > 0 ? 'in-use' : ''}><Link2 size={13} /> {asset.sceneIds.length > 0 ? `Đang dùng trong ${asset.sceneIds.length} cảnh` : 'Chưa gắn vào cảnh'}</span>
        {canEdit && !editing && <div className="project-asset-actions">
          {!locked && <button type="button" disabled={busy} onClick={() => setEditing(true)}><Pencil size={14} /> Chỉnh sửa</button>}
          {!locked && asset.currentVersion === 0 && asset.sceneIds.length === 0 && <button type="button" className="danger" disabled={busy} onClick={() => onDelete(asset)}><Trash2 size={14} /> Xóa</button>}
          {locked ? <button type="button" disabled={busy} onClick={() => onUnlock(asset)}><UnlockKeyhole size={14} /> Mở khóa</button> : <button type="button" className="lock" disabled={busy || !asset.canonicalDescription.trim()} onClick={() => onLock(asset)}><LockKeyhole size={14} /> Khóa text</button>}
        </div>}
      </footer>
    </article>
  );
}

function assetTypeLabel(assetType: ProjectAssetType): string {
  if (assetType === 'Background') return 'Bối cảnh';
  if (assetType === 'Prop') return 'Đạo cụ';
  return 'Item';
}

function assetNamePlaceholder(assetType: ProjectAssetType): string {
  if (assetType === 'Background') return 'Ví dụ: Căn bếp nhà Minh';
  if (assetType === 'Prop') return 'Ví dụ: Chiếc máy ảnh cổ';
  return 'Ví dụ: Chiếc cốc đỏ';
}

function assetDescriptionPlaceholder(assetType: ProjectAssetType): string {
  if (assetType === 'Background') return 'Kiến trúc, màu sắc, ánh sáng, vị trí cửa sổ và các chi tiết không được thay đổi...';
  if (assetType === 'Prop') return 'Hình dáng, chất liệu, màu sắc, kích thước và trạng thái cố định của đạo cụ...';
  return 'Đặc điểm nhận diện, màu sắc, vật liệu và chi tiết cố định của item...';
}

function countAssets(library: ProjectAssetLibrary | null, assetType: ProjectAssetType): number {
  return library?.assets.filter((asset) => asset.assetType === assetType).length ?? 0;
}

function StoryboardSection({
  project,
  assetLibrary,
  sceneFirstFrames = [],
  firstFrameOperation = null,
  providerStatus,
  mediaTools,
  busy,
  onGenerateVideo,
  onRequestSceneFirstFrame = () => undefined,
  onApproveSceneFirstFrame = () => undefined,
  onRejectSceneFirstFrame = () => undefined,
  onRetrySceneFirstFrameDownload = () => undefined,
  onPreviewSceneFirstFrame = () => undefined,
  onApproveNativeAudio,
  onUnapproveAudio,
  onVerifySceneSpeech,
  onInstallMediaTools,
  onCheckMediaTools,
  onUpdateScene,
  sceneSaveState,
  onClearSaveFailure,
  onUpdateSceneAssets,
  onConfirmSceneAssets,
  assetConfirmBusyId
}: {
  project: ProjectDashboard | null;
  assetLibrary: ProjectAssetLibrary | null;
  sceneFirstFrames?: SceneFirstFrameSummary[];
  firstFrameOperation?: SceneFirstFrameOperation | null;
  providerStatus: GenerationProviderStatus;
  mediaTools: MediaToolStatus;
  busy: boolean;
  onGenerateVideo: (sceneIds: string[]) => void;
  onRequestSceneFirstFrame?: (scene: SceneSummary, regenerate: boolean) => void;
  onApproveSceneFirstFrame?: (frame: SceneFirstFrameSummary) => void;
  onRejectSceneFirstFrame?: (frame: SceneFirstFrameSummary) => void;
  onRetrySceneFirstFrameDownload?: (frame: SceneFirstFrameSummary) => void;
  onPreviewSceneFirstFrame?: (frame: SceneFirstFrameSummary) => void;
  onApproveNativeAudio: (sceneId: string, playbackConfirmed: boolean, speechReviewReason?: string) => void;
  onUnapproveAudio: (sceneId: string) => void;
  onVerifySceneSpeech: (scene: SceneSummary) => void;
  onInstallMediaTools: () => void;
  onCheckMediaTools: () => void;
  onUpdateScene: (payload: UpdateScenePayload) => void;
  sceneSaveState: SceneSaveState | null;
  onClearSaveFailure: (sceneId: string) => void;
  onUpdateSceneAssets: (sceneId: string, projectAssetIds: string[]) => void;
  onConfirmSceneAssets: (sceneId: string) => void;
  assetConfirmBusyId: string | null;
}) {
  const [selectedSceneIds, setSelectedSceneIds] = useState<Set<string>>(new Set());
  const [filter, setFilter] = useState<StoryboardFilter>('all');
  const selectionProjectId = useRef('');
  const scenes = project?.scenes ?? [];
  const isVeo = project?.videoProviderCode?.toLowerCase() === 'fal';
  const canonicalWorkflowReady = project?.speechProductionPolicy !== 'CanonicalVoice' ||
    providerStatus.canonicalVoiceReady === true;
  const hasReadyFirstFrame = (sceneId: string) => !isVeo || sceneFirstFrames.some((frame) =>
    frame.sceneId === sceneId && frame.status === 'Approved' && frame.isCurrent && Boolean(frame.previewUrl));
  const filteredScenes = scenes.filter((scene) => matchesStoryboardFilter(scene, filter));
  const selectableScenes = scenes.filter(
    (scene) => canQueueScene(scene) && hasReadyFirstFrame(scene.sceneId) &&
      (sceneNeedsLocalCompletion(scene) || areSceneAssetsReady(scene.sceneId, assetLibrary))
  );
  const visibleSelectableScenes = filteredScenes.filter(
    (scene) => canQueueScene(scene) && hasReadyFirstFrame(scene.sceneId) &&
      (sceneNeedsLocalCompletion(scene) || areSceneAssetsReady(scene.sceneId, assetLibrary))
  );
  const selectableKey = selectableScenes.map((scene) => `${scene.sceneId}:${scene.status}:${hasReadyFirstFrame(scene.sceneId)}`).join('|');
  const enforceKlingLongFormSpeechPolicy = project?.workflowStructureType === 'OpenAiStructuredPlan' &&
    ['kling', 'fal'].includes(project?.videoProviderCode?.toLowerCase() ?? '');
  const speechVerificationRequired = project?.workflowStructureType !== 'OpenAiStructuredPlan';

  useEffect(() => {
    if (!project) {
      selectionProjectId.current = '';
      setSelectedSceneIds(new Set());
      return;
    }

    const allowedIds = new Set(selectableScenes.map((scene) => scene.sceneId));
    if (selectionProjectId.current !== project.project.projectId) {
      selectionProjectId.current = project.project.projectId;
      setSelectedSceneIds(allowedIds);
      return;
    }

    setSelectedSceneIds((current) => new Set([...current].filter((sceneId) => allowedIds.has(sceneId))));
  }, [project?.project.projectId, selectableKey]);

  if (!project || scenes.length === 0) return null;

  const selectedScenes = selectableScenes.filter((scene) => selectedSceneIds.has(scene.sceneId));
  const selectedIds = selectedScenes.map((scene) => scene.sceneId);
  const actionSummary = getStoryboardActionSummary(selectedScenes, project.speechProductionPolicy);
  const downloadOnlySelection = actionSummary.downloadCount > 0 && actionSummary.downloadCount === selectedScenes.length;
  const selectionActionLabel = actionSummary.label;
  const canonicalSpeechScenes = scenes.filter((scene) =>
    isCanonicalSpeechScene(scene, project.speechProductionPolicy));
  const canonicalNeedWavCount = canonicalSpeechScenes.filter((scene) =>
    !scene.hasCanonicalVoicePreview &&
    scene.speechStatus !== 'SpeechApproved' &&
    scene.speechStatus !== 'SpeechReadyForLipSync').length;
  const canonicalNeedVoiceReviewCount = canonicalSpeechScenes.filter((scene) =>
    scene.speechMode === 'OnCameraDialogue' &&
    scene.hasCanonicalVoicePreview &&
    !scene.preview?.url &&
    scene.speechStatus !== 'SpeechApproved' &&
    scene.speechStatus !== 'SpeechReadyForLipSync').length;
  const canonicalReadyForVideoCount = canonicalSpeechScenes.filter((scene) =>
    scene.speechMode === 'NativeVoiceOver' &&
    scene.hasCanonicalVoicePreview &&
    !scene.preview?.url).length;
  const canonicalVideoReviewCount = canonicalSpeechScenes.filter((scene) =>
    Boolean(scene.preview?.url) && !isSceneCompleted(scene)).length;
  const completedScenes = scenes.filter(isSceneCompleted).length;
  const totalDurationSeconds = Math.ceil(scenes.reduce((total, scene) => total + scene.durationMs, 0) / 1000);
  const allSelected = visibleSelectableScenes.length > 0 && visibleSelectableScenes.every(
    (scene) => selectedSceneIds.has(scene.sceneId)
  );

  const selectFilter = (nextFilter: StoryboardFilter) => {
    setFilter(nextFilter);
    const nextVisibleIds = scenes
      .filter((scene) => matchesStoryboardFilter(scene, nextFilter) && canQueueScene(scene) && hasReadyFirstFrame(scene.sceneId))
      .map((scene) => scene.sceneId);
    setSelectedSceneIds(new Set(nextVisibleIds));
  };

  const toggleScene = (sceneId: string) => {
    setSelectedSceneIds((current) => {
      const next = new Set(current);
      if (next.has(sceneId)) next.delete(sceneId);
      else next.add(sceneId);
      return next;
    });
  };

  return (
    <section className="card storyboard-section">
      <header className="storyboard-header">
        <div>
          <span className="generation-eyebrow">STORYBOARD</span>
          <h2>Nội dung và hình ảnh từng cảnh</h2>
          <p>
            {scenes.length} cảnh · {formatDuration(totalDurationSeconds)} · Đã hoàn thành {completedScenes}/{scenes.length} clip
          </p>
        </div>
        <div className="storyboard-toolbar">
          <button
            type="button"
            className="storyboard-select-all"
            disabled={busy || visibleSelectableScenes.length === 0}
            onClick={() => setSelectedSceneIds(allSelected ? new Set() : new Set(visibleSelectableScenes.map((scene) => scene.sceneId)))}
          >
            {allSelected ? 'Bỏ chọn tất cả' : 'Chọn cảnh cần xử lý'}
          </button>
          <button
            type="button"
            className="storyboard-generate"
            disabled={busy || !providerStatus.videoReady || !canonicalWorkflowReady || !mediaTools.ready || selectedIds.length === 0}
            onClick={() => onGenerateVideo(selectedIds)}
          >
            {busy
              ? <LoaderCircle className="spin" size={17} />
              : downloadOnlySelection
                ? <Download size={17} />
                : actionSummary.voicePreparationCount === selectedScenes.length
                  ? <Volume2 size={17} />
                  : <Film size={17} />}
            {selectionActionLabel}
          </button>
        </div>
      </header>

      {!providerStatus.videoReady && (
        <div className="storyboard-warning">
          <TriangleAlert size={16} /> {providerStatus.videoUnavailableMessage || 'Provider video chưa sẵn sàng. Bạn vẫn có thể xem và chỉnh nội dung cảnh trước khi quản trị viên hoàn tất cấu hình.'}
        </div>
      )}

      {!canonicalWorkflowReady && (
        <div className="storyboard-warning">
          <TriangleAlert size={16} /> {providerStatus.canonicalVoiceUnavailableMessage || 'Canonical Voice chưa sẵn sàng trên server.'}
        </div>
      )}

      {!mediaTools.ready && (
        <div className="storyboard-warning media-tool-warning">
          <TriangleAlert size={16} />
          <div>
            <strong>Chưa thể tải và kiểm tra clip video</strong>
            <span>{mediaTools.message}</span>
          </div>
          <div className="media-tool-actions">
            <button className="media-tool-install" type="button" disabled={busy} onClick={onInstallMediaTools}>
              <Download size={14} /> Cài bộ xử lý video
            </button>
            <button type="button" disabled={busy} onClick={onCheckMediaTools}>
              <RefreshCw size={14} /> Kiểm tra lại
            </button>
          </div>
        </div>
      )}

      {assetLibrary?.sceneAssignments.some((assignment) => assignment.hasUnlockedAssets) && (
        <div className="storyboard-warning asset-lock-warning">
          <CircleHelp size={16} /> Một số cảnh đang chờ xác nhận tài sản. Bạn có thể xác nhận trực tiếp tại từng cảnh mà không phát sinh chi phí AI.
        </div>
      )}

      {assetLibrary?.sceneAssignments.some((assignment) => !assignment.isValid) && (
        <div className="storyboard-warning asset-selection-warning">
          <TriangleAlert size={16} /> Có cảnh đang chọn tài sản không hợp lệ. Bấm “Sửa lựa chọn” tại cảnh được cảnh báo trước khi tạo clip.
        </div>
      )}

      {canonicalSpeechScenes.length > 0 && (
        <div className="storyboard-workflow-summary" role="status">
          <div>
            <strong><Volume2 size={15} /> Lộ trình Canonical Voice</strong>
            <span>Với lời dẫn ngoài khung hình, WAV qua kiểm tra kỹ thuật sẽ được dùng thẳng khi tạo video nền, không cần duyệt WAV riêng.</span>
          </div>
          <ul aria-label="Tổng hợp trạng thái Canonical Voice">
            <li><b>{canonicalNeedWavCount}</b> cần tạo WAV</li>
            <li><b>{canonicalNeedVoiceReviewCount}</b> chờ duyệt WAV thoại trực diện</li>
            <li><b>{canonicalReadyForVideoCount}</b> sẵn sàng tạo video nền</li>
            <li><b>{canonicalVideoReviewCount}</b> chờ duyệt video</li>
          </ul>
        </div>
      )}

      <div className="storyboard-filters" aria-label="Lọc cảnh theo trạng thái">
        {storyboardFilters.map((item) => {
          const count = scenes.filter((scene) => matchesStoryboardFilter(scene, item.id)).length;
          return <button type="button" key={item.id} className={filter === item.id ? 'active' : ''} onClick={() => selectFilter(item.id)}>
            {item.label}<span>{count}</span>
          </button>;
        })}
      </div>

      <div className="storyboard-list">
        {filteredScenes.map((scene) => (
          <SceneCard
            key={scene.sceneId}
            scene={scene}
            assetLibrary={assetLibrary}
            firstFrames={sceneFirstFrames.filter((frame) => frame.sceneId === scene.sceneId)}
            firstFrameOperation={firstFrameOperation?.sceneId === scene.sceneId ? firstFrameOperation : null}
            isVeo={isVeo}
            selected={selectedSceneIds.has(scene.sceneId)}
            busy={busy}
            videoReady={providerStatus.videoReady}
            mediaToolsReady={mediaTools.ready}
            speechProductionPolicy={project.speechProductionPolicy}
            enforceKlingLongFormSpeechPolicy={enforceKlingLongFormSpeechPolicy}
            speechVerificationRequired={speechVerificationRequired}
            onToggle={() => toggleScene(scene.sceneId)}
            onGenerate={() => onGenerateVideo([scene.sceneId])}
            onRequestFirstFrame={(regenerate) => onRequestSceneFirstFrame(scene, regenerate)}
            onApproveFirstFrame={onApproveSceneFirstFrame}
            onRejectFirstFrame={onRejectSceneFirstFrame}
            onRetryFirstFrameDownload={onRetrySceneFirstFrameDownload}
            onPreviewFirstFrame={onPreviewSceneFirstFrame}
            onApproveNativeAudio={(playbackConfirmed, speechReviewReason) => onApproveNativeAudio(scene.sceneId, playbackConfirmed, speechReviewReason)}
            onUnapproveAudio={() => onUnapproveAudio(scene.sceneId)}
            onVerifySceneSpeech={() => onVerifySceneSpeech(scene)}
            onUpdate={onUpdateScene}
            saveState={sceneSaveState?.sceneId === scene.sceneId ? sceneSaveState : null}
            onClearSaveFailure={() => onClearSaveFailure(scene.sceneId)}
            onUpdateAssets={(projectAssetIds) => onUpdateSceneAssets(scene.sceneId, projectAssetIds)}
            onConfirmAssets={() => onConfirmSceneAssets(scene.sceneId)}
            confirmingAssets={assetConfirmBusyId === scene.sceneId}
          />
        ))}
        {filteredScenes.length === 0 && <div className="storyboard-filter-empty"><LayoutGrid size={27} /><span>Không có cảnh thuộc trạng thái này.</span></div>}
      </div>
    </section>
  );
}

function SceneCard({
  scene,
  assetLibrary,
  firstFrames,
  firstFrameOperation,
  isVeo,
  selected,
  busy,
  videoReady,
  mediaToolsReady,
  speechProductionPolicy,
  enforceKlingLongFormSpeechPolicy,
  speechVerificationRequired,
  onToggle,
  onGenerate,
  onRequestFirstFrame,
  onApproveFirstFrame,
  onRejectFirstFrame,
  onRetryFirstFrameDownload,
  onPreviewFirstFrame,
  onApproveNativeAudio,
  onUnapproveAudio,
  onVerifySceneSpeech,
  onUpdate,
  saveState,
  onClearSaveFailure,
  onUpdateAssets,
  onConfirmAssets,
  confirmingAssets
}: {
  scene: SceneSummary;
  assetLibrary: ProjectAssetLibrary | null;
  firstFrames: SceneFirstFrameSummary[];
  firstFrameOperation: SceneFirstFrameOperation | null;
  isVeo: boolean;
  selected: boolean;
  busy: boolean;
  videoReady: boolean;
  mediaToolsReady: boolean;
  speechProductionPolicy: string;
  enforceKlingLongFormSpeechPolicy: boolean;
  speechVerificationRequired: boolean;
  onToggle: () => void;
  onGenerate: () => void;
  onRequestFirstFrame: (regenerate: boolean) => void;
  onApproveFirstFrame: (frame: SceneFirstFrameSummary) => void;
  onRejectFirstFrame: (frame: SceneFirstFrameSummary) => void;
  onRetryFirstFrameDownload: (frame: SceneFirstFrameSummary) => void;
  onPreviewFirstFrame: (frame: SceneFirstFrameSummary) => void;
  onApproveNativeAudio: (playbackConfirmed: boolean, speechReviewReason?: string) => void;
  onUnapproveAudio: () => void;
  onVerifySceneSpeech: () => void;
  onUpdate: (payload: UpdateScenePayload) => void;
  saveState: SceneSaveState | null;
  onClearSaveFailure: () => void;
  onUpdateAssets: (projectAssetIds: string[]) => void;
  onConfirmAssets: () => void;
  confirmingAssets: boolean;
}) {
  const [editing, setEditing] = useState(false);
  const [speechMode, setSpeechMode] = useState<UpdateScenePayload['speechMode']>(scene.speechMode);
  const [narration, setNarration] = useState(scene.narration ?? '');
  const [voiceStyle, setVoiceStyle] = useState(scene.voiceStyle ?? '');
  const [ambientAudio, setAmbientAudio] = useState(scene.ambientAudio ?? '');
  const [soundEffects, setSoundEffects] = useState(scene.soundEffects ?? '');
  const [visualDescription, setVisualDescription] = useState(scene.visualDescription);
  const [prompt, setPrompt] = useState(scene.prompt);
  const [videoPlaybackConfirmed, setVideoPlaybackConfirmed] = useState(false);
  const [canonicalVoicePlaybackConfirmed, setCanonicalVoicePlaybackConfirmed] = useState(false);
  const [speechReviewReason, setSpeechReviewReason] = useState('');
  const [speechContentConfirmed, setSpeechContentConfirmed] = useState(false);
  const [speakerConfirmed, setSpeakerConfirmed] = useState(false);
  const [lipSyncConfirmed, setLipSyncConfirmed] = useState(false);
  const [assigningAssets, setAssigningAssets] = useState(false);
  const [assignmentSaving, setAssignmentSaving] = useState(false);
  const assignedAssetIds = sceneAssignedAssetIds(scene.sceneId, assetLibrary);
  const [draftAssetIds, setDraftAssetIds] = useState<Set<string>>(new Set(assignedAssetIds));
  const assetAssignment = assetLibrary?.sceneAssignments.find((assignment) => assignment.sceneId === scene.sceneId);
  const assignedAssets = assetLibrary?.assets.filter((asset) => assignedAssetIds.includes(asset.projectAssetId)) ?? [];
  const sceneAssetBlocker = getSceneFirstFrameAssetBlocker(scene.sceneId, assetLibrary);
  const assignedAssetsReady = sceneAssetBlocker === null;
  const assetUiState = assetAssignment?.isValid === false
    ? 'invalid'
    : assignedAssetsReady
      ? 'ready'
      : 'pending';
  const firstFrameAssetBlocker = isVeo ? sceneAssetBlocker : null;
  const draftAssets = assetLibrary?.assets.filter((asset) => draftAssetIds.has(asset.projectAssetId)) ?? [];
  const draftBackgroundCount = draftAssets.filter((asset) => asset.assetType === 'Background').length;
  const draftAssetsValid = draftAssetIds.size === 0 || draftBackgroundCount === 1;
  const assetsChanged = assignedAssetIds.length !== draftAssetIds.size || assignedAssetIds.some((id) => !draftAssetIds.has(id));
  const status = sceneStatus(scene);
  const latestFirstFrame = [...firstFrames].sort((left, right) => right.version - left.version)[0] ?? null;
  const approvedFirstFrame = firstFrames.find((frame) => frame.status === 'Approved' && frame.isCurrent) ?? null;
  const firstFrameReady = !isVeo || Boolean(approvedFirstFrame?.previewUrl);
  const selectable = canQueueScene(scene) && firstFrameReady && (sceneNeedsLocalCompletion(scene) || assignedAssetsReady);
  const canonicalVoiceOnlyReview = Boolean(scene.hasCanonicalVoicePreview && !scene.preview?.url);
  const requiredPlaybackConfirmed = canonicalVoiceOnlyReview
    ? canonicalVoicePlaybackConfirmed
    : videoPlaybackConfirmed;
  const canonicalVoiceWorkflow = isCanonicalSpeechScene(scene, speechProductionPolicy);
  const canonicalNarrationReadyForVideo = canonicalVoiceWorkflow &&
    scene.speechMode === 'NativeVoiceOver' &&
    Boolean(scene.canonicalVoicePreview?.url) &&
    !scene.preview?.url;
  const canonicalVoiceJourney = canonicalVoiceWorkflow
    ? getCanonicalVoiceJourney(scene, requiredPlaybackConfirmed)
    : null;
  const reviewControlsReady = !canonicalVoiceOnlyReview || Boolean(scene.canonicalVoicePreview?.url);
  const speechAudioDurationMs = scene.hasCanonicalVoicePreview
    ? scene.canonicalVoicePreview?.durationMs
    : scene.preview?.durationMs;
  const audioCanBeUnapproved = isSceneCompleted(scene) ||
    scene.speechStatus === 'SpeechReadyForLipSync' ||
    (scene.speechStatus === 'SpeechApproved' && Boolean(scene.hasCanonicalVoicePreview));
  const validSpeech = speechMode === 'None'
    ? narration.trim().length === 0
    : narration.trim().length > 0 &&
      (speechMode !== 'OnCameraDialogue' || scene.characters.length === 1) &&
      (!enforceKlingLongFormSpeechPolicy || speechMode !== 'NativeVoiceOver' || scene.characters.length === 0);
  const validDraft = visualDescription.trim().length > 0 && prompt.trim().length > 0 && validSpeech;
  const displayTitle = sceneDisplayTitle(scene);
  const isSaving = saveState?.status === 'saving';
  const saveBlocker = busy
    ? isSaving
      ? 'Đang lưu cảnh. Vui lòng chờ xác nhận từ desktop.'
      : 'Không thể lưu khi một thao tác khác đang chạy.'
    : visualDescription.trim().length === 0
      ? 'Hãy nhập mô tả hình ảnh cho cảnh.'
      : prompt.trim().length === 0
        ? 'Hãy nhập prompt hình ảnh cho cảnh.'
        : speechMode !== 'None' && narration.trim().length === 0
          ? 'Hãy nhập lời provider cần nói hoặc chuyển cảnh sang không có lời nói.'
          : speechMode === 'OnCameraDialogue' && scene.characters.length !== 1
            ? 'Lời thoại trực diện cần đúng một nhân vật trong cảnh.'
            : enforceKlingLongFormSpeechPolicy && speechMode === 'NativeVoiceOver' && scene.characters.length !== 0
              ? 'Lời dẫn ngoài khung hình chỉ dùng cho cảnh B-roll không có nhân vật.'
              : null;
  const reviewChecklistComplete = scene.speechMode === 'None' ||
    (speechContentConfirmed && speakerConfirmed && lipSyncConfirmed);
  const audioReviewBlocker = !scene.requiresAudioReview
    ? null
    : speechVerificationRequired &&
        !canonicalVoiceOnlyReview &&
        scene.speechVerification?.status === 'NeedsReview' &&
            !scene.speechVerification.reviewApproved &&
            speechReviewReason.trim().length < 10
          ? 'ASR cần xem lại: hãy nghe WAV và nhập lý do chấp nhận tối thiểu 10 ký tự.'
          : !scene.canApproveNativeAudio
            ? canonicalVoiceOnlyReview
              ? 'Canonical WAV hiện hành chưa vượt qua kiểm tra kỹ thuật hoặc không còn khớp phiên bản cảnh.'
              : speechVerificationRequired
                ? 'Audio hiện hành chưa đủ điều kiện duyệt. Hãy hoàn tất bước kiểm tra transcript.'
                : 'Audio hiện hành chưa vượt qua kiểm tra kỹ thuật để nghe duyệt.'
            : !requiredPlaybackConfirmed
              ? canonicalVoiceOnlyReview
                ? 'Hãy phát Canonical WAV ít nhất một lần.'
                : 'Hãy phát video ít nhất một lần để kiểm tra hình và tiếng.'
              : !reviewChecklistComplete
                ? 'Hãy xác nhận đầy đủ checklist nghe duyệt.'
                : null;

  useEffect(() => {
    setVideoPlaybackConfirmed(false);
    setCanonicalVoicePlaybackConfirmed(false);
    setSpeechContentConfirmed(false);
    setSpeakerConfirmed(false);
    setLipSyncConfirmed(false);
    setSpeechReviewReason('');
  }, [scene.sceneId, scene.preview?.url, scene.canonicalVoicePreview?.url, scene.speechVerification?.speechVerificationReportId]);
  useEffect(() => setDraftAssetIds(new Set(assignedAssetIds)), [scene.sceneId, assignedAssetIds.join('|')]);
  useEffect(() => {
    if (!assignmentSaving || busy) return;
    const saved = assignedAssetIds.length === draftAssetIds.size && assignedAssetIds.every((id) => draftAssetIds.has(id));
    setAssignmentSaving(false);
    if (saved) setAssigningAssets(false);
  }, [assignmentSaving, busy, assignedAssetIds.join('|')]);
  useEffect(() => {
    if (saveState?.status === 'succeeded') setEditing(false);
  }, [saveState?.status]);

  const beginEdit = () => {
    onClearSaveFailure();
    setSpeechMode(scene.speechMode);
    setNarration(scene.narration ?? '');
    setVoiceStyle(scene.voiceStyle ?? '');
    setAmbientAudio(scene.ambientAudio ?? '');
    setSoundEffects(scene.soundEffects ?? '');
    setVisualDescription(scene.visualDescription);
    setPrompt(scene.prompt);
    setEditing(true);
  };

  const save = () => {
    if (!validDraft || busy) return;
    onUpdate({
      sceneId: scene.sceneId,
      narration: narration.trim(),
      visualDescription: visualDescription.trim(),
      prompt: prompt.trim(),
      speechMode,
      voiceStyle: voiceStyle.trim() || null,
      ambientAudio: ambientAudio.trim() || null,
      soundEffects: soundEffects.trim() || null
    });
  };

  const toggleAsset = (asset: ProjectTextAsset) => {
    setDraftAssetIds((current) => {
      const next = new Set(current);
      if (asset.assetType === 'Background') {
        assetLibrary?.assets
          .filter((candidate) => candidate.assetType === 'Background')
          .forEach((candidate) => next.delete(candidate.projectAssetId));
        next.add(asset.projectAssetId);
      } else if (next.has(asset.projectAssetId)) next.delete(asset.projectAssetId);
      else next.add(asset.projectAssetId);
      return next;
    });
  };

  return (
    <article className={`scene-card scene-${status.tone}${selected ? ' selected' : ''}`}>
      <header className="scene-heading">
        <div className="scene-heading-copy">
          <span>CẢNH {String(scene.sequenceNumber).padStart(2, '0')}</span>
          <h3>{displayTitle}</h3>
        </div>
        <div className="scene-heading-meta">
          <span className={`scene-status scene-status-${status.tone}`}>{status.label}</span>
          <div className="scene-time"><Clock3 size={14} /> {formatTimeline(scene.timelineStartMs)}–{formatTimeline(scene.timelineEndMs)} · {Math.ceil(scene.durationMs / 1000)}s</div>
          {selectable && (
            <label
              className="scene-selector"
              title={needsCanonicalVoicePreparation(scene, speechProductionPolicy)
                ? 'Chọn cảnh để chuẩn bị Canonical WAV'
                : 'Chọn cảnh để tạo video'}
            >
              <input type="checkbox" checked={selected} disabled={busy} onChange={onToggle} />
              <span><Check size={12} /></span>
            </label>
          )}
        </div>
      </header>

      <div className="scene-card-body">
        <div className="scene-media-column">
          <div className="scene-media">
            {scene.preview?.url ? (
              <video
                src={scene.preview.url}
                controls
                preload="metadata"
                aria-label={`Video cảnh ${scene.sequenceNumber}`}
                onPlay={() => setVideoPlaybackConfirmed(true)}
              />
            ) : (
              <div className="scene-placeholder">
                <ImageIcon size={31} />
                <strong>Chưa có video</strong>
                <small>
                  {scene.hasCanonicalVoicePreview
                    ? 'WAV đã sẵn sàng; bấm “Tạo video nền” để tiếp tục.'
                    : 'Video hoàn thành sẽ hiển thị tại đây.'}
                </small>
              </div>
            )}
          </div>
          {scene.canonicalVoicePreview?.url && (
            <div className={`scene-canonical-voice ${canonicalVoicePlaybackConfirmed || canonicalNarrationReadyForVideo ? 'played' : ''}`}>
              <div className="scene-canonical-voice-heading">
                <strong><Volume2 size={14} /> Canonical WAV</strong>
                <span>{canonicalNarrationReadyForVideo
                  ? <><CircleCheck size={12} /> WAV sẵn sàng cho video</>
                  : canonicalVoicePlaybackConfirmed
                    ? <><CircleCheck size={12} /> Đã bắt đầu phát</>
                    : 'Cần nghe trước khi duyệt'}</span>
              </div>
              <audio
                src={scene.canonicalVoicePreview.url}
                controls
                preload="metadata"
                aria-label={`Canonical Voice cảnh ${scene.sequenceNumber}`}
                onPlay={() => setCanonicalVoicePlaybackConfirmed(true)}
              />
              <small>
                Voice version {scene.voiceProfileVersionId?.slice(0, 8) ?? 'đang khóa'}
                {scene.voiceSnapshotHash ? ` · ${scene.voiceSnapshotHash.slice(0, 12)}` : ''}
                {scene.canonicalVoicePreview.durationMs != null
                  ? ` · ${(scene.canonicalVoicePreview.durationMs / 1000).toFixed(1)} giây`
                  : ''}
              </small>
              <SpeechPacingIndicator scene={scene} compact />
            </div>
          )}
        </div>

        <div className="scene-content">
          <div className="scene-character-strip">
            {scene.characters.length === 0 ? (
              <span className="scene-character-none"><Users size={13} /> Cảnh không có nhân vật cố định</span>
            ) : scene.characters.map((character) => (
              <span key={character.characterId} className={character.status === 'Approved' ? 'ready' : 'draft'}>
                {character.referencePreviewUrl ? (
                  <img src={character.referencePreviewUrl} alt="" />
                ) : (
                  <UserRound size={13} />
                )}
                {character.name}
                {character.status === 'Approved' && <ShieldCheck size={12} />}
              </span>
            ))}
          </div>
          {isVeo && (
            <div className={`scene-first-frame ${latestFirstFrame?.status.toLowerCase() ?? 'missing'}`}>
              <div className="scene-first-frame-copy">
                <span>FIRST-FRAME VEO</span>
                <strong>
                  {firstFrameOperation?.status === 'generating'
                    ? 'Đang tạo'
                    : firstFrameOperation?.status === 'failed'
                      ? 'Tạo thất bại'
                      : latestFirstFrame
                        ? firstFrameStatusLabel(latestFirstFrame)
                        : 'Chưa tạo'}
                </strong>
                <small>
                  {latestFirstFrame
                    ? `${latestFirstFrame.width}×${latestFirstFrame.height} · ${latestFirstFrame.aspectRatio} · bản ${latestFirstFrame.version}`
                    : 'Cần ảnh 720p đúng tỷ lệ dự án trước khi tạo clip.'}
                </small>
                {latestFirstFrame?.staleReason && <em><TriangleAlert size={12} /> {latestFirstFrame.staleReason}</em>}
                {latestFirstFrame && !latestFirstFrame.previewUrl && <em><TriangleAlert size={12} /> Đã tạo trên server nhưng file local chưa sẵn sàng.</em>}
                {firstFrameAssetBlocker && <em><TriangleAlert size={12} /> {firstFrameAssetBlocker}</em>}
                {firstFrameOperation?.status === 'failed' && <em><TriangleAlert size={12} /> {firstFrameOperation.message}</em>}
              </div>
              {latestFirstFrame?.previewUrl && (
                <button type="button" className="scene-first-frame-preview" onClick={() => onPreviewFirstFrame(latestFirstFrame)}>
                  <img src={latestFirstFrame.previewUrl} alt={`First-frame cảnh ${scene.sequenceNumber}`} />
                  <span>Xem ảnh lớn</span>
                </button>
              )}
              <div className="scene-first-frame-actions">
                <button
                  type="button"
                  disabled={busy || Boolean(firstFrameAssetBlocker)}
                  title={firstFrameAssetBlocker ?? undefined}
                  onClick={() => onRequestFirstFrame(Boolean(latestFirstFrame))}
                >
                  {busy ? <LoaderCircle className="spin" size={13} /> : <WandSparkles size={13} />}
                  {firstFrameAssetBlocker
                    ? 'Xác nhận tài sản trước'
                    : firstFrameOperation?.status === 'generating'
                    ? 'Đang tạo...'
                    : latestFirstFrame
                      ? 'Sinh lại'
                      : 'Tạo first-frame bằng AI'}
                </button>
                {latestFirstFrame && !latestFirstFrame.previewUrl && (
                  <button type="button" disabled={busy} onClick={() => onRetryFirstFrameDownload(latestFirstFrame)}><Download size={13} /> Tải lại output</button>
                )}
                {latestFirstFrame?.status === 'PendingReview' && latestFirstFrame.previewUrl && latestFirstFrame.isCurrent && (
                  <>
                    <button type="button" className="approve" disabled={busy} onClick={() => onApproveFirstFrame(latestFirstFrame)}><ShieldCheck size={13} /> Duyệt</button>
                    <button type="button" className="reject" disabled={busy} onClick={() => onRejectFirstFrame(latestFirstFrame)}><X size={13} /> Từ chối</button>
                  </>
                )}
              </div>
            </div>
          )}
          <div className={`scene-assets-strip ${assetUiState}`}>
            <div className="scene-assets-strip-heading">
              <span><Link2 size={13} /> Tài sản của cảnh</span>
              <div className="scene-assets-heading-actions">
                <small className={`scene-assets-state ${assetUiState}`}>{assetUiState === 'invalid' ? 'Cần chỉnh sửa' : assetUiState === 'pending' ? 'Chờ xác nhận' : 'Đã sẵn sàng'}</small>
                {assetLibrary?.canEdit && assetUiState === 'pending' && !assigningAssets && <button type="button" className="confirm" disabled={busy} onClick={onConfirmAssets}>{confirmingAssets ? <LoaderCircle className="spin" size={12} /> : <ShieldCheck size={12} />}{confirmingAssets ? 'Đang xác nhận...' : 'Xác nhận tài sản cảnh'}</button>}
                {assetLibrary?.canEdit && <button type="button" disabled={busy} onClick={() => setAssigningAssets((current) => !current)}>{assigningAssets ? 'Đóng' : assetUiState === 'invalid' ? 'Sửa lựa chọn' : assignedAssets.length > 0 ? 'Thay đổi' : 'Chọn tài sản'}</button>}
              </div>
            </div>
            <div className="scene-asset-chips">
              {assignedAssets.length === 0 ? <small>Cảnh này không dùng bối cảnh hoặc đạo cụ cố định.</small> : assignedAssets.map((asset) => (
                <span key={asset.projectAssetId} className={asset.assetType.toLowerCase()}>
                  <small>{assetTypeLabel(asset.assetType)}</small>
                  <strong>{asset.name}</strong>
                </span>
              ))}
            </div>
            {assetAssignment && !assetAssignment.isValid && <p><TriangleAlert size={13} /> {(assetAssignment.blockers ?? ['Lựa chọn tài sản chưa hợp lệ.']).join(' ')}</p>}
            {assetUiState === 'pending' && <p className="pending"><CircleHelp size={13} /> AI đã chọn {assignedAssets.length} tài sản. Hãy xác nhận để cảnh sẵn sàng tạo clip.</p>}
            {assetUiState === 'ready' && assignedAssets.length > 0 && <p className="ready"><CircleCheck size={13} /> Tài sản đã được xác nhận và sẵn sàng sử dụng.</p>}
            {assetAssignment && assetAssignment.promptLimit > 0 && assignedAssets.length > 0 && (
              <details className="scene-assets-technical">
                <summary>Chi tiết nâng cao</summary>
                <span>Phần bắt buộc: {assetAssignment.requiredPromptCharacters}/{assetAssignment.promptLimit} ký tự. Prompt hoàn chỉnh được server tự điều chỉnh trong giới hạn Kling.</span>
              </details>
            )}
            {assigningAssets && (
              <div className="scene-assets-picker">
                {(assetLibrary?.assets.length ?? 0) > 0 ? (
                  <>
                    {(['Background', 'Prop', 'Item'] as ProjectAssetType[]).map((assetType) => {
                      const options = assetLibrary?.assets.filter((asset) => asset.assetType === assetType) ?? [];
                      if (options.length === 0) return null;
                      return <div key={assetType}><strong>{assetTypeLabel(assetType)} {assetType === 'Background' ? '· chọn tối đa 1' : '· tùy chọn'}</strong>
                        {assetType === 'Background' && <label className="empty">
                          <input type="radio" name={`background-${scene.sceneId}`} checked={draftAssetIds.size === 0} disabled={busy || assignmentSaving} onChange={() => setDraftAssetIds(new Set())} />
                          <span>Không dùng tài sản cho cảnh này<small>Xóa cả bối cảnh, đạo cụ và item đã chọn</small></span>
                        </label>}
                        {options.map((asset) => (
                        <label key={asset.projectAssetId} className={asset.status === 'Locked' ? 'locked' : 'draft'}>
                          <input type={assetType === 'Background' ? 'radio' : 'checkbox'} name={assetType === 'Background' ? `background-${scene.sceneId}` : undefined} checked={draftAssetIds.has(asset.projectAssetId)} disabled={busy || assignmentSaving || (assetType !== 'Background' && draftBackgroundCount === 0)} onChange={() => toggleAsset(asset)} />
                          <span>{asset.name}<small>{asset.status === 'Locked' ? 'Đã xác nhận' : asset.sourceKind === 'AiGenerated' && asset.sceneIds.includes(scene.sceneId) ? 'AI đề xuất' : 'Có thể chọn'}</small><em>{asset.canonicalDescription}</em></span>
                        </label>
                      ))}</div>;
                    })}
                    {!draftAssetsValid && <div className="scene-validation-message invalid"><TriangleAlert size={13} /> Cảnh có tài sản phải chọn đúng một bối cảnh.</div>}
                    {assetsChanged && draftAssetsValid && <small className="scene-assets-preflight-note">Hệ thống sẽ kiểm tra lựa chọn trước khi áp dụng. Bước này không gọi AI và không phát sinh chi phí.</small>}
                    <div className="scene-assets-picker-actions"><button type="button" className="scene-cancel" disabled={busy || assignmentSaving} onClick={() => { setDraftAssetIds(new Set(assignedAssetIds)); setAssigningAssets(false); }}>Hủy</button><button type="button" className="scene-save" disabled={busy || assignmentSaving || !draftAssetsValid || !assetsChanged} onClick={() => { setAssignmentSaving(true); onUpdateAssets([...draftAssetIds]); }}><Save size={13} /> {assignmentSaving ? 'Đang áp dụng...' : 'Áp dụng lựa chọn'}</button></div>
                  </>
                ) : <small>Hãy tạo hồ sơ text trong bước Tài sản trước.</small>}
              </div>
            )}
          </div>
          {editing ? (
            <div className="scene-edit-form">
              <label>
                Cách provider phát lời
                <select
                  value={speechMode}
                  disabled={isSaving}
                  onChange={(event) => {
                    onClearSaveFailure();
                    const next = event.target.value as UpdateScenePayload['speechMode'];
                    setSpeechMode(next);
                    if (next === 'None') setNarration('');
                  }}
                >
                  <option value="None">Không có lời nói</option>
                  <option value="OnCameraDialogue">Nhân vật nói trực tiếp</option>
                  <option value="NativeVoiceOver">Lời dẫn ngoài khung hình</option>
                </select>
              </label>
              <label>
                Lời provider phải nói nguyên văn
                <textarea
                  spellCheck={false}
                  maxLength={4000}
                  disabled={speechMode === 'None' || isSaving}
                  placeholder={speechMode === 'None' ? 'Cảnh chỉ có âm thanh môi trường.' : 'Nhập nguyên văn lời provider cần nói.'}
                  value={narration}
                  onChange={(event) => {
                    onClearSaveFailure();
                    setNarration(event.target.value);
                  }}
                />
                {speechMode === 'OnCameraDialogue' && scene.characters.length !== 1 && (
                  <small className="scene-validation-message invalid">Lời thoại trực diện cần đúng một nhân vật trong cảnh.</small>
                )}
                {enforceKlingLongFormSpeechPolicy && speechMode === 'NativeVoiceOver' && scene.characters.length !== 0 && (
                  <small className="scene-validation-message invalid">Lời dẫn ngoài khung hình chỉ dùng cho cảnh B-roll không có nhân vật.</small>
                )}
                {speechMode !== 'None' && (
                  <SpeechPacingIndicator scene={scene} draftText={narration} compact />
                )}
              </label>
              {speechMode === 'OnCameraDialogue' && (
                <div className="scene-speaker-lock">
                  <UserRound size={14} /> Người nói duy nhất: <strong>{scene.speakerCharacterName || 'chưa xác định'}</strong>
                </div>
              )}
              <label>
                Phong cách giọng
                <input disabled={isSaving} maxLength={1000} value={voiceStyle} onChange={(event) => {
                  onClearSaveFailure();
                  setVoiceStyle(event.target.value);
                }} placeholder="Ví dụ: ấm áp, tự tin, thân thiện, nhịp tự nhiên" />
              </label>
              <label>
                Âm thanh môi trường
                <input disabled={isSaving} maxLength={1000} value={ambientAudio} onChange={(event) => {
                  onClearSaveFailure();
                  setAmbientAudio(event.target.value);
                }} placeholder="Ví dụ: room tone nhẹ, tiếng chim xa" />
              </label>
              <label>
                Hiệu ứng âm thanh đồng bộ
                <input disabled={isSaving} maxLength={1000} value={soundEffects} onChange={(event) => {
                  onClearSaveFailure();
                  setSoundEffects(event.target.value);
                }} placeholder="Ví dụ: tiếng bước chân nhỏ, tiếng đặt cốc" />
              </label>
              <label>
                Mô tả hình ảnh
                <textarea disabled={isSaving} spellCheck={false} maxLength={12000} required value={visualDescription} onChange={(event) => {
                  onClearSaveFailure();
                  setVisualDescription(event.target.value);
                }} />
              </label>
              <label>
                Prompt hình ảnh
                <textarea disabled={isSaving} spellCheck={false} maxLength={12000} required value={prompt} onChange={(event) => {
                  onClearSaveFailure();
                  setPrompt(event.target.value);
                }} />
              </label>
              {saveState?.status === 'failed' && (
                <div className="scene-save-feedback error" role="alert">
                  <TriangleAlert size={14} />
                  <span>{saveState.message}</span>
                </div>
              )}
              {saveBlocker && (
                <div className="scene-save-feedback" role="status">
                  <TriangleAlert size={14} />
                  <span>{saveBlocker}</span>
                </div>
              )}
            </div>
          ) : (
            <div className="scene-copy-grid">
              <div className="scene-copy-box">
                <span>{speechModeLabel(scene.speechMode, enforceKlingLongFormSpeechPolicy)}</span>
                <ExpandableSceneText text={scene.narration || 'Cảnh này không có lời nói; provider chỉ tạo âm thanh môi trường và hiệu ứng tự nhiên.'} collapseAt={190} />
              </div>
              <div className="scene-copy-box">
                <span>Mô tả hình ảnh</span>
                <ExpandableSceneText text={scene.visualDescription} collapseAt={260} />
              </div>
            </div>
          )}
          {!editing && (
            <div className="scene-audio-intent">
              <span><Film size={12} /> Model do server quản lý · Native Audio</span>
              {scene.speakerCharacterName && <span><UserRound size={12} /> Người nói: {scene.speakerCharacterName}</span>}
              <span><Volume2 size={12} /> Giọng: {scene.voiceStyle || 'tự nhiên, rõ ràng'}</span>
              <span>Ambience: {scene.ambientAudio || 'phù hợp bối cảnh'}</span>
              <span>SFX: {scene.soundEffects || 'đồng bộ hành động'}</span>
            </div>
          )}
          {!editing && canonicalVoiceJourney && (
            <div className="scene-workflow-journey">
              <div className="scene-workflow-heading">
                <strong>Lộ trình tạo video có lời</strong>
                <span>Hệ thống chỉ gọi video provider ở bước “Tạo video nền”.</span>
              </div>
              <ol>
                {canonicalVoiceJourney.steps.map((step, index) => (
                  <li key={step.id} className={step.state}>
                    <span>{step.state === 'complete' ? <Check size={11} /> : index + 1}</span>
                    <b>{step.label}</b>
                  </li>
                ))}
              </ol>
              <p><CircleHelp size={13} /> {canonicalVoiceJourney.nextAction}</p>
            </div>
          )}
          {speechVerificationRequired && scene.speechMode !== 'None' && !canonicalVoiceWorkflow && (
            <div className={`scene-speech-verification ${(scene.speechVerification?.status ?? 'pending').toLowerCase()}`}>
              <div>
                <strong><Languages size={14} /> Kiểm tra transcript</strong>
                <span>{scene.speechVerification
                  ? `${scene.speechVerification.status} · WER ${(scene.speechVerification.wordErrorRate * 100).toFixed(1)}% · CER ${(scene.speechVerification.characterErrorRate * 100).toFixed(1)}%`
                  : 'Chưa chạy ASR cho audio hiện hành.'}</span>
                {speechAudioDurationMs != null && (
                  <small>
                    Audio {(speechAudioDurationMs / 1000).toFixed(1)}s · Thời lượng cảnh {(scene.durationMs / 1000).toFixed(1)}s
                  </small>
                )}
                {scene.speechVerification?.transcript && (
                  <SpeechTranscriptComparison
                    expected={scene.narration ?? ''}
                    transcript={scene.speechVerification.transcript}
                  />
                )}
                {(scene.speechVerification?.missingRequiredTerms.length ?? 0) > 0 && (
                  <small>Thiếu từ bắt buộc: {scene.speechVerification!.missingRequiredTerms.join(', ')}</small>
                )}
                {scene.speechVerification?.status === 'NeedsReview' && !scene.speechVerification.reviewApproved && (
                  <label className="scene-speech-review-reason">
                    <span>ASR chưa đạt ngưỡng. Nhập lý do nếu bạn đã nghe và vẫn muốn chấp nhận kết quả này.</span>
                    <textarea
                      value={speechReviewReason}
                      disabled={busy}
                      maxLength={1000}
                      placeholder="Ví dụ: ASR sai dấu câu nhưng audio đọc đúng nguyên văn sau khi nghe lại."
                      onChange={(event) => setSpeechReviewReason(event.target.value)}
                    />
                    <small>{speechReviewReason.trim().length}/1000 ký tự · tối thiểu 10</small>
                  </label>
                )}
                {scene.speechVerification?.reviewApproved && (
                  <small>Đã chấp nhận thủ công: {scene.speechVerification.reviewReason}</small>
                )}
              </div>
              <button
                type="button"
                disabled={busy || (scene.hasCanonicalVoicePreview ? !scene.canonicalVoicePreview?.url : !scene.preview?.url)}
                onClick={onVerifySceneSpeech}
              >
                <Languages size={13} /> {scene.speechVerification ? 'Kiểm tra lại' : 'Báo giá & kiểm tra lời đọc'}
              </button>
            </div>
          )}
        </div>
      </div>

      <footer className="scene-footer">
        {editing ? (
          <div className="scene-edit-actions">
            <button type="button" className="scene-cancel" disabled={busy} onClick={() => setEditing(false)}><X size={14} /> Hủy</button>
            <button type="button" className="scene-save" disabled={busy || !validDraft} onClick={save}>
              {isSaving ? <LoaderCircle className="spin" size={14} /> : <Save size={14} />}
              {isSaving ? 'Đang lưu...' : 'Lưu cảnh'}
            </button>
          </div>
        ) : (
          <>
            <details className="scene-prompt">
              <summary>Prompt nguồn của cảnh</summary>
              <p>{scene.prompt}</p>
            </details>
            {(scene.lastErrorMessage || status.tone === 'failed') && (
              <div className="scene-error">
                <TriangleAlert size={14} />
                <div className="scene-error-content">
                  <span>{scene.lastErrorMessage || 'Clip của cảnh chưa hoàn tất. Bạn có thể chọn cảnh và thử lại.'}</span>
                </div>
              </div>
            )}
            {scene.requiresAudioReview && !canonicalNarrationReadyForVideo && scene.speechStatus !== 'SpeechReadyForLipSync' && (
              <div className="scene-audio-review">
                <div>
                  <strong><Volume2 size={15} /> Cần nghe và duyệt lời nói</strong>
                  <span>
                    {scene.speechMode === 'None'
                      ? 'Hãy kiểm tra âm thanh môi trường và hiệu ứng có phù hợp với hình ảnh.'
                      : scene.hasCanonicalVoicePreview
                        ? scene.speechMode === 'OnCameraDialogue'
                          ? 'Hãy nghe Canonical Voice. Sau khi duyệt, cảnh sẽ dừng ở trạng thái sẵn sàng cho lip-sync.'
                          : canonicalVoiceOnlyReview
                            ? 'Hãy nghe và duyệt Canonical WAV. Chỉ sau bước này hệ thống mới cho phép sinh video nền.'
                            : 'Hãy nghe Canonical Voice và clip đã thay hoàn toàn native speech trước khi duyệt.'
                        : `Hãy kiểm tra lời nói đúng nguyên văn, đúng người nói và khớp khẩu hình. Hệ thống đã phát hiện track âm thanh${scene.nativeAudioAudible ? ' có tín hiệu nghe được' : ''}.`}
                  </span>
                  {audioReviewBlocker && (
                    <span className="scene-audio-review-blocker"><CircleHelp size={13} /> {audioReviewBlocker}</span>
                  )}
                  {scene.speechMode !== 'None' && (
                    <div className="scene-audio-review-checklist">
                      <label><input type="checkbox" checked={speechContentConfirmed} disabled={busy || !reviewControlsReady} onChange={(event) => setSpeechContentConfirmed(event.target.checked)} /> Tôi đã nghe rõ đủ câu và đúng nguyên văn.</label>
                      <label><input type="checkbox" checked={speakerConfirmed} disabled={busy || !reviewControlsReady} onChange={(event) => setSpeakerConfirmed(event.target.checked)} /> {scene.speechMode === 'OnCameraDialogue' ? 'Đúng nhân vật trên màn hình đang nói.' : 'Đúng là lời dẫn ngoài khung hình; không có nhân vật nói trực tiếp.'}</label>
                      <label><input type="checkbox" checked={lipSyncConfirmed} disabled={busy || !reviewControlsReady} onChange={(event) => setLipSyncConfirmed(event.target.checked)} /> {canonicalVoiceOnlyReview ? 'Chất giọng và thời lượng WAV phù hợp với cảnh.' : scene.speechMode === 'OnCameraDialogue' ? 'Khẩu hình và biểu cảm chấp nhận được.' : 'Giọng dẫn và hình ảnh đồng bộ, chấp nhận được.'}</label>
                    </div>
                  )}
                </div>
                <button
                  type="button"
                  title={audioReviewBlocker ?? undefined}
                  disabled={busy || !scene.canApproveNativeAudio || !requiredPlaybackConfirmed ||
                    !reviewChecklistComplete ||
                    (speechVerificationRequired && !canonicalVoiceOnlyReview && scene.speechVerification?.status === 'NeedsReview' && !scene.speechVerification.reviewApproved && speechReviewReason.trim().length < 10)}
                  onClick={() => onApproveNativeAudio(
                    requiredPlaybackConfirmed && reviewChecklistComplete,
                    speechReviewReason)}
                >
                  <CircleCheck size={14} /> {canonicalVoiceOnlyReview ? 'Duyệt Canonical WAV' : 'Duyệt hình và âm thanh'}
                </button>
              </div>
            )}
            {scene.characterSetupMessage && (
              <div className="scene-character-warning"><TriangleAlert size={14} /> {scene.characterSetupMessage}</div>
            )}
            {enforceKlingLongFormSpeechPolicy && !canonicalVoiceWorkflow && scene.status === 'NativeAudioInvalid' && (
              <div className="scene-character-warning"><Volume2 size={14} /> Lần tạo trước không có lời nghe được. Lần thử tiếp theo sẽ dùng prompt ưu tiên lời thoại và là một request có phí mới.</div>
            )}
            <div className="scene-actions">
              {scene.canEdit && (
                <button type="button" className="scene-edit" disabled={busy} onClick={beginEdit}><Pencil size={14} /> Chỉnh sửa</button>
              )}
              {audioCanBeUnapproved && (
                <button type="button" className="scene-edit" disabled={busy} onClick={onUnapproveAudio}><X size={14} /> Hủy duyệt</button>
              )}
              {selectable && (
                <button type="button" className="scene-generate-one" disabled={busy || !videoReady || !mediaToolsReady} onClick={onGenerate}>
                  {sceneNeedsLocalCompletion(scene)
                    ? <Download size={14} />
                    : needsCanonicalVoicePreparation(scene, speechProductionPolicy)
                      ? <Volume2 size={14} />
                      : <Film size={14} />}
                  {sceneNeedsLocalCompletion(scene)
                    ? 'Tiếp tục tải clip'
                    : needsCanonicalVoicePreparation(scene, speechProductionPolicy)
                      ? !canonicalVoiceWorkflow && scene.status === 'NativeAudioInvalid'
                        ? 'Xử lý lại giọng đọc'
                        : scene.hasCanonicalVoicePreview
                          ? 'Tiếp tục duyệt WAV'
                          : 'Chuẩn bị bản đọc WAV'
                    : canonicalNarrationReadyForVideo
                      ? 'Tạo video nền'
                    : canonicalVoiceWorkflow && scene.speechStatus === 'SpeechApproved' && !scene.preview?.url
                        ? 'Tạo video nền'
                    : enforceKlingLongFormSpeechPolicy && scene.status === 'NativeAudioInvalid'
                      ? 'Tạo lại với prompt ưu tiên lời thoại'
                      : status.tone === 'running'
                        ? 'Tiếp tục theo dõi'
                        : status.tone === 'failed'
                          ? 'Thử lại cảnh này'
                          : 'Tạo clip cảnh này'}
                </button>
              )}
              {scene.speechStatus === 'SpeechReadyForLipSync' && <span className="scene-complete-note"><CircleCheck size={15} /> Canonical Voice đã duyệt · chờ lip-sync</span>}
              {isSceneCompleted(scene) && <span className="scene-complete-note"><CircleCheck size={15} /> Hình và lời nói đã được duyệt</span>}
            </div>
          </>
        )}
      </footer>
    </article>
  );
}

function ExpandableSceneText({ text, collapseAt }: { text: string; collapseAt: number }) {
  const [expanded, setExpanded] = useState(false);
  const normalizedLength = text.replace(/\s+/g, ' ').trim().length;
  const canExpand = normalizedLength > collapseAt;

  useEffect(() => setExpanded(false), [text]);

  return (
    <div className="scene-readable-copy">
      <p className={canExpand && !expanded ? 'collapsed' : ''}>{text}</p>
      {canExpand && (
        <button type="button" aria-expanded={expanded} onClick={() => setExpanded((current) => !current)}>
          {expanded ? 'Thu gọn' : 'Xem thêm'}
        </button>
      )}
    </div>
  );
}

function SpeechPacingIndicator({
  scene,
  draftText,
  compact = false
}: {
  scene: SceneSummary;
  draftText?: string;
  compact?: boolean;
}) {
  const sceneDurationSeconds = scene.durationMs / 1000;
  const speakingRate = scene.speechPacing?.speakingRate ?? 1;
  const calculated = draftText === undefined
    ? scene.speechPacing ?? assessSpeechPacing(
      scene.narration ?? '',
      sceneDurationSeconds,
      speakingRate
    )
    : assessSpeechPacing(
      draftText,
      sceneDurationSeconds,
      speakingRate
    );
  if (!calculated) return null;

  const useActual = draftText === undefined &&
    scene.speechPacing?.actualDurationSeconds != null &&
    scene.speechPacing.actualStatus != null;
  const measuredSeconds = useActual
    ? scene.speechPacing!.actualDurationSeconds!
    : calculated.estimatedDurationSeconds;
  const status = useActual
    ? scene.speechPacing!.actualStatus!
    : calculated.estimatedStatus;
  const statusLabel = status === 'TooShort'
    ? 'Quá ngắn'
    : status === 'Short'
      ? 'Hơi ngắn'
      : status === 'OnTarget'
        ? 'Phù hợp'
        : status === 'Long'
          ? 'Hơi dài'
          : 'Quá dài';
  const tone = status === 'OnTarget'
    ? 'ready'
    : status === 'TooShort' || status === 'TooLong'
      ? 'danger'
      : 'warning';

  return (
    <div className={`speech-pacing-indicator ${tone} ${compact ? 'compact' : ''}`}>
      <Clock3 size={13} />
      <span>
        <strong>{useActual ? 'WAV thực tế' : 'Ước tính'}: {formatPacingSeconds(measuredSeconds)} / {formatPacingSeconds(sceneDurationSeconds)} · {statusLabel}</strong>
        {!compact && (
          <small>Mục tiêu {formatPacingSeconds(calculated.targetMinimumSeconds)}–{formatPacingSeconds(calculated.targetMaximumSeconds)} để lời bám sát cảnh.</small>
        )}
        {useActual && status === 'TooShort' && (
          <small>WAV vẫn được dùng để tạo video; hệ thống không tự sinh lại giọng.</small>
        )}
      </span>
    </div>
  );
}

function formatPacingSeconds(value: number): string {
  return `${new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 1 }).format(value)}s`;
}

function firstFrameStatusLabel(frame: SceneFirstFrameSummary): string {
  if (!frame.isCurrent || frame.status === 'Invalidated') return 'Đã lỗi thời';
  if (frame.status === 'PendingReview') return frame.previewUrl ? 'Chờ duyệt' : 'Chưa tải xong';
  if (frame.status === 'Approved') return 'Đã duyệt';
  if (frame.status === 'Rejected') return 'Đã từ chối';
  return 'Đã được thay thế';
}

function canQueueScene(scene: SceneSummary): boolean {
  return scene.canGenerate && !isSceneCompleted(scene) && scene.prompt.trim().length > 0;
}

function sceneAssignedAssetIds(sceneId: string, assetLibrary: ProjectAssetLibrary | null): string[] {
  return assetLibrary?.sceneAssignments.find((assignment) => assignment.sceneId === sceneId)?.projectAssetIds ?? [];
}

function areSceneAssetsReady(sceneId: string, assetLibrary: ProjectAssetLibrary | null): boolean {
  return getSceneFirstFrameAssetBlocker(sceneId, assetLibrary) === null;
}

function matchesStoryboardFilter(scene: SceneSummary, filter: StoryboardFilter): boolean {
  if (filter === 'all') return true;
  const status = scene.status.toLowerCase();
  const tone = sceneStatus(scene).tone;
  if (filter === 'approved') return isSceneCompleted(scene);
  if (filter === 'review') return status === 'audioreviewrequired' || scene.requiresAudioReview || scene.canApproveNativeAudio;
  if (filter === 'processing') return tone === 'running';
  if (filter === 'failed') return tone === 'failed';
  return !isSceneCompleted(scene) && tone !== 'running' && tone !== 'failed' && status !== 'audioreviewrequired';
}

function isSceneCompleted(scene: SceneSummary): boolean {
  return scene.status.toLowerCase() === 'approved';
}

function sceneStatus(scene: SceneSummary): { label: string; tone: 'ready' | 'running' | 'completed' | 'failed' | 'waiting' } {
  if (isSceneCompleted(scene)) return { label: 'Hoàn thành', tone: 'completed' };
  const status = scene.status.toLowerCase();
  if (status === 'audioreviewrequired') {
    if (scene.hasCanonicalVoicePreview && !scene.preview?.url) {
      return { label: 'Cần nghe duyệt WAV', tone: 'waiting' };
    }
    if (scene.preview?.url) return { label: 'Cần duyệt video', tone: 'waiting' };
    return { label: 'Cần nghe duyệt', tone: 'waiting' };
  }
  if (status === 'promptinvalid') return { label: 'Cần sửa lời', tone: 'failed' };
  if (status === 'nativeaudioinvalid') return { label: 'Âm thanh không đạt', tone: 'failed' };
  if (status.includes('fail')) return { label: 'Cần thử lại', tone: 'failed' };
  if (sceneNeedsLocalCompletion(scene)) return { label: 'Chờ lưu clip', tone: 'running' };
  if (status.includes('waiting')) {
    if (scene.lastErrorCode === 'provider_output_download_failed') return { label: 'Đang lưu clip', tone: 'running' };
    if (scene.lastErrorCode === 'provider_status_check_failed') return { label: 'Đang kết nối lại', tone: 'running' };
    return { label: 'Đang tạo', tone: 'running' };
  }
  if (status.includes('prompt') || status.includes('ready')) return { label: 'Sẵn sàng', tone: 'ready' };
  return { label: 'Chờ xử lý', tone: 'waiting' };
}

function sceneNeedsLocalCompletion(scene: SceneSummary): boolean {
  const status = scene.status.toLowerCase();
  return !isSceneCompleted(scene) && (status === 'generated' || status === 'downloading');
}

function SpeechTranscriptComparison({ expected, transcript }: { expected: string; transcript: string }) {
  const diff = buildSpeechTranscriptDiff(expected, transcript);
  return (
    <div className={`speech-transcript-diff ${diff.matches ? 'matches' : 'differs'}`}>
      <div>
        <strong>Lời đã khóa</strong>
        <SpeechDiffText segments={diff.expected} />
      </div>
      <div>
        <strong>ASR nhận được</strong>
        <SpeechDiffText segments={diff.transcript} emptyText="Không nhận được lời nói" />
      </div>
    </div>
  );
}

function SpeechDiffText({
  segments,
  emptyText = 'Không có nội dung'
}: {
  segments: SpeechDiffSegment[];
  emptyText?: string;
}) {
  if (segments.length === 0) return <p className="speech-diff-empty">{emptyText}</p>;
  return (
    <p>
      {segments.map((segment, index) => (
        <span className={`speech-diff-${segment.kind}`} key={`${index}-${segment.text}`}>
          {segment.text}{index < segments.length - 1 ? ' ' : ''}
        </span>
      ))}
    </p>
  );
}

function speechModeLabel(mode: SceneSummary['speechMode'], enforceKlingLongFormSpeechPolicy = false): string {
  if (mode === 'OnCameraDialogue') return 'Nhân vật nói trực tiếp bằng Native Audio của provider';
  if (mode === 'NativeVoiceOver') return enforceKlingLongFormSpeechPolicy
    ? 'Lời dẫn ngoài khung hình — cảnh không có nhân vật'
    : 'Lời dẫn ngoài khung hình bằng Native Audio của provider';
  return 'Không có lời nói';
}

function formatTimeline(milliseconds: number): string {
  const seconds = Math.max(0, Math.floor(milliseconds / 1000));
  return `${String(Math.floor(seconds / 60)).padStart(2, '0')}:${String(seconds % 60).padStart(2, '0')}`;
}

function sceneDisplayTitle(scene: SceneSummary): string {
  const normalized = scene.storyPurpose
    .replace(/\s*Thời lượng\s*:\s*\d+\s*giây\.?\s*$/iu, '')
    .trim();
  return normalized || `Nội dung cảnh ${scene.sequenceNumber}`;
}

function CreateVideoCard({
  busy,
  speechSynchronizationEnabled,
  voiceOptions,
  voiceCatalogPreviews,
  voiceCatalogPreviewPendingKey,
  onPreviewCatalogVoice,
  voicePreviewProjectName,
  onCreate
}: {
  busy: boolean;
  speechSynchronizationEnabled: boolean;
  voiceOptions: OpenAiVoiceOption[];
  voiceCatalogPreviews: Record<string, VoiceCatalogPreviewPlayback>;
  voiceCatalogPreviewPendingKey: string | null;
  onPreviewCatalogVoice: (voiceCode: string, speakingRate: number) => void;
  voicePreviewProjectName?: string;
  onCreate: (payload: CreateProjectPayload) => void;
}) {
  const [topic, setTopic] = useState('');
  const [aspectRatio, setAspectRatio] = useState('16:9');
  const [speechProductionPolicy, setSpeechProductionPolicy] =
    useState<CreateProjectPayload['speechProductionPolicy']>('ProviderNativeVerified');
  const [voiceCode, setVoiceCode] = useState('');
  const [voiceSpeakingRate, setVoiceSpeakingRate] = useState(1);

  useEffect(() => {
    if (resolveVoiceOption(voiceCode, voiceOptions) || voiceOptions.length === 0) return;
    setVoiceCode(
      voiceOptions.find((option) => option.voiceCode === 'shimmer')?.voiceCode ??
      voiceOptions[0].voiceCode
    );
  }, [voiceCode, voiceOptions]);

  const submit = () => {
    const normalizedTopic = topic.trim();
    if (!normalizedTopic || busy || (speechProductionPolicy === 'CanonicalVoice' && !voiceCode)) return;
    onCreate({
      topic: normalizedTopic,
      aspectRatio,
      languageCode: 'vi-VN',
      speechProductionPolicy,
      voiceCode: speechProductionPolicy === 'CanonicalVoice' ? voiceCode : null,
      voiceSpeakingRate: speechProductionPolicy === 'CanonicalVoice' ? voiceSpeakingRate : null
    });
  };

  return (
    <section className="card create-card">
      <h2>Nhập chủ đề video</h2>
      <div className="topic-field">
        <textarea
          maxLength={300}
          value={topic}
          onChange={(event) => setTopic(event.target.value)}
          placeholder="Ví dụ: Tạo video viral về 5 thói quen buổi sáng giúp tăng năng lượng..."
        />
        <span>{topic.length}/300</span>
      </div>
      <div className="create-options">
        <div className="option-group ratio-group"><label>Tỉ lệ khung hình</label><div>
          {['16:9', '9:16', '1:1'].map((ratio) => (
            <button className={aspectRatio === ratio ? 'selected' : ''} key={ratio} onClick={() => setAspectRatio(ratio)}>{ratio}</button>
          ))}
        </div></div>
        <label className="select-group">Ngôn ngữ<select value="vi-VN" disabled><option value="vi-VN">Tiếng Việt</option></select></label>
        <label className="select-group">
          Đồng bộ lời nói
          <select
            value={speechProductionPolicy}
            disabled={!speechSynchronizationEnabled}
            onChange={(event) => setSpeechProductionPolicy(event.target.value as CreateProjectPayload['speechProductionPolicy'])}
          >
            <option value="ProviderNativeVerified">Provider Native Audio</option>
            {speechSynchronizationEnabled && <option value="CanonicalVoice">Canonical Voice</option>}
          </select>
          {!speechSynchronizationEnabled && <small>Canonical Voice đang tắt theo cấu hình rollout.</small>}
        </label>
        {speechProductionPolicy === 'CanonicalVoice' && (
          <>
            <div className="select-group">
              <span>Giọng narrator</span>
              <VoicePickerField
                value={voiceCode}
                options={voiceOptions}
                disabled={busy}
                previewControls={{
                  speakingRate: voiceSpeakingRate,
                  previews: voiceCatalogPreviews,
                  pendingKey: voiceCatalogPreviewPendingKey,
                  onPreview: onPreviewCatalogVoice,
                  contextProjectName: voicePreviewProjectName
                }}
                onChange={setVoiceCode}
              />
            </div>
            <label className="select-group">
              Tốc độ đọc
              <select value={voiceSpeakingRate} onChange={(event) => setVoiceSpeakingRate(Number(event.target.value))}>
                <option value={0.9}>Chậm · 0,9×</option>
                <option value={1}>Tự nhiên · 1,0×</option>
                <option value={1.1}>Nhanh · 1,1×</option>
              </select>
            </label>
          </>
        )}
        <div className="select-group create-native-audio-note">
          <span>Âm thanh</span>
          <strong><Volume2 size={15} /> {speechProductionPolicy === 'CanonicalVoice' ? 'Canonical Voice' : 'Provider Native Audio'}</strong>
          <small>{speechProductionPolicy === 'CanonicalVoice'
            ? 'Lời dẫn được tạo thành WAV có version và ghép sau khi clip nền hoàn tất. Cảnh thấy miệng sẽ chờ bước lip-sync.'
            : 'Giọng nói, âm thanh môi trường và hiệu ứng được provider tạo cùng clip.'}</small>
        </div>
        <button
          className="start-button"
          disabled={!topic.trim() || busy || (speechProductionPolicy === 'CanonicalVoice' && !voiceCode)}
          onClick={submit}
        >
          {busy ? <LoaderCircle className="spin" size={18} /> : <Play size={17} fill="currentColor" />} Bắt đầu tạo
        </button>
      </div>
    </section>
  );
}

function WorkflowCard({ project }: { project: ProjectDashboard | null }) {
  const stages = project?.pipeline ?? createEmptyStages();
  return (
    <section className="card workflow-card">
      <h2>Quy trình tạo video bằng AI</h2>
      <div className="workflow-track">
        {stages.map((stage, index) => {
          const Icon = stageIcons[stage.code] ?? WandSparkles;
          const color = stageColors[stage.code] ?? '#3978d2';
          return (
            <div className="workflow-stage" key={stage.code}>
              {index > 0 && <div className="stage-connector" />}
              <div className={`stage-orb status-${stage.status}`} style={{ '--stage-color': color } as React.CSSProperties}>
                <Icon aria-hidden="true" size={29} strokeWidth={2.15} />
              </div>
              <span className="stage-number" style={{ backgroundColor: color }}>{index + 1}</span>
              <strong>{stage.title}</strong><small>{stage.subtitle}</small>
            </div>
          );
        })}
      </div>
    </section>
  );
}

function PipelineDetails({ project, onUnavailable }: { project: ProjectDashboard | null; onUnavailable: (message: string) => void }) {
  const stages = createDisplayStages(project);
  return (
    <section className="pipeline-section">
      <h2 className="section-title">Chi tiết tiến trình</h2>
      <div className="pipeline-grid">
        {stages.map((stage, index) => {
          const Icon = stageIcons[stage.code] ?? WandSparkles;
          const actionLabel = getStageActionLabel(stage);
          return (
          <article className={`pipeline-card stage-${stage.code}`} key={stage.code}>
            <div className="pipeline-title">
              <span className="pipeline-title-icon"><Icon size={13} /></span>
              <strong>{stage.title}</strong>
              {stage.status === 'completed' && <CircleCheck className="pipeline-complete" size={17} fill="currentColor" />}
            </div>
            <StatusBadge status={stage.status} />
            <div className="pipeline-copy">
              {stage.detailLines.length > 0
                ? stage.detailLines.map((line) => <p key={line}>{stage.status === 'completed' && <Check size={12} strokeWidth={3} />}<span>{line}</span></p>)
                : <p><span>Chưa có dữ liệu xử lý.</span></p>}
            </div>
            {stage.progressPercent > 0 && stage.status === 'processing' && <ProgressBar value={stage.progressPercent} />}
            <button onClick={() => onUnavailable(`${stage.title}: chức năng chi tiết đang được phát triển.`)}>
              {actionLabel}
            </button>
            {index < stages.length - 1 && <span className="pipeline-arrow"><ArrowRight size={18} /></span>}
          </article>
          );
        })}
      </div>
    </section>
  );
}

function ModelsSection({ models }: { models: AiModel[] }) {
  const trackRef = useRef<HTMLDivElement>(null);
  const displayModels = createDisplayModels(models);
  return (
    <section className="models-section">
      <h2 className="section-title">AI Models sẵn sàng</h2>
      <div className="models-carousel-shell">
        <div className="models-grid" ref={trackRef}>
          {displayModels.map((model) => <ModelCard key={model.id} model={model} />)}
        </div>
        <button className="models-next" aria-label="Xem thêm AI model" onClick={() => trackRef.current?.scrollBy({ left: 210, behavior: 'smooth' })}>
          <ArrowRight size={18} />
        </button>
      </div>
    </section>
  );
}

function ModelCard({ model }: { model: ModelDisplay }) {
  return (
    <article className={`card model-card ${model.badge ? 'featured' : ''}`}>
      <ModelLogo brand={model.brand} label={model.name} />
      <div className="model-name"><strong>{model.name}</strong><span>{model.description}</span></div>
      <div className="model-meta">{model.badge && <span className="model-badge">{model.badge}</span>}<span>{model.secondary}</span></div>
      <span className="model-readonly-badge">Quản lý tập trung</span>
    </article>
  );
}

function ModelLogo({ brand, label }: { brand: ModelDisplay['brand']; label: string }) {
  const assets: Partial<Record<ModelDisplay['brand'], string>> = {
    kling: klingLogo,
    google: googleLogo,
    runway: runwayLogo,
    pika: pikaLogo,
    sora: openAiLogo
  };
  const source = assets[brand];
  if (source) {
    return <div className={`model-logo logo-${brand}`}><img src={source} alt={`${label} logo`} /></div>;
  }
  return <div className="model-logo logo-generic" aria-label={label}>{label.charAt(0).toUpperCase()}</div>;
}

function getFinalPreviewState(project: ProjectDashboard | null): { label: string; tone: 'ready' | 'processing' | 'waiting' | 'error' } {
  if (!project) return { label: 'Chọn hoặc tạo một dự án', tone: 'waiting' };

  const renderStatus = project.render.status.toLowerCase();
  const finalRenderRunning = renderStatus === 'rendering' || renderStatus === 'validatingoutput';
  if (project.preview?.url) {
    return finalRenderRunning
      ? { label: 'Đang dựng phiên bản mới · bản hoàn chỉnh trước vẫn có thể xem', tone: 'processing' }
      : { label: 'Video hoàn chỉnh đã sẵn sàng', tone: 'ready' };
  }
  if (finalRenderRunning) {
    return { label: 'FFmpeg đang dựng video hoàn chỉnh', tone: 'processing' };
  }
  if (renderStatus === 'failed') {
    return { label: 'Dựng video cuối chưa thành công · hãy kiểm tra và thử lại', tone: 'error' };
  }
  if (project.runningJobs > 0) {
    return { label: `Đang xử lý clip cảnh · ${project.approvedScenes}/${project.totalScenes} cảnh đã duyệt`, tone: 'processing' };
  }
  if (project.failedScenes > 0 || project.failedJobs > 0) {
    return { label: 'Một số cảnh đang lỗi · cần xử lý trước khi dựng video cuối', tone: 'error' };
  }
  if (project.totalScenes > 0 && project.approvedScenes === project.totalScenes) {
    return { label: 'Các cảnh đã sẵn sàng · hãy dựng video cuối', tone: 'waiting' };
  }
  if (project.approvedScenes > 0) {
    return { label: `${project.approvedScenes}/${project.totalScenes} cảnh đã duyệt · có thể dựng video ngay`, tone: 'waiting' };
  }
  if (project.totalScenes > 0) {
    return { label: `Chờ hoàn tất các cảnh · ${project.approvedScenes}/${project.totalScenes} cảnh đã duyệt`, tone: 'waiting' };
  }
  return { label: 'Video hoàn chỉnh sẽ xuất hiện sau khi dựng xong', tone: 'waiting' };
}

function PreviewCard({ project }: { project: ProjectDashboard | null }) {
  const preview = project?.preview;
  const state = getFinalPreviewState(project);
  return (
    <section className="card side-card preview-card">
      <h2>Xem trước dự án</h2>
      <div className="preview-frame">
        {preview?.url ? (
          <video key={preview.url} controls preload="metadata" src={preview.url} />
        ) : (
          <div className="preview-placeholder"><div className="sun" /><div className="mountain mountain-back" /><div className="mountain mountain-front" /><button aria-label="Chưa có video"><Play size={30} fill="white" /></button><span>{project ? 'Chưa có video hoàn chỉnh' : 'Chọn hoặc tạo một dự án'}</span></div>
        )}
      </div>
      {project && <div className={`preview-status ${state.tone}`}>{state.label}</div>}
      <div className="preview-meta"><Play size={13} fill="currentColor" /><span>{preview?.url ? 'Video cuối' : 'Mục tiêu'} · {formatDuration(preview?.durationMs ? Math.round(preview.durationMs / 1000) : project?.project.targetDurationSeconds ?? 0)}</span><div className="fake-timeline"><i /></div></div>
    </section>
  );
}

function ProjectInfoCard({ project }: { project: ProjectDashboard | null }) {
  return (
    <section className="card side-card project-info-card">
      <h2>Thông tin dự án</h2>
      {project ? <dl>
        <dt>Tên dự án</dt><dd title={project.project.name}>{project.project.name}</dd>
        <dt>Tỉ lệ</dt><dd>{project.project.aspectRatio}</dd>
        <dt>Tổng thời lượng</dt><dd>{formatDuration(project.project.targetDurationSeconds)}</dd>
        <dt>Số cảnh</dt><dd>{project.totalScenes} cảnh</dd>
        <dt>Ngày tạo</dt><dd>{formatDate(project.createdAtUtc)}</dd>
        <dt>Trạng thái</dt><dd><span className="project-status">{translateProjectStatus(project.project.status)}</span></dd>
      </dl> : <EmptyBlock text="Chưa có dự án được chọn." />}
    </section>
  );
}

function RenderProgressCard({
  project,
  busy,
  mediaToolsReady,
  onRender,
  onExport,
  onUnavailable
}: {
  project: ProjectDashboard | null;
  busy: boolean;
  mediaToolsReady: boolean;
  onRender: () => void;
  onExport: () => void;
  onUnavailable: (message: string) => void;
}) {
  const progress = Math.round(project?.render.progressPercent ?? project?.overallProgressPercent ?? 0);
  const readyToRender = Boolean(
    project && project.approvedScenes > 0
  );
  return (
    <section className="card side-card render-card">
      <h2>Tiến độ render</h2>
      {project ? <>
        <div className="render-summary">
          <div className="progress-ring" style={{ '--progress': `${Math.min(100, Math.max(0, progress)) * 3.6}deg` } as React.CSSProperties}><div><strong>{progress}%</strong></div></div>
          <div><strong>{project.render.totalScenes > 0 ? `Đã duyệt ${project.render.completedScenes}/${project.render.totalScenes} cảnh` : 'Chưa có cảnh để render'}</strong><span>{project.runningJobs > 0 ? `${project.runningJobs} tác vụ đang xử lý` : translateProjectStatus(project.project.status)}</span><ProgressBar value={progress} /></div>
        </div>
        <button
          className="render-final-button"
          disabled={busy || !mediaToolsReady || !readyToRender}
          onClick={onRender}
        >
          <Film size={15} /> {project.preview?.url ? 'Dựng lại video' : 'Dựng video cuối'}
        </button>
        {project.preview?.url && (
          <button className="render-export-button" disabled={busy} onClick={onExport}>
            <Download size={15} /> Xuất video MP4
          </button>
        )}
        {!readyToRender && <small className="render-requirement">Cần tạo và duyệt ít nhất một cảnh.</small>}
        {readyToRender && project.approvedScenes < project.totalScenes && <small className="render-requirement">Bản dựng sẽ chỉ gồm {project.approvedScenes} cảnh đã duyệt; các cảnh còn lại được bỏ qua.</small>}
        <button className="danger-outline" disabled={project.runningJobs === 0} onClick={() => onUnavailable('Dừng xử lý sẽ được bật khi pipeline hỗ trợ hủy job an toàn.')}>Dừng xử lý</button>
      </> : <EmptyBlock text="Tiến độ sẽ xuất hiện sau khi tạo dự án." />}
    </section>
  );
}

function ProjectsPage({ projects, onSelect, onCreate }: { projects: ProjectSummary[]; onSelect: (id: string) => void; onCreate: () => void }) {
  return (
    <div className="page-shell projects-page">
      {projects.length === 0 ? <section className="card projects-empty"><FolderOpen size={38} /><h2>Chưa có dự án</h2><p>Hãy tạo dự án video đầu tiên của bạn.</p><button onClick={onCreate}>Tạo dự án</button></section> : <section className="projects-grid">{projects.map((project) => (
        <button className="card project-tile" key={project.projectId} onClick={() => onSelect(project.projectId)}>
          <div className="project-tile-icon"><Film size={23} /></div><div><strong>{project.name}</strong><p>{project.topic}</p><span>{project.aspectRatio} · {formatDuration(project.targetDurationSeconds)} · {translateProjectStatus(project.status)}</span></div><ChevronDown size={18} className="tile-arrow" />
        </button>
      ))}</section>}
    </div>
  );
}

function DesktopSettingsPage({
  settings,
  busy,
  onSpeechSynchronizationChange
}: {
  settings: DesktopFeatureSettings;
  busy: boolean;
  onSpeechSynchronizationChange: (enabled: boolean) => void;
}) {
  const configured = settings.speechSynchronizationEnabled;
  const statusLabel = settings.restartRequired
    ? 'Chờ khởi động lại'
    : settings.activeSpeechSynchronizationEnabled
      ? 'Đang bật'
      : 'Đang tắt';

  return (
    <div className="page-shell desktop-settings-page">
      <section className="desktop-settings-intro">
        <span><Settings size={22} /></span>
        <div>
          <h2>Cài đặt ứng dụng trên máy này</h2>
          <p>Các lựa chọn tại đây không chứa API key và không thay đổi quyền AI của tổ chức.</p>
        </div>
      </section>

      <section className="card desktop-setting-card" id="speech-synchronization-setting">
        <div className="desktop-setting-heading">
          <span className="desktop-setting-icon"><Volume2 size={22} /></span>
          <div>
            <span className="api-eyebrow">VIDEO DÀI · ÂM THANH</span>
            <h2>Đồng bộ lời nói</h2>
            <p>Mở Canonical Voice và quy trình tạo, kiểm tra kỹ thuật, nghe duyệt lời nói trên Desktop này.</p>
          </div>
          <span className={`desktop-setting-status ${settings.activeSpeechSynchronizationEnabled ? 'active' : ''} ${settings.restartRequired ? 'pending' : ''}`}>
            {statusLabel}
          </span>
        </div>

        <button
          type="button"
          className={`desktop-feature-switch ${configured ? 'enabled' : ''}`}
          role="switch"
          aria-checked={configured}
          disabled={busy}
          onClick={() => onSpeechSynchronizationChange(!configured)}
        >
          {configured ? <Volume2 size={19} /> : <VolumeX size={19} />}
          <span>
            <strong>{configured ? 'Bật đồng bộ lời nói' : 'Đồng bộ lời nói đang tắt'}</strong>
            <small>Lưu riêng cho ứng dụng VideoMaker trên máy hiện tại.</small>
          </span>
          <i aria-hidden="true"><b /></i>
        </button>

        {settings.restartRequired && (
          <div className="desktop-setting-restart" role="status">
            <RefreshCw size={18} />
            <div>
              <strong>Đã lưu thay đổi</strong>
              <p>Hãy đóng hoàn toàn rồi mở lại VideoMaker để cấu hình mới có hiệu lực.</p>
            </div>
          </div>
        )}

        <div className="desktop-setting-server-note">
          <ShieldCheck size={18} />
          <div>
            <strong>Server vẫn kiểm soát request có phí</strong>
            <p>TTS và ASR của các workflow được hỗ trợ chỉ chạy khi quản trị viên đã bật cờ server, cấu hình model, đơn giá và ngân sách. Canonical Voice không yêu cầu ASR. Desktop không thể tự thay đổi các chốt này.</p>
          </div>
        </div>
      </section>
    </div>
  );
}

function ApiKeysPage({
  settings,
  providerStatus,
  organization,
  license,
  busy,
  onTest
}: {
  settings: ProviderSettings;
  providerStatus: GenerationProviderStatus;
  organization: OrganizationSummary | null;
  license: NonNullable<DashboardState['license']>;
  busy: boolean;
  onTest: (providerCode: 'openai' | 'video') => void;
}) {
  return (
    <div className="page-shell api-keys-page">
      <section className="api-security-banner">
        <span><ShieldCheck size={22} /></span>
        <div>
          <strong>API AI được quản lý tập trung</strong>
          <p>Khóa OpenAI và provider video chỉ lưu trên VideoMaker Server. Máy người dùng không nhận khóa và mọi yêu cầu AI đều đi qua gateway của tổ chức.</p>
        </div>
      </section>
      <div className="api-layout">
        <div className="api-provider-stack">
          <section className="card api-provider-card">
            <div className="api-provider-heading">
              <span className="api-brand openai"><img src={openAiLogo} alt="OpenAI" /></span>
              <div><span className="api-eyebrow">CONTENT GENERATION</span><h2>OpenAI</h2><p>Model do quản trị viên tổ chức cấu hình: {settings.openAiModel || 'Chưa chọn'}</p></div>
              <span className={`api-status ${settings.openAiConfigured ? 'configured' : ''}`}>{settings.openAiConfigured ? 'Sẵn sàng' : 'Chưa cấu hình'}</span>
            </div>
            <div className="api-provider-actions"><button type="button" className="api-test-button" disabled={!settings.openAiConfigured || busy} onClick={() => onTest('openai')}><CircleCheck size={16} /> Kiểm tra trạng thái</button></div>
          </section>
          <section className="card api-provider-card">
            <div className="api-provider-heading">
              <span className="api-brand image"><ImageIcon size={24} /></span>
              <div>
                <span className="api-eyebrow">CHARACTER IMAGE</span>
                <h2>GPT-Image-2</h2>
                <p>Model ảnh: {providerStatus.openAiImageModel || 'gpt-image-2'} · PNG 1024×1024 · medium</p>
              </div>
              <span className={`api-status ${providerStatus.openAiImageReady ? 'configured' : ''}`}>
                {providerStatus.openAiImageReady ? 'Sẵn sàng' : 'Chưa sẵn sàng'}
              </span>
            </div>
            <div className={`api-image-readiness ${providerStatus.openAiImageReady ? 'ready' : ''}`}>
              {providerStatus.openAiImageReady ? <CircleCheck size={16} /> : <TriangleAlert size={16} />}
              <div>
                <strong>{providerStatus.openAiImageReady
                  ? 'Có thể tạo ảnh chuẩn nhân vật'
                  : providerStatus.openAiImageUnavailableMessage || 'Cấu hình GPT-Image-2 chưa hoàn tất.'}</strong>
                <p>{providerStatus.openAiImageReady
                  ? `Chi phí dự kiến mỗi ảnh: ${providerStatus.estimatedCharacterImageCost
                    ? formatMoney(providerStatus.estimatedCharacterImageCost, providerStatus.currencyCode ?? 'USD')
                    : 'do server tính theo rate Active'}.`
                  : imageSetupGuidance(providerStatus.openAiImageUnavailableCode)}</p>
              </div>
            </div>
          </section>
          <section className="card api-provider-card">
            <div className="api-provider-heading">
              <span className="api-brand kling"><Film size={24} /></span>
              <div><span className="api-eyebrow">VIDEO GENERATION</span><h2>Provider video</h2><p>Policy chỉ đọc: {settings.videoProviderCode || 'chưa chọn'} / {settings.videoModel || 'chưa chọn model'}</p></div>
              <span className={`api-status ${settings.videoConfigured ? 'configured' : ''}`}>{settings.videoConfigured ? 'Sẵn sàng' : 'Chưa cấu hình'}</span>
            </div>
            <div className="api-provider-actions"><button type="button" className="api-test-button" disabled={!settings.videoConfigured || busy} onClick={() => onTest('video')}><CircleCheck size={16} /> Kiểm tra trạng thái</button></div>
          </section>
        </div>
        <aside className="card api-license-card">
          <span className="api-license-icon"><Crown size={21} /></span><span className="api-eyebrow">LICENSE ACCESS</span><h2>{license?.planName || 'Chưa có gói'}</h2>
          <span className={`license-state ${license?.hasActiveLicense ? 'active' : ''}`}>{license?.hasActiveLicense ? 'Đang hoạt động' : 'Không có hiệu lực'}</span>
          {organization && (
            <dl>
              <div><dt>Tổ chức</dt><dd>{organization.name}</dd></div>
              <div><dt>Vai trò</dt><dd>{organization.role}</dd></div>
              <div><dt>Ngân sách tháng</dt><dd>{formatMoney(organization.monthlyBudgetLimit, organization.currencyCode)}</dd></div>
              <div><dt>Đã sử dụng</dt><dd>{formatMoney(organization.actualCost, organization.currencyCode)}</dd></div>
              <div><dt>Đang giữ chỗ</dt><dd>{formatMoney(organization.reservedCost, organization.currencyCode)}</dd></div>
              <div><dt>Còn lại</dt><dd>{formatMoney(organization.remainingBudget, organization.currencyCode)}</dd></div>
            </dl>
          )}
          <p>License cá nhân và quyền thành viên tổ chức đều phải còn hiệu lực trước khi gateway chấp nhận yêu cầu AI.</p>
        </aside>
      </div>
    </div>
  );
}

function formatMoney(value: number, currencyCode: string) {
  return new Intl.NumberFormat('vi-VN', {
    style: 'currency',
    currency: currencyCode || 'USD',
    maximumFractionDigits: 4
  }).format(value);
}

function StatusBadge({ status }: { status: PipelineStage['status'] }) {
  const labels = { waiting: 'Chờ xử lý', processing: 'Đang xử lý', completed: 'Hoàn thành', failed: 'Thất bại' };
  return <span className={`status-badge status-${status}`}>{status === 'processing' && <LoaderCircle size={11} className="spin" />}{labels[status]}</span>;
}

function ProgressBar({ value }: { value: number }) {
  return <div className="progress-bar"><div className="progress-track"><i style={{ width: `${Math.max(0, Math.min(100, value))}%` }} /></div><span>{Math.round(value)}%</span></div>;
}

function EmptyBlock({ text }: { text: string }) {
  return <div className="empty-block"><Gauge size={24} /><span>{text}</span></div>;
}

function createEmptyStages(): PipelineStage[] {
  return [
    {
      code: 'research',
      title: 'Nghiên cứu viral',
      subtitle: 'Phân tích xu hướng',
      status: 'completed',
      progressPercent: 100,
      detailLines: [
        'Phân tích xu hướng TikTok, YouTube',
        'Từ khóa: sức khỏe, thói quen sáng',
        'Đối tượng: 18–45 tuổi',
        'Độ viral: Cao 🔥'
      ]
    },
    {
      code: 'script',
      title: 'Kịch bản AI',
      subtitle: 'Viết kịch bản hấp dẫn',
      status: 'completed',
      progressPercent: 100,
      detailLines: [
        'Đã tạo kịch bản 7 phần',
        'Thời lượng dự kiến: 01:15',
        'Âm thanh: Provider Native Audio',
        'Cảm xúc: Tích cực'
      ]
    },
    {
      code: 'scenes',
      title: 'Chia cảnh',
      subtitle: 'Tạo danh sách cảnh',
      status: 'processing',
      progressPercent: 67,
      detailLines: ['Tổng số cảnh: 12 cảnh', 'Cảnh đã tạo: 8/12']
    },
    {
      code: 'video',
      title: 'Tạo video',
      subtitle: 'Sinh video từ AI',
      status: 'waiting',
      progressPercent: 0,
      detailLines: [
        'Cần xử lý 12 video clip',
        'Model: Do server quản lý',
        'Độ phân giải: 1080p',
        'Thời lượng: ~75 giây'
      ]
    },
    {
      code: 'render',
      title: 'Ghép video',
      subtitle: 'Hoàn thiện và xuất',
      status: 'waiting',
      progressPercent: 0,
      detailLines: [
        'Thêm hiệu ứng chuyển cảnh',
        'Thêm nhạc nền & phụ đề',
        'Xuất video hoàn chỉnh'
      ]
    }
  ];
}

function createDisplayStages(project: ProjectDashboard | null): PipelineStage[] {
  if (!project) return createEmptyStages();

  return project.pipeline.map((stage) => {
    const duration = formatDuration(project.project.targetDurationSeconds);
    const totalScenes = project.totalScenes;
    const completedScenes = project.approvedScenes;
    const defaultDetails: Record<string, string[]> = {
      research: [
        `Phân tích xu hướng ${formatPlatform(project.project.platform)}`,
        `Chủ đề: ${project.project.topic}`,
        'Đối tượng: Đang phân tích',
        'Độ viral: Chờ đánh giá'
      ],
      script: [
        'Xây dựng cấu trúc kịch bản AI',
        `Thời lượng dự kiến: ${duration}`,
        `Ngôn ngữ nội dung: ${formatLanguage(project.effectiveGenerationLanguageCode ?? project.languageCode)}`,
        'Cảm xúc: Đang xác định'
      ],
      scenes: [
        `Tổng số cảnh: ${totalScenes} cảnh`,
        `Cảnh đã tạo: ${completedScenes}/${totalScenes}`
      ],
      video: [
        `Cần xử lý ${totalScenes} video clip`,
        'Model: Chưa chọn',
        'Độ phân giải: 1080p',
        `Thời lượng: ~${project.project.targetDurationSeconds} giây`
      ],
      render: [
        'Thêm hiệu ứng chuyển cảnh',
        'Thêm nhạc nền & phụ đề',
        'Xuất video hoàn chỉnh'
      ]
    };

    return {
      ...stage,
      detailLines: defaultDetails[stage.code] ?? stage.detailLines
    };
  });
}

function getStageActionLabel(stage: PipelineStage): string {
  if (stage.status === 'failed') return 'Thử lại';
  if (stage.code === 'research') return 'Xem chi tiết';
  if (stage.code === 'script') return 'Xem kịch bản';
  if (stage.code === 'scenes') return 'Xem danh sách';
  if (stage.code === 'video') return stage.status === 'waiting' ? 'Bắt đầu' : 'Xem chi tiết';
  return stage.status === 'waiting' ? 'Chờ xử lý' : 'Xem kết quả';
}

function createDisplayModels(configuredModels: AiModel[]): ModelDisplay[] {
  return configuredModels.map<ModelDisplay>((model) => ({
      id: `${model.providerCode}-${model.modelCode}`,
      name: model.displayName,
      provider: model.providerName,
      description: translateModality(model.modality),
      secondary: model.providerName,
      brand: model.providerCode === 'kling' ? 'kling' : 'generic',
      badge: model.isDefault ? 'Mặc định' : undefined,
      configured: true
    }));
}

function formatDuration(seconds: number): string {
  const value = Math.max(0, seconds || 0);
  const minutes = Math.floor(value / 60);
  return `${String(minutes).padStart(2, '0')}:${String(value % 60).padStart(2, '0')}`;
}

function formatDate(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? '—' : new Intl.DateTimeFormat('vi-VN', { dateStyle: 'short', timeStyle: 'short' }).format(date);
}

function formatDateOnly(value?: string | null): string {
  if (!value) return 'Không giới hạn';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? '—' : new Intl.DateTimeFormat('vi-VN', { dateStyle: 'medium' }).format(date);
}

function translateProjectStatus(status: string): string {
  const normalized = status.toLowerCase();
  if (normalized.includes('complete')) return 'Hoàn thành';
  if (normalized.includes('fail')) return 'Thất bại';
  if (normalized.includes('running') || normalized.includes('processing')) return 'Đang xử lý';
  if (normalized.includes('cancel')) return 'Đã hủy';
  return 'Bản nháp';
}

function translateModality(modality: string): string {
  const normalized = modality.toLowerCase();
  if (normalized.includes('video')) return 'Video AI';
  if (normalized.includes('image')) return 'Hình ảnh AI';
  if (normalized.includes('voice')) return 'Giọng đọc AI';
  return 'Văn bản AI';
}

function formatPlatform(platform: string): string {
  const labels: Record<string, string> = {
    YouTubeShorts: 'YouTube Shorts',
    InstagramReels: 'Instagram Reels'
  };
  return labels[platform] ?? platform;
}

function formatLanguage(languageCode: string): string {
  if (languageCode.toLowerCase().startsWith('vi')) return 'Tiếng Việt';
  if (languageCode.toLowerCase().startsWith('en')) return 'English';
  return languageCode;
}

export default App;
