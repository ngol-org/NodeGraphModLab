import { useCallback, useEffect, useRef, useState } from 'react'
import { NgolIcon } from './icons/NgolIcon'
import { NodePalette } from './NodePalette'
import { GraphListPanel } from './GraphListPanel'
import './LeftColumnCollapsedIndicators.css'
import type { NodeGraphData, NodeTypeInfo } from '../types/protocol'

type OpenPanel = 'palette' | 'graphs' | null

interface LeftColumnCollapsedIndicatorsProps {
  nodeTypes: NodeTypeInfo[]
  onLoad: (graph: NodeGraphData) => void
  onImportAsFragment?: (graph: NodeGraphData) => void
  externalLoadActive?: boolean
}

export function LeftColumnCollapsedIndicators({
  nodeTypes,
  onLoad,
  onImportAsFragment,
  externalLoadActive,
}: LeftColumnCollapsedIndicatorsProps) {
  const [open, setOpen] = useState<OpenPanel>(null)
  const rootRef = useRef<HTMLDivElement>(null)

  const close = useCallback(() => setOpen(null), [])

  const toggle = useCallback((panel: OpenPanel) => {
    setOpen(prev => (prev === panel ? null : panel))
  }, [])

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

  return (
    <div className="left-column-collapsed-indicators" ref={rootRef}>
      <div className="left-column-collapsed-indicator-slot left-column-collapsed-indicator-slot--node">
        <button
          type="button"
          className={`left-column-collapsed-indicator-btn left-column-collapsed-indicator-btn--node${open === 'palette' ? ' left-column-collapsed-indicator-active' : ''}`}
          onClick={() => toggle('palette')}
          onPointerDown={e => e.stopPropagation()}
          title="Node Palette — search and drag nodes"
          aria-expanded={open === 'palette'}
          aria-haspopup="dialog"
        >
          <NgolIcon name="plus" size={16} />
        </button>
        {open === 'palette' && (
          <div
            className="left-column-collapsed-popover left-column-collapsed-popover--node"
            role="dialog"
            aria-label="Node Palette"
            onPointerDown={e => e.stopPropagation()}
          >
            <NodePalette nodeTypes={nodeTypes} embedded={true} />
          </div>
        )}
      </div>

      <div className="left-column-collapsed-indicator-slot left-column-collapsed-indicator-slot--graph">
        <button
          type="button"
          className={`left-column-collapsed-indicator-btn left-column-collapsed-indicator-btn--graph${open === 'graphs' ? ' left-column-collapsed-indicator-active' : ''}`}
          onClick={() => toggle('graphs')}
          onPointerDown={e => e.stopPropagation()}
          title="Saved Graphs — load or delete graphs"
          aria-expanded={open === 'graphs'}
          aria-haspopup="dialog"
        >
          <NgolIcon name="folder" size={16} />
        </button>
        {open === 'graphs' && (
          <div
            className="left-column-collapsed-popover left-column-collapsed-popover--graph"
            role="dialog"
            aria-label="Saved Graphs"
            onPointerDown={e => e.stopPropagation()}
          >
            <GraphListPanel
              onLoad={onLoad}
              onImportAsFragment={onImportAsFragment}
              externalLoadActive={externalLoadActive}
            />
          </div>
        )}
      </div>
    </div>
  )
}
