(() => {
    const clock = document.getElementById('clock');
    if (!clock) return;
    const tick = () => clock.textContent = new Intl.DateTimeFormat('ru-RU', { dateStyle: 'medium', timeStyle: 'medium' }).format(new Date());
    tick(); setInterval(tick, 1000);
})();
