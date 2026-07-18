import { NgolIcon } from './icons/NgolIcon'

interface EditorDialogHostProps {
  appVersion: string
  pluginVersion: string
  connected: boolean
  saveAsDialogOpen: boolean
  saveAsName: string
  setSaveAsName: (name: string) => void
  setSaveAsDialogOpen: (open: boolean) => void
  handleSaveAsConfirm: () => void
  exportDialogOpen: boolean
  exportDllName: string
  setExportDllName: (name: string) => void
  exportOutputDir: string
  setExportOutputDir: (dir: string) => void
  exportResult: { success: boolean; message: string } | null
  setExportResult: (result: { success: boolean; message: string } | null) => void
  exportNodeTypeIds: string[]
  handleExportConfirm: () => void
  setExportDialogOpen: (open: boolean) => void
  versionDialogOpen: boolean
  setVersionDialogOpen: (open: boolean) => void
  clearCanvasDialogOpen: boolean
  setClearCanvasDialogOpen: (open: boolean) => void
  onClearCanvasConfirm: () => void
  tokenPromptOpen: boolean
  setTokenPromptOpen: (open: boolean) => void
  tokenPromptValue: string
  setTokenPromptValue: (value: string) => void
  handleTokenPromptConfirm: () => void
}

export function EditorDialogHost({
  appVersion,
  pluginVersion,
  connected,
  saveAsDialogOpen,
  saveAsName,
  setSaveAsName,
  setSaveAsDialogOpen,
  handleSaveAsConfirm,
  exportDialogOpen,
  exportDllName,
  setExportDllName,
  exportOutputDir,
  setExportOutputDir,
  exportResult,
  setExportResult,
  exportNodeTypeIds,
  handleExportConfirm,
  setExportDialogOpen,
  versionDialogOpen,
  setVersionDialogOpen,
  clearCanvasDialogOpen,
  setClearCanvasDialogOpen,
  onClearCanvasConfirm,
  tokenPromptOpen,
  setTokenPromptOpen,
  tokenPromptValue,
  setTokenPromptValue,
  handleTokenPromptConfirm,
}: EditorDialogHostProps) {
  return (
    <>
      {clearCanvasDialogOpen && (
        <div className="modal-overlay" onMouseDown={() => setClearCanvasDialogOpen(false)}>
          <div className="modal-dialog" onMouseDown={e => e.stopPropagation()}>
            <h3>Clear Canvas</h3>
            <p>This will remove all nodes from the canvas and start a new unsaved graph. Unsaved changes will be lost. Continue?</p>
            <div className="modal-buttons">
              <button className="danger" onClick={onClearCanvasConfirm}>Clear</button>
              <button onClick={() => setClearCanvasDialogOpen(false)}>Cancel</button>
            </div>
          </div>
        </div>
      )}

      {saveAsDialogOpen && (
        <div className="modal-overlay" onMouseDown={() => setSaveAsDialogOpen(false)}>
          <div className="modal-dialog" onMouseDown={e => e.stopPropagation()}>
            <h3>Save Graph As</h3>
            <label>Graph Name</label>
            <input
              autoFocus
              value={saveAsName}
              onChange={e => setSaveAsName(e.target.value)}
              onKeyDown={e => {
                if (e.key === 'Enter') handleSaveAsConfirm()
                if (e.key === 'Escape') setSaveAsDialogOpen(false)
              }}
              placeholder="Graph name"
            />
            <div className="modal-buttons">
              <button className="primary" onClick={handleSaveAsConfirm} disabled={!saveAsName.trim() || !connected}>Save</button>
              <button onClick={() => setSaveAsDialogOpen(false)}>Cancel</button>
            </div>
          </div>
        </div>
      )}

      {exportDialogOpen && (
        <div className="modal-overlay" onMouseDown={() => setExportDialogOpen(false)}>
          <div className="modal-dialog" onMouseDown={e => e.stopPropagation()}>
            <h3>Export Nodes as DLL</h3>
            <label>DLL Name (assembly name)</label>
            <input
              autoFocus
              value={exportDllName}
              onChange={e => setExportDllName(e.target.value)}
              onKeyDown={e => {
                if (e.key === 'Enter') handleExportConfirm()
                if (e.key === 'Escape') setExportDialogOpen(false)
              }}
              placeholder="MyNodePack"
            />
            <label>Output Directory (relative to plugins folder)</label>
            <input
              value={exportOutputDir}
              onChange={e => setExportOutputDir(e.target.value)}
              placeholder="Nodes/CustomNodes/dll"
            />
            <label>Selected Node Types ({exportNodeTypeIds.length})</label>
            <div className="modal-export-nodeids">
              {exportNodeTypeIds.length === 0
                ? <span style={{ color: 'var(--error)' }}>No nodes selected. Select nodes on canvas first.</span>
                : exportNodeTypeIds.map(id => <div key={id}>{id}</div>)}
            </div>
            {exportResult && (
              <div className={exportResult.success ? 'modal-result-success' : 'modal-result-error'}>
                {exportResult.message}
              </div>
            )}
            {exportResult && exportResult.success ? (
              <div className="modal-buttons">
                <button
                  className="primary"
                  onClick={() => {
                    setExportDialogOpen(false)
                    setExportResult(null)
                  }}
                >
                  OK
                </button>
              </div>
            ) : (
              <div className="modal-buttons">
                <button
                  className="primary"
                  onClick={handleExportConfirm}
                  disabled={!exportDllName.trim() || exportNodeTypeIds.length === 0 || !connected || !!exportResult}
                >
                  Export
                </button>
                <button onClick={() => setExportDialogOpen(false)}>Close</button>
              </div>
            )}
          </div>
        </div>
      )}

      {tokenPromptOpen && (
        <div className="modal-overlay" onMouseDown={() => setTokenPromptOpen(false)}>
          <div className="modal-dialog" onMouseDown={e => e.stopPropagation()}>
            <h3>Connection Token</h3>
            <p>This server requires an auth token. Paste the token shown in the host log, or open the URL printed there.</p>
            <input
              autoFocus
              value={tokenPromptValue}
              onChange={e => setTokenPromptValue(e.target.value)}
              onKeyDown={e => {
                if (e.key === 'Enter') handleTokenPromptConfirm()
                if (e.key === 'Escape') setTokenPromptOpen(false)
              }}
              placeholder="Token"
            />
            <div className="modal-buttons">
              <button className="primary" onClick={handleTokenPromptConfirm} disabled={!tokenPromptValue.trim()}>Connect</button>
              <button onClick={() => setTokenPromptOpen(false)}>Cancel</button>
            </div>
          </div>
        </div>
      )}

      {versionDialogOpen && (
        <div className="modal-overlay" onMouseDown={() => setVersionDialogOpen(false)}>
          <div className="modal-dialog" onMouseDown={e => e.stopPropagation()} style={{ minWidth: 280 }}>
            <h3>Node Graph mOd Lab</h3>
            <p>Plugin version: {pluginVersion || '---'}</p>
            <p>WebUI version: {appVersion}</p>
            <div className="modal-buttons">
              <button onClick={() => setVersionDialogOpen(false)}>Close</button>
            </div>
          </div>
        </div>
      )}
    </>
  )
}
