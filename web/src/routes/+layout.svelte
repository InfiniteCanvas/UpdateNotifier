<script lang="ts">
	import '@fontsource-variable/bricolage-grotesque';
	import '@fontsource-variable/public-sans';
	import '@fontsource-variable/spline-sans-mono';
	import favicon from '#lib/assets/favicon.svg';
	import '#lib/styles.css';
	import type { Snippet } from 'svelte';
	import { goto } from '$app/navigation';
	import { session } from '#lib/session.svelte.ts';

	let { children }: { children: Snippet } = $props();

	let loggingOut = $state(false);

	async function handleLogout(): Promise<void> {
		if (loggingOut) return;
		loggingOut = true;
		try {
			await session.logout();
			await goto('/login');
		} finally {
			loggingOut = false;
		}
	}
</script>

<svelte:head>
	<link rel="icon" href={favicon} />
</svelte:head>

<div class="app-shell">
	<header class="site-header">
		<div class="container header-inner">
			<a class="brand" href="/">
				<svg class="brand-mark" width="20" height="20" viewBox="0 0 32 32" aria-hidden="true">
					<rect width="32" height="32" rx="7" fill="#141925" />
					<circle cx="16" cy="16" r="6.5" fill="none" stroke="#ffad5c" stroke-width="2" opacity="0.5" />
					<circle cx="16" cy="16" r="3" fill="#ffad5c" />
				</svg>
				UpdateNotifier
			</a>
			{#if session.me}
				<div class="header-user">
					<span class="header-username">{session.me.username}</span>
					<a class="btn ghost" href="/settings">Settings</a>
					<button class="btn ghost" onclick={handleLogout} disabled={loggingOut}>
						{loggingOut ? 'Logging out…' : 'Logout'}
					</button>
				</div>
			{/if}
		</div>
	</header>

	<main class="container main-content">
		{@render children()}
	</main>
</div>
