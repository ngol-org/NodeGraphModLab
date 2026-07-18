/**
 * MenuBar コンポーネントテスト
 * - Clear Canvas 項目が File メニューの意図した位置（Open Graph from File... と
 *   Export Nodes as DLL... の間）に表示されること、クリックで onClearCanvas が呼ばれることを保証する。
 *   （実装当初は File メニュー最上段に配置し、ユーザーレビューで再配置した経緯があるため回帰防止）
 */
import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent, within } from '@testing-library/react'
import { MenuBar } from '../components/MenuBar'
import type { DebugNodeField } from '../webuiPlugin/debugBridge'

const DEFAULT_DEBUG_NODE_FIELDS: Record<DebugNodeField, boolean> = {
  rect: true,
  pointerEvents: true,
  visibility: true,
  transform: false,
}

const DEFAULT_PROPS = {
  onClearCanvas: vi.fn(),
  onSave: vi.fn(),
  onSaveAs: vi.fn(),
  onLoadGraph: vi.fn(),
  onOpenGraphMenuActiveChange: vi.fn(),
  onOpenGraphFromFile: vi.fn(),
  onExportNodes: vi.fn(),
  onUndo: vi.fn(),
  onRedo: vi.fn(),
  onShowVersion: vi.fn(),
  debugBridgeEnabled: false,
  onToggleDebugBridge: vi.fn(),
  debugDetailLevel: 'minimal' as const,
  onSetDebugDetailLevel: vi.fn(),
  debugNodeFields: DEFAULT_DEBUG_NODE_FIELDS,
  onSetDebugNodeField: vi.fn(),
  onTogglePluginPanel: vi.fn(),
  onShowSnapshotStore: vi.fn(),
  onClearAllSnapshots: vi.fn(),
  onAddAnnotation: vi.fn(),
  canUndo: false,
  canRedo: false,
  canExportNodes: false,
  version: 'v0.0.0-test',
  pluginVersion: '',
  connected: true,
  saving: false,
}

function openFileMenu() {
  fireEvent.click(screen.getByRole('button', { name: 'File' }))
}

describe('MenuBar - File メニュー', () => {
  it('File ボタンクリックでドロップダウンが開く', () => {
    render(<MenuBar {...DEFAULT_PROPS} />)
    expect(screen.queryByText('Clear Canvas...')).toBeNull()
    openFileMenu()
    expect(screen.getByText('Clear Canvas...')).toBeTruthy()
  })

  it('Clear Canvas... が Open Graph from File... と Export Nodes as DLL... の間に配置される', () => {
    render(<MenuBar {...DEFAULT_PROPS} />)
    openFileMenu()
    const dropdown = screen.getByText('Save Graph').closest('.menubar-dropdown') as HTMLElement
    const itemTexts = within(dropdown)
      .getAllByRole('button')
      .map(btn => btn.textContent ?? '')
    const openFromFileIdx = itemTexts.findIndex(t => t.includes('Open Graph from File...'))
    const clearCanvasIdx = itemTexts.findIndex(t => t.includes('Clear Canvas...'))
    const exportIdx = itemTexts.findIndex(t => t.includes('Export Nodes as DLL...'))
    expect(openFromFileIdx).toBeGreaterThanOrEqual(0)
    expect(clearCanvasIdx).toBeGreaterThan(openFromFileIdx)
    expect(exportIdx).toBeGreaterThan(clearCanvasIdx)
  })

  it('Clear Canvas... クリックで onClearCanvas が呼ばれる（実行はされず、確認ダイアログを開くだけの契約）', () => {
    const onClearCanvas = vi.fn()
    render(<MenuBar {...DEFAULT_PROPS} onClearCanvas={onClearCanvas} />)
    openFileMenu()
    fireEvent.click(screen.getByText('Clear Canvas...'))
    expect(onClearCanvas).toHaveBeenCalledTimes(1)
  })

  it('Save Graph クリックで onSave が呼ばれる（再配置による既存動作への回帰がないこと）', () => {
    const onSave = vi.fn()
    render(<MenuBar {...DEFAULT_PROPS} onSave={onSave} />)
    openFileMenu()
    fireEvent.click(screen.getByText('Save Graph'))
    expect(onSave).toHaveBeenCalledTimes(1)
  })

  it('Export Nodes as DLL... クリックで onExportNodes が呼ばれる', () => {
    const onExportNodes = vi.fn()
    render(<MenuBar {...DEFAULT_PROPS} onExportNodes={onExportNodes} canExportNodes={true} />)
    openFileMenu()
    fireEvent.click(screen.getByText('Export Nodes as DLL...'))
    expect(onExportNodes).toHaveBeenCalledTimes(1)
  })
})
