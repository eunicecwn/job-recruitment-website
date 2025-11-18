// Registration Form JavaScript - Complete Fixed Version with Photo Preservation and Required Photo Validation
class RegistrationManager {
    constructor() {
        this.cropper = null;
        this.currentImageFile = null;
        this.debounceTimers = {};
        this.validationCache = {};
        this.resendCountdown = 0;
        this.resendTimer = null;
        this.formToSubmit = null;
        this.modalReady = false;
        this.hasValidPhoto = false; // NEW: Track photo validation state

        this.init();
    }

    init() {
        this.setupEventListeners();
        this.setupValidation();
        this.setupImageHandling();
        this.setupOTPHandling();
        this.restoreFormData();
        this.setupPasswordStrengthChecker();
        this.setupAvailabilityCheckers();
        this.clearSessionStorageIfNeeded();
        this.initializePhotoValidation(); // NEW: Initialize photo validation
    }

    // NEW: Initialize photo validation
    initializePhotoValidation() {
        this.updatePhotoValidationUI();
        this.updateSubmitButtonState();
    }

    setupEventListeners() {
        // Account type selection
        document.querySelectorAll('.account-type-option').forEach(option => {
            option.addEventListener('click', (e) => this.handleAccountTypeSelection(e));
        });

        // Form submission - INTERCEPT TO SHOW MODAL FIRST
        const form = document.getElementById('registrationForm');
        if (form) {
            form.addEventListener('submit', (e) => this.handleFormSubmission(e));
        }

        // Profile picture handling
        const fileInput = document.getElementById('ProfilePictureInput');
        if (fileInput) {
            fileInput.addEventListener('change', (e) => this.handleFileSelection(e));
        }

        const editBtn = document.getElementById('editBtn');
        if (editBtn) {
            editBtn.addEventListener('click', () => this.openImageEditor());
        }

        const removeBtn = document.getElementById('removeBtn');
        if (removeBtn) {
            removeBtn.addEventListener('click', () => this.removeProfilePicture());
        }

        // Password matching
        const confirmPasswordInput = document.getElementById('confirmPasswordInput');
        if (confirmPasswordInput) {
            confirmPasswordInput.addEventListener('input', () => this.checkPasswordMatch());
        }

        // Terms agreement
        const agreeTermsCheckbox = document.getElementById('agreeTerms');
        if (agreeTermsCheckbox) {
            agreeTermsCheckbox.addEventListener('change', () => this.updateSubmitButtonState());
        }

        // Real-time validation
        this.setupRealTimeValidation();

        // FIXED: Setup modal event listeners
        this.setupModalEventListeners();
    }

    // NEW: Setup modal event listeners
    setupModalEventListeners() {
        const modalElement = document.getElementById('emailOtpModal');
        if (modalElement) {
            modalElement.addEventListener('shown.bs.modal', () => {
                console.log('Modal fully shown and ready');
                this.modalReady = true;
            });

            modalElement.addEventListener('hidden.bs.modal', () => {
                console.log('Modal hidden');
                this.modalReady = false;
                this.clearOtpMessage();
            });
        }
    }

    setupRealTimeValidation() {
        const fields = [
            { id: 'username', validator: 'validateUsername' },
            { id: 'email', validator: 'validateEmail' },
            { id: 'passwordInput', validator: 'validatePassword' },
            { id: 'confirmPasswordInput', validator: 'checkPasswordMatch' },
            { id: 'FullName', validator: 'validateFullName' },
            { id: 'companyName', validator: 'validateCompanyName' }
        ];

        fields.forEach(field => {
            const element = document.getElementById(field.id);
            if (element) {
                element.addEventListener('blur', () => this[field.validator](element));
                element.addEventListener('input', () => {
                    this.debounceValidation(field.id, () => this[field.validator](element));
                });
            }
        });
    }

    setupValidation() {
        const form = document.getElementById('registrationForm');
        if (form) {
            form.noValidate = true;
        }
    }

    debounceValidation(fieldId, validationFn, delay = 500) {
        clearTimeout(this.debounceTimers[fieldId]);
        this.debounceTimers[fieldId] = setTimeout(validationFn, delay);
    }

    handleAccountTypeSelection(e) {
        const selectedType = e.currentTarget.dataset.type;

        document.querySelectorAll('.account-type-option').forEach(opt => {
            opt.classList.remove('selected');
        });
        e.currentTarget.classList.add('selected');

        const accountTypeInput = document.getElementById('AccountType');
        if (accountTypeInput) {
            accountTypeInput.value = selectedType;
        }

        const employerFields = document.getElementById('employerFields');
        const companyNameInput = document.getElementById('companyName');
        const requiredIndicators = document.querySelectorAll('.employer-required');

        if (selectedType === 'Employer') {
            if (employerFields) employerFields.style.display = 'block';
            if (companyNameInput) companyNameInput.required = true;
            requiredIndicators.forEach(indicator => indicator.style.display = 'inline');
        } else {
            if (employerFields) employerFields.style.display = 'none';
            if (companyNameInput) {
                companyNameInput.required = false;
                companyNameInput.value = '';
                // FIXED: Clear any validation errors when switching to non-employer
                this.markFieldAsValid(companyNameInput);
            }
            requiredIndicators.forEach(indicator => indicator.style.display = 'none');
        }

        this.updateSubmitButtonState();
        this.logEvent('Account type selected', { type: selectedType });
    }

    async handleFormSubmission(e) {
        e.preventDefault();

        const submitBtn = document.getElementById('submitBtn');

        try {
            if (submitBtn) {
                submitBtn.disabled = true;
                submitBtn.innerHTML = '<i class="fas fa-spinner fa-spin"></i> Validating...';
            }

            // Store photo temporarily in session storage before validation
            this.storePhotoTemporarily();

            if (!await this.performFinalValidation()) {
                this.showError('Please correct the errors below before submitting.');
                return;
            }

            this.formToSubmit = e.target;

            const email = document.getElementById('email')?.value;
            if (email) {
                this.showEmailVerificationModal(email);
            } else {
                this.showError('Email address is required.');
            }

        } catch (error) {
            console.error('Form submission error:', error);
            this.showError('An error occurred during validation. Please try again.');

        } finally {
            if (submitBtn) {
                submitBtn.disabled = false;
                submitBtn.innerHTML = '<i class="fas fa-envelope-check"></i> Create Account & Verify Email';
            }
        }
    }

