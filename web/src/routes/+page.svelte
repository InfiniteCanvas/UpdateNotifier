<script lang="ts">
	import { onMount } from 'svelte';
	import { goto } from '$app/navigation';
	import {
		api,
		ApiError,
		extractErrorMessage,
		UnauthorizedError,
		type GameEntry,
		type TrackResult
	} from '#lib/api.ts';
	import { session } from '#lib/session.svelte.ts';
	import { freshnessOf } from '#lib/freshness.ts';
	import GameRow from '#lib/components/GameRow.svelte';
	import TrackBar from '#lib/components/TrackBar.svelte';
	import LimitMeter from '#lib/components/LimitMeter.svelte';

	const STALE_HASH_MESSAGE =
		'Your hash is stale — it may have been rotated or regenerated. ' +
		'Run /get_hash in Discord (or regenerate from another session), then log out and back in.';

	let ready = $state(false);
	let pageError = $state('');

	// tracked games
	let games: GameEntry[] = $state([]);
	let gamesLoading = $state(false);
	let gamesError = $state('');
	let unfollowingId: number | null = $state(null);
	let listMsg = $state('');
	let listMsgIsError = $state(false);

	// title filter
	let filter = $state('');
	const filtered = $derived.by(() => {
		const needle = filter.trim().toLowerCase();
		return needle ? games.filter((g) => g.title.toLowerCase().includes(needle)) : games;
	});
	const freshCount = $derived(games.filter((g) => freshnessOf(g.lastUpdated) === 'fresh').length);

	onMount(() => {
		void init();
	});

	async function init(): Promise<void> {
		try {
			const me = await session.refresh();
			if (!me) {
				await goto('/login');
				return;
			}
		} catch {
			pageError = 'Could not reach the server. Refresh the page to retry.';
			ready = true;
			return;
		}
		await reloadGames();
		ready = true;
	}

	async function reloadGames(): Promise<void> {
		gamesLoading = true;
		try {
			games = await api.myGames();
			gamesError = '';
		} catch (err) {
			if (err instanceof UnauthorizedError) {
				await goto('/login');
				return;
			}
			gamesError = err instanceof ApiError ? err.message : 'Could not load your games.';
		} finally {
			gamesLoading = false;
		}
	}

	/** Map a plain-text untrack response to displayable feedback. */
	function trackResponseMessage(res: TrackResult): { text: string; isError: boolean } {
		if (res.status === 404) {
			return { text: STALE_HASH_MESSAGE, isError: true };
		}
		if (!res.ok) {
			// 400 bodies may be plain text OR JSON validation problems — decide by content type.
			const text = extractErrorMessage(res.text, res.contentType).trim();
			return { text: text || `Request failed (HTTP ${res.status}).`, isError: true };
		}
		if (!res.text.trim()) {
			return {
				text: 'The server accepted the request but returned no message — the URL may not have been recognized.',
				isError: true
			};
		}
		return { text: res.text, isError: false };
	}

	async function unfollow(game: GameEntry): Promise<void> {
		const hash = session.me?.hash;
		if (!hash) return;
		unfollowingId = game.gameId;
		listMsg = '';
		try {
			const res = await api.untrack(hash, game.url);
			if (res.status === 401) {
				await goto('/login');
				return;
			}
			const outcome = trackResponseMessage(res);
			listMsg = outcome.text;
			listMsgIsError = outcome.isError;
			if (res.status === 404) await session.refresh().catch(() => null);
		} catch {
			listMsg = 'Network error — could not reach the server.';
			listMsgIsError = true;
		} finally {
			unfollowingId = null;
		}
		await reloadGames();
	}
</script>

<svelte:head>
	<title>Watchlist · UpdateNotifier</title>
</svelte:head>

{#if !ready}
	<p class="muted center">Loading…</p>
{:else if pageError}
	<section class="card">
		<h2>Something went wrong</h2>
		<p class="msg error">{pageError}</p>
	</section>
{:else}
	<section class="page-head">
		<div>
			<h1>Watchlist</h1>
			<p class="stat-line">
				{games.length} {games.length === 1 ? 'game' : 'games'} tracked
				{#if freshCount > 0}<span class="stat-fresh"> · {freshCount} fresh</span>{/if}
			</p>
		</div>
		<LimitMeter count={games.length} limit={session.me?.gameLimit ?? null} />
	</section>

	<TrackBar ontracked={reloadGames} />

	{#if gamesLoading}
		<ul aria-hidden="true">
			{#each Array(3) as _}
				<li class="skeleton-row">
					<div class="skeleton-block skeleton-tick"></div>
					<div class="skeleton-block skeleton-line wide"></div>
					<div class="skeleton-block skeleton-age"></div>
				</li>
			{/each}
		</ul>
	{:else if gamesError}
		<p class="msg error">{gamesError}</p>
	{:else if games.length === 0}
		<div class="empty-state">
			<p>Nothing tracked yet — paste a thread URL above to start your watchlist.</p>
			<div class="ramp-legend">
				<span><span class="ramp-dot" style="background: var(--fresh-1)"></span>today</span>
				<span><span class="ramp-dot" style="background: var(--fresh-2)"></span>3 days</span>
				<span><span class="ramp-dot" style="background: var(--fresh-3)"></span>a week</span>
				<span><span class="ramp-dot" style="background: var(--fresh-4)"></span>older</span>
			</div>
		</div>
	{:else}
		<div class="filter-row">
			<input type="search" placeholder="Filter by title…" aria-label="Filter by title" bind:value={filter} />
			{#if filter.trim()}<span class="filter-count">{filtered.length} of {games.length}</span>{/if}
		</div>
		{#if listMsg}<p class="msg {listMsgIsError ? 'error' : 'ok'}" role="alert">{listMsg}</p>{/if}
		{#if filtered.length === 0}
			<p class="muted">No titles match “{filter.trim()}”.</p>
		{:else}
			<ul class="ledger">
				{#each filtered as game (game.gameId)}
					<GameRow game={game} removing={unfollowingId === game.gameId} onunfollow={unfollow} />
				{/each}
			</ul>
		{/if}
	{/if}
{/if}
