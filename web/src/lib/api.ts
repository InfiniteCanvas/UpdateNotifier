// Typed client for the UpdateNotifier HTTP API.
// All calls are same-origin: the `un_session` cookie rides along automatically.

export interface Me {
	accountId: number;
	username: string;
	hash: string;
	discordLinked: boolean;
	discordUsername: string | null;
	gameLimit: number | null; // null = unlimited
}

export interface GameEntry {
	gameId: number;
	title: string;
	url: string;
	lastUpdated: string; // ISO timestamp
	thumbnailUrl: string | null; // wide 1600x400 banner image; null when unknown
}

export interface LinkCodeInfo {
	code: string;
	expiresAt: string; // ISO timestamp
}

/** Raw outcome of POST/DELETE /api/v1/games — those endpoints speak plain text, not JSON. */
export interface TrackResult {
	ok: boolean;
	status: number;
	text: string;
	contentType: string;
}

/** Error thrown for any non-2xx response; `status` carries the HTTP code. */
export class ApiError extends Error {
	readonly status: number;

	constructor(status: number, message: string) {
		super(message);
		this.name = 'ApiError';
		this.status = status;
	}
}

/**
 * Thrown by session-gated endpoints when the cookie is missing/expired.
 * Pages translate this into a redirect to /login.
 */
export class UnauthorizedError extends ApiError {
	constructor() {
		super(401, 'Not signed in');
		this.name = 'UnauthorizedError';
	}
}

// --- client-side validation patterns (mirror the API rules) ---

/** 3–32 chars of letters, digits, ".", "-", "_" (length checked separately). */
export const USERNAME_RE = /^[A-Za-z0-9_.-]+$/;

/** Recognized F95Zone thread URL. Unrecognized URLs must NOT be sent to POST /games. */
export const THREAD_URL_RE = /(https:\/\/f95zone\.to\/threads\/)(.*?\.)?([0-9]+)\/?/;

// --- plumbing ---

async function request(url: string, init?: RequestInit): Promise<Response> {
	return fetch(url, { credentials: 'same-origin', ...init });
}

function postJson(url: string, method: 'POST' | 'DELETE', body: unknown): Promise<Response> {
	return request(url, {
		method,
		headers: { 'content-type': 'application/json' },
		body: JSON.stringify(body)
	});
}

/**
 * Pull a human-readable message out of an error body. Bodies may be plain text,
 * `{error}` JSON, or ASP.NET validation-problem JSON — decide by content type.
 */
export function extractErrorMessage(text: string, contentType: string): string {
	if (contentType.includes('json')) {
		try {
			const message = messageFromJson(JSON.parse(text));
			if (message) return message;
		} catch {
			// not valid JSON after all — fall back to the raw text below
		}
	}
	return text;
}

function messageFromJson(data: unknown): string | null {
	if (typeof data === 'string') return data || null;
	if (!data || typeof data !== 'object') return null;
	const obj = data as Record<string, unknown>;
	if (typeof obj.error === 'string' && obj.error) return obj.error;
	const lines: string[] = [];
	if (typeof obj.title === 'string' && obj.title) lines.push(obj.title);
	if (typeof obj.detail === 'string' && obj.detail) lines.push(obj.detail);
	if (obj.errors && typeof obj.errors === 'object') {
		for (const value of Object.values(obj.errors as Record<string, unknown>)) {
			if (typeof value === 'string') lines.push(value);
			else if (Array.isArray(value)) {
				lines.push(...value.filter((v): v is string => typeof v === 'string'));
			}
		}
	}
	return lines.length ? lines.join('\n') : null;
}

async function toApiError(res: Response): Promise<ApiError> {
	const text = await res.text().catch(() => '');
	const message = extractErrorMessage(text, res.headers.get('content-type') ?? '').trim();
	return new ApiError(res.status, message || `Request failed (HTTP ${res.status}).`);
}

/** Fetch expecting a JSON body (204/empty 2xx resolves to undefined). */
async function fetchJson<T>(url: string, init?: RequestInit): Promise<T> {
	const res = await request(url, init);
	if (!res.ok) throw await toApiError(res);
	const text = await res.text();
	return (text ? JSON.parse(text) : undefined) as T;
}

/** Like fetchJson, but converts 401 into UnauthorizedError for session-gated endpoints. */
async function fetchJsonAuthed<T>(url: string, init?: RequestInit): Promise<T> {
	try {
		return await fetchJson<T>(url, init);
	} catch (err) {
		if (err instanceof ApiError && err.status === 401) throw new UnauthorizedError();
		throw err;
	}
}

async function trackRequest(method: 'POST' | 'DELETE', hash: string, url: string): Promise<TrackResult> {
	const res = await postJson('/api/v1/games', method, {
		threadUrl: url,
		userHash: hash,
		discordNotification: false
	});
	const text = await res.text().catch(() => '');
	return { ok: res.ok, status: res.status, text, contentType: res.headers.get('content-type') ?? '' };
}

// --- endpoints ---

export const api = {
	register(username: string, password: string): Promise<{ accountId: number; username: string }> {
		return fetchJson('/api/v1/auth/register', postInit('POST', { username, password }));
	},

	login(username: string, password: string): Promise<{ accountId: number; username: string }> {
		return fetchJson('/api/v1/auth/login', postInit('POST', { username, password }));
	},

	logout(): Promise<void> {
		return fetchJson('/api/v1/auth/logout', { method: 'POST' });
	},

	deleteAccount(password: string): Promise<void> {
		return fetchJson('/api/v1/auth/delete', postInit('POST', { password }));
	},

	me(): Promise<Me> {
		return fetchJsonAuthed('/api/v1/auth/me');
	},

	regenerateHash(): Promise<{ hash: string }> {
		return fetchJsonAuthed('/api/v1/me/hash/regenerate', { method: 'POST' });
	},

	myGames(): Promise<GameEntry[]> {
		return fetchJsonAuthed('/api/v1/me/games');
	},

	createLinkCode(): Promise<LinkCodeInfo> {
		return fetchJsonAuthed('/api/v1/me/link/code', { method: 'POST' });
	},

	unlinkDiscord(): Promise<void> {
		return fetchJsonAuthed('/api/v1/me/link', { method: 'DELETE' });
	},

	track(hash: string, url: string): Promise<TrackResult> {
		return trackRequest('POST', hash, url);
	},

	untrack(hash: string, url: string): Promise<TrackResult> {
		return trackRequest('DELETE', hash, url);
	}
};

function postInit(method: 'POST' | 'DELETE', body: unknown): RequestInit {
	return {
		method,
		headers: { 'content-type': 'application/json' },
		body: JSON.stringify(body)
	};
}
