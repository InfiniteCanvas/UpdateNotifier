<script lang="ts">
	import { goto } from '$app/navigation';
	import { api, extractErrorMessage, THREAD_URL_RE } from '#lib/api.ts';
	import { session } from '#lib/session.svelte.ts';

	let { ontracked }: { ontracked: () => void | Promise<void> } = $props();

	const STALE_HASH_MESSAGE =
		'Your hash is stale — it may have been rotated or regenerated. ' +
		'Run /get_hash in Discord (or regenerate from another session), then log out and back in.';

	let urls = $state('');
	let urlError = $state('');
	let pending = $state(false);
	let msg = $state('');
	let msgIsError = $state(false);

	async function track(event: SubmitEvent): Promise<void> {
		event.preventDefault();
		urlError = '';
		msg = '';
		const tokens = [...new Set(urls.trim().split(/\s+/).filter(Boolean))];
		if (tokens.length === 0) return;
		const invalid = tokens.filter((t) => !THREAD_URL_RE.test(t));
		if (invalid.length > 0) {
			urlError = `Not a valid F95Zone thread URL: ${invalid.join(', ')}`;
			return;
		}
		const hash = session.me?.hash;
		if (!hash) {
			await goto('/login');
			return;
		}
		pending = true;
		let okCount = 0;
		let alreadyCount = 0;
		let rateLimited = false;
		let firstFailure = '';
		try {
			for (const token of tokens) {
				const res = await api.track(hash, token);
				if (res.status === 401) {
					await goto('/login');
					return;
				}
				if (res.status === 404) {
					await session.refresh().catch(() => null);
					msg = STALE_HASH_MESSAGE;
					msgIsError = true;
					break;
				}
				if (res.status === 429) {
					rateLimited = true;
					break;
				}
				if (res.ok) {
					// 200 bodies say "Games added: …" and/or "Games already in watchlist: …";
					// an empty 200 means the server silently skipped the URL
					const text = res.text.toLowerCase();
					if (text.includes('games added')) okCount++;
					else if (text.includes('already in watchlist')) alreadyCount++;
					else if (!firstFailure)
						firstFailure = `The server accepted ${token} without a message — it may not have been recognized.`;
				} else if (!firstFailure) {
					firstFailure = extractErrorMessage(res.text, res.contentType).trim() || `Request failed (HTTP ${res.status}).`;
				}
			}
		} catch {
			firstFailure = firstFailure || 'Network error — could not reach the server.';
		} finally {
			pending = false;
		}
		if (rateLimited) {
			msg = `Rate limited — wait a minute, then track the remaining URLs. (${okCount} of ${tokens.length} tracked so far.)`;
			msgIsError = okCount === 0;
		} else if (firstFailure) {
			const prefix = alreadyCount > 0 ? `${alreadyCount} already on your list.\n` : '';
			msg = `Tracked ${okCount} of ${tokens.length}.\n${prefix}${firstFailure}`;
			msgIsError = okCount === 0 && alreadyCount === 0;
		} else if (!msg) {
			const summary: string[] = [];
			if (okCount > 0) summary.push(`Tracked ${okCount} ${okCount === 1 ? 'thread' : 'threads'}.`);
			if (alreadyCount > 0)
				summary.push(`${alreadyCount} ${alreadyCount === 1 ? 'thread was' : 'threads were'} already on your list.`);
			msg = summary.join('\n');
			msgIsError = false;
			urls = '';
		}
		await ontracked();
	}
</script>

<form class="command-bar" onsubmit={track} novalidate>
	<div class="input-row">
		<input
			type="url"
			placeholder="https://f95zone.to/threads/…"
			aria-label="Thread URL(s)"
			bind:value={urls}
			disabled={pending}
		/>
		<button class="btn primary" type="submit" disabled={pending}>
			{pending ? 'Tracking…' : 'Track'}
		</button>
	</div>
	<p class="command-hint">Paste one or more F95Zone thread URLs — separate multiple with spaces.</p>
	{#if urlError}<p class="field-error" role="alert">{urlError}</p>{/if}
	{#if msg}<p class="msg {msgIsError ? 'error' : 'ok'}" role="alert">{msg}</p>{/if}
</form>
