window.scrollElementHorizontally = (element, offset) => {
    if (!element) return;
    requestAnimationFrame(() => {
        element.scrollLeft += offset;
    });
};