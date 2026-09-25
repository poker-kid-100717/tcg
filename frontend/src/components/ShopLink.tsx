/**
 * Links a card to its TCGplayer listing. The URL comes from the Pokémon TCG
 * API (prices.pokemontcg.io/tcgplayer/…), which redirects to the product page.
 */
export function ShopLink({
  url,
  cardName,
  size = 'md',
}: {
  url: string | null;
  cardName: string;
  size?: 'sm' | 'md';
}) {
  if (!url) return null;
  return (
    <a
      href={url}
      target="_blank"
      rel="noopener noreferrer"
      className={size === 'sm' ? 'btn-shop px-2.5 py-1 text-xs' : 'btn-shop px-5 py-3 text-base'}
      aria-label={`Shop ${cardName} on TCGplayer (opens in a new tab)`}
    >
      Shop now on TCGplayer
      <span aria-hidden="true">↗</span>
    </a>
  );
}
