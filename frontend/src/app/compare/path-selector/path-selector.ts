import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CompareRequest } from '../../core/models/comparison.models';

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
  readonly initialLeftPath = input('');
  readonly initialRightPath = input('');

  readonly compare = output<CompareRequest>();
  readonly cancel = output<void>();

  readonly leftPath = signal('');
  readonly rightPath = signal('');
  readonly calculateHashes = signal(true);
  readonly archiveMaxDepth = signal(1);

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
