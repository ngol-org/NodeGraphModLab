import { NgolIcon } from './icons/NgolIcon'
import type { NodeGroup } from '../types/protocol'

export interface GroupContextMenuState {
  groupId: string
  x: number
  y: number
  renaming?: boolean
  editingDescription?: boolean
  confirmDissolve?: boolean
}

interface GroupContextMenuProps {
  groupContextMenu: GroupContextMenuState
  setGroupContextMenu: React.Dispatch<React.SetStateAction<GroupContextMenuState | null>>
  groupsRef: React.RefObject<NodeGroup[]>
  toggleCollapsed: (id: string) => void
  renameGroup: (id: string, newName: string) => void
  updateGroupDescription: (id: string, description: string) => void
  deleteGroup: (id: string) => void
}

export function GroupContextMenu({
  groupContextMenu,
  setGroupContextMenu,
  groupsRef,
  toggleCollapsed,
  renameGroup,
  updateGroupDescription,
  deleteGroup,
}: GroupContextMenuProps) {
  const group = groupsRef.current?.find(g => g.id === groupContextMenu.groupId)
  if (!group) return null

  return (
    <div
      className="node-context-menu"
      style={{ left: groupContextMenu.x, top: groupContextMenu.y }}
      onMouseDown={e => e.stopPropagation()}
    >
      <button
        className="node-context-menu-item"
        onClick={() => { toggleCollapsed(group.id); setGroupContextMenu(null) }}
      >
        <span className="ngol-btn-with-icon">
          <NgolIcon name={group.collapsed ? 'chevron-right' : 'chevron-down'} size={14} className="ngol-icon" />
          {group.collapsed ? 'Expand' : 'Collapse'}
        </span>
      </button>
      {groupContextMenu.renaming ? (
        <div style={{ padding: '4px 8px' }}>
          <input
            autoFocus
            className="group-node-rename-input"
            defaultValue={group.name}
            onKeyDown={e => {
              e.stopPropagation()
              if (e.key === 'Enter') {
                const v = e.currentTarget.value.trim()
                if (v) renameGroup(group.id, v)
                setGroupContextMenu(null)
              }
              if (e.key === 'Escape') setGroupContextMenu(null)
            }}
            onBlur={e => {
              const v = e.currentTarget.value.trim()
              if (v) renameGroup(group.id, v)
              setGroupContextMenu(null)
            }}
            onMouseDown={e => e.stopPropagation()}
          />
        </div>
      ) : (
        <button
          className="node-context-menu-item"
          onClick={() => setGroupContextMenu(prev => prev ? { ...prev, renaming: true } : null)}
        >
          ✏ Rename group
        </button>
      )}
      {groupContextMenu.editingDescription ? (
        <div style={{ padding: '4px 8px' }}>
          <textarea
            autoFocus
            className="group-node-rename-input"
            style={{ width: '100%', minHeight: 60, resize: 'vertical', fontFamily: 'inherit', fontSize: 12 }}
            defaultValue={group.description ?? ''}
            placeholder="Description (optional)"
            onKeyDown={e => {
              e.stopPropagation()
              if (e.key === 'Escape') setGroupContextMenu(null)
            }}
            onBlur={e => {
              updateGroupDescription(group.id, e.currentTarget.value)
              setGroupContextMenu(null)
            }}
            onMouseDown={e => e.stopPropagation()}
          />
        </div>
      ) : (
        <button
          className="node-context-menu-item"
          onClick={() => setGroupContextMenu(prev => prev ? { ...prev, editingDescription: true } : null)}
        >
          📝 Edit description
        </button>
      )}
      <div className="node-context-menu-separator" />
      <button
        className="node-context-menu-item node-context-menu-item-danger"
        onClick={() => { deleteGroup(group.id); setGroupContextMenu(null) }}
      >
        <span className="ngol-btn-with-icon"><NgolIcon name="minus" size={12} className="ngol-icon" /> Dissolve group</span>
      </button>
    </div>
  )
}
