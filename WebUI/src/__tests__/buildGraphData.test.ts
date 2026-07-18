/**
 * buildGraphData ロジックのテスト
 * GraphEditorLayout で使われるエッジ→接続変換ロジックを
 * 純粋関数として抽出してテスト
 */
import { describe, it, expect } from 'vitest'
import type { NodeConnection, NodeInstance } from '../types/protocol'
import type { Node, Edge } from '@xyflow/react'

// ────────────────────────────────────────────────────────────────
// テスト対象ロジック (GraphEditorLayout.tsx の buildGraphData より)
// ────────────────────────────────────────────────────────────────
function buildConnections(rfEdges: Edge[]): NodeConnection[] {
  return rfEdges
    .filter(e => e.source && e.target && e.sourceHandle && e.targetHandle)
    .map(e => ({
      fromNodeInstanceId: e.source,
      fromPortName: e.sourceHandle!,
      toNodeInstanceId: e.target,
      toPortName: e.targetHandle!,
    }))
}

function buildNodeInstances(rfNodes: Node[]): NodeInstance[] {
  return rfNodes.map(n => ({
    instanceId: n.id,
    nodeTypeId: (n.data as { nodeTypeId: string }).nodeTypeId,
    position: { x: n.position.x, y: n.position.y },
    paramValues: (n.data as { paramValues: Record<string, unknown> }).paramValues ?? {},
  }))
}

// ────────────────────────────────────────────────────────────────
// テスト
// ────────────────────────────────────────────────────────────────

describe('buildConnections', () => {
  it('sourceHandle/targetHandle が両方ある場合のみ接続を生成する', () => {
    const edges: Edge[] = [
      { id: 'e1', source: 'n1', target: 'n2', sourceHandle: 'result', targetHandle: 'a' },
      { id: 'e2', source: 'n2', target: 'n3', sourceHandle: 'result', targetHandle: 'b' },
    ]
    const conns = buildConnections(edges)
    expect(conns).toHaveLength(2)
    expect(conns[0].fromPortName).toBe('result')
    expect(conns[0].toPortName).toBe('a')
  })

  it('sourceHandle が null のエッジはフィルタされる', () => {
    const edges: Edge[] = [
      { id: 'e1', source: 'n1', target: 'n2', sourceHandle: null, targetHandle: 'a' },
    ]
    const conns = buildConnections(edges)
    expect(conns).toHaveLength(0)
  })

  it('targetHandle が null のエッジはフィルタされる', () => {
    const edges: Edge[] = [
      { id: 'e1', source: 'n1', target: 'n2', sourceHandle: 'result', targetHandle: null },
    ]
    const conns = buildConnections(edges)
    expect(conns).toHaveLength(0)
  })

  it('sourceHandle も targetHandle も null の場合は0件', () => {
    const edges: Edge[] = [
      { id: 'e1', source: 'n1', target: 'n2', sourceHandle: null, targetHandle: null },
      { id: 'e2', source: 'n3', target: 'n4' }, // sourceHandle/targetHandle プロパティなし
    ]
    const conns = buildConnections(edges)
    expect(conns).toHaveLength(0)
  })

  it('空のエッジ配列から空の接続配列を返す', () => {
    expect(buildConnections([])).toHaveLength(0)
  })

  it('接続のFromとTo情報が正しくマッピングされる', () => {
    const edges: Edge[] = [
      { id: 'e1', source: 'node-a', target: 'node-b', sourceHandle: 'out_value', targetHandle: 'in_a' },
    ]
    const [conn] = buildConnections(edges)
    expect(conn.fromNodeInstanceId).toBe('node-a')
    expect(conn.fromPortName).toBe('out_value')
    expect(conn.toNodeInstanceId).toBe('node-b')
    expect(conn.toPortName).toBe('in_a')
  })
})

describe('buildNodeInstances', () => {
  it('ReactFlowノードをNodeInstanceに変換する', () => {
    const nodes: Node[] = [
      {
        id: 'n1',
        type: 'custom',
        position: { x: 100, y: 200 },
        data: { nodeTypeId: 'ngol.logic.add', paramValues: { a: 10, b: 20 } },
      },
    ]
    const instances = buildNodeInstances(nodes)
    expect(instances).toHaveLength(1)
    expect(instances[0].instanceId).toBe('n1')
    expect(instances[0].nodeTypeId).toBe('ngol.logic.add')
    expect(instances[0].position).toEqual({ x: 100, y: 200 })
    expect(instances[0].paramValues).toEqual({ a: 10, b: 20 })
  })

  it('paramValues が未定義の場合は空オブジェクトになる', () => {
    const nodes: Node[] = [
      {
        id: 'n2',
        type: 'custom',
        position: { x: 0, y: 0 },
        data: { nodeTypeId: 'ngol.logic.log', paramValues: undefined as unknown as Record<string, unknown> },
      },
    ]
    const instances = buildNodeInstances(nodes)
    expect(instances[0].paramValues).toEqual({})
  })

  it('複数ノードを変換する', () => {
    const nodes: Node[] = [
      { id: 'n1', type: 'custom', position: { x: 0, y: 0 }, data: { nodeTypeId: 'ngol.logic.const_number', paramValues: {} } },
      { id: 'n2', type: 'custom', position: { x: 200, y: 0 }, data: { nodeTypeId: 'ngol.logic.add', paramValues: {} } },
    ]
    expect(buildNodeInstances(nodes)).toHaveLength(2)
  })
})
