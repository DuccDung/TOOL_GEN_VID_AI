const { test } = require('node:test');
const assert = require('node:assert/strict');
const { describe, validateSettings, settingsKey, failureMessage } = require('../../TOOL-SERVER/wwwroot/admin/admin-tiktok-state.js');
const now = Date.parse('2026-09-09T05:00:00Z');
const base = { adminManagementEnabled: true, integrationEnabled: false, auditedForPublicPosting: false, connectedUserCount: 0, pendingPublishJobCount: 0, credentials: [] };
const pending = { credentialId: 'pending', status: 'Pending' };
const active = { credentialId: 'active', status: 'Active' };

test('first setup, environment-managed runtime and verified runtime have distinct next actions', () => {
  assert.equal(describe(base, now).action, 'credential');
  assert.match(describe({ ...base, integrationEnabled: true }, now).title, /cấu hình môi trường/);
  assert.equal(describe({ ...base, credentials: [active] }, now).action, 'settings');
  assert.equal(describe({ ...base, integrationEnabled: true, credentials: [active] }, now).tone, 'success');
});
test('a live verification belongs to the requesting Admin and expires exactly at its deadline', () => {
  const state = { ...base, credentials: [{ ...pending, verificationExpiresAtUtc: new Date(now + 15000).toISOString(), verificationRequestedByCurrentAdmin: true }] };
  assert.match(describe(state, now).title, /chờ bạn/);
  assert.equal(describe(state, now).remaining, 15);
  state.credentials[0].verificationRequestedByCurrentAdmin = false;
  assert.match(describe(state, now).title, /Admin khác/);
  assert.equal(describe(state, now + 15000).action, 'verify');
  assert.match(describe(state, now + 15000).title, /hết hạn/);
});
test('connections or pending posts block replacement verification even with an existing active version', () => {
  for (const counts of [{ connectedUserCount: 1 }, { pendingPublishJobCount: 1 }]) {
    const view = describe({ ...base, ...counts, credentials: [active, pending] }, now);
    assert.equal(view.action, 'guide');
    assert.equal(view.canRotate, false);
  }
});
test('environment lock never suggests a mutation regardless of credential state', () => {
  for (const credentials of [[], [active], [pending]]) {
    assert.equal(describe({ ...base, adminManagementEnabled: false, credentials }, now).action, 'refresh');
  }
});
test('public posting requires enabled runtime, explicit confirmation and valid evidence together', () => {
  const value = { enabled: false, auditedForPublicPosting: true, confirmAuditApproval: false, auditEvidence: 'short' };
  assert.deepEqual(Object.keys(validateSettings(value)).sort(), ['confirm', 'enabled', 'evidence']);
  assert.deepEqual(validateSettings({ ...value, enabled: true, confirmAuditApproval: true, auditEvidence: 'ticket-1234' }), {});
  assert.ok(validateSettings({ ...value, auditEvidence: 'ticket\n1234' }).evidence);
  assert.ok(validateSettings({ ...value, auditEvidence: 'a'.repeat(501) }).evidence);
  assert.deepEqual(validateSettings({ ...value, auditedForPublicPosting: false }), {});
});
test('settings conflict detection ignores changing metrics but catches credential and policy changes', () => {
  const state = { ...base, credentials: [active] };
  assert.equal(settingsKey(state), settingsKey({ ...state, connectedUserCount: 5 }));
  assert.notEqual(settingsKey(state), settingsKey({ ...state, integrationEnabled: true }));
  assert.notEqual(settingsKey(state), settingsKey({ ...state, auditEvidence: 'new ticket' }));
  assert.notEqual(settingsKey(state), settingsKey({ ...state, credentials: [{ ...active, credentialId: 'replacement' }] }));
});
test('unknown upstream errors are not echoed into explanatory copy', () => {
  assert.doesNotMatch(failureMessage('https://example.invalid/signed?secret=test'), /example|secret/);
  assert.match(failureMessage('scope_not_authorized'), /video.publish/);
});
