// Google Identity Services (GIS) token flow for NowNextThen.
// Ported from DiceCalc's googleAuth.ts. Exposes window.googleAuth for Blazor interop.
//
// SETUP: replace CLIENT_ID below with your own OAuth 2.0 Web client id (see README).
// Authorized JavaScript origins must include the dev origin (http://localhost:5161)
// and the deployed origin (https://<user>.github.io). Scope is drive.appdata, so the
// app can only read/write its own hidden Drive folder — never the user's other files.
(function () {
    const CLIENT_ID = "REPLACE_WITH_YOUR_CLIENT_ID.apps.googleusercontent.com";
    const SCOPE = "https://www.googleapis.com/auth/drive.appdata";
    const PERSISTED_FLAG = "google-auth-connected";

    let tokenClient = null;
    let accessToken = null;
    let pending = null;
    let scriptPromise = null;
    let initialized = false;

    function loadGisScript() {
        if (window.google && window.google.accounts) return Promise.resolve();
        if (scriptPromise) return scriptPromise;
        scriptPromise = new Promise((resolve, reject) => {
            const s = document.createElement("script");
            s.src = "https://accounts.google.com/gsi/client";
            s.async = true;
            s.defer = true;
            s.onload = () => resolve();
            s.onerror = () => reject(new Error("Failed to load Google Identity Services"));
            document.head.appendChild(s);
        });
        return scriptPromise;
    }

    async function ensureInitialized() {
        if (initialized) return;
        await loadGisScript();
        if (!(window.google && window.google.accounts)) throw new Error("GIS not available");
        tokenClient = window.google.accounts.oauth2.initTokenClient({
            client_id: CLIENT_ID,
            scope: SCOPE,
            callback: () => { /* overridden per-request */ }
        });
        initialized = true;
    }

    // prompt: undefined = default (may show consent), "" = silent, "consent" = force consent
    function requestToken(prompt) {
        if (pending) return pending;
        pending = new Promise((resolve, reject) => {
            if (!tokenClient) {
                reject(new Error("Google Auth not initialized"));
                return;
            }
            tokenClient.callback = (resp) => {
                pending = null;
                if (resp.error) {
                    if (prompt === "") resolve(null);
                    else reject(new Error(resp.error));
                    return;
                }
                accessToken = resp.access_token || null;
                if (accessToken) localStorage.setItem(PERSISTED_FLAG, "true");
                resolve(accessToken);
            };
            tokenClient.error_callback = (err) => {
                pending = null;
                if (prompt === "") resolve(null);
                else reject(new Error((err && err.message) || "Sign-in failed"));
            };
            tokenClient.requestAccessToken(prompt !== undefined ? { prompt } : undefined);
        });
        return pending;
    }

    window.googleAuth = {
        signIn: async function () {
            await ensureInitialized();
            return requestToken(undefined);
        },
        trySilentSignIn: async function () {
            await ensureInitialized();
            return requestToken("");
        },
        signOut: function () {
            if (accessToken && window.google && window.google.accounts) {
                window.google.accounts.oauth2.revoke(accessToken);
            }
            accessToken = null;
            localStorage.removeItem(PERSISTED_FLAG);
        },
        getAccessToken: function () {
            return accessToken;
        },
        isSignedIn: function () {
            return accessToken !== null;
        },
        hasPreviousSession: function () {
            return localStorage.getItem(PERSISTED_FLAG) === "true";
        },
        // Drop the in-memory token (e.g. on 401) without revoking — forces a fresh request.
        invalidateToken: function () {
            accessToken = null;
        }
    };
})();
