// ────────────────────────────────────────────────────────────────
// NGOL WebUI plugin sample (Form A: single no-build .js)
// Widget tier: renders a gauge bar inside the standard node frame.
//
// Deploy: copy this file to
//   <ngolRoot>/WebUI/plugins/sample-gauge.js
// Reload the WebUI (F5). Nodes declaring
//   [NodeWebUi("sample.webui.gauge", ...)]
// will render this widget.
// ────────────────────────────────────────────────────────────────
const { React, html, registerWidget } = window.NGOL

function GaugeWidget({ spec, snapshotBadge }) {
  const value = parseFloat(snapshotBadge?.valueString ?? '')
  const max = parseFloat((spec.extra && spec.extra.max) || '100')

  if (isNaN(value)) {
    return html`<div style=${{ padding: '4px 8px', fontSize: '10px', color: '#888' }}>
      No value — run graph first
    </div>`
  }

  const pct = Math.min(100, Math.max(0, (value / max) * 100))
  const barColor = pct < 60 ? '#4caf50' : pct < 85 ? '#ff9800' : '#e94560'

  return html`
    <div style=${{ padding: '4px 8px' }}>
      <div style=${{ background: '#333', borderRadius: '4px', height: '10px', overflow: 'hidden' }}>
        <div style=${{ width: pct + '%', background: barColor, height: '100%' }} />
      </div>
      <div style=${{ fontSize: '10px', color: '#aaa', marginTop: '2px', textAlign: 'right' }}>
        ${value} / ${max}
      </div>
    </div>`
}

registerWidget('sample.webui.gauge', GaugeWidget)
window.NGOL.log('sample-gauge loaded')
