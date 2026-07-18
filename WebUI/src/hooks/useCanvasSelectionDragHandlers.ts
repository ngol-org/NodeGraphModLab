import { useState, useCallback, useEffect, useRef } from 'react'
import type { Node, Edge, ReactFlowInstance, NodeChange } from '@xyflow/react'
import type { GroupContextMenuState } from '../components/GroupContextMenu'
import type { NodeGroup, FragmentLink } from '../types/protocol'
import type { PendingConnection } from '../lib/dragAddNode'

export interface UseCanvasSelectionDragHandlersParams {
  rfRef: React.MutableRefObject<ReactFlowInstance | null>
  setRfNodes: React.Dispatch<React.SetStateAction<Node[]>>
  setRfEdges: React.Dispatch<React.SetStateAction<Edge[]>>
  onNodesChange: (changes: NodeChange[]) => void
  selectedNodeId: string | null
  setSelectedNodeId: (id: string | null) => void
  fragmentLinks: FragmentLink[]
  setFragmentLinks: React.Dispatch<React.SetStateAction<FragmentLink[]>>
  groups: NodeGroup[]
  createGroup: (name: string, nodeIds: string[]) => void
  deleteGroup: (id: string) => void
  renameGroup: (id: string, newName: string) => void
  toggleCollapsed: (id: string) => void
  pushHistory: ReturnType<typeof import('./useGraphHistory').useGraphHistory>['push']
  undo: () => void
  redo: () => void
  recordHistory: ReturnType<typeof import('./useGraphHistory').useGraphHistory>['record']
  isSpacePressed: boolean
  isDragSelecting: boolean
  setIsDragSelecting: (v: boolean) => void
  multiSelectedIds: Set<string>
  setMultiSelectedIds: React.Dispatch<React.SetStateAction<Set<string>>>
  modifierKeyRef: React.MutableRefObject<'ctrl' | 'shift' | null>
  preSelectionRef: React.MutableRefObject<Set<string>>
  lastKnownSelectionRef: React.MutableRefObject<Set<string>>
  lastMouseRef: React.MutableRefObject<{ x: number; y: number }>
  setAddMenuPos: (pos: { x: number; y: number; canvasX: number; canvasY: number } | null) => void
  lastCanvasClickRef: React.MutableRefObject<{ x: number; y: number }>
  setFragmentImportMenuPos: (pos: { x: number; y: number; canvasX: number; canvasY: number } | null) => void
  closeAllMenus: () => void
  pendingConnectionRef: React.MutableRefObject<PendingConnection | null>
  handleSave: () => void
  openSaveAs: () => void
}

