import { describe, expect, it } from 'vitest';
import { acceptsCreatorResponse, mergeTikTokJobs, selectTikTokAccount } from './tiktokAccountState';
import type { TikTokConnection, TikTokPublishStatus } from './types';

describe('TikTok account context', () => {
  const accounts = ['a', 'b'].map(connectionId => ({ connectionId } as TikTokConnection));
  it('requires a deliberate selection for multiple accounts and preserves an existing selection', () => {
    expect(selectTikTokAccount(accounts, null)).toBeNull();
    expect(selectTikTokAccount(accounts, 'b')).toBe('b');
    expect(selectTikTokAccount([accounts[0]], null)).toBe('a');
  });
  it('rejects stale creator replies after A to B to A, including mismatched server identity', () => {
    const expected = { id: 'a-second', connectionId: 'a' };
    expect(acceptsCreatorResponse(expected, 'a-first', 'a', 'a')).toBe(false);
    expect(acceptsCreatorResponse(expected, 'a-second', 'b', 'a')).toBe(false);
    expect(acceptsCreatorResponse(expected, 'a-second', 'a', 'b')).toBe(false);
    expect(acceptsCreatorResponse(expected, 'a-second', 'a', 'a')).toBe(true);
  });
  it('never regresses a terminal job or assigns its ID to another account', () => {
    const original: TikTokPublishStatus = { publishJobId: 'j', connectionId: 'a', status: 'PUBLISH_COMPLETE', isTerminal: true,
      uploadedBytes: 10, publicPostIds: ['p'], updatedAtUtc: '2026-09-09T01:00:00Z' };
    const result = mergeTikTokJobs([original], [{ ...original, connectionId: 'b' },
      { ...original, status: 'PROCESSING_UPLOAD', isTerminal: false, updatedAtUtc: '2026-09-09T02:00:00Z' }]);
    expect(result).toEqual([original]);
  });
  it('accepts a server update over a provisional job even when the desktop clock is ahead', () => {
    const provisional: TikTokPublishStatus = { publishJobId: 'j', connectionId: 'a', status: 'PROCESSING_UPLOAD',
      uploadedBytes: 0, publicPostIds: [], updatedAtUtc: '2026-09-15T12:00:00Z', isTerminal: false, provisional: true };
    const processing = { ...provisional, status: 'PROCESSING_DOWNLOAD', uploadedBytes: 10,
      updatedAtUtc: '2026-09-15T11:00:00Z', provisional: undefined };
    const completed = { ...processing, status: 'PUBLISH_COMPLETE', isTerminal: true, publicPostIds: ['p'],
      updatedAtUtc: '2026-09-15T11:30:00Z' };
    expect(mergeTikTokJobs([provisional], [processing])).toEqual([processing]);
    expect(mergeTikTokJobs([processing], [completed])).toEqual([completed]);
    expect(mergeTikTokJobs([completed], [provisional])).toEqual([completed]);
  });
});
