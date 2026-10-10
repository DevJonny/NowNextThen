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
        let token = window.googleAuth.getAccessToken();
        if (!token) {
            token = await window.googleAuth.signIn();
            if (!token) throw driveError(401, "Not signed in");
        }
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

    async function findSyncFile() {
        const q = encodeURIComponent("name='" + FILE_NAME + "'");
        const url = API + "/files?spaces=appDataFolder&q=" + q + "&fields=files(id)";
        const resp = await authedFetch(url);
        const data = await resp.json();
        const first = data.files && data.files[0];
        if (!first) return null;
        // Drive doesn't support HEAD here; GET the metadata to read the ETag header.
        const meta = await authedFetch(API + "/files/" + first.id + "?spaces=appDataFolder&fields=id");
        return { id: first.id, etag: meta.headers.get("ETag") };
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
        findSyncFile: findSyncFile,
        downloadSyncFile: downloadSyncFile,
        uploadSyncFile: uploadSyncFile
    };
})();
