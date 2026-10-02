// Freshness buckets for the ledger ramp: fresh (< 24h) → recent (< 3d) → cooling (< 7d) → settled.
export type Freshness = 'fresh' | 'recent' | 'cooling' | 'settled';

export function freshnessOf(iso: string, now: number = Date.now()): Freshness {
	const then = Date.parse(iso);
	if (Number.isNaN(then)) return 'settled';
	const ageMs = now - then;
	if (ageMs < 24 * 3_600_000) return 'fresh';
	if (ageMs < 3 * 24 * 3_600_000) return 'recent';
	if (ageMs < 7 * 24 * 3_600_000) return 'cooling';
	return 'settled';
}
