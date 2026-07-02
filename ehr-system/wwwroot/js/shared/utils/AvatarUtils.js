/**
 * AvatarUtils - Profile picture and avatar rendering utilities
 * Renders avatar elements with profile picture support and initials fallback.
 *
 * Usage:
 *   AvatarUtils.renderPatientAvatar({ patientId: 1, name: 'John Doe', hasProfilePicture: true, size: 'md' });
 *   AvatarUtils.renderProviderAvatar({ providerId: 1, name: 'Dr. Smith', color: '#2196F3', hasProfilePicture: false, size: 'lg' });
 */
const AvatarUtils = {

    // Pending photos for unsaved entities (client-side preview before save)
    _pendingFiles: {},       // { patient: File, provider: File }
    _pendingBlobUrls: {},    // { patient: 'blob:...', provider: 'blob:...' }

    /**
     * Render a patient avatar with profile picture or initials fallback
     * @param {Object} options
     * @param {number} options.patientId - Patient ID
     * @param {string} options.name - Full name for initials
     * @param {boolean} options.hasProfilePicture - Whether patient has a profile picture
     * @param {string} [options.size='md'] - Size: 'sm' (32px), 'md' (40px), 'lg' (60px), 'xl' (80px)
     * @param {string} [options.cssClass=''] - Additional CSS classes
     * @returns {string} HTML string
     */
    renderPatientAvatar({ patientId, name, hasProfilePicture, size = 'md', cssClass = '' }) {
        const initials = this.getInitials(name);
        const sizeClass = `avatar-${size}`;

        if (hasProfilePicture && patientId) {
            return `<div class="profile-avatar ${sizeClass} ${cssClass}" data-entity-type="patient" data-entity-id="${patientId}">
                <img src="/api/patients/${patientId}/profile-picture"
                     alt="${StringUtils.escape(name)}"
                     class="profile-avatar-img"
                     onerror="AvatarUtils.onImageError(this)"
                     loading="lazy">
                <span class="profile-avatar-initials" style="display:none;">${StringUtils.escape(initials)}</span>
            </div>`;
        }

        return `<div class="profile-avatar ${sizeClass} avatar-initials-patient ${cssClass}" data-entity-type="patient" data-entity-id="${patientId || ''}">
            <span class="profile-avatar-initials">${StringUtils.escape(initials)}</span>
        </div>`;
    },

    /**
     * Render a provider avatar with profile picture or initials fallback
     * @param {Object} options
     * @param {number} options.providerId - Provider ID
     * @param {string} options.name - Full name for initials
     * @param {string} [options.color='#2196F3'] - Provider color for initials background
     * @param {boolean} options.hasProfilePicture - Whether provider has a profile picture
     * @param {string} [options.size='md'] - Size: 'sm' (32px), 'md' (40px), 'lg' (60px), 'xl' (80px)
     * @param {string} [options.cssClass=''] - Additional CSS classes
     * @returns {string} HTML string
     */
    renderProviderAvatar({ providerId, name, color = '#2196F3', hasProfilePicture, size = 'md', cssClass = '' }) {
        const initials = this.getInitials(name);
        const sizeClass = `avatar-${size}`;

        if (hasProfilePicture && providerId) {
            return `<div class="profile-avatar ${sizeClass} ${cssClass}" data-entity-type="provider" data-entity-id="${providerId}">
                <img src="/api/providers/${providerId}/profile-picture"
                     alt="${StringUtils.escape(name)}"
                     class="profile-avatar-img"
                     onerror="AvatarUtils.onImageError(this)"
                     loading="lazy">
                <span class="profile-avatar-initials" style="display:none; background: ${color};">${StringUtils.escape(initials)}</span>
            </div>`;
        }

        return `<div class="profile-avatar ${sizeClass} avatar-initials-provider ${cssClass}" style="background: ${color};" data-entity-type="provider" data-entity-id="${providerId || ''}">
            <span class="profile-avatar-initials">${StringUtils.escape(initials)}</span>
        </div>`;
    },

    /**
     * Render a user avatar for the sidebar (current logged-in user)
     * @param {Object} options
     * @param {number} [options.providerId] - Provider ID if user is a provider
     * @param {string} options.name - Full name for initials
     * @param {boolean} options.hasProfilePicture - Whether user has a profile picture
     * @returns {string} HTML string (inner content for user-avatar div)
     */
    renderUserAvatarContent({ providerId, name, hasProfilePicture }) {
        if (hasProfilePicture && providerId) {
            const initials = this.getInitials(name);
            return `<img src="/api/providers/${providerId}/profile-picture"
                         alt="${StringUtils.escape(name)}"
                         class="profile-avatar-img"
                         onerror="AvatarUtils.onImageError(this)"
                         loading="lazy">
                    <span class="profile-avatar-initials" style="display:none;">${StringUtils.escape(initials)}</span>`;
        }

        return (name || 'U').charAt(0).toUpperCase();
    },

    /**
     * Render an editable avatar with Instagram/Facebook-style hover overlay
     * @param {Object} options
     * @param {string} options.entityType - 'patient' or 'provider'
     * @param {number} [options.entityId] - Entity ID (null for unsaved entities)
     * @param {string} options.name - Full name for initials
     * @param {boolean} options.hasProfilePicture - Whether entity has a profile picture
     * @param {string} [options.color='#2196F3'] - Provider color (providers only)
     * @param {string} [options.size='xl'] - Size class
     * @param {string} [options.cssClass=''] - Additional CSS classes for the trigger wrapper
     * @returns {string} HTML string
     */
    renderEditableAvatar({ entityType, entityId, name, hasProfilePicture, color = '#2196F3', size = 'xl', cssClass = '' }) {
        const isNew = !entityId;
        const hasPending = isNew && !!this._pendingBlobUrls[entityType];
        const showPhoto = hasProfilePicture || hasPending;
        const overlayText = showPhoto ? 'Update' : 'Add Photo';
        const isPlaceholder = !name || name === '?';

        // Render the inner avatar using existing methods
        let innerAvatar;
        if (hasPending) {
            // Show client-side preview from blob URL
            const sizeClass = `avatar-${size}`;
            innerAvatar = `<div class="profile-avatar ${sizeClass}" data-entity-type="${entityType}" data-entity-id="">
                <img src="${this._pendingBlobUrls[entityType]}" alt="Preview" class="profile-avatar-img" loading="lazy">
                <span class="profile-avatar-initials" style="display:none;"><i class="bi bi-person-fill"></i></span>
            </div>`;
        } else if (isPlaceholder) {
            // New entity placeholder — show person icon instead of "?" for clarity
            const sizeClass = `avatar-${size}`;
            const bgClass = entityType === 'patient' ? 'avatar-initials-patient' : 'avatar-initials-provider';
            const bgStyle = entityType === 'provider' ? `style="background: ${color};"` : '';
            innerAvatar = `<div class="profile-avatar ${sizeClass} ${bgClass}" ${bgStyle} data-entity-type="${entityType}" data-entity-id="">
                <span class="profile-avatar-initials avatar-placeholder-icon"><i class="bi bi-person-fill"></i></span>
            </div>`;
        } else if (entityType === 'patient') {
            innerAvatar = this.renderPatientAvatar({ patientId: entityId, name, hasProfilePicture, size, cssClass: '' });
        } else {
            innerAvatar = this.renderProviderAvatar({ providerId: entityId, name, color, hasProfilePicture, size, cssClass: '' });
        }

        // Build remove button (if has photo or pending preview)
        const removeBtn = showPhoto
            ? `<button type="button" class="avatar-remove-btn" data-avatar-remove title="Remove photo"><i class="bi bi-x-lg"></i></button>`
            : '';

        // Build the editable wrapper — always interactive (no disabled state)
        const html = `
            <div class="avatar-upload-trigger ${cssClass}"
                 data-avatar-editable
                 data-entity-type="${entityType}"
                 data-entity-id="${entityId || ''}"
                 data-provider-color="${color}"
                 title="Click to ${showPhoto ? 'change' : 'add'} photo">
                ${innerAvatar}
                <div class="avatar-overlay">
                    <i class="bi bi-camera-fill"></i>
                    <span class="avatar-overlay-text">${overlayText}</span>
                </div>
                <div class="avatar-edit-badge"><i class="bi bi-camera-fill"></i></div>
                ${removeBtn}
                <input type="file" class="avatar-file-input" accept=".jpg,.jpeg,.png,.webp" data-entity-type="${entityType}">
            </div>
        `;

        return html;
    },

    /**
     * Handle editable avatar upload — called from event delegation
     * @param {HTMLElement} trigger - The .avatar-upload-trigger element
     * @param {File} file - The selected file
     */
    async _handleEditableUpload(trigger, file) {
        if (!file) return;

        const entityType = trigger.dataset.entityType;
        const entityId = trigger.dataset.entityId;

        // Client-side validation (applies to both new and existing entities)
        const maxSize = 5 * 1024 * 1024;
        if (file.size > maxSize) {
            App.toast('error', '', 'File size exceeds 5MB limit');
            return;
        }

        const allowedTypes = ['image/jpeg', 'image/png', 'image/webp'];
        if (!allowedTypes.includes(file.type)) {
            App.toast('error', '', 'Only JPG, PNG, and WebP images are allowed');
            return;
        }

        // NEW ENTITY: Store file locally and show client-side preview
        if (!entityId) {
            this._setPendingPhoto(entityType, file);
            this._showPendingPreview(trigger, file, entityType);
            return;
        }

        // EXISTING ENTITY: Upload to server immediately
        const loadingEl = document.createElement('div');
        loadingEl.className = 'avatar-loading-overlay';
        loadingEl.innerHTML = '<div class="spinner-border" role="status"><span class="visually-hidden">Uploading...</span></div>';
        trigger.appendChild(loadingEl);

        const apiBase = entityType === 'patient' ? '/api/patients' : '/api/providers';
        const formData = new FormData();
        formData.append('file', file);

        try {
            const response = await fetch(`${apiBase}/${entityId}/profile-picture`, {
                method: 'POST',
                headers: { 'Authorization': `Bearer ${localStorage.getItem('authToken')}` },
                body: formData
            });

            if (!response.ok) {
                const err = await response.json().catch(() => ({}));
                throw new Error(err.message || 'Upload failed');
            }

            const result = await response.json();
            if (result.HasProfilePicture || result.hasProfilePicture) {
                App.toast('success', '', 'Profile picture uploaded successfully');

                // Update the inner avatar to show the new picture
                const innerAvatar = trigger.querySelector('.profile-avatar');
                if (innerAvatar) {
                    const timestamp = Date.now();
                    innerAvatar.className = `profile-avatar avatar-${trigger.querySelector('.profile-avatar')?.className.match(/avatar-(sm|md|lg|xl)/)?.[1] || 'xl'}`;
                    innerAvatar.innerHTML = `
                        <img src="${apiBase}/${entityId}/profile-picture?v=${timestamp}" alt="Profile" class="profile-avatar-img" onerror="AvatarUtils.onImageError(this)" loading="lazy">
                        <span class="profile-avatar-initials" style="display:none;">${innerAvatar.querySelector('.profile-avatar-initials')?.textContent || '?'}</span>
                    `;
                }

                // Add remove button if not present
                if (!trigger.querySelector('.avatar-remove-btn')) {
                    const removeBtn = document.createElement('button');
                    removeBtn.type = 'button';
                    removeBtn.className = 'avatar-remove-btn';
                    removeBtn.dataset.avatarRemove = '';
                    removeBtn.title = 'Remove photo';
                    removeBtn.innerHTML = '<i class="bi bi-x-lg"></i>';
                    trigger.insertBefore(removeBtn, trigger.querySelector('.avatar-file-input'));
                }

                // Update overlay text
                const overlayText = trigger.querySelector('.avatar-overlay-text');
                if (overlayText) overlayText.textContent = 'Update';

                // Success flash animation
                trigger.classList.add('avatar-upload-success');
                setTimeout(() => trigger.classList.remove('avatar-upload-success'), 1000);
            } else {
                App.toast('error', '', result.Message || result.message || 'Upload failed');
            }
        } catch (error) {
            console.error('[AvatarUtils] Editable upload error:', error);
            App.toast('error', '', error.message || 'Failed to upload profile picture');
        } finally {
            // Remove loading spinner and clear file input
            loadingEl.remove();
            const fileInput = trigger.querySelector('.avatar-file-input');
            if (fileInput) fileInput.value = '';
        }
    },

    /**
     * Handle editable avatar removal — called from event delegation
     * @param {HTMLElement} trigger - The .avatar-upload-trigger element
     */
    async _handleEditableRemove(trigger) {
        const entityType = trigger.dataset.entityType;
        const entityId = trigger.dataset.entityId;

        // NEW ENTITY: Just clear the pending preview (no API call)
        if (!entityId) {
            this.clearPendingPhoto(entityType);
            this._revertToInitials(trigger, entityType);
            return;
        }

        // EXISTING ENTITY: Delete from server
        const apiBase = entityType === 'patient' ? '/api/patients' : '/api/providers';

        try {
            const response = await fetch(`${apiBase}/${entityId}/profile-picture`, {
                method: 'DELETE',
                headers: {
                    'Authorization': `Bearer ${localStorage.getItem('authToken')}`,
                    'Content-Type': 'application/json'
                }
            });

            if (!response.ok) throw new Error('Delete failed');

            App.toast('success', '', 'Profile picture removed');
            this._revertToInitials(trigger, entityType);
        } catch (error) {
            console.error('[AvatarUtils] Editable remove error:', error);
            App.toast('error', '', 'Failed to remove profile picture');
        }
    },

    /**
     * Revert avatar trigger to initials (shared by remove + pending clear)
     * @param {HTMLElement} trigger - The .avatar-upload-trigger element
     * @param {string} entityType - 'patient' or 'provider'
     */
    _revertToInitials(trigger, entityType) {
        const innerAvatar = trigger.querySelector('.profile-avatar');
        if (innerAvatar) {
            const initialsEl = innerAvatar.querySelector('.profile-avatar-initials');
            const rawText = initialsEl?.textContent?.trim() || '?';
            const isPatient = entityType === 'patient';
            const sizeMatch = innerAvatar.className.match(/avatar-(sm|md|lg|xl)/);
            const sizeClass = sizeMatch ? `avatar-${sizeMatch[1]}` : 'avatar-xl';

            if (isPatient) {
                innerAvatar.className = `profile-avatar ${sizeClass} avatar-initials-patient`;
                innerAvatar.style = '';
            } else {
                const color = trigger.dataset.providerColor || '#2196F3';
                innerAvatar.className = `profile-avatar ${sizeClass} avatar-initials-provider`;
                innerAvatar.style.background = color;
            }

            // If this is a placeholder (new entity), show person icon instead of "?"
            const entityId = trigger.dataset.entityId;
            if (!entityId || rawText === '?') {
                innerAvatar.innerHTML = `<span class="profile-avatar-initials avatar-placeholder-icon"><i class="bi bi-person-fill"></i></span>`;
            } else {
                innerAvatar.innerHTML = `<span class="profile-avatar-initials">${rawText}</span>`;
            }
        }

        // Remove the remove button
        const removeBtn = trigger.querySelector('.avatar-remove-btn');
        if (removeBtn) removeBtn.remove();

        // Update overlay text
        const overlayText = trigger.querySelector('.avatar-overlay-text');
        if (overlayText) overlayText.textContent = 'Add Photo';
    },

    /**
     * Handle image load error — hide img and show initials fallback
     * @param {HTMLImageElement} img - The img element that failed
     */
    onImageError(img) {
        img.style.display = 'none';
        const fallback = img.parentElement?.querySelector('.profile-avatar-initials');
        if (fallback) {
            fallback.style.display = '';
        }
    },

    /**
     * Get initials from a name (up to 2 characters)
     * @param {string} name - Full name
     * @returns {string} Initials (e.g., "JD" for "John Doe")
     */
    getInitials(name) {
        if (!name) return '?';
        const parts = name.trim().split(/\s+/);
        if (parts.length === 1) return parts[0].charAt(0).toUpperCase();
        return (parts[0].charAt(0) + parts[parts.length - 1].charAt(0)).toUpperCase();
    },

    // ============================================
    // Pending Photo Methods (for new unsaved entities)
    // ============================================

    /**
     * Store a pending photo file for later upload (after entity is saved)
     * @param {string} entityType - 'patient' or 'provider'
     * @param {File} file - The selected image file
     */
    _setPendingPhoto(entityType, file) {
        // Revoke previous blob URL to avoid memory leaks
        if (this._pendingBlobUrls[entityType]) {
            URL.revokeObjectURL(this._pendingBlobUrls[entityType]);
        }
        this._pendingFiles[entityType] = file;
        this._pendingBlobUrls[entityType] = URL.createObjectURL(file);
    },

    /**
     * Show a client-side preview of the pending photo on the trigger element
     * @param {HTMLElement} trigger - The .avatar-upload-trigger element
     * @param {File} file - The selected image file
     * @param {string} entityType - 'patient' or 'provider'
     */
    _showPendingPreview(trigger, file, entityType) {
        const blobUrl = this._pendingBlobUrls[entityType];
        if (!blobUrl) return;

        // Update inner avatar to show the preview image
        const innerAvatar = trigger.querySelector('.profile-avatar');
        if (innerAvatar) {
            const initials = innerAvatar.querySelector('.profile-avatar-initials')?.textContent || '?';
            const sizeMatch = innerAvatar.className.match(/avatar-(sm|md|lg|xl)/);
            const sizeClass = sizeMatch ? `avatar-${sizeMatch[1]}` : 'avatar-xl';

            innerAvatar.className = `profile-avatar ${sizeClass}`;
            innerAvatar.style = '';
            innerAvatar.innerHTML = `
                <img src="${blobUrl}" alt="Preview" class="profile-avatar-img" loading="lazy">
                <span class="profile-avatar-initials" style="display:none;">${initials}</span>
            `;
        }

        // Add remove button if not present
        if (!trigger.querySelector('.avatar-remove-btn')) {
            const removeBtn = document.createElement('button');
            removeBtn.type = 'button';
            removeBtn.className = 'avatar-remove-btn';
            removeBtn.dataset.avatarRemove = '';
            removeBtn.title = 'Remove photo';
            removeBtn.innerHTML = '<i class="bi bi-x-lg"></i>';
            trigger.insertBefore(removeBtn, trigger.querySelector('.avatar-file-input'));
        }

        // Update overlay text
        const overlayText = trigger.querySelector('.avatar-overlay-text');
        if (overlayText) overlayText.textContent = 'Update';

        // Success flash
        trigger.classList.add('avatar-upload-success');
        setTimeout(() => trigger.classList.remove('avatar-upload-success'), 1000);

        // Clear file input
        const fileInput = trigger.querySelector('.avatar-file-input');
        if (fileInput) fileInput.value = '';
    },

    /**
     * Check if there's a pending photo for an entity type
     * @param {string} entityType - 'patient' or 'provider'
     * @returns {boolean}
     */
    hasPendingPhoto(entityType) {
        return !!this._pendingFiles[entityType];
    },

    /**
     * Upload the pending photo after entity has been saved
     * @param {string} entityType - 'patient' or 'provider'
     * @param {number|string} entityId - The newly created entity ID
     * @returns {Promise<boolean>} True if upload succeeded
     */
    async uploadPendingPhoto(entityType, entityId) {
        const file = this._pendingFiles[entityType];
        if (!file || !entityId) return false;

        const apiBase = entityType === 'patient' ? '/api/patients' : '/api/providers';
        const formData = new FormData();
        formData.append('file', file);

        try {
            const response = await fetch(`${apiBase}/${entityId}/profile-picture`, {
                method: 'POST',
                headers: { 'Authorization': `Bearer ${localStorage.getItem('authToken')}` },
                body: formData
            });

            if (!response.ok) {
                const err = await response.json().catch(() => ({}));
                throw new Error(err.message || 'Upload failed');
            }

            this.clearPendingPhoto(entityType);
            return true;
        } catch (error) {
            console.error(`[AvatarUtils] Pending photo upload error for ${entityType}:`, error);
            this.clearPendingPhoto(entityType);
            return false;
        }
    },

    /**
     * Clear the pending photo for an entity type (cleanup)
     * @param {string} entityType - 'patient' or 'provider'
     */
    clearPendingPhoto(entityType) {
        if (this._pendingBlobUrls[entityType]) {
            URL.revokeObjectURL(this._pendingBlobUrls[entityType]);
            delete this._pendingBlobUrls[entityType];
        }
        delete this._pendingFiles[entityType];
    },

    /**
     * Build profile picture upload UI HTML
     * @param {Object} options
     * @param {string} options.entityType - 'patient' or 'provider'
     * @param {number} [options.entityId] - Entity ID (null for new entities)
     * @param {boolean} options.hasProfilePicture - Whether entity currently has a picture
     * @param {string} options.name - Entity name for initials preview
     * @param {string} [options.color] - Provider color (only for providers)
     * @returns {string} HTML string for upload section
     */
    renderUploadSection({ entityType, entityId, hasProfilePicture, name, color }) {
        const initials = this.getInitials(name || '?');
        const apiBase = entityType === 'patient' ? '/api/patients' : '/api/providers';
        const previewSrc = hasProfilePicture && entityId ? `${apiBase}/${entityId}/profile-picture` : '';
        const bgStyle = entityType === 'provider' && color ? `background: ${color};` : '';

        return `
            <div class="profile-picture-upload" data-entity-type="${entityType}" data-entity-id="${entityId || ''}">
                <label class="form-label"><i class="bi bi-camera me-1"></i>Profile Picture</label>
                <div class="d-flex align-items-center gap-3">
                    <div class="profile-picture-preview avatar-xl ${hasProfilePicture ? '' : (entityType === 'patient' ? 'avatar-initials-patient' : 'avatar-initials-provider')}"
                         style="${!hasProfilePicture && bgStyle ? bgStyle : ''}">
                        ${hasProfilePicture && previewSrc
                            ? `<img src="${previewSrc}" alt="Profile" class="profile-avatar-img" onerror="AvatarUtils.onImageError(this)">
                               <span class="profile-avatar-initials" style="display:none;${bgStyle}">${StringUtils.escape(initials)}</span>`
                            : `<span class="profile-avatar-initials" ${bgStyle ? `style="${bgStyle}"` : ''}>${StringUtils.escape(initials)}</span>`
                        }
                    </div>
                    <div class="flex-grow-1">
                        <input type="file" class="form-control form-control-sm profile-picture-input"
                               accept=".jpg,.jpeg,.png,.webp"
                               data-entity-type="${entityType}">
                        <small class="text-muted d-block mt-1">JPG, PNG, or WebP. Max 5MB.</small>
                        ${hasProfilePicture ? `<button type="button" class="btn btn-sm btn-outline-danger mt-1 remove-profile-picture-btn" data-entity-type="${entityType}" data-entity-id="${entityId}">
                            <i class="bi bi-trash me-1"></i>Remove
                        </button>` : ''}
                    </div>
                </div>
            </div>`;
    },

    /**
     * Handle profile picture file selection — upload immediately and update preview
     * @param {HTMLInputElement} fileInput - The file input element
     * @param {string} entityType - 'patient' or 'provider'
     * @param {number} entityId - Entity ID
     * @param {Function} [onSuccess] - Callback after successful upload
     */
    async uploadProfilePicture(fileInput, entityType, entityId, onSuccess) {
        const file = fileInput.files?.[0];
        if (!file) return;

        // Client-side validation
        const maxSize = 5 * 1024 * 1024; // 5MB
        if (file.size > maxSize) {
            App.toast('error', '', 'File size exceeds 5MB limit');
            fileInput.value = '';
            return;
        }

        const allowedTypes = ['image/jpeg', 'image/png', 'image/webp'];
        if (!allowedTypes.includes(file.type)) {
            App.toast('error', '', 'Only JPG, PNG, and WebP images are allowed');
            fileInput.value = '';
            return;
        }

        const apiBase = entityType === 'patient' ? '/api/patients' : '/api/providers';
        const formData = new FormData();
        formData.append('file', file);

        try {
            const response = await fetch(`${apiBase}/${entityId}/profile-picture`, {
                method: 'POST',
                headers: {
                    'Authorization': `Bearer ${localStorage.getItem('authToken')}`
                },
                body: formData
            });

            if (!response.ok) {
                const err = await response.json().catch(() => ({}));
                throw new Error(err.message || 'Upload failed');
            }

            const result = await response.json();
            if (result.HasProfilePicture || result.hasProfilePicture) {
                App.toast('success', '', 'Profile picture uploaded successfully');

                // Update preview
                const container = fileInput.closest('.profile-picture-upload');
                if (container) {
                    const preview = container.querySelector('.profile-picture-preview');
                    if (preview) {
                        const timestamp = Date.now();
                        preview.className = 'profile-picture-preview avatar-xl';
                        preview.style = '';
                        preview.innerHTML = `
                            <img src="${apiBase}/${entityId}/profile-picture?v=${timestamp}" alt="Profile" class="profile-avatar-img" onerror="AvatarUtils.onImageError(this)">
                            <span class="profile-avatar-initials" style="display:none;">${preview.querySelector('.profile-avatar-initials')?.textContent || '?'}</span>
                        `;
                    }

                    // Add remove button if not present
                    const btnContainer = container.querySelector('.flex-grow-1');
                    if (btnContainer && !btnContainer.querySelector('.remove-profile-picture-btn')) {
                        const removeBtn = document.createElement('button');
                        removeBtn.type = 'button';
                        removeBtn.className = 'btn btn-sm btn-outline-danger mt-1 remove-profile-picture-btn';
                        removeBtn.dataset.entityType = entityType;
                        removeBtn.dataset.entityId = entityId;
                        removeBtn.innerHTML = '<i class="bi bi-trash me-1"></i>Remove';
                        btnContainer.appendChild(removeBtn);
                    }
                }

                if (onSuccess) onSuccess(result);
            } else {
                App.toast('error', '', result.Message || result.message || 'Upload failed');
            }
        } catch (error) {
            console.error('[AvatarUtils] Upload error:', error);
            App.toast('error', '', error.message || 'Failed to upload profile picture');
        }

        fileInput.value = '';
    },

    /**
     * Delete a profile picture
     * @param {string} entityType - 'patient' or 'provider'
     * @param {number} entityId - Entity ID
     * @param {Function} [onSuccess] - Callback after successful deletion
     */
    async deleteProfilePicture(entityType, entityId, onSuccess) {
        const apiBase = entityType === 'patient' ? '/api/patients' : '/api/providers';

        try {
            const response = await fetch(`${apiBase}/${entityId}/profile-picture`, {
                method: 'DELETE',
                headers: {
                    'Authorization': `Bearer ${localStorage.getItem('authToken')}`,
                    'Content-Type': 'application/json'
                }
            });

            if (!response.ok) {
                throw new Error('Delete failed');
            }

            App.toast('success', '', 'Profile picture removed');

            // Update preview in upload section if visible
            const uploadSection = document.querySelector(`.profile-picture-upload[data-entity-type="${entityType}"][data-entity-id="${entityId}"]`);
            if (uploadSection) {
                const preview = uploadSection.querySelector('.profile-picture-preview');
                if (preview) {
                    const initialsSpan = preview.querySelector('.profile-avatar-initials');
                    const initials = initialsSpan?.textContent || '?';
                    const isPatient = entityType === 'patient';
                    preview.className = `profile-picture-preview avatar-xl ${isPatient ? 'avatar-initials-patient' : 'avatar-initials-provider'}`;
                    preview.innerHTML = `<span class="profile-avatar-initials">${initials}</span>`;
                }

                // Remove the delete button
                const removeBtn = uploadSection.querySelector('.remove-profile-picture-btn');
                if (removeBtn) removeBtn.remove();
            }

            if (onSuccess) onSuccess();
        } catch (error) {
            console.error('[AvatarUtils] Delete error:', error);
            App.toast('error', '', 'Failed to remove profile picture');
        }
    }
};

