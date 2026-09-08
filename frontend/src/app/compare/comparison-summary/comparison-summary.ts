import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { ComparisonSummary } from '../../core/models/comparison.models';

/** Counts of added, removed, modified and unchanged entries for the whole comparison. */
@Component({
  selector: 'app-comparison-summary',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './comparison-summary.html',
  styleUrl: './comparison-summary.scss',
})
export class ComparisonSummaryView {
  readonly summary = input.required<ComparisonSummary>();
  readonly durationMs = input<number | null | undefined>(null);
}
