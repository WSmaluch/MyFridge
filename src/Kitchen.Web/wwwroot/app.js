window.kitchenTheme={set(theme){document.documentElement.dataset.theme=theme;localStorage.setItem('kitchen-theme',theme)},load(){document.documentElement.dataset.theme=localStorage.getItem('kitchen-theme')||'system'}};
window.kitchenTheme.load();
(() => {
  const redirect = sessionStorage.getItem('kitchen-redirect');
  if (redirect) {
    sessionStorage.removeItem('kitchen-redirect');
    const base = document.baseURI;
    history.replaceState(null, '', new URL(redirect, base).href);
  }
  if ('serviceWorker' in navigator) window.addEventListener('load', () => navigator.serviceWorker.register('service-worker.js').catch(() => {}));
})();
