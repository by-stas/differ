import { BreakpointObserver } from '@angular/cdk/layout';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  effect,
  inject,
  input,
  linkedSignal,
  signal,
} from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { map, switchMap } from 'rxjs/operators';

/** How much one arrow key press moves the handle. */
const KEYBOARD_STEP = 0.02;

/**
 * Two panes separated by a handle the user can drag to give one of them more room. The panes are
 * projected, so the component only owns the geometry:
 *
 * ```html
 * <app-split-pane storageKey="differ.results">
 *   <div splitStart>…</div>
 *   <div splitEnd>…</div>
 * </app-split-pane>
 * ```
 *
 * Side by side when there is room, stacked on narrow screens, where the handle resizes heights
 * instead of widths.
 */
@Component({
  selector: 'app-split-pane',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './split-pane.html',
  styleUrl: './split-pane.scss',
  host: {
    '[class.split-pane--stacked]': 'stacked()',
    '[class.split-pane--dragging]': 'dragging()',
  },
})
export class SplitPane {
  /** Share of the container the first pane gets until the handle is moved. */
  readonly initialFraction = input(0.34);

  /** Smallest size, in pixels, each pane can be dragged down to. */
  readonly minStart = input(220);
  readonly minEnd = input(280);

  /** When set, the size the user chose is remembered for the next visit. */
  readonly storageKey = input<string | null>(null);

  /** Below this container width the panes stack instead of sitting side by side. */
  readonly stackBelow = input(900);

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly breakpoints = inject(BreakpointObserver);

  readonly stacked = toSignal(
    toObservable(computed(() => `(max-width: ${this.stackBelow() - 0.02}px)`)).pipe(
      switchMap((query) => this.breakpoints.observe(query)),
      map((state) => state.matches),
    ),
    { initialValue: false },
  );

  /** Follows the stored or initial size until the user drags the handle. */
  private readonly fraction = linkedSignal(() => readStoredFraction(this.storageKey()) ?? this.initialFraction());

  protected readonly dragging = signal(false);

  protected readonly percent = computed(() => Math.round(this.fraction() * 100));

  /** Kept to two decimals: enough for a smooth drag, short enough to read in the inspector. */
  private readonly startSize = computed(() => `${Math.round(this.fraction() * 10_000) / 100}%`);

  constructor() {
    effect(() => {
      this.host.nativeElement.style.setProperty('--split-start', this.startSize());
    });

    effect(() => {
      const key = this.storageKey();
      if (key) {
        writeStoredFraction(key, this.fraction());
      }
    });
  }

  protected onPointerDown(event: PointerEvent): void {
    const handle = event.target as HTMLElement;
    handle.setPointerCapture?.(event.pointerId);
    this.dragging.set(true);
    event.preventDefault();
  }

  protected onPointerMove(event: PointerEvent): void {
    if (!this.dragging()) {
      return;
    }

    const bounds = this.host.nativeElement.getBoundingClientRect();
    const offset = this.stacked() ? event.clientY - bounds.top : event.clientX - bounds.left;
    const total = this.stacked() ? bounds.height : bounds.width;

    this.resize(total === 0 ? this.fraction() : offset / total);
    event.preventDefault();
  }

  protected onPointerUp(event: PointerEvent): void {
    (event.target as HTMLElement).releasePointerCapture?.(event.pointerId);
    this.dragging.set(false);
  }

  protected onKeydown(event: KeyboardEvent): void {
    const back = this.stacked() ? 'ArrowUp' : 'ArrowLeft';
    const forward = this.stacked() ? 'ArrowDown' : 'ArrowRight';

    switch (event.key) {
      case back:
        this.resize(this.fraction() - KEYBOARD_STEP);
        break;
      case forward:
        this.resize(this.fraction() + KEYBOARD_STEP);
        break;
      case 'Home':
        this.resize(0);
        break;
      case 'End':
        this.resize(1);
        break;
      case 'Escape':
        this.reset();
        break;
      default:
        return;
    }

    event.preventDefault();
  }

  /** Double-clicking or pressing Escape on the handle restores the default size. */
  protected reset(): void {
    this.resize(this.initialFraction());
  }

  private resize(fraction: number): void {
    const bounds = this.host.nativeElement.getBoundingClientRect();
    const total = this.stacked() ? bounds.height : bounds.width;

    if (total <= 0) {
      this.fraction.set(clamp(fraction, 0, 1));
      return;
    }

    // Neither pane may be squeezed below its minimum, and the start pane gives way first when
    // the container itself is too small for both minimums.
    const min = clamp(this.minStart() / total, 0, 1);
    const max = clamp(1 - this.minEnd() / total, 0, 1);

    this.fraction.set(max < min ? max : clamp(fraction, min, max));
  }
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max);
}

function readStoredFraction(key: string | null): number | null {
  if (!key) {
    return null;
  }

  try {
    const stored = Number(localStorage.getItem(key));
    return Number.isFinite(stored) && stored > 0 && stored < 1 ? stored : null;
  } catch {
    // A browser that denies storage access should not break the layout.
    return null;
  }
}

function writeStoredFraction(key: string, fraction: number): void {
  try {
    localStorage.setItem(key, fraction.toFixed(4));
  } catch {
    // Ignored for the same reason.
  }
}
