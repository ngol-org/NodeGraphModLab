import { useState, useEffect, useRef } from 'react'
import { useGraphList } from '../hooks/useGraphEditor'
import { wsClient } from '../lib/wsClient'
import type { NodeGraphData } from '../types/protocol'
import { NgolIcon } from './icons/NgolIcon'
import type { DebugDetailLevel, DebugNodeField } from '../webuiPlugin/debugBridge'
import { getPluginPanels } from '../webuiPlugin/pluginPanelRegistry'
import { subscribeExtensions, getExtensionSnapshot } from '../webuiPlugin/pluginExtensionRegistry'
import {
  subscribeNodeTypeOverrides,
  getNodeTypeOverrideSnapshot,
  setNodeTypeOverrideDisabled,
} from '../webuiPlugin/nodeTypeOverrideRegistry'
import { useSyncExternalStore } from 'react'
import './MenuBar.css'

interface MenuBarProps {
  onClearCanvas: () => void
  onSave: () => void
  onSaveAs: () => void
  onLoadGraph: (graph: NodeGraphData) => void
  onOpenGraphMenuActiveChange: (active: boolean) => void
  onOpenGraphFromFile: () => void
  onExportNodes: () => void
  onUndo: () => void
  onRedo: () => void
  onShowVersion: () => void
  debugBridgeEnabled: boolean
  onToggleDebugBridge: (enabled: boolean) => void
  debugDetailLevel: DebugDetailLevel
  onSetDebugDetailLevel: (level: DebugDetailLevel) => void
  debugNodeFields: Record<DebugNodeField, boolean>
  onSetDebugNodeField: (field: DebugNodeField, value: boolean) => void
  onTogglePluginPanel: (panelId: string) => void
  onShowSnapshotStore: () => void
  onClearAllSnapshots: () => void
  onAddAnnotation: () => void
  canUndo: boolean
  canRedo: boolean
  canExportNodes: boolean
  version: string
  pluginVersion: string
  connected: boolean
  saving: boolean
}

