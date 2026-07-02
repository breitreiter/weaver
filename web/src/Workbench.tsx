import { useCallback, useEffect, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { api, type Facets, type SearchResult, type SearchParams, type Board as BoardData } from './api'
import { Icon } from './Icon'
import Document from './Document'
import Evidence from './Evidence'

// live-sync cadence for the board+evidence panels — slow enough to be cheap,
// fast enough for the "watch the agent build the wall" moment.
const BOARD_POLL_MS = 2500

// data-type marker icon per result type (Material Symbols)
const TYPE_ICON: Record<string, string> = {
  service: 'deployed_code',
  anomaly: 'warning',
  log: 'description',
  trace: 'account_tree',
  metric: 'monitoring',
  change: 'deployed_code_update',
}

// The workbench: three equal panes (1:1:1) — search (forage), board (structure),
// evidence (narrative). Search is structured (scope + facets + free text) and
// returns rich, typed, pinnable result cards over /api/search.

const SCOPES = ['anomalies', 'traces', 'logs', 'services', 'metrics', 'changes']

// results are a capped, sorted page — never a total. When the cap is hit we say
// so, and name the sort, so "60" never reads as "there happen to be exactly 60".
// Labels mirror the backend ORDER BY per scope (see /api/search). metrics has no
// enforced order, so it gets the generic "first N" phrasing.
const RESULT_LIMIT = 60
const SORT_BY: Record<string, string> = {
  anomalies: 'magnitude', traces: 'duration', logs: 'recency',
  changes: 'time', services: 'name',
}

// which facet controls each scope shows
const CONTROLS: Record<string, string[]> = {
  anomalies: ['z', 'subsystem', 'kind', 'time'],
  traces: ['route', 'status', 'time'],
  logs: ['q', 'level', 'template', 'subsystem', 'time'],
  services: ['q', 'subsystem', 'kind', 'team'],
  metrics: ['metric', 'subsystem'],
  changes: ['subsystem', 'kind', 'time'],
}

export default function Workbench() {
  const [params, setParams] = useSearchParams()
  const boardId = params.get('board')
  const [facets, setFacets] = useState<Facets | null>(null)
  const [scope, setScope] = useState('anomalies')
  const [q, setQ] = useState('')
  const [f, setF] = useState<Record<string, string>>({})
  const [tz, setTz] = useState<Tz>('UTC')
  // imperative handle into the document editor — the evidence rail's "cite @-ref"
  // buttons deposit a reference at the cursor through this. Document registers its
  // insert fn once the editor exists.
  const docInsert = useRef<((text: string) => void) | null>(null)
  const registerInsert = useCallback((fn: (text: string) => void) => { docInsert.current = fn }, [])
  const [results, setResults] = useState<SearchResult[]>([])
  const [running, setRunning] = useState(false)
  const [err, setErr] = useState<string | null>(null)
  const [pinned, setPinned] = useState<Set<string>>(new Set())
  const [pinCount, setPinCount] = useState(0)
  const focus = params.get('focus')

  // the board is fetched + polled once here, shared by the board and evidence
  // panels so they read one always-in-sync copy.
  const [board, setBoard] = useState<BoardData | null>(null)
  const reloadBoard = useCallback(async () => {
    if (!boardId) { setBoard(null); return }
    try {
      // keep the prior reference when the poll returns identical data, so the
      // board memo (and React Flow's edge labels) don't churn every 2.5s.
      const next = await api.getBoard(boardId)
      setBoard(prev => prev && JSON.stringify(prev) === JSON.stringify(next) ? prev : next)
    }
    catch (e) {
      // a 404 means the board is gone (e.g. DB reseeded) — drop the stale
      // param so we stop polling a dead id. other errors keep the last copy.
      if (String(e).includes('404')) {
        setBoard(null)
        setParams(prev => { const next = new URLSearchParams(prev); next.delete('board'); return next })
      }
    }
  }, [boardId, setParams])
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- polls an external system; setState lands async after the fetch
    reloadBoard()
    const t = setInterval(reloadBoard, BOARD_POLL_MS)
    return () => clearInterval(t)
  }, [reloadBoard, pinCount])

  const setFocus = (svc: string) => {
    const next = new URLSearchParams(params); next.set('focus', svc); setParams(next)
  }

  useEffect(() => { api.facets().then(setFacets).catch(e => setErr(String(e))) }, [])

  async function run() {
    setErr(null); setRunning(true)
    try {
      const p: SearchParams = { scope, limit: RESULT_LIMIT }
      if (q.trim()) p.q = q.trim()
      if (f.service) p.service = f.service
      for (const k of ['subsystem', 'kind', 'team', 'level', 'template', 'route', 'status', 'metric', 'trace'])
        if (f[k]) (p as Record<string, unknown>)[k] = f[k]
      // datetime-local emits "YYYY-MM-DDTHH:mm" — pad seconds so the range is
      // inclusive of the whole boundary minute (backend compares ISO strings).
      if (f.from) p.from = f.from + ':00'
      if (f.to) p.to = f.to + ':59'
      p.z = f.z ? Number(f.z) : scope === 'anomalies' ? 3 : undefined
      setResults(await api.search(p))
    } catch (e) { setErr(String(e)); setResults([]) }
    finally { setRunning(false) }
  }

  // re-run when scope or a facet changes (free text runs on submit)
  // eslint-disable-next-line react-hooks/exhaustive-deps, react-hooks/set-state-in-effect
  useEffect(() => { run() }, [scope, JSON.stringify(f)])

  async function ensureBoard(): Promise<string> {
    if (boardId) return boardId
    const c = await api.createBoard('investigation')
    const next = new URLSearchParams(params); next.set('board', c.id); setParams(next)
    return c.id
  }

  // re-pinning is always allowed: a card you pinned, removed from the evidence
  // panel, and want back must work. The API dedups identical evidence, so a true
  // double-click is a harmless no-op. pinned/pinCount are session hints only (the
  // "done" check + the count), deliberately not reconciled against the board.
  async function pin(r: SearchResult) {
    const id = await ensureBoard()
    // nodeIds is normally a single service (a trace pins just its hot hop). The other
    // services a trace touched are reached deliberately via the span search buttons in
    // the trace card, not auto-dragged onto the board. See board-build.md.
    await api.pin(id, { serviceIds: r.pin.nodeIds, evidence: r.pin.evidence, label: r.title })
    setPinned(prev => new Set(prev).add(r.id))
    setPinCount(c => c + 1)
  }

  const setField = (k: string, v: string) => setF(prev => ({ ...prev, [k]: v }))

  // range-picker accelerators: preset windows anchored to the dataset's end
  // ('' = full / unbounded). Writes the canonical UTC from/to (run() sends them
  // as-is; the picker re-displays them in the selected zone).
  const setRange = (minutes: number | '') => {
    if (minutes === '') { setF(prev => ({ ...prev, from: '', to: '' })); return }
    if (!facets) return
    const end = Date.parse(facets.window.end.endsWith('Z') ? facets.window.end : facets.window.end + 'Z')
    if (Number.isNaN(end)) return
    const from = new Date(end - minutes * 60000).toISOString().slice(0, 16)
    const to = new Date(end).toISOString().slice(0, 16)
    setF(prev => ({ ...prev, from, to }))
  }

  // explore button on a board/evidence card → reset the left panel to a fresh
  // single-service query in the chosen scope (clears free text + every other
  // facet), so the operator can span out from a pinned node without touching the
  // graph. service rides as a facet, so it persists if they then switch scopes.
  const exploreService = (nextScope: string, svc: string, extra?: Record<string, string>) => {
    setQ(''); setF({ service: svc, ...extra }); setScope(nextScope)
  }

  // evidence-rail trash icons: drop a single piece of evidence, or a whole service
  // (its node + all its evidence). Reload so both panels reflect the removal on the
  // next frame rather than the poll.
  async function removeEvidence(evidenceId: string) {
    if (!boardId) return
    await api.deleteEvidence(boardId, evidenceId)
    reloadBoard()
  }
  async function removeService(svc: string) {
    if (!boardId) return
    await api.deleteNode(boardId, svc)
    reloadBoard()
  }
  const controls = CONTROLS[scope] ?? []
  const opts = (k: string): string[] => {
    if (!facets) return []
    return ({
      subsystem: facets.subsystems, kind: facets.kinds, team: facets.teams,
      level: facets.logLevels, template: facets.logTemplates, route: facets.routes,
      status: facets.traceStatuses, metric: facets.metrics,
    } as Record<string, string[]>)[k] ?? []
  }

  return (
    <div className="workbench">
      <div className="search-pane">
        <div className="search-head">
          <span className="brand">weaver</span>
          <span className="board-status">
            {boardId ? `board ${boardId}` : 'no board yet — pin something to start one'}
          </span>
        </div>

        <div className="scopes">
          {SCOPES.map(s => (
            <button key={s} className={'scope' + (s === scope ? ' active' : '')} onClick={() => setScope(s)}>{s}</button>
          ))}
        </div>

        <form className="query-bar" onSubmit={e => { e.preventDefault(); run() }}>
          <input className="search-input mono" value={q} onChange={e => setQ(e.target.value)}
            placeholder={scope === 'logs' ? 'full-text search logs…' : scope === 'services' ? 'filter by name/id…' : 'free text (used by logs / services)'} />
          <button className="run" type="submit">search</button>
        </form>

        <div className="facets">
          {controls.includes('z') && (
            <label>sensitivity
              <select value={f.z ?? '3'} onChange={e => setField('z', e.target.value)}>
                <option value="3">normal (z≥3)</option>
                <option value="2">loose (z≥2)</option>
                <option value="1">sensitive (z≥1)</option>
              </select>
            </label>
          )}
          {['subsystem', 'kind', 'team', 'level', 'template', 'route', 'status', 'metric'].filter(k => controls.includes(k)).map(k => (
            <label key={k}>{k}
              <select value={f[k] ?? ''} onChange={e => setField(k, e.target.value)}>
                <option value="">any</option>
                {opts(k).map(o => <option key={o} value={o}>{o}</option>)}
              </select>
            </label>
          ))}
          {controls.includes('time') && facets && (
            <>
              <label>zone
                <select value={tz} onChange={e => setTz(e.target.value as Tz)}>
                  <option value="UTC">UTC</option>
                  <option value="Local">Local</option>
                </select>
              </label>
              <label>from
                <input type="datetime-local" className="time-input" value={toDisplay(f.from ?? '', tz)}
                  min={toDisplay(facets.window.start.slice(0, 16), tz)} max={toDisplay(facets.window.end.slice(0, 16), tz)}
                  onChange={e => setField('from', toUtc(e.target.value, tz))} />
              </label>
              <label>to
                <input type="datetime-local" className="time-input" value={toDisplay(f.to ?? '', tz)}
                  min={toDisplay(facets.window.start.slice(0, 16), tz)} max={toDisplay(facets.window.end.slice(0, 16), tz)}
                  onChange={e => setField('to', toUtc(e.target.value, tz))} />
              </label>
              <div className="time-presets">
                <button type="button" onClick={() => setRange('')} title="full window (unbounded)">full</button>
                <button type="button" onClick={() => setRange(15)} title="last 15 minutes of the window">15m</button>
                <button type="button" onClick={() => setRange(60)} title="last hour of the window">1h</button>
                <button type="button" onClick={() => setRange(360)} title="last 6 hours of the window">6h</button>
              </div>
            </>
          )}
        </div>

        {f.service && (
          <div className="active-filter">
            <Icon name="filter_alt" size={14} />
            <span>scoped to <span className="mono">{f.service}</span></span>
            <button className="active-filter-clear" onClick={() => setField('service', '')}
              title="clear service scope"><Icon name="close" size={14} /></button>
          </div>
        )}

        <div className="results">
          {running && <div className="hint">searching…</div>}
          {err && <div className="error">{err}</div>}
          {!running && !err && results.length === 0 && <div className="hint">{emptyMessage(scope, q, f)}</div>}
          {!running && !err && results.length > 0 && (
            results.length >= RESULT_LIMIT
              ? <div className="result-count capped">
                  top {RESULT_LIMIT}{SORT_BY[scope] ? ` by ${SORT_BY[scope]}` : ''} — more exist
                </div>
              : <div className="result-count">{results.length} result{results.length > 1 ? 's' : ''}</div>
          )}
          {results.map(r => <ResultCard key={r.id} r={r} tz={tz} pinned={pinned.has(r.id)} onPin={() => pin(r)} onExplore={exploreService} />)}
        </div>
      </div>

      <div className="board-pane">
        <Document board={board} boardId={boardId} ensureBoard={ensureBoard} onFocus={setFocus} onRegisterInsert={registerInsert} />
      </div>

      <Evidence board={board} focus={focus} onFocus={setFocus} onExplore={exploreService}
        onDeleteEvidence={removeEvidence} onDeleteService={removeService}
        onInsertRef={ref => docInsert.current?.('@' + ref + ' ')} />
    </div>
  )
}

