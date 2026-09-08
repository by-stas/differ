import { ScrollingModule } from '@angular/cdk/scrolling';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { ComparisonNode, isContainer } from '../../core/models/comparison.models';
import { ComparisonStore } from '../../core/services/comparison-store.service';
import { StatusBadge } from '../../shared/components/status-badge/status-badge';
import { FileSizePipe } from '../../shared/pipes/file-size.pipe';
import { TreeRow, flattenTree } from './tree-flatten';

/** Virtualized difference tree; only the visible rows are rendered. */
@Component({
  selector: 'app-comparison-tree',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ScrollingModule, StatusBadge, FileSizePipe],
  templateUrl: './comparison-tree.html',
  styleUrl: './comparison-tree.scss',
})
export class ComparisonTree {
  protected readonly store = inject(ComparisonStore);

  protected readonly rows = computed<TreeRow[]>(() =>
    flattenTree(this.store.result()?.root, {
      statuses: this.store.statusFilter(),
      search: this.store.search(),
      expanded: this.store.expanded(),
    }),
  );

  protected readonly hasRows = computed(() => this.rows().length > 0);

  protected trackRow = (_: number, row: TreeRow) => row.node.relativePath;

  protected onRowClick(row: TreeRow): void {
    if (row.expandable) {
      this.store.toggleExpanded(row.node.relativePath);
    }

    this.store.select(row.node);
  }

  protected onToggle(event: Event, row: TreeRow): void {
    event.stopPropagation();
    this.store.toggleExpanded(row.node.relativePath);
  }

  protected isSelected(node: ComparisonNode): boolean {
    return this.store.selectedPath() === node.relativePath;
  }

  protected icon(node: ComparisonNode): string {
    switch (node.type) {
      case 'Archive':
        return 'archive';
      case 'Folder':
      case 'ArchiveFolder':
        return 'folder';
      default:
        return 'file';
    }
  }

  protected isContainerNode(node: ComparisonNode): boolean {
    return isContainer(node);
  }
}
