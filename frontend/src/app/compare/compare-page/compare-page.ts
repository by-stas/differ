import { ChangeDetectionStrategy, Component, effect, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { CompareRequest } from '../../core/models/comparison.models';
import { ComparisonApiService } from '../../core/services/comparison-api.service';
import { ComparisonStore } from '../../core/services/comparison-store.service';
import { MonacoDiffViewer } from '../../diff/monaco-diff-viewer/monaco-diff-viewer';
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

  constructor() {
    this.api.health().subscribe({
      next: (health) => this.maxDiffFileSizeBytes.set(health.limits.maxDiffFileSizeBytes),
      error: () => this.maxDiffFileSizeBytes.set(null),
    });

    const comparisonId = this.route.snapshot.paramMap.get('id');
    if (comparisonId) {
      this.store.loadExisting(comparisonId);
    }

    // Keep the URL in sync so a comparison can be reloaded or shared.
    effect(() => {
      const id = this.store.result()?.id;
      if (id && this.route.snapshot.paramMap.get('id') !== id) {
        void this.router.navigate(['/compare', id], { replaceUrl: true });
      }
    });
  }

  protected onCompare(request: CompareRequest): void {
    this.store.compare(request);
  }

  protected onCancel(): void {
    this.store.cancel();
  }
}
