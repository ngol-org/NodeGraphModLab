import { useCallback, useRef } from 'react'
import { computeFragments } from '../lib/fragmentUtils'
import type {
  Node,
  Edge,
  Connection,
  OnConnect,
  OnConnectStart,
  OnConnectEnd,
  ReactFlowInstance,
} from '@xyflow/react'
import { isSnapshotNodeTypeId, resolveFragmentLink } from '../lib/fragmentUtils'
import { buildAutoConnectEdge, type PendingConnection } from '../lib/dragAddNode'
import type { NodeTypeInfo, NodeGraphData, FragmentLink } from '../types/protocol'

export interface UseCanvasConnectionHandlersParams {
  rfRef: React.MutableRefObject<ReactFlowInstance | null>
  rfNodes: Node[]
  setRfNodes: React.Dispatch<React.SetStateAction<Node[]>>
  rfEdges: Edge[]
  setRfEdges: React.Dispatch<React.SetStateAction<Edge[]>>
  onEdgesChangeBase: (changes: import('@xyflow/react').EdgeChange[]) => void
  setFragmentLinks: React.Dispatch<React.SetStateAction<FragmentLink[]>>
  nodeTypeMap: Map<string, NodeTypeInfo>
  pushHistory: ReturnType<typeof import('./useGraphHistory').useGraphHistory>['push']
  setAddMenuPos: (pos: { x: number; y: number; canvasX: number; canvasY: number } | null) => void
  lastCanvasClickRef: React.MutableRefObject<{ x: number; y: number }>
  loadGraphFromFile: (file: File) => void
  pendingConnectionRef: React.MutableRefObject<PendingConnection | null>
}

