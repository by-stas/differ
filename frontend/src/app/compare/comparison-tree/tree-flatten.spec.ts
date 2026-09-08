import { ComparisonNode, ComparisonStatus } from '../../core/models/comparison.models';
import { flattenTree } from './tree-flatten';

function node(partial: Partial<ComparisonNode> & { name: string; relativePath: string }): ComparisonNode {
  return {
    type: 'File',
    status: 'Unchanged',
    canCompareContent: false,
    ...partial,
  };
}

const tree = node({
  name: '/',
  relativePath: '/',
  type: 'Folder',
  status: 'Modified',
  children: [
    node({
      name: 'src',
      relativePath: '/src',
      type: 'Folder',
      status: 'Modified',
      children: [
        node({ name: 'config.json', relativePath: '/src/config.json', status: 'Modified', canCompareContent: true }),
        node({ name: 'newFile.cs', relativePath: '/src/newFile.cs', status: 'Added' }),
        node({ name: 'stable.cs', relativePath: '/src/stable.cs', status: 'Unchanged' }),
      ],
    }),
    node({
      name: 'package.zip',
      relativePath: '/package.zip',
      type: 'Archive',
      status: 'Modified',
      children: [
        node({
          name: 'config',
          relativePath: '/package.zip!/config',
          type: 'ArchiveFolder',
          status: 'Modified',
          children: [
            node({
              name: 'settings.json',
              relativePath: '/package.zip!/config/settings.json',
              type: 'ArchiveFile',
              status: 'Modified',
              canCompareContent: true,
            }),
          ],
        }),
      ],
    }),
  ],
});

const allStatuses = new Set<ComparisonStatus>(['Added', 'Removed', 'Modified', 'Unchanged']);
const changedStatuses = new Set<ComparisonStatus>(['Added', 'Removed', 'Modified']);

describe('flattenTree', () => {
  it('returns nothing without a root', () => {
    expect(flattenTree(null, { statuses: allStatuses, search: '', expanded: new Set() })).toEqual([]);
  });

  it('only lists top level entries while everything is collapsed', () => {
    const rows = flattenTree(tree, { statuses: allStatuses, search: '', expanded: new Set() });

    expect(rows.map((row) => row.node.relativePath)).toEqual(['/src', '/package.zip']);
    expect(rows.every((row) => row.expandable)).toBe(true);
    expect(rows.every((row) => !row.expanded)).toBe(true);
  });

  it('lists the children of expanded containers with the right depth', () => {
    const rows = flattenTree(tree, { statuses: allStatuses, search: '', expanded: new Set(['/src']) });

    expect(rows.map((row) => row.node.relativePath)).toEqual([
      '/src',
      '/src/config.json',
      '/src/newFile.cs',
      '/src/stable.cs',
      '/package.zip',
    ]);
    expect(rows[1].depth).toBe(1);
  });

  it('hides unchanged entries when the filter excludes them', () => {
    const rows = flattenTree(tree, { statuses: changedStatuses, search: '', expanded: new Set(['/src']) });

    expect(rows.map((row) => row.node.relativePath)).not.toContain('/src/stable.cs');
  });

  it('keeps containers that still lead to a visible change', () => {
    const rows = flattenTree(tree, {
      statuses: new Set<ComparisonStatus>(['Added']),
      search: '',
      expanded: new Set(['/src']),
    });

    expect(rows.map((row) => row.node.relativePath)).toEqual(['/src', '/src/newFile.cs']);
  });

  it('expands archive entries through the virtual path', () => {
    const rows = flattenTree(tree, {
      statuses: changedStatuses,
      search: '',
      expanded: new Set(['/package.zip', '/package.zip!/config']),
    });

    expect(rows.map((row) => row.node.relativePath)).toContain('/package.zip!/config/settings.json');
  });

  it('filters by name and keeps the parents of the match', () => {
    const rows = flattenTree(tree, {
      statuses: allStatuses,
      search: 'settings',
      expanded: new Set(['/package.zip', '/package.zip!/config']),
    });

    expect(rows.map((row) => row.node.relativePath)).toEqual([
      '/package.zip',
      '/package.zip!/config',
      '/package.zip!/config/settings.json',
    ]);
  });

  it('reports containers without visible children as not expandable', () => {
    const rows = flattenTree(tree, {
      statuses: new Set<ComparisonStatus>(['Modified']),
      search: 'src',
      expanded: new Set(['/src']),
    });

    expect(rows).toHaveLength(1);
    expect(rows[0].expandable).toBe(false);
  });
});
