import { useEffect, useMemo, useRef } from 'react'
import { type Board as BoardData, type BoardItem, type BoardEdge } from './api'
import { Icon } from './Icon'

// The evidence panel (right pane) — a persistent, scrollable narrative of the
// whole board: every pin, grouped by node, rendered as a plain per-kind summary,
// with the red string each node throws. The graph is the index; clicking a node
// scrolls this panel to it (?focus=<svc>). Charts are a later enrichment — for
// now the evidence reads as text (the anomaly delta, the log line, the change
// annotation). See the chart-wall.md DECISION. Same `getBoard` data the board
// renders, so the two panels never disagree.

const KIND_ICON: Record<string, string> = {
  node: 'deployed_code', anomaly: 'warning', log: 'description',
  trace: 'account_tree', metric: 'monitoring', change: 'deployed_code_update',
}
const badgeClass = (kind: string) => `badge-${kind === 'node' ? 'service' : kind}`

type NodeGroup = { service: string; label?: string; items: BoardItem[]; out: BoardEdge[] }

export default function Evidence({ board, focus, onFocus }: {
  board: BoardData | null
  focus: string | null
  onFocus: (svc: string) => void
}) {
  const groups = useMemo(() => board ? groupByNode(board) : [], [board])

  // scroll the focused node's section into view when focus changes.
  const ref = useRef<HTMLDivElement>(null)
  useEffect(() => {
    if (!focus) return
    ref.current?.querySelector(`#ev-${cssId(focus)}`)?.scrollIntoView({ behavior: 'smooth', block: 'start' })
  }, [focus])

  return (
    <div className="evidence-pane" ref={ref}>
      <div className="evidence-head">
        <span className="evidence-title">evidence</span>
        <span className="evidence-sub">{board ? `${board.items.length} pins · ${groups.length} nodes` : ''}</span>
      </div>

      {!board && <div className="hint">no board yet — pin a finding to begin the narrative.</div>}
      {board && groups.length === 0 && <div className="hint">nothing pinned yet.</div>}

      {groups.map(g => (
        <section key={g.service} id={`ev-${cssId(g.service)}`}
          className={'ev-node' + (g.service === focus ? ' focused' : '')}
          onClick={() => onFocus(g.service)}>
          <div className="ev-node-head">
            <Icon name="deployed_code" size={16} className="bnode-ico" />
            <span className="ev-node-title mono">{g.service}</span>
            {g.label && <span className="ev-node-label">{g.label}</span>}
          </div>

          {g.items.filter(i => i.kind !== 'node').map(it => (
            <div key={it.id} className="ev-item">
              <span className={`badge ${badgeClass(it.kind)}`} title={it.kind}>
                <Icon name={KIND_ICON[it.kind] ?? 'help'} size={13} /> {it.kind}
              </span>
              <div className="ev-item-body">
                {it.label && <div className="ev-item-label">{it.label}</div>}
                <div className="ev-item-sub mono">{summarize(it.kind, it.evidence)}</div>
              </div>
            </div>
          ))}

          {g.out.length > 0 && (
            <div className="ev-strings">
              {g.out.map(e => (
                <div key={e.id} className={'ev-string' + (e.crossedOut ? ' cut' : '')}>
                  <Icon name={e.crossedOut ? 'content_cut' : 'trending_flat'} size={14} />
                  <span className="mono">→ {targetService(board!, e)}</span>
                  <span className="ev-string-kind">{e.kind}{e.label ? `: ${e.label}` : ''}</span>
                  {e.crossedOut && <span className="ev-string-cut">cut</span>}
                </div>
              ))}
            </div>
          )}
        </section>
      ))}
    </div>
  )
}

// --- grouping + summaries -------------------------------------------------

function groupByNode(board: BoardData): NodeGroup[] {
  const itemToService = new Map<string, string>()
  const order: string[] = []
  const map = new Map<string, NodeGroup>()
  for (const it of board.items) {
    const svc = it.ref || '(fleet)'
    itemToService.set(it.id, svc)
    if (!map.has(svc)) { map.set(svc, { service: svc, items: [], out: [] }); order.push(svc) }
    const g = map.get(svc)!
    g.items.push(it)
    if (it.kind === 'node' && it.label && !g.label) g.label = it.label
  }
  const resolve = (refId: string) => itemToService.get(refId) ?? (map.has(refId) ? refId : undefined)
  for (const e of board.edges) {
    const from = resolve(e.fromItem)
    if (from && map.has(from)) map.get(from)!.out.push(e)
  }
  return order.map(s => map.get(s)!)
}

function targetService(board: BoardData, e: BoardEdge): string {
  const it = board.items.find(i => i.id === e.toItem)
  return it?.ref || e.toItem
}

// per-kind one-liner over the pinned evidence payload (camelCase on the wire).
// Defensive: payloads vary (UI search results vs CLI `--note`), so fall back to
// the label rather than throw on a missing field.
function summarize(kind: string, ev: unknown): string {
  if (!ev || typeof ev !== 'object') return ''
  const p = ev as Record<string, unknown>
  switch (kind) {
    case 'anomaly': {
      const dir = p.direction === 'up' ? '↑' : p.direction === 'down' ? '↓' : ''
      const pct = typeof p.deltaPct === 'number' ? pctStr(p.deltaPct) : ''
      const z = typeof p.z === 'number' ? ` (z≈${Math.round(p.z)})` : ''
      return [p.metric, dir, pct].filter(Boolean).join(' ') + z
    }
    case 'log':
      return [str(p.level).toUpperCase(), str(p.templateId)].filter(Boolean).join(' ')
        + (p.message ? ` — ${str(p.message)}` : '')
    case 'change':
      return str(p.summary) || str(p.kind)
    case 'metric':
      return [str(p.shapeCode), str(p.prose)].filter(Boolean).join(' · ')
    case 'trace':
      return [str(p.route) || str(p.requestTypeId), p.durationMs != null ? `${p.durationMs}ms` : '', str(p.status)]
        .filter(Boolean).join(' · ')
    default:
      return str(p.note) || ''
  }
}

const str = (v: unknown) => (v == null ? '' : String(v))
const pctStr = (n: number) => `${n >= 0 ? '+' : ''}${Math.round(n).toLocaleString()}%`
// React Flow node ids are service ids (safe chars), but guard the selector anyway.
const cssId = (s: string) => s.replace(/[^a-zA-Z0-9_-]/g, '_')
