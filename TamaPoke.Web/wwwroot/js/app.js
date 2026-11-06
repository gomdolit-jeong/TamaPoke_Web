window.tamaPoke = {
    browserLanguage: () => navigator.language || "",
    viewportSize: () => [window.innerWidth, window.innerHeight],

    // 두 탭이 서로 세이브를 덮어쓰지 않도록, 새 탭이 열리면 먼저 열린 탭을 멈춥니다.
    claimTab: (page) => {
        if (!window.BroadcastChannel) return;
        const channel = new BroadcastChannel("tamapoke.tab");
        channel.onmessage = () => {
            channel.close();
            page.invokeMethodAsync("OnOtherTabOpened");
        };
        channel.postMessage("claim");
    },

    requestNotificationPermission: async () => {
        if (!("Notification" in window)) return false;
        return (await Notification.requestPermission()) === "granted";
    },

    // 탭을 보고 있을 때는 화면에서 바로 보이므로 알리지 않습니다.
    notify: (title, body) => {
        if (!document.hidden || !("Notification" in window) || Notification.permission !== "granted") return;
        new Notification(title, { body });
    },

    // 세이브 내보내기: 글자를 파일로 내려받습니다.
    downloadText: (fileName, text) => {
        const url = URL.createObjectURL(new Blob([text], { type: "application/json" }));
        const link = document.createElement("a");
        link.href = url;
        link.download = fileName;
        link.click();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    },

    // 목록에서 원하는 줄(예: 랭킹의 내 순위)이 보이도록 스크롤합니다.
    scrollToElement: (selector) => document.querySelector(selector)?.scrollIntoView({ block: "center", behavior: "smooth" }),

    prefersReducedMotion: () => matchMedia("(prefers-reduced-motion: reduce)").matches,
    setDocumentLanguage: (language) => { document.documentElement.lang = language; },

    // [주파수 Hz, 길이 ms] 목록을 사각파로 재생합니다. 브라우저 정책상 첫 클릭 이후에만 소리가 납니다.
    playNotes: (notes) => {
        const AudioContextClass = window.AudioContext || window.webkitAudioContext;
        if (!AudioContextClass) return;

        const context = window.tamaPoke.audioContext ??= new AudioContextClass();
        if (context.state === "suspended") context.resume();

        const volume = 0.12;
        const fade = 0.002; // 딸깍 소리를 막는 짧은 페이드
        let time = context.currentTime;
        for (const [frequency, ms] of notes) {
            const end = time + ms / 1000;
            if (frequency > 0) {
                const oscillator = context.createOscillator();
                const gain = context.createGain();
                oscillator.type = "square";
                oscillator.frequency.value = frequency;
                gain.gain.setValueAtTime(0, time);
                gain.gain.linearRampToValueAtTime(volume, time + fade);
                gain.gain.setValueAtTime(volume, end - fade);
                gain.gain.linearRampToValueAtTime(0, end);
                oscillator.connect(gain).connect(context.destination);
                oscillator.start(time);
                oscillator.stop(end);
            }
            time = end;
        }
    },
};

// 산책 화면에서는 오른쪽 버튼을 화면 옮기기에 쓰므로 브라우저 메뉴를 막습니다. (휴대폰 길게 누르기도 포함)
document.addEventListener("contextmenu", (e) => {
    if (e.target instanceof Element && e.target.closest(".walk-layer")) e.preventDefault();
});
