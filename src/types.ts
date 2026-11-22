export type LaunchType = 'DirectExecutable' | 'Steam' | 'RiotClient' | 'ExternalLauncher';

export interface StorageLocation {
  id: string;
  displayName: string;
  uncRootPath: string;
  description: string;
  isReadOnlyForClients: boolean;
}

export interface GameEntry {
  id: string;
  name: string;
  category: string;
  description: string;
  developer: string;
  coverImagePath: string;
  heroImagePath: string;
  storageLocationId: string;
  executablePath: string;
  launchArguments: string;
  launchType: LaunchType;
  externalAppId: string;
  processNameToMonitor: string;
  isVisible: boolean;
  isFeatured: boolean;
  sortOrder: number;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface CatalogSnapshot {
  catalogVersion: number;
  publishedAtUtc: string;
  serverHostname: string;
  storageLocations: StorageLocation[];
  games: GameEntry[];
}

export interface AdminAuditLog {
  id: number;
  timestampUtc: string;
  action: string;
  targetGameId: string;
  targetGameName: string;
  workstationName: string;
  sourceIp: string;
  details: string;
}

export interface AdminStatus {
  isConfigured: boolean;
  usesEnvOverride: boolean;
  workstationAuthorized: boolean;
  detectedWorkstation: string;
  detectedClientIp: string;
  totalGames: number;
  visibleGames: number;
  lastBackupUtc: string;
}

export interface SolutionFileItem {
  path: string;
  sizeBytes: number;
}
