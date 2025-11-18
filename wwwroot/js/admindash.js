const AdminDashboard = {
    // Chart colors from your existing code
    CHART_COLORS: {
        primary: '#4F46E5',
        success: '#10B981',
        info: '#06B6D4',
        warning: '#F59E0B',
        danger: '#EF4444',
        secondary: '#8B5CF6'
    },

    // Data storage
    originalChartData: null,
    originalStatsData: null,

    // Current filters (keeping your existing structure)
    currentFilters: {
        dateRange: 7,  // Back to 7 days default
        userType: 'all',
        status: 'all',
        search: '',
        // Support for custom dates
        customStartDate: null,
        customEndDate: null
    },

    // Chart instances (keeping your existing variables)
    charts: {
        userTrends: null,
        userDistribution: null,
        applications: null,
        jobStatus: null,
        topUsers: null,
        activity: null
    },

    // Initialize dashboard
    init: function (chartData, statsData) {
        this.originalChartData = chartData;
        this.originalStatsData = statsData;

        this.initializeFilters();
        this.initializeDashboard();
        this.loadEnhancedData();
    },

    // Filter initialization with auto-apply
    initializeFilters: function () {
        const self = this;

        // Filter button click handlers
        document.querySelectorAll('.filter-btn').forEach(btn => {
            btn.addEventListener('click', function (e) {
                e.preventDefault();

                const filterType = this.dataset.filter;
                const filterValue = this.dataset.value;

                // Remove active from siblings
                document.querySelectorAll(`[data-filter="${filterType}"]`).forEach(b => {
                    b.classList.remove('active');
                });

                // Add active to clicked button
                this.classList.add('active');

                // Handle custom date range
                if (filterType === 'date' && filterValue === 'custom') {
                    document.getElementById('customDateRange').classList.add('show');
                    return;
                } else {
                    document.getElementById('customDateRange').classList.remove('show');
                    if (filterType === 'date') {
                        self.currentFilters.dateRange = parseInt(filterValue);
                        self.currentFilters.customStartDate = null;
                        self.currentFilters.customEndDate = null;
                    }
                }

                // Update current filters
                if (filterType === 'usertype') {
                    self.currentFilters.userType = filterValue;
                } else if (filterType === 'status') {
                    self.currentFilters.status = filterValue;
                }

                // Apply filters immediately when clicked (except custom date)
                if (filterType !== 'date' || filterValue !== 'custom') {
                    self.applyAllFilters();
                }
            });
        });

        // Search input handler with debounce
        const searchInput = document.getElementById('searchFilter');
        if (searchInput) {
            let searchTimeout;
            searchInput.addEventListener('input', function () {
                self.currentFilters.search = this.value.toLowerCase();

                clearTimeout(searchTimeout);
                searchTimeout = setTimeout(() => {
                    if (this.value.length === 0 || this.value.length >= 2) {
                        self.applyAllFilters();
                    }
                }, 500);
            });

            searchInput.addEventListener('keypress', function (e) {
                if (e.key === 'Enter') {
                    clearTimeout(searchTimeout);
                    self.applyAllFilters();
                }
            });
        }
    },

    // Helper method to get filter data for API calls
    getFilterData: function () {
        const filterData = {
            dateRange: this.currentFilters.dateRange,
            userType: this.currentFilters.userType,
            status: this.currentFilters.status,
            search: this.currentFilters.search
        };

        if (this.currentFilters.customStartDate && this.currentFilters.customEndDate) {
            filterData.customStartDate = this.currentFilters.customStartDate;
            filterData.customEndDate = this.currentFilters.customEndDate;
        }

        return filterData;
    },

    // Keep your existing dashboard initialization
    initializeDashboard: function () {
        try {
            if (this.originalChartData && this.originalChartData.monthLabels && this.originalChartData.monthLabels.length > 0) {
                this.createUserTrendsChart();
            }

            if (this.originalStatsData) {
                this.createUserDistributionChart();
            }

            if (this.originalChartData && this.originalChartData.applicationTrends && this.originalChartData.applicationTrends.length > 0) {
                this.createApplicationsChart();
            }

            if (this.originalChartData && this.originalChartData.jobStats) {
                this.createJobStatusChart();
            }
        } catch (error) {
            console.error('Error initializing dashboard:', error);
            this.showError('Failed to initialize dashboard charts');
        }
    },

    // Load enhanced data for new features
    loadEnhancedData: function () {
        this.updateTopUsersChart();
        this.updateActivityChart();
        this.updateEmployersTable();
        this.updateSeekersTable();
    },

    // Apply all filters method with better error handling
    applyAllFilters: async function () {
        try {
            this.showGlobalLoading(true);
            this.hideError();

            await this.updateMainStats();

            await Promise.all([
                this.updateTopUsersChart(),
                this.updateActivityChart(),
                this.updateEmployersTable(),
                this.updateSeekersTable()
            ]);

            this.updateFilterStatus();

        } catch (error) {
            console.error('Error applying filters:', error);
            this.showError('Failed to apply filters. Please try again.');
        } finally {
            this.showGlobalLoading(false);
        }
    },

    // Update main dashboard stats with better error handling
    updateMainStats: async function () {
        try {
            const filterData = this.getFilterData();

            const response = await this.fetchData('/Admin/GetFilteredDashboardData', filterData);

            if (response && response.stats) {
                this.updateStatCard('totalUsersCard', response.stats.totalUsers);
                this.updateStatCard('totalEmployersCard', response.stats.totalEmployers);
                this.updateStatCard('totalJobSeekersCard', response.stats.totalJobSeekers);
                this.updateStatCard('totalJobsCard', response.stats.totalJobs);
                this.updateStatCard('totalApplicationsCard', response.stats.totalApplications);
                this.updateStatCard('pendingEmployersCard', response.stats.pendingEmployers);

                if (response.chartData) {
                    this.updateMainCharts(response.chartData);
                }
            } else if (response && response.error) {
                this.showError('Server error: ' + response.error);
            }
        } catch (error) {
            console.error('Error updating main stats:', error);
            this.showError('Failed to update dashboard statistics');
        }
    },

    // Update stat card value with animation
    updateStatCard: function (elementId, value) {
        const element = document.getElementById(elementId);
        if (element) {
            element.style.opacity = '0.5';
            element.style.transform = 'scale(0.95)';

            setTimeout(() => {
                element.textContent = value || 0;
                element.style.opacity = '1';
                element.style.transform = 'scale(1)';
            }, 150);
        }
    },

    // Update main charts with filtered data
    updateMainCharts: function (chartData) {
        if (this.charts.userTrends && chartData.monthLabels) {
            this.charts.userTrends.data.labels = chartData.monthLabels;
            this.charts.userTrends.data.datasets[0].data = chartData.jobSeekerTrends || [];
            this.charts.userTrends.data.datasets[1].data = chartData.employerTrends || [];
            this.charts.userTrends.update('active');
        }

        if (this.charts.applications && chartData.applicationTrends) {
            this.charts.applications.data.datasets[0].data = chartData.applicationTrends;
            this.charts.applications.update('active');
        }

        if (this.charts.jobStatus && chartData.jobStats) {
            this.charts.jobStatus.data.datasets[0].data = [
                chartData.jobStats.activeJobs || 0,
                chartData.jobStats.closedJobs || 0,
                chartData.jobStats.draftJobs || 0
            ];
            this.charts.jobStatus.update('active');
        }
    },

    // Enhanced Top Users Chart
    updateTopUsersChart: async function () {
        try {
            const count = parseInt(document.getElementById('topUsersCount')?.value || 5);
            const sortBy = document.getElementById('topUsersSort')?.value || 'applications';
            const order = document.getElementById('topUsersOrder')?.value || 'desc';

            const filterData = this.getFilterData();
            const requestData = {
                ...filterData,
                count: count,
                sortBy: sortBy,
                order: order
            };

            const response = await this.fetchData('/Admin/GetTopUsersChart', requestData);

            if (response && response.success) {
                this.renderTopUsersChart(response.data);
            } else {
                const errorMsg = response ? response.message : 'Unknown error';
                console.error('Top users chart error:', errorMsg);
                this.showNoDataChart('topUsersChart', 'Failed to load user data');
            }
        } catch (error) {
            console.error('Top users chart error:', error);
            this.showNoDataChart('topUsersChart', 'Error loading user data');
        }
    },

    renderTopUsersChart: function (data) {
        if (this.charts.topUsers) {
            this.charts.topUsers.destroy();
        }

        const ctx = document.getElementById('topUsersChart');
        if (!ctx) return;

        if (!data || !data.labels || data.labels.length === 0) {
            this.showNoDataChart('topUsersChart', 'No users found for selected criteria');
            return;
        }

        this.charts.topUsers = new Chart(ctx, {
            type: 'bar',
            data: data,
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        callbacks: {
                            label: function (context) {
                                return `${context.dataset.label}: ${context.parsed.y}`;
                            }
                        }
                    }
                },
                scales: {
                    y: {
                        beginAtZero: true,
                        ticks: {
                            callback: function (value) {
                                return Number.isInteger(value) ? value : '';
                            }
                        }
                    }
                }
            }
        });
    },

    // Enhanced Activity Chart
    updateActivityChart: async function () {
        try {
            const count = document.getElementById('activityUsersCount')?.value || 10;
            const activityType = document.getElementById('activityType')?.value || 'all';

            const filterData = this.getFilterData();
            const requestData = {
                ...filterData,
                count: count === 'all' ? 999 : parseInt(count),
                activityType: activityType
            };

            const response = await this.fetchData('/Admin/GetActivityChart', requestData);

            if (response && response.success) {
                this.renderActivityChart(response.data);
            } else {
                const errorMsg = response ? response.message : 'Unknown error';
                console.error('Activity chart error:', errorMsg);
                this.showNoDataChart('activityChart', 'Failed to load activity data');
            }
        } catch (error) {
            console.error('Activity chart error:', error);
            this.showNoDataChart('activityChart', 'Error loading activity data');
        }
    },

    renderActivityChart: function (data) {
        if (this.charts.activity) {
            this.charts.activity.destroy();
        }

        const ctx = document.getElementById('activityChart');
        if (!ctx) return;

        if (!data || !data.labels || data.labels.length === 0) {
            this.showNoDataChart('activityChart', 'No activity data found');
            return;
        }

        this.charts.activity = new Chart(ctx, {
            type: 'line',
            data: data,
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        callbacks: {
                            label: function (context) {
                                return `Activity Score: ${context.parsed.y}`;
                            }
                        }
                    }
                },
                scales: {
                    y: { beginAtZero: true }
                }
            }
        });
    },

    // Enhanced Employers Table
    updateEmployersTable: async function () {
        try {
            this.showLoading('topEmployersTable');

            const count = document.getElementById('employersCount')?.value || 10;
            const sortBy = document.getElementById('employersSort')?.value || 'success_rate';

            const filterData = this.getFilterData();
            const requestData = {
                ...filterData,
                count: count === 'all' ? 999 : parseInt(count),
                sortBy: sortBy,
                order: 'desc'
            };

            const response = await this.fetchData('/Admin/GetEmployerRankings', requestData);

            if (response && response.success) {
                this.renderEmployersTable(response.data);
            } else {
                const errorMsg = response ? response.message : 'Unknown error';
                console.error('Employers table error:', errorMsg);
                this.showNoDataTable('topEmployersTable', 5, 'Failed to load employer data');
            }
        } catch (error) {
            console.error('Employers table error:', error);
            this.showNoDataTable('topEmployersTable', 5, 'Error loading employer data');
        }
    },

    renderEmployersTable: function (data) {
        const tbody = document.getElementById('topEmployersTable');
        if (!tbody) return;

        if (!data || data.length === 0) {
            this.showNoDataTable('topEmployersTable', 5, 'No employers found for selected criteria');
            return;
        }

        tbody.innerHTML = data.map(employer => `
            <tr>
                <td><span class="badge bg-primary">#${employer.rank}</span></td>
                <td>
                    <div class="d-flex align-items-center">
                        <div class="bg-success rounded-circle me-2" style="width: 8px; height: 8px;"></div>
                        ${this.escapeHtml(employer.company)}
                    </div>
                </td>
                <td>${employer.jobsPosted}</td>
                <td>${employer.applications}</td>
                <td>
                    <span class="trend-${this.getTrendClass(employer.successRate)}">
                        <i class="bi bi-arrow-${this.getTrendIcon(employer.successRate)}"></i> 
                        ${employer.successRate}%
                    </span>
                </td>
            </tr>
        `).join('');
    },

    // Enhanced Job Seekers Table
    updateSeekersTable: async function () {
        try {
            this.showLoading('seekersTableBody');

            const count = document.getElementById('seekersCount')?.value || 10;
            const sortBy = document.getElementById('seekersSort')?.value || 'most_active';

            const filterData = this.getFilterData();
            const requestData = {
                ...filterData,
                count: count === 'all' ? 999 : parseInt(count),
                sortBy: sortBy,
                order: 'desc'
            };

            const response = await this.fetchData('/Admin/GetJobSeekerRankings', requestData);

            if (response && response.success) {
                this.renderSeekersTable(response.data);
            } else {
                const errorMsg = response ? response.message : 'Unknown error';
                console.error('Job seekers table error:', errorMsg);
                this.showNoDataTable('seekersTableBody', 6, 'Failed to load job seeker data');
            }
        } catch (error) {
            console.error('Job seekers table error:', error);
            this.showNoDataTable('seekersTableBody', 6, 'Error loading job seeker data');
        }
    },

    renderSeekersTable: function (data) {
        const tbody = document.getElementById('seekersTableBody');
        if (!tbody) return;

        if (!data || data.length === 0) {
            this.showNoDataTable('seekersTableBody', 6, 'No job seekers found for selected criteria');
            return;
        }

        tbody.innerHTML = data.map(seeker => `
            <tr>
                <td><span class="badge bg-primary">#${seeker.rank}</span></td>
                <td>${this.escapeHtml(seeker.name)}</td>
                <td>${seeker.applications}</td>
                <td>
                    <span class="trend-${this.getTrendClass(seeker.interviewRate)}">
                        ${seeker.interviewRate}%
                    </span>
                </td>
                <td>${this.escapeHtml(seeker.lastActivity)}</td>
                <td>
                    <div class="progress" style="height: 8px;">
                        <div class="progress-bar bg-info" style="width: ${seeker.profileCompletion}%"></div>
                    </div>
                    <small class="text-muted">${seeker.profileCompletion}%</small>
                </td>
            </tr>
        `).join('');
    },

    // Chart creation methods
    createUserTrendsChart: function () {
        const ctx = document.getElementById('userTrendsChart');
        if (!ctx) return;

        try {
            this.charts.userTrends = new Chart(ctx, {
                type: 'line',
                data: {
                    labels: this.originalChartData.monthLabels || [],
                    datasets: [{
                        label: 'Job Seekers',
                        data: this.originalChartData.jobSeekerTrends || [],
                        borderColor: this.CHART_COLORS.primary,
                        backgroundColor: this.CHART_COLORS.primary + '20',
                        tension: 0.4,
                        fill: true
                    }, {
                        label: 'Employers',
                        data: this.originalChartData.employerTrends || [],
                        borderColor: this.CHART_COLORS.success,
                        backgroundColor: this.CHART_COLORS.success + '20',
                        tension: 0.4,
                        fill: true
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: {
                        legend: { display: false }
                    },
                    scales: {
                        y: { beginAtZero: true }
                    }
                }
            });
        } catch (error) {
            console.error('Error creating user trends chart:', error);
        }
    },

    createUserDistributionChart: function () {
        const ctx = document.getElementById('userDistributionChart');
        if (!ctx) return;

        try {
            this.charts.userDistribution = new Chart(ctx, {
                type: 'doughnut',
                data: {
                    labels: ['Job Seekers', 'Employers', 'Admins'],
                    datasets: [{
                        data: [
                            this.originalStatsData.totalJobSeekers || 0,
                            this.originalStatsData.totalEmployers || 0,
                            this.originalStatsData.totalAdmins || 0
                        ],
                        backgroundColor: [
                            this.CHART_COLORS.primary,
                            this.CHART_COLORS.success,
                            this.CHART_COLORS.warning
                        ]
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: {
                        legend: { position: 'bottom' }
                    }
                }
            });
        } catch (error) {
            console.error('Error creating user distribution chart:', error);
        }
    },

    createApplicationsChart: function () {
        const ctx = document.getElementById('applicationsChart');
        if (!ctx) return;

        try {
            this.charts.applications = new Chart(ctx, {
                type: 'bar',
                data: {
                    labels: this.originalChartData.monthLabels || [],
                    datasets: [{
                        label: 'Applications',
                        data: this.originalChartData.applicationTrends || [],
                        backgroundColor: this.CHART_COLORS.warning,
                        borderRadius: 4
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: {
                        legend: { display: false }
                    },
                    scales: {
                        y: { beginAtZero: true }
                    }
                }
            });
        } catch (error) {
            console.error('Error creating applications chart:', error);
        }
    },

    createJobStatusChart: function () {
        const ctx = document.getElementById('jobStatusChart');
        if (!ctx) return;

        try {
            this.charts.jobStatus = new Chart(ctx, {
                type: 'doughnut',
                data: {
                    labels: ['Active', 'Closed', 'Draft'],
                    datasets: [{
                        data: [
                            this.originalChartData.jobStats.activeJobs || 0,
                            this.originalChartData.jobStats.closedJobs || 0,
                            this.originalChartData.jobStats.draftJobs || 0
                        ],
                        backgroundColor: [
                            this.CHART_COLORS.success,
                            this.CHART_COLORS.danger,
                            this.CHART_COLORS.secondary
                        ]
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: {
                        legend: { position: 'bottom' }
                    }
                }
            });
        } catch (error) {
            console.error('Error creating job status chart:', error);
        }
    },

    // Utility Methods with CSRF token support
    fetchData: async function (url, data) {
        let token = document.querySelector('[name="__RequestVerificationToken"]')?.value;
        if (!token) {
            token = document.querySelector('meta[name="RequestVerificationToken"]')?.getAttribute('content');
        }
        if (!token) {
            token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        }

        try {
            const response = await fetch(url, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': token || ''
                },
                body: JSON.stringify(data)
            });

            if (!response.ok) {
                throw new Error(`HTTP error! status: ${response.status} - ${response.statusText}`);
            }

            const result = await response.json();
            return result;

        } catch (error) {
            console.error('Fetch error:', error);
            throw error;
        }
    },

    // Show global loading state
    showGlobalLoading: function (show) {
        const overlay = document.getElementById('loadingOverlay');
        const btn = document.getElementById('applyFiltersBtn');

        if (overlay) {
            overlay.style.display = show ? 'flex' : 'none';
        }

        if (btn) {
            if (show) {
                btn.innerHTML = '<i class="bi bi-hourglass-split me-2"></i>Applying...';
                btn.disabled = true;
            } else {
                btn.innerHTML = '<i class="bi bi-filter me-2"></i>Apply Filters';
                btn.disabled = false;
            }
        }
    },

    showLoading: function (elementId) {
        const element = document.getElementById(elementId);
        if (element) {
            const colCount = element.closest('table')?.querySelector('thead tr')?.children.length || 6;
            element.innerHTML = `
                <tr>
                    <td colspan="${colCount}" class="text-center py-4">
                        <div class="spinner-border spinner-border-sm text-primary me-2" role="status"></div>
                        Loading data...
                    </td>
                </tr>
            `;
        }
    },

    showNoDataTable: function (elementId, colCount, message) {
        const element = document.getElementById(elementId);
        if (element) {
            element.innerHTML = `
                <tr>
                    <td colspan="${colCount}" class="text-center text-muted py-4">
                        <i class="bi bi-inbox fs-3 d-block mb-2"></i>
                        ${message}
                    </td>
                </tr>
            `;
        }
    },

    showNoDataChart: function (chartId, message) {
        const chartContainer = document.getElementById(chartId)?.parentElement;
        if (chartContainer) {
            const canvas = document.getElementById(chartId);
            if (canvas) {
                canvas.style.display = 'none';
            }

            let noDataDiv = chartContainer.querySelector('.no-data-message');
            if (!noDataDiv) {
                noDataDiv = document.createElement('div');
                noDataDiv.className = 'no-data-message';
                chartContainer.appendChild(noDataDiv);
            }

            noDataDiv.innerHTML = `
                <i class="bi bi-graph-up fs-1 d-block mb-3"></i>
                <p>${message}</p>
            `;
            noDataDiv.style.display = 'block';
        }
    },

    showError: function (message) {
        console.error(message);

        const errorDiv = document.getElementById('errorMessage');
        const errorText = document.getElementById('errorText');

        if (errorDiv && errorText) {
            errorText.textContent = message;
            errorDiv.classList.add('show');

            setTimeout(() => {
                this.hideError();
            }, 5000);
        }
    },

    hideError: function () {
        const errorDiv = document.getElementById('errorMessage');
        if (errorDiv) {
            errorDiv.classList.remove('show');
        }
    },

    escapeHtml: function (text) {
        if (!text) return '';
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    },

    getTrendClass: function (value) {
        if (value >= 70) return 'up';
        if (value >= 50) return 'stable';
        return 'down';
    },

    getTrendIcon: function (value) {
        if (value >= 70) return 'up';
        if (value >= 50) return 'right';
        return 'down';
    },

    filterDashboardData: function () {
        let dateRangeText = '';
        if (this.currentFilters.customStartDate && this.currentFilters.customEndDate) {
            dateRangeText = `(${this.currentFilters.customStartDate} to ${this.currentFilters.customEndDate})`;
        } else if (this.currentFilters.dateRange === null) {
            dateRangeText = '(No Date Filter)';
        } else {
            dateRangeText = `(Last ${this.currentFilters.dateRange} days)`;
        }

        const chartDateRange = document.getElementById('chartDateRange');
        if (chartDateRange) {
            chartDateRange.textContent = dateRangeText;
        }

        this.loadEnhancedData();
    },

    updateFilterStatus: function () {
        let status = 'Showing ';

        if (this.currentFilters.userType !== 'all') {
            status += this.currentFilters.userType + ' ';
        } else {
            status += 'all ';
        }

        if (this.currentFilters.status !== 'all') {
            status += this.currentFilters.status + ' ';
        }

        if (this.currentFilters.customStartDate && this.currentFilters.customEndDate) {
            status += `data from ${this.currentFilters.customStartDate} to ${this.currentFilters.customEndDate}`;
        } else if (this.currentFilters.dateRange >= 99999) {
            status += 'data (no date filter)';
        } else {
            status += `data for last ${this.currentFilters.dateRange} days`;
        }

        if (this.currentFilters.search) {
            status += ` matching "${this.currentFilters.search}"`;
        }

        const filterStatus = document.getElementById('filterStatus');
        if (filterStatus) {
            filterStatus.textContent = status;
        }
    }
};

// Global functions for HTML onclick handlers
function applyCustomDate() {
    const startDate = document.getElementById('startDate').value;
    const endDate = document.getElementById('endDate').value;

    if (startDate && endDate) {
        if (new Date(startDate) > new Date(endDate)) {
            AdminDashboard.showError('Start date cannot be later than end date');
            return;
        }

        AdminDashboard.currentFilters.customStartDate = startDate;
        AdminDashboard.currentFilters.customEndDate = endDate;
        AdminDashboard.currentFilters.dateRange = 'custom';
        applyAllFilters();
    } else {
        AdminDashboard.showError('Please select both start and end dates');
    }
}

function applySearchFilter() {
    const searchValue = document.getElementById('searchFilter').value;
    AdminDashboard.currentFilters.search = searchValue.toLowerCase();
    applyAllFilters();
}

function applyAllFilters() {
    AdminDashboard.applyAllFilters();
}

function resetAllFilters() {
    // Reset filter buttons
    document.querySelectorAll('.filter-btn').forEach(btn => {
        btn.classList.remove('active');
    });

    // Set defaults
    document.querySelector('[data-filter="date"][data-value="7"]')?.classList.add('active');
    document.querySelector('[data-filter="usertype"][data-value="all"]')?.classList.add('active');
    document.querySelector('[data-filter="status"][data-value="all"]')?.classList.add('active');

    // Clear custom dates
    document.getElementById('startDate').value = '';
    document.getElementById('endDate').value = '';
    document.getElementById('customDateRange').classList.remove('show');

    // Reset dropdowns
    document.getElementById('employersCount').value = '10';
    document.getElementById('employersSort').value = 'success_rate';
    document.getElementById('seekersCount').value = '10';
    document.getElementById('seekersSort').value = 'most_active';

    // Clear search if exists
    const searchInput = document.getElementById('searchFilter');
    if (searchInput) searchInput.value = '';

    // CRITICAL: Reset the filters in AdminDashboard
    AdminDashboard.currentFilters = {
        dateRange: 7,
        userType: 'all',
        status: 'all',
        search: '',
        customStartDate: null,
        customEndDate: null
    };

    // Re-initialize with original data (simulating a refresh)
    AdminDashboard.initializeDashboard();
    AdminDashboard.loadEnhancedData();
    AdminDashboard.updateFilterStatus();

    // Success message
    const msg = document.createElement('div');
    msg.className = 'alert alert-success position-fixed top-0 end-0 m-3';
    msg.style.zIndex = '9999';
    msg.innerHTML = '<i class="bi bi-check-circle me-2"></i>Filters reset successfully';
    document.body.appendChild(msg);
    setTimeout(() => msg.remove(), 2000);
}
// Enhanced function handlers
function updateTopUsersChart() {
    AdminDashboard.updateTopUsersChart();
}

function updateActivityChart() {
    AdminDashboard.updateActivityChart();
}

function updateEmployersTable() {
    AdminDashboard.updateEmployersTable();
}

function updateSeekersTable() {
    AdminDashboard.updateSeekersTable();
}

function refreshTopUsersData() {
    AdminDashboard.updateTopUsersChart();
}

function refreshActivityData() {
    AdminDashboard.updateActivityChart();
}

function refreshEmployersData() {
    AdminDashboard.updateEmployersTable();
}

function refreshSeekersData() {
    AdminDashboard.updateSeekersTable();
}

// ========== STANDARD PDF EXPORT FUNCTIONS ==========

async function exportUserTrendsChartPdf() {
    try {
        const btn = event.target.closest('.pdf-export-btn');
        if (btn) {
            btn.disabled = true;
            btn.classList.add('loading');
            btn.innerHTML = '<i class="bi bi-hourglass-split"></i> Generating...';
        }

        const filterData = AdminDashboard.getFilterData();
        const token = document.querySelector('[name="__RequestVerificationToken"]')?.value;

        const response = await fetch('/Admin/ExportUserTrendsChartPdf', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': token || ''
            },
            body: JSON.stringify(filterData)
        });

        if (response.ok) {
            await downloadPdfResponse(response, 'UserTrendsReport');
            showSuccessMessage('User trends chart exported successfully!');
        } else {
            const errorData = await response.json();
            throw new Error(errorData.message || 'Failed to export chart PDF');
        }

    } catch (error) {
        console.error('Export user trends chart PDF error:', error);
        AdminDashboard.showError('Failed to export user trends chart: ' + error.message);
    } finally {
        resetPdfButton(event.target.closest('.pdf-export-btn'));
    }
}

async function exportUserDistributionChartPdf() {
    try {
        const btn = event.target.closest('.pdf-export-btn');
        if (btn) {
            btn.disabled = true;
            btn.classList.add('loading');
            btn.innerHTML = '<i class="bi bi-hourglass-split"></i> Generating...';
        }

        const filterData = AdminDashboard.getFilterData();
        const token = document.querySelector('[name="__RequestVerificationToken"]')?.value;

        const response = await fetch('/Admin/ExportUserDistributionChartPdf', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': token || ''
            },
            body: JSON.stringify(filterData)
        });

        if (response.ok) {
            await downloadPdfResponse(response, 'UserDistributionReport');
            showSuccessMessage('User distribution chart exported successfully!');
        } else {
            const errorData = await response.json();
            throw new Error(errorData.message || 'Failed to export chart PDF');
        }

    } catch (error) {
        console.error('Export user distribution chart PDF error:', error);
        AdminDashboard.showError('Failed to export user distribution chart: ' + error.message);
    } finally {
        resetPdfButton(event.target.closest('.pdf-export-btn'));
    }
}

async function exportJobStatusChartPdf() {
    try {
        const btn = event.target.closest('.pdf-export-btn');
        if (btn) {
            btn.disabled = true;
            btn.classList.add('loading');
            btn.innerHTML = '<i class="bi bi-hourglass-split"></i> Generating...';
        }

        const filterData = AdminDashboard.getFilterData();
        const token = document.querySelector('[name="__RequestVerificationToken"]')?.value;

        const response = await fetch('/Admin/ExportJobStatusChartPdf', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': token || ''
            },
            body: JSON.stringify(filterData)
        });

        if (response.ok) {
            await downloadPdfResponse(response, 'JobStatusReport');
            showSuccessMessage('Job status chart exported successfully!');
        } else {
            const errorData = await response.json();
            throw new Error(errorData.message || 'Failed to export chart PDF');
        }

    } catch (error) {
        console.error('Export job status chart PDF error:', error);
        AdminDashboard.showError('Failed to export job status chart: ' + error.message);
    } finally {
        resetPdfButton(event.target.closest('.pdf-export-btn'));
    }
}

