// ────────────────────────────────────────────────────────────────
// NGOL WebUI plugin sample (Form A: single no-build .js)
// Widget tier: frequency/amplitude sliders that stream live to
// whatever node this one is connected to (see WaveformControlNode.cs).
//
// Deploy: copy this file to
//   <ngolRoot>/WebUI/plugins/sample-waveform-control.js
// Reload the WebUI (F5). Nodes declaring
//   [NodeWebUi("sample.webui.waveform_control")]
// will render this widget.
// ────────────────────────────────────────────────────────────────
const { React, html, registerWidget } = window.NGOL

const FREQ_MIN = 0.2, FREQ_MAX = 5.0, FREQ_STEP = 0.1
const AMP_MIN = 0.1, AMP_MAX = 1.5, AMP_STEP = 0.05

function sliderRow(label, value, min, max, step, onChange) {
  return html`
    <div style=${{ marginBottom: '6px' }}>
      <div style=${{ display: 'flex', justifyContent: 'space-between', fontSize: '10px', color: '#aaa' }}>
        <span>${label}</span>
        <span>${Number(value).toFixed(2)}</span>
      </div>
      <input
        type="range"
        className="nodrag"
        min=${min} max=${max} step=${step} value=${value}
        onInput=${(e) => onChange(parseFloat(e.target.value))}
        style=${{ width: '100%' }}
      />
    </div>`
}

function WaveformControlWidget({ nodeId, paramValues, onParamChange }) {
  const frequency = parseFloat(paramValues.frequency ?? '1.3')
  const amplitude = parseFloat(paramValues.amplitude ?? '0.6')

  const update = (name, value) => {
    onParamChange(name, value.toFixed(3))
    // 実行中の永続ノードへリアルタイム反映（GetLiveParam で毎フレーム読まれる）
    window.NGOL.pushLiveParams(nodeId, { [name]: value })
  }

  return html`
    <div style=${{ padding: '6px 10px', width: '200px' }}>
      ${sliderRow('frequency', frequency, FREQ_MIN, FREQ_MAX, FREQ_STEP, (v) => update('frequency', v))}
      ${sliderRow('amplitude', amplitude, AMP_MIN, AMP_MAX, AMP_STEP, (v) => update('amplitude', v))}
    </div>`
}

registerWidget('sample.webui.waveform_control', WaveformControlWidget)
window.NGOL.log('sample-waveform-control loaded')
