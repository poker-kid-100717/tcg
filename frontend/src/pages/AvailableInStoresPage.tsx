import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';

import { useNearbyInventory, useSession } from '../api/hooks';
import { ErrorState, Loading } from '../components/States';
import { formatPrice } from '../lib/format';

type Coordinates = { latitude: number; longitude: number };

const RADIUS_OPTIONS = [5, 10, 25, 50, 100];

const age = (iso: string) => {
  const seconds = Math.max(0, Math.floor((Date.now() - new Date(iso).getTime()) / 1000));
  if (seconds < 60) return `${seconds}s ago`;
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes}m ago`;
  return `${Math.floor(minutes / 60)}h ago`;
};

export default function AvailableInStoresPage() {
  const session = useSession();
  const [coords, setCoords] = useState<Coordinates | null>(null);
  const [radius, setRadius] = useState(25);
  const [locationError, setLocationError] = useState('');
  const [locating, setLocating] = useState(false);
  const [retailer, setRetailer] = useState('All');
  const [category, setCategory] = useState('All');

  const inventory = useNearbyInventory(coords?.latitude ?? null, coords?.longitude ?? null, radius);

  const locate = () => {
    setLocationError('');
    if (!navigator.geolocation) {
      setLocationError('This browser does not support location services.');
      return;
    }

    setLocating(true);
    navigator.geolocation.getCurrentPosition(
      (position) => {
        setCoords({
          latitude: position.coords.latitude,
          longitude: position.coords.longitude,
        });
        setLocating(false);
      },
      (error) => {
        setLocating(false);
        setLocationError(
          error.code === error.PERMISSION_DENIED
            ? 'Location permission was denied. Enable location access for TCG Signal and try again.'
            : 'Your location could not be determined. Try again when location services are available.',
        );
      },
      { enableHighAccuracy: true, maximumAge: 60_000, timeout: 12_000 },
    );
  };

  const retailers = useMemo(
    () => ['All', ...new Set((inventory.data?.listings ?? []).map((item) => item.retailer))],
    [inventory.data],
  );
  const categories = useMemo(
    () => ['All', ...new Set((inventory.data?.listings ?? []).map((item) => item.category))],
    [inventory.data],
  );
  const listings = useMemo(
    () =>
      (inventory.data?.listings ?? []).filter(
        (item) =>
          (retailer === 'All' || item.retailer === retailer) &&
          (category === 'All' || item.category === category),
      ),
    [inventory.data, retailer, category],
  );

  if (session.isPending) return <Loading label="Loading your account…" />;
  if (session.error) return <ErrorState error={session.error} onRetry={() => session.refetch()} />;

  if (!session.data.hasStoreFinder) {
    return (
      <div className="container-custom py-10">
        <section className="panel mx-auto grid max-w-2xl gap-4 p-7 text-center">
          <p className="eyebrow">Available in Stores</p>
          <h1 className="text-3xl">Local Pokémon inventory is a Store Finder feature.</h1>
          <p className="text-slate-600">
            Store Finder is a separate $4.99/month service that checks supported retailers near you and hides inventory
            we cannot verify.
          </p>
          <Link to="/pro" className="btn mx-auto bg-pokemon-pokeblue text-white">
            View Store Finder
          </Link>
        </section>
      </div>
    );
  }

  return (
    <div className="container-custom grid gap-7 py-8 sm:py-10">
      <header className="grid gap-3">
        <div className="flex flex-wrap items-center gap-2 text-sm">
          <Link to="/dashboard" className="font-semibold text-pokemon-pokeblue hover:underline">Account overview</Link>
          <span className="text-slate-300">/</span>
          <span className="font-semibold text-slate-700">Available in Stores</span>
        </div>
        <div className="grid gap-2">
          <p className="eyebrow">Store Finder</p>
          <h1 className="text-3xl sm:text-4xl">Available in Stores</h1>
          <p className="max-w-3xl text-slate-600">
            Nearby sealed Pokémon TCG inventory from retailer-specific sources. TCG Signal does not display a store as
            stocked unless the source supplies store-level availability evidence.
          </p>
        </div>
      </header>

      <section className="panel grid gap-5 p-5">
        <div className="flex flex-wrap items-end gap-4">
          <div className="grid gap-1">
            <label htmlFor="radius" className="text-xs font-semibold uppercase tracking-wide text-slate-500">
              Search radius
            </label>
            <select
              id="radius"
              value={radius}
              onChange={(event) => setRadius(Number(event.target.value))}
              className="rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm"
            >
              {RADIUS_OPTIONS.map((miles) => (
                <option key={miles} value={miles}>{miles} miles</option>
              ))}
            </select>
          </div>
          <button
            type="button"
            onClick={locate}
            disabled={locating}
            className="btn bg-pokemon-pokeblue text-white"
          >
            {locating ? 'Locating…' : coords ? 'Refresh my location' : 'Use my location'}
          </button>
          {coords && (
            <p className="text-xs text-slate-500">
              Location is used for this search and is not stored in your TCG Signal profile.
            </p>
          )}
        </div>
        {locationError && <p className="rounded-lg bg-red-50 px-4 py-3 text-sm text-red-800">{locationError}</p>}
      </section>

      {!coords ? (
        <section className="panel grid gap-2 p-7 text-center">
          <h2 className="text-xl">Find Pokémon near you</h2>
          <p className="text-sm text-slate-600">
            Choose a radius, then allow location access. Only verified store-level observations will appear.
          </p>
        </section>
      ) : inventory.isPending ? (
        <Loading label="Checking nearby retailer inventory…" />
      ) : inventory.error ? (
        <ErrorState error={inventory.error} onRetry={() => inventory.refetch()} />
      ) : !inventory.data ? (
        <Loading label="Checking nearby retailer inventory…" />
      ) : (
        <>
          <section className="grid gap-4">
            <div className="flex flex-wrap items-end justify-between gap-3">
              <div>
                <h2 className="text-2xl">Reported in stock</h2>
                <p className="text-sm text-slate-500">
                  {inventory.data.listings.length} verified listing{inventory.data.listings.length === 1 ? '' : 's'} within {inventory.data.radiusMiles} miles.
                </p>
              </div>
              <div className="flex flex-wrap gap-2">
                <select
                  aria-label="Filter by retailer"
                  value={retailer}
                  onChange={(event) => setRetailer(event.target.value)}
                  className="rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm"
                >
                  {retailers.map((value) => <option key={value}>{value}</option>)}
                </select>
                <select
                  aria-label="Filter by product type"
                  value={category}
                  onChange={(event) => setCategory(event.target.value)}
                  className="rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm"
                >
                  {categories.map((value) => <option key={value}>{value}</option>)}
                </select>
              </div>
            </div>

            {listings.length === 0 ? (
              <div className="panel p-7 text-center">
                <p className="font-semibold text-slate-900">No verified inventory matched this search.</p>
                <p className="mt-1 text-sm text-slate-600">
                  That is intentionally different from guessing. Try a wider radius or check the retailer coverage below.
                </p>
              </div>
            ) : (
              <div className="grid gap-4 lg:grid-cols-2">
                {listings.map((item) => {
                  const directions = `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(
                    `${item.address}, ${item.city}, ${item.region} ${item.postalCode}`,
                  )}`;
                  return (
                    <article key={`${item.retailer}-${item.storeId}-${item.productSku}`} className="panel overflow-hidden">
                      <div className="grid grid-cols-[5.5rem_1fr] gap-4 p-5">
                        {item.imageUrl ? (
                          <img src={item.imageUrl} alt="" loading="lazy" className="h-28 w-20 rounded object-contain" />
                        ) : (
                          <div className="h-28 w-20 rounded bg-slate-100" aria-hidden="true" />
                        )}
                        <div className="min-w-0">
                          <div className="flex flex-wrap items-center gap-2">
                            <span className="rounded-full bg-emerald-50 px-2 py-1 text-[11px] font-bold uppercase tracking-wide text-emerald-800 ring-1 ring-emerald-200">
                              {item.availability}
                            </span>
                            {item.lowStock && (
                              <span className="rounded-full bg-amber-50 px-2 py-1 text-[11px] font-bold uppercase tracking-wide text-amber-800 ring-1 ring-amber-200">
                                Low stock
                              </span>
                            )}
                            <span className="text-xs font-semibold text-slate-500">{item.confidenceScore}% confidence</span>
                          </div>
                          <h3 className="mt-2 text-lg leading-tight">{item.productName}</h3>
                          <p className="mt-1 text-sm font-semibold text-slate-700">{item.retailer} · {item.storeName}</p>
                          <p className="text-sm text-slate-500">{item.distanceMiles.toFixed(1)} miles · {item.city}, {item.region}</p>
                          <div className="mt-2 flex flex-wrap items-baseline gap-3">
                            <span className="price text-lg">{formatPrice(item.price)}</span>
                            <span className="text-xs text-slate-500">verified {age(item.observedAt)}</span>
                          </div>
                        </div>
                      </div>
                      <div className="border-t border-slate-100 bg-slate-50 px-5 py-3">
                        <p className="text-xs leading-5 text-slate-600">{item.evidence}</p>
                        <p className="text-xs text-slate-400">Source: {item.source}</p>
                        <div className="mt-2 flex flex-wrap gap-3 text-sm font-semibold">
                          <a href={directions} target="_blank" rel="noreferrer" className="text-pokemon-pokeblue hover:underline">Directions</a>
                          {item.productUrl && (
                            <a href={item.productUrl} target="_blank" rel="noreferrer" className="text-pokemon-pokeblue hover:underline">
                              View retailer product
                            </a>
                          )}
                        </div>
                      </div>
                    </article>
                  );
                })}
              </div>
            )}
          </section>

          <section className="panel overflow-hidden">
            <div className="border-b border-slate-200 px-5 py-4">
              <h2 className="text-lg">Retailer coverage</h2>
              <p className="text-xs text-slate-500">A retailer is not silently treated as monitored when its data source is unavailable.</p>
            </div>
            <div className="divide-y divide-slate-100">
              {inventory.data.providers.map((provider) => (
                <div key={provider.retailer} className="grid gap-1 px-5 py-3 sm:grid-cols-[9rem_9rem_1fr] sm:items-start sm:gap-3">
                  <span className="font-semibold text-slate-900">{provider.retailer}</span>
                  <span className="text-sm text-slate-600">{provider.status}</span>
                  <span className="text-sm text-slate-500">{provider.detail}</span>
                </div>
              ))}
            </div>
          </section>

          <p className="text-xs leading-5 text-slate-500">{inventory.data.accuracyPolicy}</p>
        </>
      )}
    </div>
  );
}
