/**
 * ConfirmDialog - Confirmation dialog utility
 * Provides a promise-based confirmation modal
 *
 * Usage:
 *   const confirmed = await ConfirmDialog.show({
 *     title: 'Delete Patient',
 *     message: 'Are you sure?',
 *     confirmText: 'Delete',
 *     confirmClass: 'btn-danger'
 *   });
 *   if (confirmed) { ... }
 */
const ConfirmDialog = {
    // Modal element ID
    modalId: 'confirmModal',

    // Current promise resolver
    resolver: null,

    /**
     * Show a confirmation dialog
     * @param {Object} options - Dialog options
     * @param {string} [options.title='Confirm'] - Dialog title
     * @param {string} [options.message='Are you sure?'] - Dialog message
     * @param {string} [options.confirmText='Confirm'] - Confirm button text
     * @param {string} [options.cancelText='Cancel'] - Cancel button text
     * @param {string} [options.confirmClass='btn-primary'] - Confirm button class
     * @param {string} [options.headerClass=''] - Header class
     * @param {boolean} [options.showCancel=true] - Show cancel button
     * @returns {Promise<boolean>} True if confirmed, false if cancelled
     */
    show(options = {}) {
        const {
            title = 'Confirm',
            message = 'Are you sure?',
            confirmText = 'Confirm',
            cancelText = 'Cancel',
            confirmClass = 'btn-primary',
            headerClass = '',
            showCancel = true
        } = options;

        return new Promise((resolve) => {
            this.resolver = resolve;

            // Get or create modal
            let modal = document.getElementById(this.modalId);
            if (!modal) {
                modal = this.createModal();
            }

            // Update modal content
            const headerEl = modal.querySelector('.modal-header');
            const titleEl = modal.querySelector('.modal-title');
            const bodyEl = modal.querySelector('.modal-body');
            const confirmBtn = modal.querySelector('.confirm-btn');
            const cancelBtn = modal.querySelector('.cancel-btn');

            if (headerEl) {
                headerEl.className = `modal-header ${headerClass}`;
            }
            if (titleEl) {
                titleEl.textContent = title;
            }
            if (bodyEl) {
                bodyEl.innerHTML = `<p class="mb-0">${StringUtils.escape(message)}</p>`;
            }
            if (confirmBtn) {
                confirmBtn.textContent = confirmText;
                confirmBtn.className = `btn ${confirmClass} confirm-btn`;
            }
            if (cancelBtn) {
                cancelBtn.textContent = cancelText;
                cancelBtn.style.display = showCancel ? '' : 'none';
            }

            // Reset confirmed flag
            modal._confirmed = false;

            // Handle modal hidden - this is where we resolve the promise
            const handleHidden = () => {
                modal.removeEventListener('hidden.bs.modal', handleHidden);
                const confirmed = modal._confirmed === true;
                modal._confirmed = false; // Reset for next use

                // Restore body.modal-open if another modal is still showing behind this one
                const otherOpenModals = document.querySelectorAll('.modal.show');
                if (otherOpenModals.length > 0) {
                    document.body.classList.add('modal-open');
                }

                if (this.resolver) {
                    this.resolver(confirmed);
                    this.resolver = null;
                }
            };
            modal.addEventListener('hidden.bs.modal', handleHidden);

            // Show modal - reuse existing instance if available
            const bsModal = bootstrap.Modal.getInstance(modal) || new bootstrap.Modal(modal);
            bsModal.show();

            // Ensure confirm modal and its backdrop appear above all other modals
            // (handles stacked modal scenarios like confirm over tabbed notes)
            modal.style.zIndex = '1070';
            setTimeout(() => {
                const backdrops = document.querySelectorAll('.modal-backdrop');
                if (backdrops.length > 1) {
                    backdrops[backdrops.length - 1].style.zIndex = '1069';
                }
            }, 10);
        });
    },

    /**
     * Create the confirmation modal element
     * @returns {HTMLElement} Modal element
     */
    createModal() {
        const modal = document.createElement('div');
        modal.id = this.modalId;
        modal.className = 'modal fade';
        modal.tabIndex = -1;
        modal.innerHTML = `
            <div class="modal-dialog modal-dialog-centered modal-md">
                <div class="modal-content">
                    <div class="modal-header">
                        <h5 class="modal-title">Confirm</h5>
                        <button type="button" class="btn-close" data-bs-dismiss="modal"></button>
                    </div>
                    <div class="modal-body">
                        <p class="mb-0">Are you sure?</p>
                    </div>
                    <div class="modal-footer">
                        <button type="button" class="btn btn-secondary cancel-btn" data-bs-dismiss="modal">Cancel</button>
                        <button type="button" class="btn btn-primary confirm-btn">Confirm</button>
                    </div>
                </div>
            </div>
        `;

        // Add event listeners
        const confirmBtn = modal.querySelector('.confirm-btn');
        confirmBtn.addEventListener('click', () => {
            // Set flag - the hidden event handler will read this and resolve with true
            modal._confirmed = true;
            const bsModal = bootstrap.Modal.getInstance(modal);
            if (bsModal) bsModal.hide();
        });

        document.body.appendChild(modal);
        return modal;
    },

    /**
     * Show a delete confirmation
     * @param {string} itemName - Name of item being deleted
     * @returns {Promise<boolean>}
     */
    confirmDelete(itemName) {
        return this.show({
            title: 'Confirm Delete',
            message: `Are you sure you want to delete ${itemName}? This action cannot be undone.`,
            confirmText: 'Delete',
            confirmClass: 'btn-danger',
            headerClass: 'bg-danger text-white'
        });
    },

    /**
     * Show a warning confirmation
     * @param {string} message - Warning message
     * @returns {Promise<boolean>}
     */
    confirmWarning(message) {
        return this.show({
            title: 'Warning',
            message,
            confirmText: 'Confirm',
            confirmClass: 'btn-warning',
            headerClass: 'bg-warning'
        });
    },

    /**
     * Show an info confirmation
     * @param {string} title - Title
     * @param {string} message - Message
     * @returns {Promise<boolean>}
     */
    confirmInfo(title, message) {
        return this.show({
            title,
            message,
            confirmText: 'OK',
            confirmClass: 'btn-info',
            showCancel: false
        });
    },

    /**
     * Show a save changes confirmation
     * @returns {Promise<boolean>}
     */
    confirmUnsavedChanges() {
        return this.show({
            title: 'Unsaved Changes',
            message: 'You have unsaved changes. Are you sure you want to leave without saving?',
            confirmText: 'Leave Without Saving',
            confirmClass: 'btn-warning',
            cancelText: 'Stay'
        });
    }
};

// Export for global access
window.ConfirmDialog = ConfirmDialog;

// Backward compatibility
window.showConfirmModal = async function(options) {
    return ConfirmDialog.show(options);
};
