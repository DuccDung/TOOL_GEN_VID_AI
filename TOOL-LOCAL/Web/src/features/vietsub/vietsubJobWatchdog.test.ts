import { describe, expect, it } from 'vitest';
import {
  VIETSUB_JOB_ERROR_BACKOFF_MS,
  VIETSUB_JOB_REQUEST_TIMEOUT_MS,
  VIETSUB_JOB_SILENCE_TIMEOUT_MS,
  shouldRequestVietsubJobStatus,
  shouldWatchVietsubJob
} from './vietsubJobWatchdog';

describe('vietsub OCR job watchdog', () => {
  it('watches only job states that can still make progress', () => {
    expect(shouldWatchVietsubJob('PENDING')).toBe(true);
    expect(shouldWatchVietsubJob('RUNNING')).toBe(true);
    expect(shouldWatchVietsubJob('PAUSING')).toBe(true);
    expect(shouldWatchVietsubJob('PAUSED')).toBe(false);
    expect(shouldWatchVietsubJob('FAILED')).toBe(false);
    expect(shouldWatchVietsubJob('COMPLETED')).toBe(false);
  });

  it('polls after silence without overlapping an active request', () => {
    const now = 20_000;
    expect(shouldRequestVietsubJobStatus(
      now,
      now - VIETSUB_JOB_SILENCE_TIMEOUT_MS,
      null,
      0
    )).toBe(true);
    expect(shouldRequestVietsubJobStatus(now, 19_000, null, 0)).toBe(false);
    expect(shouldRequestVietsubJobStatus(now, 0, 19_000, 0)).toBe(false);
    expect(shouldRequestVietsubJobStatus(
      now,
      0,
      now - VIETSUB_JOB_REQUEST_TIMEOUT_MS,
      0
    )).toBe(true);
  });

  it('honors the error backoff window', () => {
    const now = 20_000;
    expect(shouldRequestVietsubJobStatus(
      now,
      0,
      null,
      now + VIETSUB_JOB_ERROR_BACKOFF_MS
    )).toBe(false);
  });
});
