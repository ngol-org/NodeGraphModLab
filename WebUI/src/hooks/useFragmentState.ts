import { useState, useMemo } from 'react'
import type { Node, Edge } from '@xyflow/react'
import { computeFragments, getFragmentIdForNode } from '../lib/fragmentUtils'
import type { FragmentLink } from '../types/protocol'

export function useFragmentState(
  rfNodes: Node[],
  rfEdges: Edge[],
  savedSnapshots: Map<string, unknown>,
  pinnedNodes: Set<string>,
) {
  const [fragmentLinks, setFragmentLinks] = useState<FragmentLink[]>([])
  const [pinnedFragmentIds, setPinnedFragmentIds] = useState<Set<string>>(new Set())
  const [hoveredFragmentId, setHoveredFragmentId] = useState<string | null>(null)

  const fragments = useMemo(() => computeFragments(rfNodes, rfEdges), [rfNodes, rfEdges])

  const coExecutedFragmentIds = useMemo(() => {
    if (!hoveredFragmentId) return new Set<string>()
    const coExecIds = new Set<string>()
    const toProcess = [hoveredFragmentId]
    const visited = new Set<string>()
    while (toProcess.length > 0) {
      const fragId = toProcess.pop()!
      if (visited.has(fragId)) continue
      visited.add(fragId)
      const frag = fragments.find(f => f.id === fragId)
      if (!frag) continue
      for (const edge of rfEdges) {
        const kind = (edge.data as { kind?: string } | undefined)?.kind
        if (kind !== 'fragmentLink') continue
        if (!frag.nodeInstanceIds.includes(edge.target!)) continue
        if (savedSnapshots.has(edge.source!) || pinnedNodes.has(edge.source!)) continue
        const upstreamFragId = getFragmentIdForNode(edge.source!, fragments)
        if (upstreamFragId && upstreamFragId !== hoveredFragmentId && !coExecIds.has(upstreamFragId)) {
          coExecIds.add(upstreamFragId)
          toProcess.push(upstreamFragId)
        }
      }
    }
    return coExecIds
  }, [hoveredFragmentId, rfEdges, fragments, savedSnapshots, pinnedNodes])

  // hovered 断片への入力として使われる Snapshot ノード ID セット
  // スナップショット値があるか/ピン留めされている Snapshot ノードを収集（実行されず値だけ提供するノード）
  const snapshotInputNodeIds = useMemo(() => {
    if (!hoveredFragmentId) return new Set<string>()
    const inputIds = new Set<string>()
    const frag = fragments.find(f => f.id === hoveredFragmentId)
    if (!frag) return inputIds
    for (const edge of rfEdges) {
      const kind = (edge.data as { kind?: string } | undefined)?.kind
      if (kind !== 'fragmentLink') continue
      if (!frag.nodeInstanceIds.includes(edge.target!)) continue
      if (savedSnapshots.has(edge.source!) || pinnedNodes.has(edge.source!)) {
        inputIds.add(edge.source!)
      }
    }
    return inputIds
  }, [hoveredFragmentId, rfEdges, fragments, savedSnapshots, pinnedNodes])

  return {
    fragmentLinks,
    setFragmentLinks,
    pinnedFragmentIds,
    setPinnedFragmentIds,
    hoveredFragmentId,
    setHoveredFragmentId,
    fragments,
    coExecutedFragmentIds,
    snapshotInputNodeIds,
  }
}
