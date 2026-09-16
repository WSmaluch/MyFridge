const cacheName = 'moja-kuchnia-v1';
const own = ['.', 'index.html', 'app.css', 'app.js', 'js/storage.js', 'js/googleAuth.js'];
self.addEventListener('install', event => event.waitUntil(caches.open(cacheName).then(cache => cache.addAll(own)).then(() => self.skipWaiting())));
self.addEventListener('activate', event => event.waitUntil(self.clients.claim()));
self.addEventListener('fetch', event => {
  const request = event.request;
  if (new URL(request.url).origin !== self.location.origin) return;
  if (request.mode === 'navigate') { event.respondWith(fetch(request).catch(() => caches.match('index.html'))); return; }
  event.respondWith(caches.match(request).then(hit => hit || fetch(request).then(response => {
    if (!response.ok || request.method !== 'GET') return response;
    const copy = response.clone();
    caches.open(cacheName).then(cache => cache.put(request, copy));
    return response;
  })));
});
