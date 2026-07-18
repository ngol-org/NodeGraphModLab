/**
 * SnapshotHistoryPopup ドラッグ移動テスト
 */
import { describe, it, expect, vi } from 'vitest'
import { render, fireEvent } from '@testing-library/react'
import { SnapshotHistoryPopup } from '../components/SnapshotHistoryPopup'

const DEFAULT_PROPS = {
  nodeInstanceId: 'node-1',
  portName: 'value',
  entries: [],
  onRestore: vi.fn(),
  onClose: vi.fn(),
}

describe('SnapshotHistoryPopup - ドラッグ移動', () => {
  it('ヘッダーをドラッグすると left/top がマウス移動量分だけ更新される', () => {
    const { container } = render(<SnapshotHistoryPopup {...DEFAULT_PROPS} />)
    const popup = container.querySelector('.snapshot-history-popup') as HTMLElement
    const header = container.querySelector('.snapshot-history-header') as HTMLElement

    vi.spyOn(popup, 'getBoundingClientRect').mockReturnValue({
      left: 100, top: 200, right: 440, bottom: 400, width: 340, height: 200, x: 100, y: 200, toJSON() {},
    } as DOMRect)

    fireEvent.mouseDown(header, { clientX: 150, clientY: 250 })
    fireEvent.mouseMove(window, { clientX: 180, clientY: 260 })

    expect(popup.style.left).toBe('130px')
    expect(popup.style.top).toBe('210px')
    expect(popup.style.transform).toBe('none')
  })

  it('mouseup 後は mousemove しても位置が変化しない', () => {
    const { container } = render(<SnapshotHistoryPopup {...DEFAULT_PROPS} />)
    const popup = container.querySelector('.snapshot-history-popup') as HTMLElement
    const header = container.querySelector('.snapshot-history-header') as HTMLElement

    vi.spyOn(popup, 'getBoundingClientRect').mockReturnValue({
      left: 100, top: 200, right: 440, bottom: 400, width: 340, height: 200, x: 100, y: 200, toJSON() {},
    } as DOMRect)

    fireEvent.mouseDown(header, { clientX: 150, clientY: 250 })
    fireEvent.mouseMove(window, { clientX: 180, clientY: 260 })
    fireEvent.mouseUp(window)
    fireEvent.mouseMove(window, { clientX: 500, clientY: 500 })

    expect(popup.style.left).toBe('130px')
    expect(popup.style.top).toBe('210px')
  })

  it('ドラッグしていない初期状態では inline style の left/top が設定されない', () => {
    const { container } = render(<SnapshotHistoryPopup {...DEFAULT_PROPS} />)
    const popup = container.querySelector('.snapshot-history-popup') as HTMLElement

    expect(popup.style.left).toBe('')
    expect(popup.style.top).toBe('')
  })
})
