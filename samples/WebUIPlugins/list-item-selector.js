// ────────────────────────────────────────────────────────────────
// NGOL WebUI plugin sample — List Item Selector (nodeRenderer)
//
// Pair: samples/CustomNodes/custom_webui_samples/ListItemSelectorNode.cs
// Deploy: <ngolRoot>/WebUI/plugins/list-item-selector.js
//
// Header + exec ports are drawn by NGOL (CustomNode nodeRenderer shell).
// This plugin renders the body only: pill/list UI + setSnapshotValue.
// ────────────────────────────────────────────────────────────────
const { React, html, registerNodeRenderer, setSnapshotValue } = window.NGOL

const PILL_SCROLL_MAX = 120
const LIST_SCROLL_MAX = 140

// CustomNode.tsx PORT_BASE / PORT_STEP と揃える
// TODO: 現在は値を直書きしているが、NGOL.layout.PORT_BASE / NGOL.layout.PORT_STEP
//       （または NGOL.layout.autoPortLayout(nodeTypeInfo)）を直接参照する形に更新できる。
//       手計算・二重管理をやめてヘルパーを正とする（webui-plugin-guide.md §3.5参照）
const PORT_BASE = 68
const PORT_STEP = 26

const pillBtnStyle = (active) => ({
  background: active ? '#7c4dff' : '#26263a',
  border: '1px solid ' + (active ? '#9c8cff' : '#555'),
  borderRadius: '10px',
  padding: '2px 10px',
  fontSize: '10px',
  color: active ? '#fff' : '#bbb',
  cursor: 'pointer',
})

const stopDrag = (e) => e.stopPropagation()
const stopWheel = (e) => e.stopPropagation()

function ListItemSelectorUi({ nodeId, snapshotBadgesByPort, paramValues, onParamChange }) {
  // ListItemSelectorNode は items→selected→index の順で SetSnapshot するため、
  // 単一の snapshotBadge（最後に書いたポートのみ）ではなく snapshotBadgesByPort で
  // 'items' ポートのバッジを名指しで読む。
  const itemsValueString = snapshotBadgesByPort?.items?.valueString
  const items = React.useMemo(() => {
    if (!itemsValueString) return []
    try {
      const parsed = JSON.parse(itemsValueString)
      return Array.isArray(parsed) ? parsed.map(String) : []
    } catch {
      return []
    }
  }, [itemsValueString])

  const [filter, setFilter] = React.useState('')
  const viewMode = paramValues.viewMode === 'list' ? 'list' : 'pills'

  const selected = String(paramValues.selected ?? '')
  let effectiveIndex = parseInt(String(paramValues.index ?? ''), 10)
  if (!Number.isFinite(effectiveIndex) || effectiveIndex < 0 || effectiveIndex >= items.length) {
    effectiveIndex = items.indexOf(selected)
  }
  if (effectiveIndex < 0 && items.length > 0) effectiveIndex = 0
  const effectiveSelected = effectiveIndex >= 0 && effectiveIndex < items.length
    ? items[effectiveIndex]
    : (items.includes(selected) ? selected : (items[0] ?? ''))

  const pick = (opt, idx) => {
    onParamChange('selected', opt)
    onParamChange('index', String(idx))
    setSnapshotValue(nodeId, 'selected', opt)
    setSnapshotValue(nodeId, 'index', String(idx))
  }

  const setViewMode = (mode) => onParamChange('viewMode', mode)

  const filterLower = filter.trim().toLowerCase()
  const filtered = filterLower
    ? items.map((label, idx) => ({ label, idx })).filter(({ label }) => label.toLowerCase().includes(filterLower))
    : items.map((label, idx) => ({ label, idx }))

  const toggleBtn = (mode, label) => html`
    <button
      key=${mode}
      className="nodrag"
      onClick=${() => setViewMode(mode)}
      onMouseDown=${stopDrag}
      onPointerDown=${stopDrag}
      style=${{
        background: viewMode === mode ? '#3a3a5c' : 'transparent',
        border: '1px solid ' + (viewMode === mode ? '#9c8cff' : '#555'),
        borderRadius: '4px',
        padding: '1px 8px',
        fontSize: '9px',
        color: viewMode === mode ? '#e0e0e0' : '#888',
        cursor: 'pointer',
      }}
    >${label}</button>`

  return html`
    <div style=${{ width: '220px' }}>
      <div className="nodrag" style=${{ display: 'flex', justifyContent: 'flex-end', gap: '4px', marginBottom: '6px' }}>
        ${toggleBtn('pills', 'Pills')}
        ${toggleBtn('list', 'List')}
      </div>

      ${items.length === 0
        ? html`<div style=${{ fontSize: '10px', color: '#888', minHeight: '36px', lineHeight: '1.4' }}>
            No items — run upstream fragment first
          </div>`
        : viewMode === 'list'
          ? html`
            <div className="nodrag">
              <input
                type="text"
                placeholder="Filter..."
                value=${filter}
                onInput=${(e) => setFilter(e.target.value)}
                onMouseDown=${stopDrag}
                onPointerDown=${stopDrag}
                style=${{
                  width: '100%',
                  boxSizing: 'border-box',
                  marginBottom: '4px',
                  padding: '2px 6px',
                  fontSize: '10px',
                  background: '#1a1a2e',
                  border: '1px solid #555',
                  borderRadius: '4px',
                  color: '#e0e0e0',
                }}
              />
              <div
                className="nodrag nowheel"
                onWheel=${stopWheel}
                style=${{ maxHeight: LIST_SCROLL_MAX + 'px', overflowY: 'auto' }}
              >
                ${filtered.map(({ label, idx }) => html`
                  <button
                    key=${label + '-' + idx}
                    onClick=${() => pick(label, idx)}
                    onMouseDown=${stopDrag}
                    onPointerDown=${stopDrag}
                    style=${{
                      display: 'block',
                      width: '100%',
                      textAlign: 'left',
                      marginBottom: '2px',
                      padding: '3px 6px',
                      fontSize: '10px',
                      cursor: 'pointer',
                      ...pillBtnStyle(label === effectiveSelected && idx === effectiveIndex),
                    }}
                  >${label}</button>`)}
              </div>
            </div>`
          : html`
            <div
              className="nodrag nowheel"
              onWheel=${stopWheel}
              style=${{
                display: 'flex',
                flexWrap: 'wrap',
                gap: '4px',
                maxHeight: PILL_SCROLL_MAX + 'px',
                overflowY: 'auto',
              }}
            >
              ${items.map((opt, idx) => html`
                <button
                  key=${opt + '-' + idx}
                  onClick=${() => pick(opt, idx)}
                  onMouseDown=${stopDrag}
                  onPointerDown=${stopDrag}
                  style=${pillBtnStyle(opt === effectiveSelected && idx === effectiveIndex)}
                >${opt}</button>`)}
            </div>`}

      ${items.length > 0 && html`
        <div style=${{ fontSize: '10px', color: '#9c8cff', marginTop: '6px' }}>
          [${effectiveIndex}] ${effectiveSelected}
        </div>`}
    </div>`
}

registerNodeRenderer('ngol.webui.list_item_selector', ListItemSelectorUi, {
  portLayout: () => ({
    inputs: { items: PORT_BASE },
    outputs: { items: PORT_BASE, index: PORT_BASE + PORT_STEP, selected: PORT_BASE + PORT_STEP * 2 },
  }),
})

window.NGOL.log('list-item-selector loaded')
