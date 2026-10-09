import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, throwError } from 'rxjs';
import {
  ApiError,
  CompareRequest,
  ComparisonResult,
  FileContentResult,
  HealthResult,
} from '../models/comparison.models';

/** Thin HTTP layer over the comparison API that normalizes failures into {@link ApiError}. */
@Injectable({ providedIn: 'root' })
export class ComparisonApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api';

  health(): Observable<HealthResult> {
    return this.http.get<HealthResult>(`${this.baseUrl}/health`).pipe(catchError(toApiError));
  }

  startComparison(request: CompareRequest): Observable<ComparisonResult> {
    return this.http
      .post<ComparisonResult>(`${this.baseUrl}/comparisons`, request)
      .pipe(catchError(toApiError));
  }

  getComparison(comparisonId: string): Observable<ComparisonResult> {
    return this.http
      .get<ComparisonResult>(`${this.baseUrl}/comparisons/${encodeURIComponent(comparisonId)}`)
      .pipe(catchError(toApiError));
  }

  cancelComparison(comparisonId: string): Observable<void> {
    return this.http
      .delete<void>(`${this.baseUrl}/comparisons/${encodeURIComponent(comparisonId)}`)
      .pipe(catchError(toApiError));
  }

  /** Works for files on disk and for virtual archive paths such as `/package.zip!/config.json`. */
  getContent(comparisonId: string, relativePath: string): Observable<FileContentResult> {
    return this.http
      .get<FileContentResult>(`${this.baseUrl}/comparisons/${encodeURIComponent(comparisonId)}/content`, {
        params: new HttpParams().set('path', relativePath),
      })
      .pipe(catchError(toApiError));
  }
}

function toApiError(error: HttpErrorResponse): Observable<never> {
  if (error.error && typeof error.error === 'object' && 'code' in error.error) {
    return throwError(() => error.error as ApiError);
  }

  if (error.status === 0) {
    return throwError(
      () =>
        ({
          code: 'NETWORK_ERROR',
          message: 'The comparison service could not be reached. Is the backend running?',
        }) satisfies ApiError,
    );
  }

  return throwError(
    () =>
      ({
        code: 'UNEXPECTED_ERROR',
        message: error.message || 'An unexpected error occurred.',
      }) satisfies ApiError,
  );
}