    // NEW: Store photo temporarily in session storage
    storePhotoTemporarily() {
        if (this.currentImageFile && this.currentImageFile.size < 4 * 1024 * 1024) { // Only if < 4MB
            try {
                const reader = new FileReader();
                reader.onload = (e) => {
                    const photoData = {
                        data: e.target.result,
                        name: this.currentImageFile.name,
                        type: this.currentImageFile.type,
                        size: this.currentImageFile.size,
                        timestamp: Date.now()
                    };
                    sessionStorage.setItem('tempProfilePhoto', JSON.stringify(photoData));
                    console.log('Photo stored temporarily in session storage');
                };
                reader.readAsDataURL(this.currentImageFile);
            } catch (error) {
                console.error('Failed to store photo temporarily:', error);
            }
        }
    }

    // NEW: Restore photo from session storage
    restoreFromSessionStorage() {
        try {
            const photoDataStr = sessionStorage.getItem('tempProfilePhoto');
            if (photoDataStr) {
                const photoData = JSON.parse(photoDataStr);

                // Check if data is recent (within 2 hours)
                if (Date.now() - photoData.timestamp < 7200000) {
                    // Convert back to file
                    fetch(photoData.data)
                        .then(res => res.blob())
                        .then(blob => {
                            const file = new File([blob], photoData.name, {
                                type: photoData.type,
                                lastModified: photoData.timestamp
                            });

                            // Restore file input
                            const fileInput = document.getElementById('ProfilePictureInput');
                            if (fileInput) {
                                const dataTransfer = new DataTransfer();
                                dataTransfer.items.add(file);
                                fileInput.files = dataTransfer.files;

                                this.currentImageFile = file;
                                this.hasValidPhoto = true; // NEW: Set photo as valid
                                this.displayPreview(file);
                                this.showImageControls();
                                this.updateImageInfo(file);
                                this.updatePhotoValidationUI(); // NEW: Update validation UI

                                console.log('Photo restored from session storage');
                            }
                        })
                        .catch(error => {
                            console.error('Failed to restore photo from session storage:', error);
                            sessionStorage.removeItem('tempProfilePhoto');
                        });
                } else {
                    // Clean up expired data
                    sessionStorage.removeItem('tempProfilePhoto');
                    console.log('Expired photo data cleaned up');
                }
            }
        } catch (error) {
            console.error('Failed to restore photo from session storage:', error);
            sessionStorage.removeItem('tempProfilePhoto');
        }
    }

    // NEW: Restore preserved photo from server
    restorePreservedPhoto() {
        // Check if there's a preserved photo from server
        const preservedPhoto = window.registrationConfig?.preservedPhoto;
        const preservedPhotoType = window.registrationConfig?.preservedPhotoType;
        const preservedPhotoName = window.registrationConfig?.preservedPhotoName;

        if (preservedPhoto && preservedPhotoType && preservedPhotoName) {
            try {
                // Convert base64 back to blob
                const byteCharacters = atob(preservedPhoto);
                const byteNumbers = new Array(byteCharacters.length);
                for (let i = 0; i < byteCharacters.length; i++) {
                    byteNumbers[i] = byteCharacters.charCodeAt(i);
                }
                const byteArray = new Uint8Array(byteNumbers);
                const blob = new Blob([byteArray], { type: preservedPhotoType });

                // Create a File object
                const file = new File([blob], preservedPhotoName, {
                    type: preservedPhotoType,
                    lastModified: Date.now()
                });

                // Update the file input
                const fileInput = document.getElementById('ProfilePictureInput');
                if (fileInput) {
                    const dataTransfer = new DataTransfer();
                    dataTransfer.items.add(file);
                    fileInput.files = dataTransfer.files;
                }

                // Store current file and display preview
                this.currentImageFile = file;
                this.hasValidPhoto = true; // NEW: Set photo as valid
                this.displayPreview(file);
                this.showImageControls();
                this.updateImageInfo(file);
                this.updatePhotoValidationUI(); // NEW: Update validation UI

                console.log('Preserved photo restored successfully:', preservedPhotoName);

                // Clear session storage since server data takes precedence
                sessionStorage.removeItem('tempProfilePhoto');

            } catch (error) {
                console.error('Failed to restore preserved photo:', error);
                // Fallback to session storage if server restoration fails
                this.restoreFromSessionStorage();
            }
        } else {
            // Try session storage if no server preserved photo
            this.restoreFromSessionStorage();
        }
    }

    async performFinalValidation() {
        let isValid = true;
        const errors = [];

        // NEW: Validate profile picture first
        if (!this.hasValidPhoto) {
            errors.push('Profile picture is required');
            this.showPhotoError('Please upload a profile picture before submitting.');
            this.scrollToPhotoSection();
            isValid = false;
        }

        const requiredFields = [
            { id: 'username', name: 'Username' },
            { id: 'FullName', name: 'Full Name' },
            { id: 'email', name: 'Email' },
            { id: 'passwordInput', name: 'Password' },
            { id: 'confirmPasswordInput', name: 'Confirm Password' }
        ];

        const accountType = document.getElementById('AccountType')?.value;
        if (accountType === 'Employer') {
            requiredFields.push({ id: 'companyName', name: 'Company Name' });
        }

        const genderSelected = document.querySelector('input[name="Gender"]:checked');
        if (!genderSelected) {
            errors.push('Please select your gender');
            isValid = false;
        }

        const agreeTerms = document.getElementById('agreeTerms')?.checked;
        if (!agreeTerms) {
            errors.push('Please accept the terms and conditions');
            isValid = false;
        }

        requiredFields.forEach(field => {
            const element = document.getElementById(field.id);
            if (element && !element.value.trim()) {
                errors.push(`${field.name} is required`);
                this.markFieldAsInvalid(element);
                isValid = false;
            }
        });

        const password = document.getElementById('passwordInput')?.value;
        const confirmPassword = document.getElementById('confirmPasswordInput')?.value;
        if (password !== confirmPassword) {
            errors.push('Passwords do not match');
            isValid = false;
        }

        if (!isValid) {
            this.showValidationErrors(errors);
        }

        return isValid;
    }

