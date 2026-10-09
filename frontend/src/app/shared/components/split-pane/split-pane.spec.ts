import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SplitPane } from './split-pane';

const STORAGE_KEY = 'test.split';

@Component({
  imports: [SplitPane],
  template: `
    <app-split-pane [storageKey]="storageKey" [minStart]="200" [minEnd]="300">
      <div splitStart data-testid="start">tree</div>
      <div splitEnd data-testid="end">detail</div>
    </app-split-pane>
  `,
})
class SplitPaneHost {
  storageKey: string | null = null;
}

describe('SplitPane', () => {
  let fixture: ComponentFixture<SplitPaneHost>;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ imports: [SplitPaneHost] });
  });

  /** jsdom has no layout, so the splitter is given a 1000 x 600 container to divide. */
  async function create(storageKey: string | null = null): Promise<void> {
    fixture = TestBed.createComponent(SplitPaneHost);
    fixture.componentInstance.storageKey = storageKey;

    Object.defineProperty(pane(), 'getBoundingClientRect', {
      value: () => ({ left: 0, top: 0, width: 1000, height: 600, right: 1000, bottom: 600 }),
    });

    await fixture.whenStable();
  }

  function pane(): HTMLElement {
    return fixture.nativeElement.querySelector('app-split-pane') as HTMLElement;
  }

  function handle(): HTMLElement {
    return fixture.nativeElement.querySelector('[data-testid="split-handle"]') as HTMLElement;
  }

  function startSize(): string {
    return pane().style.getPropertyValue('--split-start');
  }

  async function press(key: string): Promise<void> {
    handle().dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true }));
    await fixture.whenStable();
  }

  async function drag(clientX: number, clientY = 0): Promise<void> {
    handle().dispatchEvent(new MouseEvent('pointerdown', { bubbles: true }));
    handle().dispatchEvent(new MouseEvent('pointermove', { bubbles: true, clientX, clientY }));
    await fixture.whenStable();
  }

  it('puts the handle between the two panes', async () => {
    await create();

    const children = Array.from(pane().children).map((child) => child.getAttribute('data-testid'));
    expect(children).toEqual(['start', 'split-handle', 'end']);
  });

  it('describes itself as a separator', async () => {
    await create();

    expect(handle().getAttribute('role')).toBe('separator');
    expect(handle().getAttribute('aria-orientation')).toBe('vertical');
    expect(handle().getAttribute('aria-valuenow')).toBe('34');
    expect(handle().tabIndex).toBe(0);
  });

  it('starts at the initial fraction', async () => {
    await create();

    expect(startSize()).toBe('34%');
  });

  it('resizes while the handle is dragged', async () => {
    await create();

    await drag(600);
    expect(startSize()).toBe('60%');
    expect(handle().getAttribute('aria-valuenow')).toBe('60');

    await drag(250);
    expect(startSize()).toBe('25%');
  });

  it('ignores pointer movement that did not start on the handle', async () => {
    await create();

    handle().dispatchEvent(new MouseEvent('pointermove', { bubbles: true, clientX: 600 }));
    await fixture.whenStable();

    expect(startSize()).toBe('34%');
  });

  it('keeps both panes above their minimum size', async () => {
    await create();

    await drag(20);
    expect(startSize()).toBe('20%');

    await drag(990);
    expect(startSize()).toBe('70%');
  });

  it('moves with the arrow keys and jumps to the limits with Home and End', async () => {
    await create();

    await press('ArrowRight');
    expect(startSize()).toBe('36%');

    await press('ArrowLeft');
    await press('ArrowLeft');
    expect(startSize()).toBe('32%');

    await press('End');
    expect(startSize()).toBe('70%');

    await press('Home');
    expect(startSize()).toBe('20%');
  });

  it('leaves other keys alone', async () => {
    await create();

    await press('ArrowUp');
    await press('Tab');

    expect(startSize()).toBe('34%');
  });

  it('restores the default size on double-click and on Escape', async () => {
    await create();

    await drag(600);
    handle().dispatchEvent(new MouseEvent('dblclick', { bubbles: true }));
    await fixture.whenStable();
    expect(startSize()).toBe('34%');

    await drag(600);
    await press('Escape');
    expect(startSize()).toBe('34%');
  });

  it('remembers the size the user chose', async () => {
    await create(STORAGE_KEY);

    await drag(600);

    expect(localStorage.getItem(STORAGE_KEY)).toBe('0.6000');
  });

  it('starts from the remembered size', async () => {
    localStorage.setItem(STORAGE_KEY, '0.5');

    await create(STORAGE_KEY);

    expect(startSize()).toBe('50%');
  });

  it('ignores a remembered size that is not usable', async () => {
    localStorage.setItem(STORAGE_KEY, 'wide please');

    await create(STORAGE_KEY);

    expect(startSize()).toBe('34%');
  });

  it('does not touch storage without a key', async () => {
    await create();

    await drag(600);

    expect(localStorage.length).toBe(0);
  });
});
