window.googleKitchenAuth = (() => {
  const scope = 'https://www.googleapis.com/auth/spreadsheets';
  const ready = () => new Promise((resolve, reject) => {
    const until = Date.now() + 10000;
    const check = () => window.google?.accounts?.oauth2 ? resolve() : Date.now() > until ? reject(new Error('Google Identity Services nie zostało załadowane.')) : setTimeout(check, 50);
    check();
  });
  return {
    async connect(clientId) {
      await ready();
      return new Promise((resolve, reject) => {
        const client = google.accounts.oauth2.initTokenClient({ client_id: clientId, scope, callback: response => response?.access_token ? resolve(response.access_token) : reject(new Error(response?.error || 'Nie uzyskano tokenu Google.')) });
        // Reuse the user's existing grant. Google still asks for consent when the scope has not been approved yet.
        client.requestAccessToken({ prompt: '' });
      });
    },
    revoke(token) { if (token && window.google?.accounts?.oauth2) google.accounts.oauth2.revoke(token, () => {}); }
  };
})();
