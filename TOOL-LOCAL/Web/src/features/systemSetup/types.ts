export type SetupComponentState = 'UNKNOWN' | 'NOT_INSTALLED' | 'NEEDS_VERIFICATION' | 'READY' | 'REPAIR_REQUIRED' | 'UNSUPPORTED' | 'DISABLED';
export interface SetupComponent {
  id: string; name: string; version: string; state: SetupComponentState; message: string;
  canInstall: boolean; canVerify: boolean; canRepair: boolean;
  canPrepareOffline?: boolean;
  downloadBytes?: number | null; minimumFreeDiskBytes?: number | null; errorCode?: string | null;
  checkedAtUtc?: string | null;
  resourceProfileId?: string | null;
}
export interface SetupOperation {
  operationId: string; mode: string; state: string; componentIds: string[]; sequence: number;
  currentComponent?: string | null; stage?: string | null; percent?: number | null;
  bytesProcessed?: number | null; totalBytes?: number | null;
  allSelectedReady: boolean; allRequiredReady: boolean;
}
export interface SetupSnapshot {
  revision?: number;
  contextGeneration: string; organizationId?: string | null; components: SetupComponent[];
  operation?: SetupOperation | null;
  startupRequired?: boolean;
}
export interface SetupRequest {
  operationId: string; expectedOrganizationId: string; contextGeneration: string;
  componentIds: string[]; previousOperationId?: string; resourceWarningAccepted?: boolean;
  confirmedResourceProfileId?: string;
}
export const setupRunning = (operation?: SetupOperation | null) =>
  operation?.state === 'Accepted' || operation?.state === 'Running';

const componentIds = ['media', 'ocr', 'qwen', 'piper'];
const componentStates = ['UNKNOWN', 'NOT_INSTALLED', 'NEEDS_VERIFICATION', 'READY', 'REPAIR_REQUIRED', 'UNSUPPORTED', 'DISABLED'];
const operationStates = ['Accepted', 'Running', 'Completed', 'PartiallyCompleted', 'Failed', 'Cancelled', 'Interrupted'];
const record = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value);
const text = (value: unknown): value is string => typeof value === 'string';
const optionalText = (value: unknown) => value == null || text(value);
const counter = (value: unknown) => Number.isSafeInteger(value) && Number(value) >= 0;
const optionalCounter = (value: unknown) => value == null || counter(value);

// The host always supplies these four rows, including rows disabled by policy.
// A partial response cannot prove readiness, even if all supplied rows are READY.
export function isCompleteSetupSnapshot(value: unknown): value is SetupSnapshot {
  if (!record(value) || !text(value.contextGeneration) || !value.contextGeneration.trim()
    || !optionalText(value.organizationId) || !optionalCounter(value.revision)
    || (value.startupRequired !== undefined && typeof value.startupRequired !== 'boolean')
    || !Array.isArray(value.components) || value.components.length !== componentIds.length) return false;
  const seen = new Set<string>();
  for (const component of value.components) {
    if (!record(component) || !text(component.id) || !componentIds.includes(component.id) || seen.has(component.id)
      || !text(component.name) || !text(component.version) || !text(component.message)
      || !text(component.state) || !componentStates.includes(component.state)
      || typeof component.canInstall !== 'boolean' || typeof component.canVerify !== 'boolean'
      || typeof component.canRepair !== 'boolean'
      || (component.canPrepareOffline !== undefined && typeof component.canPrepareOffline !== 'boolean')
      || !optionalCounter(component.downloadBytes) || !optionalCounter(component.minimumFreeDiskBytes)
      || !optionalText(component.errorCode) || !optionalText(component.checkedAtUtc)
      || !optionalText(component.resourceProfileId)) return false;
    seen.add(component.id);
  }
  const operation = value.operation;
  if (operation == null) return true;
  return record(operation) && text(operation.operationId) && Boolean(operation.operationId.trim())
    && text(operation.mode) && ['check', 'start', 'retry'].includes(operation.mode)
    && text(operation.state) && operationStates.includes(operation.state) && counter(operation.sequence)
    && Array.isArray(operation.componentIds) && operation.componentIds.length > 0
    && operation.componentIds.every(id => text(id) && componentIds.includes(id))
    && new Set(operation.componentIds).size === operation.componentIds.length
    && optionalText(operation.currentComponent) && optionalText(operation.stage)
    && (operation.percent == null || (typeof operation.percent === 'number'
      && Number.isFinite(operation.percent) && operation.percent >= 0 && operation.percent <= 100))
    && optionalCounter(operation.bytesProcessed) && optionalCounter(operation.totalBytes)
    && typeof operation.allSelectedReady === 'boolean' && typeof operation.allRequiredReady === 'boolean';
}

export const requiredSetupComponents = (snapshot?: SetupSnapshot | null) =>
  isCompleteSetupSnapshot(snapshot) ? snapshot.components.filter(component => component.state !== 'DISABLED') : [];

export const isSystemSetupReady = (snapshot?: SetupSnapshot | null) =>
  isCompleteSetupSnapshot(snapshot) && requiredSetupComponents(snapshot).every(component => component.state === 'READY');

export const needsApplicationRepair = (snapshot?: SetupSnapshot | null) =>
  requiredSetupComponents(snapshot).some(component =>
    component.state === 'REPAIR_REQUIRED' && !component.canInstall && component.canRepair);

export function acceptSetupSnapshot(current: SetupSnapshot | null, incoming: SetupSnapshot,
  organizationId: string, fromGet: boolean): SetupSnapshot | null {
  if (!isCompleteSetupSnapshot(incoming)) return current;
  if (incoming.organizationId !== organizationId) return current;
  if (current?.contextGeneration === incoming.contextGeneration && (incoming.revision ?? 0) < (current?.revision ?? 0)) return current;
  if (current && incoming.contextGeneration !== current.contextGeneration && !fromGet) return current;
  if (current?.contextGeneration === incoming.contextGeneration && current.operation && incoming.operation) {
    if (current.operation.operationId === incoming.operation.operationId) {
      if (incoming.operation.sequence < current.operation.sequence) return current;
      if (!setupRunning(current.operation) && setupRunning(incoming.operation)) return current;
    } else if (!fromGet) return current;
  }
  return incoming;
}
