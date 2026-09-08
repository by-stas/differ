import { Injectable, computed, inject, signal } from '@angular/core';
import { Subscription, timer } from 'rxjs';
import { switchMap } from 'rxjs/operators';
import {
  ApiError,
  ComparisonNode,
  ComparisonResult,
  ComparisonStatus,
  CompareRequest,
  FileContentResult,
  isContainer,
} from '../models/comparison.models';
import { ComparisonApiService } from './comparison-api.service';

/** Number of containers expanded automatically so large trees stay responsive. */
const AUTO_EXPAND_LIMIT = 400;

/**
 * Single source of truth for the compare screen: the comparison itself, the tree view state
 * (filters, search, expansion, selection) and the currently loaded file diff.
 */
@Injectable({ providedIn: 'root' })
export class ComparisonStore {
  private readonly api = inject(ComparisonApiService);
  private pollSubscription?: Subscription;

  readonly result = signal<ComparisonResult | null>(null);
  readonly starting = signal(false);
  readonly error = signal<ApiError | null>(null);

  readonly statusFilter = signal<ReadonlySet<ComparisonStatus>>(
    new Set<ComparisonStatus>(['Added', 'Removed', 'Modified']),
  );
  readonly search = signal('');
  readonly expanded = signal<ReadonlySet<string>>(new Set<string>());

  readonly selectedPath = signal<string | null>(null);
  readonly selectedNode = signal<ComparisonNode | null>(null);
  readonly content = signal<FileContentResult | null>(null);
  readonly contentLoading = signal(false);
  readonly contentError = signal<ApiError | null>(null);

  readonly isRunning = computed(() => {
    const status = this.result()?.status;
    return this.starting() || status === 'Pending' || status === 'Running';
  });

  readonly isCompleted = computed(() => this.result()?.status === 'Completed');

  readonly summary = computed(() => this.result()?.summary ?? null);

  readonly warnings = computed(() => this.result()?.warnings ?? []);

  compare(request: CompareRequest): void {
    this.reset();
    this.starting.set(true);

    this.api.startComparison(request).subscribe({
      next: (result) => {
        this.starting.set(false);
        this.applyResult(result);
        if (result.status === 'Pending' || result.status === 'Running') {
          this.startPolling(result.id);
        }
      },
      error: (error: ApiError) => {
        this.starting.set(false);
        this.error.set(error);
      },
    });
  }

  /** Loads a comparison that already exists on the server, e.g. after a page reload. */
  loadExisting(comparisonId: string): void {
    this.reset();
    this.starting.set(true);

    this.api.getComparison(comparisonId).subscribe({
      next: (result) => {
        this.starting.set(false);
        this.applyResult(result);
        if (result.status === 'Pending' || result.status === 'Running') {
          this.startPolling(result.id);
        }
      },
      error: (error: ApiError) => {
        this.starting.set(false);
        this.error.set(error);
      },
    });
  }

  cancel(): void {
    const id = this.result()?.id;
    if (!id) {
      return;
    }

    this.api.cancelComparison(id).subscribe({
      next: () => this.refresh(id),
      error: (error: ApiError) => this.error.set(error),
    });
  }

  select(node: ComparisonNode): void {
    this.selectedPath.set(node.relativePath);
    this.selectedNode.set(node);
    this.contentError.set(null);

    if (isContainer(node) || !node.canCompareContent) {
      this.content.set(null);
      return;
    }

    const comparisonId = this.result()?.id;
    if (!comparisonId) {
      return;
    }

    this.contentLoading.set(true);
    this.api.getContent(comparisonId, node.relativePath).subscribe({
      next: (content) => {
        this.contentLoading.set(false);
        this.content.set(content);
      },
      error: (error: ApiError) => {
        this.contentLoading.set(false);
        this.content.set(null);
        this.contentError.set(error);
      },
    });
  }

  toggleExpanded(path: string): void {
    const next = new Set(this.expanded());
    if (!next.delete(path)) {
      next.add(path);
    }

    this.expanded.set(next);
  }

  setExpanded(paths: Iterable<string>): void {
    this.expanded.set(new Set(paths));
  }

  collapseAll(): void {
    this.expanded.set(new Set<string>());
  }

  expandAll(): void {
    const root = this.result()?.root;
    if (!root) {
      return;
    }

    const paths: string[] = [];
    const walk = (node: ComparisonNode) => {
      for (const child of node.children ?? []) {
        if (isContainer(child)) {
          paths.push(child.relativePath);
          walk(child);
        }
      }
    };

    walk(root);
    this.expanded.set(new Set(paths));
  }

  toggleStatusFilter(status: ComparisonStatus): void {
    const next = new Set(this.statusFilter());
    if (!next.delete(status)) {
      next.add(status);
    }

    this.statusFilter.set(next);
  }

  setSearch(term: string): void {
    this.search.set(term);
  }

  clearError(): void {
    this.error.set(null);
  }

  private refresh(comparisonId: string): void {
    this.api.getComparison(comparisonId).subscribe({
      next: (result) => this.applyResult(result),
      error: (error: ApiError) => this.error.set(error),
    });
  }

  private startPolling(comparisonId: string): void {
    this.stopPolling();

    this.pollSubscription = timer(500, 800)
      .pipe(switchMap(() => this.api.getComparison(comparisonId)))
      .subscribe({
        next: (result) => {
          this.applyResult(result);
          if (result.status !== 'Pending' && result.status !== 'Running') {
            this.stopPolling();
          }
        },
        error: (error: ApiError) => {
          this.stopPolling();
          this.error.set(error);
        },
      });
  }

  private stopPolling(): void {
    this.pollSubscription?.unsubscribe();
    this.pollSubscription = undefined;
  }

  private applyResult(result: ComparisonResult): void {
    this.result.set(result);

    if (result.error) {
      this.error.set(result.error);
    }

    if (result.root && this.expanded().size === 0) {
      this.setExpanded(collectAutoExpandPaths(result.root));
    }
  }

  private reset(): void {
    this.stopPolling();
    this.result.set(null);
    this.error.set(null);
    this.content.set(null);
    this.contentError.set(null);
    this.selectedPath.set(null);
    this.selectedNode.set(null);
    this.expanded.set(new Set<string>());
  }
}

/**
 * Expands containers that lead to a difference, so the interesting part of the tree is visible
 * immediately without expanding an entire 100k-file structure.
 */
function collectAutoExpandPaths(root: ComparisonNode): string[] {
  const paths: string[] = [];

  const walk = (node: ComparisonNode) => {
    for (const child of node.children ?? []) {
      if (paths.length >= AUTO_EXPAND_LIMIT) {
        return;
      }

      if (isContainer(child) && child.status !== 'Unchanged') {
        paths.push(child.relativePath);
        walk(child);
      }
    }
  };

  walk(root);
  return paths;
}
