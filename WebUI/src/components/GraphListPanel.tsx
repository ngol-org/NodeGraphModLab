import { useEffect, useCallback, useState, useRef } from 'react'
import { useGraphList } from '../hooks/useGraphEditor'
import { wsClient } from '../lib/wsClient'
import { NgolIcon } from './icons/NgolIcon'
import './GraphListPanel.css'
import type { NodeGraphData } from '../types/protocol'

// ────────────────────────────────────────────────────────────────
// Props
// ────────────────────────────────────────────────────────────────
interface GraphListPanelProps {
  /** グラフ読み込み成功時のコールバック */
  onLoad: (graph: NodeGraphData) => void
  /** 断片として読み込む時のコールバック */
  onImportAsFragment?: (graph: NodeGraphData) => void
  /** true の間は load_graph_response を無視する（FragmentImportMenu使用時） */
  externalLoadActive?: boolean
}

// ────────────────────────────────────────────────────────────────
// GraphListPanel
// ────────────────────────────────────────────────────────────────
export function GraphListPanel({ onLoad, onImportAsFragment, externalLoadActive }: GraphListPanelProps) {
  const { graphs, refresh } = useGraphList()
  const [loadingId, setLoadingId] = useState<string | null>(null)
  const [deletingId, setDeletingId] = useState<string | null>(null)
  const [confirmDeleteId, setConfirmDeleteId] = useState<string | null>(null)
  const loadModeRef = useRef<'load' | 'import'>('load')

  // load_graph_response / delete_graph_response の受信
  useEffect(() => {
    const unsub = wsClient.onMessage(msg => {
      if (msg.type === 'load_graph_response') {
        setLoadingId(null)
        if (externalLoadActive) return  // FragmentImportMenu が処理中 → 無視
        if (msg.success && msg.graph) {
          if (loadModeRef.current === 'import' && onImportAsFragment) {
            onImportAsFragment(msg.graph)
          } else {
            onLoad(msg.graph)
          }
          loadModeRef.current = 'load'
        }
      } else if (msg.type === 'delete_graph_response') {
        setDeletingId(null)
        setConfirmDeleteId(null)
        if (msg.success) refresh()
      }
    })
    return unsub
  }, [onLoad, onImportAsFragment, refresh, externalLoadActive])

  const handleLoad = useCallback((id: string) => {
    loadModeRef.current = 'load'
    setLoadingId(id)
    wsClient.loadGraph(id)
  }, [])

  const handleImport = useCallback((id: string) => {
    loadModeRef.current = 'import'
    setLoadingId(id)
    wsClient.loadGraph(id, 'import')
  }, [])

  const handleDeleteConfirm = useCallback((id: string) => {
    setDeletingId(id)
    setConfirmDeleteId(null)
    wsClient.deleteGraph(id)
  }, [])

  return (
    <div className="graph-list-panel">
      <div className="panel-header" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <span>Saved Graphs</span>
        <button
          style={{ padding: '2px 8px', fontSize: 11 }}
          onClick={refresh}
          title="Refresh"
        >
          ↺
        </button>
      </div>

      <div className="graph-list-body">
        {graphs.length === 0 ? (
          <div className="graph-list-empty">
            No saved graphs
          </div>
        ) : (
          graphs.map(g => (
            <div key={g.id} className="graph-list-item">
              {/* 削除確認モード */}
              {confirmDeleteId === g.id ? (
                <div className="graph-list-confirm">
                  <span className="graph-list-confirm-text">Delete?</span>
                  <button
                    className="danger"
                    style={{ padding: '2px 8px', fontSize: 11 }}
                    onClick={() => handleDeleteConfirm(g.id)}
                    disabled={deletingId === g.id}
                  >
                    {deletingId === g.id ? '…' : 'Delete'}
                  </button>
                  <button
                    style={{ padding: '2px 8px', fontSize: 11 }}
                    onClick={() => setConfirmDeleteId(null)}
                  >
                    Cancel
                  </button>
                </div>
              ) : (
                <>
                  <div className="graph-list-name" title={g.id}>
                    {g.name}
                    {g.description && (
                      <span className="graph-list-desc">{g.description}</span>
                    )}
                  </div>
                  <div className="graph-list-actions">
                    <button
                      style={{ padding: '2px 8px', fontSize: 11 }}
                      onClick={() => handleLoad(g.id)}
                      disabled={loadingId === g.id}
                      title="Load graph"
                    >
                      {loadingId === g.id ? '…' : 'Open'}
                    </button>
                    {onImportAsFragment && (
                      <button
                        style={{ padding: '2px 8px', fontSize: 11, color: 'var(--accent)' }}
                        onClick={() => handleImport(g.id)}
                        disabled={loadingId === g.id}
                        title="Add as fragment to current graph"
                      >
                        Add
                      </button>
                    )}
                    <button
                      style={{ padding: '2px 8px', fontSize: 11, color: 'var(--error)' }}
                      onClick={() => setConfirmDeleteId(g.id)}
                      title="Delete"
                    >
                      <NgolIcon name="close" size={10} />
                    </button>
                  </div>
                </>
              )}
            </div>
          ))
        )}
      </div>
    </div>
  )
}
