// Google Identity Services (GIS) token flow for NowNextThen.
// Ported from DiceCalc's googleAuth.ts. Exposes window.googleAuth for Blazor interop.
//
// SETUP: CLIENT_ID is injected at deploy time from the GOOGLE_CLIENT_ID repo variable.
// For local dev, paste your client id below temporarily (see README) — never commit it.
// Authorized JavaScript origins must include the dev origin (http://localhost:5161)
// and the deployed origin (https://<user>.github.io). Scope is drive.appdata, so the
// app can only read/write its own hidden Drive folder — never the user's other files.
(function () {
    const CLIENT_ID = "101133685796-p43f4uqejgt9lgce4lvibrnoam8oegb1.apps.googleusercontent.com";
    const SCOPE = "https://www.googleapis.com/auth/drive.appdata";
    const PERSISTED_FLAG = "google-auth-connected";
    // The access token (1h lifetime) is kept in sessionStorage so a page reload doesn't
    // force a reconnect. GIS has no silent refresh without a user gesture, so once it
    // expires the user must reconnect (SyncStatus.NeedsReconnect).
    const TOKEN_KEY = "google-auth-token";
    const EXPIRY_MARGIN_MS = 60 * 1000;

    let tokenClient = null;
    let accessToken = null;
    let tokenExpiresAt = 0;
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
            s.onerror = () => {
                // Forget the failure so a later Sign in / Reconnect tap can try again.
                scriptPromise = null;
                s.remove();
                reject(new Error("Failed to load Google Identity Services"));
            };
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

    function setToken(token, expiresInSeconds) {
        accessToken = token;
        tokenExpiresAt = token ? Date.now() + (expiresInSeconds || 3600) * 1000 : 0;
        try {
            if (token) sessionStorage.setItem(TOKEN_KEY, JSON.stringify({ token, expiresAt: tokenExpiresAt }));
            else sessionStorage.removeItem(TOKEN_KEY);
        } catch { /* storage unavailable; in-memory only */ }
    }

    function validToken() {
        if (accessToken && Date.now() < tokenExpiresAt - EXPIRY_MARGIN_MS) return accessToken;
        if (accessToken) setToken(null);
        return null;
    }

    // Must be called from a user gesture: GIS opens a popup for every token request.
    // prompt: undefined = default (may show consent), "consent" = force consent
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
                    reject(new Error(resp.error));
                    return;
                }
                setToken(resp.access_token || null, Number(resp.expires_in));
                if (accessToken) localStorage.setItem(PERSISTED_FLAG, "true");
                resolve(accessToken);
            };
            tokenClient.error_callback = (err) => {
                pending = null;
                // Popup closed/blocked by the user — not an error worth surfacing.
                if (err && (err.type === "popup_closed" || err.type === "popup_failed_to_open")) resolve(null);
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
        // Restores a still-valid token from this browser session without any popup, and
        // preloads GIS so a later Reconnect click opens its popup within the user gesture.
        restoreSession: function () {
            ensureInitialized().catch(() => { /* offline or blocked; sign-in will retry */ });
            try {
                const saved = JSON.parse(sessionStorage.getItem(TOKEN_KEY) || "null");
                if (saved && saved.token) {
                    accessToken = saved.token;
                    tokenExpiresAt = saved.expiresAt || 0;
                }
            } catch { /* ignore corrupt entry */ }
            return validToken();
        },
        signOut: function () {
            if (accessToken && window.google && window.google.accounts) {
                window.google.accounts.oauth2.revoke(accessToken);
            }
            setToken(null);
            localStorage.removeItem(PERSISTED_FLAG);
        },
        getAccessToken: function () {
            return validToken();
        },
        isSignedIn: function () {
            return validToken() !== null;
        },
        hasPreviousSession: function () {
            return localStorage.getItem(PERSISTED_FLAG) === "true";
        },
        // Drop the in-memory token (e.g. on 401) without revoking — forces a fresh request.
        invalidateToken: function () {
            setToken(null);
        }
    };
})();
