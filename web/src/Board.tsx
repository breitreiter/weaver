import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  ReactFlow, ReactFlowProvider, Background, BackgroundVariant, Controls,
  Handle, Position, useReactFlow,
  type Node, type Edge, type NodeProps, type Connection,
} from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { api, type Board as BoardData, type Relationship } from './api'
import { Icon } from './Icon'

// The board (middle pane / wall of red string). React Flow: render the pinned
// nodes + edges, pan/zoom, auto-tidied layout. Edges are drawn human-side
// (drag node→node → relationship modal) or agent-side (CLI `weaver link`); either
// can be CROSSED OUT — the demo's payoff: cut the red string after exoneration.
// Clicking a node focuses it (URL ?focus=), scrolling the evidence panel to it.
// See project/plans/board-build.md. Manual node placement is still deferred.
//
// Board data is fetched once in the Workbench and passed in, so the board and
// evidence panels read one always-in-sync copy. The server returns one node per
// service, each carrying its layered evidence; chips summarize that evidence.

// evidence kinds only — a node is a service (no 'node' kind). The chip says what
// kind of finding is layered on the service.
const KIND_ICON: Record<string, string> = {
  anomaly: 'warning', log: 'description',
  trace: 'account_tree', metric: 'monitoring', change: 'deployed_code_update',
}

// causal/temporal/custom edges are the operator's red string; dependency & route
// edges are tool-supplied facts (neutral). Anything else reads as string.
const isRedString = (kind: string) => kind !== 'dependency' && kind !== 'route'

// freeform kinds — the operator's own hypothesis, for when the data doesn't hold
// the relationship they suspect (the modal leads with the real relationships).
const FREEFORM_KINDS = [
  { kind: 'causal', label: 'causes / explains' },
  { kind: 'temporal', label: 'precedes (temporal)' },
  { kind: 'custom', label: 'custom' },
]

const COL_W = 240
const ROW_H = 116
const MAX_ROWS = 6 // a column taller than this wraps into a grid band

type ServiceNodeData = {
  service: string
  chips: { kind: string; count: number }[]
  evidenceCount: number
}
type EdgeData = { crossedOut: boolean; drawnBy: string; kind: string }

type EvidenceNode = Node<ServiceNodeData, 'service'>
type SelectedEdge = { id: string; crossedOut: boolean; label: string; source: string; target: string }

export default function Board({ boardId, board, reload, focus, onFocus }: {
  boardId: string
  board: BoardData | null
  reload: () => void
  focus: string | null
  onFocus: (svc: string) => void
}) {
  const [connect, setConnect] = useState<{ source: string; target: string } | null>(null)
  const [selected, setSelected] = useState<SelectedEdge | null>(null)

  const { nodes, edges } = useMemo(() => {
    const g = board ? buildGraph(board) : { nodes: [], edges: [] }
    // reflect the focused node as React Flow selection (highlight)
    return { nodes: g.nodes.map(n => ({ ...n, selected: n.id === focus })), edges: g.edges }
  }, [board, focus])

  const onConnect = useCallback((c: Connection) => {
    if (c.source && c.target && c.source !== c.target) setConnect({ source: c.source, target: c.target })
  }, [])

  const onEdgeClick = useCallback((_: React.MouseEvent, edge: Edge) => {
    const d = edge.data as EdgeData | undefined
    setSelected({
      id: edge.id, crossedOut: !!d?.crossedOut, source: edge.source, target: edge.target,
      label: typeof edge.label === 'string' ? edge.label : '',
    })
  }, [])

  // from/to come from the chosen relationship (real direction), not the drag.
  async function drawEdge(from: string, to: string, kind: string, label: string) {
    await api.link(boardId, { from, to, kind, label: label.trim() || undefined, drawnBy: 'human' })
    setConnect(null); reload()
  }
  async function toggleCross() {
    if (!selected) return
    await api.crossOut(boardId, selected.id, !selected.crossedOut)
    setSelected(null); reload()
  }
  async function removeEdge() {
    if (!selected) return
    await api.deleteEdge(boardId, selected.id)
    setSelected(null); reload()
  }

  return (
    <div className="board-flow">
      <ReactFlowProvider>
        <Flow nodes={nodes} edges={edges} onConnect={onConnect} onEdgeClick={onEdgeClick}
          onNodeClick={(_, n) => onFocus(n.id)} onPaneClick={() => setSelected(null)} />
      </ReactFlowProvider>

      {board && nodes.length === 0 && (
        <div className="board-overlay">
          <div className="board-empty-title">the board</div>
          <div>nothing pinned yet — pin a finding and it lands here.</div>
        </div>
      )}

      {selected && (
        <EdgeToolbar selected={selected} onCross={toggleCross} onRemove={removeEdge} onClose={() => setSelected(null)} />
      )}
      {connect && (
        <RelationshipModal source={connect.source} target={connect.target}
          onDraw={drawEdge} onCancel={() => setConnect(null)} />
      )}
    </div>
  )
}

