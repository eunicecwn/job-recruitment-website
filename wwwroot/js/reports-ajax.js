// Reports AJAX Management
class ReportsManager {
    constructor() {
        this.currentPage = 1;
        this.pageSize = 10;
        this.searchTimeout = null;
        this.init();
    }

    init() {
        this.bindEvents();
        this.currentPage = parseInt($('#currentPageData').val()) || 1;
    }

    bindEvents() {
        // Filter button
        $('#filterBtn').on('click', (e) => {
            e.preventDefault();
            this.currentPage = 1;
            this.loadReports();
        });

        // Clear button
        $('#clearBtn').on('click', (e) => {
            e.preventDefault();
            this.clearFilters();
        });

        // Search input with debounce
        $('#searchTerm').on('input', () => {
            clearTimeout(this.searchTimeout);
            this.searchTimeout = setTimeout