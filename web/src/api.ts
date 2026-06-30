// Typed client for Weaver.Api. Mirrors the Weaver.Contracts DTOs (camelCase on
// the wire). Observation + enumerating primitives only — no verdict endpoint.

export interface Service { id: string; name: string; kind: string; subsystem?: string; ownerTeam?: string }
export interface Dependency { id: string; fromService: string; toService: string; kind: string; critical?: boolean }
export interface RequestType { id: string; name: string; weight: number; path: string[] }
export interface Graph { services: Service[]; dependencies: Dependency[]; requestTypes: RequestType[] }

export interface MetricPoint { ts: string; value: number }
export interface MetricSeries { subjectKind: string; subjectId: string; metric: string; points: MetricPoint[] }

export interface ServiceDetail { service: Service; dependsOn: Dependency[]; dependedOnBy: Dependency[] }

export interface Anomaly {
  subjectKind: string; subjectId: string; metric: string
  baseMean: number; subjectMean: number; deltaPct: number; z: number
  direction: 'up' | 'down'; onsetTs?: string
}

export interface BlastNode { serviceId: string; hops: number }
export interface BlastRadius { node: string; count: number; dependents: BlastNode[] }

export interface LogEvent { id: string; serviceId: string; ts: string; level: string; templateId: string; message: string; fields: unknown }
export interface Trace { id: string; requestTypeId: string; rootServiceId: string; startedAt: string; durationMs: number; status: string }
export interface TimelineEntry { subjectId: string; subjectKind: string; metric: string; onsetTs: string; z: number }

// A real, observed relationship between two services — surfaced when you draw a
// line so you can ground a hypothesis in a fact instead of picking from a flat list.
export interface Relationship {
  group: string            // dependency | route | temporal
  from: string; to: string // the relationship's real direction (may flip the drag)
  edgeKind: string         // the board edge kind this would draw
  title: string; detail: string; suggestedLabel: string
  evidence?: unknown
}
export interface Relationships { a: string; b: string; relationships: Relationship[] }

// A board node is a service on the wall; evidence (anomaly/log/trace/metric/
// change) layers onto it. Edges connect services. No "item" — service or evidence.
// summary is computed server-side (one renderer for CLI `board show` + the UI card)
// refId = the canonical typed id (an:svc:metric, tr:…) — the @-reference handle in
// the document. id is the opaque storage handle (delete/unpin). Same identity as the CLI.
export interface EvidenceItem { id: string; kind: string; aspect: string; at?: string | null; payload?: unknown; label?: string; summary: string; refId?: string | null }
export interface BoardNode { serviceId: string; label?: string; evidence: EvidenceItem[] }
export interface BoardEdge { id: string; from: string; to: string; kind: string; label?: string; drawnBy: string; crossedOut: boolean }
export interface LinkInput { from: string; to: string; kind?: string; label?: string; drawnBy?: string }
export interface Board { id: string; title: string; createdAt: string; nodes: BoardNode[]; edges: BoardEdge[]; doc: string; docVersion: number }
// PUT /doc result: the authoritative text + version after an optimistic write.
// conflict=true means the writer's edit touched lines a concurrent edit also
// changed — it was NOT applied; the writer should re-base on `doc` and retry.
export interface DocResult { doc: string; docVersion: number; conflict: boolean }
export interface Created { id: string; url: string }
export interface PinInput { serviceIds: string[]; evidence?: EvidenceRef | null; label?: string }

// --- structured search (the left-panel query layer) ---------------------
export interface Facets {
  window: { start: string; end: string }
  subsystems: string[]; kinds: string[]; teams: string[]
  metrics: string[]; logLevels: string[]; logTemplates: string[]
  routes: string[]; traceStatuses: string[]; changeKinds: string[]
}
export interface EvidenceRef { kind: string; aspect: string; at?: string | null; payload?: unknown; refId?: string | null }
export interface PinTarget { nodeIds: string[]; evidence?: EvidenceRef | null }
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export interface SearchResult { type: string; id: string; title: string; subtitle: string; payload?: any; pin: PinTarget }