    // FIXED: Modal timing and proper event handling
    showEmailVerificationModal(email) {
        const modal = new bootstrap.Modal(document.getElementById('emailOtpModal'));
        const displayEmail = document.getElementById('displayEmail');

        if (displayEmail) {
            displayEmail.textContent = email;
        }

        this.hideOTPSection();
        this.clearOtpMessage();
        this.modalReady = false;

        // FIXED: Use proper Bootstrap modal events
        const modalElement = document.getElementById('emailOtpModal');

        // Set up one-time event listener for when modal is fully shown
        const onModalShown = () => {
            console.log('Modal fully loaded, auto-sending OTP...');
            this.modalReady = true;

            // Small delay to ensure DOM is ready
            setTimeout(() => {
                this.sendOTP();
            }, 200);

            modalElement.removeEventListener('shown.bs.modal', onModalShown);
        };

        modalElement.addEventListener('shown.bs.modal', onModalShown);
        modal.show();
    }

    handleEmailVerificationSuccess() {
        const modal = bootstrap.Modal.getInstance(document.getElementById('emailOtpModal'));
        if (modal) {
            modal.hide();
        }

        this.showSuccessMessage('Email verified! Creating your account...');

        setTimeout(() => {
            if (this.formToSubmit) {
                this.logEvent('Form submission after email verification');

                // FIXED: Don't clone form to preserve file uploads
                // Add verification token to indicate email was verified
                const verificationInput = document.createElement('input');
                verificationInput.type = 'hidden';
                verificationInput.name = 'EmailVerificationToken';
                verificationInput.value = 'verified_' + Date.now();
                this.formToSubmit.appendChild(verificationInput);

                // FIXED: Ensure AccountType is properly set before submission
                const accountTypeInput = document.getElementById('AccountType');
                if (accountTypeInput && !accountTypeInput.value) {
                    // Get selected account type from UI
                    const selectedOption = document.querySelector('.account-type-option.selected');
                    if (selectedOption) {
                        accountTypeInput.value = selectedOption.dataset.type;
                        console.log('Account type restored:', accountTypeInput.value);
                    }
                }

                // FIXED: Ensure all form fields are properly set
                this.validateFormFieldsBeforeSubmission();

                // Clean up temporary storage since we're submitting now
                sessionStorage.removeItem('tempProfilePhoto');

                // Submit the original form directly
                this.formToSubmit.submit();
            }
        }, 1000);
    }

    continueWithoutVerification() {
        const modal = bootstrap.Modal.getInstance(document.getElementById('emailOtpModal'));
        if (modal) modal.hide();

        if (this.formToSubmit) {
            this.logEvent('Form submission without email verification');

            // FIXED: Don't clone form to preserve file uploads
            // Ensure all form fields are properly set
            this.validateFormFieldsBeforeSubmission();

            // Clean up temporary storage
            sessionStorage.removeItem('tempProfilePhoto');

            // Submit the original form directly
            this.formToSubmit.submit();
        }
    }

    // NEW: Validate form fields before submission
    validateFormFieldsBeforeSubmission() {
        // Ensure AccountType is set
        const accountTypeInput = document.getElementById('AccountType');
        const selectedOption = document.querySelector('.account-type-option.selected');

        if (accountTypeInput && selectedOption) {
            accountTypeInput.value = selectedOption.dataset.type;
            console.log('Form submission - AccountType set to:', accountTypeInput.value);
        }

        // If JobSeeker selected, clear company name to avoid validation issues
        if (accountTypeInput && accountTypeInput.value === 'JobSeeker') {
            const companyNameInput = document.getElementById('companyName');
            if (companyNameInput) {
                companyNameInput.value = '';
                companyNameInput.removeAttribute('required');
                console.log('JobSeeker selected - Company name cleared');
            }
        }

        // Log all form data for debugging
        const formData = new FormData(this.formToSubmit);
        console.log('Form data before submission:');
        for (let [key, value] of formData.entries()) {
            if (key !== 'ProfilePicture') { // Don't log file data
                console.log(`${key}: ${value}`);
            }
        }
    }

    setupImageHandling() {
        this.setupCropper();
    }

    handleFileSelection(e) {
        const file = e.target.files[0];

        // FIXED: Clear any existing errors first
        this.hidePhotoError();

        if (!file) {
            this.hasValidPhoto = false;
            this.updatePhotoValidationUI();
            return;
        }

        const validation = this.validateImageFile(file);
        if (!validation.isValid) {
            this.showPhotoError(validation.error);
            this.hasValidPhoto = false;
            e.target.value = '';
            this.updatePhotoValidationUI();
            return;
        }

        this.currentImageFile = file;
        this.hasValidPhoto = true;
        this.displayPreview(file);
        this.showImageControls();
        this.updateImageInfo(file);
        this.updateSubmitButtonState();
        this.updatePhotoValidationUI();

        // Store in session storage immediately for persistence
        this.storePhotoTemporarily();

        this.logEvent('Image file selected', {
            name: file.name,
            size: file.size,
            type: file.type
        });
    }

    validateImageFile(file) {
        const allowedTypes = ['image/jpeg', 'image/jpg', 'image/png', 'image/gif', 'image/webp'];
        const maxSize = 5 * 1024 * 1024;
        const minSize = 1024;

        if (!allowedTypes.includes(file.type.toLowerCase())) {
            return { isValid: false, error: 'Please select a valid image file (JPG, PNG, GIF, WEBP)' };
        }

        if (file.size > maxSize) {
            return { isValid: false, error: 'File size must be less than 5MB' };
        }

        if (file.size < minSize) {
            return { isValid: false, error: 'File size must be at least 1KB' };
        }

        return { isValid: true };
    }

    displayPreview(file) {
        const reader = new FileReader();
        reader.onload = (e) => {
            const previewImg = document.getElementById('profilePreviewImg');
            const defaultIcon = document.getElementById('defaultProfileIcon');

            if (previewImg && defaultIcon) {
                previewImg.src = e.target.result;
                previewImg.style.display = 'block';
                defaultIcon.style.display = 'none';
            }
        };
        reader.readAsDataURL(file);
    }

    showImageControls() {
        const editBtn = document.getElementById('editBtn');
        const removeBtn = document.getElementById('removeBtn');

        if (editBtn) editBtn.style.display = 'inline-flex';
        if (removeBtn) removeBtn.style.display = 'inline-flex';
    }

    hideImageControls() {
        const editBtn = document.getElementById('editBtn');
        const removeBtn = document.getElementById('removeBtn');

        if (editBtn) editBtn.style.display = 'none';
        if (removeBtn) removeBtn.style.display = 'none';
    }

