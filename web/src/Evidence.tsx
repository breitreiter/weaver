import { useEffect, useMemo, useRef, useState } from 'react'
import {
  ResponsiveContainer, LineChart, Line, BarChart, Bar, AreaChart, Area,
  XAxis, YAxis, CartesianGrid, Tooltip, Legend,
} from 'recharts'
import { api, type Board as BoardData, type EvidenceItem, type MetricPoint, type MetricSeries } from './api'
import { Icon } from './Icon'

// The evidence panel (right rail) — the shoebox: a persistent, scrollable list of
// every pinned finding. Each service is a SECTION HEADER (not a card); the evidence
// layered onto it hangs below as cards, each bordered in its kind's colour with the
// kind icon promoted to the lead. Clicking a service scrolls this panel to it
// (?focus=<svc>) — the same focus an @-reference in the document triggers. Findings
// are referenced from the document by their typed id (EvidenceItem.refId).

const KIND_ICON: Record<string, string> = {
  anomaly: 'warning', log: 'description',
  trace: 'account_tree', metric: 'monitoring', change: 'deployed_code_update',
  chart: 'bar_chart',
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

export default function Evidence({ board, focus, onFocus, onExplore, onDeleteEvidence, onDeleteService, onInsertRef }: {
  board: BoardData | null
  focus: string | null
  onFocus: (svc: string) => void
  onExplore: (scope: string, service: string, facets?: Record<string, string>) => void
  onDeleteEvidence: (evidenceId: string) => void
  onDeleteService: (service: string) => void
  // deposit an @-reference (a service id, or a finding's typed id) into the document
  // at the cursor. The forage→write bridge: pin a fact, then cite it in the prose.
  onInsertRef: (ref: string) => void
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
                <button className="ev-cite" title={`cite @${g.service} in the document`}
                  onClick={e => { e.stopPropagation(); onInsertRef(g.service) }}>
                  <Icon name="alternate_email" size={15} />
                </button>
              )}
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
                    {ev.kind === 'chart' && <ChartEvidence payload={ev.payload} />}
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
                  <div className="ev-item-actions" onClick={e => e.stopPropagation()}>
                    <button className="ev-cite" title={`cite @${ev.refId ?? ev.id} in the document`}
                      onClick={() => onInsertRef(ev.refId ?? ev.id)}>
                      <Icon name="alternate_email" size={14} />
                    </button>
                    <button className="ev-trash" title="remove this evidence"
                      onClick={() => onDeleteEvidence(ev.id)}>
                      <Icon name="delete" size={14} />
                    </button>
                  </div>
                </div>
              )
            })}

          </section>
        )
      })}
    </div>
  )
}

// --- grouping + summaries -------------------------------------------------

