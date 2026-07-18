import { wsClient } from '../lib/wsClient'

type ConsoleLevel = 'log' | 'warn' | 'error'

// B-1: reactFlowNodes ダンプの詳細度。minimal=含めない / proximity=クリック近傍のみ / full=全件
export type DebugDetailLevel = 'minimal' | 'proximity' | 'full'
// B-3: reactFlowNodes の各要素に含めるフィールド（dataId は常に含む）
export type DebugNodeField = 'rect' | 'pointerEvents' | 'visibility' | 'transform'

const PROXIMITY_RADIUS_PX = 200

let enabled = false
let installed = false
let domListenersAttached = false
let detailLevel: DebugDetailLevel = 'minimal'
let nodeFields: Record<DebugNodeField, boolean> = {
  rect: true,
  pointerEvents: true,
  visibility: true,
  transform: true,
}

// ページの console を差し替えた後は originals.apply(window.console) だと
// DevTools に表示されない。iframe 内の未改変 console へ転送する。
let nativeConsole: Console | null = null

function getNativeConsole(): Console {
  if (nativeConsole) return nativeConsole
  const iframe = document.createElement('iframe')
  iframe.style.display = 'none'
  // iframe をDOMに残したまま(detachすると多くのブラウザでDevToolsへの出力が止まる)
  document.documentElement.appendChild(iframe)
  const win = iframe.contentWindow
  if (!win) throw new Error('Failed to acquire native console')
  const nc = (win as unknown as { console: Console }).console
  nativeConsole = nc
  return nc
}

const pageOriginals: Record<ConsoleLevel, (...args: unknown[]) => void> = {
  log: console.log,
  warn: console.warn,
  error: console.error,
}

function forwardToConsole(level: ConsoleLevel, args: unknown[]) {
  const nc = getNativeConsole()
  const fn = nc[level] as (...args: unknown[]) => void
  fn.apply(nc, args)
}

const hooks: Record<ConsoleLevel, (...args: unknown[]) => void> = {
  log(...args: unknown[]) {
    forwardToConsole('log', args)
    if (enabled) pushEntry('console', 'log', formatArgs(args))
  },
  warn(...args: unknown[]) {
    forwardToConsole('warn', args)
    if (enabled) pushEntry('console', 'warn', formatArgs(args))
  },
  error(...args: unknown[]) {
    forwardToConsole('error', args)
    if (enabled) pushEntry('console', 'error', formatArgs(args))
  },
}

function formatArgs(args: unknown[]): string {
  return args.map(a => {
    if (typeof a === 'string') return a
    try { return JSON.stringify(a) }
    catch { return String(a) }
  }).join(' ')
}

function pushEntry(kind: string, level: string, message: string) {
  wsClient.send({
    type: 'debug_log_entry',
    kind,
    level,
    message,
    timestampMs: Date.now(),
  })
}

function describeElement(el: Element | null) {
  if (!el) return null
  const htmlEl = el as HTMLElement
  return {
    tag: el.tagName.toLowerCase(),
    className: el.className,
    dataId: el.getAttribute('data-id'),
    pointerEvents: htmlEl.style.pointerEvents || undefined,
    visibility: htmlEl.style.visibility || undefined,
    transform: htmlEl.style.transform || undefined,
  }
}

// クリック座標からrectまでの最短距離（rect内なら0）
function distanceToRect(px: number, py: number, rect: DOMRect): number {
  const dx = Math.max(rect.x - px, 0, px - (rect.x + rect.width))
  const dy = Math.max(rect.y - py, 0, py - (rect.y + rect.height))
  return Math.sqrt(dx * dx + dy * dy)
}

type ReactFlowNodeDump = {
  dataId: string | null
  rect?: { x: number; y: number; width: number; height: number }
  pointerEvents?: string
  visibility?: string
  transform?: string
}

function describeReactFlowNode(nodeEl: Element): ReactFlowNodeDump {
  const rect = nodeEl.getBoundingClientRect()
  const style = (nodeEl as HTMLElement).style
  const dump: ReactFlowNodeDump = { dataId: nodeEl.getAttribute('data-id') }
  if (nodeFields.rect) dump.rect = { x: rect.x, y: rect.y, width: rect.width, height: rect.height }
  if (nodeFields.pointerEvents) dump.pointerEvents = style.pointerEvents || undefined
  if (nodeFields.visibility) dump.visibility = style.visibility || undefined
  if (nodeFields.transform) dump.transform = style.transform || undefined
  return dump
}

function collectReactFlowNodes(clientX: number, clientY: number): ReactFlowNodeDump[] | undefined {
  if (detailLevel === 'minimal') return undefined

  const allNodes = Array.from(document.querySelectorAll('.react-flow__node'))
  if (detailLevel === 'full') return allNodes.map(describeReactFlowNode)

  // proximity: クリック座標から一定距離以内のノードのみ
  return allNodes
    .filter(nodeEl => distanceToRect(clientX, clientY, nodeEl.getBoundingClientRect()) <= PROXIMITY_RADIUS_PX)
    .map(describeReactFlowNode)
}

function collectDomEvent(event: MouseEvent) {
  if (!enabled) return

  const target = event.target instanceof Element ? event.target : null
  const hit = document.elementFromPoint(event.clientX, event.clientY)
  const reactFlowNodes = collectReactFlowNodes(event.clientX, event.clientY)

  const payload = {
    type: event.type,
    clientX: event.clientX,
    clientY: event.clientY,
    button: event.button,
    target: describeElement(target),
    elementFromPoint: describeElement(hit),
    reactFlowNodes,
  }

  pushEntry('dom_event', 'log', JSON.stringify(payload))
}

function attachDomListeners() {
  if (domListenersAttached) return
  document.addEventListener('mousedown', collectDomEvent, true)
  document.addEventListener('contextmenu', collectDomEvent, true)
  domListenersAttached = true
}

function detachDomListeners() {
  if (!domListenersAttached) return
  document.removeEventListener('mousedown', collectDomEvent, true)
  document.removeEventListener('contextmenu', collectDomEvent, true)
  domListenersAttached = false
}

function applyEnabled(next: boolean) {
  enabled = next
  if (next) {
    console.log = hooks.log
    console.warn = hooks.warn
    console.error = hooks.error
    attachDomListeners()
    forwardToConsole('log', ['[NGOL] Debug Bridge enabled'])
  } else {
    console.log = pageOriginals.log
    console.warn = pageOriginals.warn
    console.error = pageOriginals.error
    detachDomListeners()
    pageOriginals.log('[NGOL] Debug Bridge disabled')
  }
}

/** Install debug bridge infrastructure. Default is OFF until setDebugBridgeEnabled(true). */
export function installDebugBridge() {
  if (installed) return
  installed = true
}

export function setDebugBridgeEnabled(next: boolean) {
  if (!installed) installDebugBridge()
  if (enabled === next) return
  applyEnabled(next)
}

export function getDebugBridgeEnabled() {
  return enabled
}

export function setDebugDetailLevel(next: DebugDetailLevel) {
  detailLevel = next
}

export function getDebugDetailLevel() {
  return detailLevel
}

export function setDebugNodeField(field: DebugNodeField, value: boolean) {
  nodeFields = { ...nodeFields, [field]: value }
}

export function getDebugNodeFields() {
  return nodeFields
}
