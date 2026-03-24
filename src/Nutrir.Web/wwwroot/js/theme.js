export function setTheme(theme) {
    const resolved = theme === 'system'
        ? (window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light')
        : theme;
    document.documentElement.setAttribute('data-theme', resolved);
    document.documentElement.setAttribute('data-theme-setting', theme);
    localStorage.setItem('theme-preference', theme);
}

export function getStoredTheme() {
    return localStorage.getItem('theme-preference') || 'system';
}

export function watchSystemTheme(dotNetRef) {
    const mq = window.matchMedia('(prefers-color-scheme: dark)');
    mq.addEventListener('change', () => {
        const setting = document.documentElement.getAttribute('data-theme-setting');
        if (setting === 'system') {
            setTheme('system');
            dotNetRef.invokeMethodAsync('OnSystemThemeChanged', mq.matches ? 'dark' : 'light');
        }
    });
}
