import { ChangeDetectionStrategy, Component, computed, input, linkedSignal, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CompareRequest } from '../../core/models/comparison.models';

/** Used when the caller does not specify a starting value, and mirrors the backend defaults. */
export const DEFAULT_CALCULATE_HASHES = true;
export const DEFAULT_ARCHIVE_MAX_DEPTH = 1;

/** Collects the two folder paths and the comparison options. */
@Component({
  selector: 'app-path-selector',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './path-selector.html',
  styleUrl: './path-selector.scss',
})
export class PathSelector {
  readonly busy = input(false);

  /**
   * Starting values, typically taken from the URL. The fields stay editable: each one follows
   * its input until the user types, and follows it again when the input changes.
   */
  readonly initialLeftPath = input('');
  readonly initialRightPath = input('');
  readonly initialCalculateHashes = input<boolean | undefined>(undefined);
  readonly initialArchiveMaxDepth = input<number | undefined>(undefined);

  readonly compare = output<CompareRequest>();
  readonly cancel = output<void>();

  readonly leftPath = linkedSignal(() => this.initialLeftPath());
  readonly rightPath = linkedSignal(() => this.initialRightPath());
  readonly calculateHashes = linkedSignal(() => this.initialCalculateHashes() ?? DEFAULT_CALCULATE_HASHES);
  readonly archiveMaxDepth = linkedSignal(() => this.initialArchiveMaxDepth() ?? DEFAULT_ARCHIVE_MAX_DEPTH);

  readonly canSubmit = computed(
    () => this.leftPath().trim().length > 0 && this.rightPath().trim().length > 0 && !this.busy(),
  );

  submit(): void {
    if (!this.canSubmit()) {
      return;
    }

    this.compare.emit({
      leftPath: this.leftPath().trim(),
      rightPath: this.rightPath().trim(),
      calculateHashes: this.calculateHashes(),
      archiveMaxDepth: this.archiveMaxDepth(),
    });
  }
}