// Timezone handling. from/to are stored as the canonical UTC wall-clock the
// backend string-compares against (run() sends them unchanged), so only display
// and entry shift with `tz`: UTC is identity; Local round-trips through the native
// Date, which applies the browser's zone + DST correctly. All strings are the
// datetime-local shape "YYYY-MM-DDTHH:mm".
type Tz = 'UTC' | 'Local'

// canonical UTC wall-clock → what the picker should show in `tz`
function toDisplay(utc: string, tz: Tz): string {
  if (!utc || tz === 'UTC') return utc
  const t = Date.parse(utc + 'Z')
  if (Number.isNaN(t)) return utc
  // 'sv-SE' renders "YYYY-MM-DD HH:mm:ss" in the runtime's local zone
  return new Date(t).toLocaleString('sv-SE').slice(0, 16).replace(' ', 'T')
}

// what the user typed in `tz` → canonical UTC wall-clock
function toUtc(display: string, tz: Tz): string {
  if (!display || tz === 'UTC') return display
  const t = Date.parse(display) // no 'Z' → parsed as local time
  if (Number.isNaN(t)) return display
  return new Date(t).toISOString().slice(0, 16)
}

// trace timestamps are stored UTC (…Z); render them in the selected zone so cards
// and the picker stay in one frame. Returns "YYYY-MM-DD HH:mm:ss".
function fmtTime(iso: string | undefined, tz: Tz): string {
  if (!iso) return ''
  const t = Date.parse(iso.endsWith('Z') ? iso : iso + 'Z')
  if (Number.isNaN(t)) return ''
  return tz === 'Local'
    ? new Date(t).toLocaleString('sv-SE')
    : new Date(t).toISOString().slice(0, 19).replace('T', ' ')
}

