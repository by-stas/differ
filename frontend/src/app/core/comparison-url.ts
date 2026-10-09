import { ParamMap, Params } from '@angular/router';
import { CompareRequest } from './models/comparison.models';

/**
 * Query parameters that carry a comparison in the URL, so a comparison can be bookmarked,
 * shared or re-run from a link:
 *
 * ```
 * /?left=/builds/v1&right=/builds/v2&depth=2&hashes=false
 * /compare/cmp-798ae66a295c?left=/builds/v1&right=/builds/v2
 * ```
 */
export const LEFT_PARAM = 'left';
export const RIGHT_PARAM = 'right';
export const HASHES_PARAM = 'hashes';
export const DEPTH_PARAM = 'depth';

const ALL_PARAMS = [LEFT_PARAM, RIGHT_PARAM, HASHES_PARAM, DEPTH_PARAM] as const;

/** Reads a comparison request from the URL, or null when both paths are not present. */
export function readCompareRequest(params: ParamMap): CompareRequest | null {
  const leftPath = params.get(LEFT_PARAM)?.trim() ?? '';
  const rightPath = params.get(RIGHT_PARAM)?.trim() ?? '';

  if (leftPath.length === 0 || rightPath.length === 0) {
    return null;
  }

  const request: CompareRequest = { leftPath, rightPath };

  const hashes = params.get(HASHES_PARAM)?.trim();
  if (hashes) {
    request.calculateHashes = hashes !== 'false' && hashes !== '0';
  }

  const depth = params.get(DEPTH_PARAM)?.trim();
  if (depth) {
    const parsed = Number(depth);
    if (Number.isInteger(parsed) && parsed >= 0) {
      request.archiveMaxDepth = parsed;
    }
  }

  return request;
}

/** Turns a comparison request into the query parameters that describe it. */
export function writeCompareParams(request: CompareRequest | null | undefined): Params {
  if (!request) {
    return {};
  }

  const params: Params = {
    [LEFT_PARAM]: request.leftPath,
    [RIGHT_PARAM]: request.rightPath,
  };

  if (request.calculateHashes !== undefined) {
    params[HASHES_PARAM] = String(request.calculateHashes);
  }

  if (request.archiveMaxDepth !== undefined) {
    params[DEPTH_PARAM] = String(request.archiveMaxDepth);
  }

  return params;
}

/** True when the URL already carries exactly these parameters, so no navigation is needed. */
export function sameCompareParams(params: ParamMap, expected: Params): boolean {
  return ALL_PARAMS.every((key) => {
    const wanted = expected[key] === undefined ? null : String(expected[key]);
    return (params.get(key) ?? null) === wanted;
  });
}
