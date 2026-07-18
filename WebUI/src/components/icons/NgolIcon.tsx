import type { NgolIconName } from '../../icons/iconDefs'
import { NGOL_ICON_DEFS } from '../../icons/iconDefs'

export type { NgolIconName } from '../../icons/iconDefs'

interface NgolIconProps {
  name: NgolIconName
  size?: number
  className?: string
  title?: string
}

export function NgolIcon({ name, size = 16, className, title }: NgolIconProps) {
  const def = NGOL_ICON_DEFS[name]
  return (
    <svg
      className={className}
      width={size}
      height={size}
      viewBox={def.viewBox}
      aria-hidden={title ? undefined : true}
      role={title ? 'img' : undefined}
    >
      {title ? <title>{title}</title> : null}
      {def.elements.map((el, i) => {
        const attrs = el.attrs
        switch (el.kind) {
          case 'path':
            return <path key={i} {...attrs} />
          case 'polygon':
            return <polygon key={i} {...attrs} />
          case 'rect':
            return <rect key={i} {...attrs} />
          case 'circle':
            return <circle key={i} {...attrs} />
          default:
            return null
        }
      })}
    </svg>
  )
}
