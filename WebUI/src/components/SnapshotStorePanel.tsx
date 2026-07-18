import type { SnapshotBadgeInfo } from '../hooks/useGraphEditor'
import { NgolIcon } from './icons/NgolIcon'
import './SnapshotStorePanel.css'

interface Props {
  savedSnapshots: Map<string, SnapshotBadgeInfo>
  onClearAll: () => void
  onClose: () => void
}

export function SnapshotStorePanel({ savedSnapshots, onClearAll, onClose }: Props) {
  const entries = Array.from(savedSnapshots.entries())

  return (
    <div className="snapshot-store-panel">
      <div className="snapshot-store-panel-header">
        <span className="snapshot-store-panel-title">Snapshot Store</span>
        <div className="snapshot-store-panel-actions">
          <button
            className="snapshot-store-clear-btn"
            onClick={onClearAll}
            disabled={entries.length === 0}
            title="Clear all snapshots"
          >
            Clear All
          </button>
          <button className="snapshot-store-close-btn" onClick={onClose} title="Close"><NgolIcon name="close" size={12} /></button>
        </div>
      </div>
      <div className="snapshot-store-panel-body">
        {entries.length === 0 ? (
          <div className="snapshot-store-empty">No snapshots saved</div>
        ) : (
          <table className="snapshot-store-table">
            <thead>
              <tr>
                <th>Node ID</th>
                <th>Port</th>
                <th>Type</th>
                <th>Value</th>
                <th>Time</th>
              </tr>
            </thead>
            <tbody>
              {entries.map(([nodeId, info]) => (
                <tr key={nodeId}>
                  <td className="snapshot-store-nodeid" title={nodeId}>{nodeId.slice(0, 8)}…</td>
                  <td>{info.portName}</td>
                  <td>{info.valueType}</td>
                  <td className="snapshot-store-value" title={info.valueString ?? ''}>{info.valueString ?? '—'}</td>
                  <td>{info.time}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  )
}
