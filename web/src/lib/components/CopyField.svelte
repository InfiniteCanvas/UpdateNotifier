<script lang="ts">
	import { copyText } from '#lib/clipboard.ts';

	let { value, label }: { value: string; label: string } = $props();

	let note = $state('');
	let timer: ReturnType<typeof setTimeout> | undefined;

	async function copy(): Promise<void> {
		const ok = await copyText(value);
		note = ok ? 'Copied!' : 'Copy failed — select the text and copy it manually.';
		clearTimeout(timer);
		timer = setTimeout(() => (note = ''), 2500);
	}
</script>

<div class="input-row">
	<input readonly aria-label={label} value={value} />
	<button class="btn" onclick={() => void copy()}>Copy</button>
</div>
{#if note}<p class="copied-note" role="status">{note}</p>{/if}
