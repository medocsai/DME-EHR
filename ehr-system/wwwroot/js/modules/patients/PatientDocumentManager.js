/**
 * PatientDocumentManager - Handles document uploads, downloads, and file management
 */

class PatientDocumentManager {
    constructor(options = {}) {
        this.parentModule = options.parentModule;
        this.utilities = options.utilities || PatientUtilities;
        this.api = options.api;
    }

    /**
     * Initialize attachment upload handlers
     * @param {Function} onUploadFn - Callback function for file upload handling
     */
    initAttachmentUpload(onUploadFn) {
        const dropzone = document.getElementById('patientDropzone');
        const fileInput = document.getElementById('patientFileInput');

        if (!dropzone || !fileInput) return;

        // Prevent duplicate event handlers by checking if already initialized
        if (dropzone._uploadHandlersBound) {
            // Update the callback function for already bound handlers
            dropzone._uploadCallback = onUploadFn;
            fileInput._uploadCallback = onUploadFn;
            return;
        }

        // Store callback on elements so we can update it later
        dropzone._uploadCallback = onUploadFn;
        fileInput._uploadCallback = onUploadFn;

        // Drag and drop handlers
        dropzone.addEventListener('dragover', (e) => {
            e.preventDefault();
            e.stopPropagation();
            dropzone.classList.add('dragover');
        });

        dropzone.addEventListener('dragleave', (e) => {
            e.preventDefault();
            e.stopPropagation();
            dropzone.classList.remove('dragover');
        });

        dropzone.addEventListener('drop', (e) => {
            e.preventDefault();
            e.stopPropagation();
            dropzone.classList.remove('dragover');
            const files = e.dataTransfer.files;
            if (files.length && dropzone._uploadCallback) {
                dropzone._uploadCallback(files);
            }
        });

        // Click to upload
        dropzone.addEventListener('click', (e) => {
            if (e.target.tagName !== 'BUTTON') {
                fileInput.click();
            }
        });

        // File input change
        fileInput.addEventListener('change', (e) => {
            if (e.target.files.length && fileInput._uploadCallback) {
                fileInput._uploadCallback(e.target.files);
            }
            // Reset file input to allow selecting the same file again
            fileInput.value = '';
        });

        // Mark as initialized to prevent duplicate bindings
        dropzone._uploadHandlersBound = true;
    }

    /**
     * Handle file upload (validation and upload)
     * @param {FileList} files - Files to upload
     * @param {Object} options - Upload options
     * @param {number} options.patientId - Patient ID (optional)
     * @param {number} options.category - Document category
     * @param {string} options.description - Document description
     * @param {Function} options.onSuccess - Success callback
     * @param {Function} options.onError - Error callback
     * @param {Function} options.onQueueFile - Callback for queueing files
     */
    async handleFileUpload(files, options = {}) {
        const maxSize = 10 * 1024 * 1024; // 10MB
        const allowedTypes = ['.pdf', '.jpg', '.jpeg', '.png', '.doc', '.docx', '.xls', '.xlsx', '.txt', '.gif', '.bmp', '.tiff'];

        // Validate files first
        const validFiles = [];
        for (const file of files) {
            if (file.size > maxSize) {
                if (options.onError) {
                    options.onError(`File "${file.name}" exceeds 10MB limit`);
                }
                continue;
            }

            const ext = '.' + file.name.split('.').pop().toLowerCase();
            if (!allowedTypes.includes(ext)) {
                if (options.onError) {
                    options.onError(`File type "${ext}" is not supported`);
                }
                continue;
            }

            validFiles.push(file);
        }

        if (validFiles.length === 0) return;

        // If no patientId, queue files for later upload
        if (!options.patientId) {
            if (options.onQueueFile) {
                options.onQueueFile(validFiles, options.category, options.description);
            }
            return;
        }

        // Patient exists - upload files immediately
        await this._uploadFiles(validFiles, options);
    }

