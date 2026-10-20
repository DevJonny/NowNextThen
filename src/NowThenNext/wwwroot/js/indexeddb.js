// IndexedDB storage module for NowThenNext
// Replaces localStorage with IndexedDB for larger storage capacity
(function () {
    const DB_NAME = 'NowThenNextDB';
    const DB_VERSION = 2;
    const STORES = ['images', 'phonics-progress', 'learning-cards', 'feelings-log'];
    const DATA_KEY = 'data';

    // Map from old localStorage keys to IndexedDB store names
    const MIGRATION_MAP = {
        'nowthenext_images': 'images',
        'phonics-progress': 'phonics-progress',
        'learning-cards': 'learning-cards'
    };

    let db = null;
    let dbPromise = null;

    async function ensureDb() {
        if (db) return db;
        if (!dbPromise) {
            dbPromise = openDatabase().then(function (database) {
                db = database;
                return db;
            });
        }
        return dbPromise;
    }

    function openDatabase() {
        return new Promise((resolve, reject) => {
            const request = indexedDB.open(DB_NAME, DB_VERSION);

            request.onupgradeneeded = function (event) {
                const database = event.target.result;
                for (const storeName of STORES) {
                    if (!database.objectStoreNames.contains(storeName)) {
                        database.createObjectStore(storeName);
                    }
                }
            };

            request.onsuccess = function (event) {
                resolve(event.target.result);
            };

            request.onerror = function (event) {
                reject(new Error('Failed to open IndexedDB: ' + event.target.error));
            };
        });
    }

    function migrateFromLocalStorage(database) {
        const promises = [];

        for (const [lsKey, storeName] of Object.entries(MIGRATION_MAP)) {
            const data = localStorage.getItem(lsKey);
            if (data !== null) {
                const promise = new Promise((resolve, reject) => {
                    const tx = database.transaction(storeName, 'readwrite');
                    const store = tx.objectStore(storeName);
                    store.put(data, DATA_KEY);

                    tx.oncomplete = function () {
                        localStorage.removeItem(lsKey);
                        resolve();
                    };

                    tx.onerror = function (event) {
                        reject(new Error('Migration failed for ' + storeName + ': ' + event.target.error));
                    };

                    tx.onabort = function (event) {
                        reject(new Error('Migration aborted for ' + storeName + ': ' + (event.target.error || 'Transaction aborted')));
                    };
                });
                promises.push(promise);
            }
        }

        return Promise.all(promises);
    }

    window.indexedDb = {
        initialize: async function () {
            db = await openDatabase();
            await migrateFromLocalStorage(db);
        },

        getItem: async function (storeName) {
            const database = await ensureDb();
            return new Promise((resolve, reject) => {
                const tx = database.transaction(storeName, 'readonly');
                const store = tx.objectStore(storeName);
                const request = store.get(DATA_KEY);

                request.onsuccess = function () {
                    resolve(request.result || null);
                };

                request.onerror = function (event) {
                    reject(new Error('Failed to read from ' + storeName + ': ' + event.target.error));
                };
            });
        },

        setItem: async function (storeName, value) {
            const database = await ensureDb();
            return new Promise((resolve, reject) => {
                const tx = database.transaction(storeName, 'readwrite');
                const store = tx.objectStore(storeName);
                store.put(value, DATA_KEY);

                tx.oncomplete = function () {
                    resolve();
                };

                tx.onerror = function (event) {
                    reject(new Error('Failed to write to ' + storeName + ': ' + event.target.error));
                };
            });
        },

        removeItem: async function (storeName) {
            const database = await ensureDb();
            return new Promise((resolve, reject) => {
                const tx = database.transaction(storeName, 'readwrite');
                const store = tx.objectStore(storeName);
                store.delete(DATA_KEY);

                tx.oncomplete = function () {
                    resolve();
                };

                tx.onerror = function (event) {
                    reject(new Error('Failed to delete from ' + storeName + ': ' + event.target.error));
                };
            });
        },

        clearAll: async function () {
            const database = await ensureDb();
            return new Promise((resolve, reject) => {
                const tx = database.transaction(STORES, 'readwrite');

                for (const storeName of STORES) {
                    const store = tx.objectStore(storeName);
                    store.clear();
                }

                tx.oncomplete = function () {
                    resolve();
                };

                tx.onerror = function (event) {
                    reject(new Error('Failed to clear stores: ' + event.target.error));
                };
            });
        },

        getStorageEstimate: async function () {
            if (navigator.storage && navigator.storage.estimate) {
                const estimate = await navigator.storage.estimate();
                return { usage: estimate.usage || 0, quota: estimate.quota || 0 };
            }
            return { usage: 0, quota: 0 };
        }
    };
})();
