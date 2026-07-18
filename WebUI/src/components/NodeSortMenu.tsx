import { useState, useEffect, useRef } from 'react'
import type { NodeSortMode } from '../lib/nodeSort'
import { NgolIcon } from './icons/NgolIcon'
import './NodeSortMenu.css'

interface Props {
  mode: NodeSortMode
  onChange: (mode: NodeSortMode) => void
}

type SortField = 'category' | 'name' | 'modified'
type SortDirection = 'asc' | 'desc'

const SORT_FIELDS: { field: SortField; label: string; hasDirection: boolean }[] = [
  { field: 'category', label: 'Category', hasDirection: false },
  { field: 'name', label: 'Name', hasDirection: true },
  { field: 'modified', label: 'Modified', hasDirection: true },
]

function fieldOf(mode: NodeSortMode): SortField {
  if (mode === 'name-asc' || mode === 'name-desc') return 'name'
  if (mode === 'modified-asc' || mode === 'modified-desc') return 'modified'
  return 'category'
}

function directionOf(mode: NodeSortMode): SortDirection {
  return mode.endsWith('desc') ? 'desc' : 'asc'
}

function buildMode(field: SortField, direction: SortDirection): NodeSortMode {
  return field === 'category' ? 'category' : (`${field}-${direction}` as NodeSortMode)
}

export function NodeSortMenu({ mode, onChange }: Props) {
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)
  const activeField = fieldOf(mode)
  const activeDirection = directionOf(mode)

  useEffect(() => {
    if (!open) return
    const handler = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false)
    }
    document.addEventListener('mousedown', handler)
    return () => document.removeEventListener('mousedown', handler)
  }, [open])

  const selectField = (field: SortField) => {
    onChange(buildMode(field, field === activeField ? activeDirection : 'asc'))
    setOpen(false)
  }

  // クリック時に遷移する先の方向（非アクティブなfieldは常に昇順から開始）
  const nextDirectionOf = (field: SortField): SortDirection =>
    field === activeField && activeDirection === 'asc' ? 'desc' : 'asc'

  const toggleDirection = (field: SortField, e: React.MouseEvent) => {
    e.stopPropagation()
    onChange(buildMode(field, nextDirectionOf(field)))
    setOpen(false)
  }

  return (
    <div className="node-sort-menu" ref={ref}>
      <button
        type="button"
        className="node-sort-menu__trigger"
        onClick={() => setOpen(o => !o)}
        title="Sort nodes"
      >
        <NgolIcon name="sort" size={14} />
      </button>
      {open && (
        <div className="node-sort-menu__dropdown">
          {SORT_FIELDS.map(f => {
            const isActive = f.field === activeField
            // アイコンは「現在の状態」ではなく「クリックすると遷移する先」を表す
            const next: SortDirection = nextDirectionOf(f.field)
            return (
              <button
                key={f.field}
                type="button"
                className={`node-sort-menu__item${isActive ? ' node-sort-menu__item--active' : ''}`}
                onClick={() => selectField(f.field)}
              >
                <span>{f.label}</span>
                {f.hasDirection && (
                  <span
                    className="node-sort-menu__dir"
                    onClick={e => toggleDirection(f.field, e)}
                    title={next === 'desc' ? 'Click for descending' : 'Click for ascending'}
                  >
                    <NgolIcon name={next === 'asc' ? 'arrow-up' : 'arrow-down'} size={12} />
                  </span>
                )}
              </button>
            )
          })}
        </div>
      )}
    </div>
  )
}
