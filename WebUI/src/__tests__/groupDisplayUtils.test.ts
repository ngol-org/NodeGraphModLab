import { describe, it, expect, vi } from 'vitest'
import type { Node } from '@xyflow/react'
import { buildGroupedRfElements } from '../lib/groupDisplayUtils'
import type { NodeGroup } from '../types/protocol'

function makeNode(id: string, x: number, y: number): Node {
  return { id, position: { x, y }, data: {} }
}

const noop = vi.fn()

describe('buildGroupedRfElements — controlsLocked', () => {
  it('展開グループの背景ノードは controlsLocked=false で draggable=true', () => {
    const baseNodes = [makeNode('n1', 0, 0), makeNode('n2', 100, 100)]
    const group: NodeGroup = { id: 'g1', name: 'G1', nodeInstanceIds: ['n1', 'n2'], collapsed: false }

    const { displayNodes } = buildGroupedRfElements(baseNodes, [], [group], new Map(), noop, noop, noop, false)

    const groupNode = displayNodes.find(n => n.id === 'group-g1')
    expect(groupNode?.draggable).toBe(true)
  })

  it('展開グループの背景ノードは controlsLocked=true で draggable=false', () => {
    const baseNodes = [makeNode('n1', 0, 0), makeNode('n2', 100, 100)]
    const group: NodeGroup = { id: 'g1', name: 'G1', nodeInstanceIds: ['n1', 'n2'], collapsed: false }

    const { displayNodes } = buildGroupedRfElements(baseNodes, [], [group], new Map(), noop, noop, noop, true)

    const groupNode = displayNodes.find(n => n.id === 'group-g1')
    expect(groupNode?.draggable).toBe(false)
  })

  it('折りたたみグループノードは controlsLocked=false で draggable/selectable=true', () => {
    const baseNodes = [makeNode('n1', 0, 0), makeNode('n2', 100, 100)]
    const group: NodeGroup = { id: 'g1', name: 'G1', nodeInstanceIds: ['n1', 'n2'], collapsed: true }

    const { displayNodes } = buildGroupedRfElements(baseNodes, [], [group], new Map(), noop, noop, noop, false)

    const groupNode = displayNodes.find(n => n.id === 'group-g1')
    expect(groupNode?.draggable).toBe(true)
    expect(groupNode?.selectable).toBe(true)
  })

  it('折りたたみグループノードは controlsLocked=true で draggable/selectable=false', () => {
    const baseNodes = [makeNode('n1', 0, 0), makeNode('n2', 100, 100)]
    const group: NodeGroup = { id: 'g1', name: 'G1', nodeInstanceIds: ['n1', 'n2'], collapsed: true }

    const { displayNodes } = buildGroupedRfElements(baseNodes, [], [group], new Map(), noop, noop, noop, true)

    const groupNode = displayNodes.find(n => n.id === 'group-g1')
    expect(groupNode?.draggable).toBe(false)
    expect(groupNode?.selectable).toBe(false)
  })

  it('measured 実測サイズが推定値(NODE_EST_*)より大きいノードでは、グループ矩形が実測サイズを覆う', () => {
    // nodeRenderer 拡張ノード等、標準推定値(200x100)より大きくレンダリングされるケースを再現
    const bigNode: Node = { id: 'n1', position: { x: 0, y: 0 }, data: {}, measured: { width: 240, height: 320 } }
    const group: NodeGroup = { id: 'g1', name: 'G1', nodeInstanceIds: ['n1'], collapsed: false }

    const { displayNodes } = buildGroupedRfElements([bigNode], [], [group], new Map(), noop, noop, noop, false)

    const groupNode = displayNodes.find(n => n.id === 'group-g1')
    // 実測サイズ(240x320) + GROUP_PADDING(40) * 2 を覆う矩形になっているべき
    // (固定推定値 200x100 のままだとノードの右端・下端を覗き込めてしまう)
    expect(groupNode?.width).toBeGreaterThanOrEqual(240 + 40 * 2)
    expect(groupNode?.height).toBeGreaterThanOrEqual(320 + 40 * 2)
  })

  it('measured が無いノードは data.size にフォールバックする', () => {
    const resizedNode: Node = { id: 'n1', position: { x: 0, y: 0 }, data: { size: { width: 300, height: 150 } } }
    const group: NodeGroup = { id: 'g1', name: 'G1', nodeInstanceIds: ['n1'], collapsed: false }

    const { displayNodes } = buildGroupedRfElements([resizedNode], [], [group], new Map(), noop, noop, noop, false)

    const groupNode = displayNodes.find(n => n.id === 'group-g1')
    expect(groupNode?.width).toBeGreaterThanOrEqual(300 + 40 * 2)
    expect(groupNode?.height).toBeGreaterThanOrEqual(150 + 40 * 2)
  })
})
