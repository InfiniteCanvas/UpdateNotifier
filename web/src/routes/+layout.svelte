<script lang="ts">
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
			<a class="brand" href="/">UpdateNotifier</a>
			{#if session.me}
				<div class="header-user">
					<span class="header-username">{session.me.username}</span>
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
