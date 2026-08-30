import { useEffect, useRef, useState } from 'react';
import {
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
  Library,
  Link2,
  ListVideo,
  LockKeyhole,
  LoaderCircle,
  LogOut,
  Menu,
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
  Upload,
  UserRound,
  Users,
  Volume2,
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
import type {
  AiModel,
  CharacterSummary,
  CreateProjectPayload,
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
  ProjectSummary,
  ProviderSettings,
  SceneSummary,
  UpdateScenePayload,
  UpdateCharacterPayload,
} from './types';

type Page = 'create' | 'projects' | 'apiKeys';
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
  projects: {
    title: 'Dự án của tôi',
    subtitle: 'Quản lý và tiếp tục các dự án video đã tạo.'
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
  generationRunning: false
};

const primaryMenu: Array<{ label: string; icon: LucideIcon; page?: Page }> = [
  { label: 'Dashboard', icon: Home, page: 'create' },
  { label: 'Dự án của tôi', icon: FolderOpen, page: 'projects' },
  { label: 'Tạo video', icon: Play, page: 'create' },
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
  const [mediaInstallProgress, setMediaInstallProgress] = useState<DesktopUpdateProgress | null>(null);
  const [sceneSaveState, setSceneSaveState] = useState<SceneSaveState | null>(null);
  const pendingSceneSaveRef = useRef<PendingSceneSave | null>(null);

  const notify = (message: string, error = false) => {
    const id = Date.now();
    setToasts((current) => [...current, { id, message, error }]);
    window.setTimeout(() => setToasts((current) => current.filter((item) => item.id !== id)), 3600);
  };

  useEffect(() => {
    const unsubscribe = subscribeToHost((message: HostMessage) => {
      if (message.type === 'dashboard.state' && message.payload) {
        const nextDashboard = message.payload as DashboardState;
        setDashboard(nextDashboard);
        if (!nextDashboard.generationRunning) setCharacterImageBusyId(null);
        setBusy(false);
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
    setPage('create');
  };

  const createProject = (payload: CreateProjectPayload) => {
    setBusy(true);
    postToHost('project.create', payload);
  };

  const generateContent = () => {
    if (!dashboard.selectedProject || dashboard.generationRunning) return;
    setBusy(true);
    postToHost('generation.content');
  };

  const renderFinalVideo = () => {
    const project = dashboard.selectedProject;
    if (!project || dashboard.generationRunning) return;
    if (!dashboard.mediaTools.ready) {
      notify(dashboard.mediaTools.message || 'FFmpeg và FFprobe chưa sẵn sàng.', true);
      return;
    }
    if (project.totalScenes === 0 || project.approvedScenes !== project.totalScenes) {
      notify('Hãy nghe và duyệt Native Audio của tất cả cảnh trước khi dựng video cuối.', true);
      return;
    }
    setConfirmation({
      eyebrow: 'XÁC NHẬN DỰNG VIDEO CUỐI',
      title: project.preview?.url ? 'Dựng lại video hoàn chỉnh?' : 'Dựng video hoàn chỉnh?',
      description: `${project.totalScenes} clip SceneVideo đã duyệt sẽ được ghép đúng thứ tự. Âm thanh Native Audio của provider được giữ nguyên; hệ thống không tạo hoặc chèn thêm giọng TTS.`,
      note: 'FFmpeg sẽ kiểm tra lại hình, audio stream, mức âm lượng và thời lượng trước khi công nhận video đầu ra.',
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
    if (!dashboard.mediaTools.ready) {
      notify(dashboard.mediaTools.message || 'FFmpeg và FFprobe chưa sẵn sàng.', true);
      return;
    }
    const selectedSceneIds = new Set(sceneIds);
    const selectedScenes = project.scenes.filter((scene) => selectedSceneIds.has(scene.sceneId));
    if (selectedScenes.length !== selectedSceneIds.size) {
      notify('Danh sách cảnh đã thay đổi. Hãy chọn lại cảnh cần tạo.', true);
      return;
    }
    const resumableScenes = selectedScenes.filter(sceneNeedsLocalCompletion);
    const newRequestScenes = selectedScenes.filter((scene) => !sceneNeedsLocalCompletion(scene));
    const isDownloadOnly = resumableScenes.length === selectedScenes.length;
    const isMixedOperation = resumableScenes.length > 0 && newRequestScenes.length > 0;
    const totalSeconds = Math.ceil(selectedScenes.reduce((total, scene) => total + scene.durationMs, 0) / 1000);
    const newRequestSeconds = Math.ceil(newRequestScenes.reduce((total, scene) => total + scene.durationMs, 0) / 1000);
    const spokenSceneCount = selectedScenes.filter((scene) => scene.speechMode !== 'None').length;
    const spokenPreview = selectedScenes
      .map((scene) => {
        const durationSeconds = Math.ceil(scene.durationMs / 1000);
        const speech = scene.speechMode === 'None'
          ? 'Không có lời nói'
          : `${speechModeLabel(scene.speechMode)}: “${scene.narration?.trim() || 'chưa có nội dung'}”`;
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
      ? ` ${retryCount} clip có Native Audio không đạt sẽ được tạo lại và phát sinh chi phí provider mới.`
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
      title: `Tạo ${selectedScenes.length} clip video?`,
      description: `${providerLabel} · ${dashboard.providerStatus.videoModel ?? 'Model theo policy'} · ${dashboard.providerStatus.videoResolution ?? '720p'} · Native Audio\nTổng thời lượng: khoảng ${totalSeconds} giây · ${spokenSceneCount}/${selectedScenes.length} cảnh có lời nói\n\n${spokenPreview}`,
      note: `${costNote}${languageNote}${retryNote}`,
      confirmLabel: `Tạo ${selectedScenes.length} clip`,
      onConfirm: () => {
        setBusy(true);
        postToHost('generation.video', { sceneIds });
      }
    });
  };

  const requestContentRegeneration = () => {
    if (!dashboard.selectedProject || dashboard.generationRunning) return;
    setConfirmation({
      eyebrow: 'XÁC NHẬN SINH LẠI',
      title: 'Sinh lại content có nhân vật?',
      description: 'AI sẽ tạo một phiên bản kịch bản mới, chia lại các cảnh và bổ sung hồ sơ nhân vật để dùng xuyên suốt video.',
      note: 'Thao tác này có thể phát sinh chi phí OpenAI theo rate đang Active của tổ chức.',
      confirmLabel: 'Tiếp tục sinh lại',
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

  const selectCharacterReference = (characterId: string) => {
    if (!dashboard.selectedProject || dashboard.generationRunning) return;
    postToHost('character.reference.select', { characterId });
  };

  const generateCharacterReference = (character: CharacterSummary) => {
    if (!dashboard.selectedProject || dashboard.generationRunning || characterImageBusyId) return;
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
          busy={generationBusy}
          onMenu={() => setSidebarOpen(true)}
          onCreate={() => setPage('create')}
          onRefresh={() => {
            setBusy(true);
            postToHost('dashboard.refresh');
          }}
          onSelectProject={selectProject}
          onSelectOrganization={(organizationId) => {
            setBusy(true);
            postToHost('organization.select', { organizationId });
          }}
          onUnavailable={notify}
        />

        {page === 'projects' ? (
          <ProjectsPage projects={dashboard.projects} onSelect={selectProject} onCreate={() => setPage('create')} />
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

        <button className="new-video-button" onClick={() => onNavigate('Tạo video mới', 'create')}>
          <Plus size={18} /> Tạo video mới
        </button>

        <nav className="sidebar-nav">
          {primaryMenu.map(({ label, icon: Icon, page: target }) => (
            <button
              className={target === page && (label === 'Dashboard' || label === 'Dự án của tôi') ? 'active' : ''}
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
      {page !== 'apiKeys' && dashboard.projects.length > 0 && (
        <label className="project-picker">
          <span>Dự án</span>
          <select
            value={dashboard.selectedProject?.project.projectId ?? ''}
            onChange={(event) => onSelectProject(event.target.value)}
          >
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

function GenerationActions({
  project,
  providerStatus,
  busy,
  onGenerateContent
}: {
  project: ProjectDashboard | null;
  providerStatus: GenerationProviderStatus;
  busy: boolean;
  onGenerateContent: () => void;
}) {
  if (!project) return null;
  const hasContent = project.totalScenes > 0;
  if (hasContent) return null;

  return (
    <section className="card generation-actions">
      <div>
        <span className="generation-eyebrow">API GENERATION</span>
        <h2>Tạo nội dung và prompt</h2>
        <p>OpenAI sẽ viết hook, kịch bản, chia cảnh và tạo prompt có cấu trúc.</p>
      </div>
      <div className="generation-provider-state">
        <span className={providerStatus.openAiReady ? 'ready' : 'missing'}>
          OpenAI · {providerStatus.openAiReady ? providerStatus.openAiModel : 'chưa được cấu hình'}
        </span>
        <span className={providerStatus.videoReady ? 'ready' : 'missing'}>
          Video · {providerStatus.videoReady ? `${providerStatus.videoProviderName ?? providerStatus.videoProviderCode} / ${providerStatus.videoModel}` : 'chưa được cấu hình'}
        </span>
      </div>
      <button disabled={busy || !providerStatus.openAiReady} onClick={onGenerateContent}>
        {busy ? <LoaderCircle className="spin" size={18} /> : <WandSparkles size={18} />}
        Tạo nội dung &amp; chia cảnh
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

function StoryboardSection({
  project,
  providerStatus,
  mediaTools,
  busy,
  onGenerateVideo,
  onApproveNativeAudio,
  onInstallMediaTools,
  onCheckMediaTools,
  onUpdateScene,
  sceneSaveState,
  onClearSaveFailure
}: {
  project: ProjectDashboard | null;
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
}) {
  const [selectedSceneIds, setSelectedSceneIds] = useState<Set<string>>(new Set());
  const selectionProjectId = useRef('');
  const scenes = project?.scenes ?? [];
  const selectableScenes = scenes.filter(canQueueScene);
  const selectableKey = selectableScenes.map((scene) => `${scene.sceneId}:${scene.status}`).join('|');

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
  const allSelected = selectableScenes.length > 0 && selectedIds.length === selectableScenes.length;

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
            disabled={busy || selectableScenes.length === 0}
            onClick={() => setSelectedSceneIds(allSelected ? new Set() : new Set(selectableScenes.map((scene) => scene.sceneId)))}
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

      <div className="storyboard-list">
        {scenes.map((scene) => (
          <SceneCard
            key={scene.sceneId}
            scene={scene}
            selected={selectedSceneIds.has(scene.sceneId)}
            busy={busy}
            videoReady={providerStatus.videoReady}
            mediaToolsReady={mediaTools.ready}
            onToggle={() => toggleScene(scene.sceneId)}
            onGenerate={() => onGenerateVideo([scene.sceneId])}
            onApproveNativeAudio={(playbackConfirmed) => onApproveNativeAudio(scene.sceneId, playbackConfirmed)}
            onUpdate={onUpdateScene}
            saveState={sceneSaveState?.sceneId === scene.sceneId ? sceneSaveState : null}
            onClearSaveFailure={() => onClearSaveFailure(scene.sceneId)}
          />
        ))}
      </div>
    </section>
  );
}

function SceneCard({
  scene,
  selected,
  busy,
  videoReady,
  mediaToolsReady,
  onToggle,
  onGenerate,
  onApproveNativeAudio,
  onUpdate,
  saveState,
  onClearSaveFailure
}: {
  scene: SceneSummary;
  selected: boolean;
  busy: boolean;
  videoReady: boolean;
  mediaToolsReady: boolean;
  onToggle: () => void;
  onGenerate: () => void;
  onApproveNativeAudio: (playbackConfirmed: boolean) => void;
  onUpdate: (payload: UpdateScenePayload) => void;
  saveState: SceneSaveState | null;
  onClearSaveFailure: () => void;
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
  const status = sceneStatus(scene);
  const maximumSpokenWords = scene.maximumSpokenWords;
  const persistedSpokenWordCount = countWords(scene.narration ?? '');
  const speechWordBudgetExceeded = scene.speechMode !== 'None' && persistedSpokenWordCount > maximumSpokenWords;
  const hasSpeechWordBudgetError = scene.lastErrorCode === 'kling_spoken_text_too_long' || speechWordBudgetExceeded;
  const selectable = canQueueScene(scene) && !hasSpeechWordBudgetError;
  const spokenWordCount = countWords(narration);
  const validSpeech = speechMode === 'None'
    ? narration.trim().length === 0
    : narration.trim().length > 0 &&
      spokenWordCount <= maximumSpokenWords &&
      (speechMode !== 'OnCameraDialogue' || scene.characters.length === 1);
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
          : spokenWordCount > maximumSpokenWords
            ? `Lời nói cần tối đa ${maximumSpokenWords} từ cho clip ${Math.ceil(scene.durationMs / 1000)} giây.`
            : speechMode === 'OnCameraDialogue' && scene.characters.length !== 1
              ? 'Lời thoại trực diện cần đúng một nhân vật trong cảnh.'
              : null;

  useEffect(() => setPreviewPlaybackConfirmed(false), [scene.sceneId, scene.preview?.url]);
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
                  placeholder={speechMode === 'None' ? 'Cảnh chỉ có âm thanh môi trường.' : 'Nhập câu ngắn, tự nhiên và vừa với thời lượng cảnh.'}
                  value={narration}
                  onChange={(event) => {
                    onClearSaveFailure();
                    setNarration(event.target.value);
                  }}
                />
                {speechMode !== 'None' && (
                  <small className={spokenWordCount > maximumSpokenWords ? 'scene-word-count invalid' : 'scene-word-count'}>
                    {spokenWordCount}/{maximumSpokenWords} từ cho clip {Math.ceil(scene.durationMs / 1000)} giây
                  </small>
                )}
                {speechMode === 'OnCameraDialogue' && scene.characters.length !== 1 && (
                  <small className="scene-word-count invalid">Lời thoại trực diện cần đúng một nhân vật trong cảnh.</small>
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
                <span>{speechModeLabel(scene.speechMode)}</span>
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
              {scene.speechMode !== 'None' && <span>{countWords(scene.narration ?? '')}/{maximumSpokenWords} từ</span>}
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
                  {hasSpeechWordBudgetError && scene.canEdit && (
                    <button type="button" disabled={busy} onClick={beginEdit}>Sửa lời cảnh</button>
                  )}
                </div>
              </div>
            )}
            {speechWordBudgetExceeded && !scene.lastErrorMessage && status.tone !== 'failed' && (
              <div className="scene-error">
                <TriangleAlert size={14} />
                <div className="scene-error-content">
                  <span>Lời cảnh này có {persistedSpokenWordCount}/{maximumSpokenWords} từ. Hãy rút ngắn và lưu lời trước khi tạo clip.</span>
                  {scene.canEdit && (
                    <button type="button" disabled={busy} onClick={beginEdit}>Sửa lời cảnh</button>
                  )}
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
                </div>
                <button
                  type="button"
                  disabled={busy || !scene.canApproveNativeAudio || !previewPlaybackConfirmed}
                  onClick={() => onApproveNativeAudio(previewPlaybackConfirmed)}
                >
                  <CircleCheck size={14} /> Duyệt hình và âm thanh
                </button>
              </div>
            )}
            {scene.characterSetupMessage && (
              <div className="scene-character-warning"><TriangleAlert size={14} /> {scene.characterSetupMessage}</div>
            )}
            <div className="scene-actions">
              {scene.canEdit && (
                <button type="button" className="scene-edit" disabled={busy} onClick={beginEdit}><Pencil size={14} /> Chỉnh sửa</button>
              )}
              {selectable && (
                <button type="button" className="scene-generate-one" disabled={busy || !videoReady || !mediaToolsReady} onClick={onGenerate}>
                  {sceneNeedsLocalCompletion(scene) ? <Download size={14} /> : <Film size={14} />}
                  {sceneNeedsLocalCompletion(scene) ? 'Tiếp tục tải clip' : status.tone === 'running' ? 'Tiếp tục theo dõi' : status.tone === 'failed' ? 'Thử lại cảnh này' : 'Tạo clip cảnh này'}
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

function speechModeLabel(mode: SceneSummary['speechMode']): string {
  if (mode === 'OnCameraDialogue') return 'Nhân vật nói trực tiếp bằng Native Audio của provider';
  if (mode === 'NativeVoiceOver') return 'Lời dẫn ngoài khung hình bằng Native Audio của provider';
  return 'Không có lời nói';
}

function countWords(value: string): number {
  const normalized = value.trim();
  return normalized ? normalized.split(/\s+/u).length : 0;
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
  const [languageCode, setLanguageCode] = useState('vi-VN');

  const submit = () => {
    const normalizedTopic = topic.trim();
    if (!normalizedTopic || busy) return;
    onCreate({ topic: normalizedTopic, aspectRatio, languageCode });
  };

  return (
    <section className="card create-card">
      <h2><span>1.</span> Nhập chủ đề video</h2>
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
        <label className="select-group">Ngôn ngữ<select value={languageCode} onChange={(e) => setLanguageCode(e.target.value)}><option value="vi-VN">Tiếng Việt</option><option value="en-US">English</option></select></label>
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
      <h2><span>2.</span> Quy trình tạo video bằng AI</h2>
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
      <h2 className="section-title"><span>3.</span> Chi tiết tiến trình</h2>
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
      <h2 className="section-title"><span>4.</span> AI Models sẵn sàng</h2>
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
      <span className="model-readonly-badge">Do server quản lý</span>
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

function PreviewCard({ project }: { project: ProjectDashboard | null }) {
  const preview = project?.preview;
  return (
    <section className="card side-card preview-card">
      <h2>Xem trước dự án</h2>
      <div className="preview-frame">
        {preview?.url ? (
          <video controls preload="metadata" src={preview.url} />
        ) : (
          <div className="preview-placeholder"><div className="sun" /><div className="mountain mountain-back" /><div className="mountain mountain-front" /><button aria-label="Chưa có video"><Play size={30} fill="white" /></button><span>{project ? 'Video sẽ xuất hiện khi render hoàn tất' : 'Chọn hoặc tạo một dự án'}</span></div>
        )}
      </div>
      <div className="preview-meta"><Play size={13} fill="currentColor" /><span>{formatDuration(preview?.durationMs ? Math.round(preview.durationMs / 1000) : project?.project.targetDurationSeconds ?? 0)}</span><div className="fake-timeline"><i /></div></div>
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
        `Ngôn ngữ: ${formatLanguage(project.languageCode)}`,
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
