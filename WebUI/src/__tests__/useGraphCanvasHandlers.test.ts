/**
 * useGraphCanvasHandlers.ts をコンポーザへ分割する前の特性化テスト。
 * hook ファイルを分割した後も、このテストファイル自体は変更せず
 * 同じテストがそのまま通ることを分割の正しさ（リグレッションなし）の証拠にする。
 *
 * 対象はリスクが高い分岐のみ:
 *   1. onConnect: 断片リンク解決の分岐
 *   2. handleDeleteNode / handleDeleteSelected: エッジ・断片リンクの連鎖削除
 *   3. handleNodeDragStop: グループ折りたたみ時(pushHistory)と展開時(recordHistory)の分岐差
 *   4. handleParamEditEnd: ドラッグ中の連続変更が Undo 1 単位にまとまること
 */
import { describe, it, expect } from 'vitest'
import { renderHook, act } from '@testing-library/react'
import { useRef, useState } from 'react'
import type { MouseEvent as ReactMouseEvent } from 'react'
import type { Node, Edge, Connection, ReactFlowInstance } from '@xyflow/react'
import type { FragmentLink, NodeGroup, NodeTypeInfo } from '../types/protocol'
import { useGraphCanvasHandlers } from '../hooks/useGraphCanvasHandlers'
import { useGraphHistory } from '../hooks/useGraphHistory'

function makeNode(id: string, x: number, y: number, data: Record<string, unknown> = {}): Node {
  return { id, type: 'custom', position: { x, y }, data }
}

interface HarnessOptions {
  rfNodes?: Node[]
  rfEdges?: Edge[]
  fragmentLinks?: FragmentLink[]
  groups?: NodeGroup[]
  selectedNodeId?: string | null
}

function renderHarness(options: HarnessOptions = {}) {
  return renderHook(() => {
    const [rfNodes, setRfNodes] = useState<Node[]>(options.rfNodes ?? [])
    const [rfEdges, setRfEdges] = useState<Edge[]>(options.rfEdges ?? [])
    const [fragmentLinks, setFragmentLinks] = useState<FragmentLink[]>(options.fragmentLinks ?? [])
    const [selectedNodeId, setSelectedNodeId] = useState<string | null>(options.selectedNodeId ?? null)
    const [multiSelectedIds, setMultiSelectedIds] = useState<Set<string>>(new Set())
    const [isDragSelecting, setIsDragSelecting] = useState(false)
    const [graphId, setGraphId] = useState('graph-1')
    const [graphName, setGraphName] = useState('Graph 1')
    const history = useGraphHistory()

    // rfRef.current.getNodes()/getEdges() は実際の ReactFlow インスタンスの代わりに
    // 最新の state を返すフェイクで代替する（<ReactFlow> を実マウントせずに検証するため）。
    const rfNodesRef = useRef(rfNodes)
    rfNodesRef.current = rfNodes
    const rfEdgesRef = useRef(rfEdges)
    rfEdgesRef.current = rfEdges

    const rfRef = useRef<ReactFlowInstance | null>(null)
    rfRef.current = {
      getNodes: () => rfNodesRef.current,
      getEdges: () => rfEdgesRef.current,
      screenToFlowPosition: (p: { x: number; y: number }) => p,
    } as unknown as ReactFlowInstance

    const modifierKeyRef = useRef<'ctrl' | 'shift' | null>(null)
    const preSelectionRef = useRef<Set<string>>(new Set())
    const lastKnownSelectionRef = useRef<Set<string>>(new Set())
    const lastMouseRef = useRef({ x: 0, y: 0 })
    const lastCanvasClickRef = useRef({ x: 0, y: 0 })
    const nodeTypeMap = new Map<string, NodeTypeInfo>()

    const handlers = useGraphCanvasHandlers({
      rfRef,
      rfNodes,
      setRfNodes,
      rfEdges,
      setRfEdges,
      onNodesChange: () => {},
      onEdgesChangeBase: () => {},
      graphId,
      graphName,
      setGraphId,
      setGraphName,
      selectedNodeId,
      setSelectedNodeId,
      nodeTypeMap,
      fragmentLinks,
      setFragmentLinks,
      fragments: [],
      pinnedFragmentIds: new Set(),
      groups: options.groups ?? [],
      createGroup: () => {},
      deleteGroup: () => {},
      renameGroup: () => {},
      updateGroupDescription: () => {},
      toggleCollapsed: () => {},
      addNodesToGroup: () => {},
      removeNodeFromGroup: () => {},
      resetGroups: () => {},
      pushHistory: history.push,
      undo: history.undo,
      redo: history.redo,
      recordHistory: history.record,
      clearHistory: history.clear,
      isSpacePressed: false,
      isDragSelecting,
      setIsDragSelecting,
      multiSelectedIds,
      setMultiSelectedIds,
      modifierKeyRef,
      preSelectionRef,
      lastKnownSelectionRef,
      lastMouseRef,
      setAddMenuPos: () => {},
      lastCanvasClickRef,
      setFragmentImportMenuPos: () => {},
      closeAllMenus: () => {},
      execute: () => {},
      save: () => {},
      addLog: () => {},
      hoveredFragmentId: null,
      setHoveredFragmentId: () => {},
      setSaveAsDialogOpen: () => {},
      saveAsName: '',
      setSaveAsName: () => {},
      setExportDialogOpen: () => {},
      exportDllName: '',
      setExportDllName: () => {},
      exportOutputDir: '',
      setExportOutputDir: () => {},
      setExportResult: () => {},
      setClearCanvasDialogOpen: () => {},
    })

    return { handlers, rfNodes, rfEdges, fragmentLinks, history }
  })
}

