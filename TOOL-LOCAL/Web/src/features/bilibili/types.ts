export type BilibiliEntry = {
  id: string; url: string; title: string; thumbnailUrl?: string | null;
  durationSeconds?: number | null; uploader?: string | null;
};
export type BilibiliScan = {
  id: string; status: 'Scanning' | 'Completed' | 'Partial' | 'Cancelled'; count: number;
  complete: boolean; message?: string | null;
};
export type BilibiliJob = {
  id: string; video: BilibiliEntry; quality: string;
  status: 'Queued' | 'Downloading' | 'Processing' | 'Verifying' | 'Completed' | 'Failed' | 'Cancelled';
  percent: number; downloadedBytes: number; bytesPerSecond?: number | null;
  fileName?: string | null; message?: string | null;
};
export type BilibiliState = {
  revision: number; operation: 'Idle' | 'Installing' | 'Scanning' | 'Downloading';
  runtimeReady: boolean; runtimeVersion: string; folderLabel: string; scan: BilibiliScan | null;
  entries: BilibiliEntry[]; jobs: BilibiliJob[];
};
export type BilibiliScanUpdate = { revision: number; scan: BilibiliScan; entries: BilibiliEntry[] };
export type BilibiliJobUpdate = { revision: number; job: BilibiliJob };
export type BilibiliScanRequest = { url: string };
export type BilibiliDownloadRequest = { scanId: string; entryIds: string[]; quality: string };
export type BilibiliJobRequest = { jobId?: string };

export const initialBilibiliState: BilibiliState = {
  revision: -1, operation: 'Idle', runtimeReady: false, runtimeVersion: '', folderLabel: 'Bilibili',
  scan: null, entries: [], jobs: []
};

export function applyBilibiliUpdate(state: BilibiliState, type: string, payload: unknown): BilibiliState {
  if (!payload || typeof payload !== 'object' || !('revision' in payload)
    || typeof payload.revision !== 'number' || payload.revision < state.revision) return state;
  if (type === 'bilibili.state') return payload as BilibiliState;
  if (type === 'bilibili.scan.progress') {
    const update = payload as BilibiliScanUpdate;
    if (state.scan?.id !== update.scan.id || state.operation !== 'Scanning') return state;
    const entries = new Map(state.entries.map(entry => [entry.id, entry]));
    update.entries.forEach(entry => entries.set(entry.id, entry));
    return { ...state, revision: update.revision, scan: update.scan, entries: [...entries.values()] };
  }
  if (type === 'bilibili.job') {
    const update = payload as BilibiliJobUpdate;
    if (!state.jobs.some(job => job.id === update.job.id)) return state;
    return { ...state, revision: update.revision, jobs: state.jobs.map(job => job.id === update.job.id ? update.job : job) };
  }
  return state;
}
