import { useCallback, useMemo, useRef } from 'react'
import type { Node, Edge, ReactFlowInstance } from '@xyflow/react'
import { getFragmentIdForNode } from '../lib/fragmentUtils'
import { wsClient } from '../lib/wsClient'
import type { AnnotationNodeData } from '../components/AnnotationNode'
import type { FragmentDefinition, NodeGroup, NodeTypeInfo, NodeGraphData, NodeInstance, NodeConnection, FragmentLink } from '../types/protocol'
import { CURRENT_SCHEMA_VERSION } from '../types/protocol'
import type { useExecutionLogs } from './useGraphEditor'

export interface UseGraphPersistenceHandlersParams {
  rfRef: React.MutableRefObject<ReactFlowInstance | null>
  rfNodes: Node[]
  setRfNodes: React.Dispatch<React.SetStateAction<Node[]>>
  rfEdges: Edge[]
  setRfEdges: React.Dispatch<React.SetStateAction<Edge[]>>
  graphId: string
  graphName: string
  setGraphId: (id: string) => void
  setGraphName: (name: string) => void
  selectedNodeId: string | null
  setSelectedNodeId: (id: string | null) => void
  nodeTypeMap: Map<string, NodeTypeInfo>
  fragmentLinks: FragmentLink[]
  setFragmentLinks: React.Dispatch<React.SetStateAction<FragmentLink[]>>
  fragments: FragmentDefinition[]
  pinnedFragmentIds: Set<string>
  groups: NodeGroup[]
  resetGroups: (groups: NodeGroup[]) => void
  clearHistory: () => void
  multiSelectedIds: Set<string>
  execute: (graph: NodeGraphData) => void
  save: (graph: NodeGraphData) => void
  addLog: ReturnType<typeof useExecutionLogs>['addLog']
  hoveredFragmentId: string | null
  setHoveredFragmentId: (id: string | null) => void
  setSaveAsDialogOpen: (open: boolean) => void
  saveAsName: string
  setSaveAsName: (name: string) => void
  setExportDialogOpen: (open: boolean) => void
  exportDllName: string
  setExportDllName: (name: string) => void
  exportOutputDir: string
  setExportOutputDir: (dir: string) => void
  setExportResult: (result: { success: boolean; message: string } | null) => void
  setClearCanvasDialogOpen: (open: boolean) => void
}

