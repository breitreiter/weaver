import { useMemo } from 'react'
import { type Board as BoardData } from './api'

// The board (middle pane / wall of red string) — a PASSIVE reflection of board
// state, hand-rolled SVG (same no-charting-lib idiom as MetricSparkline/TraceMini).
// It originates nothing: the only affordance is click a dot → focus it (URL
// ?focus=), which scrolls the evidence panel. Edge creation/deletion live in the
// drawer now; the graph just renders the pinned nodes + their edges. The viewBox is
// computed from node bounds, so the graph always fits its panel by construction —
// no fitView, no pan/zoom. See project/plans/graph-redesign.md.

// causal/temporal/custom edges are the operator's red string; dependency & route
// edges are tool-supplied facts (neutral).
const isRedString = (kind: string) => kind !== 'dependency' && kind !== 'route'

// depth (dependency layer) is the LOW-cardinality axis — services rarely chain
// more than a few deep — so map it to the scarce width; service fan-out (siblings
// at a depth) is high-cardinality, so stack it down the abundant height. Fits the
// narrow-but-tall board pane: a flat set of pins becomes a tall column, not a line.
const COL_W = 210  // width per depth column (room for a dot + its label)
const ROW_H = 40   // height per sibling row (dots stack cheaply)
const MAX_ROWS = 8 // a depth taller than this spills into an adjacent sub-column
const NODE_R = 7   // dot radius
const PAD = 48     // viewBox padding around the content
const CHAR_W = 7.5 // rough label glyph width, to keep labels inside the viewBox

type GNode = { id: string; x: number; y: number }
type GEdge = { id: string; x1: number; y1: number; x2: number; y2: number; red: boolean }

export default function Board({ board, focus, onFocus }: {
  board: BoardData | null
  focus: string | null
  onFocus: (svc: string) => void
}) {
  const g = useMemo(() => (board ? buildGraph(board) : null), [board])

  if (!g || g.nodes.length === 0) {
    return (
      <div className="board-overlay">
        <div className="board-empty-title">the board</div>
        <div>nothing pinned yet — pin a finding and it lands here.</div>
      </div>
    )
  }

  return (
    <svg className="board-svg" viewBox={g.viewBox} preserveAspectRatio="xMidYMid meet">
      <g className="board-edges">
        {g.edges.map(e => (
          <line key={e.id} className={'bedge' + (e.red ? ' red' : '')}
            x1={e.x1} y1={e.y1} x2={e.x2} y2={e.y2} />
        ))}
      </g>
      <g className="board-nodes">
        {g.nodes.map(n => (
          <g key={n.id} className={'bnode' + (n.id === focus ? ' focused' : '')}
            transform={`translate(${n.x},${n.y})`} onClick={() => onFocus(n.id)} role="button">
            <circle className="bnode-dot" r={NODE_R} />
            <text className="bnode-label" x={NODE_R + 6} dominantBaseline="middle">{n.id}</text>
          </g>
        ))}
      </g>
    </svg>
  )
}

// --- graph construction ---------------------------------------------------

function buildGraph(board: BoardData): { nodes: GNode[]; edges: GEdge[]; viewBox: string } {
  const services = board.nodes.map(n => n.serviceId)
  const present = new Set(services)

  // edges connect services directly; dedupe by pair+kind, drop any dangling end.
  const seen = new Set<string>()
  const links: { from: string; to: string; kind: string; id: string }[] = []
  for (const e of board.edges) {
    if (!present.has(e.from) || !present.has(e.to) || e.from === e.to) continue
    const key = `${e.from}->${e.to}->${e.kind}`
    if (seen.has(key)) continue
    seen.add(key)
    links.push({ from: e.from, to: e.to, kind: e.kind, id: e.id })
  }

  const pos = layout(services, links)
  const nodes: GNode[] = services.map(s => ({ id: s, x: pos.get(s)!.x, y: pos.get(s)!.y }))
  const byId = new Map(nodes.map(n => [n.id, n]))

  const edges: GEdge[] = links.map(l => {
    const a = byId.get(l.from)!, b = byId.get(l.to)!
    return { id: l.id, x1: a.x, y1: a.y, x2: b.x, y2: b.y, red: isRedString(l.kind) }
  })

  // viewBox from node bounds + a right-hand allowance for the labels (svg clips to
  // the viewBox), so the whole graph fits the panel by construction.
  let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity
  for (const n of nodes) {
    minX = Math.min(minX, n.x - NODE_R)
    minY = Math.min(minY, n.y - NODE_R)
    maxX = Math.max(maxX, n.x + NODE_R + 6 + n.id.length * CHAR_W)
    maxY = Math.max(maxY, n.y + NODE_R)
  }
  const vb = `${minX - PAD} ${minY - PAD} ${maxX - minX + PAD * 2} ${maxY - minY + PAD * 2}`
  return { nodes, edges, viewBox: vb }
}

// Deterministic depth layering (longest-path) — never force-directed. Transposed
// for the narrow-tall pane: depth runs left→right (few columns), siblings stack
// down (many rows). A flat set of pins → one tall column, not a wide line.
function layout(
  services: string[],
  links: { from: string; to: string }[],
): Map<string, { x: number; y: number }> {
  const adj = new Map<string, string[]>()
  const indeg = new Map<string, number>()
  for (const s of services) { adj.set(s, []); indeg.set(s, 0) }
  for (const l of links) {
    if (!adj.has(l.from) || !adj.has(l.to)) continue
    adj.get(l.from)!.push(l.to)
    indeg.set(l.to, indeg.get(l.to)! + 1)
  }

  // longest-path layering via Kahn; cycle nodes (never reach indeg 0) stay at 0.
  const depth = new Map(services.map(s => [s, 0]))
  const left = new Map(indeg)
  let frontier = services.filter(s => indeg.get(s) === 0).sort()
  const done = new Set<string>()
  while (frontier.length) {
    const next: string[] = []
    for (const n of frontier) {
      if (done.has(n)) continue
      done.add(n)
      for (const m of adj.get(n)!) {
        depth.set(m, Math.max(depth.get(m)!, depth.get(n)! + 1))
        left.set(m, left.get(m)! - 1)
        if (left.get(m)! <= 0) next.push(m)
      }
    }
    frontier = [...new Set(next)].sort()
  }

  // bucket by depth; each depth is a COLUMN, its services stacked down the height.
  // a depth taller than MAX_ROWS spills into an adjacent sub-column.
  const bands = new Map<number, string[]>()
  for (const s of services) {
    const d = depth.get(s)!
    ;(bands.get(d) ?? bands.set(d, []).get(d)!).push(s)
  }
  const pos = new Map<string, { x: number; y: number }>()
  let x = 0
  for (const d of [...bands.keys()].sort((a, b) => a - b)) {
    const band = bands.get(d)!.sort()
    const subCols = Math.ceil(band.length / MAX_ROWS)
    band.forEach((s, i) => {
      const sub = Math.floor(i / MAX_ROWS)
      const row = i % MAX_ROWS
      pos.set(s, { x: x + sub * COL_W, y: row * ROW_H })
    })
    x += Math.max(1, subCols) * COL_W
  }
  return pos
}
