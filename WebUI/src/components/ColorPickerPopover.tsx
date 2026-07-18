import { useEffect, useRef } from 'react'
import { RgbaColorPicker } from 'react-colorful'
import './ColorPickerPopover.css'
import { type RgbaColor } from './colorUtils'

const clamp01 = (v: number) => Math.max(0, Math.min(1, v))

// ────────────────────────────────────────────────────────────────
// ColorPickerPopover — dataType: "color" 入力ポート共通のカラーピッカー
// ────────────────────────────────────────────────────────────────
interface Props {
  color: RgbaColor
  onChange: (c: RgbaColor) => void
  onClose: () => void
  onEditStart?: () => void
  onEditEnd?: () => void
}

export function ColorPickerPopover({ color, onChange, onClose, onEditStart, onEditEnd }: Props) {
  const ref = useRef<HTMLDivElement>(null)

  // ポップオーバー外クリックで閉じる
  useEffect(() => {
    const handler = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) onClose()
    }
    document.addEventListener('mousedown', handler)
    return () => document.removeEventListener('mousedown', handler)
  }, [onClose])

  // react-colorful は rgb: 0–255, a: 0–1
  const rcColor = {
    r: Math.round(color.r * 255),
    g: Math.round(color.g * 255),
    b: Math.round(color.b * 255),
    a: color.a,
  }

  return (
    <div
      ref={ref}
      className="color-picker-popover"
      onMouseDown={e => e.stopPropagation()}
      onPointerDown={e => {
        e.stopPropagation()
        e.currentTarget.setPointerCapture(e.pointerId)
        onEditStart?.()
      }}
      onPointerUp={() => onEditEnd?.()}
    >
      <RgbaColorPicker
        color={rcColor}
        onChange={c => onChange({ r: c.r / 255, g: c.g / 255, b: c.b / 255, a: c.a })}
      />
      <div className="color-picker-fields">
        {(['r', 'g', 'b'] as const).map(ch => (
          <label key={ch} className="color-picker-field">
            <span>{ch.toUpperCase()}</span>
            <input
              type="number" min={0} max={255} step={1}
              value={Math.round(color[ch] * 255)}
              onChange={e => {
                const v = parseInt(e.target.value, 10)
                if (!isNaN(v)) onChange({ ...color, [ch]: clamp01(v / 255) })
              }}
            />
          </label>
        ))}
        <label className="color-picker-field">
          <span>A</span>
          <input
            type="number" min={0} max={1} step={0.01}
            value={color.a.toFixed(2)}
            onChange={e => {
              const v = parseFloat(e.target.value)
              if (!isNaN(v)) onChange({ ...color, a: clamp01(v) })
            }}
          />
        </label>
      </div>
    </div>
  )
}
