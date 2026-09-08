import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { routes } from '../../app.routes';
import { ComparisonNode, ComparisonResult, FileContentResult } from '../../core/models/comparison.models';
import { ComparisonStore } from '../../core/services/comparison-store.service';
import { MonacoLoaderService } from '../../core/services/monaco-loader.service';
import { ComparePage } from './compare-page';

const modifiedFile: ComparisonNode = {
  name: 'config.json',
  relativePath: '/config.json',
  type: 'File',
  status: 'Modified',
  leftSize: 10,
  rightSize: 12,
  canCompareContent: true,
};

const largeFile: ComparisonNode = {
  name: 'huge.log',
  relativePath: '/huge.log',
  type: 'File',
  status: 'Modified',
  leftSize: 9_000_000,
  rightSize: 9_500_000,
  canCompareContent: false,
  contentUnavailableReason: 'File exceeds the 5 MB comparison limit.',
};

function result(overrides: Partial<ComparisonResult> = {}): ComparisonResult {
  return {
    id: 'cmp-1',
    status: 'Completed',
    leftPath: '/a',
    rightPath: '/b',
    startedAt: new Date().toISOString(),
    durationMs: 42,
    summary: {
      total: 2,
      added: 0,
      removed: 0,
      modified: 2,
      unchanged: 5,
      folders: 0,
      files: 2,
      archives: 0,
      archiveEntries: 0,
    },
    progress: { phase: 'Completed', scannedFiles: 2, scannedFolders: 0, scannedArchives: 0, comparedNodes: 2 },
    root: {
      name: '/',
      relativePath: '/',
      type: 'Folder',
      status: 'Modified',
      canCompareContent: false,
      children: [modifiedFile, largeFile],
    },
    warnings: [],
    ...overrides,
  };
}

describe('ComparePage', () => {
  let fixture: ComponentFixture<ComparePage>;
  let http: HttpTestingController;
  let store: ComparisonStore;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ComparePage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter(routes),
        // Monaco is loaded from static assets, which jsdom cannot execute.
        { provide: MonacoLoaderService, useValue: { load: () => new Promise(() => {}) } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ComparePage);
    store = TestBed.inject(ComparisonStore);
    http = TestBed.inject(HttpTestingController);

    await fixture.whenStable();
    http.expectOne('/api/health').flush({
      status: 'ok',
      limits: {
        maxDiffFileSizeBytes: 5 * 1024 * 1024,
        archiveMaxDepth: 1,
        maxArchiveEntries: 10000,
        caseSensitive: true,
        restrictedToAllowedRoots: false,
      },
    });
    await fixture.whenStable();
  });

  afterEach(() => http.verify({ ignoreCancelled: true }));

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  function query(testId: string): HTMLElement | null {
    return (fixture.nativeElement as HTMLElement).querySelector(`[data-testid="${testId}"]`);
  }

  async function complete(overrides: Partial<ComparisonResult> = {}): Promise<void> {
    store.compare({ leftPath: '/a', rightPath: '/b' });
    http.expectOne('/api/comparisons').flush(result(overrides));
    await fixture.whenStable();
  }

  it('shows the folder inputs before a comparison runs', () => {
    expect(query('left-path')).not.toBeNull();
    expect(query('right-path')).not.toBeNull();
    expect(query('summary')).toBeNull();
  });

  it('shows progress while the comparison is running', async () => {
    store.compare({ leftPath: '/a', rightPath: '/b' });
    await fixture.whenStable();

    expect(query('progress-banner')).not.toBeNull();

    http.expectOne('/api/comparisons').flush(result());
    await fixture.whenStable();

    expect(query('progress-banner')).toBeNull();
  });

  it('renders the summary once the comparison completes', async () => {
    await complete();

    expect(query('summary')).not.toBeNull();
    expect(text()).toContain('Modified: 2');
    expect(text()).toContain('Same: 5');
  });

  it('renders the comparison tree', async () => {
    await complete();
    expect(query('comparison-tree')).not.toBeNull();
  });

  it('shows the backend error when a folder does not exist', async () => {
    store.compare({ leftPath: '/missing', rightPath: '/b' });
    http
      .expectOne('/api/comparisons')
      .flush({ code: 'PATH_NOT_FOUND', message: 'The left folder does not exist.' }, { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();

    expect(query('error-banner')).not.toBeNull();
    expect(text()).toContain('The left folder does not exist.');
  });

  it('explains why a file that is too large cannot be opened', async () => {
    await complete();

    store.select(largeFile);
    await fixture.whenStable();

    expect(query('not-comparable')?.textContent).toContain('5 MB comparison limit');
  });

  it('loads the diff content for a modified file', async () => {
    await complete();

    store.select(modifiedFile);
    await fixture.whenStable();

    const content: FileContentResult = {
      relativePath: '/config.json',
      leftContent: '{ "a": 1 }',
      rightContent: '{ "a": 2 }',
      language: 'json',
      leftSize: 10,
      rightSize: 12,
      canCompareContent: true,
    };

    http.expectOne((request) => request.url === '/api/comparisons/cmp-1/content').flush(content);
    await fixture.whenStable();

    expect(query('monaco-host')).not.toBeNull();
  });

  it('surfaces content errors returned by the backend', async () => {
    await complete();

    store.select(modifiedFile);
    await fixture.whenStable();

    http
      .expectOne((request) => request.url === '/api/comparisons/cmp-1/content')
      .flush(
        { code: 'FILE_DISAPPEARED', message: 'The file is no longer available; run the comparison again.' },
        { status: 409, statusText: 'Conflict' },
      );
    await fixture.whenStable();

    expect(query('content-error')?.textContent).toContain('no longer available');
  });

  it('lists scan warnings', async () => {
    await complete({ warnings: ['left: /broken.zip: The archive is corrupted or not a valid ZIP file.'] });

    expect(text()).toContain('1 warning(s) during the scan');
  });
});
