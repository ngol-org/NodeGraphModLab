// ────────────────────────────────────────────────────────────────
// NGOL WebUI plugin sample (Form A: single no-build .js)
// Editor extension points:
//   - NGOL.extensions.registerMenu            : menubar menu
//   - NGOL.extensions.registerNodeContextMenuItems : node right-click items
//   - NGOL.extensions.registerOverlay          : canvas overlay
//   - NGOL.events.on                           : editor event hooks
//
// Deploy: copy this file to
//   <ngolRoot>/WebUI/plugins/sample-extensions.js
// Reload the WebUI (F5).
// ────────────────────────────────────────────────────────────────
const { React, html, extensions, events, log } = window.NGOL

// ---- 1. Event hooks: keep the latest event in a tiny shared store ----
let lastEvent = { type: '(none yet)', at: '' }
const lastEventListeners = new Set()
function setLastEvent(type) {
  lastEvent = { type, at: new Date().toLocaleTimeString() }
  lastEventListeners.forEach((cb) => cb())
}

let selectedIds = []
events.on('selection_changed', (p) => {
  selectedIds = (p && p.selectedNodeIds) || []
  setLastEvent(`selection_changed (${selectedIds.length})`)
})
events.on('snapshot_saved', () => setLastEvent('snapshot_saved'))
events.on('execution_finished', () => setLastEvent('execution_finished'))
events.on('graph_loaded', () => setLastEvent('graph_loaded'))

// ---- 2. Menubar menu ----
extensions.registerMenu({
  label: 'Sample Ext',
  items: [
    { label: 'Log Selected Nodes', onClick: () => log('selected:', selectedIds) },
    { separator: true },
    { label: 'Hello from plugin', onClick: () => alert('Hello from sample-extensions.js') },
  ],
})

// ---- 3. Node context menu items (per node-type filtering) ----
extensions.registerNodeContextMenuItems((ctx) => {
  // Show on every node; log full node context to the console
  return [{
    label: `Log Node Info (${ctx.nodeTypeId.split('.').pop()})`,
    onClick: () => log('node info:', ctx),
  }]
})

// ---- 4. Canvas overlay: shows last event + selection, follows viewport zoom ----
function SampleOverlay() {
  const [, force] = React.useReducer((x) => x + 1, 0)
  React.useEffect(() => {
    lastEventListeners.add(force)
    return () => lastEventListeners.delete(force)
  }, [])

  const { getZoom } = extensions.useReactFlow()

  return html`
    <div style=${{
      position: 'absolute', top: '8px', right: '8px', zIndex: 5,
      background: 'rgba(30,30,46,0.85)', border: '1px solid #444',
      borderRadius: '6px', padding: '6px 10px', fontSize: '11px',
      color: '#ccc', pointerEvents: 'none',
    }}>
      <div>last event: ${lastEvent.type} ${lastEvent.at}</div>
      <div>selected: ${selectedIds.length} node(s) / zoom: ${getZoom().toFixed(2)}</div>
    </div>`
}

extensions.registerOverlay({ id: 'sample.webui.ext-overlay', component: SampleOverlay })

log('sample-extensions loaded')
