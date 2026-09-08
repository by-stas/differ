import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ApiError } from '../models/comparison.models';
import { ComparisonApiService } from './comparison-api.service';

describe('ComparisonApiService', () => {
  let service: ComparisonApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(ComparisonApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('posts both paths when starting a comparison', () => {
    service.startComparison({ leftPath: '/a', rightPath: '/b' }).subscribe();

    const request = http.expectOne('/api/comparisons');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ leftPath: '/a', rightPath: '/b' });
    request.flush({});
  });

  it('passes archive virtual paths through as a query parameter', () => {
    service.getContent('cmp-1', '/package.zip!/config.json').subscribe();

    const request = http.expectOne((candidate) => candidate.url === '/api/comparisons/cmp-1/content');
    expect(request.request.params.get('path')).toBe('/package.zip!/config.json');
    request.flush({});
  });

  it('surfaces the structured error returned by the backend', async () => {
    const failure = new Promise<ApiError>((resolve) => {
      service.startComparison({ leftPath: '/a', rightPath: '/b' }).subscribe({ error: resolve });
    });

    http
      .expectOne('/api/comparisons')
      .flush({ code: 'PATH_NOT_FOUND', message: 'The left folder does not exist.' }, { status: 404, statusText: 'Not Found' });

    await expect(failure).resolves.toEqual({
      code: 'PATH_NOT_FOUND',
      message: 'The left folder does not exist.',
    });
  });

  it('reports an unreachable backend as a network error', async () => {
    const failure = new Promise<ApiError>((resolve) => {
      service.health().subscribe({ error: resolve });
    });

    http.expectOne('/api/health').error(new ProgressEvent('error'), { status: 0 });

    await expect(failure).resolves.toMatchObject({ code: 'NETWORK_ERROR' });
  });
});