const nodeTypes = { service: ServiceNode }

function Flow({ nodes, edges, onConnect, onEdgeClick, onNodeClick, onPaneClick }: {
  nodes: EvidenceNode[]; edges: Edge[]
  onConnect: (c: Connection) => void
  onEdgeClick: (e: React.MouseEvent, edge: Edge) => void
  onNodeClick: (e: React.MouseEvent, node: EvidenceNode) => void
  onPaneClick: () => void
}) {
  const { fitView } = useReactFlow()
  const had = useRef(0)
  // fit once when the first nodes arrive (and again if a board goes empty→full).
  useEffect(() => {
    if (had.current === 0 && nodes.length > 0) fitView({ duration: 300, padding: 0.2 })
    had.current = nodes.length
  }, [nodes.length, fitView])

  return (
    <ReactFlow
      nodes={nodes}
      edges={edges}
      nodeTypes={nodeTypes}
      onConnect={onConnect}
      onEdgeClick={onEdgeClick}
      onNodeClick={onNodeClick}
      onPaneClick={onPaneClick}
      fitView
      nodesDraggable={false}
      nodesConnectable
      proOptions={{ hideAttribution: true }}
      minZoom={0.2}
    >
      <Background variant={BackgroundVariant.Dots} gap={24} size={1} color="#20242e" />
      <Controls showInteractive={false} />
    </ReactFlow>
  )
}

function ServiceNode({ data }: NodeProps<EvidenceNode>) {
  return (
    <div className="bnode">
      <Handle type="target" position={Position.Top} />
      <div className="bnode-head">
        <Icon name="deployed_code" size={16} className="bnode-ico" />
        <span className="bnode-title mono">{data.service}</span>
      </div>
      {data.chips.length > 0 && (
        <div className="bnode-chips">
          {data.chips.map(c => (
            <span key={c.kind} className={`bchip bchip-${c.kind}`} title={`${c.count} ${c.kind}`}>
              <Icon name={KIND_ICON[c.kind] ?? 'help'} size={13} />
              {c.count > 1 && <span className="bchip-n">{c.count}</span>}
            </span>
          ))}
        </div>
      )}
      <Handle type="source" position={Position.Bottom} />
    </div>
  )
}

// floating toolbar for a selected edge — cross out (the climax) / remove.
function EdgeToolbar({ selected, onCross, onRemove, onClose }: {
  selected: SelectedEdge; onCross: () => void; onRemove: () => void; onClose: () => void
}) {
  return (
    <div className="edge-toolbar">
      <span className="edge-toolbar-label mono">
        {selected.source} → {selected.target}{selected.label ? `  ·  ${selected.label}` : ''}
      </span>
      <button className="etb-btn" onClick={onCross}>
        <Icon name={selected.crossedOut ? 'undo' : 'content_cut'} size={15} />
        {selected.crossedOut ? 'restore' : 'cross out'}
      </button>
      <button className="etb-btn etb-danger" onClick={onRemove}><Icon name="delete" size={15} />remove</button>
      <button className="etb-btn" onClick={onClose}><Icon name="close" size={15} /></button>
    </div>
  )
}

// the relationship modal — drag node→node to draw a line. Leads with the REAL
// relationships the data holds between the two nodes (pick one to ground your
// hypothesis in a fact); falls back to a freeform assertion when none fit.
type Pick = { rel: Relationship } | { free: string }

