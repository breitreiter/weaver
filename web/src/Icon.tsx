// Material Symbols (rounded). Self-hosted via the `material-symbols` package —
// no runtime CDN. The name is the symbol's ligature, e.g. <Icon name="graph_3" />.
export function Icon({ name, size = 18, className = '' }: { name: string; size?: number; className?: string }) {
  return (
    <span className={`material-symbols-rounded ${className}`} aria-hidden style={{ fontSize: size }}>
      {name}
    </span>
  )
}
