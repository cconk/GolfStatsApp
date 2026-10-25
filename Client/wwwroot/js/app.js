window.golfTheme = {
    get: function () {
        return document.documentElement.getAttribute('data-bs-theme') || 'light';
    },
    set: function (theme) {
        document.documentElement.setAttribute('data-bs-theme', theme);
        localStorage.setItem('golf-theme', theme);
        var meta = document.querySelector('meta[name="theme-color"]');
        if (meta) meta.setAttribute('content', theme === 'dark' ? '#0f1a15' : '#0b5d3b');
    }
};
