/** Mirrors the contracts exposed by the ASP.NET Core comparison API. */

export type NodeType = 'Folder' | 'File' | 'Archive' | 'ArchiveFolder' | 'ArchiveFile';

export type ComparisonStatus = 'Unchanged' | 'Added' | 'Removed' | 'Modified';

export type ComparisonState = 'Pending' | 'Running' | 'Completed' | 'Failed' | 'Cancelled';

export interface ComparisonNode {
  name: string;
  relativePath: string;
  type: NodeType;
  status: ComparisonStatus;
  leftSize?: number | null;
  rightSize?: number | null;
  canCompareContent: boolean;
  contentUnavailableReason?: string | null;
  error?: string | null;
  children?: ComparisonNode[] | null;
}

export interface ComparisonSummary {
  total: number;
  added: number;
  removed: number;
  modified: number;
  unchanged: number;
  folders: number;
  files: number;
  archives: number;
  archiveEntries: number;
}

export interface ComparisonProgress {
  phase: string;
  scannedFiles: number;
  scannedFolders: number;
  scannedArchives: number;
  comparedNodes: number;
  currentPath?: string | null;
}

export interface ComparisonResult {
  id: string;
  status: ComparisonState;
  leftPath: string;
  rightPath: string;
  startedAt: string;
  completedAt?: string | null;
  durationMs?: number | null;
  summary?: ComparisonSummary | null;
  progress: ComparisonProgress;
  root?: ComparisonNode | null;
  warnings: string[];
  error?: ApiError | null;
}

export interface FileContentResult {
  relativePath: string;
  leftContent?: string | null;
  rightContent?: string | null;
  language: string;
  leftSize?: number | null;
  rightSize?: number | null;
  leftEncoding?: string | null;
  rightEncoding?: string | null;
  canCompareContent: boolean;
  reason?: string | null;
}

export interface ApiError {
  code: string;
  message: string;
  detail?: string | null;
}

export interface CompareRequest {
  leftPath: string;
  rightPath: string;
  calculateHashes?: boolean;
  archiveMaxDepth?: number;
}

export interface ApiLimits {
  maxDiffFileSizeBytes: number;
  archiveMaxDepth: number;
  maxArchiveEntries: number;
  caseSensitive: boolean;
  restrictedToAllowedRoots: boolean;
}

export interface HealthResult {
  status: string;
  version?: string;
  limits: ApiLimits;
}

export const CONTAINER_TYPES: ReadonlySet<NodeType> = new Set<NodeType>(['Folder', 'Archive', 'ArchiveFolder']);

export function isContainer(node: ComparisonNode): boolean {
  return CONTAINER_TYPES.has(node.type);
}

/** Symbols used throughout the tree: + added, - removed, ~ modified, = unchanged. */
export const STATUS_SYMBOLS: Record<ComparisonStatus, string> = {
  Added: '+',
  Removed: '-',
  Modified: '~',
  Unchanged: '=',
};
