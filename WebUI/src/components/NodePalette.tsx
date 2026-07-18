import { useState, useMemo } from 'react'
import type { NodeTypeInfo } from '../types/protocol'
import { groupAndSortNodes, type NodeSortMode } from '../lib/nodeSort'
import { NodeSortMenu } from './NodeSortMenu'
import './NodePalette.css'

interface Props { nodeTypes: NodeTypeInfo[]; embedded?: boolean; headerAction?: React.ReactNode }

export function NodePalette({ nodeTypes, embedded, headerAction }: Props) {
  const [search, setSearch] = useState('')
  const [sortMode, setSortMode] = useState<NodeSortMode>('category')

  const filtered = useMemo(() =>
    search
      ? nodeTypes.filter(n =>
          n.displayName.toLowerCase().includes(search.toLowerCase()) ||
          n.category.toLowerCase().includes(search.toLowerCase()) ||
          n.id.toLowerCase().includes(search.toLowerCase()))
      : nodeTypes
  , [nodeTypes, search])

  // category モードでもカテゴリ内は displayName 昇順にしておく
  const sortInput = useMemo(() =>
    [...filtered].sort((a, b) => a.displayName.localeCompare(b.displayName))
  , [filtered])

  const groups = useMemo(() => groupAndSortNodes(sortInput, sortMode), [sortInput, sortMode])

  const onDragStart = (e: React.DragEvent, typeId: string) => {
    e.dataTransfer.setData('application/node-type-id', typeId)
    e.dataTransfer.effectAllowed = 'copy'
  }

  const inner = (
    <>
      <div className="panel-header" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <span>Node Palette</span>
        {headerAction}
      </div>
      <div className="palette-search">
        <input
          type="text"
          placeholder="Search nodes…"
          value={search}
          onChange={e => setSearch(e.target.value)}
        />
        <NodeSortMenu mode={sortMode} onChange={setSortMode} />
      </div>
      <div className="palette-list">
        {nodeTypes.length === 0 && (
          <div style={{ padding: '12px', color: 'var(--text-dim)', fontSize: 11 }}>
            Connecting to server…
          </div>
        )}
        {groups.map(group => (
          <div key={group.category ?? '__flat__'}>
            {group.category && <div className="palette-category">{group.category}</div>}
            {group.nodes.map(node => (
              <div
                key={node.id}
                className="palette-item"
                draggable
                onDragStart={e => onDragStart(e, node.id)}
                title={node.description ?? node.id}
              >
                {node.displayName}
                {node.description && (
                  <div className="palette-item-desc">{node.description}</div>
                )}
              </div>
            ))}
          </div>
        ))}
      </div>
    </>
  )

  if (embedded) return inner
  return <aside className="palette">{inner}</aside>
}
