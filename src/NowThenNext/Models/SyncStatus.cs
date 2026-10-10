namespace NowThenNext.Models;

/// <summary>
/// High-level state of the Google Drive sync engine, surfaced to the UI.
/// </summary>
public enum SyncStatus
{
    /// <summary>No Drive session; nothing syncs until the user signs in.</summary>
    NotSignedIn,

    /// <summary>
    /// Previously connected, but the access token has expired. Google needs a user
    /// gesture to issue a new one, so sync waits until the user taps Reconnect.
    /// </summary>
    NeedsReconnect,

    /// <summary>Signed in and up to date-ish; a sync may be scheduled.</summary>
    Idle,

    /// <summary>A sync is currently running.</summary>
    Syncing,

    /// <summary>The most recent sync completed successfully.</summary>
    Synced,

    /// <summary>The most recent sync failed (see the error message).</summary>
    Error
}
