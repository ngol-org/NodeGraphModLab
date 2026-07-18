/**
 * GraphEditorLayout — ReactFlow editor shell.
 * Do NOT add logic here; see docs/developer-guide.md §3.4 / §5 / §6.
 * New state/handlers → hooks/ or components/; pure helpers → lib/.
 */
import { useState, useMemo, useRef, useEffect, useSyncExternalStore } from 'react'
import {
  ReactFlow,
  Background,
  Controls,
  MiniMap,
  Panel,
  useNodesState,
  useEdgesState,
  type Node,
  type Edge,
  type ReactFlowInstance,
} from '@xyflow/react'
import '@xyflow/react/dist/style.css'

import { useConnection, useNodeTypes, useExecutionLogs, useGraphExecution, useGraphSave, useExecutionProgress, useSavedSnapshots, usePinnedSnapshots, useScriptCompileError, useSnapshotHistory, usePersistentNodes, useGraphList } from '../hooks/useGraphEditor'
import { useGraphHistory } from '../hooks/useGraphHistory'
import { useFragmentState } from '../hooks/useFragmentState'
import { useGroupState } from '../hooks/useGroupState'
import { useMenuState } from '../hooks/useMenuState'
import { useLayoutPanelState } from '../hooks/useLayoutPanelState'
import { useCanvasSelectionState } from '../hooks/useCanvasSelectionState'
import { useEditorDialogs } from '../hooks/useEditorDialogs'
import { useGraphEditorSync } from '../hooks/useGraphEditorSync'
import { useCanvasDisplayNodes } from '../hooks/useCanvasDisplayNodes'
import { useGraphCanvasHandlers } from '../hooks/useGraphCanvasHandlers'
import { useAnnotationRfCallbacks } from '../hooks/useAnnotationState'
import { wsClient } from '../lib/wsClient'
import { getFragmentIdForNode } from '../lib/fragmentUtils'
import { MenuBar } from './MenuBar'
import { NodePalette } from './NodePalette'
import { ExecutionLogPanel } from './ExecutionLogPanel'
import { LogPanelResizeHandle } from './LogPanelResizeHandle'
import { NodeInspector } from './NodeInspector'
import { CustomNode } from './CustomNode'
import { GroupNode } from './GroupNode'
import { AnnotationNode } from './AnnotationNode'
import { GraphListPanel } from './GraphListPanel'
import { NodeAddMenu } from './NodeAddMenu'
import { NodeContextMenu } from './NodeContextMenu'
import { SnapshotHistoryPopup } from './SnapshotHistoryPopup'
import { SnapshotStorePanel } from './SnapshotStorePanel'
import { FragmentLinkEdge } from './FragmentLinkEdge'
import { FragmentPanel } from './FragmentPanel'
import { PersistentCollapsedIndicator } from './PersistentCollapsedIndicator'
import { LeftColumnCollapsedIndicators } from './LeftColumnCollapsedIndicators'
import { FragmentImportMenu } from './FragmentImportMenu'
import { EditorDialogHost } from './EditorDialogHost'
import { GroupContextMenu } from './GroupContextMenu'
import { ExecuteFragmentContext } from '../contexts/ExecuteFragmentContext'
import { NgolIcon } from './icons/NgolIcon'
import { getPluginPanels } from '../webuiPlugin/pluginPanelRegistry'
import { PluginPanelHost } from '../webuiPlugin/PluginPanelHost'
import { subscribeExtensions, getExtensionSnapshot } from '../webuiPlugin/pluginExtensionRegistry'
import { PluginErrorBoundary } from '../webuiPlugin/PluginErrorBoundary'
import '../App.css'

const APP_VERSION = 'v0.7.14'
const RF_NODE_TYPES = { custom: CustomNode, nodeGroup: GroupNode, annotation: AnnotationNode } as const
const RF_EDGE_TYPES = { fragmentLink: FragmentLinkEdge } as const

interface GraphEditorLayoutProps {
  initialGraphName?: string
}

