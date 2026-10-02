<script lang="ts">
	import { freshnessOf } from '#lib/freshness.ts';
	import { absoluteTime, relativeTime } from '#lib/time.ts';
	let { iso }: { iso: string } = $props();
	const freshness = $derived(freshnessOf(iso));
</script>

<time class="age" data-fresh={freshness} datetime={iso} title={absoluteTime(iso)}>
	updated {relativeTime(iso)}
</time>