async function exportEmployersPdf() {
    try {
        const btn = event.target.closest('.pdf-export-btn');
        if (btn) {
            btn.disabled = true;
            btn.classList.add('loading');
            btn.innerHTML = '<i class="bi bi-hourglass-split"></i> Generating...';
        }

        const count = document.getElementById('employersCount')?.value || 10;
        const sortBy = document.getElementById('employersSort')?.value || 'success_rate';

        const filterData = AdminDashboard.getFilterData();
        const requestData = {
            ...filterData,
            count: count === 'all' ? 999 : parseInt(count),
            sortBy: sortBy,
            order: 'desc'
        };

        const token = document.querySelector('[name="__RequestVerificationToken"]')?.value;

        const response = await fetch('/Admin/ExportEmployersPdf', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': token || ''
            },
            body: JSON.stringify(requestData)
        });

        if (response.ok) {
            await downloadPdfResponse(response, 'EmployersReport');
            showSuccessMessage('Employers report exported successfully!');
        } else {
            const errorData = await response.json();
            throw new Error(errorData.message || 'Failed to export PDF');
        }

    } catch (error) {
        console.error('Export employers PDF error:', error);
        AdminDashboard.showError('Failed to export employers PDF: ' + error.message);
    } finally {
        resetPdfButton(event.target.closest('.pdf-export-btn'));
    }
}

