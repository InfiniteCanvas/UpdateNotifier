<script lang="ts">
	import { goto } from '$app/navigation';
	import { api, ApiError, USERNAME_RE } from '#lib/api.ts';
	import { session } from '#lib/session.svelte.ts';

	let username = $state('');
	let password = $state('');
	let passwordConfirm = $state('');
	let error = $state('');
	let pending = $state(false);

	function validate(): string {
		const name = username.trim();
		if (name.length < 3 || name.length > 32 || !USERNAME_RE.test(name)) {
			return 'Username must be 3–32 characters using letters, digits, ".", "-" or "_" only.';
		}
		if (password.length < 8) {
			return 'Password must be at least 8 characters.';
		}
		if (password !== passwordConfirm) {
			return 'Passwords do not match.';
		}
		return '';
	}

	async function submit(event: SubmitEvent): Promise<void> {
		event.preventDefault();
		error = '';
		const problem = validate();
		if (problem) {
			error = problem;
			return;
		}
		pending = true;
		try {
			await api.register(username.trim(), password);
			await session.refresh().catch(() => null);
			await goto('/');
		} catch (err) {
			if (err instanceof ApiError && err.status === 409) {
				error = 'That username is already taken.';
			} else {
				error = err instanceof ApiError ? err.message : 'Could not reach the server. Try again.';
			}
		} finally {
			pending = false;
		}
	}
</script>

<svelte:head>
	<title>Create account · UpdateNotifier</title>
</svelte:head>

<section class="card auth-card">
	<div class="auth-brand" aria-hidden="true">
		<svg width="28" height="28" viewBox="0 0 32 32">
			<rect width="32" height="32" rx="7" fill="#141925" />
			<circle cx="16" cy="16" r="6.5" fill="none" stroke="#ffad5c" stroke-width="2" opacity="0.5" />
			<circle cx="16" cy="16" r="3" fill="#ffad5c" />
		</svg>
	</div>
	<h1>Create an account</h1>
	<form onsubmit={submit} novalidate>
		<div class="field">
			<label for="register-username">Username</label>
			<input
				id="register-username"
				name="username"
				autocomplete="username"
				autocapitalize="none"
				spellcheck="false"
				bind:value={username}
				disabled={pending}
			/>
			<small class="hint">3–32 characters: letters, digits, ".", "-", "_".</small>
		</div>
		<div class="field">
			<label for="register-password">Password</label>
			<input
				id="register-password"
				name="password"
				type="password"
				autocomplete="new-password"
				bind:value={password}
				disabled={pending}
			/>
			<small class="hint">At least 8 characters.</small>
		</div>
		<div class="field">
			<label for="register-confirm">Confirm password</label>
			<input
				id="register-confirm"
				name="confirm"
				type="password"
				autocomplete="new-password"
				bind:value={passwordConfirm}
				disabled={pending}
			/>
		</div>
		{#if error}<p class="msg error" role="alert">{error}</p>{/if}
		<button class="btn primary block" type="submit" disabled={pending}>
			{pending ? 'Creating…' : 'Create account'}
		</button>
	</form>
	<p class="auth-alt">Already have an account? <a href="/login">Sign in</a></p>
</section>
