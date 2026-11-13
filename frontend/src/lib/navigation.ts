/** Only same-site paths, so a ?next= parameter can't send someone to another site. */
export function safeNext(next: string | null, fallback = '/dashboard') {
  return next && next.startsWith('/') && !next.startsWith('//') && !next.startsWith('/\\') ? next : fallback;
}
