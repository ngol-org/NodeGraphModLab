import { useState } from 'react'
import type { Node } from '@xyflow/react'
import type { NodeTypeInfo, FragmentDefinition } from '../types/protocol'
import './NodeInspector.css'
import { ColorPickerPopover } from './ColorPickerPopover'
import { rgbaCssColor, toRgbaColor } from './colorUtils'
import { resolveCurrentNodeTypeVersion } from '../lib/nodeVersion'

interface Props {
  selectedNode: Node | null
  nodeTypeInfo: NodeTypeInfo | null
  onParamChange: (paramName: string, value: unknown) => void
  fragments?: FragmentDefinition[]
  onParamEditStart?: () => void
  onParamEditEnd?: () => void
  headerAction?: React.ReactNode
}

// ────────────────────────────────────────────────────────────────
// ポート別入力フィールド
// ────────────────────────────────────────────────────────────────
interface PortInputProps {
  name: string
  dataType: string
  value: unknown
  onChange: (value: unknown) => void
  onEditStart?: () => void
  onEditEnd?: () => void
}

function PortInput({ name, dataType, value, onChange, onEditStart, onEditEnd }: PortInputProps) {
  const [pickerOpen, setPickerOpen] = useState(false)
  const type = dataType.toLowerCase()

  // Color 型: スウォッチ + カラーピッカー
  if (type === 'color') {
    const colorVal = toRgbaColor(value)
    return (
      <div style={{ position: 'relative' }}>
        <div
          className="inspector-color-swatch"
          style={{ background: rgbaCssColor(colorVal) }}
          onClick={() => setPickerOpen(v => !v)}
          title="Click to open color picker"
        />
        {pickerOpen && (
          <div className="inspector-picker-anchor">
            <ColorPickerPopover
              color={colorVal}
              onChange={onChange}
              onClose={() => setPickerOpen(false)}
              onEditStart={onEditStart}
              onEditEnd={onEditEnd}
            />
          </div>
        )}
      </div>
    )
  }

  if (type === 'bool' || type === 'boolean') {
    const checked = value === true || value === 'true'
    return (
      <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
        <input
          type="checkbox"
          id={`param-${name}`}
          checked={checked}
          onChange={e => onChange(e.target.checked)}
          style={{ width: 'auto', cursor: 'pointer' }}
        />
        <label htmlFor={`param-${name}`} style={{ fontSize: 12, cursor: 'pointer' }}>
          {checked ? 'true' : 'false'}
        </label>
      </div>
    )
  }

  if (type === 'number' || type === 'float' || type === 'double' || type === 'int' || type === 'single') {
    return (
      <input
        type="number"
        style={{ width: '100%' }}
        placeholder={`${name} value…`}
        value={value === undefined || value === null ? '' : String(value)}
        onFocus={() => onEditStart?.()}
        onBlur={() => onEditEnd?.()}
        onChange={e => {
          const raw = e.target.value
          onChange(raw === '' ? '' : Number(raw))
        }}
      />
    )
  }

  // string / object / その他
  return (
    <input
      type="text"
      style={{ width: '100%' }}
      placeholder={`${name} value…`}
      value={value === undefined || value === null ? '' : String(value)}
      onFocus={() => onEditStart?.()}
      onBlur={() => onEditEnd?.()}
      onChange={e => onChange(e.target.value)}
    />
  )
}

