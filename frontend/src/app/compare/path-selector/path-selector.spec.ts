import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CompareRequest } from '../../core/models/comparison.models';
import { PathSelector } from './path-selector';

describe('PathSelector', () => {
  let fixture: ComponentFixture<PathSelector>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [PathSelector] }).compileComponents();
    fixture = TestBed.createComponent(PathSelector);
    await fixture.whenStable();
  });

  function input(testId: string): HTMLInputElement {
    return fixture.nativeElement.querySelector(`[data-testid="${testId}"]`) as HTMLInputElement;
  }

  function compareButton(): HTMLButtonElement {
    return fixture.nativeElement.querySelector('[data-testid="compare-button"]') as HTMLButtonElement;
  }

  async function type(testId: string, value: string): Promise<void> {
    const element = input(testId);
    element.value = value;
    element.dispatchEvent(new Event('input'));
    await fixture.whenStable();
  }

  it('disables the compare button until both paths are entered', async () => {
    expect(compareButton().disabled).toBe(true);

    await type('left-path', '/builds/a');
    expect(compareButton().disabled).toBe(true);

    await type('right-path', '/builds/b');
    expect(compareButton().disabled).toBe(false);
  });

  it('rejects whitespace-only paths', async () => {
    await type('left-path', '   ');
    await type('right-path', '   ');

    expect(compareButton().disabled).toBe(true);
  });

  it('emits the trimmed paths and options', async () => {
    let emitted: CompareRequest | undefined;
    fixture.componentInstance.compare.subscribe((request) => (emitted = request));

    await type('left-path', '  /builds/a  ');
    await type('right-path', '/builds/b');
    compareButton().click();
    await fixture.whenStable();

    expect(emitted).toEqual({
      leftPath: '/builds/a',
      rightPath: '/builds/b',
      calculateHashes: true,
      archiveMaxDepth: 1,
    });
  });

  it('shows a cancel button and disables comparing while busy', async () => {
    fixture.componentRef.setInput('busy', true);
    await type('left-path', '/builds/a');
    await type('right-path', '/builds/b');

    expect(compareButton().disabled).toBe(true);
    expect(compareButton().textContent).toContain('Comparing');
    expect(fixture.nativeElement.querySelector('[data-testid="cancel-button"]')).not.toBeNull();
  });
});
