import type { VietsubModuleState } from './types';

export type VietsubNoticeChannel = 'errorMessage' | 'subtitleNotice' | 'translationNotice' | 'voiceNotice';
export type VietsubNoticeEvents = Partial<Record<VietsubNoticeChannel, { id: number; source?: string }>>;
const channels: VietsubNoticeChannel[] = ['errorMessage', 'subtitleNotice', 'translationNotice', 'voiceNotice'];

// Explicit event identity distinguishes a fresh failure with identical wording from another poll.
export function noticeEvent(state: VietsubModuleState, channel: VietsubNoticeChannel, source?: string | null) {
  const previous = state.noticeEvents?.[channel];
  if (source && previous?.source === source) return { noticeEvents: state.noticeEvents };
  return { noticeEvents: { ...state.noticeEvents, [channel]: { id: (previous?.id ?? 0) + 1, source: source ?? undefined } } };
}

export function trackNoticeChanges(previous: VietsubModuleState, next: VietsubModuleState): VietsubModuleState {
  let result = next;
  for (const channel of channels) {
    if (next[channel] !== previous[channel] && next.noticeEvents?.[channel] === previous.noticeEvents?.[channel]) {
      result = { ...result, ...noticeEvent(result, channel) };
    }
  }
  return result;
}
