export type SetupComponentState = 'UNKNOWN' | 'NOT_INSTALLED' | 'NEEDS_VERIFICATION' | 'READY' | 'REPAIR_REQUIRED' | 'UNSUPPORTED' | 'DISABLED';
export interface SetupComponent {
  id: string; name: string; version: string; state: SetupComponentState; message: string;
  canInstall: boolean; canVerify: boolean; canRepair: boolean;
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

export const requiredSetupComponents = (snapshot?: SetupSnapshot | null) =>
  snapshot?.components.filter(component => component.state !== 'DISABLED') ?? [];

export const isSystemSetupReady = (snapshot?: SetupSnapshot | null) =>
  Boolean(snapshot) && requiredSetupComponents(snapshot).every(component => component.state === 'READY');

export const needsApplicationRepair = (snapshot?: SetupSnapshot | null) =>
  requiredSetupComponents(snapshot).some(component =>
    component.state === 'REPAIR_REQUIRED' && !component.canInstall && component.canRepair);

export function acceptSetupSnapshot(current: SetupSnapshot | null, incoming: SetupSnapshot,
  organizationId: string, fromGet: boolean): SetupSnapshot | null {
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
