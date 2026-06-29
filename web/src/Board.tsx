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

// ONE NODE PER ROW, stacked straight down. The pane is narrow and every label is
// horizontal text reaching rightward, so horizontal room is scarce — two nodes never
// share a row (their labels would collide). Vertical room is abundant (tall pane), so
// we spend it: a flat set of pins is a tidy column, a dependency chain an indented
// staircase. Dependency depth is only a small x-INDENT — a hierarchy cue, not a
// second axis competing for the width we don't have.
const ROW_H = 44   // vertical pitch per node (every node gets its own row)
const INDENT = 26  // x shift per dependency depth (the staircase step)
const NODE_R = 7   // dot radius
const PAD = 48     // viewBox padding around the content
const CHAR_W = 7.5 // rough label glyph width, to keep labels inside the viewBox
// floor on the viewBox: a sparse board (a single node) would otherwise be scaled up
// to fill the pane (preserveAspectRatio meet) and render huge. Hold a minimum extent
// so a small graph sits mid-pane at natural size. Tune to taste against the pane.
const MIN_VB_W = 480
const MIN_VB_H = 700

// the lit subgraph for a drawer hover — a set of node + edge ids. Path-shaped from
// day one (a trace lights many nodes + edges), never node-at-a-time. null = nothing
// hovered (the field shows at full strength).
export type Highlight = { nodeIds: string[]; edgeIds: string[] }

type GNode = { id: string; x: number; y: number }
type GEdge = { id: string; x1: number; y1: number; x2: number; y2: number; red: boolean }

export default function Board({ board, focus, highlight, onFocus }: {
  board: BoardData | null
  focus: string | null
  highlight: Highlight | null
  onFocus: (svc: string) => void
}) {
  const g = useMemo(() => (board ? buildGraph(board) : null), [board])
  // sets for O(1) lit lookup; memoised so a board poll (same data) doesn't churn.
  const lit = useMemo(() => ({
    nodes: new Set(highlight?.nodeIds ?? []),
    edges: new Set(highlight?.edgeIds ?? []),
  }), [highlight])

  if (!g || g.nodes.length === 0) {
    return (
      <div className="board-overlay">
        <div className="board-empty-title">the board</div>
        <div>nothing pinned yet — pin a finding and it lands here.</div>
      </div>
    )
  }

  // spotlight, not paint: when something's hovered, dim the field and let the lit
  // subgraph emerge. Dimming is recession (still legible), not concealment.
  return (
    <svg className={'board-svg' + (highlight ? ' dimmed' : '')}
      viewBox={g.viewBox} preserveAspectRatio="xMidYMid meet">
      <g className="board-edges">
        {g.edges.map(e => (
          <line key={e.id} className={'bedge' + (e.red ? ' red' : '') + (lit.edges.has(e.id) ? ' lit' : '')}
            x1={e.x1} y1={e.y1} x2={e.x2} y2={e.y2} />
        ))}
      </g>
      <g className="board-nodes">
        {g.nodes.map(n => (
          <g key={n.id} className={'bnode' + (n.id === focus ? ' focused' : '') + (lit.nodes.has(n.id) ? ' lit' : '')}
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
  // the viewBox). Floored to a minimum extent and centred, so a sparse board renders
  // at natural size rather than ballooning to fill the pane.
  let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity
  for (const n of nodes) {
    minX = Math.min(minX, n.x - NODE_R)
    minY = Math.min(minY, n.y - NODE_R)
    maxX = Math.max(maxX, n.x + NODE_R + 6 + n.id.length * CHAR_W)
    maxY = Math.max(maxY, n.y + NODE_R)
  }
  const contentW = maxX - minX, contentH = maxY - minY
  const vbW = Math.max(contentW + PAD * 2, MIN_VB_W)
  const vbH = Math.max(contentH + PAD * 2, MIN_VB_H)
  const vbX = minX - (vbW - contentW) / 2
  const vbY = minY - (vbH - contentH) / 2
  return { nodes, edges, viewBox: `${vbX} ${vbY} ${vbW} ${vbH}` }
}

// Deterministic depth layering (longest-path) — never force-directed. Depth only
// sets the horizontal indent; every node still takes its own row, read top→bottom in
// (depth, name) order. A flat set of pins → a straight column; a chain → a staircase.
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

  // one row per node, read top→bottom in (depth, name) order; x is the depth indent.
  const ordered = [...services].sort((a, b) =>
    (depth.get(a)! - depth.get(b)!) || (a < b ? -1 : a > b ? 1 : 0))
  const pos = new Map<string, { x: number; y: number }>()
  ordered.forEach((s, row) => pos.set(s, { x: depth.get(s)! * INDENT, y: row * ROW_H }))
  return pos
}
