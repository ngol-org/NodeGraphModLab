// ────────────────────────────────────────────────────────────────
// NGOL WebUI plugin sample (Form A: single no-build .js)
// Node-renderer tier: draws a live scrolling waveform on a Canvas.
// Samples arrive via the standard Snapshot push path (PushLiveValue →
// snapshot_saved), no new WS message type required.
//
// Deploy: copy this file to
//   <ngolRoot>/WebUI/plugins/sample-live-waveform.js
// Reload the WebUI (F5). Nodes declaring
//   [NodeWebUi("sample.webui.waveform")]
// will be rendered entirely by this plugin.
// ────────────────────────────────────────────────────────────────
const { React, html, registerNodeRenderer } = window.NGOL

const WIDTH = 220
const HEIGHT = 90
const MAX_SAMPLES = 90
const LINE_COLOR = '#3987e5'
const FILL_COLOR = 'rgba(57, 135, 229, 0.18)'
const GRID_COLOR = 'rgba(255, 255, 255, 0.08)'
const BASELINE_COLOR = '#444'

function drawWaveform(canvas, buffer) {
  if (!canvas) return
  const ctx = canvas.getContext('2d')
  ctx.clearRect(0, 0, WIDTH, HEIGHT)

  // recessive grid lines
  ctx.strokeStyle = GRID_COLOR
  ctx.lineWidth = 1
  ;[0.25, 0.5, 0.75].forEach((f) => {
    ctx.beginPath()
    ctx.moveTo(0, HEIGHT * f)
    ctx.lineTo(WIDTH, HEIGHT * f)
    ctx.stroke()
  })

  if (buffer.length >= 2) {
    const mid = HEIGHT / 2
    const scale = mid - 8
    const step = WIDTH / (MAX_SAMPLES - 1)
    const startX = WIDTH - (buffer.length - 1) * step

    ctx.beginPath()
    buffer.forEach((v, i) => {
      const x = startX + i * step
      const y = mid - Math.max(-1, Math.min(1, v)) * scale
      if (i === 0) ctx.moveTo(x, y)
      else ctx.lineTo(x, y)
    })
    ctx.strokeStyle = LINE_COLOR
    ctx.lineWidth = 2
    ctx.lineJoin = 'round'
    ctx.stroke()

    // area fill under the line
    const lastX = startX + (buffer.length - 1) * step
    ctx.lineTo(lastX, HEIGHT)
    ctx.lineTo(startX, HEIGHT)
    ctx.closePath()
    ctx.fillStyle = FILL_COLOR
    ctx.fill()
  }

  // baseline
  ctx.strokeStyle = BASELINE_COLOR
  ctx.lineWidth = 1
  ctx.beginPath()
  ctx.moveTo(0, HEIGHT / 2)
  ctx.lineTo(WIDTH, HEIGHT / 2)
  ctx.stroke()
}

function LiveWaveformNode({ nodeTypeInfo, snapshotBadgesByPort }) {
  const canvasRef = React.useRef(null)
  const bufferRef = React.useRef([])
  const [isLive, setIsLive] = React.useState(false)
  const badge = snapshotBadgesByPort?.value

  React.useEffect(() => {
    if (!badge) return
    const v = parseFloat(badge.valueString)
    if (Number.isNaN(v)) return

    const buf = bufferRef.current
    buf.push(v)
    if (buf.length > MAX_SAMPLES) buf.shift()
    drawWaveform(canvasRef.current, buf)
    setIsLive(true)
  }, [badge?.time, badge?.valueString])

  return html`
    <div style=${{ padding: '6px 10px' }}>
      <div style=${{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '4px' }}>
        <span style=${{ fontSize: '11px', fontWeight: 600, color: '#e0e0e0' }}>${nodeTypeInfo.displayName}</span>
        <span style=${{ fontSize: '10px', color: isLive ? LINE_COLOR : '#888' }}>${isLive ? '● live' : 'run to start'}</span>
      </div>
      <canvas
        ref=${canvasRef}
        width=${WIDTH}
        height=${HEIGHT}
        className="nodrag"
        style=${{
          display: 'block',
          background: '#26263a',
          border: '1px solid #444',
          borderRadius: '6px',
        }}
      />
    </div>`
}

registerNodeRenderer('sample.webui.waveform', LiveWaveformNode, {
  // portLayout を指定すると既定の自動スプレッド配置は使われず完全に置き換わるため、
  // frequency/amplitude を明示しないと両方とも既定値(PORT_BASE)に重なって描画される。
  // 注意: top=48px は exec_in/exec_out 専用の固定座標（EXEC_TOP、portLayoutでは
  // 変更不可）と衝突するため使わないこと。データポートの既定開始位置は68px(PORT_BASE)、
  // 以降26px間隔（WebUI/src/components/CustomNode.tsx 参照）。
  // TODO: 座標を手計算する代わりに NGOL.layout.autoPortLayout(nodeTypeInfo) を使う書き方に
  //       更新できる: `{ ...NGOL.layout.autoPortLayout(nodeTypeInfo), outputs: { value: 128 } }`
  //       （既定値を自動計算し value だけ上書き）。このサンプルをコピーして新しいノードを
  //       作る場合は、手計算ではなくヘルパーを使うこと（webui-plugin-guide.md §3.5参照）
  portLayout: () => ({
    inputs: { frequency: 68, amplitude: 94 },
    outputs: { value: 128 },
  }),
})
window.NGOL.log('sample-live-waveform loaded')
