import { useEffect, useRef, useState } from 'react';
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
import { VietsubPage } from './features/vietsub/VietsubPage';
import { useVietsubModule } from './features/vietsub/useVietsubModule';
import type {
  AiModel,
  CharacterSummary,
  CreateProjectAssetPayload,
  CreateProjectPayload,
  CreateShortVideoPayload,
  DashboardState,
  DesktopRelease,
  DesktopUpdateNotice,
  DesktopUpdateProgress,
  GenerationProviderStatus,
  HostMessage,
  MediaToolStatus,
  OrganizationSummary,
  PipelineStage,
  ProjectDashboard,
  ProjectAssetLibrary,
  ProjectAssetSummary as ProjectTextAsset,
  ProjectAssetType,
  ProjectSummary,
  ProviderSettings,
  SceneSummary,
  UpdateScenePayload,
  UpdateCharacterPayload,
  UpdateProjectAssetPayload,
} from './types';

type Page = 'create' | 'longVideo' | 'shortVideo' | 'projects' | 'vietsub' | 'apiKeys';
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
    vietsubEnabled: false
  }
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
  { label: 'Cài đặt', icon: Settings },
  { label: 'Thanh toán', icon: CreditCard },
  { label: 'Hướng dẫn', icon: CircleHelp }
];

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
  const [page, setPage] = useState<Page>('create');
  const [busy, setBusy] = useState(true);
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const [toasts, setToasts] = useState<Toast[]>([]);
  const [updateNotice, setUpdateNotice] = useState<DesktopUpdateNotice | null>(null);
  const [updateProgress, setUpdateProgress] = useState<DesktopUpdateProgress | null>(null);
  const [updateError, setUpdateError] = useState<string | null>(null);
  const [providerSettings, setProviderSettings] = useState<ProviderSettings | null>(null);
  const [confirmation, setConfirmation] = useState<ConfirmationRequest | null>(null);
  const [serviceError, setServiceError] = useState<ServiceError | null>(null);
  const [characterImageBusyId, setCharacterImageBusyId] = useState<string | null>(null);
  const [assetConfirmBusyId, setAssetConfirmBusyId] = useState<string | null>(null);
  const [mediaInstallProgress, setMediaInstallProgress] = useState<DesktopUpdateProgress | null>(null);
  const [sceneSaveState, setSceneSaveState] = useState<SceneSaveState | null>(null);
  const [shortVideoProjectId, setShortVideoProjectId] = useState<string | null>(null);
  const pendingSceneSaveRef = useRef<PendingSceneSave | null>(null);
  const vietsub = useVietsubModule(dashboard.features.vietsubEnabled);

  const notify = (message: string, error = false) => {
    const id = Date.now();
    setToasts((current) => [...current, { id, message, error }]);
    window.setTimeout(() => setToasts((current) => current.filter((item) => item.id !== id)), 3600);
  };

  useEffect(() => {
    const unsubscribe = subscribeToHost((message: HostMessage) => {
      if (message.type === 'dashboard.state' && message.payload) {
        const nextDashboard = message.payload as DashboardState;
        setDashboard({
          ...nextDashboard,
          features: nextDashboard.features ?? { vietsubEnabled: false }
        });
        if (!nextDashboard.generationRunning) setCharacterImageBusyId(null);
        setAssetConfirmBusyId(null);
        setBusy(false);
        return;
      }

      if (message.type === 'short-video.started' && message.payload) {
        const payload = message.payload as { projectId?: string };
        if (payload.projectId) setShortVideoProjectId(payload.projectId);
        return;
      }

      if (message.type === 'operation.error') {
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
        notify(message.error?.message ?? 'Không thể hoàn tất thao tác.', true);
        return;
      }

      if (message.type === 'operation.notice') {
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

      if (message.type === 'license.invalidated') {
        setDashboard((current) => ({ ...current, license: null }));
        notify(String((message.payload as { message?: string })?.message ?? 'License không còn hiệu lực.'), true);
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

  const handleNavigation = (label: string, target?: Page) => {
    setSidebarOpen(false);
    if (target) {
      setPage(target);
      if (target === 'apiKeys') postToHost('providers.settings.get');
      return;
    }

    notify(`${label} đang được phát triển.`);
  };

  const selectProject = (projectId: string) => {
    setBusy(true);
    postToHost('project.select', { projectId });
    if (page !== 'create') setPage('longVideo');
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
    setBusy(true);
    postToHost('generation.content');
  };

  const renderFinalVideo = () => {
    const project = dashboard.selectedProject;
    if (!project || dashboard.generationRunning) return;
    const silentOutput = project.audioStrategy === 'SilentOutput';
    if (!dashboard.mediaTools.ready) {
      notify(dashboard.mediaTools.message || 'FFmpeg và FFprobe chưa sẵn sàng.', true);
      return;
    }
    if (project.totalScenes === 0 || project.approvedScenes !== project.totalScenes) {
      notify(silentOutput
        ? 'Hãy hoàn tất tất cả clip trước khi dựng video cuối.'
        : 'Hãy nghe và duyệt Native Audio của tất cả cảnh trước khi dựng video cuối.', true);
      return;
    }
    setConfirmation({
      eyebrow: 'XÁC NHẬN DỰNG VIDEO CUỐI',
      title: project.preview?.url ? 'Dựng lại video hoàn chỉnh?' : 'Dựng video hoàn chỉnh?',
      description: silentOutput
        ? `${project.totalScenes} clip SceneVideo không âm thanh sẽ được ghép đúng thứ tự. Video đầu ra sẽ không chứa audio stream.`
        : `${project.totalScenes} clip SceneVideo đã duyệt sẽ được ghép đúng thứ tự. Âm thanh Native Audio của provider được giữ nguyên; hệ thống không tạo hoặc chèn thêm giọng TTS.`,
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

  const generateVideos = (sceneIds: string[]) => {
    const project = dashboard.selectedProject;
    if (!project || dashboard.generationRunning || sceneIds.length === 0) return;
    if (project.requiresVietnameseContentRegeneration) {
      notify('Dự án Kling này còn nội dung tiếng Anh. Hãy sinh lại nội dung tiếng Việt trước khi tạo clip.', true);
      return;
    }
    if (!dashboard.mediaTools.ready) {
      notify(dashboard.mediaTools.message || 'FFmpeg và FFprobe chưa sẵn sàng.', true);
      return;
    }
    const selectedSceneIds = new Set(sceneIds);
    const enforceKlingLongFormSpeechPolicy = project.workflowStructureType === 'OpenAiStructuredPlan' &&
      project.videoProviderCode?.toLowerCase() === 'kling';
    const selectedScenes = project.scenes.filter((scene) => selectedSceneIds.has(scene.sceneId));
    if (selectedScenes.length !== selectedSceneIds.size) {
      notify('Danh sách cảnh đã thay đổi. Hãy chọn lại cảnh cần tạo.', true);
      return;
    }
    const resumableScenes = selectedScenes.filter(sceneNeedsLocalCompletion);
    const newRequestScenes = selectedScenes.filter((scene) => !sceneNeedsLocalCompletion(scene));
    const blockedAssetScenes = newRequestScenes.filter(
      (scene) => !areSceneAssetsReady(scene.sceneId, dashboard.assetLibrary ?? null)
    );
    if (blockedAssetScenes.length > 0) {
      const hasInvalidSelection = blockedAssetScenes.some((scene) =>
        dashboard.assetLibrary?.sceneAssignments.find((assignment) => assignment.sceneId === scene.sceneId)?.isValid === false);
      notify(
        hasInvalidSelection
          ? `Cảnh ${blockedAssetScenes.map((scene) => scene.sequenceNumber).join(', ')} có lựa chọn tài sản không hợp lệ. Hãy sửa trong Storyboard trước khi tạo clip.`
          : `Cảnh ${blockedAssetScenes.map((scene) => scene.sequenceNumber).join(', ')} đang dùng tài sản text chưa khóa. Hãy duyệt và khóa tài sản trước khi tạo clip.`,
        true
      );
      return;
    }
    const isDownloadOnly = resumableScenes.length === selectedScenes.length;
    const isMixedOperation = resumableScenes.length > 0 && newRequestScenes.length > 0;
    const totalSeconds = Math.ceil(selectedScenes.reduce((total, scene) => total + scene.durationMs, 0) / 1000);
    const newRequestSeconds = Math.ceil(newRequestScenes.reduce(
      (total, scene) => total + (scene.generationDurationMs ?? scene.durationMs),
      0) / 1000);
    const spokenSceneCount = selectedScenes.filter((scene) => scene.speechMode !== 'None').length;
    const spokenPreview = selectedScenes
      .map((scene) => {
        const durationSeconds = Math.ceil(scene.durationMs / 1000);
        const speech = scene.speechMode === 'None'
          ? 'Không có lời nói'
          : `${speechModeLabel(scene.speechMode, enforceKlingLongFormSpeechPolicy)}: “${scene.narration?.trim() || 'chưa có nội dung'}”`;
        const operation = sceneNeedsLocalCompletion(scene) ? 'Tải clip đã tạo' : 'Tạo clip mới';
        return `${isMixedOperation ? `${operation} · ` : ''}Cảnh ${scene.sequenceNumber} (${durationSeconds}s) — ${speech}`;
      })
      .join('\n');
    const retryCount = newRequestScenes.filter((scene) => scene.status === 'NativeAudioInvalid').length;
    const estimatedVideoCost = newRequestSeconds > 0 && dashboard.providerStatus.estimatedVideoCostPerSecond
      ? dashboard.providerStatus.estimatedVideoCostPerSecond * newRequestSeconds
      : null;
    const costNote = estimatedVideoCost
      ? `Chi phí ước tính ${formatMoney(estimatedVideoCost, dashboard.providerStatus.currencyCode ?? 'USD')} theo rate Active hiện tại; server sẽ quote và giữ budget chính xác trước outbound.`
      : 'Chi phí được server quote theo rate Active và giữ trong budget tổ chức trước outbound.';
    const languageNote = ' Bạn phải nghe và duyệt từng clip trước khi dựng video cuối.';
    const retryNote = retryCount > 0
      ? ` ${retryCount} clip có Native Audio không đạt sẽ được tạo lại bằng prompt ưu tiên lời thoại và phát sinh chi phí provider mới.`
      : '';
    const providerLabel = dashboard.providerStatus.videoProviderName ?? dashboard.providerStatus.videoProviderCode ?? 'Provider do server chọn';

    if (isDownloadOnly) {
      setConfirmation({
        intent: 'download',
        noteTone: 'info',
        eyebrow: 'XÁC NHẬN TẢI CLIP',
        title: `Tải ${selectedScenes.length} clip đã tạo về máy?`,
        description: `Video đã hoàn thành trên server và đang chờ lưu về máy.\n${providerLabel} · ${dashboard.providerStatus.videoModel ?? 'Model theo policy'} · ${dashboard.providerStatus.videoResolution ?? '720p'} · Native Audio\nTổng thời lượng: khoảng ${totalSeconds} giây\n\n${spokenPreview}`,
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
      setConfirmation({
        eyebrow: 'XÁC NHẬN TẢI VÀ TẠO CLIP',
        title: `Tiếp tục xử lý ${selectedScenes.length} clip video?`,
        description: `${providerLabel} · ${dashboard.providerStatus.videoModel ?? 'Model theo policy'} · ${dashboard.providerStatus.videoResolution ?? '720p'} · Native Audio\n${resumableScenes.length} clip sẽ dùng lại video đã có; ${newRequestScenes.length} clip sẽ được tạo mới. Phần tạo mới dài khoảng ${newRequestSeconds} giây.\n\n${spokenPreview}`,
        note: `${resumableScenes.length} clip tải lại không phát sinh chi phí provider mới. ${costNote}${languageNote}${retryNote}`,
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
        : `Tạo ${selectedScenes.length} clip video?`,
      description: `${providerLabel} · ${dashboard.providerStatus.videoModel ?? 'Model theo policy'} · ${dashboard.providerStatus.videoResolution ?? '720p'} · Native Audio\nTổng thời lượng: khoảng ${totalSeconds} giây · ${spokenSceneCount}/${selectedScenes.length} cảnh có lời nói\n\n${spokenPreview}`,
      note: `${costNote}${languageNote}${retryNote}`,
      confirmLabel: retryCount === selectedScenes.length
        ? `Tạo lại ${selectedScenes.length} clip`
        : `Tạo ${selectedScenes.length} clip`,
      onConfirm: () => {
        setBusy(true);
        postToHost('generation.video', { sceneIds });
      }
    });
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

  const approveSceneNativeAudio = (sceneId: string, playbackConfirmed: boolean) => {
    if (!dashboard.selectedProject || dashboard.generationRunning) return;
    setBusy(true);
    postToHost('scene.native-audio.approve', { sceneId, playbackConfirmed });
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

  const generationBusy = busy || dashboard.generationRunning;
  const pageBusy = page === 'vietsub'
    ? vietsub.state.loading || vietsub.state.busy
    : generationBusy;
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

  return (
    <div className="app-shell">
      <Sidebar
        dashboard={dashboard}
        page={page}
        open={sidebarOpen}
        onClose={() => setSidebarOpen(false)}
        onNavigate={handleNavigation}
        onLogout={() => postToHost('auth.logout')}
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
          onSelectOrganization={(organizationId) => {
            setShortVideoProjectId(null);
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
            onImportSrt={vietsub.importSrt}
            onActivateSubtitleTrack={vietsub.activateSubtitleTrack}
            onLoadSubtitlePage={vietsub.loadSubtitlePage}
            onUpdateSubtitleCue={vietsub.updateSubtitleCue}
            onSplitSubtitleCue={vietsub.splitSubtitleCue}
            onAlignSubtitleCue={vietsub.alignSubtitleCue}
            onDuplicateSubtitleCue={vietsub.duplicateSubtitleCue}
            onDeleteSubtitleCue={vietsub.deleteSubtitleCue}
            onExportSrt={vietsub.exportSrt}
          />
        ) : page === 'projects' ? (
          <ProjectsPage projects={dashboard.projects} onSelect={selectProject} onCreate={() => setPage('longVideo')} />
        ) : page === 'shortVideo' ? (
          <ShortVideoPage
            project={dashboard.selectedProject?.project.projectId === shortVideoProjectId
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
        ) : page === 'longVideo' ? (
          <LongVideoPage
            project={dashboard.selectedProject ?? null}
            assetLibrary={dashboard.assetLibrary ?? null}
            models={dashboard.models}
            providerStatus={dashboard.providerStatus}
            mediaTools={dashboard.mediaTools}
            busy={generationBusy}
            onCreate={createProject}
            onGenerateContent={generateContent}
            onRegenerateContent={requestContentRegeneration}
            onGenerateVideo={generateVideos}
            onRenderFinalVideo={renderFinalVideo}
            onApproveSceneNativeAudio={approveSceneNativeAudio}
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
            models={dashboard.models}
            providerStatus={dashboard.providerStatus}
            mediaTools={dashboard.mediaTools}
            busy={generationBusy}
            onCreate={createProject}
            onGenerateContent={generateContent}
            onRegenerateContent={requestContentRegeneration}
            onGenerateVideo={generateVideos}
            onRenderFinalVideo={renderFinalVideo}
            onApproveSceneNativeAudio={approveSceneNativeAudio}
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
  onClose,
  onNavigate,
  onLogout,
  onUnavailable
}: {
  dashboard: DashboardState;
  page: Page;
  open: boolean;
  onClose: () => void;
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
      <aside className={`sidebar ${open ? 'open' : ''}`}>
        <div className="brand">
          <div className="brand-mark"><Clapperboard size={25} /></div>
          <div><strong>VideoMaker</strong><span>Tự động tạo video</span></div>
        </div>

        <button className="new-video-button" onClick={() => onNavigate('Tạo Video Dài', 'longVideo')}>
          <Plus size={18} /> Tạo video mới
        </button>

        <nav className="sidebar-nav">
          {primaryMenu
            .filter(({ feature }) => !feature || dashboard.features[feature])
            .map(({ label, icon: Icon, page: target }) => (
              <button
                className={target === page ? 'active' : ''}
                key={label}
                onClick={() => onNavigate(label, target)}
              >
                <Icon size={18} /><span>{label}</span>
              </button>
            ))}
          <div className="nav-divider" />
          {secondaryMenu.map(({ label, icon: Icon, page: target }) => (
            <button className={target === page ? 'active' : ''} key={label} onClick={() => onNavigate(label, target)}>
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
          <button className="profile-action" onClick={onLogout} title="Đăng xuất"><LogOut size={17} /></button>
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
      {page !== 'apiKeys' && page !== 'shortVideo' && page !== 'vietsub' && dashboard.projects.length > 0 && (
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
      <section className="short-video-hero">
        <div className="short-video-hero-icon"><Film size={28} /></div>
        <div>
          <span className="short-video-eyebrow">KLING QUICK CREATE</span>
          <h2>Một nội dung, một clip ngắn theo ý bạn</h2>
          <p>Nội dung được dùng trực tiếp làm prompt hình ảnh. VideoMaker không gọi OpenAI và không tự thêm lời thoại.</p>
        </div>
        <div className="short-video-fixed-badge"><Clock3 size={15} /> Chọn từ 5–15 giây</div>
      </section>

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
  models,
  providerStatus,
  mediaTools,
  busy,
  onCreate,
  onGenerateContent,
  onRegenerateContent,
  onGenerateVideo,
  onRenderFinalVideo,
  onApproveSceneNativeAudio,
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
  models: AiModel[];
  providerStatus: GenerationProviderStatus;
  mediaTools: MediaToolStatus;
  busy: boolean;
  onCreate: (payload: CreateProjectPayload) => void;
  onGenerateContent: () => void;
  onRegenerateContent: () => void;
  onGenerateVideo: (sceneIds: string[]) => void;
  onRenderFinalVideo: () => void;
  onApproveSceneNativeAudio: (sceneId: string, playbackConfirmed: boolean) => void;
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
          <CreateVideoCard busy={busy} onCreate={onCreate} />
          <GenerationActions
            project={project}
            providerStatus={providerStatus}
            busy={busy}
            onGenerateContent={onGenerateContent}
            onRegenerateContent={onRegenerateContent}
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
            onUnavailable={onUnavailable}
          />
        </aside>
      </div>
    </div>
  );
}

function LongVideoPage({
  project,
  assetLibrary,
  models,
  providerStatus,
  mediaTools,
  busy,
  onCreate,
  onGenerateContent,
  onRegenerateContent,
  onGenerateVideo,
  onRenderFinalVideo,
  onApproveSceneNativeAudio,
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
  assetLibrary: ProjectAssetLibrary | null;
  models: AiModel[];
  providerStatus: GenerationProviderStatus;
  mediaTools: MediaToolStatus;
  busy: boolean;
  onCreate: (payload: CreateProjectPayload) => void;
  onGenerateContent: () => void;
  onRegenerateContent: () => void;
  onGenerateVideo: (sceneIds: string[]) => void;
  onRenderFinalVideo: () => void;
  onApproveSceneNativeAudio: (sceneId: string, playbackConfirmed: boolean) => void;
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
  const [activeStep, setActiveStep] = useState<LongVideoStepId>(suggestedStep);
  const [assetTab, setAssetTab] = useState<'characters' | ProjectAssetType>('characters');

  useEffect(() => {
    setActiveStep(getSuggestedLongVideoStep(project));
  }, [projectId]);

  const activeStepIndex = longVideoSteps.findIndex((step) => step.id === activeStep);
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
        <CreateVideoCard busy={busy} onCreate={onCreate} />
        <ModelsSection models={models} />
      </>;
    }

    if (activeStep === 'content') {
      return <>
        <LongVideoContentSummary project={project} providerStatus={providerStatus} />
        {project && <LongVideoContentScenes scenes={project.scenes} />}
        <GenerationActions
          project={project}
          providerStatus={providerStatus}
          busy={busy}
          onGenerateContent={onGenerateContent}
          onRegenerateContent={onRegenerateContent}
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
        providerStatus={providerStatus}
        mediaTools={mediaTools}
        busy={busy}
        onGenerateVideo={onGenerateVideo}
        onApproveNativeAudio={onApproveSceneNativeAudio}
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
        onUnavailable={onUnavailable}
      />
    </>;
  };

  return (
    <div className="page-shell long-video-page">
      <section className="long-video-workspace-header">
        <div className="long-video-workspace-copy">
          <span className="long-video-eyebrow">LONG-FORM STUDIO</span>
          <div><h2>{project?.project.name ?? 'Workspace video nhiều cảnh'}</h2>{project && <span className="long-video-project-status">{translateProjectStatus(project.project.status)}</span>}</div>
          <p>{project ? project.project.topic : 'Bắt đầu từ chủ đề, phát triển kịch bản, chuẩn hóa nhân vật và dựng video theo từng cảnh.'}</p>
        </div>
        <div className="long-video-workspace-meta">
          <span><strong>{project?.totalScenes ?? 0}</strong>Cảnh</span>
          <span><strong>{project?.approvedScenes ?? 0}</strong>Đã duyệt</span>
          <span><strong>{Math.round(project?.overallProgressPercent ?? 0)}%</strong>Tiến độ</span>
        </div>
      </section>

      <nav className="long-video-stepper" aria-label="Quy trình tạo video dài">
        {longVideoSteps.map((step, index) => {
          const Icon = step.icon;
          const available = isLongVideoStepAvailable(step.id, project);
            const completed = isLongVideoStepCompleted(step.id, project);
          return (
            <button
              key={step.id}
              className={`${activeStep === step.id ? 'active' : ''} ${completed ? 'completed' : ''}`}
              disabled={!available}
              onClick={() => setActiveStep(step.id)}
              aria-current={activeStep === step.id ? 'step' : undefined}
            >
              <span className="long-video-step-number">{completed ? <Check size={15} strokeWidth={3} /> : <Icon size={16} />}</span>
              <span className="long-video-step-copy"><small>Bước {index + 1}</small><strong>{step.shortLabel}</strong></span>
              {index < longVideoSteps.length - 1 && <span className="long-video-step-line" />}
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
  if (step === 'content') return project.totalScenes > 0;
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

  return (
    <section className="card long-video-content-summary">
      <div className="long-video-summary-heading"><div><span>NỘI DUNG HIỆN HÀNH</span><h3>{project.project.topic}</h3></div><strong className={project.totalScenes > 0 ? 'ready' : 'draft'}>{project.totalScenes > 0 ? 'Đã có scene plan' : 'Chờ sinh nội dung'}</strong></div>
      <div className="long-video-summary-grid">
        <div><small>Ngôn ngữ nội dung</small><strong>{project.effectiveGenerationLanguageCode?.toLowerCase().startsWith('vi') && project.videoProviderCode?.toLowerCase() === 'kling'
          ? 'Tiếng Việt (bắt buộc cho Video Dài dùng Kling)'
          : formatLanguage(project.effectiveGenerationLanguageCode ?? project.languageCode)}</strong></div>
        <div><small>Thời lượng mục tiêu</small><strong>{formatDuration(project.project.targetDurationSeconds)}</strong></div>
        <div><small>Số cảnh</small><strong>{project.totalScenes}</strong></div>
        <div><small>OpenAI model</small><strong>{providerStatus.openAiModel ?? 'Chưa cấu hình'}</strong></div>
      </div>
      {project.requiresVietnameseContentRegeneration && (
        <p className="long-video-language-warning">Nội dung hiện tại còn tiếng Anh và sẽ bị chặn trước khi gọi Kling. Hãy dùng hành động “Sinh lại nội dung tiếng Việt”.</p>
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
  const ready = totalScenes > 0 && approvedScenes === totalScenes && mediaTools.ready;
  return (
    <section className="card long-video-export-overview">
      <div className={`long-video-export-icon ${ready ? 'ready' : ''}`}>{ready ? <CircleCheck size={25} /> : <Clapperboard size={25} />}</div>
      <div><span>KIỂM TRA TRƯỚC KHI XUẤT</span><h3>{ready ? 'Dự án đã sẵn sàng để dựng video cuối' : 'Hoàn tất các điều kiện còn thiếu'}</h3><p>{totalScenes > 0 ? `Đã duyệt ${approvedScenes}/${totalScenes} cảnh. ${mediaTools.ready ? 'FFmpeg và FFprobe đã sẵn sàng.' : mediaTools.message}` : 'Dự án chưa có cảnh để dựng video.'}</p></div>
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
      <div><span><Users size={15} /> Nhân vật</span><strong className={charactersReady ? 'ready' : 'waiting'}>{characterLabel}</strong></div>
      <div><span><Clapperboard size={15} /> FFmpeg</span><strong className={mediaTools.ready ? 'ready' : 'missing'}>{mediaTools.ready ? 'Sẵn sàng' : 'Cần kiểm tra'}</strong></div>
    </section>
  );
}

function GenerationActions({
  project,
  providerStatus,
  busy,
  onGenerateContent,
  onRegenerateContent
}: {
  project: ProjectDashboard | null;
  providerStatus: GenerationProviderStatus;
  busy: boolean;
  onGenerateContent: () => void;
  onRegenerateContent: () => void;
}) {
  if (!project) return null;
  const hasContent = project.totalScenes > 0;
  if (hasContent && !project.requiresVietnameseContentRegeneration) return null;
  const requiresVietnamese = project.requiresVietnameseContentRegeneration;

  return (
    <section className="card generation-actions">
      <div>
        <span className="generation-eyebrow">API GENERATION</span>
        <h2>{requiresVietnamese ? 'Sinh lại nội dung tiếng Việt' : 'Tạo nội dung và prompt'}</h2>
        <p>{requiresVietnamese
          ? 'Dự án video dài dùng Kling còn dữ liệu tiếng Anh. OpenAI cần tạo một version tiếng Việt mới trước khi sinh clip.'
          : 'OpenAI sẽ viết hook, kịch bản, chia cảnh và tạo prompt có cấu trúc.'}</p>
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
        onClick={requiresVietnamese ? onRegenerateContent : onGenerateContent}
      >
        {busy ? <LoaderCircle className="spin" size={18} /> : <WandSparkles size={18} />}
        {requiresVietnamese ? 'Sinh lại nội dung tiếng Việt' : 'Tạo nội dung & chia cảnh'}
      </button>
    </section>
  );
}

function CharacterSection({
  project,
  providerStatus,
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
  busy: boolean;
  imageBusy: boolean;
  onUpdate: (payload: UpdateCharacterPayload) => void;
  onSelectReference: (characterId: string) => void;
  onGenerateReference: (character: CharacterSummary) => void;
  onApprove: (characterId: string) => void;
  onOpenImageSetup: () => void;
}) {
  const [editing, setEditing] = useState(false);
  const [name, setName] = useState(character.name);
  const [role, setRole] = useState(character.role ?? '');
  const [visualIdentity, setVisualIdentity] = useState(character.visualIdentity);
  const [wardrobe, setWardrobe] = useState(character.wardrobe);
  const [immutableTraits, setImmutableTraits] = useState(character.immutableTraits.join('\n'));
  const [forbiddenChanges, setForbiddenChanges] = useState(character.forbiddenChanges.join('\n'));
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
      forbiddenChanges: parseCharacterRules(forbiddenChanges)
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
  providerStatus,
  mediaTools,
  busy,
  onGenerateVideo,
  onApproveNativeAudio,
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
  providerStatus: GenerationProviderStatus;
  mediaTools: MediaToolStatus;
  busy: boolean;
  onGenerateVideo: (sceneIds: string[]) => void;
  onApproveNativeAudio: (sceneId: string, playbackConfirmed: boolean) => void;
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
  const filteredScenes = scenes.filter((scene) => matchesStoryboardFilter(scene, filter));
  const selectableScenes = scenes.filter(
    (scene) => canQueueScene(scene) && (sceneNeedsLocalCompletion(scene) || areSceneAssetsReady(scene.sceneId, assetLibrary))
  );
  const visibleSelectableScenes = filteredScenes.filter(
    (scene) => canQueueScene(scene) && (sceneNeedsLocalCompletion(scene) || areSceneAssetsReady(scene.sceneId, assetLibrary))
  );
  const selectableKey = selectableScenes.map((scene) => `${scene.sceneId}:${scene.status}`).join('|');
  const enforceKlingLongFormSpeechPolicy = project?.workflowStructureType === 'OpenAiStructuredPlan' &&
    project?.videoProviderCode?.toLowerCase() === 'kling';

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
  const selectedDownloadCount = selectedScenes.filter(sceneNeedsLocalCompletion).length;
  const selectedCreateCount = selectedScenes.length - selectedDownloadCount;
  const downloadOnlySelection = selectedDownloadCount > 0 && selectedCreateCount === 0;
  const selectionActionLabel = selectedIds.length === 0
    ? 'Chọn cảnh để xử lý'
    : downloadOnlySelection
      ? `Tải ${selectedDownloadCount} clip đã tạo`
      : selectedDownloadCount > 0
        ? `Xử lý ${selectedIds.length} clip`
        : `Tạo ${selectedCreateCount} clip video`;
  const completedScenes = scenes.filter(isSceneCompleted).length;
  const totalDurationSeconds = Math.ceil(scenes.reduce((total, scene) => total + scene.durationMs, 0) / 1000);
  const allSelected = visibleSelectableScenes.length > 0 && visibleSelectableScenes.every(
    (scene) => selectedSceneIds.has(scene.sceneId)
  );

  const selectFilter = (nextFilter: StoryboardFilter) => {
    setFilter(nextFilter);
    const nextVisibleIds = scenes
      .filter((scene) => matchesStoryboardFilter(scene, nextFilter) && canQueueScene(scene))
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
        <div className="storyboard-provider-state">
          <span className={providerStatus.openAiReady ? 'ready' : 'missing'}>
            OpenAI · {providerStatus.openAiReady ? providerStatus.openAiModel : 'chưa sẵn sàng'}
          </span>
          <span className={providerStatus.videoReady ? 'ready' : 'missing'}>
            Video · {providerStatus.videoReady ? `${providerStatus.videoProviderName ?? providerStatus.videoProviderCode} / ${providerStatus.videoModel}` : 'chưa sẵn sàng'}
          </span>
          <span className={providerStatus.videoReady ? 'ready' : 'missing'}>
            Native Audio · {providerStatus.videoReady && providerStatus.videoNativeAudio ? 'bật theo policy server' : 'chưa sẵn sàng'}
          </span>
          <span className={mediaTools.ready ? 'ready' : 'missing'}>
            Media · {mediaTools.ready ? 'FFmpeg sẵn sàng' : 'chưa cấu hình'}
          </span>
          <span className="ready">Âm thanh · nghe và duyệt từng cảnh</span>
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
            disabled={busy || !providerStatus.videoReady || !mediaTools.ready || selectedIds.length === 0}
            onClick={() => onGenerateVideo(selectedIds)}
          >
            {busy
              ? <LoaderCircle className="spin" size={17} />
              : downloadOnlySelection
                ? <Download size={17} />
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
            selected={selectedSceneIds.has(scene.sceneId)}
            busy={busy}
            videoReady={providerStatus.videoReady}
            mediaToolsReady={mediaTools.ready}
            enforceKlingLongFormSpeechPolicy={enforceKlingLongFormSpeechPolicy}
            onToggle={() => toggleScene(scene.sceneId)}
            onGenerate={() => onGenerateVideo([scene.sceneId])}
            onApproveNativeAudio={(playbackConfirmed) => onApproveNativeAudio(scene.sceneId, playbackConfirmed)}
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
  selected,
  busy,
  videoReady,
  mediaToolsReady,
  enforceKlingLongFormSpeechPolicy,
  onToggle,
  onGenerate,
  onApproveNativeAudio,
  onUpdate,
  saveState,
  onClearSaveFailure,
  onUpdateAssets,
  onConfirmAssets,
  confirmingAssets
}: {
  scene: SceneSummary;
  assetLibrary: ProjectAssetLibrary | null;
  selected: boolean;
  busy: boolean;
  videoReady: boolean;
  mediaToolsReady: boolean;
  enforceKlingLongFormSpeechPolicy: boolean;
  onToggle: () => void;
  onGenerate: () => void;
  onApproveNativeAudio: (playbackConfirmed: boolean) => void;
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
  const [previewPlaybackConfirmed, setPreviewPlaybackConfirmed] = useState(false);
  const [speechContentConfirmed, setSpeechContentConfirmed] = useState(false);
  const [speakerConfirmed, setSpeakerConfirmed] = useState(false);
  const [lipSyncConfirmed, setLipSyncConfirmed] = useState(false);
  const [assigningAssets, setAssigningAssets] = useState(false);
  const [assignmentSaving, setAssignmentSaving] = useState(false);
  const assignedAssetIds = sceneAssignedAssetIds(scene.sceneId, assetLibrary);
  const [draftAssetIds, setDraftAssetIds] = useState<Set<string>>(new Set(assignedAssetIds));
  const assetAssignment = assetLibrary?.sceneAssignments.find((assignment) => assignment.sceneId === scene.sceneId);
  const assignedAssets = assetLibrary?.assets.filter((asset) => assignedAssetIds.includes(asset.projectAssetId)) ?? [];
  const assignedAssetsReady = (assetAssignment?.isValid ?? true) &&
    !(assetAssignment?.hasUnlockedAssets ?? false) &&
    assignedAssets.every((asset) => asset.status === 'Locked');
  const assetUiState = assetAssignment?.isValid === false
    ? 'invalid'
    : assignedAssetsReady
      ? 'ready'
      : 'pending';
  const draftAssets = assetLibrary?.assets.filter((asset) => draftAssetIds.has(asset.projectAssetId)) ?? [];
  const draftBackgroundCount = draftAssets.filter((asset) => asset.assetType === 'Background').length;
  const draftAssetsValid = draftAssetIds.size === 0 || draftBackgroundCount === 1;
  const assetsChanged = assignedAssetIds.length !== draftAssetIds.size || assignedAssetIds.some((id) => !draftAssetIds.has(id));
  const status = sceneStatus(scene);
  const selectable = canQueueScene(scene) && (sceneNeedsLocalCompletion(scene) || assignedAssetsReady);
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

  useEffect(() => {
    setPreviewPlaybackConfirmed(false);
    setSpeechContentConfirmed(false);
    setSpeakerConfirmed(false);
    setLipSyncConfirmed(false);
  }, [scene.sceneId, scene.preview?.url]);
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
            <label className="scene-selector" title="Chọn cảnh để tạo video">
              <input type="checkbox" checked={selected} disabled={busy} onChange={onToggle} />
              <span><Check size={12} /></span>
            </label>
          )}
        </div>
      </header>

      <div className="scene-card-body">
        <div className="scene-media">
          {scene.preview?.url ? (
            <video
              src={scene.preview.url}
              controls
              preload="metadata"
              aria-label={`Video cảnh ${scene.sequenceNumber}`}
              onPlay={() => setPreviewPlaybackConfirmed(true)}
            />
          ) : (
            <div className="scene-placeholder">
              <ImageIcon size={31} />
              <strong>Chưa có thumbnail</strong>
              <small>Clip hoàn thành sẽ hiển thị tại đây</small>
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
            {scene.requiresAudioReview && (
              <div className="scene-audio-review">
                <div>
                  <strong><Volume2 size={15} /> Cần nghe và duyệt Native Audio</strong>
                  <span>
                    {scene.speechMode === 'None'
                      ? 'Hãy kiểm tra âm thanh môi trường và hiệu ứng có phù hợp với hình ảnh.'
                      : `Hãy kiểm tra lời nói đúng nguyên văn, đúng người nói và khớp khẩu hình. Hệ thống đã phát hiện track âm thanh${scene.nativeAudioAudible ? ' có tín hiệu nghe được' : ''}.`}
                  </span>
                  {!previewPlaybackConfirmed && <span>Hãy bấm phát clip ít nhất một lần để mở nút duyệt.</span>}
                  {scene.speechMode !== 'None' && (
                    <div className="scene-audio-review-checklist">
                      <label><input type="checkbox" checked={speechContentConfirmed} disabled={busy} onChange={(event) => setSpeechContentConfirmed(event.target.checked)} /> Tôi đã nghe rõ đủ câu và đúng nguyên văn.</label>
                      <label><input type="checkbox" checked={speakerConfirmed} disabled={busy} onChange={(event) => setSpeakerConfirmed(event.target.checked)} /> {scene.speechMode === 'OnCameraDialogue' ? 'Đúng nhân vật trên màn hình đang nói.' : 'Đúng là lời dẫn ngoài khung hình; không có nhân vật nói trực tiếp.'}</label>
                      <label><input type="checkbox" checked={lipSyncConfirmed} disabled={busy} onChange={(event) => setLipSyncConfirmed(event.target.checked)} /> {scene.speechMode === 'OnCameraDialogue' ? 'Khẩu hình và biểu cảm chấp nhận được.' : 'Giọng dẫn và hình ảnh đồng bộ, chấp nhận được.'}</label>
                    </div>
                  )}
                </div>
                <button
                  type="button"
                  disabled={busy || !scene.canApproveNativeAudio || !previewPlaybackConfirmed ||
                    (scene.speechMode !== 'None' && (!speechContentConfirmed || !speakerConfirmed || !lipSyncConfirmed))}
                  onClick={() => onApproveNativeAudio(
                    previewPlaybackConfirmed &&
                    (scene.speechMode === 'None' || (speechContentConfirmed && speakerConfirmed && lipSyncConfirmed)))}
                >
                  <CircleCheck size={14} /> Duyệt hình và âm thanh
                </button>
              </div>
            )}
            {scene.characterSetupMessage && (
              <div className="scene-character-warning"><TriangleAlert size={14} /> {scene.characterSetupMessage}</div>
            )}
            {enforceKlingLongFormSpeechPolicy && scene.status === 'NativeAudioInvalid' && (
              <div className="scene-character-warning"><Volume2 size={14} /> Lần tạo trước không có lời nghe được. Lần thử tiếp theo sẽ dùng prompt ưu tiên lời thoại và là một request có phí mới.</div>
            )}
            <div className="scene-actions">
              {scene.canEdit && (
                <button type="button" className="scene-edit" disabled={busy} onClick={beginEdit}><Pencil size={14} /> Chỉnh sửa</button>
              )}
              {selectable && (
                <button type="button" className="scene-generate-one" disabled={busy || !videoReady || !mediaToolsReady} onClick={onGenerate}>
                  {sceneNeedsLocalCompletion(scene) ? <Download size={14} /> : <Film size={14} />}
                  {sceneNeedsLocalCompletion(scene)
                    ? 'Tiếp tục tải clip'
                    : enforceKlingLongFormSpeechPolicy && scene.status === 'NativeAudioInvalid'
                      ? 'Tạo lại với prompt ưu tiên lời thoại'
                      : status.tone === 'running'
                        ? 'Tiếp tục theo dõi'
                        : status.tone === 'failed'
                          ? 'Thử lại cảnh này'
                          : 'Tạo clip cảnh này'}
                </button>
              )}
              {isSceneCompleted(scene) && <span className="scene-complete-note"><CircleCheck size={15} /> Hình và Native Audio đã được duyệt</span>}
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

function canQueueScene(scene: SceneSummary): boolean {
  return scene.canGenerate && !isSceneCompleted(scene) && scene.prompt.trim().length > 0;
}

function sceneAssignedAssetIds(sceneId: string, assetLibrary: ProjectAssetLibrary | null): string[] {
  return assetLibrary?.sceneAssignments.find((assignment) => assignment.sceneId === sceneId)?.projectAssetIds ?? [];
}

function areSceneAssetsReady(sceneId: string, assetLibrary: ProjectAssetLibrary | null): boolean {
  if (!assetLibrary) return true;
  const assignment = assetLibrary.sceneAssignments.find((item) => item.sceneId === sceneId);
  if (assignment && !assignment.isValid) return false;
  if (!assignment || assignment.projectAssetIds.length === 0) return true;
  if (assignment.hasUnlockedAssets) return false;
  const assetById = new Map(assetLibrary.assets.map((asset) => [asset.projectAssetId, asset]));
  return assignment.projectAssetIds.every((assetId) => assetById.get(assetId)?.status === 'Locked');
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
  if (status === 'audioreviewrequired') return { label: 'Cần nghe duyệt', tone: 'waiting' };
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

function CreateVideoCard({ busy, onCreate }: { busy: boolean; onCreate: (payload: CreateProjectPayload) => void }) {
  const [topic, setTopic] = useState('');
  const [aspectRatio, setAspectRatio] = useState('16:9');

  const submit = () => {
    const normalizedTopic = topic.trim();
    if (!normalizedTopic || busy) return;
    onCreate({ topic: normalizedTopic, aspectRatio, languageCode: 'vi-VN' });
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
        <div className="select-group create-native-audio-note">
          <span>Âm thanh</span>
          <strong><Volume2 size={15} /> Provider Native Audio</strong>
          <small>Giọng nói, âm thanh môi trường và hiệu ứng được provider tạo cùng clip.</small>
        </div>
        <button className="start-button" disabled={!topic.trim() || busy} onClick={submit}>
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
  onUnavailable
}: {
  project: ProjectDashboard | null;
  busy: boolean;
  mediaToolsReady: boolean;
  onRender: () => void;
  onUnavailable: (message: string) => void;
}) {
  const progress = Math.round(project?.render.progressPercent ?? project?.overallProgressPercent ?? 0);
  const readyToRender = Boolean(
    project && project.totalScenes > 0 && project.approvedScenes === project.totalScenes
  );
  return (
    <section className="card side-card render-card">
      <h2>Tiến độ render</h2>
      {project ? <>
        <div className="render-summary">
          <div className="progress-ring" style={{ '--progress': `${Math.min(100, Math.max(0, progress)) * 3.6}deg` } as React.CSSProperties}><div><strong>{progress}%</strong></div></div>
          <div><strong>{project.render.totalScenes > 0 ? `Đã tạo ${project.render.completedScenes}/${project.render.totalScenes} cảnh` : 'Chưa có cảnh để render'}</strong><span>{project.runningJobs > 0 ? `${project.runningJobs} tác vụ đang xử lý` : translateProjectStatus(project.project.status)}</span><ProgressBar value={progress} /></div>
        </div>
        <button
          className="render-final-button"
          disabled={busy || !mediaToolsReady || !readyToRender}
          onClick={onRender}
        >
          <Film size={15} /> {project.preview?.url ? 'Dựng lại video' : 'Dựng video cuối'}
        </button>
        {!readyToRender && <small className="render-requirement">Cần duyệt hình và Native Audio của tất cả cảnh.</small>}
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
