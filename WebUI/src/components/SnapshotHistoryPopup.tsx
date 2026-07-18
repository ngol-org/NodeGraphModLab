import { useState, useEffect, useRef } from 'react'
import type { SnapshotHistoryEntry } from '../types/protocol'
import type { SnapshotBadgeInfo } from '../hooks/useGraphEditor'
import './SnapshotHistoryPopup.css'

interface Props {
  nodeInstanceId: string
  portName: string
  currentSnapshot?: SnapshotBadgeInfo
  entries: SnapshotHistoryEntry[]
  isPinned?: boolean
  onRestore: (historyIndex: number) => void
  onClose: () => void
}

function formatTimestamp(ms: number): string {
  const d = new Date(ms)
  return `${d.getHours().toString().padStart(2, '0')}:${d.getMinutes().toString().padStart(2, '0')}:${d.getSeconds().toString().padStart(2, '0')}`
}

export function SnapshotHistoryPopup({ portName, currentSnapshot, entries, isPinned, onRestore, onClose }: Props) {
  const [selectedIndex, setSelectedIndex] = useState<number | null>(null)
  const [dragPos, setDragPos] = useState<{ left: number; top: number } | null>(null)
  const popupRef = useRef<HTMLDivElement>(null)
  const dragStartRef = useRef<{ x: number; y: number; left: number; top: number } | null>(null)

  useEffect(() => {
    const handleClick = (e: MouseEvent) => {
      if (popupRef.current && !popupRef.current.contains(e.target as Node)) {
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

  const handleHeaderMouseDown = (e: React.MouseEvent) => {
    e.stopPropagation()
    const rect = popupRef.current?.getBoundingClientRect()
    if (!rect) return
    dragStartRef.current = { x: e.clientX, y: e.clientY, left: rect.left, top: rect.top }

    const handleMove = (ev: MouseEvent) => {
      const start = dragStartRef.current
      if (!start) return
      setDragPos({ left: start.left + (ev.clientX - start.x), top: start.top + (ev.clientY - start.y) })
    }
    const handleUp = () => {
      dragStartRef.current = null
      window.removeEventListener('mousemove', handleMove)
      window.removeEventListener('mouseup', handleUp)
    }
    window.addEventListener('mousemove', handleMove)
    window.addEventListener('mouseup', handleUp)
  }

  return (
    <div
      ref={popupRef}
      className="snapshot-history-popup"
      style={dragPos ? { left: dragPos.left, top: dragPos.top, transform: 'none' } : undefined}
    >
      <div className="snapshot-history-header" onMouseDown={handleHeaderMouseDown}>
        <span className="snapshot-history-title">Snapshot History</span>
        <span className="snapshot-history-port">{portName}</span>
      </div>
      <div className="snapshot-history-list">
        {currentSnapshot && (
          <div className="snapshot-history-entry snapshot-history-current">
            <span className="snapshot-history-index">[Current]</span>
            <span className="snapshot-history-value" title={currentSnapshot.valueString ?? ''}>
              {currentSnapshot.valueString ?? '(null)'}
            </span>
            <span className="snapshot-history-type">{currentSnapshot.valueType}</span>
            <span className="snapshot-history-time">{currentSnapshot.time}</span>
          </div>
        )}
        {entries.length === 0 ? (
          currentSnapshot
            ? <div className="snapshot-history-empty">No past history</div>
            : <div className="snapshot-history-empty">No history yet</div>
        ) : (
          entries.map((entry, idx) => (
            <button
              key={idx}
              className={`snapshot-history-entry${selectedIndex === idx ? ' selected' : ''}`}
              onClick={() => setSelectedIndex(idx)}
            >
              <span className="snapshot-history-index">[{idx}]</span>
              <span className="snapshot-history-value" title={entry.valueString ?? ''}>
                {entry.valueString ?? '(null)'}
              </span>
              <span className="snapshot-history-type">{entry.valueType}</span>
              <span className="snapshot-history-time">{formatTimestamp(entry.timestampMs)}</span>
            </button>
          ))
        )}
      </div>
      <div className="snapshot-history-footer">
        {isPinned && (
          <div className="snapshot-history-pin-notice">📌 Pinned — unpin to restore</div>
        )}
        <div className="snapshot-history-btn-row">
          <button
            className="snapshot-history-btn snapshot-history-btn-restore"
            disabled={selectedIndex === null || !!isPinned}
            onClick={() => {
              if (selectedIndex !== null) {
                onRestore(selectedIndex)
                onClose()
              }
            }}
          >
            Restore
          </button>
          <button className="snapshot-history-btn snapshot-history-btn-close" onClick={onClose}>
            Close
          </button>
        </div>
      </div>
    </div>
  )
}