async function exportJobSeekersPdf() {
    try {
        const btn = event.target.closest('.pdf-export-btn');
        if (btn) {
            btn.disabled = true;
            btn.classList.add('loading');
            btn.innerHTML = '<i class="bi bi-hourglass-split"></i> Generating...';
        }

        const count = document.getElementById('seekersCount')?.value || 10;
        const sortBy = document.getElementById('seekersSort')?.value || 'most_active';

        const filterData = AdminDashboard.getFilterData();
        const requestData = {
            ...filterData,
            count: count === 'all' ? 999 : parseInt(count),
            sortBy: sortBy,
            order: 'desc'
        };

        const token = document.querySelector('[name="__RequestVerificationToken"]')?.value;

        const response = await fetch('/Admin/ExportJobSeekersPdf', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': token || ''
            },
            body: JSON.stringify(requestData)
        });

        if (response.ok) {
            await downloadPdfResponse(response, 'JobSeekersReport');
            showSuccessMessage('Job seekers report exported successfully!');
        } else {
            const errorData = await response.json();
            throw new Error(errorData.message || 'Failed to export PDF');
        }

    } catch (error) {
        console.error('Export job seekers PDF error:', error);
        AdminDashboard.showError('Failed to export job seekers PDF: ' + error.message);
    } finally {
        resetPdfButton(event.target.closest('.pdf-export-btn'));
    }
}

