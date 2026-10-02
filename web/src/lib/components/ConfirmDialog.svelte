<script lang="ts">
	import { tick } from 'svelte';

	interface ConfirmOptions {
		title: string;
		body?: string;
		confirmLabel?: string;
		danger?: boolean;
	}

	let open = $state(false);
	let current = $state<ConfirmOptions | null>(null);
	let resolver: ((value: boolean) => void) | undefined;
	let returnFocus: HTMLElement | null = null;
	let cancelBtn = $state<HTMLButtonElement>();
	let confirmBtn = $state<HTMLButtonElement>();

	/** Open the dialog; resolves true on confirm, false on cancel / Escape / backdrop click. */
	export function confirm(opts: ConfirmOptions): Promise<boolean> {
		if (open) resolver?.(false); // a second call while open must not orphan the first promise
		current = opts;
		returnFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
		const promise = new Promise<boolean>((resolve) => {
			resolver = resolve;
		});
		open = true;
		void tick().then(() => confirmBtn?.focus());
		return promise;
	}

	function settle(result: boolean): void {
		if (!open) return;
		open = false;
		resolver?.(result);
		resolver = undefined;
		current = null;
		returnFocus?.focus();
		returnFocus = null;
	}

	function onBackdropClick(event: MouseEvent): void {
		// only a click on the backdrop itself dismisses — not one bubbled from the dialog
		if (event.target === event.currentTarget) settle(false);
	}

	function onKeydown(event: KeyboardEvent): void {
		if (event.key === 'Escape') {
			event.preventDefault();
			settle(false);
		} else if (event.key === 'Tab') {
			event.preventDefault();
			// simple trap: cycle between just the two buttons
			if (document.activeElement === confirmBtn) cancelBtn?.focus();
			else confirmBtn?.focus();
		}
	}
</script>

{#if open && current}
	<!-- svelte-ignore a11y_no_static_element_interactions -->
	<div class="dialog-backdrop" onclick={onBackdropClick} onkeydown={onKeydown}>
		<div class="dialog" role="dialog" aria-modal="true" aria-labelledby="dialog-title" tabindex="-1">
			<h2 id="dialog-title">{current.title}</h2>
			{#if current.body}<p class="dialog-body">{current.body}</p>{/if}
			<div class="dialog-actions">
				<button class="btn" bind:this={cancelBtn} onclick={() => settle(false)}>Cancel</button>
				<button
					class="btn {current.danger ? 'danger' : 'primary'}"
					bind:this={confirmBtn}
					onclick={() => settle(true)}
				>
					{current.confirmLabel ?? 'Confirm'}
				</button>
			</div>
		</div>
	</div>
{/if}
