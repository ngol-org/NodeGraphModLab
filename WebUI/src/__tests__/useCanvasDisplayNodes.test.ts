import { describe, it, expect, vi } from 'vitest'
import { renderHook } from '@testing-library/react'
import type { Node } from '@xyflow/react'
import { useCanvasDisplayNodes } from '../hooks/useCanvasDisplayNodes'

function makeNode(id: string): Node {
  return { id, position: { x: 0, y: 0 }, data: {} }
}

const noop = vi.fn()

function setup(overrides: { isSpacePressed?: boolean; controlsLocked?: boolean } = {}) {
  return renderHook(() =>
    useCanvasDisplayNodes({
      rfNodes: [makeNode('n1')],
      rfEdges: [],
      nodeStatus: {},
      savedSnapshots: new Map(),
      savedSnapshotsByPort: new Map(),
      pinnedNodes: new Set(),
      justSavedNodes: new Set(),
      fragments: [],
      hoveredFragmentId: null,
      coExecutedFragmentIds: new Set(),
      snapshotInputNodeIds: new Set(),
      persistentNodes: new Map(),
      isSpacePressed: overrides.isSpacePressed ?? false,
      controlsLocked: overrides.controlsLocked ?? false,
      isDragSelecting: false,
      multiSelectedIds: new Set(),
      modifierKeyRef: { current: null },
      groups: [],
      groupDragPositions: new Map(),
      stableGroupToggle: noop,
      stableGroupRename: noop,
      stableGroupDissolve: noop,
      stableAnnotationTextChange: noop,
      stableAnnotationDelete: noop,
      stableAnnotationResize: noop,
    })
  )
}

describe('useCanvasDisplayNodes — controlsLocked', () => {
  it('isSpacePressed=false, controlsLocked=false ではドラッグ・選択可能', () => {
    const { result } = setup({ isSpacePressed: false, controlsLocked: false })
    const n1 = result.current.displayNodes.find(n => n.id === 'n1')
    expect(n1?.draggable).toBe(true)
    expect(n1?.selectable).toBe(true)
  })

  it('controlsLocked=true ではドラッグ・選択とも禁止される', () => {
    const { result } = setup({ isSpacePressed: false, controlsLocked: true })
    const n1 = result.current.displayNodes.find(n => n.id === 'n1')
    expect(n1?.draggable).toBe(false)
    expect(n1?.selectable).toBe(false)
  })

  it('isSpacePressed=true（従来のスペースキー一時ロック）は controlsLocked=false でも禁止のまま（回帰なし）', () => {
    const { result } = setup({ isSpacePressed: true, controlsLocked: false })
    const n1 = result.current.displayNodes.find(n => n.id === 'n1')
    expect(n1?.draggable).toBe(false)
    expect(n1?.selectable).toBe(false)
  })

  it('isSpacePressed と controlsLocked が両方 true でも禁止のまま', () => {
    const { result } = setup({ isSpacePressed: true, controlsLocked: true })
    const n1 = result.current.displayNodes.find(n => n.id === 'n1')
    expect(n1?.draggable).toBe(false)
    expect(n1?.selectable).toBe(false)
  })
})
