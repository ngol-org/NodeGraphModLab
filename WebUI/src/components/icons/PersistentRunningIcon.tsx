interface PersistentRunningIconProps {
  size?: number
  className?: string
}

/** Loop + play — continuous persistent execution (not settings gear). */
export function PersistentRunningIcon({ size = 18, className }: PersistentRunningIconProps) {
  return (
    <svg
      className={className}
      width={size}
      height={size}
      viewBox="0 0 24 24"
      aria-hidden
    >
      <path
        className="persistent-running-icon-arc"
        d="M12 4.5a7.5 7.5 0 1 1-5.3 2.2"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
      />
      <path
        d="M6.7 6.7 4.5 4.5M4.5 4.5h3M4.5 4.5v3"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
      <path
        className="persistent-running-icon-play"
        d="M10.2 9.2v5.6l4.8-2.8z"
        fill="currentColor"
      />
    </svg>
  )
}
