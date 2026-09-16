# Moja Kuchnia

Moja Kuchnia jest w pełni statyczną aplikacją Blazor WebAssembly (.NET 10). Po `dotnet publish` wynik to wyłącznie pliki `wwwroot`, które można hostować na GitHub Pages lub zwykłym serwerze statycznym. Nie ma backendu, bazy danych, konta serwisowego, proxy, kontenerów ani sekretów wdrożeniowych.

## Uruchomienie lokalne

```bash
dotnet restore
dotnet run --project src/Kitchen.Web
```

Przy pierwszym otwarciu aplikacja pokazuje konfigurację. Tryb **Demo** działa bez sieci i zawiera gotowe dane oraz mock AI. Dane demonstracyjne istnieją tylko w pamięci przeglądarki.

## Google Sheets i Gemini

W trybie Google wszystkie wywołania są wykonywane bezpośrednio z przeglądarki:

- Google Identity Services wydaje krótkotrwały token OAuth dla zakresu `https://www.googleapis.com/auth/spreadsheets`; token pozostaje wyłącznie w pamięci aktywnej karty;
- REST Google Sheets tworzy wymagane karty i czyta/zapisuje dane w wybranym arkuszu;
- Gemini REST jest wywoływane z nagłówkiem `x-goog-api-key` i wymusza odpowiedzi JSON Schema.

W ekranie **Więcej** wpisz `Spreadsheet ID`, OAuth `Client ID`, opcjonalny Gemini API Key oraz model. Te wartości są zapisywane tylko w `localStorage` tej przeglądarki. Klucz Gemini po stronie klienta nie jest sekretem — każdy, kto ma dostęp do profilu przeglądarki, może go odczytać. Używaj osobnego, ograniczonego klucza i limitów/ograniczeń klucza w Google AI Studio. Google musi też pozwalać na CORS dla używanego API; bezpośrednie wywołanie może zostać zablokowane przez politykę dostawcy lub przeglądarki, co aplikacja zgłasza jako czytelny błąd.

### Konfiguracja OAuth Google

1. W Google Cloud włącz Google Sheets API.
2. Utwórz OAuth Client typu **Web application**.
3. Dodaj do *Authorized JavaScript origins* lokalny adres, np. `http://127.0.0.1:5000`, i adres GitHub Pages, np. `https://<uzytkownik>.github.io`.
4. Utwórz arkusz, skopiuj jego ID (fragment między `/d/` a `/edit`) i udostępnij go zalogowanemu użytkownikowi.
5. Po połączeniu wybierz „Utwórz układ kart”; aplikacja doda `Produkty`, `Historia`, `Paragony`, `Przepisy`, `Zakupy`, `Mapowania` i `App` bez usuwania istniejących danych.

## GitHub Pages

Workflow [deploy-pages.yml](.github/workflows/deploy-pages.yml) buduje na `main`, publikuje `wwwroot`, ustawia automatycznie `<base href="/nazwa-repo/">` (dla repozytorium projektu), obsługuje user pages i dołącza `404.html` dla odświeżonych deep-linków. W ustawieniach repozytorium wybierz **Pages → GitHub Actions**.

## Weryfikacja

```bash
dotnet build Kitchen.slnx
dotnet test Kitchen.slnx
dotnet publish src/Kitchen.Web -c Release
```
