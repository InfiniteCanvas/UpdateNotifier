<script lang="ts">
	import { onMount } from 'svelte';
	import { goto } from '$app/navigation';
	import { api, ApiError, UnauthorizedError, type LinkCodeInfo } from '#lib/api.ts';
	import { session } from '#lib/session.svelte.ts';
	import ConfirmDialog from '#lib/components/ConfirmDialog.svelte';
	import CopyField from '#lib/components/CopyField.svelte';

	let dialog = $state<ConfirmDialog>();

	let ready = $state(false);
	let pageError = $state('');

	// extension hash
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
		}
		ready = true;
	}

	async function regenerate(): Promise<void> {
		const ok = await dialog?.confirm({
			title: 'Regenerate hash?',
			body: "This invalidates your browser extension's saved hash until you update it.",
			confirmLabel: 'Regenerate'
		});
		if (!ok) return;
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
		const ok = await dialog?.confirm({
			title: 'Unlink Discord?',
			body: 'The Discord identity row will be removed from your account, and you can re-link at any time by running /link in the bot again.',
			confirmLabel: 'Unlink'
		});
		if (!ok) return;
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
		const ok = await dialog?.confirm({
			title: 'Delete account?',
			body: 'This permanently deletes your account and every tracked game. This cannot be undone.',
			confirmLabel: 'Delete account',
			danger: true
		});
		if (!ok) return;
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
	<title>Settings · UpdateNotifier</title>
</svelte:head>

{#if !ready}
	<p class="muted center">Loading…</p>
{:else if pageError}
	<section class="card">
		<h2>Something went wrong</h2>
		<p class="msg error">{pageError}</p>
	</section>
{:else}
	<a class="back-link" href="/">← Watchlist</a>
	<section class="page-head">
		<div>
			<h1>Settings</h1>
			<p class="stat-line">Extension hash, Discord delivery, and account removal.</p>
		</div>
	</section>

	<section class="card" aria-labelledby="extension-title">
		<h2 id="extension-title">Extension</h2>
		<p class="card-sub">
			The browser extension button uses this hash to add threads to your watchlist. Don't have the extension?
			<a
				href="https://github.com/InfiniteCanvas/Update-Notifier-Chromium-Extension/releases"
				target="_blank"
				rel="noopener noreferrer">Get it here</a
			>.
		</p>
		<CopyField value={session.me?.hash ?? ''} label="Extension hash" />
		<div class="row">
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
			<CopyField value={code.code} label="Discord link code" />
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
			<div class="input-row">
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

<ConfirmDialog bind:this={dialog} />
