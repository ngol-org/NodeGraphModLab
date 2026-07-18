import { describe, expect, it } from 'vitest'
import { isSnapshotNodeTypeId, resolveFragmentLink } from '../lib/fragmentUtils'
import type { FragmentDefinition } from '../types/protocol'

const FRAGMENTS: FragmentDefinition[] = [
  { id: 'auto-snap-1', name: 'Fragment 1', nodeInstanceIds: ['snap-1'] },
  { id: 'auto-node-1', name: 'Fragment 2', nodeInstanceIds: ['node-1'] },
]

describe('isSnapshotNodeTypeId', () => {
  it('returns true for ngol.snapshot.* node types', () => {
    expect(isSnapshotNodeTypeId('ngol.snapshot.any')).toBe(true)
  })

  it('returns false for non-snapshot node types', () => {
    expect(isSnapshotNodeTypeId('ngol.logic.sample')).toBe(false)
  })

  it('returns false for undefined', () => {
    expect(isSnapshotNodeTypeId(undefined)).toBe(false)
  })
})

describe('resolveFragmentLink', () => {
  it('builds a fragment link when source is a snapshot node in a different fragment (existing node target)', () => {
    const resolved = resolveFragmentLink({
      sourceNodeId: 'snap-1',
      sourceNodeTypeId: 'ngol.snapshot.any',
      sourceHandle: 'value',
      targetNodeId: 'node-1',
      targetHandle: 'input',
    }, FRAGMENTS)

    expect(resolved).not.toBeNull()
    expect(resolved!.edge).toMatchObject({
      source: 'snap-1',
      sourceHandle: 'value',
      target: 'node-1',
      targetHandle: 'input',
      type: 'fragmentLink',
      data: { kind: 'fragmentLink' },
    })
    expect(resolved!.link).toEqual({
      sourceSnapshotNodeInstanceId: 'snap-1',
      sourcePortName: 'value',
      toNodeInstanceId: 'node-1',
      toPortName: 'input',
    })
  })

  it('builds a fragment link when the target node is not part of any fragment yet (newly added node)', () => {
    // 新規追加ノードはまだ rfNodes に存在しないため fragments に含まれず、常に未所属(null)として扱われる
    const resolved = resolveFragmentLink({
      sourceNodeId: 'snap-1',
      sourceNodeTypeId: 'ngol.snapshot.any',
      sourceHandle: 'value',
      targetNodeId: 'new-node',
      targetHandle: 'input',
    }, FRAGMENTS)

    expect(resolved).not.toBeNull()
    expect(resolved!.edge.type).toBe('fragmentLink')
  })

  it('returns null when the source node is not a snapshot node', () => {
    const resolved = resolveFragmentLink({
      sourceNodeId: 'node-1',
      sourceNodeTypeId: 'ngol.logic.sample',
      sourceHandle: 'result',
      targetNodeId: 'new-node',
      targetHandle: 'value',
    }, FRAGMENTS)

    expect(resolved).toBeNull()
  })

  it('returns null when source and target already belong to the same fragment', () => {
    const sameFragment: FragmentDefinition[] = [
      { id: 'auto-snap-1', name: 'Fragment 1', nodeInstanceIds: ['snap-1', 'node-1'] },
    ]
    const resolved = resolveFragmentLink({
      sourceNodeId: 'snap-1',
      sourceNodeTypeId: 'ngol.snapshot.any',
      sourceHandle: 'value',
      targetNodeId: 'node-1',
      targetHandle: 'input',
    }, sameFragment)

    expect(resolved).toBeNull()
  })
})