    updateImageInfo(file) {
        const imageInfo = document.getElementById('imageInfo');
        if (imageInfo && file) {
            const sizeInMB = (file.size / (1024 * 1024)).toFixed(2);
            imageInfo.innerHTML = `
                <strong>File Info:</strong><br>
                Name: ${file.name}<br>
                Size: ${sizeInMB} MB<br>
                Type: ${file.type}
            `;
            imageInfo.style.display = 'block';
        }
    }

    removeProfilePicture() {
        const fileInput = document.getElementById('ProfilePictureInput');
        const previewImg = document.getElementById('profilePreviewImg');
        const defaultIcon = document.getElementById('defaultProfileIcon');
        const imageInfo = document.getElementById('imageInfo');

        if (fileInput) fileInput.value = '';
        if (previewImg) previewImg.style.display = 'none';
        if (defaultIcon) defaultIcon.style.display = 'flex';
        if (imageInfo) imageInfo.style.display = 'none';

        this.currentImageFile = null;
        this.hasValidPhoto = false;
        this.hideImageControls();
        this.updateSubmitButtonState();
        this.updatePhotoValidationUI();

        // Clean up temporary storage
        sessionStorage.removeItem('tempProfilePhoto');

        this.logEvent('Profile picture removed');
    }

    // FIXED: Update photo validation UI with proper error clearing
    updatePhotoValidationUI() {
        const container = document.getElementById('profileUploadContainer');
        const preview = document.getElementById('profilePreview');
        const fileInput = document.getElementById('ProfilePictureInput');

        if (!container || !preview || !fileInput) return;

        if (this.hasValidPhoto) {
            // Valid photo state
            container.classList.remove('required');
            container.classList.add('has-file');
            preview.classList.remove('error');
            preview.classList.add('success');
            fileInput.classList.remove('is-invalid');
            fileInput.classList.add('is-valid');
            this.hidePhotoError(); // FIXED: This now properly clears all errors
        } else {
            // Invalid/missing photo state
            container.classList.add('required');
            container.classList.remove('has-file');
            preview.classList.add('error');
            preview.classList.remove('success');
            fileInput.classList.add('is-invalid');
            fileInput.classList.remove('is-valid');
        }
    }

    // FIXED: Enhanced photo error showing
    showPhotoError(message) {
        const errorDiv = document.getElementById('profilePictureError');
        if (errorDiv) {
            errorDiv.textContent = message;
            errorDiv.style.display = 'block';
        }

        // Add shake animation to upload button
        const uploadBtn = document.querySelector('.upload-btn');
        if (uploadBtn) {
            uploadBtn.classList.add('error');
            setTimeout(() => {
                uploadBtn.classList.remove('error');
            }, 500);
        }
    }

    // FIXED: Enhanced photo error hiding with complete cleanup
    hidePhotoError() {
        // Hide the specific photo error div
        const errorDiv = document.getElementById('profilePictureError');
        if (errorDiv) {
            errorDiv.style.display = 'none';
            errorDiv.textContent = ''; // FIXED: Clear content too
        }

        // FIXED: Also clear any field-level errors on the file input
        const fileInput = document.getElementById('ProfilePictureInput');
        if (fileInput) {
            this.clearFieldError(fileInput);
        }

        // FIXED: Remove error class from upload button
        const uploadBtn = document.querySelector('.upload-btn');
        if (uploadBtn) {
            uploadBtn.classList.remove('error');
        }
    }

    // NEW: Scroll to photo section
    scrollToPhotoSection() {
        const photoContainer = document.getElementById('profileUploadContainer');
        if (photoContainer) {
            photoContainer.scrollIntoView({ behavior: 'smooth', block: 'center' });
        }
    }

    setupCropper() {
        const saveCropBtn = document.getElementById('saveCrop');
        const modal = document.getElementById('imageEditorModal');

        if (saveCropBtn) {
            saveCropBtn.addEventListener('click', () => this.applyCrop());
        }

        this.setupCropperControls();

        if (modal) {
            modal.addEventListener('hidden.bs.modal', () => {
                if (this.cropper) {
                    this.cropper.destroy();
                    this.cropper = null;
                }
            });
        }
    }

    setupCropperControls() {
        const zoomSlider = document.getElementById('zoomSlider');
        if (zoomSlider) {
            zoomSlider.addEventListener('input', (e) => {
                if (this.cropper) {
                    this.cropper.zoomTo(parseFloat(e.target.value));
                }
            });
        }

        const rotateLeft = document.getElementById('rotateLeft');
        const rotateRight = document.getElementById('rotateRight');

        if (rotateLeft) {
            rotateLeft.addEventListener('click', () => {
                if (this.cropper) this.cropper.rotate(-90);
            });
        }

        if (rotateRight) {
            rotateRight.addEventListener('click', () => {
                if (this.cropper) this.cropper.rotate(90);
            });
        }

        const flipH = document.getElementById('flipHorizontal');
        const flipV = document.getElementById('flipVertical');

        if (flipH) {
            flipH.addEventListener('click', () => {
                if (this.cropper) {
                    const data = this.cropper.getData();
                    this.cropper.scaleX(-data.scaleX || -1);
                }
            });
        }

        if (flipV) {
            flipV.addEventListener('click', () => {
                if (this.cropper) {
                    const data = this.cropper.getData();
                    this.cropper.scaleY(-data.scaleY || -1);
                }
            });
        }

        document.querySelectorAll('.aspect-btn').forEach(btn => {
            btn.addEventListener('click', (e) => {
                document.querySelectorAll('.aspect-btn').forEach(b => b.classList.remove('active'));
                e.target.classList.add('active');

                const aspect = e.target.dataset.aspect;
                if (this.cropper) {
                    if (aspect === 'free') {
                        this.cropper.setAspectRatio(NaN);
                    } else {
                        this.cropper.setAspectRatio(parseFloat(aspect));
                    }
                }
            });
        });

        const resetBtn = document.getElementById('resetCrop');
        if (resetBtn) {
            resetBtn.addEventListener('click', () => {
                if (this.cropper) this.cropper.reset();
            });
        }
    }