export function useCanvasSelectionDragHandlers({
  rfRef,
  setRfNodes,
  setRfEdges,
  onNodesChange,
  selectedNodeId,
  setSelectedNodeId,
  fragmentLinks,
  setFragmentLinks,
  groups,
  createGroup,
  deleteGroup,
  renameGroup,
  toggleCollapsed,
  pushHistory,
  undo,
  redo,
  recordHistory,
  isSpacePressed,
  setIsDragSelecting,
  multiSelectedIds,
  setMultiSelectedIds,
  modifierKeyRef,
  preSelectionRef,
  lastKnownSelectionRef,
  lastMouseRef,
  setAddMenuPos,
  lastCanvasClickRef,
  setFragmentImportMenuPos,
  closeAllMenus,
  pendingConnectionRef,
  handleSave,
  openSaveAs,
}: UseCanvasSelectionDragHandlersParams) {
  const groupsRef = useRef<NodeGroup[]>([])
  groupsRef.current = groups

  const groupNodeActionsRef = useRef({
    toggleCollapsed: (_id: string) => {},
    rename: (_id: string, _newName: string) => {},
    dissolve: (_id: string) => {},
  })
  groupNodeActionsRef.current = {
    toggleCollapsed: (id: string) => toggleCollapsed(id),
    rename: (id: string, newName: string) => renameGroup(id, newName),
    dissolve: (id: string) => deleteGroup(id),
  }
  const stableGroupToggle = useCallback((id: string) => groupNodeActionsRef.current.toggleCollapsed(id), [])
  const stableGroupRename = useCallback((id: string, newName: string) => groupNodeActionsRef.current.rename(id, newName), [])
  const stableGroupDissolve = useCallback((id: string) => groupNodeActionsRef.current.dissolve(id), [])

  const [groupContextMenu, setGroupContextMenu] = useState<GroupContextMenuState | null>(null)

  const preDragGroupInfo = useRef<{
    groupId: string
    startPos: { x: number; y: number }
    memberPositions: Map<string, { x: number; y: number }>
  } | null>(null)

  const [groupDragPositions, setGroupDragPositions] = useState<Map<string, { x: number; y: number }>>(new Map())
  const groupDragPositionsRef = useRef<Map<string, { x: number; y: number }>>(new Map())
  groupDragPositionsRef.current = groupDragPositions

  const fragmentLinksRef = useRef<FragmentLink[]>([])
  fragmentLinksRef.current = fragmentLinks
  const multiSelectedIdsRef = useRef<Set<string>>(new Set())
  multiSelectedIdsRef.current = multiSelectedIds
  const preDragPositions = useRef<Map<string, { x: number; y: number }>>(new Map())
  // 複数選択ドラッグ用。xyflowネイティブの selected 追従ドラッグは
  // 独自の選択管理ロジックとの競合で信頼できないため、multiSelectedIds を正とした
  // 手動delta計算方式で全選択ノードを追従させる（グループドラッグと同じパターン）。
  const preDragMultiInfo = useRef<{
    startPos: { x: number; y: number }
    memberPositions: Map<string, { x: number; y: number }>
  } | null>(null)

  const handleDeleteNode = useCallback((nodeId: string) => {
    const currentNodes = rfRef.current?.getNodes() ?? []
    const currentEdges = rfRef.current?.getEdges() ?? []
    const deletedNode = currentNodes.find(n => n.id === nodeId)
    if (!deletedNode) return
    const deletedEdges = currentEdges.filter(e => e.source === nodeId || e.target === nodeId)
    const deletedEdgeIds = new Set(deletedEdges.map(e => e.id))
    const deletedLinks = fragmentLinksRef.current.filter(fl =>
      fl.sourceSnapshotNodeInstanceId === nodeId || fl.toNodeInstanceId === nodeId
    )
    const deletedLinkKeys = new Set(deletedLinks.map(l =>
      `${l.sourceSnapshotNodeInstanceId}:${l.sourcePortName}:${l.toNodeInstanceId}:${l.toPortName}`
    ))
    pushHistory({
      label: 'Delete Node',
      do: () => {
        setRfNodes(prev => prev.filter(n => n.id !== nodeId))
        setRfEdges(prev => prev.filter(e => !deletedEdgeIds.has(e.id)))
        setFragmentLinks(prev => prev.filter(fl =>
          !deletedLinkKeys.has(`${fl.sourceSnapshotNodeInstanceId}:${fl.sourcePortName}:${fl.toNodeInstanceId}:${fl.toPortName}`)
        ))
      },
      undo: () => {
        setRfNodes(prev => [...prev, { ...deletedNode, selected: false }])
        setRfEdges(prev => [...prev, ...deletedEdges])
        setFragmentLinks(prev => [...prev, ...deletedLinks])
      },
    })
    if (selectedNodeId === nodeId) setSelectedNodeId(null)
  }, [rfRef, setRfNodes, setRfEdges, setFragmentLinks, selectedNodeId, pushHistory, setSelectedNodeId])

  const handleSaveRef = useRef<() => void>(() => {})
  handleSaveRef.current = handleSave
  const openSaveAsRef = useRef<() => void>(() => {})
  openSaveAsRef.current = openSaveAs

  useEffect(() => {
    const handleKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'z' && !e.shiftKey) {
        e.preventDefault()
        undo()
        setSelectedNodeId(null)
        setGroupContextMenu(null)
        return
      }
      if ((e.ctrlKey || e.metaKey) && (e.key.toLowerCase() === 'y' || (e.key.toLowerCase() === 'z' && e.shiftKey))) {
        e.preventDefault()
        redo()
        setSelectedNodeId(null)
        setGroupContextMenu(null)
        return
      }
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's' && !e.shiftKey) {
        e.preventDefault()
        handleSaveRef.current()
        return
      }
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's' && e.shiftKey) {
        e.preventDefault()
        openSaveAsRef.current()
        return
      }
      if (e.target instanceof HTMLInputElement || e.target instanceof HTMLTextAreaElement) return
      if (e.key === 'Delete' || e.key === 'Backspace') {
        const currentNodes = rfRef.current?.getNodes() ?? []
        const currentEdges = rfRef.current?.getEdges() ?? []
        const fl = fragmentLinksRef.current
        const delNodes = currentNodes.filter(n => n.selected)
        const delNodeIds = new Set(delNodes.map(n => n.id))
        const selectedEdges = currentEdges.filter(e => e.selected)
        const connectedEdges = currentEdges.filter(e => delNodeIds.has(e.source) || delNodeIds.has(e.target))
        const allDelEdgesMap = new Map([...selectedEdges, ...connectedEdges].map(e => [e.id, e]))
        const allDelEdges = [...allDelEdgesMap.values()]
        const allDelEdgeIds = new Set(allDelEdges.map(e => e.id))
        const delLinks = fl.filter(link =>
          delNodeIds.has(link.sourceSnapshotNodeInstanceId) ||
          delNodeIds.has(link.toNodeInstanceId) ||
          allDelEdges.some(e =>
            (e.data as { kind?: string } | undefined)?.kind === 'fragmentLink' &&
            e.source === link.sourceSnapshotNodeInstanceId &&
            (e.sourceHandle ?? 'value') === link.sourcePortName &&
            e.target === link.toNodeInstanceId &&
            (e.targetHandle ?? '') === link.toPortName
          )
        )
        const delLinkKeys = new Set(delLinks.map(l =>
          `${l.sourceSnapshotNodeInstanceId}:${l.sourcePortName}:${l.toNodeInstanceId}:${l.toPortName}`
        ))
        if (delNodes.length === 0 && allDelEdges.length === 0) return
        const savedNodes = [...delNodes]
        const savedEdges = [...allDelEdges]
        const savedLinks = [...delLinks]
        pushHistory({
          label: 'Delete',
          do: () => {
            setRfNodes(prev => prev.filter(n => !delNodeIds.has(n.id)))
            setRfEdges(prev => prev.filter(e => !allDelEdgeIds.has(e.id)))
            setFragmentLinks(prev => prev.filter(fl =>
              !delLinkKeys.has(`${fl.sourceSnapshotNodeInstanceId}:${fl.sourcePortName}:${fl.toNodeInstanceId}:${fl.toPortName}`)
            ))
            setMultiSelectedIds(new Set())
            lastKnownSelectionRef.current = new Set()
          },
          undo: () => {
            setRfNodes(prev => [...prev, ...savedNodes.map(n => ({ ...n, selected: false }))])
            setRfEdges(prev => [...prev, ...savedEdges])
            setFragmentLinks(prev => [...prev, ...savedLinks])
            setMultiSelectedIds(new Set())
            lastKnownSelectionRef.current = new Set()
          },
        })
        return
      }
      if (e.key === 'a' || e.key === 'A') {
        e.preventDefault()
        pendingConnectionRef.current = null
        const { x: mx, y: my } = lastMouseRef.current
        const flowPos = rfRef.current?.screenToFlowPosition({ x: mx, y: my })
        const canvasX = flowPos?.x ?? 200
        const canvasY = flowPos?.y ?? 200
        setAddMenuPos({ x: mx, y: my, canvasX, canvasY })
      }
      if (e.key === 'f' || e.key === 'F') {
        const { x: mx, y: my } = lastMouseRef.current
        const flowPos = rfRef.current?.screenToFlowPosition({ x: mx, y: my })
        const canvasX = flowPos?.x ?? 200
        const canvasY = flowPos?.y ?? 200
        lastCanvasClickRef.current = { x: canvasX, y: canvasY }
        setFragmentImportMenuPos({ x: mx, y: my, canvasX, canvasY })
      }
    }
    window.addEventListener('keydown', handleKey)
    return () => window.removeEventListener('keydown', handleKey)
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [undo, redo, pushHistory, setSelectedNodeId, setMultiSelectedIds, setAddMenuPos, setFragmentImportMenuPos, lastMouseRef, lastKnownSelectionRef, lastCanvasClickRef, rfRef, pendingConnectionRef])

  const onSelectionStart = useCallback(() => {
    preSelectionRef.current = new Set(lastKnownSelectionRef.current)
    setIsDragSelecting(true)
  }, [preSelectionRef, lastKnownSelectionRef, setIsDragSelecting])

  const onSelectionEnd = useCallback(() => {
    setIsDragSelecting(false)
    const modifier = modifierKeyRef.current
    const pre = preSelectionRef.current
    const nodes = rfRef.current?.getNodes() ?? []
    const boxSelected = new Set(nodes.filter(n => n.selected).map(n => n.id))
    let finalIds: Set<string>
    if (modifier === 'ctrl') {
      finalIds = new Set([...pre, ...boxSelected])
      setRfNodes(prev => prev.map(n => ({ ...n, selected: finalIds.has(n.id) })))
    } else if (modifier === 'shift') {
      finalIds = new Set([...pre].filter(id => !boxSelected.has(id)))
      setRfNodes(prev => prev.map(n => ({ ...n, selected: finalIds.has(n.id) })))
    } else {
      finalIds = boxSelected
    }
    setMultiSelectedIds(finalIds)
    lastKnownSelectionRef.current = finalIds
  }, [rfRef, modifierKeyRef, preSelectionRef, lastKnownSelectionRef, setIsDragSelecting, setRfNodes, setMultiSelectedIds])

  const onNodeClick = useCallback((e: React.MouseEvent, node: Node) => {
    if (isSpacePressed) return
    if (e.shiftKey) {
      const newSelection = new Set([...multiSelectedIdsRef.current].filter(id => id !== node.id))
      setRfNodes(prev => prev.map(n => ({ ...n, selected: newSelection.has(n.id) })))
      setMultiSelectedIds(newSelection)
      lastKnownSelectionRef.current = newSelection
      return
    }
    if (e.ctrlKey || e.metaKey) {
      const newSelection = new Set([...multiSelectedIdsRef.current, node.id])
      setRfNodes(prev => prev.map(n => ({ ...n, selected: newSelection.has(n.id) })))
      setMultiSelectedIds(newSelection)
      lastKnownSelectionRef.current = newSelection
      return
    }
    if (multiSelectedIdsRef.current.size >= 2 && multiSelectedIdsRef.current.has(node.id)) {
      const currentIds = multiSelectedIdsRef.current
      setRfNodes(prev => prev.map(n => ({ ...n, selected: currentIds.has(n.id) })))
      return
    }
    setRfNodes(prev => prev.map(n => ({ ...n, selected: n.id === node.id })))
    setSelectedNodeId(node.id)
    setMultiSelectedIds(new Set([node.id]))
    lastKnownSelectionRef.current = new Set([node.id])
  }, [isSpacePressed, setRfNodes, setSelectedNodeId, setMultiSelectedIds, lastKnownSelectionRef])

  const handleDeleteSelected = useCallback(() => {
    const currentNodes = rfRef.current?.getNodes() ?? []
    const currentEdges = rfRef.current?.getEdges() ?? []
    const fl = fragmentLinksRef.current
    const delNodes = currentNodes.filter(n => n.selected)
    const delNodeIds = new Set(delNodes.map(n => n.id))
    const connectedEdges = currentEdges.filter(e => delNodeIds.has(e.source) || delNodeIds.has(e.target))
    const allDelEdgeIds = new Set(connectedEdges.map(e => e.id))
    const delLinks = fl.filter(l =>
      delNodeIds.has(l.sourceSnapshotNodeInstanceId) || delNodeIds.has(l.toNodeInstanceId)
    )
    const delLinkKeys = new Set(delLinks.map(l =>
      `${l.sourceSnapshotNodeInstanceId}:${l.sourcePortName}:${l.toNodeInstanceId}:${l.toPortName}`
    ))
    if (delNodes.length === 0) return
    const savedNodes = [...delNodes]
    const savedEdges = [...connectedEdges]
    const savedLinks = [...delLinks]
    pushHistory({
      label: 'Delete',
      do: () => {
        setRfNodes(prev => prev.filter(n => !delNodeIds.has(n.id)))
        setRfEdges(prev => prev.filter(e => !allDelEdgeIds.has(e.id)))
        setFragmentLinks(prev => prev.filter(fl =>
          !delLinkKeys.has(`${fl.sourceSnapshotNodeInstanceId}:${fl.sourcePortName}:${fl.toNodeInstanceId}:${fl.toPortName}`)
        ))
        setMultiSelectedIds(new Set())
        lastKnownSelectionRef.current = new Set()
      },
      undo: () => {
        setRfNodes(prev => [...prev, ...savedNodes.map(n => ({ ...n, selected: false }))])
        setRfEdges(prev => [...prev, ...savedEdges])
        setFragmentLinks(prev => [...prev, ...savedLinks])
        setMultiSelectedIds(new Set())
        lastKnownSelectionRef.current = new Set()
      },
    })
    setSelectedNodeId(null)
  }, [rfRef, setRfNodes, setRfEdges, setFragmentLinks, pushHistory, setSelectedNodeId, setMultiSelectedIds, lastKnownSelectionRef])

  const handleNodesChange = useCallback((changes: NodeChange[]) => {
    let hasGroupChange = false
    const newPositions = new Map(groupDragPositionsRef.current)
    for (const c of changes) {
      if (c.type === 'position' && c.id?.startsWith('group-')) {
        const pos = (c as { position?: { x: number; y: number } }).position
        if (pos) {
          newPositions.set(c.id.slice('group-'.length), pos)
          hasGroupChange = true
        }
      }
    }
    if (hasGroupChange) setGroupDragPositions(new Map(newPositions))
    onNodesChange(changes)
  }, [onNodesChange])

  const handleNodeDrag = useCallback((_e: React.MouseEvent, node: Node) => {
    if (node.id.startsWith('group-')) {
      const dragInfo = preDragGroupInfo.current
      if (!dragInfo || dragInfo.groupId !== node.id.slice('group-'.length)) return
      // 折りたたみ状態: メンバーは非表示のため毎フレーム座標更新しても見た目に影響しない。
      // 無駄な全ノード再計算を避け、確定は handleNodeDragStop でまとめて行う。
      const group = groupsRef.current.find(g => g.id === dragInfo.groupId)
      if (group?.collapsed) return
      const delta = {
        x: node.position.x - dragInfo.startPos.x,
        y: node.position.y - dragInfo.startPos.y,
      }
      if (Math.abs(delta.x) < 0.1 && Math.abs(delta.y) < 0.1) return
      setRfNodes(prev => prev.map(n => {
        const origPos = dragInfo.memberPositions.get(n.id)
        if (!origPos) return n
        return { ...n, position: { x: origPos.x + delta.x, y: origPos.y + delta.y } }
      }))
      return
    }
    // 複数選択ドラッグ — ドラッグ中のノード自身は xyflow が既にネイティブに動かしているため、
    // それ以外の選択済みノードにだけ同じ delta を手動適用して追従させる。
    const multiInfo = preDragMultiInfo.current
    if (!multiInfo) return
    const delta = {
      x: node.position.x - multiInfo.startPos.x,
      y: node.position.y - multiInfo.startPos.y,
    }
    if (Math.abs(delta.x) < 0.1 && Math.abs(delta.y) < 0.1) return
    setRfNodes(prev => prev.map(n => {
      if (n.id === node.id) return n
      const origPos = multiInfo.memberPositions.get(n.id)
      if (!origPos) return n
      return { ...n, position: { x: origPos.x + delta.x, y: origPos.y + delta.y } }
    }))
  }, [setRfNodes])

  const handleCreateGroup = useCallback(() => {
    const selectedIds = Array.from(multiSelectedIdsRef.current)
    if (selectedIds.length < 2) return
    createGroup('Group', selectedIds)
  }, [createGroup])

  const handleNodeDragStart = useCallback((_e: React.MouseEvent, node: Node, nodes: Node[]) => {
    if (node.id.startsWith('group-')) {
      const groupId = node.id.slice('group-'.length)
      const group = groupsRef.current.find(g => g.id === groupId)
      if (group) {
        const memberPositions = new Map<string, { x: number; y: number }>()
        rfRef.current?.getNodes().forEach(n => {
          if (group.nodeInstanceIds.includes(n.id)) {
            memberPositions.set(n.id, { x: n.position.x, y: n.position.y })
          }
        })
        preDragGroupInfo.current = {
          groupId,
          startPos: { x: node.position.x, y: node.position.y },
          memberPositions,
        }
      }
      return
    }
    preDragPositions.current = new Map(nodes.map(n => [n.id, { x: n.position.x, y: n.position.y }]))
    if (!multiSelectedIdsRef.current.has(node.id)) {
      setMultiSelectedIds(new Set())
      lastKnownSelectionRef.current = new Set()
      preDragMultiInfo.current = null
    } else if (multiSelectedIdsRef.current.size >= 2) {
      const memberPositions = new Map<string, { x: number; y: number }>()
      rfRef.current?.getNodes().forEach(n => {
        if (multiSelectedIdsRef.current.has(n.id)) {
          memberPositions.set(n.id, { x: n.position.x, y: n.position.y })
        }
      })
      preDragMultiInfo.current = {
        startPos: { x: node.position.x, y: node.position.y },
        memberPositions,
      }
    } else {
      preDragMultiInfo.current = null
    }
  }, [rfRef, setMultiSelectedIds, lastKnownSelectionRef])

  const handleNodeDragStop = useCallback((_e: React.MouseEvent, node: Node, nodes: Node[]) => {
    if (node.id.startsWith('annot-')) return
    if (node.id.startsWith('group-')) {
      const dragInfo = preDragGroupInfo.current
      preDragGroupInfo.current = null
      setGroupDragPositions(prev => {
        const next = new Map(prev)
        next.delete(node.id.slice('group-'.length))
        return next
      })
      if (!dragInfo) return
      const group = groupsRef.current.find(g => g.id === dragInfo.groupId)
      const delta = {
        x: node.position.x - dragInfo.startPos.x,
        y: node.position.y - dragInfo.startPos.y,
      }
      if (Math.abs(delta.x) < 0.5 && Math.abs(delta.y) < 0.5) return
      const beforePositions = new Map(dragInfo.memberPositions)
      const afterPositions = new Map<string, { x: number; y: number }>()
      dragInfo.memberPositions.forEach((pos, id) => {
        afterPositions.set(id, { x: pos.x + delta.x, y: pos.y + delta.y })
      })
      const cmd = {
        label: 'グループ移動',
        do: () => setRfNodes(prev => prev.map(n => {
          const pos = afterPositions.get(n.id)
          return pos ? { ...n, position: pos } : n
        })),
        undo: () => setRfNodes(prev => prev.map(n => {
          const pos = beforePositions.get(n.id)
          return pos ? { ...n, position: pos } : n
        })),
      }
      // 折りたたみ時: handleNodeDrag 側で毎フレーム更新をスキップしているため、
      // ここで do() を実行して実座標を確定させる必要がある（pushHistory を使う）。
      // 展開時: handleNodeDrag が既に毎フレーム座標を反映済みなので、二重適用を避けるため
      // record（do() を呼ばない）で履歴にだけ積む。
      if (group?.collapsed) {
        pushHistory(cmd)
      } else {
        recordHistory(cmd)
      }
      return
    }
    // 複数選択ドラッグの場合、xyflowネイティブの `nodes` 引数は選択中の
    // 全ノードを含むとは限らない（selected同期のタイミング問題）ため、handleNodeDrag で
    // 実際に手動移動させた対象（multiInfo.memberPositions）を正として Undo 履歴を記録する。
    const multiInfo = preDragMultiInfo.current
    preDragMultiInfo.current = null
    if (multiInfo) {
      const before = multiInfo.memberPositions
      const liveNodes = rfRef.current?.getNodes() ?? []
      const after = new Map<string, { x: number; y: number }>()
      before.forEach((_pos, id) => {
        const live = liveNodes.find(n => n.id === id)
        if (live) after.set(id, { x: live.position.x, y: live.position.y })
      })
      const hasMoved = Array.from(before.entries()).some(([id, pos]) => {
        const a = after.get(id)
        return a && (a.x !== pos.x || a.y !== pos.y)
      })
      if (!hasMoved) return
      pushHistory({
        label: 'Move Node',
        do: () => setRfNodes(prev => prev.map(n => {
          const pos = after.get(n.id)
          return pos ? { ...n, position: pos } : n
        })),
        undo: () => setRfNodes(prev => prev.map(n => {
          const pos = before.get(n.id)
          return pos ? { ...n, position: pos } : n
        })),
      })
      return
    }
    const before = preDragPositions.current
    const after = new Map(nodes.map(n => [n.id, { x: n.position.x, y: n.position.y }]))
    const hasMoved = nodes.some(n => {
      const prev = before.get(n.id)
      return prev && (prev.x !== n.position.x || prev.y !== n.position.y)
    })
    if (!hasMoved) return
    pushHistory({
      label: 'Move Node',
      do: () => setRfNodes(prev => prev.map(n => {
        const pos = after.get(n.id)
        return pos ? { ...n, position: pos } : n
      })),
      undo: () => setRfNodes(prev => prev.map(n => {
        const pos = before.get(n.id)
        return pos ? { ...n, position: pos } : n
      })),
    })
  }, [rfRef, setRfNodes, pushHistory, recordHistory])

  const onPaneClick = useCallback((e: React.MouseEvent) => {
    const flowPos = rfRef.current?.screenToFlowPosition({ x: e.clientX, y: e.clientY })
    if (flowPos) lastCanvasClickRef.current = flowPos
    setSelectedNodeId(null)
    setMultiSelectedIds(new Set())
    lastKnownSelectionRef.current = new Set()
    setGroupContextMenu(null)
    pendingConnectionRef.current = null
    closeAllMenus()
  }, [rfRef, lastCanvasClickRef, setSelectedNodeId, setMultiSelectedIds, lastKnownSelectionRef, closeAllMenus, pendingConnectionRef])

  return {
    groupsRef,
    groupContextMenu,
    setGroupContextMenu,
    groupDragPositions,
    stableGroupToggle,
    stableGroupRename,
    stableGroupDissolve,
    handleDeleteNode,
    onSelectionStart,
    onSelectionEnd,
    onNodeClick,
    onPaneClick,
    handleDeleteSelected,
    handleNodesChange,
    handleNodeDrag,
    handleCreateGroup,
    handleNodeDragStart,
    handleNodeDragStop,
  }
}
