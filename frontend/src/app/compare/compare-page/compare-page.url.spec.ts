import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from '../../app.routes';
import { ComparisonResult } from '../../core/models/comparison.models';
import { MonacoLoaderService } from '../../core/services/monaco-loader.service';

function result(overrides: Partial<ComparisonResult> = {}): ComparisonResult {
  return {
    id: 'cmp-1',
    status: 'Completed',
    leftPath: '/builds/a',
    rightPath: '/builds/b',
    startedAt: new Date().toISOString(),
    durationMs: 7,
    summary: {
      total: 1,
      added: 0,
      removed: 0,
      modified: 1,
      unchanged: 0,
      folders: 0,
      files: 1,
      archives: 0,
      archiveEntries: 0,
    },
    progress: { phase: 'Completed', scannedFiles: 1, scannedFolders: 0, scannedArchives: 0, comparedNodes: 1 },
    root: {
      name: '/',
      relativePath: '/',
      type: 'Folder',
      status: 'Modified',
      canCompareContent: false,
      children: [
        {
          name: 'config.json',
          relativePath: '/config.json',
          type: 'File',
          status: 'Modified',
          leftSize: 10,
          rightSize: 12,
          canCompareContent: true,
        },
      ],
    },
    warnings: [],
    ...overrides,
  };
}

describe('ComparePage URL', () => {
  let http: HttpTestingController;
  let router: Router;
  let harness: RouterTestingHarness;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter(routes),
        // Monaco is loaded from static assets, which jsdom cannot execute.
        { provide: MonacoLoaderService, useValue: { load: () => new Promise(() => {}) } },
      ],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    harness = await RouterTestingHarness.create();
  });

  afterEach(() => http.verify({ ignoreCancelled: true }));

  /** Every page instance asks the backend for its limits; those answers are not under test. */
  function flushHealth(): void {
    for (const request of http.match('/api/health')) {
      request.flush({
        status: 'ok',
        limits: {
          maxDiffFileSizeBytes: 5 * 1024 * 1024,
          archiveMaxDepth: 1,
          maxArchiveEntries: 10000,
          caseSensitive: true,
          restrictedToAllowedRoots: false,
        },
      });
    }
  }

  async function navigate(url: string): Promise<void> {
    await harness.navigateByUrl(url);
    flushHealth();
    await harness.fixture.whenStable();
  }

  function queryParams(): Record<string, string> {
    return router.parseUrl(router.url).queryParams;
  }

  function field(testId: string): HTMLInputElement {
    return harness.fixture.nativeElement.querySelector(`[data-testid="${testId}"]`) as HTMLInputElement;
  }

  it('starts the comparison described by the URL', async () => {
    await navigate('/?left=/builds/a&right=/builds/b&depth=2&hashes=false');

    const started = http.expectOne('/api/comparisons');
    expect(started.request.body).toEqual({
      leftPath: '/builds/a',
      rightPath: '/builds/b',
      calculateHashes: false,
      archiveMaxDepth: 2,
    });

    started.flush(result());
    await harness.fixture.whenStable();
    flushHealth();

    expect(harness.fixture.nativeElement.querySelector('[data-testid="comparison-tree"]')).not.toBeNull();
  });

  it('prefills the form from the URL', async () => {
    await navigate('/?left=/builds/a&right=/builds/b&depth=2&hashes=false');

    expect(field('left-path').value).toBe('/builds/a');
    expect(field('right-path').value).toBe('/builds/b');

    const select = harness.fixture.nativeElement.querySelector('select') as HTMLSelectElement;
    expect(select.value).toBe('2');

    http.expectOne('/api/comparisons').flush(result());
    await harness.fixture.whenStable();
    flushHealth();
  });

  it('does nothing when the URL carries only one path', async () => {
    await navigate('/?left=/builds/a');

    http.expectNone('/api/comparisons');
    expect(field('left-path').value).toBe('');
  });

  it('puts the comparison and both paths in the URL once it is running', async () => {
    await navigate('/');

    field('left-path').value = '/builds/a';
    field('left-path').dispatchEvent(new Event('input'));
    field('right-path').value = '/builds/b';
    field('right-path').dispatchEvent(new Event('input'));
    await harness.fixture.whenStable();

    (harness.fixture.nativeElement.querySelector('[data-testid="compare-button"]') as HTMLButtonElement).click();
    http.expectOne('/api/comparisons').flush(result());
    await harness.fixture.whenStable();
    flushHealth();

    expect(router.url.startsWith('/compare/cmp-1')).toBe(true);
    expect(queryParams()).toEqual({
      left: '/builds/a',
      right: '/builds/b',
      hashes: 'true',
      depth: '1',
    });
  });

  it('loads an existing comparison when its link is opened', async () => {
    await navigate('/compare/cmp-1?left=/builds/a&right=/builds/b');

    http.expectOne('/api/comparisons/cmp-1').flush(result());
    await harness.fixture.whenStable();

    expect(harness.fixture.nativeElement.querySelector('[data-testid="comparison-tree"]')).not.toBeNull();
    expect(router.url.startsWith('/compare/cmp-1')).toBe(true);
    expect(queryParams()).toEqual({ left: '/builds/a', right: '/builds/b' });
  });

  it('runs the comparison again when the link points at an expired result', async () => {
    await navigate('/compare/cmp-old?left=/builds/a&right=/builds/b');

    http
      .expectOne('/api/comparisons/cmp-old')
      .flush(
        { code: 'COMPARISON_NOT_FOUND', message: 'The comparison was not found or has expired.' },
        { status: 404, statusText: 'Not Found' },
      );
    await harness.fixture.whenStable();

    const started = http.expectOne('/api/comparisons');
    expect(started.request.body).toEqual({ leftPath: '/builds/a', rightPath: '/builds/b' });

    started.flush(result());
    await harness.fixture.whenStable();
    flushHealth();

    expect(harness.fixture.nativeElement.querySelector('[data-testid="error-banner"]')).toBeNull();
    expect(router.url.startsWith('/compare/cmp-1')).toBe(true);
  });

  it('reports an expired result that cannot be re-run', async () => {
    await navigate('/compare/cmp-old');

    http
      .expectOne('/api/comparisons/cmp-old')
      .flush(
        { code: 'COMPARISON_NOT_FOUND', message: 'The comparison was not found or has expired.' },
        { status: 404, statusText: 'Not Found' },
      );
    await harness.fixture.whenStable();

    expect(harness.fixture.nativeElement.querySelector('[data-testid="error-banner"]')?.textContent).toContain(
      'expired',
    );
  });
});
