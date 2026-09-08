import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ComparisonNode, ComparisonResult } from '../models/comparison.models';
import { ComparisonStore } from './comparison-store.service';

function completedResult(root: ComparisonNode): ComparisonResult {
  return {
    id: 'cmp-1',
    status: 'Completed',
    leftPath: '/a',
    rightPath: '/b',
    startedAt: new Date().toISOString(),
    durationMs: 12,
    summary: {
      total: 2,
      added: 1,
      removed: 0,
      modified: 1,
      unchanged: 0,
      folders: 1,
      files: 1,
      archives: 0,
      archiveEntries: 0,
    },
    progress: { phase: 'Completed', scannedFiles: 2, scannedFolders: 1, scannedArchives: 0, comparedNodes: 2 },
    root,
    warnings: [],
  };
}

const modifiedFile: ComparisonNode = {
  name: 'config.json',
  relativePath: '/src/config.json',
  type: 'File',
  status: 'Modified',
  canCompareContent: true,
};

const root: ComparisonNode = {
  name: '/',
  relativePath: '/',
  type: 'Folder',
  status: 'Modified',
  canCompareContent: false,
  children: [
    {
      name: 'src',
      relativePath: '/src',
      type: 'Folder',
      status: 'Modified',
      canCompareContent: false,
      children: [modifiedFile],
    },
  ],
};

describe('ComparisonStore', () => {
  let store: ComparisonStore;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    store = TestBed.inject(ComparisonStore);
    http = TestBed.inject(HttpTestingController);
  });

  it('exposes the summary once a comparison completes', () => {
    store.compare({ leftPath: '/a', rightPath: '/b' });
    expect(store.isRunning()).toBe(true);

    http.expectOne('/api/comparisons').flush(completedResult(root));

    expect(store.isRunning()).toBe(false);
    expect(store.isCompleted()).toBe(true);
    expect(store.summary()?.added).toBe(1);
  });

  it('auto expands the containers that lead to a change', () => {
    store.compare({ leftPath: '/a', rightPath: '/b' });
    http.expectOne('/api/comparisons').flush(completedResult(root));

    expect([...store.expanded()]).toEqual(['/src']);
  });

  it('keeps the error returned when the folder does not exist', () => {
    store.compare({ leftPath: '/missing', rightPath: '/b' });

    http
      .expectOne('/api/comparisons')
      .flush({ code: 'PATH_NOT_FOUND', message: 'The left folder does not exist.' }, { status: 404, statusText: 'Not Found' });

    expect(store.error()?.code).toBe('PATH_NOT_FOUND');
    expect(store.isRunning()).toBe(false);
  });

  it('loads the content of a selected file that can be compared', () => {
    store.compare({ leftPath: '/a', rightPath: '/b' });
    http.expectOne('/api/comparisons').flush(completedResult(root));

    store.select(modifiedFile);
    expect(store.contentLoading()).toBe(true);

    http.expectOne((request) => request.url === '/api/comparisons/cmp-1/content').flush({
      relativePath: '/src/config.json',
      leftContent: 'a',
      rightContent: 'b',
      language: 'json',
      canCompareContent: true,
    });

    expect(store.contentLoading()).toBe(false);
    expect(store.content()?.language).toBe('json');
    expect(store.selectedPath()).toBe('/src/config.json');
  });

  it('does not request content for entries that cannot be compared', () => {
    store.compare({ leftPath: '/a', rightPath: '/b' });
    http.expectOne('/api/comparisons').flush(completedResult(root));

    store.select({ ...modifiedFile, canCompareContent: false, contentUnavailableReason: 'Too large' });

    http.expectNone((request) => request.url.endsWith('/content'));
    expect(store.content()).toBeNull();
  });

  it('toggles status filters', () => {
    expect(store.statusFilter().has('Unchanged')).toBe(false);

    store.toggleStatusFilter('Unchanged');
    expect(store.statusFilter().has('Unchanged')).toBe(true);

    store.toggleStatusFilter('Unchanged');
    expect(store.statusFilter().has('Unchanged')).toBe(false);
  });

  it('expands and collapses the whole tree', () => {
    store.compare({ leftPath: '/a', rightPath: '/b' });
    http.expectOne('/api/comparisons').flush(completedResult(root));

    store.collapseAll();
    expect(store.expanded().size).toBe(0);

    store.expandAll();
    expect([...store.expanded()]).toEqual(['/src']);
  });

  afterEach(() => {
    http.verify({ ignoreCancelled: true });
  });
});
