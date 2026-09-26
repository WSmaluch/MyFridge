const kitchenSystemTheme = window.matchMedia('(prefers-color-scheme: dark)');
function applyKitchenTheme() {
  const preference = localStorage.getItem('kitchen-theme') || 'system';
  const resolved = preference === 'system' ? (kitchenSystemTheme.matches ? 'dark' : 'light') : preference;
  document.documentElement.dataset.theme = resolved;
  return resolved;
}
window.kitchenTheme = {
  set(theme) { localStorage.setItem('kitchen-theme', theme); return applyKitchenTheme(); },
  load: applyKitchenTheme,
  toggle() { return window.kitchenTheme.set(document.documentElement.dataset.theme === 'dark' ? 'light' : 'dark'); }
};
window.kitchenTheme.load();
kitchenSystemTheme.addEventListener('change', () => {
  if ((localStorage.getItem('kitchen-theme') || 'system') === 'system') applyKitchenTheme();
});
(() => {
  const redirect = sessionStorage.getItem('kitchen-redirect');
  if (redirect) {
    sessionStorage.removeItem('kitchen-redirect');
    const base = document.baseURI;
    history.replaceState(null, '', new URL(redirect, base).href);
  }
  if ('serviceWorker' in navigator) window.addEventListener('load', () => navigator.serviceWorker.register('service-worker.js').catch(() => {}));
})();
