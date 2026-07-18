import { useState, useEffect } from 'react'
import { wsClient } from '../lib/wsClient'
import { getFragmentIdForNode } from '../lib/fragmentUtils'
import { NgolIcon } from './icons/NgolIcon'
import { PersistentNodesList } from './PersistentNodesList'
import './FragmentPanel.css'
import type { FragmentDefinition, PersistentNodeInfo } from '../types/protocol'

interface FragmentPanelProps {
  fragments: FragmentDefinition[]
  pinnedIds: Set<string>
  selectedNodeId: string | null
  onPinnedIdsChange: (ids: Set<string>) => void
  onExecuteFragment: (fragmentId: string, pinnedIds: string[]) => void
  onExecuteAll: (pinnedIds: string[]) => void
  persistentNodes: Map<string, PersistentNodeInfo>
  currentGraphName: string
}

export function FragmentPanel({
  fragments,
  pinnedIds,
  selectedNodeId,
  onPinnedIdsChange,
  onExecuteFragment,
  onExecuteAll,
  persistentNodes,
  currentGraphName,
}: FragmentPanelProps) {
  const [executedIds, setExecutedIds] = useState<Set<string>>(new Set())

  useEffect(() => {
    return wsClient.onMessage(msg => {
      if (msg.type === 'execution_result' && msg.fragmentId) {
        if (msg.success) {
          setExecutedIds(prev => new Set([...prev, msg.fragmentId!]))
        }
      }
    })
  }, [])

  const togglePin = (id: string) => {
    onPinnedIdsChange(new Set(
      pinnedIds.has(id)
        ? [...pinnedIds].filter(x => x !== id)
        : [...pinnedIds, id]
    ))
  }

  const pinnedArr = Array.from(pinnedIds)

  const selectedFragmentId = selectedNodeId ? getFragmentIdForNode(selectedNodeId, fragments) : null
  const selectedFragmentName = fragments.find(f => f.id === selectedFragmentId)?.name ?? null

  return (
    <div className="fragment-panel">
      {/* Fragment ヘッダー */}
      <div className="panel-header" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <span>Fragments</span>
        <div style={{ display: 'flex', gap: 4 }}>
          <button
            className="ngol-btn-with-icon"
            style={{ padding: '2px 8px', fontSize: 11 }}
            onClick={() => { if (selectedFragmentId) onExecuteFragment(selectedFragmentId, pinnedArr) }}
            disabled={!selectedFragmentId}
            title={selectedFragmentName ? `Run "${selectedFragmentName}"` : 'Select a node to run its fragment'}
          >
            <NgolIcon name="play" size={11} className="ngol-icon" /> Run
          </button>
          <button
            className="ngol-btn-with-icon"
            style={{ padding: '2px 8px', fontSize: 11 }}
            onClick={() => onExecuteAll(pinnedArr)}
            disabled={fragments.length === 0}
            title="Run all unpinned fragments in topological order"
          >
            <NgolIcon name="double-play" size={11} className="ngol-icon" /> All
          </button>
        </div>
      </div>

      {/* Fragment 一覧 */}
      <div className="fragment-list">
        {fragments.length === 0 && (
          <div className="graph-list-empty">No fragments</div>
        )}
        {fragments.map(f => {
          const isPinned = pinnedIds.has(f.id)
          const isExecuted = executedIds.has(f.id)
          return (
            <div key={f.id} className="fragment-item">
              <div style={{ display: 'flex', alignItems: 'center', gap: 4, flex: 1, minWidth: 0 }}>
                <span
                  className="fragment-item-name"
                  title={`ID: ${f.id}`}
                >
                  {f.name}
                </span>
                <span style={{ fontSize: 10, color: 'var(--text-dim)', flexShrink: 0 }}>
                  {f.nodeInstanceIds.length}N
                </span>
              </div>
              <div style={{ display: 'flex', gap: 2, flexShrink: 0, alignItems: 'center' }}>
                {isPinned && <span className="fragment-badge fragment-badge-pin">PIN</span>}
                {isExecuted && !isPinned && <span className="fragment-badge fragment-badge-done">DONE</span>}
                <button
                  className="fragment-icon-btn"
                  onClick={() => togglePin(f.id)}
                  title={isPinned ? 'Unpin (include in full run)' : 'Pin (skip in full run)'}
                >
                  {isPinned ? '🔒' : '🔓'}
                </button>
                <button
                  className="fragment-icon-btn"
                  onClick={() => onExecuteFragment(f.id, pinnedArr)}
                  title="Run this fragment"
                >
                  <NgolIcon name="play" size={11} />
                </button>
              </div>
            </div>
          )
        })}
      </div>

      <PersistentNodesList
        persistentNodes={persistentNodes}
        currentGraphName={currentGraphName}
      />
    </div>
  )
}
