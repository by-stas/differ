import { Injectable } from '@angular/core';
import type * as MonacoApi from 'monaco-editor';

declare global {
  interface Window {
    monaco?: typeof MonacoApi;
    MonacoEnvironment?: { getWorker?: (moduleId: string, label: string) => Worker };
    require?: {
      (modules: string[], onLoad: () => void, onError?: (error: unknown) => void): void;
      config?: (options: { paths: Record<string, string> }) => void;
    };
  }
}

/** Base path the Monaco assets are copied to by the build (see angular.json). */
const MONACO_BASE = 'assets/monaco';

/**
 * Loads Monaco from static assets with its own AMD loader instead of bundling it. Monaco stays
 * out of the initial bundle, brings its own stylesheet and its language workers keep working.
 */
@Injectable({ providedIn: 'root' })
export class MonacoLoaderService {
  private loading?: Promise<typeof MonacoApi>;

  load(): Promise<typeof MonacoApi> {
    if (window.monaco) {
      return Promise.resolve(window.monaco);
    }

    this.loading ??= this.loadInternal();
    return this.loading;
  }

  private loadInternal(): Promise<typeof MonacoApi> {
    return new Promise((resolve, reject) => {
      const baseUrl = new URL(MONACO_BASE, document.baseURI).href.replace(/\/$/, '');

      // Monaco computes diffs in a web worker; the worker needs the same AMD loader.
      window.MonacoEnvironment = {
        getWorker: () => createWorker(baseUrl),
      };

      const script = document.createElement('script');
      script.src = `${baseUrl}/vs/loader.js`;
      script.onload = () => {
        const loader = window.require;
        if (!loader) {
          reject(new Error('The Monaco AMD loader did not register itself.'));
          return;
        }

        loader.config?.({ paths: { vs: `${baseUrl}/vs` } });
        loader(
          ['vs/editor/editor.main'],
          () => {
            if (window.monaco) {
              resolve(window.monaco);
            } else {
              reject(new Error('Monaco loaded but did not expose its API.'));
            }
          },
          (error: unknown) => reject(error instanceof Error ? error : new Error(String(error))),
        );
      };
      script.onerror = () => reject(new Error(`Failed to load ${script.src}.`));

      document.body.appendChild(script);
    });
  }
}

function createWorker(baseUrl: string): Worker {
  const bootstrap = `
    self.MonacoEnvironment = { baseUrl: '${baseUrl}/' };
    importScripts('${baseUrl}/vs/loader.js');
    require.config({ paths: { vs: '${baseUrl}/vs' } });
    require(['vs/editor/editor.worker'], function () {});
  `;

  const blob = new Blob([bootstrap], { type: 'text/javascript' });
  const worker = new Worker(URL.createObjectURL(blob));
  return worker;
}
