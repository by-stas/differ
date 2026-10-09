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

  it('prefills the fields from the starting values', async () => {
    fixture.componentRef.setInput('initialLeftPath', '/builds/a');
    fixture.componentRef.setInput('initialRightPath', '/builds/b');
    fixture.componentRef.setInput('initialCalculateHashes', false);
    fixture.componentRef.setInput('initialArchiveMaxDepth', 2);
    await fixture.whenStable();

    expect(input('left-path').value).toBe('/builds/a');
    expect(input('right-path').value).toBe('/builds/b');

    let emitted: CompareRequest | undefined;
    fixture.componentInstance.compare.subscribe((request) => (emitted = request));
    compareButton().click();
    await fixture.whenStable();

    expect(emitted).toEqual({
      leftPath: '/builds/a',
      rightPath: '/builds/b',
      calculateHashes: false,
      archiveMaxDepth: 2,
    });
  });

  it('keeps what the user typed until the starting values change again', async () => {
    fixture.componentRef.setInput('initialLeftPath', '/builds/a');
    await fixture.whenStable();

    await type('left-path', '/typed');
    expect(input('left-path').value).toBe('/typed');

    fixture.componentRef.setInput('initialLeftPath', '/builds/c');
    await fixture.whenStable();

    expect(input('left-path').value).toBe('/builds/c');
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
