// ────────────────────────────────────────────────────────────────
// NGOL WebUI plugin sample (Form A: single no-build .js)
// Node-renderer tier: replaces the entire node body with an XY pad.
// NGOL still manages the outer frame and the port Handles;
// portLayout aligns the output Handles with this component's rows.
//
// Deploy: copy this file to
//   <ngolRoot>/WebUI/plugins/sample-fullnode.js
// Reload the WebUI (F5). Nodes declaring
//   [NodeWebUi("sample.webui.xypad")]
// will be rendered entirely by this plugin.
// ────────────────────────────────────────────────────────────────
const { React, html, registerNodeRenderer } = window.NGOL

const PAD_SIZE = 120

function XyPadNode({ nodeTypeInfo, paramValues, onParamChange, runFragment }) {
  const x = Math.min(1, Math.max(0, parseFloat(paramValues.x ?? '0.5') || 0))
  const y = Math.min(1, Math.max(0, parseFloat(paramValues.y ?? '0.5') || 0))

  const handlePointer = (e) => {
    const rect = e.currentTarget.getBoundingClientRect()
    const nx = Math.min(1, Math.max(0, (e.clientX - rect.left) / rect.width))
    const ny = Math.min(1, Math.max(0, (e.clientY - rect.top) / rect.height))
    onParamChange('x', nx.toFixed(3))
    onParamChange('y', ny.toFixed(3))
  }

  const onPointerDown = (e) => {
    e.stopPropagation()
    e.currentTarget.setPointerCapture(e.pointerId)
    handlePointer(e)
  }
  const onPointerMove = (e) => {
    if (e.buttons & 1) handlePointer(e)
  }

  return html`
    <div style=${{ padding: '6px 10px', width: PAD_SIZE + 40 + 'px' }}>
      <div style=${{ fontSize: '11px', fontWeight: 600, color: '#e0e0e0', marginBottom: '4px' }}>
        ${nodeTypeInfo.displayName}
      </div>
      <div
        className="nodrag"
        style=${{
          width: PAD_SIZE + 'px', height: PAD_SIZE + 'px', margin: '0 auto',
          background: '#26263a', border: '1px solid #444', borderRadius: '6px',
          position: 'relative', cursor: 'crosshair', touchAction: 'none',
        }}
        onPointerDown=${onPointerDown}
        onPointerMove=${onPointerMove}
        onDoubleClick=${runFragment}
      >
        <div style=${{
          position: 'absolute',
          left: x * PAD_SIZE - 5 + 'px', top: y * PAD_SIZE - 5 + 'px',
          width: '10px', height: '10px', borderRadius: '50%',
          background: '#e94560', pointerEvents: 'none',
        }} />
      </div>
      <div style=${{ display: 'flex', justifyContent: 'space-between', fontSize: '10px', color: '#aaa', marginTop: '4px' }}>
        <span>x: ${x.toFixed(3)}</span>
        <span>y: ${y.toFixed(3)}</span>
      </div>
    </div>`
}

registerNodeRenderer('sample.webui.xypad', XyPadNode, {
  // Handle 縦位置を x / y の表示行付近に揃える
  // TODO: NGOL.layout.autoPortLayout(nodeTypeInfo) で既定配置を取得し必要な箇所だけ
  //       上書きする書き方に統一できる（このノードは x/y ともカスタム位置のため差分は小さいが、
  //       将来ポートを追加する際は手計算せずヘルパーを使うこと。webui-plugin-guide.md §3.5参照）
  portLayout: () => ({
    inputs: {},
    outputs: { x: 160, y: 176 },
  }),
})
window.NGOL.log('sample-fullnode loaded')
