import type { SceneSummary } from './types';

export type WorkflowStepState = 'complete' | 'current' | 'pending';

export type WorkflowStep = {
  id: 'voice' | 'voice-review' | 'video' | 'final-review';
  label: string;
  state: WorkflowStepState;
};

export type StoryboardActionSummary = {
  downloadCount: number;
  voicePreparationCount: number;
  videoCount: number;
  label: string;
};

function isLocalVideoCompletion(scene: SceneSummary): boolean {
  const status = scene.status.toLowerCase();
  return status === 'generated' || status === 'downloading';
}

export function isCanonicalSpeechScene(scene: SceneSummary, speechProductionPolicy?: string | null): boolean {
  return speechProductionPolicy === 'CanonicalVoice' && scene.speechMode !== 'None';
}

export function needsCanonicalVoicePreparation(
  scene: SceneSummary,
  speechProductionPolicy?: string | null
): boolean {
  if (!isCanonicalSpeechScene(scene, speechProductionPolicy) || isLocalVideoCompletion(scene)) return false;
  if (scene.preview?.url) return false;
  if (!scene.canonicalVoicePreview?.url) return true;
  if (scene.speechMode === 'NativeVoiceOver' && scene.canonicalVoicePreview?.url) return false;
  return scene.speechStatus !== 'SpeechApproved';
}

export function getStoryboardActionSummary(
  scenes: SceneSummary[],
  speechProductionPolicy?: string | null
): StoryboardActionSummary {
  const downloadCount = scenes.filter(isLocalVideoCompletion).length;
  const voicePreparationCount = scenes.filter(
    (scene) => !isLocalVideoCompletion(scene) && needsCanonicalVoicePreparation(scene, speechProductionPolicy)
  ).length;
  const videoCount = Math.max(0, scenes.length - downloadCount - voicePreparationCount);
  const label = scenes.length === 0
    ? 'Chọn cảnh để xử lý'
    : downloadCount === scenes.length
      ? `Tải ${downloadCount} clip đã tạo`
      : voicePreparationCount === scenes.length
        ? `Chuẩn bị WAV cho ${voicePreparationCount} cảnh`
        : videoCount === scenes.length
          ? speechProductionPolicy === 'CanonicalVoice'
            ? `Tạo video nền cho ${videoCount} cảnh`
            : `Tạo ${videoCount} clip video`
          : `Tiếp tục ${scenes.length} cảnh`;

  return { downloadCount, voicePreparationCount, videoCount, label };
}

export function getCanonicalVoiceJourney(
  scene: SceneSummary,
  requiredPlaybackConfirmed: boolean
): { steps: WorkflowStep[]; nextAction: string } {
  const hasVoice = Boolean(scene.canonicalVoicePreview?.url);
  const hasVideo = Boolean(scene.preview?.url);
  const voiceApproved = hasVideo || (hasVoice && scene.speechStatus === 'SpeechApproved');
  const finalApproved = scene.status.toLowerCase() === 'approved';
  const onCamera = scene.speechMode === 'OnCameraDialogue';

  if (!onCamera) {
    let narrationCurrentId: WorkflowStep['id'] = 'voice';
    if (hasVoice && !hasVideo) narrationCurrentId = 'video';
    else if (hasVideo && !finalApproved) narrationCurrentId = 'final-review';

    const narrationSteps: WorkflowStep[] = [
      { id: 'voice', label: 'Tạo WAV', state: hasVoice ? 'complete' : 'current' },
      { id: 'video', label: 'Tạo video nền', state: hasVideo ? 'complete' : narrationCurrentId === 'video' ? 'current' : 'pending' },
      { id: 'final-review', label: 'Duyệt video', state: finalApproved ? 'complete' : narrationCurrentId === 'final-review' ? 'current' : 'pending' }
    ];

    if (!hasVoice) {
      return { steps: narrationSteps, nextAction: 'Bước tiếp theo: tạo bản đọc WAV. Video chưa được gửi sang provider ở bước này.' };
    }
    if (!hasVideo) {
      return { steps: narrationSteps, nextAction: 'Canonical WAV hiện hành đã sẵn sàng. Bấm “Tạo video nền”; hệ thống sẽ dùng lại WAV và không gọi TTS mới.' };
    }
    if (!finalApproved && !requiredPlaybackConfirmed) {
      return { steps: narrationSteps, nextAction: 'Video đã ghép tiếng. Hãy phát video ít nhất một lần trước khi duyệt kết quả cuối.' };
    }
    if (!finalApproved) {
      return { steps: narrationSteps, nextAction: 'Bước tiếp theo: kiểm tra hình và tiếng, sau đó duyệt video hoàn chỉnh.' };
    }
    return { steps: narrationSteps, nextAction: 'Cảnh đã hoàn tất và sẵn sàng cho bước dựng video cuối.' };
  }

  let currentId: WorkflowStep['id'] = 'voice';
  if (hasVoice && !voiceApproved) currentId = 'voice-review';
  else if (voiceApproved && !hasVideo) currentId = 'video';
  else if (hasVideo && !finalApproved) currentId = 'final-review';

  const steps: WorkflowStep[] = [
    { id: 'voice', label: 'Tạo WAV', state: hasVoice ? 'complete' : currentId === 'voice' ? 'current' : 'pending' },
    { id: 'voice-review', label: 'Duyệt WAV', state: voiceApproved ? 'complete' : currentId === 'voice-review' ? 'current' : 'pending' },
    { id: 'video', label: 'Tạo video nền và ghép WAV', state: hasVideo ? 'complete' : currentId === 'video' ? 'current' : 'pending' },
    { id: 'final-review', label: 'Duyệt video', state: finalApproved ? 'complete' : currentId === 'final-review' ? 'current' : 'pending' }
  ];

  if (!hasVoice) {
    return { steps, nextAction: 'Bước tiếp theo: tạo bản đọc WAV. Video chưa được gửi sang provider ở bước này.' };
  }
  if (!voiceApproved && !requiredPlaybackConfirmed) {
    return { steps, nextAction: 'WAV đã qua kiểm tra kỹ thuật. Bước tiếp theo: phát WAV ít nhất một lần, sau đó xác nhận checklist để mở nút duyệt.' };
  }
  if (!voiceApproved) {
    return { steps, nextAction: 'Bước tiếp theo: hoàn tất checklist và duyệt Canonical WAV.' };
  }
  if (!hasVideo) {
    return { steps, nextAction: 'Canonical WAV đã duyệt. Bấm “Tạo video nền”; hệ thống sẽ ghép WAV hiện hành vào clip và không gọi TTS mới. Hình ảnh và chuyển động miệng giữ theo video được tạo.' };
  }
  if (!finalApproved && !requiredPlaybackConfirmed) {
    return { steps, nextAction: 'Video đã ghép tiếng. Hãy phát video ít nhất một lần trước khi duyệt kết quả cuối.' };
  }
  if (!finalApproved) {
    return { steps, nextAction: 'Bước tiếp theo: kiểm tra hình và tiếng, sau đó duyệt video hoàn chỉnh.' };
  }
  return { steps, nextAction: 'Cảnh đã hoàn tất và sẵn sàng cho bước dựng video cuối.' };
}