// ────────────────────────────────────────────────────────────────
// 1. onConnect: 断片リンク解決の分岐
// ────────────────────────────────────────────────────────────────

describe('onConnect', () => {
  it('Snapshot ノード発・別断片への接続は断片リンクとして解決される', () => {
    const { result } = renderHarness({
      rfNodes: [
        makeNode('snap1', 0, 0, { nodeTypeId: 'ngol.snapshot.value', paramValues: {} }),
        makeNode('target1', 200, 0, { nodeTypeId: 'ngol.logic.log', paramValues: {} }),
      ],
    })
    const connection: Connection = { source: 'snap1', sourceHandle: 'value', target: 'target1', targetHandle: 'in' }
    act(() => result.current.handlers.onConnect(connection))

    expect(result.current.fragmentLinks).toHaveLength(1)
    expect(result.current.fragmentLinks[0]).toMatchObject({
      sourceSnapshotNodeInstanceId: 'snap1',
      sourcePortName: 'value',
      toNodeInstanceId: 'target1',
      toPortName: 'in',
    })
    expect(result.current.rfEdges).toHaveLength(1)
    expect((result.current.rfEdges[0].data as { kind?: string } | undefined)?.kind).toBe('fragmentLink')
    expect(result.current.history.canUndo).toBe(true)

    act(() => result.current.history.undo())
    expect(result.current.fragmentLinks).toHaveLength(0)
    expect(result.current.rfEdges).toHaveLength(0)
  })

  it('非Snapshotノード発の接続は通常エッジになり断片リンクは作られない', () => {
    const { result } = renderHarness({
      rfNodes: [
        makeNode('a', 0, 0, { nodeTypeId: 'ngol.logic.const', paramValues: {} }),
        makeNode('b', 200, 0, { nodeTypeId: 'ngol.logic.log', paramValues: {} }),
      ],
    })
    const connection: Connection = { source: 'a', sourceHandle: 'out', target: 'b', targetHandle: 'in' }
    act(() => result.current.handlers.onConnect(connection))

    expect(result.current.fragmentLinks).toHaveLength(0)
    expect(result.current.rfEdges).toHaveLength(1)
    expect((result.current.rfEdges[0].data as { kind?: string } | undefined)?.kind).toBeUndefined()

    act(() => result.current.history.undo())
    expect(result.current.rfEdges).toHaveLength(0)
  })
})

// ────────────────────────────────────────────────────────────────
// 2. handleDeleteNode / handleDeleteSelected: エッジ・断片リンクの連鎖削除
// ────────────────────────────────────────────────────────────────

