import { useRef, useEffect, useState, useMemo } from 'react'
import type { LogEntry } from '../hooks/useGraphEditor'
import { NgolIcon } from './icons/NgolIcon'
import './ExecutionLogPanel.css'

interface Props {
  logs: LogEntry[]
  onClear: () => void
  onCollapse?: () => void
}

type LevelFilter = 'all' | 'debug' | 'info' | 'warning' | 'error'
type CategoryFilter = 'all' | 'exec' | 'system' | 'notify'

const LEVEL_COLORS: Record<string, string> = {
  debug:   'var(--text-dim)',
  info:    'var(--text-primary)',
  warning: 'var(--warning)',
  error:   'var(--error)',
}

const CATEGORY_BADGE: Record<string, { bg: string; label: string }> = {
  exec:   { bg: 'var(--accent)',  label: 'EXEC' },
  system: { bg: '#4a9eff',       label: 'SYS'  },
  notify: { bg: '#c084fc',       label: 'NTFY' },
}

export function ExecutionLogPanel({ logs, onClear, onCollapse }: Props) {
  const bottomRef = useRef<HTMLDivElement>(null)
  const [levelFilter, setLevelFilter] = useState<LevelFilter>('all')
  const [categoryFilter, setCategoryFilter] = useState<CategoryFilter>('all')
  const [autoScroll, setAutoScroll] = useState(true)

  const filteredLogs = useMemo(() => {
    let result = logs
    if (categoryFilter !== 'all') result = result.filter(l => l.category === categoryFilter)
    if (levelFilter !== 'all') result = result.filter(l => l.level === levelFilter)
    return result
  }, [logs, categoryFilter, levelFilter])

  useEffect(() => {
    if (autoScroll) bottomRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [filteredLogs.length, autoScroll])

  return (
    <div className="log-panel">
      <div className="log-panel-header">
        <span>Activity Log</span>
        <span style={{ flex: 1 }} />
        {/* カテゴリフィルター */}
        <select
          value={categoryFilter}
          onChange={e => setCategoryFilter(e.target.value as CategoryFilter)}
          style={{ fontSize: 10, padding: '1px 4px', marginRight: 4, background: 'var(--bg-secondary)', color: 'var(--text-secondary)', border: '1px solid var(--border)' }}
          title="Filter by category"
        >
          <option value="all">ALL CAT</option>
          <option value="exec">EXEC</option>
          <option value="system">SYS</option>
          <option value="notify">NOTIFY</option>
        </select>
        {/* レベルフィルター */}
        <select
          value={levelFilter}
          onChange={e => setLevelFilter(e.target.value as LevelFilter)}
          style={{ fontSize: 10, padding: '1px 4px', marginRight: 8, background: 'var(--bg-secondary)', color: 'var(--text-secondary)', border: '1px solid var(--border)' }}
          title="Filter by level"
        >
          <option value="all">ALL LVL</option>
          <option value="debug">DEBUG</option>
          <option value="info">INFO</option>
          <option value="warning">WARN</option>
          <option value="error">ERROR</option>
        </select>
        {/* オートスクロールトグル */}
        <button
          onClick={() => setAutoScroll(v => !v)}
          style={{ padding: '2px 8px', fontSize: 10, marginRight: 4, borderColor: autoScroll ? 'var(--accent)' : undefined }}
          title={autoScroll ? 'Auto-scroll ON' : 'Auto-scroll OFF'}
        >
          {autoScroll ? '\u2b07 Auto' : '\u2b07 Off'}
        </button>
        <span style={{ fontSize: 10, color: 'var(--text-dim)', marginRight: 8 }}>
          {filteredLogs.length}/{logs.length}
        </span>
        <button onClick={onClear} style={{ padding: '2px 8px', fontSize: 10 }}>Clear</button>
        {onCollapse && (
          <button
            onClick={onCollapse}
            style={{ padding: '2px 4px', display: 'flex', alignItems: 'center' }}
            title="Collapse log panel"
          >
            <NgolIcon name="chevron-down" size={12} />
          </button>
        )}
      </div>
      <div className="log-entries">
        {filteredLogs.map(entry => {
          const catBadge = CATEGORY_BADGE[entry.category] ?? CATEGORY_BADGE.exec
          return (
            <div
              key={entry.localId}
              className={`log-entry ${entry.level}`}
              style={{ color: LEVEL_COLORS[entry.level] }}
            >
              <span style={{ color: 'var(--text-dim)', marginRight: 6, fontSize: 10 }}>
                {new Date(entry.timestampMs).toLocaleTimeString()}
              </span>
              {/* カテゴリバッジ */}
              <span style={{
                fontSize: 9,
                padding: '0 3px',
                borderRadius: 2,
                marginRight: 4,
                background: catBadge.bg,
                color: '#fff',
                opacity: 0.9,
              }}>
                {catBadge.label}
              </span>
              {/* レベルバッジ */}
              <span style={{
                fontSize: 9,
                padding: '0 3px',
                borderRadius: 2,
                marginRight: 4,
                background: LEVEL_COLORS[entry.level],
                color: entry.level === 'debug' || entry.level === 'info' ? 'var(--bg-primary)' : '#fff',
                opacity: 0.85,
              }}>
                {entry.level.toUpperCase()}
              </span>
              {entry.nodeInstanceId && (
                <span style={{ color: 'var(--accent-secondary)', marginRight: 6, fontSize: 10 }}>
                  [{entry.nodeInstanceId.slice(-8)}]
                </span>
              )}
              {entry.message}
            </div>
          )
        })}
        <div ref={bottomRef} />
      </div>
    </div>
  )
}

