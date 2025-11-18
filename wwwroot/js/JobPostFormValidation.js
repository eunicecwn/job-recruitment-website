jQuery(function ($) {
    console.log('Document ready - initializing form validation');

    // Initialize validation
    $.validator.unobtrusive.parse("#jobForm");

    // Enhanced character counter for Title field
    function updateTitleCounter() {
        const titleInput = document.getElementById('Title');
        const counter = document.getElementById('title-counter');

        if (!titleInput || !counter) {
            console.log('Title input or counter not found');
            return;
        }

        const charCount = titleInput.value.length;
        counter.textContent = `${charCount}/120 characters`;

        // Update styling
        counter.classList.remove('text-muted', 'text-warning', 'text-danger', 'fw-bold');

        if (charCount > 120) {
            counter.classList.add('text-danger', 'fw-bold');
        } else if (charCount >= 96) {
            counter.classList.add('text-warning');
        } else {
            counter.classList.add('text-muted');
        }
    }

    // Initialize title counter
    const titleCounter = document.getElementById('title-counter');
    if (titleCounter) {
        console.log('Title counter found, initializing');
        updateTitleCounter();
        $('#Title').on('input', updateTitleCounter);
    } else {
        console.log('Title counter not found in DOM');
    }

    // COMPLETE FORM RESET FUNCTION
    function resetFormCompletely() {
        console.log('Resetting form completely');

        // 1. Reset the form values
        var form = document.getElementById('jobForm');
        if (form) {
            form.reset();
        }

        // 2. Reset jQuery validation
        var validator = $('#jobForm').data('validator');
        if (validator) {
            validator.resetForm();
        }

        // 3. MANUALLY Clear all validation error classes and messages
        // This is the key fix - manually remove all ASP.NET validation classes
        document.querySelectorAll('.is-invalid').forEach(el => {
            el.classList.remove('is-invalid');
        });

        document.querySelectorAll('.input-validation-error').forEach(el => {
            el.classList.remove('input-validation-error');
        });

        document.querySelectorAll('.field-validation-error').forEach(el => {
            el.classList.remove('field-validation-error');
            el.textContent = '';
        });

        document.querySelectorAll('.invalid-feedback').forEach(el => {
            el.textContent = '';
        });

        // 4. Hide validation summary (ASP.NET specific)
        const validationSummary = document.querySelector('[data-valmsg-summary="true"]');
        if (validationSummary) {
            validationSummary.style.display = 'none';
        }

        const alertDanger = document.querySelector('.alert-danger');
        if (alertDanger) {
            alertDanger.style.display = 'none';
        }

        // 5. Reset character counter
        if (titleCounter) {
            updateTitleCounter();
        }

        // 6. Focus on title field using modern approach
        setTimeout(function () {
            const titleInput = document.getElementById('Title');
            if (titleInput) {
                titleInput.focus();
                titleInput.select();
            }
        }, 100);

        // 7. If using a map, reset it too
        if (typeof resetMap === 'function') {
            resetMap();
        }

        console.log('Form reset completed');
    }

    // Handle custom reset button click - FIXED
    // Use event delegation to ensure the button is found
    $(document).on('click', '#customReset', function (e) {
        e.preventDefault();
        console.log('Custom reset button clicked via jQuery');
        resetFormCompletely();
    });

    // Add validation styling on changes
    $('input, select').on('change blur', function () {
        // Trigger validation for this field
        $(this).valid();
        updateValidationStyles();
    });

    // Validation styling function
    function updateValidationStyles() {
        $('.form-control, .form-select').each(function () {
            const $this = $(this);
            const $inputGroup = $this.closest('.input-group');

            if ($this.hasClass('input-validation-error')) {
                $this.addClass('is-invalid');
                if ($inputGroup.length) {
                    $inputGroup.addClass('is-invalid');
                }
            } else {
                $this.removeClass('is-invalid');
                if ($inputGroup.length) {
                    $inputGroup.removeClass('is-invalid');
                }
            }
        });
    }

    // Custom salary validation
    function validateSalaryRange() {
        const minVal = $('#MinSalary').val();
        const maxVal = $('#MaxSalary').val();

        const min = parseFloat(minVal);
        const max = parseFloat(maxVal);

        const $maxSalary = $('#MaxSalary');

        const isMinValid = minVal !== "" && !isNaN(min);
        const isMaxValid = maxVal !== "" && !isNaN(max);

        if (isMinValid && isMaxValid && max < min) {
            $maxSalary.addClass('is-invalid input-validation-error');
            $maxSalary.closest('.input-group').addClass('is-invalid');
            $maxSalary.siblings('.invalid-feedback').text("Max Salary must be greater than or equal to Min Salary.");
        } else {
            if (!$maxSalary.hasClass('input-validation-error') || (isMinValid && isMaxValid)) {
                $maxSalary.removeClass('is-invalid input-validation-error');
                $maxSalary.closest('.input-group').removeClass('is-invalid');
            }

            const defaultError = $maxSalary.attr('data-val-required') || '';
            const feedback = $maxSalary.siblings('.invalid-feedback');
            if (feedback.text() === "Max Salary must be greater than or equal to Min Salary.") {
                feedback.text(defaultError);
            }
        }
    }

    $('#MinSalary, #MaxSalary').on('input change blur', function () {
        validateSalaryRange();
    });

    // Focus on title when page loads using modern approach
    setTimeout(function () {
        const titleInput = document.getElementById('Title');
        if (titleInput) {
            titleInput.focus();
        }
    }, 100);

    console.log('Form validation initialized successfully');
});