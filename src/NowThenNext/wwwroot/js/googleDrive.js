// Google Drive appDataFolder sync file access for NowNextThen.
// Ported from DiceCalc's googleDrive.ts. Exposes window.driveSync for Blazor interop.
//
// The whole app data set is stored as one JSON file (nownextthen-sync.json) inside the
// app's hidden Drive folder. Expected HTTP failures are surfaced to C# as errors whose
// message is "DRIVE_ERROR:{status}:{detail}" so SyncService can branch on 401/412.
(function () {
    const FILE_NAME = "nownextthen-sync.json";
    const API = "https://www.googleapis.com/drive/v3";
    const UPLOAD = "https://www.googleapis.com/upload/drive/v3";

    function driveError(status, message) {
        return new Error("DRIVE_ERROR:" + status + ":" + message);
    }

    async function authedFetch(url, init) {
        init = init || {};
        // Never prompts: a sign-in popup needs a user gesture, so SyncService asks the user to reconnect.
        const token = window.googleAuth.getAccessToken();
        if (!token) throw driveError(401, "Not signed in");
        const headers = new Headers(init.headers || {});
        headers.set("Authorization", "Bearer " + token);
        const resp = await fetch(url, Object.assign({}, init, { headers }));
        if (resp.status === 401) {
            window.googleAuth.invalidateToken();
        }
        // 412 (etag mismatch) and 304 are handled by callers, not treated as fatal here.
        if (!resp.ok && resp.status !== 412 && resp.status !== 304) {
            throw driveError(resp.status, resp.status + " " + resp.statusText);
        }
        return resp;
    }

    // Returns every sync file, oldest first. Two devices syncing for the first time at once
    // can each create one; SyncService merges them all into the oldest (so every device picks
    // the same one) and deletes the rest. Only the oldest gets an ETag, as it's the one updated.
    async function findSyncFiles() {
        const q = encodeURIComponent("name='" + FILE_NAME + "' and trashed=false");
        const url = API + "/files?spaces=appDataFolder&q=" + q + "&orderBy=createdTime&fields=files(id)";
        const resp = await authedFetch(url);
        const data = await resp.json();
        const files = (data.files || []).map(f => ({ id: f.id, etag: null }));
        if (files.length === 0) return files;
        // Drive doesn't support HEAD here; GET the metadata to read the ETag header.
        const meta = await authedFetch(API + "/files/" + files[0].id + "?fields=id");
        files[0].etag = meta.headers.get("ETag");
        return files;
    }

    async function deleteSyncFile(fileId) {
        try {
            await authedFetch(API + "/files/" + fileId, { method: "DELETE" });
        } catch (e) {
            // Already deleted by another device — nothing to do.
            if (!String(e.message).startsWith("DRIVE_ERROR:404:")) throw e;
        }
    }

    async function downloadSyncFile(fileId) {
        const resp = await authedFetch(API + "/files/" + fileId + "?alt=media");
        return await resp.text();
    }

    async function uploadSyncFile(envelopeJson, existingId, existingEtag) {
        if (existingId) return await updateFile(existingId, existingEtag, envelopeJson);
        return await createFile(envelopeJson);
    }

    async function createFile(body) {
        const boundary = "b" + Math.random().toString(36).slice(2);
        const metadata = JSON.stringify({ name: FILE_NAME, parents: ["appDataFolder"] });
        const multipart =
            "--" + boundary + "\r\n" +
            "Content-Type: application/json; charset=UTF-8\r\n\r\n" +
            metadata + "\r\n" +
            "--" + boundary + "\r\n" +
            "Content-Type: application/json; charset=UTF-8\r\n\r\n" +
            body + "\r\n" +
            "--" + boundary + "--";

        const resp = await authedFetch(UPLOAD + "/files?uploadType=multipart&fields=id", {
            method: "POST",
            headers: { "Content-Type": "multipart/related; boundary=" + boundary },
            body: multipart
        });
        const data = await resp.json();
        return { id: data.id, etag: resp.headers.get("ETag") };
    }

    async function updateFile(id, etag, body) {
        const headers = { "Content-Type": "application/json" };
        if (etag) headers["If-Match"] = etag;
        const resp = await authedFetch(UPLOAD + "/files/" + id + "?uploadType=media&fields=id", {
            method: "PATCH",
            headers: headers,
            body: body
        });
        if (resp.status === 412) {
            throw driveError(412, "Precondition failed (etag mismatch)");
        }
        const data = await resp.json();
        return { id: data.id, etag: resp.headers.get("ETag") };
    }

    window.driveSync = {
        findSyncFiles: findSyncFiles,
        downloadSyncFile: downloadSyncFile,
        uploadSyncFile: uploadSyncFile,
        deleteSyncFile: deleteSyncFile
    };
})();