async function exportRecentActivitiesPdf() {
    try {
        const btn = event.target.closest('.pdf-export-btn');
        if (btn) {
            btn.disabled = true;
            btn.classList.add('loading');
            btn.innerHTML = '<i class="bi bi-hourglass-split"></i> Generating...';
        }

        const filterData = AdminDashboard.getFilterData();
        const token = document.querySelector('[name="__RequestVerificationToken"]')?.value;

        const response = await fetch('/Admin/ExportRecentActivitiesPdf', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': token || ''
            },
            body: JSON.stringify(filterData)
        });

        if (response.ok) {
            await downloadPdfResponse(response, 'RecentActivitiesReport');
            showSuccessMessage('Recent activities report exported successfully!');
        } else {
            const errorData = await response.json();
            throw new Error(errorData.message || 'Failed to export PDF');
        }

    } catch (error) {
        console.error('Export recent activities PDF error:', error);
        AdminDashboard.showError('Failed to export recent activities PDF: ' + error.message);
    } finally {
        resetPdfButton(event.target.closest('.pdf-export-btn'));
    }
}

// ========== HELPER FUNCTIONS ==========

async function downloadPdfResponse(response, defaultFilename) {
    const blob = await response.blob();
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;

    const contentDisposition = response.headers.get('Content-Disposition');
    let filename = `${defaultFilename}.pdf`;
    if (contentDisposition) {
        const filenameMatch = contentDisposition.match(/filename="(.+)"/);
        if (filenameMatch) {
            filename = filenameMatch[1];
        }
    }

    a.download = filename;
    document.body.appendChild(a);
    a.click();
    window.URL.revokeObjectURL(url);
    document.body.removeChild(a);
}

