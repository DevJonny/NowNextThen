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
        if (!resp.ok) {
            throw driveError(resp.status, resp.status + " " + resp.statusText);
        }
        return resp;
    }

    // Returns every sync file, oldest first, with its Drive `version`. Two devices syncing for
    // the first time at once can each create one; SyncService merges them all into the oldest
    // (so every device picks the same one) and deletes the rest.
    async function findSyncFiles() {
        const q = encodeURIComponent("name='" + FILE_NAME + "' and trashed=false");
        const url = API + "/files?spaces=appDataFolder&q=" + q + "&orderBy=createdTime&fields=files(id,version)";
        const resp = await authedFetch(url);
        const data = await resp.json();
        return (data.files || []).map(f => ({ id: f.id, version: f.version || null }));
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

    // Drive v3 has no compare-and-swap on update, so check the file's `version` (which
    // increments on every change) just before writing. A mismatch means another device
    // wrote since we downloaded; report 412 so SyncService re-downloads and merges again.
    // This narrows the race to one round trip rather than closing it; anything lost in
    // that window is still on the other device and re-uploads on its next sync.
    async function uploadSyncFile(envelopeJson, existingId, expectedVersion) {
        if (!existingId) return await uploadResumable(UPLOAD + "/files?uploadType=resumable&fields=id,version",
            "POST", { name: FILE_NAME, parents: ["appDataFolder"] }, envelopeJson);

        if (expectedVersion) {
            const meta = await authedFetch(API + "/files/" + existingId + "?fields=version");
            const current = (await meta.json()).version;
            if (current !== expectedVersion) {
                throw driveError(412, "Sync file changed on another device");
            }
        }
        return await uploadResumable(UPLOAD + "/files/" + existingId + "?uploadType=resumable&fields=id,version",
            "PATCH", {}, envelopeJson);
    }

    // Resumable upload: simple/multipart uploads are only documented up to 5 MB, and the
    // envelope carries every image.
    async function uploadResumable(sessionUrl, method, metadata, body) {
        const session = await authedFetch(sessionUrl, {
            method: method,
            headers: {
                "Content-Type": "application/json; charset=UTF-8",
                "X-Upload-Content-Type": "application/json"
            },
            body: JSON.stringify(metadata)
        });
        const location = session.headers.get("Location");
        if (!location) throw driveError(500, "Drive did not return an upload session");

        const resp = await authedFetch(location, {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: body
        });
        const data = await resp.json();
        return { id: data.id, version: data.version || null };
    }

    window.driveSync = {
        findSyncFiles: findSyncFiles,
        downloadSyncFile: downloadSyncFile,
        uploadSyncFile: uploadSyncFile,
        deleteSyncFile: deleteSyncFile
    };
})();
