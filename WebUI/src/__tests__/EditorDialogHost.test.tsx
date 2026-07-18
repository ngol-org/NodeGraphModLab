/**
 * EditorDialogHost コンポーネントテスト
 * - Clear Canvas 確認ダイアログの表示・Clear/Cancel ボタンの配線を保証する。
 */
import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { EditorDialogHost } from '../components/EditorDialogHost'

const DEFAULT_PROPS = {
  appVersion: 'v0.0.0-test',
  pluginVersion: '',
  connected: true,
  saveAsDialogOpen: false,
  saveAsName: '',
  setSaveAsName: vi.fn(),
  setSaveAsDialogOpen: vi.fn(),
  handleSaveAsConfirm: vi.fn(),
  exportDialogOpen: false,
  exportDllName: '',
  setExportDllName: vi.fn(),
  exportOutputDir: '',
  setExportOutputDir: vi.fn(),
  exportResult: null,
  setExportResult: vi.fn(),
  exportNodeTypeIds: [] as string[],
  handleExportConfirm: vi.fn(),
  setExportDialogOpen: vi.fn(),
  versionDialogOpen: false,
  setVersionDialogOpen: vi.fn(),
  clearCanvasDialogOpen: false,
  setClearCanvasDialogOpen: vi.fn(),
  onClearCanvasConfirm: vi.fn(),
  tokenPromptOpen: false,
  setTokenPromptOpen: vi.fn(),
  tokenPromptValue: '',
  setTokenPromptValue: vi.fn(),
  handleTokenPromptConfirm: vi.fn(),
}

describe('EditorDialogHost - Clear Canvas 確認ダイアログ', () => {
  it('clearCanvasDialogOpen が false の場合は表示されない', () => {
    render(<EditorDialogHost {...DEFAULT_PROPS} />)
    expect(screen.queryByText('Clear Canvas')).toBeNull()
  })

  it('clearCanvasDialogOpen が true の場合はダイアログが表示される', () => {
    render(<EditorDialogHost {...DEFAULT_PROPS} clearCanvasDialogOpen={true} />)
    expect(screen.getByText('Clear Canvas')).toBeTruthy()
    expect(screen.getByText(/This will remove all nodes from the canvas/)).toBeTruthy()
  })

  it('Clear ボタンクリックで onClearCanvasConfirm が呼ばれる', () => {
    const onClearCanvasConfirm = vi.fn()
    render(
      <EditorDialogHost
        {...DEFAULT_PROPS}
        clearCanvasDialogOpen={true}
        onClearCanvasConfirm={onClearCanvasConfirm}
      />
    )
    fireEvent.click(screen.getByText('Clear'))
    expect(onClearCanvasConfirm).toHaveBeenCalledTimes(1)
  })

  it('Cancel ボタンクリックで setClearCanvasDialogOpen(false) が呼ばれる', () => {
    const setClearCanvasDialogOpen = vi.fn()
    render(
      <EditorDialogHost
        {...DEFAULT_PROPS}
        clearCanvasDialogOpen={true}
        setClearCanvasDialogOpen={setClearCanvasDialogOpen}
      />
    )
    fireEvent.click(screen.getByText('Cancel'))
    expect(setClearCanvasDialogOpen).toHaveBeenCalledWith(false)
  })

  it('オーバーレイクリックで setClearCanvasDialogOpen(false) が呼ばれる', () => {
    const setClearCanvasDialogOpen = vi.fn()
    const { container } = render(
      <EditorDialogHost
        {...DEFAULT_PROPS}
        clearCanvasDialogOpen={true}
        setClearCanvasDialogOpen={setClearCanvasDialogOpen}
      />
    )
    const overlay = container.querySelector('.modal-overlay') as HTMLElement
    fireEvent.mouseDown(overlay)
    expect(setClearCanvasDialogOpen).toHaveBeenCalledWith(false)
  })

  it('ダイアログ本体クリックはオーバーレイへ伝播せず閉じない', () => {
    const setClearCanvasDialogOpen = vi.fn()
    const { container } = render(
      <EditorDialogHost
        {...DEFAULT_PROPS}
        clearCanvasDialogOpen={true}
        setClearCanvasDialogOpen={setClearCanvasDialogOpen}
      />
    )
    const dialog = container.querySelector('.modal-dialog') as HTMLElement
    fireEvent.mouseDown(dialog)
    expect(setClearCanvasDialogOpen).not.toHaveBeenCalled()
  })
})
