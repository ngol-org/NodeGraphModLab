import { useMemo } from 'react'
import type { Node, Edge } from '@xyflow/react'
import { getFragmentIdForNode } from '../lib/fragmentUtils'
import { buildGroupedRfElements } from '../lib/groupDisplayUtils'
import type { FragmentDefinition, NodeGroup } from '../types/protocol'
import type { PersistentNodeInfo } from '../types/protocol'

interface UseCanvasDisplayNodesParams {
  rfNodes: Node[]
  rfEdges: Edge[]
  nodeStatus: Record<string, string>
  savedSnapshots: Map<string, unknown>
  /** `${nodeId}:${portName}` キーの全ポート分バッジ（複数ポートSetSnapshotノード用） */
  savedSnapshotsByPort: Map<string, unknown>
  pinnedNodes: Set<string>
  justSavedNodes: Set<string>
  fragments: FragmentDefinition[]
  hoveredFragmentId: string | null
  coExecutedFragmentIds: Set<string>
  snapshotInputNodeIds: Set<string>
  persistentNodes: Map<string, PersistentNodeInfo>
  isSpacePressed: boolean
  /** ReactFlow標準インタラクティブロックボタンの状態 */
  controlsLocked: boolean
  isDragSelecting: boolean
  multiSelectedIds: Set<string>
  modifierKeyRef: React.MutableRefObject<'ctrl' | 'shift' | null>
  groups: NodeGroup[]
  groupDragPositions: Map<string, { x: number; y: number }>
  stableGroupToggle: (id: string) => void
  stableGroupRename: (id: string, newName: string) => void
  stableGroupDissolve: (id: string) => void
  stableAnnotationTextChange: (annotId: string, text: string) => void
  stableAnnotationDelete: (annotId: string) => void
  stableAnnotationResize: (annotId: string, width: number, height: number) => void
}