    /**
     * Upload files to the server
     * @private
     * @param {Array} validFiles - Files to upload
     * @param {Object} options - Upload options
     */
    async _uploadFiles(validFiles, options) {
        const progressContainer = document.getElementById('uploadProgress');
        const progressBar = progressContainer?.querySelector('.progress-bar');
        const progressStatus = document.getElementById('uploadStatus');

        if (progressContainer) {
            progressContainer.classList.remove('d-none');
        }

        let uploaded = 0;
        const total = validFiles.length;

        for (const file of validFiles) {
            if (progressStatus) {
                progressStatus.textContent = `Uploading ${file.name}...`;
            }

            try {
                const formData = new FormData();
                formData.append('file', file);
                formData.append('category', options.category);
                formData.append('description', options.description || '');

                const token = localStorage.getItem('authToken');
                const response = await fetch(`/api/patients/${options.patientId}/documents`, {
                    method: 'POST',
                    headers: {
                        'Authorization': token ? `Bearer ${token}` : ''
                    },
                    body: formData
                });

                if (!response.ok) {
                    const error = await response.json().catch(() => ({}));
                    throw new Error(error.message || 'Upload failed');
                }

                uploaded++;

                if (progressBar) {
                    progressBar.style.width = `${(uploaded / total) * 100}%`;
                }
            } catch (error) {
                console.error('[PatientDocumentManager] File upload failed:', error);
                if (options.onError) {
                    options.onError(`Failed to upload "${file.name}": ${error.message}`);
                }
            }
        }

        if (progressContainer) {
            progressContainer.classList.add('d-none');
        }

        if (progressBar) {
            progressBar.style.width = '0%';
        }

        if (uploaded > 0 && options.onSuccess) {
            options.onSuccess(`${uploaded} file(s) uploaded successfully`, options.patientId);
        }

        // Clear form inputs
        this._clearUploadForm();
    }

    /**
     * Clear upload form
     * @private
     */
    _clearUploadForm() {
        const fileInput = document.getElementById('patientFileInput');
        if (fileInput) fileInput.value = '';

        const descriptionInput = document.getElementById('documentDescription');
        if (descriptionInput) descriptionInput.value = '';
    }

    /**
     * Load patient attachments in the edit modal
     * @param {number} patientId - Patient ID
     * @param {Function} apiFn - API function to call
     * @param {Object} options - Options
     */
    async loadPatientAttachments(patientId, apiFn, options = {}) {
        const listContainer = document.getElementById('patientAttachmentsList');
        if (!listContainer) return;

        listContainer.innerHTML = `
            <tr>
                <td colspan="5" class="text-center py-3">
                    <span class="spinner-border spinner-border-sm me-1"></span>Loading...
                </td>
            </tr>
        `;

        try {
            const attachments = await apiFn(`/patients/${patientId}/documents`);

            if (!attachments || attachments.length === 0) {
                listContainer.innerHTML = `
                    <tr>
                        <td colspan="5" class="text-center text-muted py-3">
                            No documents uploaded yet
                        </td>
                    </tr>
                `;
                return;
            }

            const categoryNames = ['Insurance Card', 'ID Document', 'Referral', 'Medical Record', 'Consent Form', 'Other'];

            listContainer.innerHTML = attachments.map(att => `
                <tr>
                    <td>
                        <i class="bi ${this.utilities.getFileIcon(att.FileName)} me-1"></i>
                        ${this.utilities.escape(att.FileName)}
                    </td>
                    <td><span class="badge bg-secondary">${categoryNames[att.Category] || 'Other'}</span></td>
                    <td>${this.utilities.formatFileSize(att.FileSize)}</td>
                    <td>${this.utilities.formatDate(att.CreatedAt)}</td>
                    <td>
                        <button type="button" class="btn btn-sm btn-outline-primary" onclick="window.patientModule.downloadDocument(${att.DocumentId}, ${patientId})">
                            <i class="bi bi-download"></i>
                        </button>
                        <button type="button" class="btn btn-sm btn-outline-danger" onclick="window.patientModule.deleteDocument(${att.DocumentId})">
                            <i class="bi bi-trash"></i>
                        </button>
                    </td>
                </tr>
            `).join('');
        } catch (error) {
            console.error('[PatientDocumentManager] Failed to load attachments:', error);
            // Show empty state instead of error if endpoint doesn't exist
            listContainer.innerHTML = `
                <tr>
                    <td colspan="5" class="text-center text-muted py-3">
                        No documents uploaded yet
                    </td>
                </tr>
            `;
        }
    }