export function GraphEditorLayout({ initialGraphName }: GraphEditorLayoutProps) {
  const connected = useConnection()
  const nodeTypes = useNodeTypes()
  const { logs, clear: clearLogs, addLog } = useExecutionLogs()
  const { executing, execute } = useGraphExecution()
  const { saving, lastSaved, save } = useGraphSave()
  const { nodeStatus } = useExecutionProgress()
  const { savedSnapshots, savedSnapshotsByPort, justSavedNodes, setSavedSnapshots, setSavedSnapshotsByPort } = useSavedSnapshots()
  const { pinnedNodes, togglePin } = usePinnedSnapshots()
  const persistentNodes = usePersistentNodes()
  const { compileError, dismiss: dismissCompileError } = useScriptCompileError()
  const { historyState, requestHistory, restoreSnapshot, clearSnapshot, closeHistory } = useSnapshotHistory(savedSnapshots, setSavedSnapshots, setSavedSnapshotsByPort)

  const { graphs } = useGraphList()

  const [graphName, setGraphName] = useState('New Graph')
  const [graphId, setGraphId] = useState<string>(() => crypto.randomUUID())
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null)
  const [rfNodes, setRfNodes, onNodesChange] = useNodesState<Node>([])
  const [rfEdges, setRfEdges, onEdgesChangeBase] = useEdgesState<Edge>([])
  const rfRef = useRef<ReactFlowInstance | null>(null)

  const layout = useLayoutPanelState()
  const dialogs = useEditorDialogs()
  const selection = useCanvasSelectionState(selectedNodeId)

  const {
    addMenuPos, setAddMenuPos,
    nodeContextMenu, setNodeContextMenu,
    paneContextMenuPos, setPaneContextMenuPos,
    paneClearSubmenuOpen, setPaneClearSubmenuOpen,
    fragmentImportMenuPos, setFragmentImportMenuPos,
    lastCanvasClickRef,
    handleNodeContextMenu,
    handlePaneContextMenu,
    closeAllMenus,
  } = useMenuState(rfRef)

  const {
    fragmentLinks, setFragmentLinks,
    pinnedFragmentIds, setPinnedFragmentIds,
    hoveredFragmentId, setHoveredFragmentId,
    fragments,
    coExecutedFragmentIds,
    snapshotInputNodeIds,
  } = useFragmentState(rfNodes, rfEdges, savedSnapshots, pinnedNodes)

  const { push: pushHistory, undo, redo, clear: clearHistory, record: recordHistory, canUndo, canRedo } = useGraphHistory()
  const {
    groups,
    createGroup,
    deleteGroup,
    renameGroup,
    updateGroupDescription,
    toggleCollapsed,
    addNodesToGroup,
    removeNodeFromGroup,
    resetGroups,
  } = useGroupState(pushHistory)

  const nodeTypeMap = useMemo(() => new Map(nodeTypes.map(t => [t.id, t])), [nodeTypes])

  const { pluginVersion, gameName, runtimeType } = useGraphEditorSync({
    setRfNodes,
    setExportResult: dialogs.setExportResult,
    nodeTypeMap,
  })

  // 接続先ゲーム名をブラウザタブタイトルに反映（複数ゲーム並行作業時の判別用）
  useEffect(() => {
    document.title = gameName ? `NGOL — ${gameName}` : 'Node Graph mOd Lab (NGOL)'
  }, [gameName])

  const { stableAnnotationTextChange, stableAnnotationDelete, stableAnnotationResize, addAnnotationAtPosition } = useAnnotationRfCallbacks(setRfNodes)

  const handlers = useGraphCanvasHandlers({
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
    updateGroupDescription,
    toggleCollapsed,
    addNodesToGroup,
    removeNodeFromGroup,
    resetGroups,
    pushHistory,
    undo,
    redo,
    recordHistory,
    clearHistory,
    isSpacePressed: selection.isSpacePressed,
    isDragSelecting: selection.isDragSelecting,
    setIsDragSelecting: selection.setIsDragSelecting,
    multiSelectedIds: selection.multiSelectedIds,
    setMultiSelectedIds: selection.setMultiSelectedIds,
    modifierKeyRef: selection.modifierKeyRef,
    preSelectionRef: selection.preSelectionRef,
    lastKnownSelectionRef: selection.lastKnownSelectionRef,
    lastMouseRef: selection.lastMouseRef,
    setAddMenuPos,
    lastCanvasClickRef,
    setFragmentImportMenuPos,
    closeAllMenus,
    execute,
    save,
    addLog,
    hoveredFragmentId,
    setHoveredFragmentId,
    setSaveAsDialogOpen: dialogs.setSaveAsDialogOpen,
    saveAsName: dialogs.saveAsName,
    setSaveAsName: dialogs.setSaveAsName,
    setExportDialogOpen: dialogs.setExportDialogOpen,
    exportDllName: dialogs.exportDllName,
    setExportDllName: dialogs.setExportDllName,
    exportOutputDir: dialogs.exportOutputDir,
    setExportOutputDir: dialogs.setExportOutputDir,
    setExportResult: dialogs.setExportResult,
    setClearCanvasDialogOpen: dialogs.setClearCanvasDialogOpen,
  })

  useEffect(() => {
    const unsub = wsClient.onMessage(msg => {
      if (msg.type === 'open_graph_push') {
        wsClient.loadGraph((msg as any).graphId)
      } else if (msg.type === 'load_graph_response' && msg.success && msg.graph) {
        if (wsClient.getLoadPurpose() !== 'import') {
          handlers.handleLoadGraph(msg.graph)
        }
      }
    })

    return unsub
  }, [handlers])

  const { displayNodes, displayEdges } = useCanvasDisplayNodes({
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
    isSpacePressed: selection.isSpacePressed,
    controlsLocked: layout.controlsLocked,
    isDragSelecting: selection.isDragSelecting,
    multiSelectedIds: selection.multiSelectedIds,
    modifierKeyRef: selection.modifierKeyRef,
    groups,
    groupDragPositions: handlers.groupDragPositions,
    stableGroupToggle: handlers.stableGroupToggle,
    stableGroupRename: handlers.stableGroupRename,
    stableGroupDissolve: handlers.stableGroupDissolve,
    stableAnnotationTextChange,
    stableAnnotationDelete,
    stableAnnotationResize,
  })

  const pluginOverlays = useSyncExternalStore(subscribeExtensions, getExtensionSnapshot).overlays

  return (
    <ExecuteFragmentContext.Provider value={handlers.executeFragmentContextValue}>
    <div
      className="app-layout"
      style={{
        gridTemplateColumns: `${layout.leftColumnCollapsed ? 28 : 220}px 1fr ${layout.rightColumnCollapsed ? 28 : 280}px`,
        gridTemplateRows: `28px 44px 1fr ${layout.logPanelVisible ? 3 : 0}px ${layout.logPanelVisible ? layout.logPanelHeight : 24}px`,
      }}
    >
      <MenuBar
        onClearCanvas={handlers.handleClearCanvas}
        onSave={() => save(handlers.buildGraphData())}
        onSaveAs={() => { dialogs.setSaveAsName(graphName); dialogs.setSaveAsDialogOpen(true) }}
        onLoadGraph={handlers.handleLoadGraph}
        onOpenGraphMenuActiveChange={layout.setFileMenuGraphSubmenuOpen}
        onOpenGraphFromFile={() => handlers.graphFileInputRef.current?.click()}
        onExportNodes={handlers.handleExportNodes}
        onUndo={undo}
        onRedo={redo}
        onShowVersion={() => dialogs.setVersionDialogOpen(true)}
        debugBridgeEnabled={layout.debugBridgeEnabled}
        onToggleDebugBridge={layout.setDebugBridgeEnabled}
        debugDetailLevel={layout.debugDetailLevel}
        onSetDebugDetailLevel={layout.setDebugDetailLevel}
        debugNodeFields={layout.debugNodeFields}
        onSetDebugNodeField={layout.setDebugNodeField}
        onTogglePluginPanel={layout.togglePluginPanel}
        onShowSnapshotStore={() => dialogs.setSnapshotStorePanelOpen(true)}
        onClearAllSnapshots={() => wsClient.clearAllSnapshots()}
        onAddAnnotation={() => {
          const center = rfRef.current?.screenToFlowPosition({ x: window.innerWidth / 2, y: window.innerHeight / 2 }) ?? { x: 100, y: 100 }
          addAnnotationAtPosition(center)
        }}
        canUndo={canUndo}
        canRedo={canRedo}
        canExportNodes={handlers.exportNodeTypeIds.length > 0}
        version={APP_VERSION}
        pluginVersion={pluginVersion}
        connected={connected}
        saving={saving}
      />

      <input
        ref={handlers.graphFileInputRef}
        type="file"
        accept=".json,application/json"
        style={{ display: 'none' }}
        onChange={e => {
          const file = e.target.files?.[0]
          if (file) handlers.loadGraphFromFile(file)
          e.target.value = ''
        }}
      />

      <header className="header">
        <span className="header-title">🎮 Node Graph mOd Lab</span>
        <span style={{ fontSize: 10, color: 'var(--text-dim)' }}>WebUI: {APP_VERSION}</span>
        <input
          value={graphName}
          onChange={e => setGraphName(e.target.value)}
          style={{ width: 200, background: 'transparent', border: '1px solid var(--border)' }}
          placeholder="Graph name"
        />
        <button
          className="primary ngol-btn-with-icon"
          onClick={handlers.handleExecute}
          disabled={!connected || executing || rfNodes.length === 0}
        >
          <NgolIcon
            name={executing ? 'loader' : 'play'}
            size={14}
            className={executing ? 'ngol-icon ngol-icon-spin' : 'ngol-icon'}
          />
          {executing ? 'Running…' : 'Execute'}
        </button>
        {executing && (
          <button className="danger ngol-btn-with-icon" onClick={handlers.handleStop} title="Stop execution">
            <NgolIcon name="stop" size={14} className="ngol-icon" />
            Stop
          </button>
        )}
        {lastSaved && (
          <span style={{ fontSize: 11, color: 'var(--text-dim)' }}>
            Saved {lastSaved.toLocaleTimeString()}
          </span>
        )}
        <div
          className="connection-badge"
          title={connected
            ? `Target process: ${gameName || '(unknown)'}\nPlugin version: ${pluginVersion || '---'}\nRuntime type: ${runtimeType || '(unknown)'}`
            : 'Click to enter connection token'}
          onClick={() => { if (!connected) dialogs.setTokenPromptOpen(true) }}
          style={{ cursor: connected ? 'default' : 'pointer' }}
        >
          <div className={`connection-dot ${connected ? 'connected' : ''}`} />
          {connected ? (gameName ? `Connected: ${gameName}` : 'Connected') : 'Disconnected'}
        </div>
      </header>

      {layout.leftColumnCollapsed ? (
        <div className="palette panel-column-collapsed" onDoubleClick={layout.toggleLeftColumn} title="Double-click to expand">
          <button className="panel-collapse-toggle" onClick={layout.toggleLeftColumn} title="Expand node/graph panels">
            <NgolIcon name="chevron-right" size={16} />
          </button>
          <LeftColumnCollapsedIndicators
            nodeTypes={nodeTypes}
            onLoad={handlers.handleLoadGraph}
            onImportAsFragment={handlers.handleImportAsFragment}
            externalLoadActive={fragmentImportMenuPos !== null || layout.fileMenuGraphSubmenuOpen}
          />
        </div>
      ) : (
        <div className="palette">
          <NodePalette
            nodeTypes={nodeTypes}
            embedded={true}
            headerAction={
              <button className="panel-collapse-toggle panel-collapse-toggle-header" onClick={layout.toggleLeftColumn} title="Collapse node/graph panels">
                <NgolIcon name="chevron-left" size={14} />
              </button>
            }
          />
          <GraphListPanel
            onLoad={handlers.handleLoadGraph}
            onImportAsFragment={handlers.handleImportAsFragment}
            externalLoadActive={fragmentImportMenuPos !== null || layout.fileMenuGraphSubmenuOpen}
          />
        </div>
      )}

      <div ref={handlers.canvasRef} className="canvas-area" onDrop={handlers.onDrop} onDragOver={handlers.onDragOver}>
        <ReactFlow
          nodes={displayNodes}
          edges={displayEdges}
          onNodesChange={handlers.handleNodesChange}
          onEdgesChange={handlers.onEdgesChange}
          onConnect={handlers.onConnect}
          onConnectStart={handlers.handleConnectStart}
          onConnectEnd={handlers.handleConnectEnd}
          onNodeClick={handlers.onNodeClick}
          onNodeDrag={handlers.handleNodeDrag}
          onNodeDoubleClick={(e, node) => {
            if (node.id.startsWith('group-')) { e.stopPropagation(); e.preventDefault() }
          }}
          onNodeContextMenu={(e, node) => {
            if (node.id.startsWith('group-')) {
              e.preventDefault()
              handlers.setGroupContextMenu({ groupId: node.id.slice('group-'.length), x: e.clientX, y: e.clientY })
              return
            }
            handleNodeContextMenu(e, node)
          }}
          onPaneClick={handlers.onPaneClick}
          onPaneContextMenu={handlePaneContextMenu}
          onInit={inst => { rfRef.current = inst }}
          onNodeDragStart={handlers.handleNodeDragStart}
          onNodeDragStop={handlers.handleNodeDragStop}
          onSelectionStart={handlers.onSelectionStart}
          onSelectionEnd={handlers.onSelectionEnd}
          nodeTypes={RF_NODE_TYPES}
          edgeTypes={RF_EDGE_TYPES}
          selectionOnDrag={true}
          panOnDrag={[1]}
          multiSelectionKeyCode="Control"
          zoomOnDoubleClick={false}
          deleteKeyCode={null}
          fitView
          proOptions={{ hideAttribution: true }}
          style={{ background: 'var(--bg-primary)' }}
        >
          {pluginOverlays.map(o => (
            <PluginErrorBoundary key={o.id} pluginId={o.id}>
              <o.component />
            </PluginErrorBoundary>
          ))}
          <Background color="var(--border)" gap={20} />
          <Controls
            className={layout.controlsLocked ? 'controls-locked' : undefined}
            onInteractiveChange={isInteractive => layout.setControlsLocked(!isInteractive)}
          />
          <Panel position="bottom-right" className="minimap-panel">
            {layout.minimapVisible ? (
              <div className="minimap-shell">
                <div className="minimap-shell-header">
                  <span className="minimap-shell-title">Minimap</span>
                  <button type="button" className="minimap-shell-hide" onClick={() => layout.setMinimapVisible(false)} title="Hide minimap">
                    <NgolIcon name="chevron-down" size={14} />
                  </button>
                </div>
                <MiniMap
                  style={{ position: 'static', background: 'var(--bg-panel)', border: 'none', margin: 0 }}
                  nodeColor="var(--accent)"
                  nodeStrokeColor="var(--accent)"
                  nodeStrokeWidth={2}
                  maskColor="rgba(15, 15, 35, 0.75)"
                  maskStrokeColor="var(--border)"
                />
              </div>
            ) : (
              <button type="button" className="minimap-restore-chip" onClick={() => layout.setMinimapVisible(true)} title="Show minimap">
                <NgolIcon name="map" size={14} className="ngol-icon" />
                Minimap
              </button>
            )}
          </Panel>
        </ReactFlow>

        {addMenuPos && (
          <NodeAddMenu
            nodeTypes={nodeTypes}
            position={{ x: addMenuPos.x, y: addMenuPos.y }}
            onAdd={typeId => handlers.addNodeAtCanvasPos(typeId, addMenuPos.canvasX, addMenuPos.canvasY)}
            onClose={() => {
              handlers.pendingConnectionRef.current = null
              setAddMenuPos(null)
            }}
          />
        )}

        {paneContextMenuPos && (
          <div className="node-context-menu" style={{ left: paneContextMenuPos.x, top: paneContextMenuPos.y }} onMouseDown={e => e.stopPropagation()}>
            <button
              className="node-context-menu-item"
              onClick={() => {
                handlers.pendingConnectionRef.current = null
                setAddMenuPos(paneContextMenuPos)
                setPaneContextMenuPos(null)
              }}
            >
              <span className="ngol-btn-with-icon"><NgolIcon name="plus" size={12} className="ngol-icon" /> Add Node...</span>
            </button>
            <div className="node-context-menu-separator" />
            <button
              className="node-context-menu-item"
              onClick={() => {
                const pos = rfRef.current?.screenToFlowPosition({ x: paneContextMenuPos.canvasX, y: paneContextMenuPos.canvasY }) ?? { x: 100, y: 100 }
                addAnnotationAtPosition(pos)
                setPaneContextMenuPos(null)
              }}
            >
              📝 Add Annotation
            </button>
            <div className="node-context-menu-separator" />
            <button
              className="node-context-menu-item"
              onClick={() => {
                setFragmentImportMenuPos(paneContextMenuPos)
                setPaneContextMenuPos(null)
              }}
            >
              🧷 Add Fragment...
            </button>
            <div className="node-context-menu-separator" />
            <button
              className="node-context-menu-item"
              onClick={() => setPaneClearSubmenuOpen(v => !v)}
            >
              <span>Clear Canvas</span>
              <NgolIcon name="chevron-right" size={12} />
            </button>
            {paneClearSubmenuOpen && (
              <button
                className="node-context-menu-item"
                style={{ paddingLeft: 28 }}
                onClick={() => {
                  handlers.handleClearCanvas()
                  setPaneContextMenuPos(null)
                  setPaneClearSubmenuOpen(false)
                }}
              >
                <span>Clear Canvas...</span>
              </button>
            )}
          </div>
        )}

        {fragmentImportMenuPos && (
          <FragmentImportMenu
            position={{ x: fragmentImportMenuPos.x, y: fragmentImportMenuPos.y }}
            onImport={graph => {
              lastCanvasClickRef.current = { x: fragmentImportMenuPos.canvasX, y: fragmentImportMenuPos.canvasY }
              handlers.handleImportAsFragment(graph)
            }}
            onClose={() => setFragmentImportMenuPos(null)}
          />
        )}

        {nodeContextMenu && (() => {
          const isMultiSelect = selection.multiSelectedIds.size >= 2 && selection.multiSelectedIds.has(nodeContextMenu.nodeId)
          const fragId = getFragmentIdForNode(nodeContextMenu.nodeId, fragments)
          const fragName = fragments.find(f => f.id === fragId)?.name ?? null
          const ctxNode = rfNodes.find(n => n.id === nodeContextMenu.nodeId)
          const isSnapshot = (ctxNode?.data as { nodeTypeId?: string })?.nodeTypeId?.startsWith('ngol.snapshot.')
          const hasSnap = isSnapshot && savedSnapshots.has(nodeContextMenu.nodeId)
          const nodeGroupMembership = groups.find(g => g.nodeInstanceIds.includes(nodeContextMenu.nodeId))
          const ctxNodeTypeId = (ctxNode?.data as { nodeTypeId?: string })?.nodeTypeId ?? ''
          const ctxFilePath = nodeTypeMap.get(ctxNodeTypeId)?.filePath
          return (
            <NodeContextMenu
              position={{ x: nodeContextMenu.x, y: nodeContextMenu.y }}
              fragmentName={fragName}
              isSnapshotNode={isSnapshot}
              isPinned={pinnedNodes.has(nodeContextMenu.nodeId)}
              hasSnapshot={hasSnap}
              onExecuteFragment={() => {
                if (fragId) wsClient.executeFragment(handlers.buildGraphData(), fragId, Array.from(pinnedFragmentIds))
              }}
              onDeleteNode={() => handlers.handleDeleteNode(nodeContextMenu.nodeId)}
              onTogglePin={() => togglePin(nodeContextMenu.nodeId)}
              onShowHistory={isSnapshot ? () => requestHistory(nodeContextMenu.nodeId) : undefined}
              onClearSnapshot={isSnapshot ? () => clearSnapshot(nodeContextMenu.nodeId) : undefined}
              onClose={() => setNodeContextMenu(null)}
              selectedNodeCount={isMultiSelect ? selection.multiSelectedIds.size : undefined}
              onDeleteSelected={isMultiSelect ? handlers.handleDeleteSelected : undefined}
              onCreateGroup={isMultiSelect ? handlers.handleCreateGroup : undefined}
              onRemoveFromGroup={nodeGroupMembership && !isMultiSelect
                ? () => handlers.removeNodeFromGroup(nodeGroupMembership.id, nodeContextMenu.nodeId)
                : undefined}
              groups={isMultiSelect ? groups : undefined}
              onAddToGroup={isMultiSelect ? (gId) => handlers.addNodesToGroup(gId, Array.from(selection.multiSelectedIds)) : undefined}
              isPersistent={persistentNodes.has(nodeContextMenu.nodeId)}
              onStopPersistent={persistentNodes.has(nodeContextMenu.nodeId)
                ? () => wsClient.stopPersistentNode(nodeContextMenu.nodeId)
                : undefined}
              nodeFilePath={ctxFilePath}
              onOpenNodeFolder={ctxFilePath ? () => wsClient.openNodeFolder(ctxNodeTypeId) : undefined}
              onCopyNodePath={ctxFilePath ? () => navigator.clipboard.writeText(ctxFilePath) : undefined}
              nodeId={nodeContextMenu.nodeId}
              nodeTypeId={ctxNodeTypeId}
              paramValues={(ctxNode?.data as { paramValues?: Record<string, unknown> })?.paramValues}
            />
          )
        })()}

        {historyState && (
          <SnapshotHistoryPopup
            nodeInstanceId={historyState.nodeInstanceId}
            portName={historyState.portName}
            currentSnapshot={savedSnapshots.get(historyState.nodeInstanceId)}
            entries={historyState.entries}
            isPinned={pinnedNodes.has(historyState.nodeInstanceId)}
            onRestore={(idx) => restoreSnapshot(historyState.nodeInstanceId, historyState.portName, idx)}
            onClose={closeHistory}
          />
        )}

        {handlers.groupContextMenu && (
          <GroupContextMenu
            groupContextMenu={handlers.groupContextMenu}
            setGroupContextMenu={handlers.setGroupContextMenu}
            groupsRef={handlers.groupsRef}
            toggleCollapsed={toggleCollapsed}
            renameGroup={renameGroup}
            updateGroupDescription={updateGroupDescription}
            deleteGroup={deleteGroup}
          />
        )}
      </div>

      {layout.rightColumnCollapsed ? (
        <div className="inspector-column panel-column-collapsed" onDoubleClick={layout.toggleRightColumn} title="Double-click to expand">
          <button className="panel-collapse-toggle" onClick={layout.toggleRightColumn} title="Expand inspector/fragment panels">
            <NgolIcon name="chevron-left" size={16} />
          </button>
          <PersistentCollapsedIndicator
            persistentNodes={persistentNodes}
            currentGraphName={graphName}
          />
        </div>
      ) : (
        <div className="inspector-column">
          <NodeInspector
            selectedNode={handlers.selectedNode}
            nodeTypeInfo={handlers.selectedNodeTypeInfo}
            fragments={fragments}
            onParamChange={handlers.handleParamChange}
            onParamEditStart={handlers.handleParamEditStart}
            onParamEditEnd={handlers.handleParamEditEnd}
            headerAction={
              <button className="panel-collapse-toggle panel-collapse-toggle-header" onClick={layout.toggleRightColumn} title="Collapse inspector/fragment panels">
                <NgolIcon name="chevron-right" size={14} />
              </button>
            }
          />
          <FragmentPanel
            fragments={fragments}
            pinnedIds={pinnedFragmentIds}
            selectedNodeId={selectedNodeId}
            onPinnedIdsChange={setPinnedFragmentIds}
            onExecuteFragment={handlers.handleExecuteFragment}
            onExecuteAll={handlers.handleExecuteAllFragments}
            persistentNodes={persistentNodes}
            currentGraphName={graphName}
          />
        </div>
      )}

      {layout.logPanelVisible ? (
        <>
          <LogPanelResizeHandle height={layout.logPanelHeight} onResize={layout.setLogPanelHeight} />
          <ExecutionLogPanel logs={logs} onClear={clearLogs} onCollapse={() => layout.setLogPanelVisible(false)} />
        </>
      ) : (
        <div className="log-panel log-panel-collapsed" onDoubleClick={() => layout.setLogPanelVisible(true)} title="Double-click to expand">
          <button className="panel-collapse-toggle" onClick={() => layout.setLogPanelVisible(true)} title="Expand log panel">
            <NgolIcon name="chevron-up" size={14} />
            <span className="log-panel-collapsed-label">Activity Log</span>
          </button>
        </div>
      )}

      {compileError && (
        <div className="script-compile-error-toast" role="alert">
          <strong className="ngol-btn-with-icon">
            <NgolIcon name="warning" size={14} className="ngol-icon" />
            Compile Error: {compileError.fileName}
          </strong>
          <pre className="script-compile-error-msg">{compileError.errorMessage}</pre>
          <button className="script-compile-error-dismiss" onClick={dismissCompileError} title="Dismiss">
            <NgolIcon name="close" size={14} />
          </button>
        </div>
      )}

      <EditorDialogHost
        appVersion={APP_VERSION}
        pluginVersion={pluginVersion}
        connected={connected}
        saveAsDialogOpen={dialogs.saveAsDialogOpen}
        saveAsName={dialogs.saveAsName}
        setSaveAsName={dialogs.setSaveAsName}
        setSaveAsDialogOpen={dialogs.setSaveAsDialogOpen}
        handleSaveAsConfirm={handlers.handleSaveAsConfirm}
        exportDialogOpen={dialogs.exportDialogOpen}
        exportDllName={dialogs.exportDllName}
        setExportDllName={dialogs.setExportDllName}
        exportOutputDir={dialogs.exportOutputDir}
        setExportOutputDir={dialogs.setExportOutputDir}
        exportResult={dialogs.exportResult}
        setExportResult={dialogs.setExportResult}
        exportNodeTypeIds={handlers.exportNodeTypeIds}
        handleExportConfirm={handlers.handleExportConfirm}
        setExportDialogOpen={dialogs.setExportDialogOpen}
        versionDialogOpen={dialogs.versionDialogOpen}
        setVersionDialogOpen={dialogs.setVersionDialogOpen}
        clearCanvasDialogOpen={dialogs.clearCanvasDialogOpen}
        setClearCanvasDialogOpen={dialogs.setClearCanvasDialogOpen}
        onClearCanvasConfirm={handlers.handleClearCanvasConfirm}
        tokenPromptOpen={dialogs.tokenPromptOpen}
        setTokenPromptOpen={dialogs.setTokenPromptOpen}
        tokenPromptValue={dialogs.tokenPromptValue}
        setTokenPromptValue={dialogs.setTokenPromptValue}
        handleTokenPromptConfirm={dialogs.handleTokenPromptConfirm}
      />

      {dialogs.snapshotStorePanelOpen && (
        <SnapshotStorePanel
          savedSnapshots={savedSnapshots}
          onClearAll={() => wsClient.clearAllSnapshots()}
          onClose={() => dialogs.setSnapshotStorePanelOpen(false)}
        />
      )}

      {getPluginPanels()
        .filter(p => layout.openPluginPanelIds.includes(p.id))
        .map((p, i) => (
          <PluginPanelHost key={p.id} def={p} index={i} onClose={() => layout.togglePluginPanel(p.id)} />
        ))}
    </div>
    </ExecuteFragmentContext.Provider>
  )
}
