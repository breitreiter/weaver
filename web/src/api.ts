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

export interface BoardItem { id: string; kind: string; ref: string; evidence?: unknown; label?: string; x?: number; y?: number }
export interface BoardEdge { id: string; fromItem: string; toItem: string; kind: string; label?: string; drawnBy: string }
export interface Board { id: string; title: string; createdAt: string; items: BoardItem[]; edges: BoardEdge[] }
export interface Created { id: string; url: string }
export interface PinInput { kind: string; ref: string; label?: string; evidence?: unknown }

// --- structured search (the left-panel query layer) ---------------------
export interface Facets {
  window: { start: string; end: string }
  subsystems: string[]; kinds: string[]; teams: string[]
  metrics: string[]; logLevels: string[]; logTemplates: string[]
  routes: string[]; traceStatuses: string[]; changeKinds: string[]
}
export interface EvidenceRef { kind: string; aspect: string; at?: string | null; payload?: unknown }
export interface PinTarget { nodeIds: string[]; evidence?: EvidenceRef | null }
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export interface SearchResult { type: string; id: string; title: string; subtitle: string; payload?: any; pin: PinTarget }

export type SearchParams = {
  scope: string; q?: string
  subsystem?: string; kind?: string; team?: string
  level?: string; template?: string; route?: string; status?: string; minMs?: number
  metric?: string; split?: string; z?: number; minPct?: number; limit?: number
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
  logs: (p: { serviceId?: string; level?: string; grep?: string; limit?: number }) =>
    get<LogEvent[]>(`/logs${qs({ serviceId: p.serviceId, level: p.level, q: p.grep, limit: p.limit })}`),
  traces: (p: { route?: string; status?: string; minDurationMs?: number; limit?: number }) =>
    get<Trace[]>(`/traces${qs({ ...p })}`),

  createBoard: (title?: string) => post<Created>('/boards', { title }),
  getBoard: (id: string) => get<Board>(`/boards/${id}`),
  pin: (boardId: string, item: PinInput) => post<Created>(`/boards/${boardId}/items`, item),

  facets: () => get<Facets>('/search/facets'),
  search: (p: SearchParams) => get<SearchResult[]>(`/search${qs({ ...p })}`),
}
