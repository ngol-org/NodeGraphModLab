import { useRef } from 'react'
import type {
  Node,
  Edge,
  ReactFlowInstance,
  NodeChange,
} from '@xyflow/react'
import type { PendingConnection } from '../lib/dragAddNode'
import type { FragmentDefinition, NodeGroup, NodeTypeInfo, NodeGraphData, FragmentLink } from '../types/protocol'
import { useGraphPersistenceHandlers } from './useGraphPersistenceHandlers'
import { useCanvasConnectionHandlers } from './useCanvasConnectionHandlers'
import { useNodeParamEditHandlers } from './useNodeParamEditHandlers'
import { useCanvasSelectionDragHandlers } from './useCanvasSelectionDragHandlers'

// このファイルは 4 つの関心別 hook（永続化・接続・パラメータ編集・選択/ドラッグ）を
// compose するコンポーザのみを担う。新しい状態・副作用・イベントハンドラをここに追加しないこと。
// 該当する関心の hook（useGraphPersistenceHandlers / useCanvasConnectionHandlers /
// useNodeParamEditHandlers / useCanvasSelectionDragHandlers）に追加するか、
// どれにも該当しない新しい関心であれば新規 hook ファイルを追加する。
// 詳細: docs/developer-guide.md §3.4

interface UseGraphCanvasHandlersParams {
  rfRef: React.MutableRefObject<ReactFlowInstance | null>
  rfNodes: Node[]
  setRfNodes: React.Dispatch<React.SetStateAction<Node[]>>
  rfEdges: Edge[]
  setRfEdges: React.Dispatch<React.SetStateAction<Edge[]>>
  onNodesChange: (changes: NodeChange[]) => void
  onEdgesChangeBase: (changes: import('@xyflow/react').EdgeChange[]) => void
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
  createGroup: (name: string, nodeIds: string[]) => void
  deleteGroup: (id: string) => void
  renameGroup: (id: string, newName: string) => void
  updateGroupDescription: (id: string, description: string) => void
  toggleCollapsed: (id: string) => void
  addNodesToGroup: (groupId: string, nodeIds: string[]) => void
  removeNodeFromGroup: (groupId: string, nodeId: string) => void
  resetGroups: (groups: NodeGroup[]) => void
  pushHistory: ReturnType<typeof import('./useGraphHistory').useGraphHistory>['push']
  undo: () => void
  redo: () => void
  recordHistory: ReturnType<typeof import('./useGraphHistory').useGraphHistory>['record']
  clearHistory: () => void
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
  execute: (graph: NodeGraphData) => void
  save: (graph: NodeGraphData) => void
  addLog: ReturnType<typeof import('./useGraphEditor').useExecutionLogs>['addLog']
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

export function useGraphCanvasHandlers(params: UseGraphCanvasHandlersParams) {
  const {
    rfRef,
    rfNodes,
    setRfNodes,
    rfEdges,
    setRfEdges,
    onNodesChange,
    onEdgesChangeBase,
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
    createGroup,
    deleteGroup,
    renameGroup,
    toggleCollapsed,
    addNodesToGroup,
    removeNodeFromGroup,
    resetGroups,
    pushHistory,
    undo,
    redo,
    recordHistory,
    clearHistory,
    isSpacePressed,
    isDragSelecting,
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
  } = params

  // 接続保留状態（onConnectStart/End・addNodeAtCanvasPos・onPaneClick・A キー）で共有される ref。
  const pendingConnectionRef = useRef<PendingConnection | null>(null)

  const persistence = useGraphPersistenceHandlers({
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
  })

  const connection = useCanvasConnectionHandlers({
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
    loadGraphFromFile: persistence.loadGraphFromFile,
    pendingConnectionRef,
  })

  const paramEdit = useNodeParamEditHandlers({
    rfRef,
    selectedNodeId,
    setRfNodes,
    pushHistory,
    recordHistory,
  })

  const selectionDrag = useCanvasSelectionDragHandlers({
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
    isDragSelecting,
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
    handleSave: persistence.handleSave,
    openSaveAs: persistence.openSaveAs,
  })

  return {
    canvasRef: connection.canvasRef,
    graphFileInputRef: persistence.graphFileInputRef,
    pendingConnectionRef,
    groupsRef: selectionDrag.groupsRef,
    groupContextMenu: selectionDrag.groupContextMenu,
    setGroupContextMenu: selectionDrag.setGroupContextMenu,
    groupDragPositions: selectionDrag.groupDragPositions,
    stableGroupToggle: selectionDrag.stableGroupToggle,
    stableGroupRename: selectionDrag.stableGroupRename,
    stableGroupDissolve: selectionDrag.stableGroupDissolve,
    onConnect: connection.onConnect,
    onEdgesChange: connection.onEdgesChange,
    onDrop: connection.onDrop,
    onDragOver: connection.onDragOver,
    buildGraphData: persistence.buildGraphData,
    handleExecute: persistence.handleExecute,
    handleSave: persistence.handleSave,
    handleStop: persistence.handleStop,
    handleSaveAsConfirm: persistence.handleSaveAsConfirm,
    handleClearCanvas: persistence.handleClearCanvas,
    handleClearCanvasConfirm: persistence.handleClearCanvasConfirm,
    exportNodeTypeIds: persistence.exportNodeTypeIds,
    handleExportNodes: persistence.handleExportNodes,
    handleExportConfirm: persistence.handleExportConfirm,
    loadGraphFromFile: persistence.loadGraphFromFile,
    addNodeAtCanvasPos: connection.addNodeAtCanvasPos,
    handleConnectStart: connection.handleConnectStart,
    handleConnectEnd: connection.handleConnectEnd,
    handleDeleteNode: selectionDrag.handleDeleteNode,
    handleLoadGraph: persistence.handleLoadGraph,
    handleImportAsFragment: connection.handleImportAsFragment,
    onSelectionStart: selectionDrag.onSelectionStart,
    onSelectionEnd: selectionDrag.onSelectionEnd,
    onNodeClick: selectionDrag.onNodeClick,
    onPaneClick: selectionDrag.onPaneClick,
    handleDeleteSelected: selectionDrag.handleDeleteSelected,
    handleNodesChange: selectionDrag.handleNodesChange,
    handleNodeDrag: selectionDrag.handleNodeDrag,
    handleCreateGroup: selectionDrag.handleCreateGroup,
    handleNodeDragStart: selectionDrag.handleNodeDragStart,
    handleNodeDragStop: selectionDrag.handleNodeDragStop,
    handleParamEditStart: paramEdit.handleParamEditStart,
    handleParamEditEnd: paramEdit.handleParamEditEnd,
    handleParamChange: paramEdit.handleParamChange,
    handleExecuteFragment: persistence.handleExecuteFragment,
    handleExecuteAllFragments: persistence.handleExecuteAllFragments,
    selectedNode: persistence.selectedNode,
    selectedNodeTypeInfo: persistence.selectedNodeTypeInfo,
    executeFragmentContextValue: persistence.executeFragmentContextValue,
    addNodesToGroup,
    removeNodeFromGroup,
  }
}
