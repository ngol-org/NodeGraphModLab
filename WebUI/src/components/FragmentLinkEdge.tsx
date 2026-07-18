import { getBezierPath, Position, type EdgeProps } from '@xyflow/react'

export function FragmentLinkEdge({
  id,
  sourceX,
  sourceY,
  sourcePosition,
  targetX,
  targetY,
  targetPosition,
  markerEnd,
  selected,
}: EdgeProps) {
  const [edgePath, labelX, labelY] = getBezierPath({
    sourceX,
    sourceY,
    sourcePosition: sourcePosition ?? Position.Right,
    targetX,
    targetY,
    targetPosition: targetPosition ?? Position.Left,
  })

  return (
    <>
      {/* 透明な広いヒット領域でクリックを確実に拾う */}
      <path
        d={edgePath}
        style={{ stroke: 'transparent', strokeWidth: 20, fill: 'none', cursor: 'pointer' }}
      />
      <path
        id={id}
        className="react-flow__edge-path fragment-link-edge-path"
        d={edgePath}
        markerEnd={markerEnd}
        style={{
          stroke: selected ? '#fbbf24' : 'var(--fragment-link-color, #f59e0b)',
          strokeWidth: selected ? 3 : 2,
          strokeDasharray: '8 4',
          fill: 'none',
          filter: selected ? 'drop-shadow(0 0 4px #fbbf24)' : undefined,
        }}
      />
      <text
        x={labelX}
        y={labelY - 8}
        textAnchor="middle"
        className="fragment-link-edge-label"
        style={{ fontSize: 9, fill: 'var(--fragment-link-color, #f59e0b)', pointerEvents: 'none' }}
      >
        FRAGMENT LINK
      </text>
    </>
  )
}