export function MenuBar({ onClearCanvas, onSave, onSaveAs, onLoadGraph, onOpenGraphMenuActiveChange, onOpenGraphFromFile, onExportNodes, onUndo, onRedo, onShowVersion, debugBridgeEnabled, onToggleDebugBridge, debugDetailLevel, onSetDebugDetailLevel, debugNodeFields, onSetDebugNodeField, onTogglePluginPanel, onShowSnapshotStore, onClearAllSnapshots, onAddAnnotation, canUndo, canRedo, canExportNodes, version, pluginVersion, connected, saving }: MenuBarProps) {
  const pluginPanels = getPluginPanels()
  // プラグイン拡張メニュー — 遅延登録に追従するため変更通知付きストアを購読
  const extensionMenus = useSyncExternalStore(subscribeExtensions, getExtensionSnapshot).menus
  // ノード型上書き — トグル状態の変更に追従するためストアを購読
  const nodeTypeOverrides = useSyncExternalStore(subscribeNodeTypeOverrides, getNodeTypeOverrideSnapshot)
  const [openMenu, setOpenMenu] = useState<string | null>(null)
  const [openGraphSubmenu, setOpenGraphSubmenu] = useState(false)
  const ref = useRef<HTMLDivElement>(null)
  const { graphs, refresh } = useGraphList()
  const [loadingGraphId, setLoadingGraphId] = useState<string | null>(null)

  useEffect(() => {
    const handler = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) {
        setOpenMenu(null)
        setOpenGraphSubmenu(false)
      }
    }
    document.addEventListener('mousedown', handler)
    return () => document.removeEventListener('mousedown', handler)
  }, [])

  // File > Open Graph サブメニューの開閉を親へ通知（GraphListPanel の二重処理防止用）
  useEffect(() => {
    onOpenGraphMenuActiveChange(openGraphSubmenu)
  }, [openGraphSubmenu, onOpenGraphMenuActiveChange])

  // サブメニューを開いたタイミングでグラフ一覧を更新
  useEffect(() => {
    if (openGraphSubmenu) refresh()
  }, [openGraphSubmenu, refresh])

  // load_graph_response をここで受け取り onLoadGraph に転送
  useEffect(() => {
    const unsub = wsClient.onMessage(msg => {
      if (msg.type === 'load_graph_response') {
        setLoadingGraphId(null)
        if (!openGraphSubmenu) return
        if (msg.success && msg.graph) {
          onLoadGraph(msg.graph)
          setOpenGraphSubmenu(false)
          setOpenMenu(null)
        }
      }
    })
    return unsub
  }, [onLoadGraph, openGraphSubmenu])

  const toggle = (id: string) => {
    setOpenMenu(prev => prev === id ? null : id)
    setOpenGraphSubmenu(false)
  }
  // 既にいずれかのカテゴリが展開中のときだけ、横移動先のカテゴリへ自動切替する
  const handleMenuHover = (id: string) => {
    if (openMenu === null || openMenu === id) return
    setOpenMenu(id)
    setOpenGraphSubmenu(false)
  }
  const pick = (action: () => void) => { setOpenMenu(null); setOpenGraphSubmenu(false); action() }
  const handleSelectGraph = (id: string) => {
    setLoadingGraphId(id)
    wsClient.loadGraph(id)
  }

  return (
    <div className="menubar" ref={ref}>
      {/* File */}
      <div className="menubar-item">
        <button
          className={`menubar-label${openMenu === 'file' ? ' active' : ''}`}
          onClick={() => toggle('file')}
          onMouseEnter={() => handleMenuHover('file')}
        >
          File
        </button>
        {openMenu === 'file' && (
          <div className="menubar-dropdown">
            <button
              className="menubar-dropdown-item"
              onClick={() => pick(onSave)}
              disabled={!connected || saving}
            >
              <span>Save Graph</span>
              <span className="menubar-shortcut">Ctrl+S</span>
            </button>
            <button
              className="menubar-dropdown-item"
              onClick={() => pick(onSaveAs)}
              disabled={!connected}
            >
              <span>Save Graph As...</span>
              <span className="menubar-shortcut">Ctrl+Shift+S</span>
            </button>
            <div className="menubar-separator" />
            <div className="menubar-submenu-wrapper">
              <button
                className="menubar-dropdown-item"
                onClick={() => setOpenGraphSubmenu(v => !v)}
                disabled={!connected}
              >
                <span>Open Graph...</span>
                <NgolIcon name="chevron-right" size={12} />
              </button>
              {openGraphSubmenu && (
                <div className="menubar-dropdown menubar-submenu">
                  {graphs.length === 0 ? (
                    <div className="menubar-dropdown-item" style={{ color: 'var(--text-dim)', cursor: 'default' }}>
                      <span>No saved graphs</span>
                    </div>
                  ) : (
                    graphs.map(g => (
                      <button
                        key={g.id}
                        className="menubar-dropdown-item"
                        onClick={() => handleSelectGraph(g.id)}
                        disabled={loadingGraphId !== null}
                        title={g.id}
                      >
                        <span>{loadingGraphId === g.id ? '…' : g.name}</span>
                      </button>
                    ))
                  )}
                </div>
              )}
            </div>
            <button
              className="menubar-dropdown-item"
              onClick={() => pick(onOpenGraphFromFile)}
            >
              <span>Open Graph from File...</span>
            </button>
            <div className="menubar-separator" />
            <button
              className="menubar-dropdown-item"
              onClick={() => pick(onClearCanvas)}
            >
              <span>Clear Canvas...</span>
            </button>
            <button
              className="menubar-dropdown-item"
              onClick={() => pick(onExportNodes)}
              disabled={!connected || !canExportNodes}
            >
              <span>Export Nodes as DLL...</span>
            </button>
          </div>
        )}
      </div>

      {/* Edit */}
      <div className="menubar-item">
        <button
          className={`menubar-label${openMenu === 'edit' ? ' active' : ''}`}
          onClick={() => toggle('edit')}
          onMouseEnter={() => handleMenuHover('edit')}
        >
          Edit
        </button>
        {openMenu === 'edit' && (
          <div className="menubar-dropdown">
            <button className="menubar-dropdown-item" onClick={() => pick(onUndo)} disabled={!canUndo}>
              <span>Undo</span>
              <span className="menubar-shortcut">Ctrl+Z</span>
            </button>
            <button className="menubar-dropdown-item" onClick={() => pick(onRedo)} disabled={!canRedo}>
              <span>Redo</span>
              <span className="menubar-shortcut">Ctrl+Shift+Z</span>
            </button>
          </div>
        )}
      </div>

      {/* Nodes */}
      <div className="menubar-item">
        <button
          className={`menubar-label${openMenu === 'nodes' ? ' active' : ''}`}
          onClick={() => toggle('nodes')}
          onMouseEnter={() => handleMenuHover('nodes')}
        >
          Nodes
        </button>
        {openMenu === 'nodes' && (
          <div className="menubar-dropdown">
            <button
              className="menubar-dropdown-item"
              onClick={() => pick(onAddAnnotation)}
            >
              <span>Add Annotation</span>
              <span className="menubar-shortcut">sticky note</span>
            </button>
            <div className="menubar-separator" />
            <button
              className="menubar-dropdown-item"
              onClick={() => pick(onShowSnapshotStore)}
              disabled={!connected}
            >
              <span>Snapshot Store...</span>
            </button>
            <div className="menubar-separator" />
            <button
              className="menubar-dropdown-item"
              onClick={() => pick(onClearAllSnapshots)}
              disabled={!connected}
            >
              <span>Clear All Snapshots</span>
            </button>
          </div>
        )}
      </div>

      {/* Plugins — 外部プラグインのパネルまたはノード型上書きが登録されている場合のみ表示 */}
      {(pluginPanels.length > 0 || nodeTypeOverrides.overrides.size > 0) && (
        <div className="menubar-item">
          <button
            className={`menubar-label${openMenu === 'plugins' ? ' active' : ''}`}
            onClick={() => toggle('plugins')}
            onMouseEnter={() => handleMenuHover('plugins')}
          >
            Plugins
          </button>
          {openMenu === 'plugins' && (
            <div className="menubar-dropdown">
              {pluginPanels.map(p => (
                <button
                  key={p.id}
                  className="menubar-dropdown-item"
                  onClick={() => pick(() => onTogglePluginPanel(p.id))}
                >
                  <span>{p.title}</span>
                </button>
              ))}
              {/* ノード型上書きの有効/無効トグル — チェック OFF で標準 UI に戻す */}
              {nodeTypeOverrides.overrides.size > 0 && (
                <>
                  {pluginPanels.length > 0 && <div className="menubar-separator" />}
                  <div
                    className="menubar-dropdown-item"
                    style={{ color: 'var(--text-dim)', cursor: 'default', fontSize: 11 }}
                  >
                    <span>Node Overrides</span>
                  </div>
                  {[...nodeTypeOverrides.overrides.entries()].map(([nodeTypeId, entry]) => (
                    <label
                      key={nodeTypeId}
                      className="menubar-dropdown-item"
                      style={{ cursor: 'pointer', display: 'flex', gap: 6, alignItems: 'center', justifyContent: 'flex-start' }}
                      title={`Override by: ${entry.label}`}
                    >
                      <input
                        type="checkbox"
                        checked={!nodeTypeOverrides.disabled.has(nodeTypeId)}
                        onChange={e => setNodeTypeOverrideDisabled(nodeTypeId, !e.target.checked)}
                      />
                      <span>{nodeTypeId}</span>
                    </label>
                  ))}
                </>
              )}
            </div>
          )}
        </div>
      )}

      {/* Debug */}
      <div className="menubar-item">
        <button
          className={`menubar-label${openMenu === 'debug' ? ' active' : ''}`}
          onClick={() => toggle('debug')}
          onMouseEnter={() => handleMenuHover('debug')}
        >
          Debug
        </button>
        {openMenu === 'debug' && (
          <div className="menubar-dropdown">
            <label
              className="menubar-dropdown-item"
              style={{ cursor: 'pointer', display: 'flex', gap: 6, alignItems: 'center', justifyContent: 'flex-start' }}
              title="Capture browser console and DOM events for MCP debug log retrieval"
            >
              <input
                type="checkbox"
                checked={debugBridgeEnabled}
                onChange={e => onToggleDebugBridge(e.target.checked)}
              />
              <span>Debug Bridge</span>
            </label>
            <div className="menubar-separator" />
            <div
              className="menubar-dropdown-item"
              style={{ color: 'var(--text-dim)', cursor: 'default', fontSize: 11 }}
            >
              <span>DOM Event Node Dump</span>
            </div>
            {(['minimal', 'proximity', 'full'] as const).map(level => (
              <label
                key={level}
                className="menubar-dropdown-item"
                style={{ cursor: 'pointer', display: 'flex', gap: 6, alignItems: 'center', justifyContent: 'flex-start' }}
                title={
                  level === 'minimal'
                    ? 'Do not include reactFlowNodes in dom_event payload (smallest, default)'
                    : level === 'proximity'
                      ? 'Include only reactFlowNodes near the click position'
                      : 'Include all reactFlowNodes (largest payload)'
                }
              >
                <input
                  type="radio"
                  name="debug-detail-level"
                  checked={debugDetailLevel === level}
                  onChange={() => onSetDebugDetailLevel(level)}
                />
                <span>{level === 'minimal' ? 'Minimal' : level === 'proximity' ? 'Proximity' : 'Full'}</span>
              </label>
            ))}
            <div className="menubar-separator" />
            <div
              className="menubar-dropdown-item"
              style={{ color: 'var(--text-dim)', cursor: 'default', fontSize: 11 }}
            >
              <span>Node Dump Fields</span>
            </div>
            {(['rect', 'pointerEvents', 'visibility', 'transform'] as const).map(field => (
              <label
                key={field}
                className="menubar-dropdown-item"
                style={{ cursor: 'pointer', display: 'flex', gap: 6, alignItems: 'center', justifyContent: 'flex-start' }}
                title={`Include "${field}" in each reactFlowNodes entry (only applies at Proximity/Full)`}
              >
                <input
                  type="checkbox"
                  checked={debugNodeFields[field]}
                  onChange={e => onSetDebugNodeField(field, e.target.checked)}
                />
                <span>{field}</span>
              </label>
            ))}
          </div>
        )}
      </div>

      {/* Help */}
      <div className="menubar-item">
        <button
          className={`menubar-label${openMenu === 'help' ? ' active' : ''}`}
          onClick={() => toggle('help')}
          onMouseEnter={() => handleMenuHover('help')}
        >
          Help
        </button>
        {openMenu === 'help' && (
          <div className="menubar-dropdown">
            <button className="menubar-dropdown-item" onClick={() => pick(onShowVersion)}>
              <span>Version</span>
            </button>
          </div>
        )}
      </div>

      {/* プラグイン拡張メニュー — メニューバー右端に追加 */}
      {extensionMenus.map(menu => {
        const menuKey = `ext:${menu.label}`
        return (
          <div key={menuKey} className="menubar-item">
            <button
              className={`menubar-label${openMenu === menuKey ? ' active' : ''}`}
              onClick={() => toggle(menuKey)}
              onMouseEnter={() => handleMenuHover(menuKey)}
            >
              {menu.label}
            </button>
            {openMenu === menuKey && (
              <div className="menubar-dropdown">
                {menu.items.map((item, i) =>
                  'separator' in item && item.separator ? (
                    <div key={i} className="menubar-separator" />
                  ) : (
                    <button
                      key={i}
                      className="menubar-dropdown-item"
                      onClick={() => pick(() => {
                        try {
                          (item as { onClick: () => void }).onClick()
                        } catch (e) {
                          console.warn('[NGOL Plugin] menu item error:', e)
                        }
                      })}
                    >
                      <span>{(item as { label: string }).label}</span>
                    </button>
                  )
                )}
              </div>
            )}
          </div>
        )
      })}
    </div>
  )
}
