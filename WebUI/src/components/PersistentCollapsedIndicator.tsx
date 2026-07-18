import { useCallback, useEffect, useRef, useState } from 'react'
import { PersistentRunningIcon } from './icons/PersistentRunningIcon'
import { PersistentNodesList } from './PersistentNodesList'
import './PersistentCollapsedIndicator.css'
import type { PersistentNodeInfo } from '../types/protocol'

interface PersistentCollapsedIndicatorProps {
  persistentNodes: Map<string, PersistentNodeInfo>
  currentGraphName: string
}

export function PersistentCollapsedIndicator({
  persistentNodes,
  currentGraphName,
}: PersistentCollapsedIndicatorProps) {
  const count = persistentNodes.size
  const [open, setOpen] = useState(false)
  const rootRef = useRef<HTMLDivElement>(null)

  const close = useCallback(() => setOpen(false), [])

  useEffect(() => {
    if (!open) return

    const onPointerDown = (e: PointerEvent) => {
      if (!rootRef.current?.contains(e.target as Node)) close()
    }
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') close()
    }

    document.addEventListener('pointerdown', onPointerDown)
    document.addEventListener('keydown', onKeyDown)
    return () => {
      document.removeEventListener('pointerdown', onPointerDown)
      document.removeEventListener('keydown', onKeyDown)
    }
  }, [open, close])

  useEffect(() => {
    if (count === 0) close()
  }, [count, close])

  if (count === 0) return null

  const title = `${count} persistent node${count === 1 ? '' : 's'} running — click to manage`

  return (
    <div className="persistent-collapsed-indicator" ref={rootRef}>
      <button
        type="button"
        className={`persistent-collapsed-indicator-btn persistent-collapsed-indicator-pulse${open ? ' persistent-collapsed-indicator-active' : ''}`}
        onClick={() => setOpen(v => !v)}
        onPointerDown={e => e.stopPropagation()}
        title={title}
        aria-expanded={open}
        aria-haspopup="dialog"
      >
        <PersistentRunningIcon size={18} />
        <span className="persistent-collapsed-indicator-badge">{count}</span>
      </button>
      {open && (
        <div
          className="persistent-collapsed-indicator-popover"
          role="dialog"
          aria-label="Persistent nodes"
          onPointerDown={e => e.stopPropagation()}
        >
          <PersistentNodesList
            persistentNodes={persistentNodes}
            currentGraphName={currentGraphName}
            showEmpty={false}
          />
        </div>
      )}
    </div>
  )
}
