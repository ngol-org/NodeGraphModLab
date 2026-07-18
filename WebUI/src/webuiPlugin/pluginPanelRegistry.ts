import type React from 'react'

/** プラグインパネルコンポーネントの props。データアクセスは NGOL.ws 経由で行う。 */
export interface PluginPanelProps {
  onClose: () => void
}

/** プラグインパネルの登録定義。 */
export interface PluginPanelDef {
  id: string
  title: string
  component: React.FC<PluginPanelProps>
  defaultOpen?: boolean
}

const panels: PluginPanelDef[] = []

/** プラグインパネルを登録する。同一 id の再登録は上書き。 */
export function registerPluginPanel(def: PluginPanelDef): void {
  const idx = panels.findIndex(p => p.id === def.id)
  if (idx >= 0) panels[idx] = def
  else panels.push(def)
}

/** 登録済みプラグインパネルの一覧を返す。 */
export function getPluginPanels(): readonly PluginPanelDef[] {
  return panels
}