export function useGraphPersistenceHandlers({
  rfRef,
  rfNodes,
  setRfNodes,
  rfEdges,
  setRfEdges,
  graphId,
  graphName,
  setGraphId,
  setGraphName,
  selectedNodeId,
  setSelectedNodeId,
  nodeTypeMap,
  fragmentLinks,
  setFragmentLinks,
  fragments,
  pinnedFragmentIds,
  groups,
  resetGroups,
  clearHistory,
  multiSelectedIds,
  execute,
  save,
  addLog,
  hoveredFragmentId,
  setHoveredFragmentId,
  setSaveAsDialogOpen,
  saveAsName,
  setSaveAsName,
  setExportDialogOpen,
  exportDllName,
  setExportDllName,
  exportOutputDir,
  setExportOutputDir,
  setExportResult,
  setClearCanvasDialogOpen,
}: UseGraphPersistenceHandlersParams) {
  const graphFileInputRef = useRef<HTMLInputElement>(null)
  const handleLoadGraphRef = useRef<((graph: NodeGraphData) => void) | null>(null)

  const buildGraphData = useCallback((): NodeGraphData => {
    const allRfNodes = rfRef.current?.getNodes() ?? rfNodes
    const nodes: NodeInstance[] = allRfNodes
      .filter(n => n.type !== 'annotation' && n.type !== 'nodeGroup')
      .map(n => {
        const data = n.data as {
          size?: { width: number; height: number }
          nodeTypeVersion?: string
        }
        const size = data.size
        return {
          instanceId: n.id,
          nodeTypeId: (n.data as { nodeTypeId: string }).nodeTypeId,
          ...(data.nodeTypeVersion ? { nodeTypeVersion: data.nodeTypeVersion } : {}),
          position: { x: n.position.x, y: n.position.y },
          paramValues: (n.data as { paramValues: Record<string, unknown> }).paramValues ?? {},
          ...(size ? { size } : {}),
        }
      })
    const normalEdges = rfEdges.filter(e =>
      e.source && e.target && e.sourceHandle && e.targetHandle &&
      (e.data as { kind?: string } | undefined)?.kind !== 'fragmentLink'
    )
    const connections: NodeConnection[] = normalEdges.map(e => ({
      fromNodeInstanceId: e.source,
      fromPortName: e.sourceHandle!,
      toNodeInstanceId: e.target,
      toPortName: e.targetHandle!,
    }))
    const annotationsData = allRfNodes
      .filter(n => n.type === 'annotation')
      .map(n => {
        const d = n.data as AnnotationNodeData
        return {
          id: d.annotationId,
          text: d.text,
          position: n.position,
          width: d.width ?? 200,
          height: d.height ?? 100,
          color: d.color,
        }
      })
    return {
      id: graphId,
      name: graphName,
      description: '',
      schemaVersion: CURRENT_SCHEMA_VERSION,
      version: 1,
      createdAt: new Date().toISOString(),
      nodes,
      connections,
      fragments,
      fragmentLinks,
      groups,
      annotations: annotationsData,
    }
  }, [rfNodes, rfEdges, graphId, graphName, fragments, fragmentLinks, groups])

  const handleLoadGraph = useCallback((graph: NodeGraphData) => {
    setGraphId(graph.id)
    setGraphName(graph.name)
    setSelectedNodeId(null)

    const loadedNodes: Node[] = graph.nodes.map(ni => {
      const typeInfo = nodeTypeMap.get(ni.nodeTypeId)
      return {
        id: ni.instanceId,
        type: 'custom',
        position: { x: ni.position.x, y: ni.position.y },
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

    const loadedEdges: Edge[] = graph.connections.map((c, i) => ({
      id: `loaded-edge-${i}-${c.fromNodeInstanceId}-${c.fromPortName}`,
      source: c.fromNodeInstanceId,
      sourceHandle: c.fromPortName,
      target: c.toNodeInstanceId,
      targetHandle: c.toPortName,
      animated: true,
    }))

    const fragmentLinkEdges: Edge[] = (graph.fragmentLinks ?? []).map((fl, i) => ({
      id: `fl-edge-${i}-${fl.sourceSnapshotNodeInstanceId}-${fl.sourcePortName}`,
      source: fl.sourceSnapshotNodeInstanceId,
      sourceHandle: fl.sourcePortName,
      target: fl.toNodeInstanceId,
      targetHandle: fl.toPortName,
      type: 'fragmentLink',
      data: { kind: 'fragmentLink' },
    }))

    const annotationNodes: Node[] = (graph.annotations ?? []).map(a => ({
      id: `annot-${a.id}`,
      type: 'annotation',
      position: a.position,
      data: {
        annotationId: a.id,
        text: a.text,
        color: a.color ?? '#fffde7',
        width: a.width ?? 200,
        height: a.height ?? 100,
        onTextChange: () => {},
        onDelete: () => {},
      } as unknown as Record<string, unknown>,
      draggable: true,
      selectable: true,
    }))

    setRfNodes([...loadedNodes, ...annotationNodes])
    setRfEdges([...loadedEdges, ...fragmentLinkEdges])
    setFragmentLinks(graph.fragmentLinks ?? [])
    if (!graph.schemaVersion) {
      console.info('[NodeGraph] Loading legacy graph (no schemaVersion). groups will be empty.')
    } else if (graph.schemaVersion !== CURRENT_SCHEMA_VERSION) {
      console.warn(`[NodeGraph] schemaVersion mismatch: file=${graph.schemaVersion}, current=${CURRENT_SCHEMA_VERSION}`)
    }
    resetGroups(graph.groups ?? [])
    const versionWarnings = graph.nodes
      .map(ni => {
        const saved = ni.nodeTypeVersion
        if (!saved) return null
        const current = nodeTypeMap.get(ni.nodeTypeId)?.nodeVersion ?? '1.0.0'
        if (saved === current) return null
        return `${ni.nodeTypeId} saved=${saved}, current=${current}`
      })
      .filter((x): x is string => x !== null)
    if (versionWarnings.length > 0) {
      addLog({
        timestampMs: Date.now(),
        level: 'warning',
        category: 'notify',
        message: `Node version mismatch detected (${versionWarnings.length}). ${versionWarnings.join(' | ')}`,
      })
    }
    clearHistory()
  }, [nodeTypeMap, setRfNodes, setRfEdges, clearHistory, resetGroups, setGraphId, setGraphName, setSelectedNodeId, setFragmentLinks, addLog])

  handleLoadGraphRef.current = handleLoadGraph

  const loadGraphFromFile = useCallback((file: File) => {
    if (!file.name.endsWith('.json')) return
    const reader = new FileReader()
    reader.onload = (ev) => {
      try {
        const json = JSON.parse(ev.target?.result as string)
        if (json && typeof json.id === 'string' && Array.isArray(json.nodes) && Array.isArray(json.connections)) {
          handleLoadGraphRef.current?.(json as NodeGraphData)
          addLog({ timestampMs: Date.now(), level: 'info', message: `Graph loaded from file: ${file.name}`, category: 'notify' })
        } else {
          addLog({ timestampMs: Date.now(), level: 'error', message: `Invalid graph file: ${file.name}`, category: 'notify' })
        }
      } catch {
        addLog({ timestampMs: Date.now(), level: 'error', message: `Failed to parse graph file: ${file.name}`, category: 'notify' })
      }
    }
    reader.readAsText(file)
  }, [addLog])

  const handleExecute = useCallback(() => {
    const graphData = buildGraphData()
    const pinnedArr = Array.from(pinnedFragmentIds)
    if (graphData.fragments.length <= 1) {
      execute(graphData)
    } else {
      wsClient.executeAllFragments(graphData, pinnedArr)
    }
  }, [buildGraphData, execute, pinnedFragmentIds])

  const handleSave = () => save(buildGraphData())
  const handleStop = () => wsClient.stopGraph()

  const openSaveAs = useCallback(() => {
    setSaveAsName(graphName)
    setSaveAsDialogOpen(true)
  }, [graphName, setSaveAsName, setSaveAsDialogOpen])

  const handleSaveAsConfirm = () => {
    const newName = saveAsName.trim()
    if (!newName) return
    const newId = crypto.randomUUID()
    const graphData = buildGraphData()
    save({ ...graphData, name: newName, id: newId })
    setGraphName(newName)
    setGraphId(newId)
    setSaveAsDialogOpen(false)
  }

  const handleClearCanvas = () => setClearCanvasDialogOpen(true)

  const handleClearCanvasConfirm = useCallback(() => {
    const emptyGraph: NodeGraphData = {
      id: crypto.randomUUID(),
      name: 'New Graph',
      description: '',
      schemaVersion: CURRENT_SCHEMA_VERSION,
      version: 1,
      createdAt: new Date().toISOString(),
      nodes: [],
      connections: [],
      fragments: [],
      fragmentLinks: [],
      groups: [],
      annotations: [],
    }
    handleLoadGraphRef.current?.(emptyGraph)
    setClearCanvasDialogOpen(false)
  }, [setClearCanvasDialogOpen])

  const exportNodeTypeIds = [...new Set(
    rfNodes
      .filter(n => multiSelectedIds.has(n.id))
      .map(n => (n.data as { nodeTypeId: string }).nodeTypeId)
  )]

  const handleExportNodes = () => {
    setExportDllName('MyNodePack')
    setExportOutputDir('Nodes/CustomNodes/dll')
    setExportResult(null)
    setExportDialogOpen(true)
  }

  const handleExportConfirm = () => {
    const dllName = exportDllName.trim()
    if (!dllName || exportNodeTypeIds.length === 0) return
    wsClient.exportNodesAsDll(dllName, exportOutputDir.trim() || 'Nodes/CustomNodes/dll', exportNodeTypeIds)
    setExportResult(null)
  }

  const handleExecuteFragment = useCallback((fragmentId: string, pinnedIds: string[]) => {
    wsClient.executeFragment(buildGraphData(), fragmentId, pinnedIds)
  }, [buildGraphData])

  const handleExecuteFragmentForNode = useCallback((fragmentId: string) => {
    handleExecuteFragment(fragmentId, Array.from(pinnedFragmentIds))
  }, [handleExecuteFragment, pinnedFragmentIds])

  const handleExecuteAllFragments = useCallback((pinnedIds: string[]) => {
    wsClient.executeAllFragments(buildGraphData(), pinnedIds)
  }, [buildGraphData])

  const selectSingleNode = useCallback((nodeId: string) => {
    setSelectedNodeId(nodeId)
    setRfNodes(prev => prev.map(n => ({ ...n, selected: n.id === nodeId })))
  }, [setSelectedNodeId, setRfNodes])

  const executeFragmentContextValue = useMemo(() => ({
    executeFragmentForNode: (nodeId: string) => {
      const fragId = getFragmentIdForNode(nodeId, fragments)
      if (fragId) handleExecuteFragmentForNode(fragId)
    },
    hoveredFragmentId,
    setHoveredFragmentId,
    selectNode: selectSingleNode,
  }), [fragments, handleExecuteFragmentForNode, hoveredFragmentId, setHoveredFragmentId, selectSingleNode])

  const selectedNode = selectedNodeId
    ? rfNodes.find(n => n.id === selectedNodeId) ?? null
    : null
  const selectedNodeTypeInfo = selectedNode
    ? nodeTypeMap.get((selectedNode.data as { nodeTypeId: string }).nodeTypeId) ?? null
    : null

  return {
    graphFileInputRef,
    buildGraphData,
    handleLoadGraph,
    loadGraphFromFile,
    handleExecute,
    handleSave,
    handleStop,
    openSaveAs,
    handleSaveAsConfirm,
    handleClearCanvas,
    handleClearCanvasConfirm,
    exportNodeTypeIds,
    handleExportNodes,
    handleExportConfirm,
    handleExecuteFragment,
    handleExecuteAllFragments,
    selectSingleNode,
    executeFragmentContextValue,
    selectedNode,
    selectedNodeTypeInfo,
  }
}