export function useCanvasDisplayNodes({
  rfNodes,
  rfEdges,
  nodeStatus,
  savedSnapshots,
  savedSnapshotsByPort,
  pinnedNodes,
  justSavedNodes,
  fragments,
  hoveredFragmentId,
  coExecutedFragmentIds,
  snapshotInputNodeIds,
  persistentNodes,
  isSpacePressed,
  controlsLocked,
  isDragSelecting,
  multiSelectedIds,
  modifierKeyRef,
  groups,
  groupDragPositions,
  stableGroupToggle,
  stableGroupRename,
  stableGroupDissolve,
  stableAnnotationTextChange,
  stableAnnotationDelete,
  stableAnnotationResize,
}: UseCanvasDisplayNodesParams) {
  // ノードID → { portName: badge } のグルーピングを一度だけ計算
  // （複数ポートに SetSnapshot するノードで各ポートのバッジを個別に参照できるようにする）
  const badgesByNode = useMemo(() => {
    const grouped = new Map<string, Record<string, unknown>>()
    for (const [key, badge] of savedSnapshotsByPort) {
      const sep = key.indexOf(':')
      if (sep < 0) continue
      const nodeId = key.slice(0, sep)
      const portName = key.slice(sep + 1)
      let rec = grouped.get(nodeId)
      if (!rec) { rec = {}; grouped.set(nodeId, rec) }
      rec[portName] = badge
    }
    return grouped
  }, [savedSnapshotsByPort])

  // ノードにハイライトclassNameと savedSnapshot/pin data を付加
  const nodesWithHighlight = useMemo<Node[]>(() => {
    // ドラッグ中: box選択ノード + 既存選択を修飾キーに応じて統合した実効選択セット
    // これにより Ctrl+drag時の既存ハイライト維持とボックス選択プレビューを一元管理
    let effectiveIds: Set<string>
    if (isDragSelecting) {
      const boxSelected = new Set(rfNodes.filter(n => n.selected).map(n => n.id))
      // eslint-disable-next-line react-hooks/exhaustive-deps
      const mod = modifierKeyRef.current
      if (mod === 'ctrl') {
        effectiveIds = new Set([...multiSelectedIds, ...boxSelected])
      } else if (mod === 'shift') {
        effectiveIds = new Set([...multiSelectedIds].filter(id => !boxSelected.has(id)))
      } else {
        effectiveIds = boxSelected
      }
    } else {
      effectiveIds = multiSelectedIds
    }
    const isMultiSelectMode = effectiveIds.size >= 2

    return rfNodes.map(n => {
      const status = nodeStatus[n.id]
      const snapshotBadge = savedSnapshots.get(n.id) ?? null
      const snapshotBadgesByPort = badgesByNode.get(n.id)
      const snapshotPinned = pinnedNodes.has(n.id)
      const snapshotJustSaved = justSavedNodes.has(n.id)
      // T13: 断片IDをdataに付加
      const fragmentId = getFragmentIdForNode(n.id, fragments)
      const withBadge = {
        ...n,
        draggable: !isSpacePressed && !controlsLocked,
        selectable: !isSpacePressed && !controlsLocked,
        data: { ...n.data, snapshotBadge, snapshotBadgesByPort, snapshotPinned, snapshotJustSaved, fragmentId, isMultiSelect: isMultiSelectMode, isPersistent: persistentNodes.has(n.id) },
      }
      // T14: ホバーハイライト — 同断片: highlighted / 連動上流: co-executed / 他断片: dimmed
      const fragmentClass = hoveredFragmentId
        ? fragmentId === hoveredFragmentId
          ? 'node-fragment-highlighted'
          : coExecutedFragmentIds.has(fragmentId ?? '')
            ? 'node-fragment-co-executed'
            : snapshotInputNodeIds.has(n.id)
              ? 'node-fragment-snapshot-input'
              : 'node-fragment-dimmed'
        : null
      const baseStatus = (!status || status === 'idle') ? null : status
      // 複数選択ハイライト: effectiveIds で判定（Ctrl+drag中の既存選択保持・プレビューを含む）
      const multiSelectClass = isMultiSelectMode && effectiveIds.has(n.id) ? 'node-multi-selected' : null
      const combinedClass = [fragmentClass, baseStatus, multiSelectClass].filter(Boolean).join(' ') || undefined
      // className は常に上書き（undefined でも可）— 旧クラスが復元ノードに残るのを防ぐ
      return { ...withBadge, className: combinedClass }
    })
  },
  // eslint-disable-next-line react-hooks/exhaustive-deps
  [rfNodes, nodeStatus, savedSnapshots, badgesByNode, pinnedNodes, justSavedNodes, fragments, hoveredFragmentId, coExecutedFragmentIds, snapshotInputNodeIds, isSpacePressed, controlsLocked, isDragSelecting, multiSelectedIds, persistentNodes]
  )

  // PIN 済み Snapshot ノードへの入力エッジをグレーアウト (T11-B)
  const edgesWithHighlight = useMemo(() =>
    rfEdges.map(e => {
      const isFragmentLink = (e.data as { kind?: string } | undefined)?.kind === 'fragmentLink'
      if (!isFragmentLink && e.target && pinnedNodes.has(e.target)) {
        return { ...e, className: [e.className, 'edge-target-pinned'].filter(Boolean).join(' ') }
      }
      return e
    }),
    [rfEdges, pinnedNodes]
  )

  // グループ表示要素の構築（グループ背景ノード追加・折りたたみ表示）
  // annotation ノード（type='annotation'）は buildGroupedRfElements に渡さず、displayNodes の先頭に追加する
  const [baseRfNodes, annotationRfNodes] = useMemo(() => {
    const base: Node[] = []
    const annot: Node[] = []
    for (const n of nodesWithHighlight) {
      if (n.type === 'annotation') annot.push(n)
      else base.push(n)
    }
    return [base, annot]
  }, [nodesWithHighlight])

  const { displayNodes: groupedNodes, displayEdges } = useMemo(
    () => buildGroupedRfElements(baseRfNodes, edgesWithHighlight, groups, groupDragPositions, stableGroupToggle, stableGroupRename, stableGroupDissolve, controlsLocked),
    [baseRfNodes, edgesWithHighlight, groups, groupDragPositions, stableGroupToggle, stableGroupRename, stableGroupDissolve, controlsLocked]
  )

  // Annotation ノードを displayNodes の先頭に追加（背面に表示）
  const displayNodes = useMemo(() => {
    // stableAnnotationTextChange/Delete を data に注入
    const annotWithCallbacks = annotationRfNodes.map(n => ({
      ...n,
      data: {
        ...n.data,
        onTextChange: stableAnnotationTextChange,
        onDelete: stableAnnotationDelete,
        onResize: stableAnnotationResize,
      },
    }))
    return [...annotWithCallbacks, ...groupedNodes]
  }, [annotationRfNodes, groupedNodes, stableAnnotationTextChange, stableAnnotationDelete, stableAnnotationResize])

  return { displayNodes, displayEdges }
}
