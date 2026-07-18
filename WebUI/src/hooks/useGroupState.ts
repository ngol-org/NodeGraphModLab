import { useState, useCallback, useRef } from 'react'
import type { NodeGroup } from '../types/protocol'
import type { GraphCommand } from './useGraphHistory'

type PushHistory = (cmd: GraphCommand) => void

export interface GroupStateActions {
  groups: NodeGroup[]
  createGroup: (name: string, nodeIds: string[]) => void
  deleteGroup: (groupId: string) => void
  renameGroup: (groupId: string, newName: string) => void
  updateGroupDescription: (groupId: string, description: string) => void
  toggleCollapsed: (groupId: string) => void
  addNodesToGroup: (groupId: string, nodeIds: string[]) => void
  removeNodeFromGroup: (groupId: string, nodeId: string) => void
  resetGroups: (initial: NodeGroup[]) => void
}

export function useGroupState(pushHistory: PushHistory): GroupStateActions {
  const [groups, setGroups] = useState<NodeGroup[]>([])
  const groupsRef = useRef<NodeGroup[]>([])
  groupsRef.current = groups

  const createGroup = useCallback((name: string, nodeIds: string[]) => {
    const newGroup: NodeGroup = {
      id: crypto.randomUUID(),
      name,
      nodeInstanceIds: nodeIds,
      collapsed: false,
    }
    pushHistory({
      label: 'グループ化',
      do: () => setGroups(prev => [...prev, newGroup]),
      undo: () => setGroups(prev => prev.filter(g => g.id !== newGroup.id)),
    })
  }, [pushHistory])

  const deleteGroup = useCallback((groupId: string) => {
    const group = groupsRef.current.find(g => g.id === groupId)
    if (!group) return
    const snapshot: NodeGroup = { ...group, nodeInstanceIds: [...group.nodeInstanceIds] }
    pushHistory({
      label: 'グループ解除',
      do: () => setGroups(prev => prev.filter(g => g.id !== groupId)),
      undo: () => setGroups(prev => [...prev, snapshot]),
    })
  }, [pushHistory])

  const renameGroup = useCallback((groupId: string, newName: string) => {
    const group = groupsRef.current.find(g => g.id === groupId)
    if (!group) return
    const oldName = group.name
    pushHistory({
      label: 'グループ名変更',
      do: () => setGroups(prev => prev.map(g => g.id === groupId ? { ...g, name: newName } : g)),
      undo: () => setGroups(prev => prev.map(g => g.id === groupId ? { ...g, name: oldName } : g)),
    })
  }, [pushHistory])

  const updateGroupDescription = useCallback((groupId: string, description: string) => {
    const group = groupsRef.current.find(g => g.id === groupId)
    if (!group) return
    const oldDesc = group.description
    pushHistory({
      label: 'グループ説明変更',
      do: () => setGroups(prev => prev.map(g => g.id === groupId ? { ...g, description } : g)),
      undo: () => setGroups(prev => prev.map(g => g.id === groupId ? { ...g, description: oldDesc } : g)),
    })
  }, [pushHistory])

  const toggleCollapsed = useCallback((groupId: string) => {
    const group = groupsRef.current.find(g => g.id === groupId)
    if (!group) return
    const newCollapsed = !group.collapsed
    pushHistory({
      label: newCollapsed ? '折りたたみ' : '展開',
      do: () => setGroups(prev => prev.map(g => g.id === groupId ? { ...g, collapsed: newCollapsed } : g)),
      undo: () => setGroups(prev => prev.map(g => g.id === groupId ? { ...g, collapsed: !newCollapsed } : g)),
    })
  }, [pushHistory])

  const addNodesToGroup = useCallback((groupId: string, nodeIds: string[]) => {
    pushHistory({
      label: 'グループに追加',
      do: () => setGroups(prev => prev.map(g =>
        g.id === groupId
          ? { ...g, nodeInstanceIds: [...g.nodeInstanceIds, ...nodeIds.filter(id => !g.nodeInstanceIds.includes(id))] }
          : g
      )),
      undo: () => setGroups(prev => prev.map(g =>
        g.id === groupId
          ? { ...g, nodeInstanceIds: g.nodeInstanceIds.filter(id => !nodeIds.includes(id)) }
          : g
      )),
    })
  }, [pushHistory])

  const removeNodeFromGroup = useCallback((groupId: string, nodeId: string) => {
    pushHistory({
      label: 'グループから除外',
      do: () => setGroups(prev => prev.map(g =>
        g.id === groupId
          ? { ...g, nodeInstanceIds: g.nodeInstanceIds.filter(id => id !== nodeId) }
          : g
      )),
      undo: () => setGroups(prev => prev.map(g =>
        g.id === groupId ? { ...g, nodeInstanceIds: [...g.nodeInstanceIds, nodeId] } : g
      )),
    })
  }, [pushHistory])

  const resetGroups = useCallback((initial: NodeGroup[]) => {
    setGroups(initial)
  }, [])

  return {
    groups,
    createGroup,
    deleteGroup,
    renameGroup,
    updateGroupDescription,
    toggleCollapsed,
    addNodesToGroup,
    removeNodeFromGroup,
    resetGroups,
  }
}
