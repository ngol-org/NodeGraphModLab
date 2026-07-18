// ────────────────────────────────────────────────────────────────
// NGOL WebUI plugin sample — Text Box (node type ID override)
//
// Target: ngol.logic.text_box (builtin node, NodeGraphModLab.BuiltinNodes/Logic/TextBoxNode.cs)
// The builtin node has no [NodeWebUi] declaration — it works standalone with the
// standard compact rendering. This plugin overrides its rendering by node type ID
// (registerNodeRendererOverride) to show the "text" value as a full-node
// editable text box. Without this plugin, the node still executes normally.
//
// Deploy: copy this file to
//   <ngolRoot>/WebUI/plugins/text-box-nodeui.js
// Reload the WebUI (F5).
// ────────────────────────────────────────────────────────────────
const { React, html, registerNodeRendererOverride, setSnapshotValue } = window.NGOL

const stopDrag = (e) => e.stopPropagation()

function TextBoxUi({ nodeId, paramValues, onParamChange, snapshotBadgesByPort }) {
  const executedValue = snapshotBadgesByPort?.text?.valueString
  const initial = executedValue ?? String(paramValues.text ?? '')

  const [value, setValue] = React.useState(initial)
  const [dirty, setDirty] = React.useState(false)

  // 編集中でなければ、実行結果 (Execute() → SetSnapshot) または paramValues の更新に追従する
  React.useEffect(() => {
    if (!dirty) setValue(initial)
    // eslint-disable-next-line
  }, [initial])

  const commit = () => {
    onParamChange('text', value)
    setSnapshotValue(nodeId, 'text', value)
    setDirty(false)
  }

  const onKeyDown = (e) => {
    if (e.key === 'Enter' && e.ctrlKey) {
      e.preventDefault()
      commit()
    }
  }

  return html`
    <div style=${{ padding: '4px 10px 6px' }}>
      <textarea
        className="nodrag nowheel"
        value=${value}
        onChange=${(e) => { setValue(e.target.value); setDirty(true) }}
        onBlur=${commit}
        onKeyDown=${onKeyDown}
        onPointerDown=${stopDrag}
        onMouseDown=${stopDrag}
        placeholder="Type text here..."
        style=${{
          display: 'block',
          width: '100%',
          minHeight: '80px',
          resize: 'both',
          boxSizing: 'border-box',
          fontSize: '11px',
          fontFamily: 'inherit',
          background: '#1a1a2e',
          border: '1px solid #555',
          borderRadius: '4px',
          color: '#e0e0e0',
          padding: '4px 6px',
        }}
      />
    </div>`
}

registerNodeRendererOverride('ngol.logic.text_box', TextBoxUi, { label: 'text-box-nodeui' })

window.NGOL.log('text-box-nodeui loaded')
