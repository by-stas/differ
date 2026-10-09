import { convertToParamMap } from '@angular/router';
import { readCompareRequest, sameCompareParams, writeCompareParams } from './comparison-url';

describe('comparison URL parameters', () => {
  describe('readCompareRequest', () => {
    it('reads both paths', () => {
      const request = readCompareRequest(convertToParamMap({ left: '/builds/a', right: '/builds/b' }));

      expect(request).toEqual({ leftPath: '/builds/a', rightPath: '/builds/b' });
    });

    it('trims the paths', () => {
      const request = readCompareRequest(convertToParamMap({ left: ' /builds/a ', right: '/builds/b ' }));

      expect(request?.leftPath).toBe('/builds/a');
      expect(request?.rightPath).toBe('/builds/b');
    });

    it('returns null when a path is missing or empty', () => {
      expect(readCompareRequest(convertToParamMap({}))).toBeNull();
      expect(readCompareRequest(convertToParamMap({ left: '/builds/a' }))).toBeNull();
      expect(readCompareRequest(convertToParamMap({ left: '/builds/a', right: '  ' }))).toBeNull();
    });

    it('reads the options when they are present', () => {
      const request = readCompareRequest(
        convertToParamMap({ left: '/a', right: '/b', hashes: 'false', depth: '2' }),
      );

      expect(request).toEqual({
        leftPath: '/a',
        rightPath: '/b',
        calculateHashes: false,
        archiveMaxDepth: 2,
      });
    });

    it('treats any value other than false or 0 as hashing enabled', () => {
      const request = readCompareRequest(convertToParamMap({ left: '/a', right: '/b', hashes: 'true' }));

      expect(request?.calculateHashes).toBe(true);
    });

    it('ignores a depth that is not a whole positive number', () => {
      for (const depth of ['abc', '-1', '1.5', '']) {
        const request = readCompareRequest(convertToParamMap({ left: '/a', right: '/b', depth }));

        expect(request?.archiveMaxDepth).toBeUndefined();
      }
    });
  });

  describe('writeCompareParams', () => {
    it('writes the paths and omits options that are not set', () => {
      expect(writeCompareParams({ leftPath: '/a', rightPath: '/b' })).toEqual({ left: '/a', right: '/b' });
    });

    it('writes the options as strings', () => {
      expect(
        writeCompareParams({ leftPath: '/a', rightPath: '/b', calculateHashes: false, archiveMaxDepth: 0 }),
      ).toEqual({ left: '/a', right: '/b', hashes: 'false', depth: '0' });
    });

    it('round-trips a request through the URL', () => {
      const request = { leftPath: '/a', rightPath: '/b', calculateHashes: false, archiveMaxDepth: 3 };

      expect(readCompareRequest(convertToParamMap(writeCompareParams(request)))).toEqual(request);
    });

    it('returns nothing without a request', () => {
      expect(writeCompareParams(null)).toEqual({});
    });
  });

  describe('sameCompareParams', () => {
    it('compares every parameter', () => {
      const params = convertToParamMap({ left: '/a', right: '/b', depth: '2' });

      expect(sameCompareParams(params, { left: '/a', right: '/b', depth: '2' })).toBe(true);
      expect(sameCompareParams(params, { left: '/a', right: '/b', depth: 2 })).toBe(true);
      expect(sameCompareParams(params, { left: '/a', right: '/other', depth: '2' })).toBe(false);
    });

    it('detects a parameter that should no longer be there', () => {
      expect(sameCompareParams(convertToParamMap({ left: '/a', right: '/b', hashes: 'false' }), {
        left: '/a',
        right: '/b',
      })).toBe(false);
    });

    it('ignores unrelated parameters', () => {
      const params = convertToParamMap({ left: '/a', right: '/b', theme: 'dark' });

      expect(sameCompareParams(params, { left: '/a', right: '/b' })).toBe(true);
    });
  });
});
