import { ComparisonNode, ComparisonStatus, isContainer } from '../../core/models/comparison.models';

export interface TreeRow {
  node: ComparisonNode;
  depth: number;
  expandable: boolean;
  expanded: boolean;
}

export interface FlattenOptions {
  statuses: ReadonlySet<ComparisonStatus>;
  search: string;
  expanded: ReadonlySet<string>;
}

/**
 * Turns the comparison tree into the flat row list the virtual scroll viewport renders.
 * A container survives filtering when it matches itself or still has a visible descendant, so
 * the path to every difference stays navigable.
 */
export function flattenTree(root: ComparisonNode | null | undefined, options: FlattenOptions): TreeRow[] {
  if (!root) {
    return [];
  }

  const term = options.search.trim().toLowerCase();
  const visibility = new Map<ComparisonNode, boolean>();

  const matchesSelf = (node: ComparisonNode): boolean =>
    options.statuses.has(node.status) && (term.length === 0 || node.name.toLowerCase().includes(term));

  const isVisible = (node: ComparisonNode): boolean => {
    const cached = visibility.get(node);
    if (cached !== undefined) {
      return cached;
    }

    let visible = matchesSelf(node);
    if (!visible && isContainer(node)) {
      visible = (node.children ?? []).some(isVisible);
    }

    visibility.set(node, visible);
    return visible;
  };

  const rows: TreeRow[] = [];

  const walk = (node: ComparisonNode, depth: number): void => {
    for (const child of node.children ?? []) {
      if (!isVisible(child)) {
        continue;
      }

      const expandable = isContainer(child) && (child.children ?? []).some(isVisible);
      const expanded = expandable && options.expanded.has(child.relativePath);

      rows.push({ node: child, depth, expandable, expanded });

      if (expanded) {
        walk(child, depth + 1);
      }
    }
  };

  walk(root, 0);
  return rows;
}
