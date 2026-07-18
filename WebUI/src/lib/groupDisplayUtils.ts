import type { Node, Edge } from '@xyflow/react'
import type { GroupNodeData } from '../components/GroupNode'
import type { NodeGroup } from '../types/protocol'

// グループ背景矩形のパディング（px）
export const GROUP_PADDING = 40
// 展開時グループノードのデフォルトノードサイズ推定（実際のサイズは不明なので大き目に）
export const NODE_EST_WIDTH = 200
export const NODE_EST_HEIGHT = 100

/** グループ state と rfNodes/rfEdges から ReactFlow に渡す表示用 nodes/edges を構築する */
export function buildGroupedRfElements(
  baseNodes: Node[],
  baseEdges: Edge[],
  groups: NodeGroup[],
  groupDragPositions: Map<string, { x: number; y: number }>,
  onToggleCollapsed: (id: string) => void,
  onRename: (id: string, newName: string) => void,
  onDissolve: (id: string) => void,
  /** ReactFlow標準インタラクティブロックボタンの状態 */
  controlsLocked: boolean,
): { displayNodes: Node[]; displayEdges: Edge[] } {
  if (groups.length === 0) return { displayNodes: baseNodes, displayEdges: baseEdges }

  const collapsedGroups = groups.filter(g => g.collapsed)
  const expandedGroups = groups.filter(g => !g.collapsed)

  const collapsedMemberIds = new Set<string>()
  collapsedGroups.forEach(g => g.nodeInstanceIds.forEach(id => collapsedMemberIds.add(id)))

  const memberToCollapsedGroup = new Map<string, NodeGroup>()
  collapsedGroups.forEach(g => g.nodeInstanceIds.forEach(id => memberToCollapsedGroup.set(id, g)))

  const nodeMap = new Map(baseNodes.map(n => [n.id, n]))

  // ノード: 折りたたみグループのメンバーを hidden に
  const displayNodes: Node[] = baseNodes.map(n =>
    collapsedMemberIds.has(n.id) ? { ...n, hidden: true } : n
  )

  // 展開グループの背景矩形ノードを追加
  expandedGroups.forEach(group => {
    const members = group.nodeInstanceIds
      .map(id => nodeMap.get(id))
      .filter((n): n is Node => n !== undefined)
    if (members.length === 0) return

    // xyflow が ResizeObserver で実測した現在サイズ（n.measured）を最優先で使う。
    // nodeRenderer 拡張ノード等、標準ノードより大きく/小さくレンダリングされるノードでは
    // 固定推定値(NODE_EST_*) を使うとグループ矩形がノードを覗き込めない。
    // measured がまだ無い場合（追加直後で初回描画未測定）は角ドラッグ手動リサイズ値、
    // それも無ければ推定値にフォールバックする。
    const memberWidth = (n: Node) =>
      n.measured?.width ?? (n.data as { size?: { width: number } }).size?.width ?? NODE_EST_WIDTH
    const memberHeight = (n: Node) =>
      n.measured?.height ?? (n.data as { size?: { height: number } }).size?.height ?? NODE_EST_HEIGHT

    const minX = Math.min(...members.map(n => n.position.x)) - GROUP_PADDING
    const minY = Math.min(...members.map(n => n.position.y)) - GROUP_PADDING
    const maxX = Math.max(...members.map(n => n.position.x + memberWidth(n))) + GROUP_PADDING
    const maxY = Math.max(...members.map(n => n.position.y + memberHeight(n))) + GROUP_PADDING
    const width = maxX - minX
    const height = maxY - minY

    const groupNodeData: GroupNodeData = {
      groupId: group.id,
      name: group.name,
      description: group.description,
      memberCount: group.nodeInstanceIds.length,
      collapsed: false,
      color: group.color,
      width,
      height,
      onToggleCollapsed,
      onRename,
      onDissolve,
    }
    const groupNode: Node = {
      id: `group-${group.id}`,
      type: 'nodeGroup',
      position: { x: minX, y: minY },
      // width/height を明示しないと xyflow の nodeHasDimensions() が ResizeObserver による
      // 非同期実測(measured)待ちになり、buildGroupedRfElements が再計算される度(ホバー等の
      // 頻繁な状態変化のたびに一瞬 visibility:hidden になってクリックが素通りする
      width,
      height,
      draggable: !controlsLocked,
      selectable: false,
      dragHandle: '.group-drag-handle',
      data: groupNodeData as unknown as Record<string, unknown>,
      className: 'group-node-expanded-wrapper nopan',
    }
    displayNodes.unshift(groupNode) // 先頭に追加して背面レンダリング
  })

  // 折りたたみグループのコンパクトノードを追加
  collapsedGroups.forEach(group => {
    const members = group.nodeInstanceIds
      .map(id => nodeMap.get(id))
      .filter((n): n is Node => n !== undefined)
    if (members.length === 0) return

    const centerX = members.reduce((sum, n) => sum + n.position.x, 0) / members.length
    const centerY = members.reduce((sum, n) => sum + n.position.y, 0) / members.length

    const groupNodeData: GroupNodeData = {
      groupId: group.id,
      name: group.name,
      description: group.description,
      memberCount: group.nodeInstanceIds.length,
      collapsed: true,
      color: group.color,
      onToggleCollapsed,
      onRename,
      onDissolve,
    }
    const groupNode: Node = {
      id: `group-${group.id}`,
      type: 'nodeGroup',
      position: groupDragPositions.get(group.id) ?? { x: centerX - 100, y: centerY - 20 },
      // .group-node-collapsed の固定サイズ(200x40)と一致させる。理由は展開時と同じ（上記コメント参照）。
      width: 200,
      height: 40,
      draggable: !controlsLocked,
      selectable: !controlsLocked,
      data: groupNodeData as unknown as Record<string, unknown>,
    }
    displayNodes.push(groupNode)
  })

  // エッジ: 折りたたみグループに関するバーチャルエッジ変換
  const displayEdges: Edge[] = baseEdges.map(e => {
    const srcCollapsed = collapsedMemberIds.has(e.source)
    const dstCollapsed = collapsedMemberIds.has(e.target)

    if (!srcCollapsed && !dstCollapsed) return e

    const srcGroup = memberToCollapsedGroup.get(e.source)
    const dstGroup = memberToCollapsedGroup.get(e.target)

    if (srcCollapsed && dstCollapsed) {
      if (srcGroup?.id === dstGroup?.id) {
        return { ...e, hidden: true } // 同一グループ内部エッジ → 非表示
      }
      // 異グループ間エッジ → group-out → group-in
      return {
        ...e,
        id: `virt-${e.id}`,
        source: `group-${srcGroup!.id}`,
        sourceHandle: 'group-out',
        target: `group-${dstGroup!.id}`,
        targetHandle: 'group-in',
        hidden: false,
      }
    }

    if (srcCollapsed) {
      return {
        ...e,
        id: `virt-${e.id}`,
        source: `group-${srcGroup!.id}`,
        sourceHandle: 'group-out',
        hidden: false,
      }
    }

    // dstCollapsed
    return {
      ...e,
      id: `virt-${e.id}`,
      target: `group-${dstGroup!.id}`,
      targetHandle: 'group-in',
      hidden: false,
    }
  })

  return { displayNodes, displayEdges }
}
