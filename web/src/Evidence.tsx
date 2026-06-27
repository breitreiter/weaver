import { useEffect, useMemo, useRef, useState } from 'react'
import { api, type Board as BoardData, type EvidenceItem, type MetricPoint, type MetricSeries } from './api'
import { Icon } from './Icon'

// causal/temporal/custom edges are the operator's red string; dependency & route
// edges are tool-supplied facts (neutral). Mirrors Board's predicate.
const isRedString = (kind: string) => kind !== 'dependency' && kind !== 'route'

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

type NodeGroup = { service: string; label?: string; evidence: EvidenceItem[] }

export default function Evidence({ board, focus, onFocus, onExplore, onDeleteEvidence, onDeleteService, onDeleteEdge, onAddRelationship }: {
  board: BoardData | null
  focus: string | null
  onFocus: (svc: string) => void
  onExplore: (scope: string, service: string, facets?: Record<string, string>) => void
  onDeleteEvidence: (evidenceId: string) => void
  onDeleteService: (service: string) => void
  onDeleteEdge: (edgeId: string) => void
  onAddRelationship: () => void
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
                    <div className="ev-item-sub mono">{ev.summary}</div>
                    {ev.kind === 'metric' && real && <MetricSparkline service={g.service} metric={ev.aspect} />}
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

          </section>
        )
      })}

      {board && groups.length > 0 && (
        <section className="ev-rels">
          <div className="ev-rels-head">
            <span className="ev-rels-title">relationships</span>
            <button className="ev-rels-add" onClick={onAddRelationship} title="draw a relationship between two pinned services">
              <Icon name="add" size={14} /> relationship
            </button>
          </div>
          {board.edges.length === 0 && <div className="hint">no red string yet — relate two pins to draw one.</div>}
          {board.edges.map(e => (
            <div key={e.id} className={'ev-string' + (isRedString(e.kind) ? ' red' : '')}>
              <Icon name="trending_flat" size={16} className="ev-string-ico" />
              <div className="ev-string-body">
                <div className="ev-string-dir mono">{e.from} → {e.to}</div>
                <div className="ev-string-kind">
                  {e.kind}{e.label ? `: ${e.label}` : ''}
                  <span className="ev-string-by"> · {e.drawnBy}</span>
                </div>
              </div>
              <button className="ev-trash" title="remove this line" onClick={() => onDeleteEdge(e.id)}>
                <Icon name="delete" size={14} />
              </button>
            </div>
          ))}
        </section>
      )}
    </div>
  )
}

// --- grouping + summaries -------------------------------------------------

function groupByService(board: BoardData): NodeGroup[] {
  // one section per node (service), in board order. Edges no longer hang under a
  // service — they live in their own top-level relationships section (peer to pins).
  return board.nodes.map(n => ({ service: n.serviceId, label: n.label, evidence: n.evidence }))
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
      const p = ev.payload as { trace?: { id?: string }; serviceCount?: number } | undefined
      const traceId = p?.trace?.id
      const out: ItemSearch[] = []
      if (route) out.push({ scope: 'traces', icon: 'account_tree', label: 'this route', facets: { route, ...win } })
      // the services this trace actually crossed — reverses the traces scope's
      // "traces touching service X" filter. service:'' clears the single-service
      // facet exploreService forces, so every participant comes back, not just the
      // hot hop this evidence hangs under. serviceCount rides on the pin so the
      // button can name the count without re-fetching the spans.
      if (traceId) {
        const n = p?.serviceCount
        out.push({ scope: 'services', icon: 'lan', label: n ? `${n} services in this trace` : 'services in this trace', facets: { service: '', trace: traceId } })
      }
      return out
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

// the per-kind one-liner now comes from the server (EvidenceItem.summary) — one
// renderer shared with the CLI's `board show`, so the two surfaces never drift.

// React Flow node ids are service ids (safe chars), but guard the selector anyway.
const cssId = (s: string) => s.replace(/[^a-zA-Z0-9_-]/g, '_')

// --- metric sparkline -----------------------------------------------------

// The pinned metric payload carries only the prose trajectory (shapeCode/prose),
// not the points — so the card pulls the raw series live from /api/metrics. One
// service can have several metric cards, so dedupe the fetch per service (the
// endpoint returns every metric for the subject in one shot anyway).
const seriesCache = new Map<string, Promise<MetricSeries[]>>()
function loadSeries(service: string): Promise<MetricSeries[]> {
  let p = seriesCache.get(service)
  if (!p) { p = api.metrics(service); seriesCache.set(service, p) }
  return p
}

// hand-rolled SVG polyline (matches TraceMini's no-charting-lib idiom). The svg
// stretches to fill the card with preserveAspectRatio="none"; non-scaling-stroke
// keeps the line crisp despite the horizontal stretch. Colour inherits the card's
// --k (the metric green) via the custom-property cascade.
function MetricSparkline({ service, metric }: { service: string; metric: string }) {
  const [points, setPoints] = useState<MetricPoint[] | null>(null)
  useEffect(() => {
    let alive = true
    loadSeries(service)
      .then(all => { if (alive) setPoints(all.find(s => s.metric === metric)?.points ?? []) })
      .catch(() => { if (alive) setPoints([]) })
    return () => { alive = false }
  }, [service, metric])

  if (!points || points.length < 2) return null
  const vals = points.map(p => p.value)
  const min = Math.min(...vals), max = Math.max(...vals)
  const span = max - min || 1
  const W = 240, H = 32, pad = 3
  const path = points
    .map((p, i) => {
      const x = (i / (points.length - 1)) * W
      const y = pad + (1 - (p.value - min) / span) * (H - 2 * pad)
      return `${x.toFixed(1)},${y.toFixed(1)}`
    })
    .join(' ')
  return (
    <svg className="ev-spark" viewBox={`0 0 ${W} ${H}`} preserveAspectRatio="none"
      role="img" aria-label={`${metric} trajectory`}>
      <polyline points={path} fill="none" stroke="var(--k)" strokeWidth={1.5}
        strokeLinejoin="round" strokeLinecap="round" vectorEffect="non-scaling-stroke" />
    </svg>
  )
}
