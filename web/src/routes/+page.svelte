<script lang="ts">
	import { onMount } from 'svelte';
	import { goto } from '$app/navigation';
	import {
		api,
		ApiError,
		extractErrorMessage,
		THREAD_URL_RE,
		UnauthorizedError,
		type GameEntry,
		type LinkCodeInfo,
		type TrackResult
	} from '#lib/api.ts';
	import { session } from '#lib/session.svelte.ts';
	import { absoluteTime, relativeTime } from '#lib/time.ts';
	import { copyText } from '#lib/clipboard.ts';

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

	// add-game form
	let newUrl = $state('');
	let urlError = $state('');
	let addPending = $state(false);
	let addMsg = $state('');
	let addMsgIsError = $state(false);

	// account / hash
	let copyNote = $state('');
	let copyNoteTimer: ReturnType<typeof setTimeout> | undefined;
	let regenPending = $state(false);
	let regenMsg = $state('');
	let regenMsgIsError = $state(false);

	// Discord linking
	let linkPending = $state(false);
	let unlinkPending = $state(false);
	let linkError = $state('');
	let linkCode: LinkCodeInfo | null = $state(null);
	let nowTs = $state(Date.now());

	// danger zone
	let deletePassword = $state('');
	let deletePending = $state(false);
	let deleteError = $state('');

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

	/** Map a plain-text track/untrack response to displayable feedback. */
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

	async function addGame(event: SubmitEvent): Promise<void> {
		event.preventDefault();
		const url = newUrl.trim();
		urlError = '';
		addMsg = '';
		if (!THREAD_URL_RE.test(url)) {
			urlError = 'Enter a valid F95Zone thread URL (https://f95zone.to/threads/…).';
			return;
		}
		const hash = session.me?.hash;
		if (!hash) {
			await goto('/login');
			return;
		}
		addPending = true;
		try {
			const res = await api.track(hash, url);
			if (res.status === 401) {
				await goto('/login');
				return;
			}
			const outcome = trackResponseMessage(res);
			addMsg = outcome.text;
			addMsgIsError = outcome.isError;
			if (!outcome.isError) newUrl = '';
			if (res.status === 404) await session.refresh().catch(() => null);
		} catch {
			addMsg = 'Network error — could not reach the server.';
			addMsgIsError = true;
		} finally {
			addPending = false;
		}
		await reloadGames();
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

	async function copy(value: string): Promise<void> {
		const ok = await copyText(value);
		copyNote = ok ? 'Copied!' : 'Copy failed — select the text and copy it manually.';
		clearTimeout(copyNoteTimer);
		copyNoteTimer = setTimeout(() => (copyNote = ''), 2500);
	}

	async function regenerate(): Promise<void> {
		const confirmed = confirm(
			'Regenerate your hash?\n\n' +
				"This invalidates your browser extension's saved hash until you update it."
		);
		if (!confirmed) return;
		regenPending = true;
		regenMsg = '';
		try {
			const { hash } = await api.regenerateHash();
			session.setHash(hash);
			regenMsg = 'Hash regenerated. Update your browser extension with the new value above.';
			regenMsgIsError = false;
		} catch (err) {
			if (err instanceof UnauthorizedError) {
				await goto('/login');
				return;
			}
			regenMsg = err instanceof ApiError ? err.message : 'Could not regenerate the hash.';
			regenMsgIsError = true;
		} finally {
			regenPending = false;
		}
	}

	async function startLink(): Promise<void> {
		linkPending = true;
		linkError = '';
		try {
			linkCode = await api.createLinkCode();
			nowTs = Date.now();
		} catch (err) {
			if (err instanceof UnauthorizedError) {
				await goto('/login');
				return;
			}
			linkError = err instanceof ApiError ? err.message : 'Could not create a link code.';
		} finally {
			linkPending = false;
		}
	}

	async function unlinkDiscord(): Promise<void> {
		const confirmed = confirm(
			'Unlink Discord?\n\n' +
				'The Discord identity row will be removed from your account, ' +
				'and re-linking requires running /enable in the bot again.'
		);
		if (!confirmed) return;
		unlinkPending = true;
		linkError = '';
		try {
			await api.unlinkDiscord();
			await session.refresh();
		} catch (err) {
			if (err instanceof UnauthorizedError) {
				await goto('/login');
				return;
			}
			linkError = err instanceof ApiError ? err.message : 'Could not unlink Discord.';
		} finally {
			unlinkPending = false;
		}
	}

	async function deleteAccount(event: SubmitEvent): Promise<void> {
		event.preventDefault();
		deleteError = '';
		if (!deletePassword) {
			deleteError = 'Enter your password to confirm.';
			return;
		}
		const confirmed = confirm(
			'Delete your account?\n\n' +
				'This permanently deletes your account and every tracked game. This cannot be undone.'
		);
		if (!confirmed) return;
		deletePending = true;
		try {
			await api.deleteAccount(deletePassword);
			// Server deleted the account + data and cleared the cookie; clear local state + hash.
			session.clear();
			deletePassword = '';
			await goto('/login');
		} catch (err) {
			if (err instanceof ApiError && err.status === 401) {
				deleteError = 'Incorrect password, or your session expired — sign in again and retry.';
			} else {
				deleteError = err instanceof ApiError ? err.message : 'Could not delete the account.';
			}
		} finally {
			deletePending = false;
		}
	}

	// While a link code is shown: tick a live countdown and poll /me every 5s,
	// flipping to "linked" automatically. Stops on success, expiry or unmount
	// (clearing linkCode re-runs this effect and runs the cleanup below).
	$effect(() => {
		const code = linkCode;
		if (!code) return;
		const expires = Date.parse(code.expiresAt);

		const ticker = setInterval(() => {
			nowTs = Date.now();
			if (nowTs >= expires) linkCode = null;
		}, 1000);

		const poll = setInterval(() => {
			void session
				.refresh()
				.then((me) => {
					if (!me || me.discordLinked || Date.now() >= expires) linkCode = null;
				})
				.catch(() => {
					// transient network error — keep polling until expiry
				});
		}, 5000);

		return () => {
			clearInterval(ticker);
			clearInterval(poll);
		};
	});

	const countdown = $derived.by(() => {
		const code = linkCode;
		if (!code) return '';
		const remaining = Math.max(0, Date.parse(code.expiresAt) - nowTs);
		const totalSeconds = Math.floor(remaining / 1000);
		const minutes = Math.floor(totalSeconds / 60);
		const seconds = totalSeconds % 60;
		return `expires in ${minutes}:${String(seconds).padStart(2, '0')}`;
	});
</script>

<svelte:head>
	<title>Dashboard · UpdateNotifier</title>
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
		<h1>Your tracked games</h1>
		<p class="muted">{games.length} {games.length === 1 ? 'game' : 'games'} tracked</p>
	</section>

	<section class="card" aria-labelledby="add-game-title">
		<h2 id="add-game-title">Track a new game</h2>
		<p class="card-sub">Paste an F95Zone thread URL.</p>
		<form onsubmit={addGame} novalidate>
			<div class="url-row">
				<input
					type="url"
					placeholder="https://f95zone.to/threads/…"
					aria-label="Thread URL"
					bind:value={newUrl}
					disabled={addPending}
				/>
				<button class="btn primary" type="submit" disabled={addPending}>
					{addPending ? 'Adding…' : 'Track'}
				</button>
			</div>
			{#if urlError}<p class="field-error" role="alert">{urlError}</p>{/if}
			{#if addMsg}<p class="msg {addMsgIsError ? 'error' : 'ok'}" role="alert">{addMsg}</p>{/if}
		</form>
	</section>

	<section class="card" aria-labelledby="games-title">
		<h2 id="games-title">Tracked games</h2>
		<p class="card-sub">Sorted by last update, newest first.</p>
		{#if gamesLoading}
			<p class="muted">Loading…</p>
		{:else if gamesError}
			<p class="msg error">{gamesError}</p>
		{:else if games.length === 0}
			<p class="muted">Nothing here yet — track your first game above.</p>
		{:else}
			{#if listMsg}<p class="msg {listMsgIsError ? 'error' : 'ok'}">{listMsg}</p>{/if}
			<ul class="game-list">
				{#each games as game (game.gameId)}
					<li class="game-row">
						<div class="game-main">
							<a class="game-title" href={game.url} target="_blank" rel="noopener noreferrer">
								{game.title}
							</a>
							<time class="game-time" datetime={game.lastUpdated} title={absoluteTime(game.lastUpdated)}>
								updated {relativeTime(game.lastUpdated)}
							</time>
						</div>
						<button
							class="btn danger small"
							disabled={unfollowingId !== null}
							onclick={() => void unfollow(game)}
						>
							{unfollowingId === game.gameId ? 'Removing…' : 'Unfollow'}
						</button>
					</li>
				{/each}
			</ul>
		{/if}
	</section>

	<section class="card" aria-labelledby="account-title">
		<h2 id="account-title">Account</h2>
		<p class="card-sub">Browser extension hash.</p>
		<div class="hash-row">
			<input class="mono" readonly aria-label="Extension hash" value={session.me?.hash ?? ''} />
			<button class="btn" onclick={() => void copy(session.me?.hash ?? '')} disabled={!session.me}>
				Copy
			</button>
		</div>
		{#if copyNote}<p class="hint" role="status">{copyNote}</p>{/if}
		<div class="row" style="margin-top: 14px">
			<button class="btn" onclick={regenerate} disabled={regenPending}>
				{regenPending ? 'Regenerating…' : 'Regenerate hash'}
			</button>
		</div>
		{#if regenMsg}<p class="msg {regenMsgIsError ? 'error' : 'ok'}">{regenMsg}</p>{/if}
	</section>

	<section class="card" aria-labelledby="discord-title">
		<h2 id="discord-title">Discord</h2>
		{#if session.me?.discordLinked}
			<p>Linked to Discord as <strong>{session.me.discordUsername ?? 'unknown'}</strong>.</p>
			<button class="btn danger" onclick={unlinkDiscord} disabled={unlinkPending}>
				{unlinkPending ? 'Unlinking…' : 'Unlink Discord'}
			</button>
		{:else if linkCode}
			{@const code = linkCode}
			<p class="muted">Run <code>/link {code.code}</code> in the bot on Discord.</p>
			<div class="hash-row">
				<input class="mono" readonly aria-label="Discord link code" value={code.code} />
				<button class="btn" onclick={() => void copy(code.code)}>Copy</button>
			</div>
			<p class="hint" role="status">{countdown}</p>
			<p class="hint">This page updates automatically once the bot confirms the link.</p>
		{:else}
			<p class="card-sub">Get update notifications in Discord.</p>
			<button class="btn primary" onclick={startLink} disabled={linkPending}>
				{linkPending ? 'Creating code…' : 'Link Discord'}
			</button>
		{/if}
		{#if linkError}<p class="msg error">{linkError}</p>{/if}
	</section>

	<section class="card danger-zone" aria-labelledby="danger-title">
		<h2 id="danger-title">Danger zone</h2>
		<p class="card-sub">Deleting your account removes it and every tracked game. There is no undo.</p>
		<form onsubmit={deleteAccount} novalidate>
			<div class="hash-row">
				<input
					type="password"
					placeholder="Password"
					aria-label="Password"
					autocomplete="current-password"
					bind:value={deletePassword}
					disabled={deletePending}
				/>
				<button class="btn danger" type="submit" disabled={deletePending}>
					{deletePending ? 'Deleting…' : 'Delete account'}
				</button>
			</div>
			{#if deleteError}<p class="field-error" role="alert">{deleteError}</p>{/if}
		</form>
	</section>
{/if}
