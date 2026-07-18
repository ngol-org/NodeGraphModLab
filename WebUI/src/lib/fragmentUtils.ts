import type { Node, Edge } from '@xyflow/react'
import type { FragmentDefinition, FragmentLink } from '../types/protocol'

class UnionFind {
  private parent = new Map<string, string>()

  find(x: string): string {
    if (!this.parent.has(x)) this.parent.set(x, x)
    const p = this.parent.get(x)!
    if (p !== x) this.parent.set(x, this.find(p))
    return this.parent.get(x)!
  }

  union(x: string, y: string) {
    const rx = this.find(x)
    const ry = this.find(y)
    if (rx === ry) return
    // lexicographically smaller becomes root for ID stability
    if (rx < ry) this.parent.set(ry, rx)
    else this.parent.set(rx, ry)
  }
}

/**
 * 通常コネクタ（fragmentLink 以外）の連結成分から断片定義を自動導出する。
 * 断片 ID は構成ノードの最小 instanceId から決まるため安定している。
 */
export function computeFragments(nodes: Node[], edges: Edge[]): FragmentDefinition[] {
  const validNodes = nodes.filter(n => n.type !== 'nodeGroup' && n.type !== 'annotation')
  if (validNodes.length === 0) return []

  const uf = new UnionFind()
  for (const node of validNodes) uf.find(node.id)

  for (const edge of edges) {
    const kind = (edge.data as { kind?: string } | undefined)?.kind
    if (kind === 'fragmentLink') continue
    if (edge.source && edge.target) uf.union(edge.source, edge.target)
  }

  const components = new Map<string, string[]>()
  for (const node of validNodes) {
    const root = uf.find(node.id)
    if (!components.has(root)) components.set(root, [])
    components.get(root)!.push(node.id)
  }

  const sortedRoots = [...components.keys()].sort()
  return sortedRoots.map((root, i) => ({
    id: `auto-${root}`,
    name: `Fragment ${i + 1}`,
    nodeInstanceIds: components.get(root)!,
  }))
}

/** ノードが属する断片の ID を返す。未所属の場合は null。 */
export function getFragmentIdForNode(nodeId: string, fragments: FragmentDefinition[]): string | null {
  return fragments.find(f => f.nodeInstanceIds.includes(nodeId))?.id ?? null
}

/** ノードタイプ ID が Snapshot 系ノードかどうかを判定する。 */
export function isSnapshotNodeTypeId(nodeTypeId: string | undefined): boolean {
  return !!nodeTypeId?.startsWith('ngol.snapshot.')
}

export interface FragmentLinkCandidate {
  sourceNodeId: string
  sourceNodeTypeId: string | undefined
  sourceHandle: string | null | undefined
  targetNodeId: string
  targetHandle: string | null | undefined
}

/**
 * 接続候補が断片リンクの条件（source が Snapshot ノードかつ source/target が別断片）を満たす場合、
 * 断片リンク用の Edge と FragmentLink を組み立てて返す。満たさない場合は null（＝通常リンクとして扱う）。
 */
export function resolveFragmentLink(
  candidate: FragmentLinkCandidate,
  fragments: FragmentDefinition[],
): { edge: Edge; link: FragmentLink } | null {
  if (!isSnapshotNodeTypeId(candidate.sourceNodeTypeId)) return null

  const srcFragId = getFragmentIdForNode(candidate.sourceNodeId, fragments)
  const dstFragId = getFragmentIdForNode(candidate.targetNodeId, fragments)
  if (srcFragId === dstFragId) return null

  return {
    edge: {
      id: `fl-edge-${crypto.randomUUID()}`,
      source: candidate.sourceNodeId,
      sourceHandle: candidate.sourceHandle,
      target: candidate.targetNodeId,
      targetHandle: candidate.targetHandle,
      type: 'fragmentLink',
      data: { kind: 'fragmentLink' },
    },
    link: {
      sourceSnapshotNodeInstanceId: candidate.sourceNodeId,
      sourcePortName: candidate.sourceHandle ?? 'value',
      toNodeInstanceId: candidate.targetNodeId,
      toPortName: candidate.targetHandle ?? '',
    },
  }
}
