import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { readCompareRequest, sameCompareParams, writeCompareParams } from '../../core/comparison-url';
import { CompareRequest } from '../../core/models/comparison.models';
import { ComparisonApiService } from '../../core/services/comparison-api.service';
import { ComparisonStore } from '../../core/services/comparison-store.service';
import { MonacoDiffViewer } from '../../diff/monaco-diff-viewer/monaco-diff-viewer';
import { SplitPane } from '../../shared/components/split-pane/split-pane';
import { FileSizePipe } from '../../shared/pipes/file-size.pipe';
import { ComparisonSummaryView } from '../comparison-summary/comparison-summary';
import { ComparisonToolbar } from '../comparison-toolbar/comparison-toolbar';
import { ComparisonTree } from '../comparison-tree/comparison-tree';
import { PathSelector } from '../path-selector/path-selector';

/** Main screen: folder input, comparison tree and the Monaco diff for the selected file. */
@Component({
  selector: 'app-compare-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ComparisonSummaryView,
    ComparisonToolbar,
    ComparisonTree,
    FileSizePipe,
    MonacoDiffViewer,
    PathSelector,
    SplitPane,
  ],
  templateUrl: './compare-page.html',
  styleUrl: './compare-page.scss',
})
export class ComparePage {
  protected readonly store = inject(ComparisonStore);
  private readonly api = inject(ComparisonApiService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly maxDiffFileSizeBytes = signal<number | null>(null);
  protected readonly showWarnings = signal(false);

  /** The request the current URL describes, used to prefill the form and to rebuild the URL. */
  private readonly activeRequest = signal<CompareRequest | null>(null);

  protected readonly formLeftPath = computed(
    () => this.activeRequest()?.leftPath ?? this.store.result()?.leftPath ?? '',
  );

  protected readonly formRightPath = computed(
    () => this.activeRequest()?.rightPath ?? this.store.result()?.rightPath ?? '',
  );

  protected readonly formCalculateHashes = computed(() => this.activeRequest()?.calculateHashes);

  protected readonly formArchiveMaxDepth = computed(() => this.activeRequest()?.archiveMaxDepth);

  constructor() {
    this.api.health().subscribe({
      next: (health) => this.maxDiffFileSizeBytes.set(health.limits.maxDiffFileSizeBytes),
      error: () => this.maxDiffFileSizeBytes.set(null),
    });

    const snapshot = this.route.snapshot;
    const comparisonId = snapshot.paramMap.get('id');
    const urlRequest = readCompareRequest(snapshot.queryParamMap);
    this.activeRequest.set(urlRequest);

    if (comparisonId) {
      // Navigating to /compare/:id remounts this page; the store already holds that result.
      if (this.store.result()?.id !== comparisonId) {
        this.store.loadExisting(comparisonId, urlRequest ?? undefined);
      }
    } else if (urlRequest) {
      this.store.compare(urlRequest);
    }

    // Keep the URL in sync so a comparison can be reloaded, bookmarked or shared.
    effect(() => {
      const result = this.store.result();
      if (!result) {
        return;
      }

      const queryParams = writeCompareParams(
        this.activeRequest() ?? { leftPath: result.leftPath, rightPath: result.rightPath },
      );

      const current = this.route.snapshot;
      if (current.paramMap.get('id') === result.id && sameCompareParams(current.queryParamMap, queryParams)) {
        return;
      }

      void this.router.navigate(['/compare', result.id], { queryParams, replaceUrl: true });
    });
  }

  protected onCompare(request: CompareRequest): void {
    this.activeRequest.set(request);
    this.store.compare(request);
  }

  protected onCancel(): void {
    this.store.cancel();
  }
}