// Global event delegation for profile picture interactions
document.addEventListener('change', (e) => {
    const input = e.target.closest('.profile-picture-input');
    if (!input) return;

    const entityType = input.dataset.entityType;
    const container = input.closest('.profile-picture-upload');
    const entityId = container?.dataset?.entityId;

    if (!entityId) {
        App.toast('warning', '', 'Please save the record first before uploading a profile picture');
        input.value = '';
        return;
    }

    AvatarUtils.uploadProfilePicture(input, entityType, parseInt(entityId));
});

document.addEventListener('click', (e) => {
    const btn = e.target.closest('.remove-profile-picture-btn');
    if (!btn) return;

    const entityType = btn.dataset.entityType;
    const entityId = btn.dataset.entityId;

    if (entityId) {
        AvatarUtils.deleteProfilePicture(entityType, parseInt(entityId));
    }
});

// ============================================
// Editable Avatar Event Delegation
// ============================================

// Click on editable avatar trigger → open file picker
document.addEventListener('click', (e) => {
    // Ignore clicks on the remove button
    if (e.target.closest('[data-avatar-remove]')) return;

    const trigger = e.target.closest('[data-avatar-editable]');
    if (!trigger) return;

    const fileInput = trigger.querySelector('.avatar-file-input');
    if (fileInput) fileInput.click();
});

