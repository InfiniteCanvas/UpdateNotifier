<script lang="ts">
	import { freshnessOf } from '#lib/freshness.ts';
	import AgeChip from '#lib/components/AgeChip.svelte';
	import type { GameEntry } from '#lib/api.ts';

	let {
		game,
		removing,
		onunfollow
	}: {
		game: GameEntry;
		removing: boolean;
		onunfollow: (game: GameEntry) => void;
	} = $props();

	const freshness = $derived(freshnessOf(game.lastUpdated));

	// Flips when the banner image fails to load, leaving the plain placeholder box.
	// Safe as per-row state: the ledger list is keyed by gameId.
	let thumbFailed = $state(false);
</script>

<li class="game-row">
	<span class="tick" data-fresh={freshness} aria-hidden="true"></span>
	<a class="thumb" href={game.url} target="_blank" rel="noopener noreferrer" tabindex="-1" aria-hidden="true">
		{#if game.thumbnailUrl && !thumbFailed}
			<img src={game.thumbnailUrl} alt={game.title} loading="lazy" decoding="async" referrerpolicy="no-referrer" onerror={() => (thumbFailed = true)} />
		{/if}
	</a>
	<div class="game-main">
		{#if freshness === 'fresh'}<span class="pulse" aria-hidden="true"></span>{/if}
		<a class="game-title" href={game.url} target="_blank" rel="noopener noreferrer">{game.title}</a>
	</div>
	<AgeChip iso={game.lastUpdated} />
	<button class="btn danger small" disabled={removing} onclick={() => onunfollow(game)}>
		{removing ? 'Removing…' : 'Unfollow'}
	</button>
</li>
