import { useEffect, useState } from 'react'
import { api, type Relationship } from './api'

// The relationship dialog — the human names x and y the same way the agent does
// (`weaver link x y --as z`). It leads with the REAL relationships the data holds
// between the two (pick one to ground a hypothesis in a fact), and falls back to a
// freeform assertion when none fit. x/y are chosen from board members; the dying
// drag path preselects them via initialSource/initialTarget.

// freeform kinds — the operator's own hypothesis, for when the data doesn't hold
// the relationship they suspect.
const FREEFORM_KINDS = [
  { kind: 'causal', label: 'causes / explains' },
  { kind: 'temporal', label: 'precedes (temporal)' },
  { kind: 'custom', label: 'custom' },
]

type Pick = { rel: Relationship } | { free: string }

export function RelationshipModal({ members, initialSource, initialTarget, onDraw, onCancel }: {
  members: string[]
  initialSource?: string
  initialTarget?: string
  onDraw: (from: string, to: string, kind: string, label: string) => void
  onCancel: () => void
}) {
  const [x, setX] = useState(initialSource ?? '')
  const [y, setY] = useState(initialTarget ?? '')
  const [rels, setRels] = useState<Relationship[] | null>(null)
  const [pick, setPick] = useState<Pick | null>(null)
  const [label, setLabel] = useState('')

  const pairReady = x !== '' && y !== '' && x !== y

  // (re)load the real relationships when the pair is valid. The choice/label reset
  // lives in the pickers' onChange (not here) to avoid a synchronous setState in
  // the effect; stale rels are hidden by the pairReady render guard regardless.
  useEffect(() => {
    if (x === '' || y === '' || x === y) return
    let live = true
    api.relationships(x, y)
      .then(r => { if (live) setRels(r.relationships) })
      .catch(() => { if (live) setRels([]) })
    return () => { live = false }
  }, [x, y])

  const changeX = (v: string) => { setX(v); setRels(null); setPick(null); setLabel('') }
  const changeY = (v: string) => { setY(v); setRels(null); setPick(null); setLabel('') }
  const chooseRel = (rel: Relationship) => { setPick({ rel }); setLabel(rel.suggestedLabel) }
  const chooseFree = (kind: string) => { setPick({ free: kind }); setLabel('') }

  // a freeform 'custom' edge needs a label to mean anything; everything else can draw bare.
  const ready = pairReady && pick !== null && !('free' in pick && pick.free === 'custom' && !label.trim())
  function draw() {
    if (!pick) return
    if ('rel' in pick) onDraw(pick.rel.from, pick.rel.to, pick.rel.edgeKind, label)
    else onDraw(x, y, pick.free, label)
  }

  return (
    <div className="modal-scrim" onClick={onCancel}>
      <div className="modal modal-rel" onClick={e => e.stopPropagation()}>
        <div className="modal-title">draw a line</div>

        <div className="rel-pick">
          <select value={x} onChange={e => changeX(e.target.value)}>
            <option value="">choose x…</option>
            {members.map(m => <option key={m} value={m}>{m}</option>)}
          </select>
          <span className="rel-pick-arrow">↔</span>
          <select value={y} onChange={e => changeY(e.target.value)}>
            <option value="">choose y…</option>
            {members.map(m => <option key={m} value={m}>{m}</option>)}
          </select>
        </div>

        {!pairReady && <div className="rel-empty">pick two distinct services to relate.</div>}

        {pairReady && (
          <>
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
          </>
        )}

        <div className="modal-actions">
          <button className="run" onClick={onCancel}>cancel</button>
          <button className="run modal-primary" disabled={!ready} onClick={draw}>draw the line</button>
        </div>
      </div>
    </div>
  )
}
