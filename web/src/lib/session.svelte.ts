// Tiny runes-based session store, exported as a singleton.
// Also mirrors the extension hash into localStorage['un_hash']:
// refreshed on every successful /me, cleared on logout and account deletion.

import { api, UnauthorizedError, type Me } from '#lib/api.ts';

const HASH_KEY = 'un_hash';

function saveHash(hash: string): void {
	try {
		localStorage.setItem(HASH_KEY, hash);
	} catch {
		// storage unavailable (private mode etc.) — in-memory state still works
	}
}

class SessionStore {
	me: Me | null = $state(null);
	loading: boolean = $state(false);

	/** Fetch /auth/me. Returns null (and resets state) when not signed in. */
	async refresh(): Promise<Me | null> {
		this.loading = true;
		try {
			const me = await api.me();
			this.me = me;
			saveHash(me.hash);
			return me;
		} catch (err) {
			if (err instanceof UnauthorizedError) {
				this.me = null;
				return null;
			}
			throw err;
		} finally {
			this.loading = false;
		}
	}

	/** Update the hash in state + localStorage after a regenerate. */
	setHash(hash: string): void {
		if (this.me) this.me = { ...this.me, hash };
		saveHash(hash);
	}

	/** Log out on the server (best effort) and clear local state + stored hash. */
	async logout(): Promise<void> {
		try {
			await api.logout();
		} catch {
			// server unreachable / already signed out — clearing locally is still correct
		} finally {
			this.clear();
		}
	}

	/** Clear local session state and the mirrored hash (logout + account deletion). */
	clear(): void {
		this.me = null;
		try {
			localStorage.removeItem(HASH_KEY);
		} catch {
			// ignore
		}
	}
}

export const session = new SessionStore();
