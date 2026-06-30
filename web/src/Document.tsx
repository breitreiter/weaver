import { useEffect, useRef, useState } from 'react'
import {
  EditorView, keymap, drawSelection, lineNumbers, ViewPlugin, Decoration, MatchDecorator,
  type DecorationSet, type ViewUpdate,
} from '@codemirror/view'
import { Annotation } from '@codemirror/state'
import { defaultKeymap, history, historyKeymap } from '@codemirror/commands'
import { markdown } from '@codemirror/lang-markdown'
import { syntaxHighlighting, HighlightStyle } from '@codemirror/language'
import { autocompletion, type CompletionContext, type CompletionResult } from '@codemirror/autocomplete'
import { tags } from '@lezer/highlight'
import { api, type Board as BoardData } from './api'

// The document — weaver's synthesis surface (replaces the graph). A CodeMirror 6
// markdown editor co-edited by the human and the agent (which writes via the CLI's
// `doc edit`). Persistence is the version-checked PUT /doc: disjoint concurrent
// edits 3-way-merge server-side; same-line collisions bounce. See co-edit-document.md.
//
// Sync model (Architecture C):
//  - baseRef = the last server-acked {text, version} — the ancestor for the next PUT.
//  - while the human is mid-edit (editor text ≠ baseRef.text) we IGNORE poll updates
//    (no clobber); our next save merges. When in sync, we adopt the server's text so
//    the agent's edits appear live.
//  - on save, if the server merged in remote edits and the human hasn't typed since,
//    we adopt the authoritative merged text. On a same-line conflict we re-base on the
//    server and resave — the human's edit wins the contested line (they have final say).

// marks a programmatic doc replacement so the change-listener doesn't treat it as a
// human keystroke (which would re-trigger a save and loop).
const Programmatic = Annotation.define<boolean>()

const mdHighlight = HighlightStyle.define([
  { tag: tags.heading1, fontSize: '1.4em', fontWeight: '600', color: 'var(--text-h)' },
  { tag: tags.heading2, fontSize: '1.2em', fontWeight: '600', color: 'var(--text-h)' },
  { tag: tags.heading3, fontSize: '1.06em', fontWeight: '600', color: 'var(--text-h)' },
  { tag: tags.strong, fontWeight: '700', color: 'var(--text-h)' },
  { tag: tags.emphasis, fontStyle: 'italic' },
  { tag: tags.link, color: 'var(--accent)', textDecoration: 'underline' },
  { tag: tags.monospace, fontFamily: 'ui-monospace, SFMono-Regular, Consolas, monospace', color: 'var(--text)' },
  { tag: tags.quote, color: 'var(--text-dim)' },
  { tag: tags.list, color: 'var(--text)' },
])

const theme = EditorView.theme({
  '&': { height: '100%', color: 'var(--text)', backgroundColor: 'transparent' },
  '.cm-scroller': { overflow: 'auto', fontFamily: 'ui-sans-serif, system-ui, "Segoe UI", Roboto, sans-serif', lineHeight: '1.65' },
  '.cm-content': { padding: '14px 16px 40vh', caretColor: 'var(--accent)', fontSize: '14px' },
  '.cm-gutters': { backgroundColor: 'transparent', color: 'var(--text-dim)', border: 'none' },
  '.cm-lineNumbers .cm-gutterElement': { padding: '0 6px 0 10px', minWidth: '2.5ch' },
  '.cm-activeLineGutter': { backgroundColor: 'transparent', color: 'var(--text)' },
  '&.cm-focused': { outline: 'none' },
  '.cm-cursor, .cm-dropCursor': { borderLeftColor: 'var(--accent)' },
  '.cm-selectionBackground, &.cm-focused .cm-selectionBackground, .cm-content ::selection': {
    backgroundColor: 'color-mix(in srgb, var(--accent) 26%, transparent)',
  },
  '.cm-atref': { color: 'var(--accent)', cursor: 'pointer', textDecoration: 'underline', textDecorationStyle: 'dotted', textUnderlineOffset: '2px' },
  '.cm-tooltip': { backgroundColor: 'var(--panel-2)', border: '1px solid var(--border)', borderRadius: '8px', color: 'var(--text)' },
  '.cm-tooltip-autocomplete > ul > li': { padding: '3px 8px' },
  '.cm-tooltip-autocomplete > ul > li[aria-selected]': { backgroundColor: 'var(--accent)', color: '#0c1018' },
  '.cm-completionDetail': { color: 'var(--text-dim)', fontStyle: 'normal', marginLeft: '8px', fontSize: '11px' },
}, { dark: true })

function replaceDoc(view: EditorView, text: string) {
  view.dispatch({
    changes: { from: 0, to: view.state.doc.length, insert: text },
    annotations: Programmatic.of(true),
  })
}

type SaveState = 'synced' | 'editing' | 'saving' | 'merging'