export function useCanvasConnectionHandlers({
  rfRef,
  rfNodes,
  setRfNodes,
  rfEdges,
  setRfEdges,
  onEdgesChangeBase,
  setFragmentLinks,
  nodeTypeMap,
  pushHistory,
  setAddMenuPos,
  lastCanvasClickRef,
  loadGraphFromFile,
  pendingConnectionRef,
}: UseCanvasConnectionHandlersParams) {
  const canvasRef = useRef<HTMLDivElement>(null)

  const onEdgesChange = useCallback((changes: import('@xyflow/react').EdgeChange[]) => {
    const removedIds = changes
      .filter(c => c.type === 'remove')
      .map(c => c.id)
    if (removedIds.length > 0) {
      setRfEdges(eds => {
        const removedEdges = eds.filter(e => removedIds.includes(e.id) && (e.data as { kind?: string } | undefined)?.kind === 'fragmentLink')
        if (removedEdges.length > 0) {
          const removedLinks = new Set(removedEdges.map(e => `${e.source}:${e.sourceHandle}:${e.target}:${e.targetHandle}`))
          setFragmentLinks(prev => prev.filter(fl =>
            !removedLinks.has(`${fl.sourceSnapshotNodeInstanceId}:${fl.sourcePortName}:${fl.toNodeInstanceId}:${fl.toPortName}`)
          ))
        }
        return eds
      })
    }
    onEdgesChangeBase(changes)
  }, [onEdgesChangeBase, setRfEdges, setFragmentLinks])

  const onConnect: OnConnect = useCallback((connection: Connection) => {
    const sourceNode = rfNodes.find(n => n.id === connection.source)
    const sourceNodeTypeId = (sourceNode?.data as { nodeTypeId?: string })?.nodeTypeId

    if (isSnapshotNodeTypeId(sourceNodeTypeId) && connection.source && connection.target) {
      const currentFrags = computeFragments(rfNodes, rfEdges)
      const resolved = resolveFragmentLink({
        sourceNodeId: connection.source,
        sourceNodeTypeId,
        sourceHandle: connection.sourceHandle,
        targetNodeId: connection.target,
        targetHandle: connection.targetHandle,
      }, currentFrags)

      if (resolved) {
        const { edge: newEdge, link: newLink } = resolved
        pushHistory({
          label: 'Connect Fragment Link',
          do: () => {
            setFragmentLinks(prev => [...prev, newLink])
            setRfEdges(prev => [...prev, newEdge])
          },
          undo: () => {
            setRfEdges(prev => prev.filter(e => e.id !== newEdge.id))
            setFragmentLinks(prev => prev.filter(fl =>
              !(fl.sourceSnapshotNodeInstanceId === newLink.sourceSnapshotNodeInstanceId &&
                fl.sourcePortName === newLink.sourcePortName &&
                fl.toNodeInstanceId === newLink.toNodeInstanceId &&
                fl.toPortName === newLink.toPortName)
            ))
          },
        })
        return
      }
    }

    const edgeId = `edge-${crypto.randomUUID()}`
    const newEdge: Edge = {
      id: edgeId,
      source: connection.source!,
      sourceHandle: connection.sourceHandle,
      target: connection.target!,
      targetHandle: connection.targetHandle,
      animated: true,
    }
    pushHistory({
      label: 'Connect',
      do: () => setRfEdges(prev => [...prev, newEdge]),
      undo: () => setRfEdges(prev => prev.filter(e => e.id !== edgeId)),
    })
  }, [rfNodes, rfEdges, setRfEdges, setFragmentLinks, pushHistory])

  const onDrop = useCallback((e: React.DragEvent<HTMLDivElement>) => {
    e.preventDefault()
    if (e.dataTransfer.files.length > 0) {
      loadGraphFromFile(e.dataTransfer.files[0])
      return
    }
    const typeId = e.dataTransfer.getData('application/node-type-id')
    if (!typeId) return
    const pos = rfRef.current?.screenToFlowPosition({ x: e.clientX, y: e.clientY })
    if (!pos) return
    const nodeInfo = nodeTypeMap.get(typeId)
    const newNode: Node = {
      id: crypto.randomUUID(),
      type: 'custom',
      position: pos,
      data: {
        label: nodeInfo?.displayName ?? typeId,
        nodeTypeId: typeId,
        nodeTypeVersion: nodeInfo?.nodeVersion ?? '1.0.0',
        nodeTypeInfo: nodeInfo,
        paramValues: {},
      },
    }
    pushHistory({
      label: 'Add Node',
      do: () => setRfNodes(prev => [...prev, newNode]),
      undo: () => setRfNodes(prev => prev.filter(n => n.id !== newNode.id)),
    })
  }, [nodeTypeMap, setRfNodes, pushHistory, loadGraphFromFile, rfRef])

  const onDragOver = useCallback((e: React.DragEvent) => {
    e.preventDefault()
    e.dataTransfer.dropEffect = 'copy'
  }, [])

  const addNodeAtCanvasPos = useCallback((typeId: string, canvasX: number, canvasY: number) => {
    const nodeInfo = nodeTypeMap.get(typeId)
    const pendingConnection = pendingConnectionRef.current
    const newNode: Node = {
      id: crypto.randomUUID(),
      type: 'custom',
      position: { x: canvasX, y: canvasY },
      data: {
        label: nodeInfo?.displayName ?? typeId,
        nodeTypeId: typeId,
        nodeTypeVersion: nodeInfo?.nodeVersion ?? '1.0.0',
        nodeTypeInfo: nodeInfo,
        paramValues: {},
      },
    }
    const autoEdge = buildAutoConnectEdge(pendingConnection, newNode.id, nodeInfo)

    let fragmentResolved: { edge: Edge; link: FragmentLink } | null = null
    if (autoEdge && pendingConnection) {
      const pendingNode = rfNodes.find(n => n.id === pendingConnection.nodeId)
      const pendingNodeTypeId = (pendingNode?.data as { nodeTypeId?: string })?.nodeTypeId
      const candidate = pendingConnection.handleType === 'source'
        ? {
            sourceNodeId: pendingConnection.nodeId,
            sourceNodeTypeId: pendingNodeTypeId,
            sourceHandle: autoEdge.sourceHandle,
            targetNodeId: newNode.id,
            targetHandle: autoEdge.targetHandle,
          }
        : {
            sourceNodeId: newNode.id,
            sourceNodeTypeId: typeId,
            sourceHandle: autoEdge.sourceHandle,
            targetNodeId: pendingConnection.nodeId,
            targetHandle: autoEdge.targetHandle,
          }
      fragmentResolved = resolveFragmentLink(candidate, computeFragments(rfNodes, rfEdges))
    }

    const newEdge = fragmentResolved?.edge ?? autoEdge
    const newLink = fragmentResolved?.link ?? null

    pushHistory({
      label: 'Add Node',
      do: () => {
        setRfNodes(prev => [...prev, newNode])
        if (newEdge) setRfEdges(prev => [...prev, newEdge])
        if (newLink) setFragmentLinks(prev => [...prev, newLink])
      },
      undo: () => {
        setRfNodes(prev => prev.filter(n => n.id !== newNode.id))
        if (newEdge) setRfEdges(prev => prev.filter(e => e.id !== newEdge.id))
        if (newLink) {
          setFragmentLinks(prev => prev.filter(fl =>
            !(fl.sourceSnapshotNodeInstanceId === newLink.sourceSnapshotNodeInstanceId &&
              fl.sourcePortName === newLink.sourcePortName &&
              fl.toNodeInstanceId === newLink.toNodeInstanceId &&
              fl.toPortName === newLink.toPortName)
          ))
        }
      },
    })
    pendingConnectionRef.current = null
    setAddMenuPos(null)
  }, [nodeTypeMap, rfNodes, rfEdges, setRfNodes, setRfEdges, setFragmentLinks, pushHistory, setAddMenuPos, pendingConnectionRef])

  const handleConnectStart: OnConnectStart = useCallback((_event, params) => {
    if ((params.handleType !== 'source' && params.handleType !== 'target') || !params.nodeId || !params.handleId) {
      pendingConnectionRef.current = null
      return
    }
    pendingConnectionRef.current = {
      nodeId: params.nodeId,
      handleId: params.handleId,
      handleType: params.handleType,
    }
  }, [pendingConnectionRef])

  const handleConnectEnd: OnConnectEnd = useCallback((event, connectionState) => {
    const pendingConnection = pendingConnectionRef.current
    if (!pendingConnection) return
    if (connectionState.toHandle) {
      pendingConnectionRef.current = null
      return
    }
    const clientX = 'changedTouches' in event ? event.changedTouches[0]?.clientX : event.clientX
    const clientY = 'changedTouches' in event ? event.changedTouches[0]?.clientY : event.clientY
    if (typeof clientX !== 'number' || typeof clientY !== 'number') {
      pendingConnectionRef.current = null
      return
    }
    const eventTarget = event.target
    if (!(eventTarget instanceof globalThis.Node) || !canvasRef.current?.contains(eventTarget)) {
      pendingConnectionRef.current = null
      return
    }
    const flowPos = rfRef.current?.screenToFlowPosition({ x: clientX, y: clientY })
    if (!flowPos) {
      pendingConnectionRef.current = null
      return
    }
    setAddMenuPos({ x: clientX, y: clientY, canvasX: flowPos.x, canvasY: flowPos.y })
  }, [setAddMenuPos, pendingConnectionRef, rfRef])

  const handleImportAsFragment = useCallback((importedGraph: NodeGraphData) => {
    const idMap = new Map<string, string>()
    const minX = importedGraph.nodes.length > 0 ? Math.min(...importedGraph.nodes.map(n => n.position.x)) : 0
    const minY = importedGraph.nodes.length > 0 ? Math.min(...importedGraph.nodes.map(n => n.position.y)) : 0
    const baseX = lastCanvasClickRef.current.x
    const baseY = lastCanvasClickRef.current.y
    const newRfNodes = importedGraph.nodes.map(ni => {
      const newId = crypto.randomUUID()
      idMap.set(ni.instanceId, newId)
      const typeInfo = nodeTypeMap.get(ni.nodeTypeId)
      return {
        id: newId,
        type: 'custom' as const,
        position: { x: baseX + (ni.position.x - minX), y: baseY + (ni.position.y - minY) },
        data: {
          label: typeInfo?.displayName ?? ni.nodeTypeId,
          nodeTypeId: ni.nodeTypeId,
          ...(ni.nodeTypeVersion ? { nodeTypeVersion: ni.nodeTypeVersion } : {}),
          nodeTypeInfo: typeInfo,
          paramValues: ni.paramValues ?? {},
          ...(ni.size ? { size: ni.size } : {}),
        },
      }
    })
    const newRfEdges = importedGraph.connections.map((c, i) => ({
      id: `imp-${i}-${crypto.randomUUID()}`,
      source: idMap.get(c.fromNodeInstanceId) ?? c.fromNodeInstanceId,
      sourceHandle: c.fromPortName,
      target: idMap.get(c.toNodeInstanceId) ?? c.toNodeInstanceId,
      targetHandle: c.toPortName,
      animated: true,
    }))
    const newFragmentLinks: FragmentLink[] = (importedGraph.fragmentLinks ?? []).map(fl => ({
      sourceSnapshotNodeInstanceId: idMap.get(fl.sourceSnapshotNodeInstanceId) ?? fl.sourceSnapshotNodeInstanceId,
      sourcePortName: fl.sourcePortName,
      toNodeInstanceId: idMap.get(fl.toNodeInstanceId) ?? fl.toNodeInstanceId,
      toPortName: fl.toPortName,
    }))
    const newFragmentLinkEdges: Edge[] = newFragmentLinks.map((fl, i) => ({
      id: `imp-fl-${i}-${crypto.randomUUID()}`,
      source: fl.sourceSnapshotNodeInstanceId,
      sourceHandle: fl.sourcePortName,
      target: fl.toNodeInstanceId,
      targetHandle: fl.toPortName,
      type: 'fragmentLink',
      data: { kind: 'fragmentLink' },
    }))
    const allNewEdges = [...newRfEdges, ...newFragmentLinkEdges]
    const newNodeIds = new Set<string>(newRfNodes.map(n => n.id))
    const newEdgeIds = new Set<string>(allNewEdges.map(e => e.id))
    pushHistory({
      label: 'Import Fragment',
      do: () => {
        setRfNodes(prev => [...prev, ...newRfNodes])
        setRfEdges(prev => [...prev, ...allNewEdges])
        if (newFragmentLinks.length > 0) setFragmentLinks(prev => [...prev, ...newFragmentLinks])
      },
      undo: () => {
        setRfNodes(prev => prev.filter(n => !newNodeIds.has(n.id)))
        setRfEdges(prev => prev.filter(e => !newEdgeIds.has(e.id)))
        if (newFragmentLinks.length > 0) {
          const newLinkKeys = new Set(newFragmentLinks.map(l =>
            `${l.sourceSnapshotNodeInstanceId}:${l.sourcePortName}:${l.toNodeInstanceId}:${l.toPortName}`
          ))
          setFragmentLinks(prev => prev.filter(fl =>
            !newLinkKeys.has(`${fl.sourceSnapshotNodeInstanceId}:${fl.sourcePortName}:${fl.toNodeInstanceId}:${fl.toPortName}`)
          ))
        }
      },
    })
  }, [nodeTypeMap, setRfNodes, setRfEdges, setFragmentLinks, pushHistory, lastCanvasClickRef])

  return {
    canvasRef,
    onConnect,
    onEdgesChange,
    onDrop,
    onDragOver,
    addNodeAtCanvasPos,
    handleConnectStart,
    handleConnectEnd,
    handleImportAsFragment,
  }
}