function groupByService(board: BoardData): NodeGroup[] {
  // one section per pinned node (service), in board order.
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

// service ids are usually safe selector chars, but guard the selector anyway.
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

// A pinned agent-authored SQL chart (evidence kind `chart`). Renders the snapshot
// captured at pin time — payload { type, xColumn, yColumns, columns, rows } — in
// Recharts, the one styling surface all pinned charts share (agent-sql-charts.md
// decision 3). Nothing is computed here: the rows are the query's, shaped into the
// {col: value} records Recharts wants. Series colours come from the dataviz palette
// (validated for CVD on the dark card surface); identity for ≥2 series rides the
// legend, never colour alone.
type ChartPayload = {
  title?: string; type?: string; xColumn?: string | null
  yColumns?: string[] | null; columns?: string[]; rows?: unknown[][]
}
const CHART_SERIES = ['#3987e5', '#199e70', '#c98500', '#008300', '#9085e9', '#e66767']

function ChartEvidence({ payload }: { payload: unknown }) {
  const p = (payload ?? {}) as ChartPayload
  const cols = p.columns ?? []
  const rows = p.rows ?? []
  if (cols.length === 0 || rows.length === 0) return null

  const type = p.type ?? 'line'
  const xCol = p.xColumn && cols.includes(p.xColumn) ? p.xColumn : cols[0]
  // a column is numeric iff every non-null cell is a number — only those can be a
  // y-series. y defaults to every non-x numeric column when the agent didn't name any.
  const isNum = (c: string) => {
    const i = cols.indexOf(c)
    return rows.some(r => typeof r[i] === 'number') && rows.every(r => r[i] == null || typeof r[i] === 'number')
  }
  const named = (p.yColumns ?? []).filter(c => cols.includes(c) && c !== xCol)
  const yCols = (named.length ? named : cols.filter(c => c !== xCol)).filter(isNum)
  if (yCols.length === 0) return null

  const data = rows.map(r => { const o: Record<string, unknown> = {}; cols.forEach((c, i) => { o[c] = r[i] }); return o })
  const axis = 'var(--text-dim)'
  const color = (i: number) => CHART_SERIES[i % CHART_SERIES.length]
  // recessive grid + axes; the marks carry the ink (dataviz marks-and-anatomy).
  const grid = <CartesianGrid stroke="var(--border)" strokeDasharray="2 2" vertical={false} />
  const xa = <XAxis dataKey={xCol} tick={{ fill: axis, fontSize: 10 }} stroke={axis} tickLine={false} />
  const ya = <YAxis tick={{ fill: axis, fontSize: 10 }} stroke={axis} tickLine={false} width={40} />
  const tip = <Tooltip cursor={{ stroke: axis, strokeDasharray: '2 2' }}
    contentStyle={{ background: 'var(--panel)', border: '1px solid var(--border)', borderRadius: 6, fontSize: 11 }}
    labelStyle={{ color: 'var(--text)' }} itemStyle={{ color: 'var(--text)' }} />
  // ≥2 series → a legend names them (secondary encoding for the CVD floor band).
  const legend = yCols.length >= 2 ? <Legend wrapperStyle={{ fontSize: 11, color: axis }} /> : null

  let chart
  if (type === 'bar')
    chart = <BarChart data={data} barGap={2}>{grid}{xa}{ya}{tip}{legend}
      {yCols.map((c, i) => <Bar key={c} dataKey={c} fill={color(i)} radius={[4, 4, 0, 0]} isAnimationActive={false} />)}</BarChart>
  else if (type === 'area')
    chart = <AreaChart data={data}>{grid}{xa}{ya}{tip}{legend}
      {yCols.map((c, i) => <Area key={c} dataKey={c} stroke={color(i)} strokeWidth={2} fill={color(i)} fillOpacity={0.2} isAnimationActive={false} />)}</AreaChart>
  else // 'line' (the default) — also the fallback for any legacy/unknown type (e.g. a retired scatter snapshot)
    chart = <LineChart data={data}>{grid}{xa}{ya}{tip}{legend}
      {yCols.map((c, i) => <Line key={c} dataKey={c} stroke={color(i)} strokeWidth={2} dot={false} isAnimationActive={false} />)}</LineChart>

  return (
    <div className="ev-chart" role="img" aria-label={`${p.title ?? 'chart'} — ${type} of ${yCols.join(', ')}`}>
      <ResponsiveContainer width="100%" height={160}>{chart}</ResponsiveContainer>
    </div>
  )
}

// A metric's trajectory as a Recharts mini line — a true sparkline (no axes, grid, or
// tooltip). Re-skinned from the old hand-rolled SVG polyline onto Recharts so pinned
// metrics and agent charts share one styling surface (agent-sql-charts.md decision 3).
// Colour inherits the card's --k (the metric teal) via the custom-property cascade.
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
  return (
    <div className="ev-spark" role="img" aria-label={`${metric} trajectory`}>
      <ResponsiveContainer width="100%" height={28}>
        <LineChart data={points} margin={{ top: 2, right: 0, bottom: 2, left: 0 }}>
          {/* hidden axis so the tooltip labels each point by its time (UTC, matching
              the app) rather than by row index — the sparkline itself stays bare. */}
          <XAxis dataKey="ts" hide />
          <Tooltip cursor={{ stroke: 'var(--text-dim)', strokeDasharray: '2 2' }}
            contentStyle={{ background: 'var(--panel)', border: '1px solid var(--border)', borderRadius: 6, fontSize: 11, padding: '2px 6px' }}
            labelStyle={{ color: 'var(--text-dim)' }} itemStyle={{ color: 'var(--text)' }}
            labelFormatter={(ts: string) => ts.slice(11, 16)}
            formatter={(v: number) => [Number.isInteger(v) ? v : v.toFixed(1), metric]} />
          <Line dataKey="value" stroke="var(--k)" strokeWidth={1.5} dot={false}
            activeDot={{ r: 3, fill: 'var(--k)', stroke: 'none' }} isAnimationActive={false} />
        </LineChart>
      </ResponsiveContainer>
    </div>
  )
}
