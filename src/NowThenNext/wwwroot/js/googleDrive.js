// Google Drive appDataFolder file access for NowNextThen. Exposes window.driveSync for
// Blazor interop; SyncService decides what the files mean.
//
// Layout: a small manifest (nownextthen-manifest.json) holding every record with image
// payloads replaced by content hashes, plus one file per image (nownextthen-blob-<sha256>).
// Older app versions wrote everything to nownextthen-sync.json, which SyncService migrates.
//
// Expected HTTP failures are surfaced to C# as errors whose message is
// "DRIVE_ERROR:{status}:{detail}" so SyncService can branch on 401/404/412.
(function () {
    const API = "https://www.googleapis.com/drive/v3";
    const UPLOAD = "https://www.googleapis.com/upload/drive/v3";

    // Multipart uploads are documented up to 5 MB; anything bigger uses a resumable session.
    const MULTIPART_LIMIT = 4 * 1024 * 1024;

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

    // Every file in the app folder, oldest first: [{ id, name, version, createdTime }].
    async function listFiles() {
        const files = [];
        let pageToken = null;
        do {
            let url = API + "/files?spaces=appDataFolder&q=" + encodeURIComponent("trashed=false")
                + "&orderBy=createdTime&pageSize=1000"
                + "&fields=nextPageToken,files(id,name,version,createdTime)";
            if (pageToken) url += "&pageToken=" + encodeURIComponent(pageToken);
            const data = await (await authedFetch(url)).json();
            for (const f of data.files || []) {
                files.push({ id: f.id, name: f.name, version: f.version || null, createdTime: f.createdTime });
            }
            pageToken = data.nextPageToken || null;
        } while (pageToken);
        return files;
    }

    async function downloadFile(fileId) {
        const resp = await authedFetch(API + "/files/" + fileId + "?alt=media");
        return await resp.text();
    }

    async function deleteFile(fileId) {
        try {
            await authedFetch(API + "/files/" + fileId, { method: "DELETE" });
        } catch (e) {
            // Already deleted by another device — nothing to do.
            if (!String(e.message).startsWith("DRIVE_ERROR:404:")) throw e;
        }
    }

    // Creates the file, or overwrites existingId. Drive v3 has no compare-and-swap on update,
    // so when expectedVersion is given, check the file's `version` (which increments on every
    // change) just before writing; a mismatch means another device wrote since we downloaded,
    // reported as 412 so SyncService re-downloads and merges again. This narrows the race to
    // one round trip rather than closing it.
    async function uploadFile(name, content, contentType, existingId, expectedVersion) {
        if (existingId && expectedVersion) {
            const meta = await authedFetch(API + "/files/" + existingId + "?fields=version");
            const current = (await meta.json()).version;
            if (current !== expectedVersion) {
                throw driveError(412, "File changed on another device");
            }
        }

        const metadata = existingId ? {} : { name: name, parents: ["appDataFolder"] };
        const path = existingId ? "/files/" + existingId : "/files";
        const method = existingId ? "PATCH" : "POST";

        return content.length < MULTIPART_LIMIT
            ? await uploadMultipart(UPLOAD + path + "?uploadType=multipart&fields=id,version", method, metadata, content, contentType)
            : await uploadResumable(UPLOAD + path + "?uploadType=resumable&fields=id,version", method, metadata, content, contentType);
    }

    async function uploadMultipart(url, method, metadata, content, contentType) {
        const boundary = "nownextthen-" + crypto.randomUUID();
        const body = "--" + boundary + "\r\n"
            + "Content-Type: application/json; charset=UTF-8\r\n\r\n"
            + JSON.stringify(metadata) + "\r\n"
            + "--" + boundary + "\r\n"
            + "Content-Type: " + contentType + "\r\n\r\n"
            + content + "\r\n"
            + "--" + boundary + "--";
        const resp = await authedFetch(url, {
            method: method,
            headers: { "Content-Type": "multipart/related; boundary=" + boundary },
            body: body
        });
        const data = await resp.json();
        return { id: data.id, version: data.version || null };
    }

    async function uploadResumable(url, method, metadata, content, contentType) {
        const session = await authedFetch(url, {
            method: method,
            headers: {
                "Content-Type": "application/json; charset=UTF-8",
                "X-Upload-Content-Type": contentType
            },
            body: JSON.stringify(metadata)
        });
        const location = session.headers.get("Location");
        if (!location) throw driveError(500, "Drive did not return an upload session");

        const resp = await authedFetch(location, {
            method: "PUT",
            headers: { "Content-Type": contentType },
            body: content
        });
        const data = await resp.json();
        return { id: data.id, version: data.version || null };
    }

    window.driveSync = {
        listFiles: listFiles,
        downloadFile: downloadFile,
        uploadFile: uploadFile,
        deleteFile: deleteFile
    };
})();
