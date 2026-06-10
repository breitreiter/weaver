import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { api, type Facets, type SearchResult, type SearchParams } from './api'

// The workbench: search is the main event (≥55% of the screen); the board is a
// secondary evidence anchor. Search is structured (scope + facets + free text)
// and returns rich, typed, pinnable result cards over /api/search.

const SCOPES = ['anomalies', 'traces', 'logs', 'services', 'metrics', 'changes']

// which facet controls each scope shows
const CONTROLS: Record<string, string[]> = {
  anomalies: ['z', 'subsystem', 'kind'],
  traces: ['route', 'status'],
  logs: ['q', 'level', 'template', 'subsystem'],
  services: ['q', 'subsystem', 'kind', 'team'],
  metrics: ['metric', 'subsystem'],
  changes: ['subsystem', 'kind'],
}

export default function Workbench() {
  const [params, setParams] = useSearchParams()
  const boardId = params.get('board')
  const [facets, setFacets] = useState<Facets | null>(null)
  const [scope, setScope] = useState('anomalies')
  const [q, setQ] = useState('')
  const [f, setF] = useState<Record<string, string>>({})
  const [results, setResults] = useState<SearchResult[]>([])
  const [running, setRunning] = useState(false)
  const [err, setErr] = useState<string | null>(null)
  const [pinned, setPinned] = useState<Set<string>>(new Set())
  const [pinCount, setPinCount] = useState(0)

  useEffect(() => { api.facets().then(setFacets).catch(e => setErr(String(e))) }, [])

  async function run() {
    setErr(null); setRunning(true)
    try {
      const p: SearchParams = { scope, limit: 60 }
      if (q.trim()) p.q = q.trim()
      for (const k of ['subsystem', 'kind', 'team', 'level', 'template', 'route', 'status', 'metric'])
        if (f[k]) (p as Record<string, unknown>)[k] = f[k]
      p.z = f.z ? Number(f.z) : scope === 'anomalies' ? 3 : undefined
      setResults(await api.search(p))
    } catch (e) { setErr(String(e)); setResults([]) }
    finally { setRunning(false) }
  }

  // re-run when scope or a facet changes (free text runs on submit)
  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => { run() }, [scope, JSON.stringify(f)])

  async function ensureBoard(): Promise<string> {
    if (boardId) return boardId
    const c = await api.createBoard('investigation')
    const next = new URLSearchParams(params); next.set('board', c.id); setParams(next)
    return c.id
  }

  async function pin(r: SearchResult) {
    if (pinned.has(r.id)) return
    const id = await ensureBoard()
    const ev = r.pin.evidence
    await api.pin(id, { kind: ev?.kind ?? 'node', ref: r.pin.nodeIds[0] ?? '(fleet)', label: r.title, evidence: ev?.payload })
    setPinned(prev => new Set(prev).add(r.id))
    setPinCount(c => c + 1)
  }

  const setField = (k: string, v: string) => setF(prev => ({ ...prev, [k]: v }))
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
            {boardId ? `board ${boardId} · ${pinCount} pinned` : 'no board yet — pin something to start one'}
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
        </div>

        <div className="results">
          {running && <div className="hint">searching…</div>}
          {err && <div className="error">{err}</div>}
          {!running && !err && results.length === 0 && <div className="hint">No results. Try a different scope or loosen the facets.</div>}
          {!running && !err && results.length > 0 && <div className="result-count">{results.length} result{results.length > 1 ? 's' : ''}</div>}
          {results.map(r => <ResultCard key={r.id} r={r} pinned={pinned.has(r.id)} onPin={() => pin(r)} />)}
        </div>
      </div>

      <div className="board-pane">
        <div className="board-empty">
          <div className="board-empty-title">the board</div>
          <div>pinned findings hang here — the wall of red string.<br />(render coming next)</div>
        </div>
      </div>
    </div>
  )
}

function ResultCard({ r, pinned, onPin }: { r: SearchResult; pinned: boolean; onPin: () => void }) {
  const dir = r.type === 'anomaly' ? r.payload?.direction : undefined
  return (
    <div className={'card' + (dir ? ` dir-${dir}` : '')}>
      <div className="card-head">
        <span className={`badge badge-${r.type}`}>{r.type}</span>
        <span className="card-title mono">{r.title}</span>
        <button className={'pin' + (pinned ? ' done' : '')} onClick={onPin} disabled={pinned}>
          {pinned ? 'pinned' : 'pin'}
        </button>
      </div>
      <div className="card-sub">{r.subtitle}</div>
      {r.type === 'trace' && Array.isArray(r.payload?.spans) && <TraceMini spans={r.payload.spans} />}
    </div>
  )
}

function TraceMini({ spans }: { spans: { id: string; serviceId: string; selfMs: number; status: string }[] }) {
  const top = [...spans].sort((a, b) => b.selfMs - a.selfMs).slice(0, 4)
  const max = Math.max(1, ...top.map(s => s.selfMs))
  return (
    <div className="trace-mini">
      {top.map(s => (
        <div className="hop" key={s.id}>
          <span className="hop-svc mono">{s.serviceId}</span>
          <span className="hop-bar" style={{ width: `${(s.selfMs / max) * 100}%` }} />
          <span className="hop-ms">{s.selfMs}ms</span>
        </div>
      ))}
    </div>
  )
}
