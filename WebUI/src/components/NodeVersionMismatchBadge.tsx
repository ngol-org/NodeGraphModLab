interface Props {
  savedVersion: string
  currentVersion: string
}

export function NodeVersionMismatchBadge({ savedVersion, currentVersion }: Props) {
  return (
    <span
      title={`Graph saved with v${savedVersion}, current node type is v${currentVersion}. Execution continues.`}
      className="node-version-mismatch-badge"
      style={{
        marginLeft: 4,
        background: '#ff9800',
        borderRadius: 3,
        padding: '0 4px',
        fontSize: 9,
        color: '#fff',
        verticalAlign: 'middle',
        fontFamily: 'monospace',
        letterSpacing: '-0.02em',
      }}
    >
      v{savedVersion}→{currentVersion}
    </span>
  )
}
