window.scrollElementHorizontally = (element, offset) => {
    if (!element) return;
    requestAnimationFrame(() => {
        element.scrollLeft += offset;
    });
};
window.scrollElementVertically = (element, amount) => {
    if (element) {
        element.scrollBy({ top: amount, behavior: 'smooth' });
    }
}