// ────────────────────────────────────────────────────────────────
// NodeInspector
// ────────────────────────────────────────────────────────────────
export function NodeInspector({ selectedNode, nodeTypeInfo, onParamChange, fragments, onParamEditStart, onParamEditEnd, headerAction }: Props) {
  const paramValues = selectedNode
    ? ((selectedNode.data as { paramValues?: Record<string, unknown> }).paramValues ?? {})
    : {}

  const inputPorts  = nodeTypeInfo?.ports.filter(p => p.direction === 'input')  ?? []
  const outputPorts = nodeTypeInfo?.ports.filter(p => p.direction === 'output') ?? []
  const selectedNodeTypeVersion = selectedNode
    ? (selectedNode.data as { nodeTypeVersion?: string }).nodeTypeVersion
    : undefined
  const currentNodeTypeVersion = resolveCurrentNodeTypeVersion(nodeTypeInfo?.nodeVersion)

  return (
    <aside className="inspector">
      <div className="panel-header" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        {headerAction}
        <span>Inspector</span>
      </div>
      <div className="inspector-content">
        {!selectedNode && (
          <div style={{ color: 'var(--text-dim)', fontSize: 11 }}>
            Select a node to inspect its properties.
          </div>
        )}
        {selectedNode && nodeTypeInfo && (
          <>
            {/* ノード情報 */}
            <div className="inspector-row">
              <div className="inspector-label">Node Type</div>
              <div style={{ fontSize: 12, color: 'var(--text-primary)' }}>{nodeTypeInfo.displayName}</div>
              <div style={{ fontSize: 10, color: 'var(--text-dim)', marginTop: 2 }}>{nodeTypeInfo.id}</div>
            </div>
            <div className="inspector-row">
              <div className="inspector-label">Category</div>
              <div style={{ fontSize: 12, color: 'var(--text-secondary)' }}>{nodeTypeInfo.category}</div>
            </div>
            <div className="inspector-row">
              <div className="inspector-label">Version</div>
              <div style={{ fontSize: 12, color: 'var(--text-secondary)' }}>
                current: {currentNodeTypeVersion}
                {selectedNodeTypeVersion && selectedNodeTypeVersion !== currentNodeTypeVersion
                  ? ` (saved: ${selectedNodeTypeVersion})`
                  : ''}
              </div>
            </div>
            {fragments && fragments.length > 0 && (
              <div className="inspector-row">
                <div className="inspector-label">Fragment</div>
                <div style={{ fontSize: 12, color: 'var(--text-primary)' }}>
                  {fragments.find(f => f.nodeInstanceIds.includes(selectedNode.id))?.name ?? '—'}
                </div>
              </div>
            )}
            {nodeTypeInfo.description && (
              <div className="inspector-row">
                <div className="inspector-label">Description</div>
                <div style={{ fontSize: 11, color: 'var(--text-dim)', lineHeight: 1.4 }}>{nodeTypeInfo.description}</div>
              </div>
            )}

            {inputPorts.length > 0 && (
              <div className="inspector-row">
                <div className="inspector-label">Input Parameters</div>
                {inputPorts.map(port => (
                  <div key={port.name} style={{ marginBottom: 8 }}>
                    <div style={{ display: 'flex', gap: 6, alignItems: 'center', marginBottom: 3 }}>
                      <span style={{ fontSize: 12, color: 'var(--text-primary)' }}>{port.name}</span>
                      <span style={{ fontSize: 10, color: 'var(--text-dim)' }}>{port.dataType}</span>
                      {port.isRequired && (
                        <span style={{ fontSize: 9, color: 'var(--error)' }} title="Required">*</span>
                      )}
                    </div>
                    <PortInput
                      name={port.name}
                      dataType={port.dataType}
                      value={paramValues[port.name]}
                      onChange={val => onParamChange(port.name, val)}
                      onEditStart={onParamEditStart}
                      onEditEnd={onParamEditEnd}
                    />
                    {port.description && (
                      <div style={{ fontSize: 10, color: 'var(--text-dim)', marginTop: 2, lineHeight: 1.3 }}>
                        {port.description}
                      </div>
                    )}
                  </div>
                ))}
              </div>
            )}

            {/* 出力ポート: 入力ポートがない場合は値編集、ある場合は readonly 表示 */}
            {outputPorts.length > 0 && (
              <div className="inspector-row">
                <div className="inspector-label">
                  {inputPorts.length === 0 ? 'Output Parameters' : 'Output Ports'}
                </div>
                {outputPorts.map(port => {
                  const dt = port.dataType.toLowerCase()
                  const isEditable = inputPorts.length === 0 &&
                    (dt === 'number' || dt === 'float' || dt === 'double' || dt === 'int' || dt === 'integer' || dt === 'single' || dt === 'string')
                  return (
                    <div key={port.name} style={{ marginBottom: isEditable ? 8 : 4 }}>
                      {isEditable ? (
                        <>
                          <div style={{ display: 'flex', gap: 6, alignItems: 'center', marginBottom: 3 }}>
                            <span style={{ fontSize: 12, color: 'var(--text-primary)' }}>{port.name}</span>
                            <span style={{ fontSize: 10, color: 'var(--text-dim)' }}>{port.dataType}</span>
                          </div>
                          <PortInput
                            name={port.name}
                            dataType={port.dataType}
                            value={paramValues[port.name]}
                            onChange={val => onParamChange(port.name, val)}
                            onEditStart={onParamEditStart}
                            onEditEnd={onParamEditEnd}
                          />
                          {port.description && (
                            <div style={{ fontSize: 10, color: 'var(--text-dim)', marginTop: 2, lineHeight: 1.3 }}>
                              {port.description}
                            </div>
                          )}
                        </>
                      ) : (
                        <div style={{ display: 'flex', gap: 6, alignItems: 'center' }}>
                          <span style={{
                            fontSize: 9,
                            padding: '1px 5px',
                            borderRadius: 2,
                            background: 'var(--success)',
                            color: '#fff',
                          }}>
                            out
                          </span>
                          <span style={{ fontSize: 12, color: 'var(--text-primary)' }}>{port.name}</span>
                          <span style={{ fontSize: 10, color: 'var(--text-dim)' }}>{port.dataType}</span>
                        </div>
                      )}
                    </div>
                  )
                })}
              </div>
            )}
          </>
        )}
      </div>
    </aside>
  )
}