describe('handleDeleteNode', () => {
  it('ノード削除時に接続エッジ・断片リンクが連鎖削除され、Undoで両方復元される', () => {
    const { result } = renderHarness({
      rfNodes: [
        makeNode('n1', 0, 0, { nodeTypeId: 'ngol.logic.const', paramValues: {} }),
        makeNode('n2', 100, 0, { nodeTypeId: 'ngol.logic.log', paramValues: {} }),
        makeNode('n3', 0, 100, { nodeTypeId: 'ngol.snapshot.value', paramValues: {} }),
      ],
      rfEdges: [
        { id: 'e1', source: 'n1', target: 'n2', sourceHandle: 'out', targetHandle: 'in' },
        { id: 'fl-e1', source: 'n3', target: 'n2', sourceHandle: 'value', targetHandle: 'in2', type: 'fragmentLink', data: { kind: 'fragmentLink' } },
      ],
      fragmentLinks: [
        { sourceSnapshotNodeInstanceId: 'n3', sourcePortName: 'value', toNodeInstanceId: 'n2', toPortName: 'in2' },
      ],
    })

    act(() => result.current.handlers.handleDeleteNode('n3'))

    expect(result.current.rfNodes.map(n => n.id)).toEqual(['n1', 'n2'])
    expect(result.current.rfEdges.map(e => e.id)).toEqual(['e1'])
    expect(result.current.fragmentLinks).toHaveLength(0)

    act(() => result.current.history.undo())

    expect(result.current.rfNodes.map(n => n.id).sort()).toEqual(['n1', 'n2', 'n3'])
    expect(result.current.rfEdges.map(e => e.id).sort()).toEqual(['e1', 'fl-e1'])
    expect(result.current.fragmentLinks).toHaveLength(1)
  })
})

describe('handleDeleteSelected', () => {
  it('selected フラグの立ったノード・接続エッジのみ連鎖削除され、Undoで復元される', () => {
    const { result } = renderHarness({
      rfNodes: [
        { ...makeNode('n1', 0, 0, { nodeTypeId: 'ngol.logic.const', paramValues: {} }), selected: true },
        makeNode('n2', 100, 0, { nodeTypeId: 'ngol.logic.log', paramValues: {} }),
      ],
      rfEdges: [
        { id: 'e1', source: 'n1', target: 'n2', sourceHandle: 'out', targetHandle: 'in' },
      ],
    })

    act(() => result.current.handlers.handleDeleteSelected())

    expect(result.current.rfNodes.map(n => n.id)).toEqual(['n2'])
    expect(result.current.rfEdges).toHaveLength(0)

    act(() => result.current.history.undo())

    expect(result.current.rfNodes.map(n => n.id).sort()).toEqual(['n1', 'n2'])
    expect(result.current.rfEdges.map(e => e.id)).toEqual(['e1'])
  })
})

// ────────────────────────────────────────────────────────────────
// 3. handleNodeDragStop: グループ折りたたみ時(pushHistory)と展開時(recordHistory)の分岐差
// ────────────────────────────────────────────────────────────────

