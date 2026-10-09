import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { ComparisonStatus, STATUS_SYMBOLS } from '../../../core/models/comparison.models';

/** Renders the `+ - ~ =` status indicator used across the tree and summary. */
@Component({
  selector: 'app-status-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="badge" [class]="'badge--' + status().toLowerCase()" [attr.title]="status()">{{
    symbol()
  }}</span>`,
  styleUrl: './status-badge.scss',
})
export class StatusBadge {
  readonly status = input.required<ComparisonStatus>();

  readonly symbol = computed(() => STATUS_SYMBOLS[this.status()]);
}
