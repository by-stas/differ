import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ComparisonStatus } from '../../core/models/comparison.models';
import { ComparisonStore } from '../../core/services/comparison-store.service';

/** Status filters, name search and expand/collapse controls for the comparison tree. */
@Component({
  selector: 'app-comparison-toolbar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './comparison-toolbar.html',
  styleUrl: './comparison-toolbar.scss',
})
export class ComparisonToolbar {
  protected readonly store = inject(ComparisonStore);

  protected readonly statuses: ComparisonStatus[] = ['Added', 'Removed', 'Modified', 'Unchanged'];

  protected isEnabled(status: ComparisonStatus): boolean {
    return this.store.statusFilter().has(status);
  }

  protected toggle(status: ComparisonStatus): void {
    this.store.toggleStatusFilter(status);
  }
}
