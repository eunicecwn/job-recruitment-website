// Real-time validation for question form
document.addEventListener('DOMContentLoaded', function () {
    console.log('DOM loaded - initializing question options...');

    // Initialize all question options visibility
    document.querySelectorAll('.question-type').forEach(select => {
        toggleQuestionOptions(select);
    });

    // Initialize character counters
    initializeCharacterCounters();

    // Initialize option handlers for existing questions
    document.querySelectorAll('.option-inputs').forEach(container => {
        initOptionHandlers(container);
    });
});

// Toggle options and max length based on question type
function toggleQuestionOptions(selectElement) {
    const card = $(selectElement).closest('.question-card');
    const optionsInput = card.find('.options-input');
    const maxLengthInput = card.find('.max-length-input');
    const questionType = selectElement.value;

    // Show/hide options for multiple choice types
    if (['MultipleChoice', 'Checkbox', 'Dropdown'].includes(questionType)) {
        optionsInput.css('display', 'block');
        maxLengthInput.css('display', 'none');
    } else if (['Text', 'TextArea'].includes(questionType)) {
        optionsInput.css('display', 'none');
        maxLengthInput.css('display', 'block');
    } else {
        optionsInput.css('display', 'none');
        maxLengthInput.css('display', 'none');
    }
}

// Initialize option handlers for a container
function initOptionHandlers(container) {
    if (!container) return;

    const optionsInput = container.closest('.options-input');
    const addButton = optionsInput.querySelector('.add-option-btn');
    const optionsData = optionsInput.querySelector('.options-data');

    // Add option button
    addButton.addEventListener('click', function () {
        const optionInputs = container;
        const newOption = document.createElement('div');
        newOption.className = 'option-input mb-2';
        newOption.innerHTML = `
            <input type="text" class="form-control option-text-input" placeholder="Option text" />
            <button type="button" class="btn btn-sm btn-danger option-remove-btn">×</button>
        `;
        optionInputs.appendChild(newOption);

        // Add input event to clear validation
        newOption.querySelector('.option-text-input').addEventListener('input', function () {
            updateOptionsData(container);
            removeOptionValidation(this);
        });

        // Add remove event
        newOption.querySelector('.option-remove-btn').addEventListener('click', function () {
            if (optionInputs.querySelectorAll('.option-input').length > 1) {
                newOption.remove();
                updateOptionsData(container);
            } else {
                alert('You must have at least one option.');
            }
        });

        updateOptionsData(container);
    });

    // Initialize existing remove buttons
    container.querySelectorAll('.option-remove-btn').forEach(btn => {
        btn.addEventListener('click', function () {
            const optionInputs = container;
            if (optionInputs.querySelectorAll('.option-input').length > 1) {
                this.closest('.option-input').remove();
                updateOptionsData(container);
            } else {
                alert('You must have at least one option.');
            }
        });
    });

    // Initialize input events for existing options
    container.querySelectorAll('.option-text-input').forEach(input => {
        input.addEventListener('input', function () {
            updateOptionsData(container);
            removeOptionValidation(this);
        });
    });

    // Initial update
    updateOptionsData(container);
}

// Update the hidden options data field
function updateOptionsData(container) {
    const options = [];
    container.querySelectorAll('.option-text-input').forEach(input => {
        if (input.value.trim() !== '') {
            options.push(input.value.trim());
        }
    });

    const optionsData = container.closest('.options-input').querySelector('.options-data');
    if (optionsData) {
        optionsData.value = options.join(',');
    }
}

// Remove validation styling when user types
function removeValidation(element) {
    element.classList.remove('is-invalid');

    // Also clear any validation messages
    const feedback = element.nextElementSibling;
    if (feedback && feedback.classList.contains('invalid-feedback')) {
        feedback.style.display = 'none';
    }
}

function removeOptionValidation(element) {
    const optionsInput = element.closest('.options-input');
    if (optionsInput) {
        optionsInput.classList.remove('is-invalid');
        const optionsData = optionsInput.querySelector('.options-data');
        if (optionsData) {
            optionsData.classList.remove('is-invalid');
        }
        const feedback = optionsInput.querySelector('.invalid-feedback');
        if (feedback) {
            feedback.style.display = 'none';
        }
    }
}

// Character counter function
function updateCharacterCounter(inputElement, counterElementId) {
    const counterElement = document.getElementById(counterElementId);
    if (counterElement) {
        const maxLength = parseInt(inputElement.getAttribute('maxlength'));
        const currentLength = inputElement.value.length;

        counterElement.textContent = `${currentLength}/${maxLength} characters`;

        // Add warning color when approaching limit
        if (currentLength > maxLength * 0.8) {
            counterElement.classList.add('text-warning');
            counterElement.classList.remove('text-muted');
        } else {
            counterElement.classList.remove('text-warning');
            counterElement.classList.add('text-muted');
        }

        if (currentLength > maxLength * 0.95) {
            counterElement.classList.add('text-danger');
            counterElement.classList.remove('text-warning', 'text-muted');
        } else {
            counterElement.classList.remove('text-danger');
        }
    }
}

// Initialize character counters
function initializeCharacterCounters() {
    // Initialize title counter
    const titleInput = document.querySelector('input[name="Name"]');
    if (titleInput) {
        updateCharacterCounter(titleInput, 'title-counter');
    }

    // Initialize description counter
    const descriptionInput = document.querySelector('textarea[name="Description"]');
    if (descriptionInput) {
        updateCharacterCounter(descriptionInput, 'description-counter');
    }
}

// Form revalidation
function revalidateForm() {
    // Remove all validation classes first
    document.querySelectorAll('.is-invalid, .input-validation-error').forEach(el => {
        el.classList.remove('is-invalid', 'input-validation-error');
    });

    document.querySelectorAll('.field-validation-error, .invalid-feedback').forEach(el => {
        el.style.display = 'none';
    });

    // Re-parse validation if jQuery Validation is available
    if (typeof $.validator !== 'undefined' && typeof $.validator.unobtrusive !== 'undefined') {
        const form = $('#questionSetForm');
        $.validator.unobtrusive.parse(form);
        form.validate().form();
    }
}