    /**
     * Load documents for View Patient modal Attachments tab
     * @param {number} patientId - Patient ID
     * @param {Function} apiFn - API function to call (optional, defaults to direct fetch)
     */
    async loadViewPatientDocuments(patientId, apiFn) {
        const container = document.getElementById('patientAttachmentsContent');
        if (!container) return;

        // Show loading state
        container.innerHTML = `
            <div class="text-center py-3">
                <span class="spinner-border spinner-border-sm me-1"></span>Loading documents...
            </div>
        `;

        try {
            // Use provided apiFn or fall back to direct fetch
            let documents;
            if (apiFn) {
                documents = await apiFn(`/patients/${patientId}/documents`);
            } else {
                // Default API call using fetch
                const token = localStorage.getItem('authToken');
                const response = await fetch(`/api/patients/${patientId}/documents`, {
                    method: 'GET',
                    headers: {
                        'Content-Type': 'application/json',
                        'Authorization': token ? `Bearer ${token}` : ''
                    }
                });
                if (!response.ok) {
                    throw new Error('Failed to load documents');
                }
                documents = await response.json();
            }

            if (!documents || documents.length === 0) {
                container.innerHTML = `
                    <div class="text-center text-muted py-4">
                        <i class="bi bi-paperclip fs-1 d-block mb-2"></i>
                        No documents found for this patient.
                    </div>
                `;
                return;
            }

            const categoryNames = ['Insurance Card', 'ID Document', 'Referral', 'Medical Record', 'Consent Form', 'Other'];

            container.innerHTML = `
                <div class="table-responsive">
                    <table class="table table-sm">
                        <thead>
                            <tr>
                                <th>File Name</th>
                                <th>Category</th>
                                <th>Size</th>
                                <th>Uploaded Date</th>
                                <th>Uploaded By</th>
                                <th>Actions</th>
                            </tr>
                        </thead>
                        <tbody>
                            ${documents.map(doc => `
                                <tr>
                                    <td>
                                        <i class="bi ${this.utilities.getFileIcon(doc.FileName)} me-1"></i>
                                        ${this.utilities.escape(doc.FileName)}
                                    </td>
                                    <td><span class="badge bg-secondary">${categoryNames[doc.Category] || 'Other'}</span></td>
                                    <td>${this.utilities.formatFileSize(doc.FileSize)}</td>
                                    <td>${this.utilities.formatDate(doc.CreatedAt)}</td>
                                    <td>${doc.IsPatientUploaded ? '<span class="badge bg-info">Patient</span>' : this.utilities.escape(doc.UploadedByName || '-')}</td>
                                    <td>
                                        <button class="btn btn-sm btn-outline-info me-1" onclick="FileViewerModal.showFromFetch({fetchUrl:'/api/patients/${patientId}/documents/${doc.DocumentId}',fileName:'${(doc.FileName || '').replace(/'/g, "\\'")}'});" title="View">
                                            <i class="bi bi-eye"></i>
                                        </button>
                                        <button class="btn btn-sm btn-outline-primary" onclick="window.patientModule.downloadDocument(${doc.DocumentId}, ${patientId})" title="Download">
                                            <i class="bi bi-download"></i>
                                        </button>
                                    </td>
                                </tr>
                            `).join('')}
                        </tbody>
                    </table>
                </div>
            `;
        } catch (error) {
            console.error('[PatientDocumentManager] Failed to load documents:', error);
            container.innerHTML = `
                <div class="text-center text-muted py-4">
                    <i class="bi bi-paperclip fs-1 d-block mb-2"></i>
                    No documents found for this patient.
                </div>
            `;
        }
    }

    /**
     * Download a document
     * @param {number} documentId - Document ID
     * @param {number} patientId - Patient ID
     */
    async downloadDocument(documentId, patientId) {
        try {
            if (!patientId) {
                throw new Error('Patient ID is required to download document');
            }

            const token = localStorage.getItem('authToken');
            const response = await fetch(`/api/patients/${patientId}/documents/${documentId}`, {
                headers: {
                    'Authorization': token ? `Bearer ${token}` : ''
                }
            });

            if (!response.ok) {
                throw new Error('Download failed');
            }

            // Get filename from Content-Disposition header or use default
            const contentDisposition = response.headers.get('Content-Disposition');
            let fileName = 'document';
            if (contentDisposition) {
                const match = contentDisposition.match(/filename="?(.+?)"?(?:;|$)/);
                if (match) fileName = match[1];
            }

            // Create blob and download
            const blob = await response.blob();
            const url = window.URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = fileName;
            document.body.appendChild(a);
            a.click();
            window.URL.revokeObjectURL(url);
            document.body.removeChild(a);
        } catch (error) {
            console.error('[PatientDocumentManager] Failed to download document:', error);
            throw error;
        }
    }

    /**
     * Delete a document
     * @param {number} documentId - Document ID
     * @param {number} patientId - Patient ID
     * @param {Function} apiFn - API delete function
     * @param {Function} confirmFn - Confirmation function
     */
    async deleteDocument(documentId, patientId, apiFn, confirmFn) {
        const confirmed = await confirmFn({
            title: 'Delete Document',
            message: 'Are you sure you want to delete this document? This action cannot be undone.',
            confirmText: 'Delete',
            confirmClass: 'btn-danger'
        });

        if (!confirmed) return;

        if (!patientId) {
            throw new Error('Patient ID is required to delete document');
        }

        try {
            await apiFn(`/patients/${patientId}/documents/${documentId}`);
            return true;
        } catch (error) {
            console.error('[PatientDocumentManager] Failed to delete document:', error);
            throw error;
        }
    }

    /**
     * Refresh documents in the View modal's Attachments tab
     * @param {number} patientId - Patient ID
     * @param {Function} apiFn - API function to call
     */
    async refreshViewModalDocuments(patientId, apiFn) {
        const tabContent = document.getElementById('patientAttachments');
        if (!tabContent) return;

        tabContent.innerHTML = `
            <div class="text-center py-3">
                <span class="spinner-border spinner-border-sm me-1"></span>Loading...
            </div>
        `;

        try {
            const documents = await apiFn(`/patients/${patientId}/documents`);

            if (!documents || documents.length === 0) {
                tabContent.innerHTML = `
                    <div class="text-center text-muted py-4">
                        <i class="bi bi-paperclip fs-1 d-block mb-2"></i>
                        No documents found for this patient.
                    </div>
                `;
                return;
            }

            const categoryNames = ['Insurance Card', 'ID Document', 'Referral', 'Medical Record', 'Consent Form', 'Other'];

            tabContent.innerHTML = `
                <div class="table-responsive">
                    <table class="table table-sm">
                        <thead>
                            <tr>
                                <th>File Name</th>
                                <th>Category</th>
                                <th>Size</th>
                                <th>Uploaded</th>
                                <th>Actions</th>
                            </tr>
                        </thead>
                        <tbody>
                            ${documents.map(doc => `
                                <tr>
                                    <td>
                                        <i class="bi ${this.utilities.getFileIcon(doc.FileName)} me-1"></i>
                                        ${this.utilities.escape(doc.FileName)}
                                    </td>
                                    <td><span class="badge bg-secondary">${categoryNames[doc.Category] || 'Other'}</span></td>
                                    <td>${this.utilities.formatFileSize(doc.FileSize)}</td>
                                    <td>${this.utilities.formatDate(doc.CreatedAt)}</td>
                                    <td>
                                        <button class="btn btn-sm btn-outline-info me-1" onclick="FileViewerModal.showFromFetch({fetchUrl:'/api/patients/${patientId}/documents/${doc.DocumentId}',fileName:'${(doc.FileName || '').replace(/'/g, "\\'")}'});" title="View">
                                            <i class="bi bi-eye"></i>
                                        </button>
                                        <button class="btn btn-sm btn-outline-primary" onclick="window.patientModule.downloadDocument(${doc.DocumentId}, ${patientId})" title="Download">
                                            <i class="bi bi-download"></i>
                                        </button>
                                    </td>
                                </tr>
                            `).join('')}
                        </tbody>
                    </table>
                </div>
            `;
        } catch (error) {
            console.error('[PatientDocumentManager] Failed to refresh documents:', error);
            tabContent.innerHTML = `
                <div class="text-center text-muted py-4">
                    <i class="bi bi-paperclip fs-1 d-block mb-2"></i>
                    No documents found for this patient.
                </div>
            `;
        }
    }

    /**
     * Render pending files in the attachment list
     * @param {Array} pendingFiles - Array of pending files
     */
    renderPendingFiles(pendingFiles) {
        const listContainer = document.getElementById('patientAttachmentsList');
        if (!listContainer) return;

        if (pendingFiles.length === 0) {
            listContainer.innerHTML = `
                <tr>
                    <td colspan="5" class="text-center text-muted py-3">
                        No documents uploaded yet
                    </td>
                </tr>
            `;
            return;
        }

        const categoryNames = ['Insurance Card', 'ID Document', 'Referral', 'Medical Record', 'Consent Form', 'Other'];

        listContainer.innerHTML = pendingFiles.map(item => `
            <tr>
                <td>
                    <i class="bi ${this.utilities.getFileIcon(item.file.name)} me-1"></i>
                    ${this.utilities.escape(item.file.name)}
                    <span class="badge bg-warning text-dark ms-1">Pending</span>
                </td>
                <td><span class="badge bg-secondary">${categoryNames[item.category] || 'Other'}</span></td>
                <td>${this.utilities.formatFileSize(item.file.size)}</td>
                <td>-</td>
                <td>
                    <button type="button" class="btn btn-sm btn-outline-danger" onclick="window.patientModule.removePendingFile(${item.id})">
                        <i class="bi bi-trash"></i>
                    </button>
                </td>
            </tr>
        `).join('');
    }

    /**
     * Upload all pending files for a newly created patient
     * @param {Array} pendingFiles - Array of pending files
     * @param {number} patientId - New patient ID
     * @param {Function} apiFn - API fetch function
     */
    async uploadPendingFiles(pendingFiles, patientId, apiFn) {
        if (pendingFiles.length === 0) return false;

        let uploaded = 0;
        const total = pendingFiles.length;

        for (const item of pendingFiles) {
            try {
                const formData = new FormData();
                formData.append('file', item.file);
                formData.append('category', item.category);
                formData.append('description', item.description);

                const token = localStorage.getItem('authToken');
                const response = await fetch(`/api/patients/${patientId}/documents`, {
                    method: 'POST',
                    headers: {
                        'Authorization': token ? `Bearer ${token}` : ''
                    },
                    body: formData
                });

                if (response.ok) {
                    uploaded++;
                }
            } catch (error) {
                console.error('[PatientDocumentManager] Failed to upload pending file:', error);
            }
        }

        return { uploaded, total: pendingFiles.length };
    }
}

// Export for use in both modern and legacy environments
if (typeof module !== 'undefined' && module.exports) {
    module.exports = PatientDocumentManager;
}
window.PatientDocumentManager = PatientDocumentManager;