export type SearchParams = {
  scope: string; q?: string
  subsystem?: string; kind?: string; team?: string
  level?: string; template?: string; route?: string; status?: string; minMs?: number
  metric?: string; split?: string; z?: number; minPct?: number; limit?: number
  service?: string; trace?: string
  from?: string; to?: string
}

async function get<T>(path: string): Promise<T> {
  const res = await fetch('/api' + path)
  if (!res.ok) throw new Error(`${res.status} ${res.statusText}: ${await res.text()}`)
  return res.json() as Promise<T>
}

async function post<T>(path: string, body: unknown): Promise<T> {
  const res = await fetch('/api' + path, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(body),
  })
  if (!res.ok) throw new Error(`${res.status} ${res.statusText}: ${await res.text()}`)
  return res.json() as Promise<T>
}

async function del<T>(path: string): Promise<T> {
  const res = await fetch('/api' + path, { method: 'DELETE' })
  if (!res.ok) throw new Error(`${res.status} ${res.statusText}: ${await res.text()}`)
  return res.json() as Promise<T>
}

// 409 Conflict is an EXPECTED outcome for the doc PUT — it carries a typed body
// (DocResult with conflict=true), so only other non-2xx codes throw.
async function put<T>(path: string, body: unknown): Promise<T> {
  const res = await fetch('/api' + path, {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(body),
  })
  if (!res.ok && res.status !== 409) throw new Error(`${res.status} ${res.statusText}: ${await res.text()}`)
  return res.json() as Promise<T>
}

const qs = (params: Record<string, string | number | undefined>) => {
  const p = Object.entries(params).filter(([, v]) => v !== undefined && v !== '')
  return p.length ? '?' + p.map(([k, v]) => `${k}=${encodeURIComponent(String(v))}`).join('&') : ''
}

export const api = {
  graph: () => get<Graph>('/graph'),
  service: (id: string) => get<ServiceDetail>(`/services/${id}`),
  metrics: (subjectId: string, subjectKind = 'service') =>
    get<MetricSeries[]>(`/metrics${qs({ subjectId, subjectKind })}`),
  anomalies: (split?: string, z?: number, minPct?: number) =>
    get<Anomaly[]>(`/anomalies${qs({ split, z, minPct })}`),
  timeline: (split?: string, z?: number, minPct?: number) =>
    get<TimelineEntry[]>(`/timeline${qs({ split, z, minPct })}`),
  blastRadius: (node: string) => get<BlastRadius>(`/blast-radius/${node}`),
  relationships: (a: string, b: string) => get<Relationships>(`/relationships${qs({ a, b })}`),
  logs: (p: { serviceId?: string; level?: string; grep?: string; limit?: number }) =>
    get<LogEvent[]>(`/logs${qs({ serviceId: p.serviceId, level: p.level, q: p.grep, limit: p.limit })}`),
  traces: (p: { route?: string; status?: string; minDurationMs?: number; limit?: number }) =>
    get<Trace[]>(`/traces${qs({ ...p })}`),

  createBoard: (title?: string) => post<Created>('/boards', { title }),
  getBoard: (id: string) => get<Board>(`/boards/${id}`),
  putDoc: (boardId: string, body: { baseVersion: number; baseText: string; text: string }) =>
    put<DocResult>(`/boards/${boardId}/doc`, body),
  pin: (boardId: string, item: PinInput) => post<Created>(`/boards/${boardId}/pin`, item),
  link: (boardId: string, edge: LinkInput) => post<Created>(`/boards/${boardId}/edges`, edge),
  crossOut: (boardId: string, edgeId: string, crossedOut: boolean) =>
    post<BoardEdge>(`/boards/${boardId}/edges/${edgeId}/crossout`, { crossedOut }),
  deleteEdge: (boardId: string, edgeId: string) => del<{ ok: boolean }>(`/boards/${boardId}/edges/${edgeId}`),
  deleteEvidence: (boardId: string, evidenceId: string) => del<{ ok: boolean }>(`/boards/${boardId}/evidence/${evidenceId}`),
  deleteNode: (boardId: string, serviceId: string) => del<{ ok: boolean }>(`/boards/${boardId}/nodes/${encodeURIComponent(serviceId)}`),

  facets: () => get<Facets>('/search/facets'),
  search: (p: SearchParams) => get<SearchResult[]>(`/search${qs({ ...p })}`),
}