    openImageEditor() {
        if (!this.currentImageFile) return;

        const modal = new bootstrap.Modal(document.getElementById('imageEditorModal'));
        const cropperImage = document.getElementById('cropperImage');

        const reader = new FileReader();
        reader.onload = (e) => {
            cropperImage.src = e.target.result;
            modal.show();

            setTimeout(() => {
                this.cropper = new Cropper(cropperImage, {
                    aspectRatio: 1,
                    viewMode: 1,
                    minCropBoxWidth: 100,
                    minCropBoxHeight: 100,
                    ready: () => {
                        console.log('Cropper initialized');
                    }
                });
            }, 500);
        };

        reader.readAsDataURL(this.currentImageFile);
    }

    applyCrop() {
        if (!this.cropper) return;

        const canvas = this.cropper.getCroppedCanvas({
            width: 300,
            height: 300,
            fillColor: '#fff',
            imageSmoothingEnabled: false,
            imageSmoothingQuality: 'high',
        });

        canvas.toBlob((blob) => {
            const fileName = this.currentImageFile.name;
            const croppedFile = new File([blob], fileName, {
                type: this.currentImageFile.type,
                lastModified: Date.now()
            });

            const fileInput = document.getElementById('ProfilePictureInput');
            const dataTransfer = new DataTransfer();
            dataTransfer.items.add(croppedFile);
            fileInput.files = dataTransfer.files;

            const previewImg = document.getElementById('profilePreviewImg');
            if (previewImg) {
                previewImg.src = canvas.toDataURL();
            }

            this.currentImageFile = croppedFile;
            this.hasValidPhoto = true;
            this.updateImageInfo(croppedFile);
            this.updatePhotoValidationUI();

            // Update temporary storage with cropped image
            this.storePhotoTemporarily();

            const modal = bootstrap.Modal.getInstance(document.getElementById('imageEditorModal'));
            if (modal) modal.hide();

            this.logEvent('Image cropped and applied');
        }, this.currentImageFile.type, 0.9);
    }

    setupOTPHandling() {
        const sendOtpBtn = document.getElementById('sendOtpBtn');
        const verifyOtpBtn = document.getElementById('verifyOtpBtn');
        const resendOtpBtn = document.getElementById('resendOtpBtn');
        const continueBtn = document.getElementById('continueWithoutVerification');

        if (sendOtpBtn) {
            sendOtpBtn.addEventListener('click', () => this.sendOTP());
        }

        if (verifyOtpBtn) {
            verifyOtpBtn.addEventListener('click', () => this.verifyOTP());
        }

        if (resendOtpBtn) {
            resendOtpBtn.addEventListener('click', () => this.sendOTP());
        }

        if (continueBtn) {
            continueBtn.addEventListener('click', () => this.continueWithoutVerification());
        }

        const otpInput = document.getElementById('OtpCode');
        if (otpInput) {
            otpInput.addEventListener('input', (e) => {
                if (e.target.value.length === 6) {
                    this.verifyOTP();
                }
            });
        }
    }

