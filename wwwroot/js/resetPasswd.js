document.addEventListener('DOMContentLoaded', function () {
    // Password toggle functionality
    const toggleButtons = document.querySelectorAll('.password-toggle');

    toggleButtons.forEach(function (button) {
        button.addEventListener('click', function () {
            const targetId = this.getAttribute('data-target');
            const field = document.getElementById(targetId);
            const icon = this.querySelector('i');

            if (field.type === 'password') {
                field.type = 'text';
                icon.className = 'fas fa-eye-slash';
            } else {
                field.type = 'password';
                icon.className = 'fas fa-eye';
            }
        });
    });

    // Password strength checker
    function checkPasswordStrength(password) {
        let strength = 0;
        let feedback = [];

        if (password.length >= 6) strength++;
        else feedback.push('At least 6 characters');

        if (/[a-z]/.test(password)) strength++;
        else feedback.push('Lowercase letter');

        if (/[A-Z]/.test(password)) strength++;
        else feedback.push('Uppercase letter');

        if (/\d/.test(password)) strength++;
        else feedback.push('Number');

        if (/[@$!%*?&]/.test(password)) strength++;
        else feedback.push('Special character');

        return { strength, feedback };
    }

    function updatePasswordStrength(password) {
        const strengthMeter = document.getElementById('passwordStrength');
        const strengthFill = document.getElementById('strengthFill');
        const strengthText = document.getElementById('strengthText');

        if (password.length === 0) {
            strengthMeter.style.display = 'none';
            return;
        }

        strengthMeter.style.display = 'block';

        const { strength, feedback } = checkPasswordStrength(password);

        // Remove all strength classes
        strengthFill.className = 'strength-fill';

        let className, text;
        switch (strength) {
            case 0:
            case 1:
                className = 'strength-weak';
                text = `Weak - Missing: ${feedback.join(', ')}`;
                break;
            case 2:
                className = 'strength-fair';
                text = `Fair - Missing: ${feedback.join(', ')}`;
                break;
            case 3:
            case 4:
                className = 'strength-good';
                text = feedback.length > 0 ? `Good - Missing: ${feedback.join(', ')}` : 'Good';
                break;
            case 5:
                className = 'strength-strong';
                text = 'Strong';
                break;
        }

        strengthFill.classList.add(className);
        strengthText.textContent = text;
        strengthText.className = `strength-text ${className.replace('strength-', '')}`;
    }

    // Password matching validation
    function checkPasswordMatch() {
        const newPassword = document.getElementById('newPassword').value;
        const confirmPassword = document.getElementById('confirmPassword').value;
        const matchIndicator = document.getElementById('passwordMatch');
        const matchText = document.getElementById('matchText');

        if (confirmPassword.length === 0) {
            matchIndicator.style.display = 'none';
            return;
        }

        matchIndicator.style.display = 'block';

        if (newPassword === confirmPassword) {
            matchText.textContent = '✓ Passwords match';
            matchText.className = 'match-text success';
        } else {
            matchText.textContent = '✗ Passwords do not match';
            matchText.className = 'match-text error';
        }
    }

    // Add event listeners
    const newPasswordField = document.getElementById('newPassword');
    const confirmPasswordField = document.getElementById('confirmPassword');

    if (newPasswordField) {
        newPasswordField.addEventListener('input', function () {
            updatePasswordStrength(this.value);
            checkPasswordMatch();
        });
    }

    if (confirmPasswordField) {
        confirmPasswordField.addEventListener('input', checkPasswordMatch);
    }

    // Form submission handler
    const form = document.getElementById('resetPasswordForm');
    if (form) {
        form.addEventListener('submit', function (e) {
            const password = document.getElementById('newPassword').value;
            const confirmPassword = document.getElementById('confirmPassword').value;

            if (password !== confirmPassword) {
                e.preventDefault();
                alert('Passwords do not match. Please try again.');
                return;
            }

            if (password.length < 6) {
                e.preventDefault();
                alert('Password must be at least 6 characters long.');
                return;
            }

            const { strength } = checkPasswordStrength(password);
            if (strength < 5) {
                if (!confirm('Your password does not meet all requirements. Do you want to continue anyway?')) {
                    e.preventDefault();
                    return;
                }
            }

            const button = document.getElementById('resetButton');
            const buttonText = document.getElementById('buttonText');
            const loadingText = document.getElementById('loadingText');

            button.disabled = true;
            buttonText.style.display = 'none';
            loadingText.style.display = 'inline';
        });
    }

    // Auto-focus password field
    const passwordField = document.getElementById('newPassword');
    if (passwordField) {
        passwordField.focus();
    }
});