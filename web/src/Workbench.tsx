import { useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { api, type PinInput } from './api'

// The workbench: left = search (the fire axe, mirroring the CLI), right = the
// board (blank for now). Forage on the left, pin to the right.

type Row = { id: string; title: string; sub?: string; pin?: PinInput }

const EXAMPLES = [
  'anomalies',
  'timeline',
  'blast-radius payments-db',
  'logs payments-db --grep timeout',
  'traces --route checkout',
  'graph',
]

export default function Workbench() {
  const [params, setParams] = useSearchParams()
  const boardId = params.get('board')
  const [input, setInput] = useState('')
  const [rows, setRows] = useState<Row[]>([])
  const [verb, setVerb] = useState('')
  const [running, setRunning] = useState(false)
  const [err, setErr] = useState<string | null>(null)
  const [pinned, setPinned] = useState<Set<string>>(new Set())
  const [pinCount, setPinCount] = useState(0)

  async function runQuery(q: string) {
    if (!q.trim()) return
    setErr(null); setRunning(true)
    try {
      const res = await dispatch(q)
      setVerb(res.verb); setRows(res.rows)
    } catch (e) { setErr(String(e)); setRows([]); setVerb('') }
    finally { setRunning(false) }
  }

  async function ensureBoard(): Promise<string> {
    if (boardId) return boardId
    const c = await api.createBoard('investigation')
    const next = new URLSearchParams(params); next.set('board', c.id); setParams(next)
    return c.id
  }

  async function pin(row: Row) {
    if (!row.pin || pinned.has(row.id)) return
    const id = await ensureBoard()
    await api.pin(id, row.pin)
    setPinned(prev => new Set(prev).add(row.id))
    setPinCount(c => c + 1)
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

        <form onSubmit={e => { e.preventDefault(); runQuery(input) }}>
          <input className="search-input mono" placeholder="ask…  e.g.  anomalies --z 3"
            value={input} onChange={e => setInput(e.target.value)} autoFocus />
        </form>

        <div className="examples">
          {EXAMPLES.map(ex => (
            <button key={ex} className="ex mono" onClick={() => { setInput(ex); runQuery(ex) }}>{ex}</button>
          ))}
        </div>

        <div className="results">
          {running && <div className="hint">running…</div>}
          {err && <div className="error">{err}</div>}
          {!running && !err && rows.length === 0 && (
            <div className="hint">Forage for something, then pin it to the board.</div>
          )}
          {!running && !err && rows.length > 0 && (
            <div className="result-count">{rows.length} result{rows.length > 1 ? 's' : ''} · {verb}</div>
          )}
          {rows.map(r => (
            <div className="result" key={r.id}>
              <div className="r-main">
                <div className="r-title mono">{r.title}</div>
                {r.sub && <div className="r-sub">{r.sub}</div>}
              </div>
              {r.pin && (
                <button className={'pin' + (pinned.has(r.id) ? ' done' : '')}
                  onClick={() => pin(r)} disabled={pinned.has(r.id)}>
                  {pinned.has(r.id) ? 'pinned' : 'pin'}
                </button>
              )}
            </div>
          ))}
        </div>
      </div>

      <div className="board-pane">
        <div className="board-empty">
          <div className="board-empty-title">the board</div>
          <div>pinned findings will hang here — the wall of red string.<br />(render coming next)</div>
        </div>
      </div>
    </div>
  )
}

// --- dispatch: parse a CLI-style query and call the matching endpoint ------
function parse(q: string) {
  const toks = q.trim().split(/\s+/).filter(Boolean)
  if (toks[0] === 'weaver') toks.shift()
  const verb = toks.shift() ?? ''
  const pos: string[] = []
  const opts: Record<string, string> = {}
  for (let i = 0; i < toks.length; i++) {
    const t = toks[i]
    if (t.startsWith('--')) {
      const k = t.slice(2)
      if (i + 1 < toks.length && !toks[i + 1].startsWith('--')) opts[k] = toks[++i]
      else opts[k] = 'true'
    } else pos.push(t)
  }
  return { verb, pos, opts }
}

const num = (s?: string) => (s !== undefined ? Number(s) : undefined)
const clock = (iso?: string) => (iso ? new Date(iso).toISOString().slice(11, 19) : '-')
const sign = (n: number) => (n > 0 ? '+' : '')

async function dispatch(q: string): Promise<{ verb: string; rows: Row[] }> {
  const { verb, pos, opts } = parse(q)
  switch (verb) {
    case 'graph': {
      const g = await api.graph()
      return { verb, rows: g.services.map(s => ({
        id: 'svc:' + s.id, title: s.id, sub: `${s.kind} · ${s.subsystem ?? '-'}`,
        pin: { kind: 'service', ref: s.id, label: s.id },
      })) }
    }
    case 'anomalies': {
      const a = await api.anomalies(opts.split, num(opts.z), num(opts['min-pct']))
      return { verb, rows: a.map((x, i) => ({
        id: `an:${x.subjectId}:${x.metric}:${i}`,
        title: `${x.subjectId}  ${x.metric}  ${sign(x.deltaPct)}${x.deltaPct}%`,
        sub: `z ${x.z} · ${x.direction} · onset ${clock(x.onsetTs)}`,
        pin: { kind: 'anomaly', ref: x.subjectId, label: `${x.subjectId} ${x.metric} ${sign(x.deltaPct)}${x.deltaPct}%`, evidence: x },
      })) }
    }
    case 'timeline': {
      const t = await api.timeline(opts.split, num(opts.z), num(opts['min-pct']))
      return { verb, rows: t.map((x, i) => ({
        id: `tl:${x.subjectId}:${i}`, title: `${clock(x.onsetTs)}  ${x.subjectId}`,
        sub: `first via ${x.metric} · z ${x.z}`,
        pin: { kind: 'anomaly', ref: x.subjectId, label: `${x.subjectId} onset ${clock(x.onsetTs)}`, evidence: x },
      })) }
    }
    case 'blast-radius': {
      if (!pos[0]) throw new Error('usage: blast-radius <service>')
      const b = await api.blastRadius(pos[0])
      return { verb, rows: b.dependents.map(d => ({
        id: 'br:' + d.serviceId, title: d.serviceId, sub: `${d.hops} hop${d.hops > 1 ? 's' : ''} from ${b.node}`,
        pin: { kind: 'service', ref: d.serviceId, label: `${d.serviceId} (depends on ${b.node})` },
      })) }
    }
    case 'logs': {
      const l = await api.logs({ serviceId: pos[0], level: opts.level, grep: opts.grep, limit: num(opts.limit) ?? 50 })
      return { verb, rows: l.map(x => ({
        id: 'log:' + x.id, title: x.message, sub: `${x.level} · ${x.serviceId} · ${clock(x.ts)}`,
        pin: { kind: 'log', ref: x.serviceId, label: x.message, evidence: x },
      })) }
    }
    case 'traces': {
      const tr = await api.traces({ route: opts.route, status: opts.status, minDurationMs: num(opts['min-ms']), limit: num(opts.limit) ?? 20 })
      return { verb, rows: tr.map(x => ({
        id: 'tr:' + x.id, title: `${x.requestTypeId}  ${x.durationMs}ms  ${x.status}`, sub: x.id.slice(0, 8),
        pin: { kind: 'trace', ref: x.id, label: `${x.requestTypeId} ${x.durationMs}ms ${x.status}`, evidence: x },
      })) }
    }
    case 'service': {
      if (!pos[0]) throw new Error('usage: service <id>')
      const d = await api.service(pos[0])
      return { verb, rows: [{
        id: 'svc:' + d.service.id, title: d.service.id,
        sub: `${d.service.kind} · depends on ${d.dependsOn.length} · used by ${d.dependedOnBy.length}`,
        pin: { kind: 'service', ref: d.service.id, label: d.service.id },
      }] }
    }
    default:
      throw new Error(`unknown query "${verb}". try: ${EXAMPLES.join(' · ')}`)
  }
}
