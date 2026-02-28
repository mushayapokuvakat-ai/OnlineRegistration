/**
 * api.js - Centralized API communication layer
 * Handles all fetch requests, JWT token management, and auth state.
 */

const API_BASE = '/api';

// ─── Token Management ──────────────────────────────────────────
const Auth = {
    getToken() {
        return localStorage.getItem('jwt_token');
    },

    setToken(token) {
        localStorage.setItem('jwt_token', token);
    },

    getUser() {
        const user = localStorage.getItem('user_data');
        return user ? JSON.parse(user) : null;
    },

    setUser(userData) {
        localStorage.setItem('user_data', JSON.stringify(userData));
    },

    isLoggedIn() {
        return !!this.getToken();
    },

    isAdmin() {
        const user = this.getUser();
        return user && user.role === 'Admin';
    },

    isLecturer() {
        const user = this.getUser();
        return user && user.role === 'Lecturer';
    },

    isStudent() {
        const user = this.getUser();
        return user && user.role === 'Student';
    },

    logout() {
        localStorage.removeItem('jwt_token');
        localStorage.removeItem('user_data');
        window.location.href = '/login.html';
    },

    /** Redirects based on auth state. */
    requireAuth() {
        if (!this.isLoggedIn()) {
            window.location.href = '/login.html';
            return false;
        }
        return true;
    },

    requireAdmin() {
        if (!this.isLoggedIn() || !this.isAdmin()) {
            window.location.href = '/login.html';
            return false;
        }
        return true;
    },

    requireLecturer() {
        if (!this.isLoggedIn() || !this.isLecturer()) {
            window.location.href = '/login.html';
            return false;
        }
        return true;
    },

    requireStudent() {
        if (!this.isLoggedIn() || !this.isStudent()) {
            window.location.href = '/login.html';
            return false;
        }
        return true;
    },

    /** Get the appropriate dashboard URL for the current user. */
    getDashboardUrl() {
        if (this.isAdmin()) return '/admin.html';
        if (this.isLecturer()) return '/lecturer.html';
        return '/dashboard.html';
    }
};

// ─── API Client ────────────────────────────────────────────────
const Api = {
    /**
     * Generic fetch wrapper with auth header injection.
     */
    async request(url, options = {}) {
        const headers = {
            'Content-Type': 'application/json',
            ...options.headers
        };

        const token = Auth.getToken();
        if (token) {
            headers['Authorization'] = `Bearer ${token}`;
        }

        try {
            const response = await fetch(`${API_BASE}${url}`, {
                ...options,
                headers
            });

            const data = await response.json();

            if (!response.ok) {
                throw { status: response.status, data };
            }

            return data;
        } catch (error) {
            if (error.data) throw error;
            throw { status: 0, data: { success: false, message: 'Network error. Please check your connection.' } };
        }
    },

    // ─── Auth Endpoints ────────────────────────────────────────
    async register(fullName, email, password, role = 'Student') {
        return this.request('/auth/register', {
            method: 'POST',
            body: JSON.stringify({ fullName, email, password, role })
        });
    },

    async login(email, password) {
        const response = await this.request('/auth/login', {
            method: 'POST',
            body: JSON.stringify({ email, password })
        });

        if (response.success && response.data) {
            Auth.setToken(response.data.token);
            Auth.setUser({
                fullName: response.data.fullName,
                email: response.data.email,
                role: response.data.role
            });
        }

        return response;
    },

    // ─── Course Endpoints ──────────────────────────────────────
    async getCourses() {
        return this.request('/courses', { method: 'GET' });
    },

    async getMyCourses() {
        return this.request('/courses/my-courses', { method: 'GET' });
    },

    async enrollCourses(courseIds) {
        return this.request('/courses/enroll', {
            method: 'POST',
            body: JSON.stringify({ courseIds })
        });
    },

    async dropCourse(courseId) {
        return this.request(`/courses/enroll/${courseId}`, { method: 'DELETE' });
    },

    async getSettings() {
        return this.request('/courses/settings', { method: 'GET' });
    },

    // ─── Lecturer Endpoints ────────────────────────────────────
    async getLecturerCourses() {
        return this.request('/lecturer/courses', { method: 'GET' });
    },

    async getCourseStudents(courseId) {
        return this.request(`/lecturer/courses/${courseId}/students`, { method: 'GET' });
    },

    // ─── Admin Endpoints ───────────────────────────────────────
    async getAllUsers() {
        return this.request('/admin/users', { method: 'GET' });
    },

    async deleteUser(userId) {
        return this.request(`/admin/users/${userId}`, { method: 'DELETE' });
    },

    async getAdminCourses() {
        return this.request('/admin/courses', { method: 'GET' });
    },

    async addCourse(courseData) {
        return this.request('/admin/courses', {
            method: 'POST',
            body: JSON.stringify(courseData)
        });
    },

    async deleteCourse(courseId) {
        return this.request(`/admin/courses/${courseId}`, { method: 'DELETE' });
    },

    async getAdminSettings() {
        return this.request('/admin/settings', { method: 'GET' });
    },

    async updateAdminSettings(settings) {
        return this.request('/admin/settings', {
            method: 'PUT',
            body: JSON.stringify(settings)
        });
    }
};