describe('handleNodeDragStop — グループドラッグ', () => {
  it('折りたたみ時: ドラッグ中は位置が動かず、ドロップ時に一括反映される（pushHistory）', () => {
    const groups: NodeGroup[] = [{ id: 'g1', name: 'G', collapsed: true, nodeInstanceIds: ['m1', 'm2'] }]
    const { result } = renderHarness({
      groups,
      rfNodes: [
        makeNode('group-g1', 0, 0),
        makeNode('m1', 10, 10, { nodeTypeId: 'x', paramValues: {} }),
        makeNode('m2', 20, 20, { nodeTypeId: 'x', paramValues: {} }),
      ],
    })
    const groupNodeStart = makeNode('group-g1', 0, 0)
    act(() => result.current.handlers.handleNodeDragStart({} as ReactMouseEvent, groupNodeStart, []))

    const groupNodeMoved = makeNode('group-g1', 15, 12)
    act(() => result.current.handlers.handleNodeDrag({} as ReactMouseEvent, groupNodeMoved))
    // 折りたたみ中は毎フレーム更新をスキップする
    expect(result.current.rfNodes.find(n => n.id === 'm1')?.position).toEqual({ x: 10, y: 10 })
    expect(result.current.rfNodes.find(n => n.id === 'm2')?.position).toEqual({ x: 20, y: 20 })

    act(() => result.current.handlers.handleNodeDragStop({} as ReactMouseEvent, groupNodeMoved, []))
    expect(result.current.rfNodes.find(n => n.id === 'm1')?.position).toEqual({ x: 25, y: 22 })
    expect(result.current.rfNodes.find(n => n.id === 'm2')?.position).toEqual({ x: 35, y: 32 })
    expect(result.current.history.canUndo).toBe(true)

    act(() => result.current.history.undo())
    expect(result.current.rfNodes.find(n => n.id === 'm1')?.position).toEqual({ x: 10, y: 10 })
    expect(result.current.rfNodes.find(n => n.id === 'm2')?.position).toEqual({ x: 20, y: 20 })
  })

  it('展開時: ドラッグ中に毎フレーム反映され、ドロップ時に二重適用されない（recordHistory）', () => {
    const groups: NodeGroup[] = [{ id: 'g1', name: 'G', collapsed: false, nodeInstanceIds: ['m1', 'm2'] }]
    const { result } = renderHarness({
      groups,
      rfNodes: [
        makeNode('group-g1', 0, 0),
        makeNode('m1', 10, 10, { nodeTypeId: 'x', paramValues: {} }),
        makeNode('m2', 20, 20, { nodeTypeId: 'x', paramValues: {} }),
      ],
    })
    const groupNodeStart = makeNode('group-g1', 0, 0)
    act(() => result.current.handlers.handleNodeDragStart({} as ReactMouseEvent, groupNodeStart, []))

    const groupNodeMoved = makeNode('group-g1', 15, 12)
    act(() => result.current.handlers.handleNodeDrag({} as ReactMouseEvent, groupNodeMoved))
    expect(result.current.rfNodes.find(n => n.id === 'm1')?.position).toEqual({ x: 25, y: 22 })
    expect(result.current.rfNodes.find(n => n.id === 'm2')?.position).toEqual({ x: 35, y: 32 })

    act(() => result.current.handlers.handleNodeDragStop({} as ReactMouseEvent, groupNodeMoved, []))
    // 二重適用されていないこと（(40,34) 等になっていないこと）
    expect(result.current.rfNodes.find(n => n.id === 'm1')?.position).toEqual({ x: 25, y: 22 })
    expect(result.current.rfNodes.find(n => n.id === 'm2')?.position).toEqual({ x: 35, y: 32 })
    expect(result.current.history.canUndo).toBe(true)

    act(() => result.current.history.undo())
    expect(result.current.rfNodes.find(n => n.id === 'm1')?.position).toEqual({ x: 10, y: 10 })
    expect(result.current.rfNodes.find(n => n.id === 'm2')?.position).toEqual({ x: 20, y: 20 })
  })
})

// ────────────────────────────────────────────────────────────────
// 4. handleParamEditEnd: ドラッグ中の連続変更が Undo 1 単位にまとまる
// ────────────────────────────────────────────────────────────────

describe('パラメータ編集の履歴バッチ', () => {
  it('ドラッグ中の複数回変更は履歴に積まれず、ドロップ時に1つのUndo単位として確定する', () => {
    const { result } = renderHarness({
      rfNodes: [makeNode('n1', 0, 0, { nodeTypeId: 'x', paramValues: { a: 1 } })],
      selectedNodeId: 'n1',
    })

    act(() => result.current.handlers.handleParamEditStart())
    act(() => result.current.handlers.handleParamChange('a', 2))
    act(() => result.current.handlers.handleParamChange('a', 3))
    // ドラッグ中は個々の変更が Undo スタックに積まれない
    expect(result.current.history.canUndo).toBe(false)
    expect((result.current.rfNodes[0].data as { paramValues: Record<string, unknown> }).paramValues.a).toBe(3)

    act(() => result.current.handlers.handleParamEditEnd())
    expect(result.current.history.canUndo).toBe(true)
    expect((result.current.rfNodes[0].data as { paramValues: Record<string, unknown> }).paramValues.a).toBe(3)

    act(() => result.current.history.undo())
    expect((result.current.rfNodes[0].data as { paramValues: Record<string, unknown> }).paramValues.a).toBe(1)

    act(() => result.current.history.redo())
    expect((result.current.rfNodes[0].data as { paramValues: Record<string, unknown> }).paramValues.a).toBe(3)
  })

  it('ドラッグを伴わない単発変更は即座にUndo可能な1ステップとして積まれる', () => {
    const { result } = renderHarness({
      rfNodes: [makeNode('n1', 0, 0, { nodeTypeId: 'x', paramValues: { a: 1 } })],
      selectedNodeId: 'n1',
    })

    act(() => result.current.handlers.handleParamChange('a', 99))
    expect(result.current.history.canUndo).toBe(true)
    expect((result.current.rfNodes[0].data as { paramValues: Record<string, unknown> }).paramValues.a).toBe(99)

    act(() => result.current.history.undo())
    expect((result.current.rfNodes[0].data as { paramValues: Record<string, unknown> }).paramValues.a).toBe(1)
  })
})
