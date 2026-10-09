import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnDestroy,
  effect,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import type * as MonacoApi from 'monaco-editor';
import { FileContentResult } from '../../core/models/comparison.models';
import { MonacoLoaderService } from '../../core/services/monaco-loader.service';

/** Side-by-side diff of the two sides of a file, rendered by the Monaco diff editor. */
@Component({
  selector: 'app-monaco-diff-viewer',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './monaco-diff-viewer.html',
  styleUrl: './monaco-diff-viewer.scss',
})
export class MonacoDiffViewer implements OnDestroy {
  private readonly monacoLoader = inject(MonacoLoaderService);

  readonly content = input.required<FileContentResult>();
  readonly leftLabel = input('Folder A');
  readonly rightLabel = input('Folder B');

  readonly loadError = signal<string | null>(null);
  readonly ready = signal(false);

  private readonly host = viewChild.required<ElementRef<HTMLDivElement>>('editorHost');

  private monaco?: typeof MonacoApi;
  private editor?: MonacoApi.editor.IStandaloneDiffEditor;
  private models?: { original: MonacoApi.editor.ITextModel; modified: MonacoApi.editor.ITextModel };

  constructor() {
    effect(() => {
      const content = this.content();
      const host = this.host().nativeElement;
      void this.render(host, content);
    });
  }

  ngOnDestroy(): void {
    this.disposeEditor();
  }

  private async render(host: HTMLElement, content: FileContentResult): Promise<void> {
    try {
      this.monaco ??= await this.monacoLoader.load();
    } catch (error) {
      this.loadError.set(error instanceof Error ? error.message : 'The diff editor could not be loaded.');
      return;
    }

    const monaco = this.monaco;
    this.disposeModels();

    this.models = {
      original: monaco.editor.createModel(content.leftContent ?? '', content.language),
      modified: monaco.editor.createModel(content.rightContent ?? '', content.language),
    };

    this.editor ??= monaco.editor.createDiffEditor(host, {
      readOnly: true,
      originalEditable: false,
      automaticLayout: true,
      renderSideBySide: true,
      renderOverviewRuler: true,
      lineNumbers: 'on',
      wordWrap: 'on',
      scrollBeyondLastLine: false,
      minimap: { enabled: false },
      theme: 'vs',
    });

    this.editor.setModel(this.models);
    this.ready.set(true);
    this.loadError.set(null);
  }

  private disposeModels(): void {
    this.models?.original.dispose();
    this.models?.modified.dispose();
    this.models = undefined;
  }

  private disposeEditor(): void {
    this.editor?.dispose();
    this.editor = undefined;
    this.disposeModels();
  }
}