// Empty-state copy that names what's actually filtering the view, so "no results"
// reads as "you're looking at a filtered slice", not "nothing exists". Doubles as
// the facet-brick cue: a dead facet combo explains itself rather than just bricking.
function emptyMessage(scope: string, q: string, f: Record<string, string>): string {
  const parts: string[] = []
  if (q.trim()) parts.push(`“${q.trim()}”`)
  for (const [k, v] of Object.entries(f)) {
    if (!v || k === 'from' || k === 'to') continue
    parts.push(`${k} ${v}`)
  }
  if (f.from || f.to) parts.push(`window ${f.from || '…'} – ${f.to || '…'} UTC`)
  const where = parts.length ? ` for ${parts.join(', ')}` : ''
  return `No ${scope}${where}. Loosen a facet or widen the window.`
}

function ResultCard({ r, tz, pinned, onPin, onExplore }: { r: SearchResult; tz: Tz; pinned: boolean; onPin: () => void; onExplore: (scope: string, svc: string, extra?: Record<string, string>) => void }) {
  const dir = r.type === 'anomaly' ? r.payload?.direction : undefined
  const startedAt = r.type === 'trace' ? fmtTime(r.payload?.trace?.startedAt, tz) : ''
  return (
    <div className={'card' + (dir ? ` dir-${dir}` : '')}>
      <div className="card-head">
        <span className={`badge badge-${r.type}`} title={r.type}>
          <Icon name={TYPE_ICON[r.type] ?? 'help'} size={15} /> {r.type}
        </span>
        <span className="card-title mono">{r.title}</span>
        <button className="card-id mono" title={`typed id — pin from the CLI with: weaver pin ${r.id}  (click to copy)`}
          onClick={() => navigator.clipboard?.writeText(r.id)}>{r.id}</button>
        <button className={'pin' + (pinned ? ' done' : '')} onClick={onPin}
          title={pinned ? 'pinned to the board — click to pin again' : 'pin to the board'}>
          <Icon name="push_pin" size={18} />
        </button>
      </div>
      <div className="card-sub">
        {startedAt && <span className="card-time mono" title={`trace start (${tz})`}>{startedAt}</span>}
        {r.subtitle}
      </div>
      {r.type === 'trace' && Array.isArray(r.payload?.spans) && <TraceMini spans={r.payload.spans} onExplore={onExplore} />}
      {r.type === 'log' && r.payload?.traceId && (
        // the correlation pivot: this log fired under a sampled trace — jump to it.
        <div className="log-trace">
          <button className="hop-svc mono" title={`open the trace this log fired under (${r.payload.traceId})`}
            onClick={() => onExplore('traces', '', { trace: r.payload.traceId })}>
            <Icon name="account_tree" size={13} /> trace {r.payload.traceId.slice(0, 8)}
          </button>
        </div>
      )}
    </div>
  )
}

// the hottest hops of a trace, each service name a button that spiders the search
// to that participant (services scope, filtered) — the deliberate way to bring a
// trace's other services onto the board now that pinning no longer drags them in.
function TraceMini({ spans, onExplore }: {
  spans: { id: string; serviceId: string; selfMs: number; status: string }[]
  onExplore: (scope: string, svc: string) => void
}) {
  const top = [...spans].sort((a, b) => b.selfMs - a.selfMs).slice(0, 4)
  const max = Math.max(1, ...top.map(s => s.selfMs))
  return (
    <div className="trace-mini">
      {top.map(s => (
        <div className="hop" key={s.id}>
          <button className="hop-svc mono" title={`search services for ${s.serviceId}`}
            onClick={() => onExplore('services', s.serviceId)}>{s.serviceId}</button>
          <span className="hop-bar" style={{ width: `${(s.selfMs / max) * 100}%` }} />
          <span className="hop-ms">{s.selfMs}ms</span>
        </div>
      ))}
    </div>
  )
}