// File selected on hidden input → handle upload
document.addEventListener('change', (e) => {
    const input = e.target.closest('.avatar-file-input');
    if (!input) return;

    const trigger = input.closest('[data-avatar-editable]');
    if (!trigger) return;

    const file = input.files?.[0];
    if (file) {
        AvatarUtils._handleEditableUpload(trigger, file);
    }
});

// Click on remove button → handle removal
document.addEventListener('click', (e) => {
    const removeBtn = e.target.closest('[data-avatar-remove]');
    if (!removeBtn) return;

    e.stopPropagation(); // Prevent triggering the file picker

    const trigger = removeBtn.closest('[data-avatar-editable]');
    if (trigger) {
        AvatarUtils._handleEditableRemove(trigger);
    }
});

// 2026-05: expose to window.AvatarUtils so defensive `window.AvatarUtils ?`
// guards work alongside bare-identifier usage. Top-level `const AvatarUtils`
// in a browser script tag is reachable as the bare identifier but is NOT a
// property of `window` (ES2015 const semantics). Without this line, every
// `if (window.AvatarUtils)` guard silently evaluated false and the call site
// rendered an empty string, which is exactly the bug that broke portal
// Profile avatars, dashboard card avatars, and encounter top-bar avatars.
window.AvatarUtils = AvatarUtils;
