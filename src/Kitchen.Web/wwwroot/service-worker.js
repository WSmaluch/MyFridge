const cacheName = 'moja-kuchnia-v2';
const own = ['.', 'index.html', 'app.css', 'app.js', 'js/storage.js', 'js/googleAuth.js'];
self.addEventListener('install', event => event.waitUntil(caches.open(cacheName).then(cache => cache.addAll(own)).then(() => self.skipWaiting())));
self.addEventListener('activate', event => event.waitUntil((async () => {
  const names = await caches.keys();
  await Promise.all(names.filter(name => name.startsWith('moja-kuchnia-') && name !== cacheName).map(name => caches.delete(name)));
  await self.clients.claim();
})()));
self.addEventListener('fetch', event => {
  const request = event.request;
  if (request.method !== 'GET' || new URL(request.url).origin !== self.location.origin) return;
  event.respondWith((async () => {
    try {
      const response = await fetch(request, { cache: 'no-cache' });
      if (response.ok) {
        const cache = await caches.open(cacheName);
        await cache.put(request, response.clone());
      }
      return response;
    } catch {
      return await caches.match(request) ?? (request.mode === 'navigate' ? await caches.match('index.html') : Response.error());
    }
  })());
});