function resetPdfButton(btn) {
    if (btn) {
        btn.disabled = false;
        btn.classList.remove('loading');
        btn.innerHTML = '<i class="bi bi-file-earmark-pdf"></i> PDF';
    }
}

function showSuccessMessage(message, duration = 4000) {
    let successDiv = document.getElementById('successMessage');
    if (!successDiv) {
        successDiv = document.createElement('div');
        successDiv.id = 'successMessage';
        successDiv.className = 'alert alert-success alert-dismissible fade position-fixed';
        successDiv.style.top = '20px';
        successDiv.style.right = '20px';
        successDiv.style.zIndex = '9999';
        successDiv.style.minWidth = '300px';
        successDiv.style.maxWidth = '400px';
        document.body.appendChild(successDiv);
    }

    successDiv.innerHTML = `
        <div class="d-flex align-items-center">
            <i class="bi bi-check-circle-fill text-success me-2 fs-5"></i>
            <div>${message}</div>
            <button type="button" class="btn-close ms-auto" data-bs-dismiss="alert"></button>
        </div>
    `;

    successDiv.classList.add('show');

    setTimeout(() => {
        if (successDiv && successDiv.parentNode) {
            successDiv.classList.remove('show');
            setTimeout(() => {
                if (successDiv && successDiv.parentNode) {
                    successDiv.parentNode.removeChild(successDiv);
                }
            }, 150);
        }
    }, duration);
}