<script lang="ts">
	import { goto } from '$app/navigation';
	import { api, ApiError } from '#lib/api.ts';
	import { session } from '#lib/session.svelte.ts';

	let username = $state('');
	let password = $state('');
	let error = $state('');
	let pending = $state(false);

	async function submit(event: SubmitEvent): Promise<void> {
		event.preventDefault();
		error = '';
		if (!username.trim() || !password) {
			error = 'Enter your username and password.';
			return;
		}
		pending = true;
		try {
			await api.login(username.trim(), password);
			// Populate the header + hash mirror; the dashboard refreshes again on mount.
			await session.refresh().catch(() => null);
			await goto('/');
		} catch (err) {
			error = err instanceof ApiError ? err.message : 'Could not reach the server. Try again.';
		} finally {
			pending = false;
		}
	}
</script>

<svelte:head>
	<title>Sign in · UpdateNotifier</title>
</svelte:head>

<section class="card auth-card">
	<h1>Sign in</h1>
	<form onsubmit={submit} novalidate>
		<div class="field">
			<label for="login-username">Username</label>
			<input
				id="login-username"
				name="username"
				autocomplete="username"
				autocapitalize="none"
				spellcheck="false"
				bind:value={username}
				disabled={pending}
			/>
		</div>
		<div class="field">
			<label for="login-password">Password</label>
			<input
				id="login-password"
				name="password"
				type="password"
				autocomplete="current-password"
				bind:value={password}
				disabled={pending}
			/>
		</div>
		{#if error}<p class="msg error" role="alert">{error}</p>{/if}
		<button class="btn primary block" type="submit" disabled={pending}>
			{pending ? 'Signing in…' : 'Sign in'}
		</button>
	</form>
	<p class="auth-alt">No account yet? <a href="/register">Create one</a></p>
</section>
