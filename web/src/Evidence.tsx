import { useEffect, useMemo, useRef } from 'react'
import { type Board as BoardData, type EvidenceItem, type BoardEdge } from './api'
import { Icon } from './Icon'

// The evidence panel (right pane) — a persistent, scrollable narrative of the
// whole board. Each service is a SECTION HEADER (not a card); the evidence layered
// onto it hangs below as cards, each bordered in its kind's colour with the kind
// icon promoted to the lead. The graph is the index; clicking a service scrolls
// this panel to it (?focus=<svc>). Same `getBoard` data the board renders, so the
// two panels never disagree.

const KIND_ICON: Record<string, string> = {
  anomaly: 'warning', log: 'description',
  trace: 'account_tree', metric: 'monitoring', change: 'deployed_code_update',
}

// service-level "find more for this node" buttons — all five legal scopes, each
// fires a fresh single-service search in the left panel (see exploreService).
// traces is the spanning move (pulls in adjacent services); the rest deepen the
// node already on the board.
const EXPLORE: { scope: string; icon: string; label: string }[] = [
  { scope: 'metrics', icon: 'monitoring', label: 'metrics' },
  { scope: 'anomalies', icon: 'warning', label: 'anomalies' },
  { scope: 'logs', icon: 'description', label: 'logs' },
  { scope: 'traces', icon: 'account_tree', label: 'traces' },
  { scope: 'changes', icon: 'deployed_code_update', label: 'changes' },
]

type NodeGroup = { service: string; label?: string; evidence: EvidenceItem[]; out: BoardEdge[] }

export default function Evidence({ board, focus, onFocus, onExplore, onDeleteEvidence, onDeleteService }: {
  board: BoardData | null
  focus: string | null
  onFocus: (svc: string) => void
  onExplore: (scope: string, service: string, facets?: Record<string, string>) => void
  onDeleteEvidence: (evidenceId: string) => void
  onDeleteService: (service: string) => void
}) {
  const groups = useMemo(() => board ? groupByService(board) : [], [board])
  const evidenceCount = useMemo(() => groups.reduce((n, g) => n + g.evidence.length, 0), [groups])

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
        <span className="evidence-sub">{board ? `${groups.length} ${groups.length === 1 ? 'service' : 'services'} · ${evidenceCount} evidence` : ''}</span>
      </div>

      {!board && <div className="hint">no board yet — pin a finding to begin the narrative.</div>}
      {board && groups.length === 0 && <div className="hint">nothing pinned yet.</div>}

      {groups.map(g => {
        const real = g.service !== '(fleet)'
        return (
          <section key={g.service} id={`ev-${cssId(g.service)}`}
            className={'ev-service' + (g.service === focus ? ' focused' : '')}
            onClick={() => onFocus(g.service)}>
            <div className="ev-service-head">
              <Icon name="deployed_code" size={16} className="bnode-ico" />
              <span className="ev-service-title mono">{g.service}</span>
              {g.label && <span className="ev-service-label">{g.label}</span>}
              {real && (
                <button className="ev-trash" title={`remove ${g.service} and all its evidence`}
                  onClick={e => { e.stopPropagation(); onDeleteService(g.service) }}>
                  <Icon name="delete" size={15} />
                </button>
              )}
            </div>

            {real && (
              <div className="ev-explore" onClick={e => e.stopPropagation()}>
                {EXPLORE.map(x => (
                  <button key={x.scope} className="ev-explore-btn"
                    onClick={() => onExplore(x.scope, g.service)}
                    title={`search ${x.label} for ${g.service}`}>
                    <Icon name={x.icon} size={12} /> {x.label}
                  </button>
                ))}
              </div>
            )}

            {g.evidence.map(ev => {
              const searches = real ? itemSearches(ev) : []
              return (
                <div key={ev.id} className={`ev-item kind-${ev.kind}`}>
                  <Icon name={KIND_ICON[ev.kind] ?? 'help'} size={18} className="ev-item-ico" />
                  <div className="ev-item-body">
                    {ev.label && <div className="ev-item-label">{ev.label}</div>}
                    <div className="ev-item-sub mono">{summarize(ev.kind, ev.payload)}</div>
                    {searches.length > 0 && (
                      <div className="ev-item-search" onClick={e => e.stopPropagation()}>
                        {searches.map(s => (
                          <button key={s.scope + s.label} className="ev-explore-btn"
                            onClick={() => onExplore(s.scope, g.service, s.facets)}
                            title={`${s.label} for ${g.service}`}>
                            <Icon name={s.icon} size={12} /> {s.label}
                          </button>
                        ))}
                      </div>
                    )}
                  </div>
                  <button className="ev-trash ev-item-trash" title="remove this evidence"
                    onClick={e => { e.stopPropagation(); onDeleteEvidence(ev.id) }}>
                    <Icon name="delete" size={14} />
                  </button>
                </div>
              )
            })}

            {g.out.length > 0 && (
              <div className="ev-strings">
                {g.out.map(e => (
                  <div key={e.id} className={'ev-string' + (e.crossedOut ? ' cut' : '')}>
                    <Icon name={e.crossedOut ? 'content_cut' : 'trending_flat'} size={14} />
                    <span className="mono">→ {e.to}</span>
                    <span className="ev-string-kind">{e.kind}{e.label ? `: ${e.label}` : ''}</span>
                    {e.crossedOut && <span className="ev-string-cut">cut</span>}
                  </div>
                ))}
              </div>
            )}
          </section>
        )
      })}
    </div>
  )
}

