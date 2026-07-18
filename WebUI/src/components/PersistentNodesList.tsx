import { wsClient } from '../lib/wsClient'
import './FragmentPanel.css'
import type { PersistentNodeInfo } from '../types/protocol'

interface PersistentNodesListProps {
  persistentNodes: Map<string, PersistentNodeInfo>
  currentGraphName: string
  showEmpty?: boolean
}

export function PersistentNodesList({
  persistentNodes,
  currentGraphName,
  showEmpty = true,
}: PersistentNodesListProps) {
  return (
    <div className="persistent-nodes-section">
      <div className="persistent-nodes-header">
        <span className="persistent-nodes-title">Persistent Nodes</span>
        <button
          style={{ padding: '2px 8px', fontSize: 11 }}
          onClick={() => wsClient.stopPersistent()}
          disabled={persistentNodes.size === 0}
          title="Stop all running persistent nodes"
        >
          ■ Stop All
        </button>
      </div>
      {persistentNodes.size === 0 ? (
        showEmpty ? (
          <div className="graph-list-empty">No persistent nodes running</div>
        ) : null
      ) : (
        <div className="fragment-list">
          {Array.from(persistentNodes.values()).map(node => {
            const isExternal = node.graphName !== currentGraphName
            return (
              <div
                key={node.nodeInstanceId}
                className={`persistent-node-row${isExternal ? ' persistent-node-row--external' : ''}`}
                title={isExternal ? `Running in graph "${node.graphName}"` : undefined}
              >
                <div className="persistent-node-info">
                  <div style={{ display: 'flex', alignItems: 'center', gap: 3, minWidth: 0 }}>
                    <span className="persistent-node-label" title={node.nodeInstanceId}>
                      {node.displayName || node.nodeInstanceId}
                    </span>
                    {isExternal && (
                      <span className="persistent-node-ext-badge">EXT</span>
                    )}
                  </div>
                  {node.graphName && (
                    <span className="persistent-node-graph">{node.graphName}</span>
                  )}
                </div>
                <button
                  className="fragment-icon-btn"
                  onClick={() => wsClient.stopPersistentNode(node.nodeInstanceId)}
                  title="Stop this persistent node"
                >
                  ■
                </button>
              </div>
            )
          })}
        </div>
      )}
    </div>
  )
}
