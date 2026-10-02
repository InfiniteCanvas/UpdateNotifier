<script lang="ts">
	let { count, limit }: { count: number; limit: number | null } = $props();
	const pct = $derived(limit === null || limit === 0 ? 0 : Math.min(100, Math.round((count / limit) * 100)));
	const hot = $derived(limit !== null && count / limit >= 0.9);
</script>

{#if limit === null}
	<p class="limit-count">{count} tracked · no limit</p>
{:else}
	<div class="limit-meter">
		<div class="meter-track">
			<div class="meter-fill" data-hot={hot ? 'true' : 'false'} style="width: {pct}%"></div>
		</div>
		<span class="limit-count">{count} / {limit}</span>
	</div>
{/if}
