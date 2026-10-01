// Small date/time formatting helpers.

const rtf = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' });

const UNITS: readonly (readonly [Intl.RelativeTimeFormatUnit, number])[] = [
	['year', 31_557_600_000],
	['month', 2_629_800_000],
	['week', 604_800_000],
	['day', 86_400_000],
	['hour', 3_600_000],
	['minute', 60_000],
	['second', 1000]
];

/** "3 days ago" / "in 2 hours" style rendering of an ISO timestamp. */
export function relativeTime(iso: string, now: number = Date.now()): string {
	const then = Date.parse(iso);
	if (Number.isNaN(then)) return iso;
	const diff = then - now;
	const abs = Math.abs(diff);
	for (const [unit, ms] of UNITS) {
		if (abs >= ms || unit === 'second') {
			return rtf.format(Math.round(diff / ms), unit);
		}
	}
	return iso;
}

/** Absolute, locale-formatted date+time — used as the tooltip on relative times. */
export function absoluteTime(iso: string): string {
	const date = new Date(iso);
	if (Number.isNaN(date.getTime())) return iso;
	return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(date);
}
