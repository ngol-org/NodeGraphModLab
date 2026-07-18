import { useEffect, useMemo, useRef } from 'react'
import { NgolIcon } from './icons/NgolIcon'
import { collectContextMenuItems } from '../webuiPlugin/pluginExtensionRegistry'
import './NodeContextMenu.css'

interface Props {
  position: { x: number; y: number }
  fragmentName: string | null
  isSnapshotNode?: boolean
  isPinned?: boolean
  hasSnapshot?: boolean
  onExecuteFragment: () => void
  onDeleteNode: () => void
  onTogglePin?: () => void
  onShowHistory?: () => void
  onClearSnapshot?: () => void
  onClose: () => void
  // 複数選択モード
  selectedNodeCount?: number
  onDeleteSelected?: () => void
  onCreateGroup?: () => void
  // 既存グループに追加（複数選択時）
  groups?: { id: string; name: string }[]
  onAddToGroup?: (groupId: string) => void
  // グループメンバー除外（単一選択時）
  onRemoveFromGroup?: () => void
  // 永続実行停止
  isPersistent?: boolean
  onStopPersistent?: () => void
  // ファイル場所を開く / パスコピー（スクリプトノード専用）
  nodeFilePath?: string
  onOpenNodeFolder?: () => void
  onCopyNodePath?: () => void
  // プラグイン拡張項目のノードコンテキスト
  nodeId?: string
  nodeTypeId?: string
  paramValues?: Record<string, unknown>
}

export function NodeContextMenu({ position, fragmentName, isSnapshotNode, isPinned, hasSnapshot, onExecuteFragment, onDeleteNode, onTogglePin, onShowHistory, onClearSnapshot, onClose, selectedNodeCount, onDeleteSelected, onCreateGroup, groups, onAddToGroup, onRemoveFromGroup, isPersistent, onStopPersistent, nodeFilePath, onOpenNodeFolder, onCopyNodePath, nodeId, nodeTypeId, paramValues }: Props) {
  const menuRef = useRef<HTMLDivElement>(null)

  // プラグイン拡張項目 — メニューを開いた時点で provider を評価
  const pluginItems = useMemo(() => {
    if (!nodeId || !nodeTypeId) return []
    return collectContextMenuItems({
      nodeId,
      nodeTypeId,
      paramValues: paramValues ?? {},
      isPersistentRunning: isPersistent ?? false,
    })
  }, [nodeId, nodeTypeId, paramValues, isPersistent])

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

  const isMultiSelect = (selectedNodeCount ?? 0) >= 2

  if (isMultiSelect) {
    return (
      <div
        ref={menuRef}
        className="node-context-menu"
        style={{ left: position.x, top: position.y }}
        onMouseDown={e => e.stopPropagation()}
      >
        {onCreateGroup && (
          <button
            className="node-context-menu-item"
            onClick={() => { onCreateGroup(); onClose() }}
          >
            🗂 Group selection
          </button>
        )}
        {groups && groups.length > 0 && onAddToGroup && (
          <>
            <div className="node-context-menu-separator" />
            <div className="node-context-menu-label">Add to existing group:</div>
            {groups.map(g => (
              <button
                key={g.id}
                className="node-context-menu-item"
                onClick={() => { onAddToGroup(g.id); onClose() }}
              >
                🗂 {g.name}
              </button>
            ))}
          </>
        )}
        <div className="node-context-menu-separator" />
        <button
          className="node-context-menu-item node-context-menu-item-danger"
          onClick={() => { onDeleteSelected?.(); onClose() }}
        >
          🗑 Delete {selectedNodeCount} Nodes
        </button>
      </div>
    )
  }

  return (
    <div
      ref={menuRef}
      className="node-context-menu"
      style={{ left: position.x, top: position.y }}
      onMouseDown={e => e.stopPropagation()}
    >
      <button
        className="node-context-menu-item"
        onClick={() => { onExecuteFragment(); onClose() }}
        title={fragmentName ? `Run fragment "${fragmentName}"` : 'Run fragment'}
      >
        <span className="ngol-btn-with-icon"><NgolIcon name="play" size={12} className="ngol-icon" /> Run Fragment</span>
        {fragmentName && (
          <span className="node-context-menu-hint">{fragmentName}</span>
        )}
      </button>
      {isSnapshotNode && (
        <>
          <div className="node-context-menu-separator" />
          {onShowHistory && (
            <button
              className="node-context-menu-item"
              onClick={() => { onShowHistory(); onClose() }}
            >
              📜 Snapshot History
            </button>
          )}
          {onClearSnapshot && hasSnapshot && (
            <button
              className="node-context-menu-item"
              onClick={() => { onClearSnapshot(); onClose() }}
            >
              🗑 Clear Snapshot
            </button>
          )}
          {onTogglePin && (
            <button
              className="node-context-menu-item"
              onClick={() => { onTogglePin(); onClose() }}
            >
              {isPinned ? '📌 Unpin Snapshot' : '📌 Pin Snapshot'}
            </button>
          )}
        </>
      )}
      <div className="node-context-menu-separator" />
      {isPersistent && onStopPersistent && (
        <>
          <div className="node-context-menu-separator" />
          <button
            className="node-context-menu-item node-context-menu-item-danger"
            onClick={() => { onStopPersistent(); onClose() }}
          >
            <span className="ngol-btn-with-icon"><NgolIcon name="stop" size={12} className="ngol-icon" /> Stop Persistent</span>
          </button>
        </>
      )}
      {onRemoveFromGroup && (
        <button
          className="node-context-menu-item"
          onClick={() => { onRemoveFromGroup(); onClose() }}
        >
          <span className="ngol-btn-with-icon"><NgolIcon name="minus" size={12} className="ngol-icon" /> Remove from Group</span>
        </button>
      )}
      {nodeFilePath && (
        <>
          <div className="node-context-menu-separator" />
          {onOpenNodeFolder && (
            <button
              className="node-context-menu-item"
              onClick={() => { onOpenNodeFolder(); onClose() }}
              title={nodeFilePath}
            >
              📂 Open File Location
            </button>
          )}
          {onCopyNodePath && (
            <button
              className="node-context-menu-item"
              onClick={() => { onCopyNodePath(); onClose() }}
              title={nodeFilePath}
            >
              📋 Copy File Path
            </button>
          )}
        </>
      )}
      {/* プラグイン拡張項目 */}
      {pluginItems.length > 0 && (
        <>
          <div className="node-context-menu-separator" />
          {pluginItems.map((item, i) => (
            <button
              key={i}
              className="node-context-menu-item"
              onClick={() => {
                try {
                  item.onClick()
                } catch (e) {
                  console.warn('[NGOL Plugin] context menu item error:', e)
                }
                onClose()
              }}
            >
              <span>{item.label}</span>
            </button>
          ))}
        </>
      )}
      <button
        className="node-context-menu-item node-context-menu-item-danger"
        onClick={() => { onDeleteNode(); onClose() }}
      >
        🗑 Delete Node
      </button>
    </div>
  )
}
