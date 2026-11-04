window.tamaPoke = {
    browserLanguage: () => navigator.language || "",
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
