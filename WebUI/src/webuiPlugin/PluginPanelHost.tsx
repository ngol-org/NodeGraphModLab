import { useRef, useState } from 'react'
import type { PluginPanelDef } from './pluginPanelRegistry'
import { PluginErrorBoundary } from './PluginErrorBoundary'
import { NgolIcon } from '../components/icons/NgolIcon'
import './PluginPanelHost.css'

interface Props {
  def: PluginPanelDef
  /** 複数パネル同時表示時のカスケードオフセット用インデックス。 */
  index: number
  onClose: () => void
}

/**
 * 外部プラグインパネルの共通ホスト。
 * ヘッダ（タイトル + 閉じるボタン）を NGOL が提供し、ボディをプラグインに委譲する。
 * ヘッダのドラッグでパネルを移動できる。
 * プラグイン描画例外は ErrorBoundary で隔離される。
 */
export function PluginPanelHost({ def, index, onClose }: Props) {
  const rootRef = useRef<HTMLDivElement>(null)
  // null = 初期位置（中央 + カスケードオフセット）。ドラッグ後は左上座標で管理。
  const [pos, setPos] = useState<{ x: number; y: number } | null>(null)
  const dragRef = useRef<{ startX: number; startY: number; baseX: number; baseY: number } | null>(null)

  const onHeaderPointerDown = (e: React.PointerEvent<HTMLDivElement>) => {
    const rect = rootRef.current?.getBoundingClientRect()
    if (!rect) return
    dragRef.current = { startX: e.clientX, startY: e.clientY, baseX: rect.left, baseY: rect.top }
    e.currentTarget.setPointerCapture(e.pointerId)
  }
  const onHeaderPointerMove = (e: React.PointerEvent<HTMLDivElement>) => {
    const drag = dragRef.current
    if (!drag) return
    setPos({
      x: Math.max(0, drag.baseX + e.clientX - drag.startX),
      y: Math.max(0, drag.baseY + e.clientY - drag.startY),
    })
  }
  const onHeaderPointerUp = () => { dragRef.current = null }

  const offset = index * 32
  const style: React.CSSProperties = pos
    ? { left: pos.x, top: pos.y, transform: 'none' }
    : { transform: `translate(calc(-50% + ${offset}px), calc(-50% + ${offset}px))` }

  return (
    <div ref={rootRef} className="plugin-panel-host" style={style}>
      <div
        className="plugin-panel-host-header"
        onPointerDown={onHeaderPointerDown}
        onPointerMove={onHeaderPointerMove}
        onPointerUp={onHeaderPointerUp}
        onPointerCancel={onHeaderPointerUp}
      >
        <span className="plugin-panel-host-title">{def.title}</span>
        <button
          className="plugin-panel-host-close-btn"
          onClick={onClose}
          onPointerDown={e => e.stopPropagation()}
          title="Close"
        >
          <NgolIcon name="close" size={12} />
        </button>
      </div>
      <div className="plugin-panel-host-body">
        <PluginErrorBoundary pluginId={def.id}>
          <def.component onClose={onClose} />
        </PluginErrorBoundary>
      </div>
    </div>
  )
}
