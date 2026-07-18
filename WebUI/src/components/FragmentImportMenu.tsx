import { useEffect, useRef, useState, useCallback } from 'react'
import { useGraphList } from '../hooks/useGraphEditor'
import { wsClient } from '../lib/wsClient'
import type { NodeGraphData } from '../types/protocol'

interface Props {
  position: { x: number; y: number }
  onImport: (graph: NodeGraphData) => void
  onClose: () => void
}

export function FragmentImportMenu({ position, onImport, onClose }: Props) {
  const menuRef = useRef<HTMLDivElement>(null)
  const { graphs, refresh } = useGraphList()
  const [loadingId, setLoadingId] = useState<string | null>(null)

  useEffect(() => { refresh() }, [refresh])

  // load_graph_response をここで受け取り onImport に転送
  useEffect(() => {
    const unsub = wsClient.onMessage(msg => {
      if (msg.type === 'load_graph_response') {
        setLoadingId(null)
        if (msg.success && msg.graph) {
          onImport(msg.graph)
          onClose()
        }
      }
    })
    return unsub
  }, [onImport, onClose])

  // 外クリック / Escape で閉じる
  useEffect(() => {
    const handleClick = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) {
        onClose()
      }
    }
    const handleKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
    }
    document.addEventListener('mousedown', handleClick)
    document.addEventListener('keydown', handleKey)
    return () => {
      document.removeEventListener('mousedown', handleClick)
      document.removeEventListener('keydown', handleKey)
    }
  }, [onClose])

  const handleSelect = useCallback((id: string) => {
    setLoadingId(id)
    wsClient.loadGraph(id, 'import')
  }, [])

  return (
    <div
      ref={menuRef}
      className="node-context-menu fragment-import-menu"
      style={{ left: position.x, top: position.y }}
    >
      <div className="node-context-menu-item fragment-import-menu-header">
        Add Fragment from Graph
      </div>
      <div className="node-context-menu-separator" />
      {graphs.length === 0 ? (
        <div className="node-context-menu-item" style={{ color: 'var(--text-dim)', cursor: 'default' }}>
          No saved graphs
        </div>
      ) : (
        graphs.map(g => (
          <button
            key={g.id}
            className="node-context-menu-item"
            onClick={() => handleSelect(g.id)}
            disabled={loadingId !== null}
            title={g.id}
          >
            {loadingId === g.id ? '…' : g.name}
          </button>
        ))
      )}
    </div>
  )
}
