import { useState } from 'react'
import { wsClient } from '../lib/wsClient'
import { setAuthToken } from '../lib/authToken'

export function useEditorDialogs() {
  // Save As ダイアログ
  const [saveAsDialogOpen, setSaveAsDialogOpen] = useState(false)
  const [saveAsName, setSaveAsName] = useState('')

  // Export Nodes as DLL ダイアログ
  const [exportDialogOpen, setExportDialogOpen] = useState(false)
  const [versionDialogOpen, setVersionDialogOpen] = useState(false)
  const [snapshotStorePanelOpen, setSnapshotStorePanelOpen] = useState(false)

  // Clear Canvas 確認ダイアログ
  const [clearCanvasDialogOpen, setClearCanvasDialogOpen] = useState(false)

  const [exportDllName, setExportDllName] = useState('')
  const [exportOutputDir, setExportOutputDir] = useState('Nodes/CustomNodes/dll')
  const [exportResult, setExportResult] = useState<{ success: boolean; message: string } | null>(null)

  // 接続トークン手動入力ダイアログ（requireAuthToken 有効時、未接続バッジクリックで開く）
  const [tokenPromptOpen, setTokenPromptOpen] = useState(false)
  const [tokenPromptValue, setTokenPromptValue] = useState('')

  const handleTokenPromptConfirm = () => {
    const token = tokenPromptValue.trim()
    if (!token) return
    setAuthToken(token)
    setTokenPromptOpen(false)
    setTokenPromptValue('')
    wsClient.reconnectNow()
  }

  return {
    saveAsDialogOpen,
    setSaveAsDialogOpen,
    saveAsName,
    setSaveAsName,
    exportDialogOpen,
    setExportDialogOpen,
    versionDialogOpen,
    setVersionDialogOpen,
    snapshotStorePanelOpen,
    setSnapshotStorePanelOpen,
    clearCanvasDialogOpen,
    setClearCanvasDialogOpen,
    exportDllName,
    setExportDllName,
    exportOutputDir,
    setExportOutputDir,
    exportResult,
    setExportResult,
    tokenPromptOpen,
    setTokenPromptOpen,
    tokenPromptValue,
    setTokenPromptValue,
    handleTokenPromptConfirm,
  }
}
