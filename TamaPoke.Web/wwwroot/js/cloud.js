// 구글 로그인과 클라우드 세이브. 아래 설정값은 공개되는 식별값이고, 접근은 Firestore 규칙으로 막습니다.
// (본인 계정의 saves/{uid} 문서만 읽고 쓸 수 있음)
const FIREBASE_VERSION = "12.19.0";
const BASE = `https://www.gstatic.com/firebasejs/${FIREBASE_VERSION}`;

const firebaseConfig = {
    apiKey: "AIzaSyBd08Nlr2P9gHR6hCDdxGLLwfSV0J21uKM",
    authDomain: "tamapoke-b6130.firebaseapp.com",
    projectId: "tamapoke-b6130",
    storageBucket: "tamapoke-b6130.firebasestorage.app",
    messagingSenderId: "28198197455",
    appId: "1:28198197455:web:becd7879db6c2f6289bfba",
};

let auth, authApi, app, firestore;

async function db() {
    if (!firestore) {
        const api = await import(`${BASE}/firebase-firestore.js`);
        firestore = { api, instance: api.getFirestore(app) };
    }
    return firestore;
}

export async function init(dotnet) {
    const appApi = await import(`${BASE}/firebase-app.js`);
    authApi = await import(`${BASE}/firebase-auth.js`);
    app = appApi.initializeApp(firebaseConfig);
    auth = authApi.getAuth(app);

    authApi.onAuthStateChanged(auth, (user) => dotnet.invokeMethodAsync("OnAuthChanged", user ? user.uid : null));
    document.addEventListener("visibilitychange", () => {
        if (document.visibilityState === "hidden") dotnet.invokeMethodAsync("OnPageHidden");
    });
}

// 성공하면 빈 문자열, 실패하면 오류 코드를 돌려줍니다.
export async function signIn() {
    try {
        await authApi.signInWithPopup(auth, new authApi.GoogleAuthProvider());
        return "";
    } catch (error) {
        return error.code || "unknown";
    }
}

export function signOut() {
    return authApi.signOut(auth);
}

// "버전\n세이브 JSON"을 돌려줍니다. 세이브가 없으면 빈 문자열입니다.
export async function load() {
    const { api, instance } = await db();
    const snapshot = await api.getDoc(api.doc(instance, "saves", auth.currentUser.uid));
    if (!snapshot.exists()) return "";
    const value = snapshot.data();
    return `${value.version}\n${value.data}`;
}

// 서버 버전이 expected와 같을 때만 저장하고 새 버전을 돌려줍니다.
// 다른 기기가 먼저 저장했으면 저장하지 않고 음수(0 - 서버 버전)를 돌려줍니다.
export async function save(json, expected) {
    const { api, instance } = await db();
    const ref = api.doc(instance, "saves", auth.currentUser.uid);
    return api.runTransaction(instance, async (transaction) => {
        const snapshot = await transaction.get(ref);
        const current = snapshot.exists() ? snapshot.data().version : 0;
        if (current !== expected) return -current;

        const next = current + 1;
        transaction.set(ref, { data: json, version: next, savedAt: api.serverTimestamp() });
        return next;
    });
}
