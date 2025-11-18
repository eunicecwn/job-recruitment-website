// Initiate GET request (AJAX-supported)
$(document).on('click', '[data-get]', e => {
    e.preventDefault();
    const url = e.target.dataset.get;
    location = url || location;
});

// Initiate POST request (AJAX-supported)
$(document).on('click', '[data-post]', e => {
    e.preventDefault();
    const url = e.target.dataset.post;
    const f = $('<form>').appendTo(document.body)[0];
    f.method = 'post';
    f.action = url || location;
    f.submit();
});

// Trim input
$('[data-trim]').on('change', e => {
    e.target.value = e.target.value.trim();
});

// Auto uppercase
$('[data-upper]').on('input', e => {
    const a = e.target.selectionStart;
    const b = e.target.selectionEnd;
    e.target.value = e.target.value.toUpperCase();
    e.target.setSelectionRange(a, b);
});

// RESET form
$('[type=reset]').on('click', e => {
    e.preventDefault();
    location = location;
});

// Check all checkboxes
$('[data-check]').on('click', e => {
    e.preventDefault();
    const name = e.target.dataset.check;
    $(`[name=${name}]`).prop('checked', true);
});

// Uncheck all checkboxes
$('[data-uncheck]').on('click', e => {
    e.preventDefault();
    const name = e.target.dataset.uncheck;
    $(`[name=${name}]`).prop('checked', false);
});

// Row checkable (AJAX-supported)
$(document).on('click', '[data-checkable]', e => {
    if ($(e.target).is(':input,a')) return;

    $(e.currentTarget)
        .find(':checkbox')
        .prop('checked', (i, v) => !v);
});

document.addEventListener('DOMContentLoaded', function () {
    // Sidebar collapse functionality
    const collapseBtn = document.querySelector('.sidebar-collapse-btn');
    const sidebar = document.querySelector('.sidebar');

    if (collapseBtn && sidebar) {
        collapseBtn.addEventListener('click', function () {
            sidebar.classList.toggle('sidebar-collapsed');

            // Update the chevron icon direction
            const icon = this.querySelector('i');
            if (icon) {
                if (sidebar.classList.contains('sidebar-collapsed')) {
                    icon.classList.remove('bi-chevron-left');
                    icon.classList.add('bi-chevron-right');
                } else {
                    icon.classList.remove('bi-chevron-right');
                    icon.classList.add('bi-chevron-left');
                }
            }

            // Optional: Save state to localStorage
            localStorage.setItem('sidebarCollapsed', sidebar.classList.contains('sidebar-collapsed'));
        });
    }

    // Restore sidebar state from localStorage on page load
    const savedState = localStorage.getItem('sidebarCollapsed');
    if (savedState === 'true' && sidebar) {
        sidebar.classList.add('sidebar-collapsed');
        const icon = collapseBtn?.querySelector('i');
        if (icon) {
            icon.classList.remove('bi-chevron-left');
            icon.classList.add('bi-chevron-right');
        }
    }

    // Mobile menu functionality (if needed)
    const mobileMenuBtn = document.querySelector('.mobile-menu-btn');
    if (mobileMenuBtn && sidebar) {
        mobileMenuBtn.addEventListener('click', function () {
            sidebar.classList.toggle('show');
        });
    }

    // Close mobile menu when clicking outside
    document.addEventListener('click', function (event) {
        if (window.innerWidth <= 991.98) {
            const isClickInsideSidebar = sidebar?.contains(event.target);
            const isClickOnMenuBtn = mobileMenuBtn?.contains(event.target);

            if (!isClickInsideSidebar && !isClickOnMenuBtn && sidebar?.classList.contains('show')) {
                sidebar.classList.remove('show');
            }
        }
    });
});