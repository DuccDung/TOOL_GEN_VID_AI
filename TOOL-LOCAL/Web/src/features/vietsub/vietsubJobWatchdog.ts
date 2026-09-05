export const VIETSUB_JOB_SILENCE_TIMEOUT_MS = 3_000;
export const VIETSUB_JOB_REQUEST_TIMEOUT_MS = 8_000;
export const VIETSUB_JOB_ERROR_BACKOFF_MS = 5_000;

export function shouldWatchVietsubJob(status: string): boolean {
  return status === 'PENDING' || status === 'RUNNING' || status === 'PAUSING';
}

export function shouldRequestVietsubJobStatus(
  now: number,
  lastUpdateAt: number,
  requestSentAt: number | null,
  backoffUntil: number
): boolean {
  if (now < backoffUntil || now - lastUpdateAt < VIETSUB_JOB_SILENCE_TIMEOUT_MS) {
    return false;
  }

  return requestSentAt === null || now - requestSentAt >= VIETSUB_JOB_REQUEST_TIMEOUT_MS;
}