// ─── UI Helpers ────────────────────────────────────────────────
const UI = {
    /** Show an alert message inside a container element. */
    showAlert(containerId, message, type = 'danger') {
        const container = document.getElementById(containerId);
        if (!container) return;

        const icon = type === 'success' ? '✓' : '✕';
        container.innerHTML = `
            <div class="alert-custom alert-${type}-custom">
                <span>${icon}</span>
                <span>${message}</span>
            </div>
        `;

        // Auto-dismiss success after 5 seconds
        if (type === 'success') {
            setTimeout(() => { container.innerHTML = ''; }, 5000);
        }
    },

    /** Clear alert messages. */
    clearAlert(containerId) {
        const container = document.getElementById(containerId);
        if (container) container.innerHTML = '';
    },

    /** Show toast notification. */
    showToast(message, type = 'success') {
        let container = document.querySelector('.toast-container');
        if (!container) {
            container = document.createElement('div');
            container.className = 'toast-container';
            document.body.appendChild(container);
        }

        const toast = document.createElement('div');
        toast.className = `toast-custom ${type}`;
        toast.innerHTML = `<span>${type === 'success' ? '✓' : '✕'}</span><span>${message}</span>`;
        container.appendChild(toast);

        setTimeout(() => {
            toast.style.opacity = '0';
            toast.style.transform = 'translateX(100%)';
            setTimeout(() => toast.remove(), 300);
        }, 4000);
    },

    /** Set button loading state. */
    setLoading(buttonId, loading) {
        const btn = document.getElementById(buttonId);
        if (!btn) return;

        if (loading) {
            btn.disabled = true;
            btn.dataset.originalText = btn.innerHTML;
            btn.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>Processing...';
        } else {
            btn.disabled = false;
            btn.innerHTML = btn.dataset.originalText || 'Submit';
        }
    },

    /** Update navbar based on auth state. */
    updateNavbar() {
        const navAuth = document.getElementById('navAuth');
        if (!navAuth) return;

        if (Auth.isLoggedIn()) {
            const user = Auth.getUser();
            const dashUrl = Auth.getDashboardUrl();
            let roleLinks = '';

            if (Auth.isAdmin()) {
                roleLinks = '<li class="nav-item"><a class="nav-link" href="/admin.html">🛡️ Admin</a></li>';
            } else if (Auth.isLecturer()) {
                roleLinks = '<li class="nav-item"><a class="nav-link" href="/lecturer.html">📖 Lecturer</a></li>';
            }

            navAuth.innerHTML = `
                ${roleLinks}
                <li class="nav-item"><a class="nav-link" href="${dashUrl}">👤 ${user?.fullName || 'Profile'}</a></li>
                <li class="nav-item"><a class="nav-link btn-nav ms-2" href="#" onclick="Auth.logout(); return false;">Logout</a></li>
            `;
        } else {
            navAuth.innerHTML = `
                <li class="nav-item"><a class="nav-link" href="/login.html">Login</a></li>
                <li class="nav-item"><a class="nav-link btn-nav ms-2" href="/register.html">Register</a></li>
            `;
        }
    }
};

// ─── Utility ───────────────────────────────────────────────────
function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

// ─── Auto-update navbar on page load ───────────────────────────
document.addEventListener('DOMContentLoaded', () => {
    UI.updateNavbar();
});