// --- grouping + summaries -------------------------------------------------

function groupByService(board: BoardData): NodeGroup[] {
  // one section per node (service), in board order; the red string it throws
  // hangs under it (edges are keyed by source service).
  const out = new Map<string, BoardEdge[]>()
  for (const e of board.edges) (out.get(e.from) ?? out.set(e.from, []).get(e.from)!).push(e)
  return board.nodes.map(n => ({ service: n.serviceId, label: n.label, evidence: n.evidence, out: out.get(n.serviceId) ?? [] }))
}

// per-item "find more like this" — only the searches that make sense for the
// item's kind, each implicitly scoped to its own service (the handler sets the
// service facet). The facet value comes from `aspect` (templateId / route: / metric
// / changeKind), which is stable regardless of payload shape (UI vs CLI --note).
// For the time-series kinds we also pass a ±window around the evidence timestamp,
// which the search API already honours via from/to. Returns [] when nothing fits.
type ItemSearch = { scope: string; icon: string; label: string; facets: Record<string, string> }
function itemSearches(ev: EvidenceItem): ItemSearch[] {
  const a = ev.aspect ?? ''
  const win = ev.at ? aroundWindow(ev.at) : {}
  switch (ev.kind) {
    case 'log':
      return a ? [{ scope: 'logs', icon: 'description', label: 'logs like this', facets: { template: a, ...win } }] : []
    case 'trace': {
      const route = a.replace(/^route:/, '')
      return route ? [{ scope: 'traces', icon: 'account_tree', label: 'this route', facets: { route, ...win } }] : []
    }
    case 'anomaly':
      // anomalies don't filter by metric, but the metrics scope does — jump there.
      return a ? [{ scope: 'metrics', icon: 'monitoring', label: 'this metric', facets: { metric: a } }] : []
    case 'change':
      return a ? [{ scope: 'changes', icon: 'deployed_code_update', label: 'changes like this', facets: { kind: a } }] : []
    default:
      // metric items already show the whole series; nothing more specific is useful.
      return []
  }
}

// a ±30-minute window around an evidence timestamp, as the datetime-local minute
// strings ("YYYY-MM-DDTHH:mm") the time facet uses — Workbench.run pads the seconds.
// logs and traces filter on their own ts column, so this re-centres the search on
// the moment the pinned evidence concerns (and visibly fills the from/to inputs).
// Parse + format as UTC (the stored timestamps are UTC-naive) so the window lands
// in the same clock the facet's min/max and the backend string-compare use.
function aroundWindow(at: string): Record<string, string> {
  const t = Date.parse(at.endsWith('Z') ? at : at + 'Z')
  if (Number.isNaN(t)) return {}
  const half = 30 * 60 * 1000
  const fmt = (ms: number) => new Date(ms).toISOString().slice(0, 16)
  return { from: fmt(t - half), to: fmt(t + half) }
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
