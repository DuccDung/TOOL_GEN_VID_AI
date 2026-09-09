import type { TikTokConnection, TikTokPublishStatus } from './types';

export function selectTikTokAccount(accounts: TikTokConnection[], selected: string | null): string | null {
  if (selected && accounts.some(a => a.connectionId === selected)) return selected;
  return accounts.length === 1 ? accounts[0].connectionId : null;
}

export function acceptsCreatorResponse(expected: { id: string; connectionId: string } | null,
  requestId: string, selected: string | null, responseConnectionId?: string | null): boolean {
  return Boolean(expected && expected.id === requestId && expected.connectionId === selected &&
    responseConnectionId === expected.connectionId);
}

export function mergeTikTokJobs(previous: TikTokPublishStatus[], incoming: TikTokPublishStatus[]): TikTokPublishStatus[] {
  const jobs = new Map(previous.map(j => [j.publishJobId, j]));
  for (const job of incoming) {
    const old = jobs.get(job.publishJobId);
    if (old && ((old.connectionId && job.connectionId && old.connectionId !== job.connectionId) ||
      (old.isTerminal && !job.isTerminal) || Date.parse(old.updatedAtUtc) > Date.parse(job.updatedAtUtc))) continue;
    jobs.set(job.publishJobId, job);
  }
  return [...jobs.values()].sort((a, b) => Date.parse(b.updatedAtUtc) - Date.parse(a.updatedAtUtc));
}