export default function Document({ board, boardId, ensureBoard, onFocus }: {
  board: BoardData | null
  boardId: string | null
  ensureBoard: () => Promise<string>
  onFocus: (svc: string) => void
}) {
  const parentRef = useRef<HTMLDivElement>(null)
  const viewRef = useRef<EditorView | null>(null)
  const boardRef = useRef(board)
  const boardIdRef = useRef(boardId)
  const ensureBoardRef = useRef(ensureBoard)
  const onFocusRef = useRef(onFocus)
  const baseTextRef = useRef('')
  const baseVerRef = useRef(0)
  const savingRef = useRef(false)
  const saveTimer = useRef<number | undefined>(undefined)
  const [saveState, setSaveState] = useState<SaveState>('synced')

  // keep refs fresh for the editor's long-lived callbacks (no deps → every render).
  useEffect(() => {
    boardRef.current = board; boardIdRef.current = boardId
    ensureBoardRef.current = ensureBoard; onFocusRef.current = onFocus
  })

  // create the editor once; all extensions read live values via refs.
  useEffect(() => {
    baseTextRef.current = boardRef.current?.doc ?? ''
    baseVerRef.current = boardRef.current?.docVersion ?? 0

    const scheduleSave = (delay = 700) => {
      clearTimeout(saveTimer.current)
      saveTimer.current = window.setTimeout(doSave, delay)
    }

    async function doSave() {
      const view = viewRef.current
      if (!view || savingRef.current) return
      const sent = view.state.doc.toString()
      if (sent === baseTextRef.current) { setSaveState('synced'); return }
      savingRef.current = true
      setSaveState('saving')
      try {
        // no board yet? the first keystrokes create one lazily, so you can capture
        // initial context before pinning anything.
        const id = boardIdRef.current ?? await ensureBoardRef.current()
        const res = await api.putDoc(id, { baseVersion: baseVerRef.current, baseText: baseTextRef.current, text: sent })
        baseTextRef.current = res.doc
        baseVerRef.current = res.docVersion
        if (res.conflict) {
          // same-line collision: server kept its text. Re-base on it and resave —
          // the human's edit re-applies over the new base (they win the contested line).
          setSaveState('merging')
          scheduleSave(0)
        } else {
          // server may have merged in remote edits; if the human hasn't typed since,
          // adopt the authoritative merged text.
          const now = view.state.doc.toString()
          if (res.doc !== now && now === sent) replaceDoc(view, res.doc)
          setSaveState('synced')
        }
      } catch {
        setSaveState('editing') // network/other error — keep local text, retry on next edit
      } finally {
        savingRef.current = false
        if ((viewRef.current?.state.doc.toString() ?? '') !== baseTextRef.current) scheduleSave()
      }
    }

    const atSource = (ctx: CompletionContext): CompletionResult | null => {
      const tok = ctx.matchBefore(/@[\w:.-]*/)
      if (!tok || (tok.from === tok.to && !ctx.explicit)) return null
      const b = boardRef.current
      if (!b) return null
      // reference by the canonical typed id (an:svc:metric, tr:…) — the same identity
      // used everywhere else. The opaque storage id is only a fallback for the rare
      // manual pin that has no typed id. Dropdown shows a friendly summary as detail.
      const options = b.nodes.flatMap(n => [
        { label: '@' + n.serviceId, type: 'class', detail: 'service' },
        ...n.evidence.map(ev => ({ label: '@' + (ev.refId ?? ev.id), type: ev.kind, detail: ev.summary || ev.kind })),
      ])
      return { from: tok.from, options, validFor: /^@[\w:.-]*$/ }
    }

    const atMatcher = new MatchDecorator({ regexp: /@[\w:.-]+/g, decoration: Decoration.mark({ class: 'cm-atref' }) })
    const atPlugin = ViewPlugin.fromClass(class {
      decorations: DecorationSet
      constructor(view: EditorView) { this.decorations = atMatcher.createDeco(view) }
      update(u: ViewUpdate) { this.decorations = atMatcher.updateDeco(u, this.decorations) }
    }, { decorations: v => v.decorations })

    // click an @ref → focus its service (highlights the node, scrolls the drawer).
    // returns false so the click still places the cursor for editing.
    const clickFocus = EditorView.domEventHandlers({
      mousedown(e) {
        const t = e.target as HTMLElement
        if (!t.classList?.contains('cm-atref')) return false
        const id = (t.textContent ?? '').replace(/^@/, '')
        const b = boardRef.current
        const svc = b?.nodes.some(n => n.serviceId === id)
          ? id
          : b?.nodes.find(n => n.evidence.some(ev => ev.refId === id || ev.id === id))?.serviceId
        if (svc) onFocusRef.current(svc)
        return false
      },
    })

    const view = new EditorView({
      doc: baseTextRef.current,
      parent: parentRef.current!,
      extensions: [
        lineNumbers(),
        history(),
        drawSelection(),
        EditorView.lineWrapping,
        markdown(),
        syntaxHighlighting(mdHighlight),
        autocompletion({ override: [atSource] }),
        atPlugin,
        clickFocus,
        keymap.of([...defaultKeymap, ...historyKeymap]),
        theme,
        EditorView.updateListener.of(u => {
          if (u.docChanged && !u.transactions.some(tr => tr.annotation(Programmatic))) {
            setSaveState('editing')
            scheduleSave()
          }
        }),
      ],
    })
    viewRef.current = view
    return () => { clearTimeout(saveTimer.current); view.destroy(); viewRef.current = null }
    // mount once — a lazily-created board must NOT remount the editor (it'd lose text).
  }, [])

  // adopt server text when the editor is in sync (not mid-edit) — this is how the
  // agent's edits appear live. Mid-edit, we hold local and let the next save merge.
  useEffect(() => {
    const view = viewRef.current
    if (!view || !board) return
    const cur = view.state.doc.toString()
    if (cur !== baseTextRef.current) return // dirty — don't clobber the human
    if (board.doc !== cur) replaceDoc(view, board.doc)
    baseTextRef.current = board.doc
    baseVerRef.current = board.docVersion
    setSaveState('synced')
  }, [board])

  return (
    <div className="doc-pane">
      <div className="doc-editor" ref={parentRef} />
      <div className="doc-foot">
        <span className={'doc-status doc-status-' + saveState}>{STATUS_LABEL[saveState]}</span>
        <span className="doc-ver mono">v{board?.docVersion ?? 0}</span>
      </div>
    </div>
  )
}

const STATUS_LABEL: Record<SaveState, string> = {
  synced: 'synced', editing: 'editing…', saving: 'saving…', merging: 'merging…',
}
