import type { LocalVoiceState, ProjectDashboard, SceneSummary } from '../../types';

export function supportsLocalVoice(project: ProjectDashboard): boolean {
  return project.workflowStructureType === 'OpenAiStructuredPlan' && project.videoProviderCode === 'fal' &&
    project.speechProductionPolicy === 'ProviderNativeVerified';
}

export function canUseAsVoiceSource(scene: SceneSummary): boolean {
  return scene.status === 'Approved' && scene.speechMode === 'OnCameraDialogue' &&
    scene.characters.length === 1 && scene.nativeAudioAudible && [4000, 6000, 8000].includes(scene.generationDurationMs);
}

export function canConvertScene(scene: SceneSummary, state: LocalVoiceState): boolean {
  return canUseAsVoiceSource(scene) && state.enabled && state.runtime.status === 'READY' &&
    state.anchors.some(anchor => anchor.characterId === scene.characters[0].characterId && anchor.status === 'Approved');
}

export function localVoiceStatusLabel(status: string): string {
  const labels: Record<string, string> = {
    Preparing: 'Chuẩn bị nguồn', DetectingSpeech: 'Tìm đoạn nói', SeparatingAudio: 'Tách giọng / âm nền',
    ConvertingVoice: 'Chuyển màu giọng', Mixing: 'Trộn âm nền', Validating: 'Kiểm tra media',
    ReviewRequired: 'Chờ nghe duyệt', Approved: 'Đã duyệt', Rejected: 'Không đạt', Failed: 'Xử lý lỗi',
    Cancelled: 'Đã hủy', Stale: 'Hết hiệu lực', Interrupted: 'Bị gián đoạn — có thể chạy lại',
  };
  return labels[status] ?? status;
}
