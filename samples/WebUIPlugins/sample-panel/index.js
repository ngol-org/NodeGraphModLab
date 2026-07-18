// ────────────────────────────────────────────────────────────────
// NGOL WebUI plugin sample (Form B: folder + plugin.json)
// Panel tier: adds a "Snapshot Watch" entry to the Plugins menu.
// Subscribes to snapshot_saved pushes via NGOL.ws and lists them.
//
// Deploy: copy this folder to
//   <ngolRoot>/WebUI/plugins/sample-panel/
// Reload the WebUI (F5), then open it from the Plugins menu.
// ────────────────────────────────────────────────────────────────
const { React, html, registerPanel, ws } = window.NGOL

const MAX_ENTRIES = 30

function SnapshotWatchPanel() {
  const [entries, setEntries] = React.useState([])

  React.useEffect(() => {
    // onMessage は購読解除関数を返すので、そのまま effect の cleanup にできる
    return ws.onMessage((msg) => {
      if (msg.type !== 'snapshot_saved') return
      setEntries((prev) => [
        {
          time: new Date().toLocaleTimeString(),
          nodeId: String(msg.nodeInstanceId ?? '?'),
          port: String(msg.portName ?? '?'),
          value: msg.valueString == null ? '—' : String(msg.valueString),
        },
        ...prev,
      ].slice(0, MAX_ENTRIES))
    })
  }, [])

  if (entries.length === 0) {
    return html`<div style=${{ color: '#888' }}>
      Waiting for snapshots... Run a graph containing Snapshot nodes.
    </div>`
  }

  return html`
    <table style=${{ width: '100%', borderCollapse: 'collapse', fontSize: '11px' }}>
      <thead>
        <tr style=${{ color: '#888', textAlign: 'left' }}>
          <th style=${{ padding: '2px 6px' }}>Time</th>
          <th style=${{ padding: '2px 6px' }}>Node</th>
          <th style=${{ padding: '2px 6px' }}>Port</th>
          <th style=${{ padding: '2px 6px' }}>Value</th>
        </tr>
      </thead>
      <tbody>
        ${entries.map((e, i) => html`
          <tr key=${i} style=${{ borderTop: '1px solid #333' }}>
            <td style=${{ padding: '2px 6px', whiteSpace: 'nowrap' }}>${e.time}</td>
            <td style=${{ padding: '2px 6px', fontFamily: 'monospace' }} title=${e.nodeId}>${e.nodeId.slice(0, 8)}</td>
            <td style=${{ padding: '2px 6px' }}>${e.port}</td>
            <td style=${{ padding: '2px 6px', maxWidth: '160px', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }} title=${e.value}>${e.value}</td>
          </tr>`)}
      </tbody>
    </table>`
}

registerPanel({
  id: 'sample.webui.snapshot-watch',
  title: 'Snapshot Watch',
  component: SnapshotWatchPanel,
})
window.NGOL.log('sample-panel loaded')