    // FIXED: Enhanced sendOTP with proper error checking
    async sendOTP() {
        const email = document.getElementById('email')?.value;
        console.log('SendOTP called for email:', email);

        if (!email) {
            this.showOtpError('Email address not found.');
            return;
        }

        // FIXED: Check if modal is ready and elements exist
        if (!this.modalReady) {
            console.warn('Modal not ready yet, retrying...');
            setTimeout(() => this.sendOTP(), 300);
            return;
        }

        // FIXED: Verify elements exist before proceeding
        const otpSection = document.getElementById('otpSection');
        const sendSection = document.getElementById('sendOtpSection');

        if (!otpSection || !sendSection) {
            console.error('Modal elements not found:', {
                otpSection: !!otpSection,
                sendSection: !!sendSection
            });

            // Try one more time after delay
            setTimeout(() => {
                const retryOtp = document.getElementById('otpSection');
                const retrySend = document.getElementById('sendOtpSection');
                if (retryOtp && retrySend) {
                    console.log('Elements found on retry, proceeding...');
                    this.sendOTP();
                } else {
                    this.showOtpError('Modal not loaded properly. Please close and try again.');
                }
            }, 500);
            return;
        }

        try {
            this.setOtpButtonLoading(true);

            // FIXED: Better token retrieval from main form
            const mainForm = document.getElementById('registrationForm');
            const token = mainForm?.querySelector('input[name="__RequestVerificationToken"]')?.value;

            if (!token) {
                console.error('CSRF token not found');
                this.showOtpError('Security token missing. Please refresh the page.');
                return;
            }

            console.log('Making OTP request...');

            const response = await fetch('/Account/SendEmailOTP', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/x-www-form-urlencoded',
                },
                body: new URLSearchParams({
                    'email': email,
                    '__RequestVerificationToken': token
                })
            });

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}: ${response.statusText}`);
            }

            const result = await response.json();
            console.log('OTP Response:', result);

            if (result.success) {
                this.showOtpSuccess(result.message);
                this.startResendCountdown(result.resendCountdown || 60);

                // FIXED: Ensure elements still exist before showing section
                setTimeout(() => {
                    const currentOtpSection = document.getElementById('otpSection');
                    if (currentOtpSection) {
                        this.showOTPSection();
                    } else {
                        console.error('OTP section disappeared');
                    }
                }, 100);

                this.logEvent('OTP sent successfully', { email: email });
            } else {
                this.showOtpError(result.message);
                this.logEvent('OTP send failed', { email: email, error: result.message });
            }

        } catch (error) {
            console.error('Send OTP error:', error);
            this.showOtpError('Failed to send verification code. Please try again.');
        } finally {
            this.setOtpButtonLoading(false);
        }
    }

    async verifyOTP() {
        const email = document.getElementById('email')?.value;
        const otp = document.getElementById('OtpCode')?.value;

        if (!email || !otp) {
            this.showOtpError('Please enter the verification code.');
            return;
        }

        if (!/^\d{6}$/.test(otp)) {
            this.showOtpError('Please enter a valid 6-digit verification code.');
            return;
        }

        try {
            this.setVerifyButtonLoading(true);

            const mainForm = document.getElementById('registrationForm');
            const token = mainForm?.querySelector('input[name="__RequestVerificationToken"]')?.value;

            const response = await fetch('/Account/VerifyEmailOTP', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/x-www-form-urlencoded',
                },
                body: new URLSearchParams({
                    'email': email,
                    'otp': otp,
                    '__RequestVerificationToken': token || ''
                })
            });

            const result = await response.json();

            if (result.success && result.verified) {
                this.showOtpSuccess('Email verified successfully!');
                this.logEvent('Email verified successfully', { email: email });

                setTimeout(() => {
                    this.handleEmailVerificationSuccess();
                }, 1500);
            } else {
                this.showOtpError(result.message);
                this.logEvent('Email verification failed', {
                    email: email,
                    error: result.message
                });
            }

        } catch (error) {
            console.error('Verify OTP error:', error);
            this.showOtpError('Failed to verify code. Please try again.');
        } finally {
            this.setVerifyButtonLoading(false);
        }
    }

    setupPasswordStrengthChecker() {
        const passwordInput = document.getElementById('passwordInput');
        if (passwordInput) {
            passwordInput.addEventListener('input', () => this.checkPasswordStrength());
        }
    }

    checkPasswordStrength() {
        const password = document.getElementById('passwordInput')?.value || '';
        const strengthContainer = document.getElementById('passwordStrength');
        const strengthText = document.getElementById('passwordStrengthText');

        if (!strengthContainer || !strengthText) return;

        const strength = this.calculatePasswordStrength(password);

        strengthContainer.className = 'password-strength';
        strengthText.className = 'password-strength-text';

        if (strength.score === 0) {
            strengthContainer.classList.add('strength-weak');
            strengthText.classList.add('strength-weak');
            strengthText.textContent = '';
        } else if (strength.score < 3) {
            strengthContainer.classList.add('strength-weak');
            strengthText.classList.add('strength-weak');
            strengthText.textContent = 'Weak - ' + strength.feedback;
        } else if (strength.score < 4) {
            strengthContainer.classList.add('strength-medium');
            strengthText.classList.add('strength-medium');
            strengthText.textContent = 'Medium - ' + strength.feedback;
        } else {
            strengthContainer.classList.add('strength-strong');
            strengthText.classList.add('strength-strong');
            strengthText.textContent = 'Strong - Great password!';
        }
    }

    calculatePasswordStrength(password) {
        let score = 0;
        const feedback = [];

        if (password.length === 0) {
            return { score: 0, feedback: '' };
        }

        if (password.length >= 8) score++;
        else feedback.push('at least 8 characters');

        if (/[A-Z]/.test(password)) score++;
        else feedback.push('uppercase letter');

        if (/[a-z]/.test(password)) score++;
        else feedback.push('lowercase letter');

        if (/\d/.test(password)) score++;
        else feedback.push('number');

        if (/[^A-Za-z0-9]/.test(password)) score++;
        else feedback.push('special character');

        const feedbackText = feedback.length > 0
            ? `Add ${feedback.join(', ')}`
            : '';

        return { score, feedback: feedbackText };
    }

    checkPasswordMatch() {
        const password = document.getElementById('passwordInput')?.value;
        const confirmPassword = document.getElementById('confirmPasswordInput')?.value;
        const matchIndicator = document.getElementById('passwordMatch');

        if (!matchIndicator) return;

        if (confirmPassword === '') {
            matchIndicator.textContent = '';
            return;
        }

        if (password === confirmPassword) {
            matchIndicator.textContent = 'Passwords match ✓';
            matchIndicator.className = 'form-text text-success';
            this.markFieldAsValid(document.getElementById('confirmPasswordInput'));
        } else {
            matchIndicator.textContent = 'Passwords do not match ✗';
            matchIndicator.className = 'form-text text-danger';
            this.markFieldAsInvalid(document.getElementById('confirmPasswordInput'));
        }
    }

    setupAvailabilityCheckers() {
        const usernameInput = document.getElementById('username');
        const emailInput = document.getElementById('email');

        if (usernameInput) {
            usernameInput.addEventListener('blur', () => this.checkUsernameAvailability());
            usernameInput.addEventListener('input', () => {
                this.debounceValidation('username_availability', () => this.checkUsernameAvailability(), 1000);
            });
        }

        if (emailInput) {
            emailInput.addEventListener('blur', () => this.checkEmailAvailability());
            emailInput.addEventListener('input', () => {
                this.debounceValidation('email_availability', () => this.checkEmailAvailability(), 1000);
            });
        }
    }

    async checkUsernameAvailability() {
        const usernameInput = document.getElementById('username');
        const availabilityDiv = document.getElementById('usernameAvailability');

        if (!usernameInput || !availabilityDiv) return;

        const username = usernameInput.value.trim();
        if (username.length < 4) {
            availabilityDiv.innerHTML = '';
            return;
        }

        if (this.validationCache[`username_${username}`]) {
            const cached = this.validationCache[`username_${username}`];
            this.displayAvailabilityResult(availabilityDiv, cached.available, 'Username', cached.reason);
            return;
        }

        try {
            availabilityDiv.innerHTML = '<span class="checking"><div class="spinner-border" role="status"></div> Checking...</span>';

            const response = await fetch(`/Account/IsUsernameAvailable?username=${encodeURIComponent(username)}`);
            const result = await response.json();

            this.validationCache[`username_${username}`] = result;
            this.displayAvailabilityResult(availabilityDiv, result.available, 'Username', result.reason);

            if (result.available) {
                this.markFieldAsValid(usernameInput);
            } else {
                this.markFieldAsInvalid(usernameInput);
            }

        } catch (error) {
            console.error('Username availability check failed:', error);
            availabilityDiv.innerHTML = '<span class="text-warning">Could not check availability</span>';
        }
    }

    async checkEmailAvailability() {
        const emailInput = document.getElementById('email');
        const availabilityDiv = document.getElementById('emailAvailability');

        if (!emailInput || !availabilityDiv) return;

        const email = emailInput.value.trim();
        if (!this.isValidEmailFormat(email)) {
            availabilityDiv.innerHTML = '';
            return;
        }

        if (this.validationCache[`email_${email}`]) {
            const cached = this.validationCache[`email_${email}`];
            this.displayAvailabilityResult(availabilityDiv, cached.available, 'Email', cached.reason);
            return;
        }

        try {
            availabilityDiv.innerHTML = '<span class="checking"><div class="spinner-border" role="status"></div> Checking...</span>';

            const response = await fetch(`/Account/IsEmailAvailable?email=${encodeURIComponent(email)}`);
            const result = await response.json();

            this.validationCache[`email_${email}`] = result;
            this.displayAvailabilityResult(availabilityDiv, result.available, 'Email', result.reason);

            if (result.available) {
                this.markFieldAsValid(emailInput);
            } else {
                this.markFieldAsInvalid(emailInput);
            }

        } catch (error) {
            console.error('Email availability check failed:', error);
            availabilityDiv.innerHTML = '<span class="text-warning">Could not check availability</span>';
        }
    }

    displayAvailabilityResult(container, available, fieldName, reason) {
        if (available) {
            container.innerHTML = `<span class="text-success"><i class="fas fa-check"></i> ${fieldName} is available</span>`;
        } else {
            const reasonText = reason ? ` (${reason})` : '';
            container.innerHTML = `<span class="text-danger"><i class="fas fa-times"></i> ${fieldName} not available${reasonText}</span>`;
        }
    }

    validateUsername(element, showError = true) {
        const username = element.value.trim();
        const errors = [];

        if (username.length < 4) errors.push('Username must be at least 4 characters');
        if (username.length > 50) errors.push('Username must not exceed 50 characters');
        if (!/^[a-zA-Z0-9_]+$/.test(username)) errors.push('Username can only contain letters, numbers, and underscores');

        const isValid = errors.length === 0;

        if (showError) {
            if (isValid) {
                this.markFieldAsValid(element);
            } else {
                this.markFieldAsInvalid(element);
                this.showFieldError(element, errors[0]);
            }
        }

        return isValid;
    }

    validateEmail(element, showError = true) {
        const email = element.value.trim();
        const isValid = this.isValidEmailFormat(email);

        if (showError) {
            if (isValid) {
                this.markFieldAsValid(element);
            } else {
                this.markFieldAsInvalid(element);
                this.showFieldError(element, 'Please enter a valid email address');
            }
        }

        return isValid;
    }

    validatePassword(element, showError = true) {
        const password = element.value;
        const strength = this.calculatePasswordStrength(password);
        const isValid = strength.score >= 2;

        if (showError) {
            if (isValid) {
                this.markFieldAsValid(element);
            } else {
                this.markFieldAsInvalid(element);
                if (password.length > 0) {
                    this.showFieldError(element, 'Password is too weak. ' + strength.feedback);
                }
            }
        }

        return isValid;
    }

    validateFullName(element, showError = true) {
        const fullName = element.value.trim();
        const errors = [];

        if (fullName.length < 2) errors.push('Full name must be at least 2 characters');
        if (fullName.length > 100) errors.push('Full name must not exceed 100 characters');
        if (!/^[a-zA-ZÀ-ÿĀ-žА-я\s\-\.\']+$/.test(fullName)) {
            errors.push('Full name can only contain letters, spaces, hyphens, dots, and apostrophes');
        }

        const isValid = errors.length === 0;

        if (showError) {
            if (isValid) {
                this.markFieldAsValid(element);
            } else {
                this.markFieldAsInvalid(element);
                this.showFieldError(element, errors[0]);
            }
        }

        return isValid;
    }

    validateCompanyName(element, showError = true) {
        const accountType = document.getElementById('AccountType')?.value;
        if (accountType !== 'Employer') {
            // FIXED: Clear any existing errors if not employer
            this.markFieldAsValid(element);
            return true;
        }

        const companyName = element.value.trim();
        const errors = [];

        if (companyName.length < 2) errors.push('Company name must be at least 2 characters');
        if (companyName.length > 100) errors.push('Company name must not exceed 100 characters');
        if (!/^[a-zA-ZÀ-ÿĀ-žА-я0-9\s\-\.\,\&\']+$/.test(companyName)) {
            errors.push('Company name contains invalid characters');
        }

        const isValid = errors.length === 0;

        if (showError) {
            if (isValid) {
                this.markFieldAsValid(element);
            } else {
                this.markFieldAsInvalid(element);
                this.showFieldError(element, errors[0]);
            }
        }

        return isValid;
    }

    isValidEmailFormat(email) {
        return /^[a-zA-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*$/.test(email);
    }

    // FIXED: Enhanced markFieldAsValid to clear errors automatically
    markFieldAsValid(element) {
        if (element) {
            element.classList.remove('is-invalid');
            element.classList.add('is-valid');
            this.clearFieldError(element); // FIXED: Clear error when field becomes valid
        }
    }

    markFieldAsInvalid(element) {
        if (element) {
            element.classList.remove('is-valid');
            element.classList.add('is-invalid');
        }
    }

    showFieldError(element, message) {
        let errorSpan = element.parentNode.querySelector('.field-validation-error');
        if (!errorSpan) {
            errorSpan = document.createElement('span');
            errorSpan.className = 'text-danger field-validation-error';
            element.parentNode.appendChild(errorSpan);
        }
        errorSpan.textContent = message;
    }

    // FIXED: Enhanced clearFieldError to be more thorough
    clearFieldError(element) {
        if (!element) return;

        // Clear field-validation-error spans
        const errorSpan = element.parentNode.querySelector('.field-validation-error');
        if (errorSpan) {
            errorSpan.remove();
        }

        // FIXED: Also clear any Bootstrap invalid-feedback divs
        const invalidFeedback = element.parentNode.querySelector('.invalid-feedback');
        if (invalidFeedback) {
            invalidFeedback.style.display = 'none';
            invalidFeedback.textContent = '';
        }

        // FIXED: Clear validation summary if this was the last error
        setTimeout(() => {
            this.updateValidationSummary();
        }, 100);
    }

    // NEW: Add method to update validation summary
    updateValidationSummary() {
        const errorContainer = document.querySelector('.validation-summary');
        if (!errorContainer) return;

        // Check if there are any remaining field errors
        const remainingErrors = document.querySelectorAll('.field-validation-error:not([style*="display: none"]), .is-invalid');

        if (remainingErrors.length === 0) {
            errorContainer.style.display = 'none';
            errorContainer.innerHTML = '';
        }
    }

    showError(message) {
        alert(message);
    }

    showSuccessMessage(message) {
        const alert = document.createElement('div');
        alert.className = 'alert alert-success position-fixed top-0 start-50 translate-middle-x mt-3';
        alert.style.zIndex = '9999';
        alert.textContent = message;
        document.body.appendChild(alert);

        setTimeout(() => {
            alert.remove();
        }, 3000);
    }

    showValidationErrors(errors) {
        const errorContainer = document.querySelector('.validation-summary');
        if (errorContainer && errors.length > 0) {
            errorContainer.innerHTML = `
                <ul>
                    ${errors.map(error => `<li>${error}</li>`).join('')}
                </ul>
            `;
            errorContainer.style.display = 'block';
            errorContainer.scrollIntoView({ behavior: 'smooth' });
        }
    }

    // FIXED: Enhanced OTP section display with better error handling
    showOTPSection() {
        console.log('Attempting to show OTP section...');

        const otpSection = document.getElementById('otpSection');
        const sendSection = document.getElementById('sendOtpSection');

        console.log('Elements found:', {
            otpSection: !!otpSection,
            sendSection: !!sendSection
        });

        if (!otpSection) {
            console.error('OTP section element not found!');
            this.showOtpError('Interface error. Please close and reopen the verification dialog.');
            return;
        }

        if (!sendSection) {
            console.error('Send section element not found!');
            return;
        }

        // Show OTP section and hide send section
        otpSection.style.display = 'block';
        sendSection.style.display = 'none';

        console.log('OTP section displayed successfully');

        // Focus on OTP input
        setTimeout(() => {
            const otpInput = document.getElementById('OtpCode');
            if (otpInput) {
                otpInput.focus();
                console.log('OTP input focused');
            }
        }, 150);
    }

    hideOTPSection() {
        const otpSection = document.getElementById('otpSection');
        const sendSection = document.getElementById('sendOtpSection');
        if (otpSection) otpSection.style.display = 'none';
        if (sendSection) sendSection.style.display = 'block';
    }

    showOtpSuccess(message) {
        const messageDiv = document.getElementById('otpMessage');
        if (messageDiv) {
            messageDiv.textContent = message;
            messageDiv.className = 'alert alert-success';
            messageDiv.style.display = 'block';
        }
    }

    showOtpError(message) {
        const messageDiv = document.getElementById('otpMessage');
        if (messageDiv) {
            messageDiv.textContent = message;
            messageDiv.className = 'alert alert-danger';
            messageDiv.style.display = 'block';
        }
    }

    clearOtpMessage() {
        const messageDiv = document.getElementById('otpMessage');
        if (messageDiv) {
            messageDiv.style.display = 'none';
            messageDiv.textContent = '';
        }
    }

    setOtpButtonLoading(loading) {
        const btn = document.getElementById('sendOtpBtn');
        if (btn) {
            btn.disabled = loading;
            btn.innerHTML = loading
                ? '<i class="fas fa-spinner fa-spin"></i> Sending...'
                : '<i class="fas fa-envelope"></i> Send Verification Code';
        }
    }

    setVerifyButtonLoading(loading) {
        const btn = document.getElementById('verifyOtpBtn');
        if (btn) {
            btn.disabled = loading;
            btn.innerHTML = loading
                ? '<i class="fas fa-spinner fa-spin"></i> Verifying...'
                : '<i class="fas fa-check"></i> Verify Code';
        }
    }

    startResendCountdown(seconds) {
        this.resendCountdown = seconds;
        const resendBtn = document.getElementById('resendOtpBtn');

        if (resendBtn) {
            resendBtn.disabled = true;

            this.resendTimer = setInterval(() => {
                if (this.resendCountdown <= 0) {
                    clearInterval(this.resendTimer);
                    resendBtn.disabled = false;
                    resendBtn.textContent = 'Resend Code';
                } else {
                    resendBtn.textContent = `Resend in ${this.resendCountdown}s`;
                    this.resendCountdown--;
                }
            }, 1000);
        }
    }

    updateSubmitButtonState() {
        const submitBtn = document.getElementById('submitBtn');
        if (!submitBtn) return;

        const accountType = document.getElementById('AccountType')?.value;
        const agreeTerms = document.getElementById('agreeTerms')?.checked;

        // NEW: Include photo validation in submit button state
        const canSubmit = accountType && agreeTerms && this.hasValidPhoto;
        submitBtn.disabled = !canSubmit;

        // NEW: Update button text to reflect photo requirement
        if (!this.hasValidPhoto) {
            submitBtn.title = 'Please upload a profile picture first';
        } else if (!accountType) {
            submitBtn.title = 'Please select an account type';
        } else if (!agreeTerms) {
            submitBtn.title = 'Please accept the terms and conditions';
        } else {
            submitBtn.title = '';
        }
    }

    restoreFormData() {
        if (window.registrationConfig?.preserveFormData) {
            try {
                const formData = JSON.parse(window.registrationConfig.modelJson || '{}');
                this.populateFormFromData(formData);

                // NEW: Restore preserved photo from server first, fallback to session storage
                this.restorePreservedPhoto();

            } catch (error) {
                console.error('Failed to restore form data:', error);
                // Fallback to session storage only
                this.restoreFromSessionStorage();
            }
        } else {
            // If no server preserved data, try session storage
            this.restoreFromSessionStorage();
        }
    }

    populateFormFromData(data) {
        Object.keys(data).forEach(key => {
            const element = document.getElementById(key) || document.querySelector(`[name="${key}"]`);
            if (element && data[key]) {
                if (element.type === 'checkbox') {
                    element.checked = data[key];
                } else if (element.type === 'radio') {
                    const radioBtn = document.querySelector(`input[name="${key}"][value="${data[key]}"]`);
                    if (radioBtn) radioBtn.checked = true;
                } else {
                    element.value = data[key];
                }
            }
        });
    }

    clearSessionStorageIfNeeded() {
        if (window.registrationConfig?.clearSessionStorage) {
            sessionStorage.clear();
            console.log('Session storage cleared after successful registration');
        }
    }

    logEvent(event, data = {}) {
        if (window.console && window.console.log) {
            console.log(`[Registration] ${event}:`, data);
        }
    }

    destroy() {
        Object.values(this.debounceTimers).forEach(timer => clearTimeout(timer));
        if (this.resendTimer) clearInterval(this.resendTimer);

        if (this.cropper) {
            this.cropper.destroy();
            this.cropper = null;
        }

        // Clean up session storage
        sessionStorage.removeItem('tempProfilePhoto');

        console.log('Registration manager destroyed');
    }
}

document.addEventListener('DOMContentLoaded', function () {
    window.registrationManager = new RegistrationManager();
    console.log('Registration form enhanced and initialized with required photo validation');
});

window.addEventListener('beforeunload', function () {
    if (window.registrationManager) {
        window.registrationManager.destroy();
    }
});