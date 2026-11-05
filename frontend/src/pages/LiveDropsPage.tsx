import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { useNearbyInventory, useSession } from '../api/hooks';
import { ErrorState, Loading } from '../components/States';

const RADII = [5, 10, 25, 50, 100];
const CHAINS = new Set(['target','walmart','best buy','gamestop','barnes & noble','walgreens','cvs','five below','dollar general','family dollar']);
const isChain = (name: string) => CHAINS.has(name.trim().toLowerCase());
const matchesProduct = (name: string, category: string, selected: string) =>
  selected === 'all' || /elite trainer box|\betb\b/i.test(name + ' ' + category);

export default function LiveDropsPage() {
  const session = useSession();
  const [radius, setRadius] = useState(25);
  const [product, setProduct] = useState('etb');
  const [coords, setCoords] = useState<{ latitude: number; longitude: number } | null>(null);
  const [locationError, setLocationError] = useState('');
  const [locating, setLocating] = useState(false);
  const inventory = useNearbyInventory(coords?.latitude ?? null, coords?.longitude ?? null, radius);
  const listings = useMemo(() => (inventory.data?.listings ?? [])
    .filter(item => matchesProduct(item.productName, item.category, product))
    .sort((a, b) => a.distanceMiles - b.distanceMiles), [inventory.data, product]);

  function locate() {
    setLocationError('');
    if (!navigator.geolocation) { setLocationError('Location is unavailable in this browser.'); return; }
    setLocating(true);
    navigator.geolocation.getCurrentPosition(
      position => { setCoords({ latitude: position.coords.latitude, longitude: position.coords.longitude }); setLocating(false); },
      () => { setLocationError('Location permission denied or unavailable.'); setLocating(false); },
      { maximumAge: 60000, timeout: 12000 },
    );
  }

  function storeSection(title: string, items: typeof listings, description: string) {
    return <section className="panel grid gap-4 p-5" aria-label={title}>
      <div><h2 className="text-2xl">{title} ({items.length})</h2><p className="text-sm text-slate-600">{description}</p></div>
      {items.length === 0 ? <p className="rounded-lg bg-slate-50 p-4 text-sm text-slate-600">No verified listings from this category. This does not mean the stores are sold out or do not carry the product.</p> :
        <div className="grid gap-3 lg:grid-cols-2">{items.map(item => (
          <article key={item.retailer + item.storeId + item.productSku} className="rounded-lg border border-slate-200 p-4">
            <p className="text-xs font-bold uppercase text-emerald-700">{item.availability} · {item.confidenceScore}% confidence</p>
            <h3 className="mt-1 font-bold">{item.productName}</h3>
            <p className="text-sm">{item.retailer} · {item.storeName}</p>
            <p className="text-sm text-slate-600">{item.distanceMiles.toFixed(1)} miles · {item.address}, {item.city}, {item.region}</p>
            <p className="mt-2 text-xs text-slate-500">Observed {new Date(item.observedAt).toLocaleString()} · {item.source}</p>
            <p className="mt-1 text-xs text-slate-600">{item.evidence}</p>
            <a className="mt-2 inline-block text-sm font-semibold text-pokemon-pokeblue underline" target="_blank" rel="noreferrer"
              href={'https://www.google.com/maps/search/?api=1&query=' + encodeURIComponent(item.address + ', ' + item.city + ', ' + item.region + ' ' + item.postalCode)}>Directions</a>
          </article>
        ))}</div>}
    </section>;
  }

  if (session.isPending) return <Loading label="Loading account…" />;
  if (session.error) return <ErrorState error={session.error} onRetry={() => session.refetch()} />;

  return <div className="container-custom grid gap-6 py-8">
    <header><p className="eyebrow">Live Drops</p><h1 className="text-3xl sm:text-4xl">Find Pokémon drops near you</h1>
      <p className="mt-2 text-slate-600">Only supported store-level observations are shown. Online availability is not proof of physical shelf inventory.</p></header>
    <section className="panel flex flex-wrap items-end gap-4 p-5">
      <label className="grid gap-1 text-sm font-semibold">Product
        <select aria-label="Product" value={product} onChange={e => setProduct(e.target.value)} className="rounded-lg border p-2">
          <option value="etb">Elite Trainer Boxes (ETB)</option><option value="all">All sealed products</option>
        </select></label>
      <label className="grid gap-1 text-sm font-semibold">Search radius
        <select aria-label="Search radius" value={radius} onChange={e => setRadius(Number(e.target.value))} className="rounded-lg border p-2">
          {RADII.map(r => <option key={r} value={r}>{r} miles</option>)}
        </select></label>
      <button type="button" className="btn bg-pokemon-pokeblue text-white" onClick={locate} disabled={locating}>{locating ? 'Locating…' : 'Use my location'}</button>
      {locationError && <p role="alert" className="w-full text-sm text-red-700">{locationError}</p>}
    </section>
    {!session.data?.hasStoreFinder ? <section className="panel p-5"><p>Store Finder access is required for inventory observations.</p><Link className="text-pokemon-pokeblue underline" to="/pro">View plans</Link></section> :
      !coords ? <section className="panel p-5">Choose a radius and share your location to search. ZIP-code lookup and independent shop discovery will require additional data sources.</section> :
      inventory.isPending ? <Loading label="Checking nearby stores…" /> :
      inventory.error ? <ErrorState error={inventory.error} onRetry={() => inventory.refetch()} /> :
      <>{storeSection('Major Retailers', listings.filter(item => isChain(item.retailer)), 'Verified inventory observations from supported major retailers.')}
        {storeSection('Local Card Shops', listings.filter(item => !isChain(item.retailer)), 'Only independent shops already present in the inventory feed can appear. Separate local shop discovery is not yet integrated.')}</>}
  </div>;
}