function RelationshipModal({ source, target, onDraw, onCancel }: {
  source: string; target: string
  onDraw: (from: string, to: string, kind: string, label: string) => void
  onCancel: () => void
}) {
  const [rels, setRels] = useState<Relationship[] | null>(null)
  const [pick, setPick] = useState<Pick | null>(null)
  const [label, setLabel] = useState('')

  useEffect(() => {
    let live = true
    api.relationships(source, target)
      .then(r => { if (live) setRels(r.relationships) })
      .catch(() => { if (live) setRels([]) })
    return () => { live = false }
  }, [source, target])

  const chooseRel = (rel: Relationship) => { setPick({ rel }); setLabel(rel.suggestedLabel) }
  const chooseFree = (kind: string) => { setPick({ free: kind }); setLabel('') }

  // a freeform 'custom' edge needs a label to mean anything; everything else can draw bare.
  const ready = pick !== null && !('free' in pick && pick.free === 'custom' && !label.trim())
  function draw() {
    if (!pick) return
    if ('rel' in pick) onDraw(pick.rel.from, pick.rel.to, pick.rel.edgeKind, label)
    else onDraw(source, target, pick.free, label)
  }

  return (
    <div className="modal-scrim" onClick={onCancel}>
      <div className="modal modal-rel" onClick={e => e.stopPropagation()}>
        <div className="modal-title">draw a line</div>
        <div className="modal-sub mono">{source} ↔ {target}</div>

        <div className="rel-section">real relationships in the data</div>
        {rels === null && <div className="rel-empty">finding relationships…</div>}
        {rels?.length === 0 && <div className="rel-empty">none recorded between these two — assert your own below.</div>}
        {rels && rels.length > 0 && (
          <div className="rel-list">
            {rels.map((r, i) => {
              const active = pick !== null && 'rel' in pick && pick.rel === r
              return (
                <button key={i} className={'rel-row' + (active ? ' active' : '')} onClick={() => chooseRel(r)}>
                  <span className="rel-row-top">
                    <span className={`rel-badge rel-badge-${r.group}`}>{r.group}</span>
                    <span className="rel-dir mono">{r.from} → {r.to}</span>
                    <span className="rel-title">{r.title}</span>
                  </span>
                  <span className="rel-detail">{r.detail}</span>
                </button>
              )
            })}
          </div>
        )}

        <div className="rel-section">— or assert your own —</div>
        <div className="modal-kinds">
          {FREEFORM_KINDS.map(k => {
            const active = pick !== null && 'free' in pick && pick.free === k.kind
            return (
              <button key={k.kind} className={'mk mk-red' + (active ? ' active' : '')}
                onClick={() => chooseFree(k.kind)}>{k.label}</button>
            )
          })}
        </div>

        <input className="modal-input mono" placeholder="label (e.g. deployed-just-before?)"
          value={label} onChange={e => setLabel(e.target.value)}
          onKeyDown={e => { if (e.key === 'Enter' && ready) draw() }} />
        <div className="modal-actions">
          <button className="run" onClick={onCancel}>cancel</button>
          <button className="run modal-primary" disabled={!ready} onClick={draw}>draw the line</button>
        </div>
      </div>
    </div>
  )
}

// --- graph construction ---------------------------------------------------

type Link = { from: string; to: string; kind: string; label?: string; drawnBy: string; crossedOut: boolean; id: string }

function buildGraph(board: BoardData): { nodes: EvidenceNode[]; edges: Edge[] } {
  const services = board.nodes.map(n => n.serviceId)
  const present = new Set(services)

  // edges connect services directly; dedupe by pair+kind, drop any dangling end.
  const seen = new Set<string>()
  const links: Link[] = []
  for (const e of board.edges) {
    if (!present.has(e.from) || !present.has(e.to) || e.from === e.to) continue
    const key = `${e.from}->${e.to}->${e.kind}`
    if (seen.has(key)) continue
    seen.add(key)
    links.push({ from: e.from, to: e.to, kind: e.kind, label: e.label, drawnBy: e.drawnBy, crossedOut: e.crossedOut, id: e.id })
  }

  const pos = layout(services, links)

  const nodes: EvidenceNode[] = board.nodes.map(n => {
    const counts = new Map<string, number>()
    for (const ev of n.evidence) counts.set(ev.kind, (counts.get(ev.kind) ?? 0) + 1)
    const chips = [...counts.entries()].map(([kind, count]) => ({ kind, count }))
    return { id: n.serviceId, type: 'service', position: pos.get(n.serviceId)!, data: { service: n.serviceId, chips, evidenceCount: n.evidence.length } }
  })

  const edges: Edge[] = links.map(l => {
    const red = isRedString(l.kind)
    const cut = l.crossedOut
    return {
      id: l.id,
      source: l.from,
      target: l.to,
      label: l.label ?? (red ? l.kind : undefined),
      animated: red && !cut,
      data: { crossedOut: cut, drawnBy: l.drawnBy, kind: l.kind } satisfies EdgeData,
      className: [red ? 'edge-string' : 'edge-dep', cut ? 'edge-cut' : '', `edge-${l.drawnBy}`].filter(Boolean).join(' '),
      style: cut
        ? { stroke: 'var(--up)', strokeWidth: 2, opacity: 0.4, strokeDasharray: '2 5' }
        : red
          ? { stroke: 'var(--up)', strokeWidth: 2 }
          : { stroke: 'var(--text-dim)', strokeWidth: 1 },
    }
  })

  return { nodes, edges }
}

// Deterministic, layered left→right by dependency depth — never force-directed.
// Tall columns (e.g. many unconnected pins, all depth 0) wrap into a grid band.
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

  // bucket by depth, place each column; wrap tall columns into sub-columns.
  const columns = new Map<number, string[]>()
  for (const s of services) {
    const d = depth.get(s)!
    ;(columns.get(d) ?? columns.set(d, []).get(d)!).push(s)
  }
  const pos = new Map<string, { x: number; y: number }>()
  let x = 0
  for (const d of [...columns.keys()].sort((a, b) => a - b)) {
    const col = columns.get(d)!.sort()
    const subCols = Math.ceil(col.length / MAX_ROWS)
    col.forEach((s, i) => {
      const sub = Math.floor(i / MAX_ROWS)
      const row = i % MAX_ROWS
      pos.set(s, { x: x + sub * COL_W, y: row * ROW_H })
    })
    x += Math.max(1, subCols) * COL_W
  }
  return pos
}
