import type { NodeGraphData } from '../types/protocol'

/**
 * NGOL 発のページリロードを跨いでキャンバス状態を引き継ぐための一時領域。
 *
 * sessionStorage を使うのはタブ単位で分離されるため。複数タブが同時にリロードしても
 * 互いの状態を上書きしない。書き込みはリロードする瞬間の1回だけで、読み取り時に削除する。
 */

const STORAGE_KEY = 'ngol_reload_handoff'

/** 形式を変えたら上げる。不一致の payload は破棄される。 */
const HANDOFF_VERSION = 1

/**
 * 書き込みからこの時間を過ぎた payload は使わない。
 * リロードが中断されたまま放置された場合に、後から古い状態が復元されるのを防ぐ。
 */
const MAX_AGE_MS = 60_000

export interface ReloadHandoffViewport {
  x: number
  y: number
  zoom: number
}

export interface ReloadHandoff {
  version: number
  savedAt: number
  graph: NodeGraphData
  viewport?: ReloadHandoffViewport
}

export interface WriteHandoffInput {
  graph: NodeGraphData
  viewport?: ReloadHandoffViewport
}

/**
 * リロード後に復元する状態を書き込む。
 *
 * 失敗（プライベートモード・容量超過など）は false で返す。呼び出し側は
 * リロードを中止すること——書けていないままリロードすると状態が失われる。
 */
export function writeHandoff(input: WriteHandoffInput): boolean {
  const payload: ReloadHandoff = {
    version: HANDOFF_VERSION,
    savedAt: Date.now(),
    graph: input.graph,
    ...(input.viewport ? { viewport: input.viewport } : {}),
  }
  try {
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify(payload))
    return true
  } catch {
    return false
  }
}

/**
 * 書き込まれた状態を取り出す。**読み取れたかどうかに関わらず領域は削除する**ため、
 * 同じ payload が二度復元されることはない。
 *
 * 壊れた payload・期限切れ・形式不一致はすべて null を返す。復元に失敗するより
 * 新規キャンバスで起動する方が安全なため。
 */
export function consumeHandoff(): ReloadHandoff | null {
  let raw: string | null
  try {
    raw = sessionStorage.getItem(STORAGE_KEY)
    sessionStorage.removeItem(STORAGE_KEY)
  } catch {
    return null
  }
  if (!raw) return null

  try {
    const parsed = JSON.parse(raw) as Partial<ReloadHandoff>
    if (parsed.version !== HANDOFF_VERSION) return null
    if (typeof parsed.savedAt !== 'number') return null
    if (Date.now() - parsed.savedAt > MAX_AGE_MS) return null

    const graph = parsed.graph
    if (!graph || typeof graph.id !== 'string' || !Array.isArray(graph.nodes)) return null

    return {
      version: parsed.version,
      savedAt: parsed.savedAt,
      graph: graph as NodeGraphData,
      ...(parsed.viewport ? { viewport: parsed.viewport } : {}),
    }
  } catch {
    return null
  }
}
