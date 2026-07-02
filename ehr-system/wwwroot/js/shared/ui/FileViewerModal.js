/**
 * FileViewerModal — In-app modal file viewer for PDFs, images, text, and video files.
 * Fetches files with auth token and displays using blob URLs.
 *
 * Usage:
 *   FileViewerModal.showFromFetch({ fetchUrl, fileName, token });
 */
class FileViewerModal {
    static _modal = null;
    static _bsModal = null;
    static _currentBlobUrl = null;

    static _PDF_TYPES = ['application/pdf'];
    static _IMAGE_TYPES = ['image/png', 'image/jpeg', 'image/jpg', 'image/gif', 'image/bmp', 'image/tiff', 'image/webp', 'image/svg+xml'];
    static _TEXT_TYPES = ['text/plain', 'text/csv', 'text/html', 'text/xml', 'application/json', 'application/xml'];
    static _VIDEO_TYPES = ['video/mp4', 'video/webm', 'video/ogg', 'video/quicktime'];

    static _IMAGE_EXTS = ['png', 'jpg', 'jpeg', 'gif', 'bmp', 'tiff', 'webp', 'svg'];
    static _TEXT_EXTS = ['txt', 'csv', 'json', 'xml', 'log', 'md'];
    static _VIDEO_EXTS = ['mp4', 'webm', 'ogg', 'mov'];

    static init() {
        this._modal = document.getElementById('fileViewerModal');
        if (this._modal) {
            this._bsModal = new bootstrap.Modal(this._modal, { keyboard: true });
            this._modal.addEventListener('hidden.bs.modal', () => this._cleanup());
            // Ensure file viewer modal + backdrop always appear above other modals
            this._modal.addEventListener('shown.bs.modal', () => {
                this._modal.style.zIndex = '1070';
                // Find the backdrop for this modal and raise it too
                const backdrops = document.querySelectorAll('.modal-backdrop');
                if (backdrops.length > 0) {
                    backdrops[backdrops.length - 1].style.zIndex = '1065';
                }
            });
        }
    }

    static async showFromFetch({ fetchUrl, fileName = 'File', token = null }) {
        if (!this._modal) this.init();
        if (!this._bsModal) {
            console.error('[FileViewerModal] Modal element not found');
            return;
        }

        this._setTitle(fileName);
        this._setLoading();
        this._bsModal.show();

        try {
            const authToken = token || localStorage.getItem('authToken') || localStorage.getItem('portalAuthToken');
            const response = await fetch(fetchUrl, {
                headers: authToken ? { 'Authorization': `Bearer ${authToken}` } : {}
            });

            if (!response.ok) throw new Error(`Failed to load file (${response.status})`);

            const blob = await response.blob();
            const contentType = response.headers.get('Content-Type') || blob.type || '';
            this._renderContent(blob, contentType, fileName);
        } catch (error) {
            console.error('[FileViewerModal] Error:', error);
            this._setError(error.message || 'Failed to load file');
        }
    }

    static isViewableFile(fileName) {
        const ext = (fileName || '').toLowerCase().split('.').pop();
        return ['pdf', ...this._IMAGE_EXTS, ...this._TEXT_EXTS, ...this._VIDEO_EXTS].includes(ext);
    }

    static _renderContent(blob, contentType, fileName) {
        const body = this._modal.querySelector('.fv-modal-body');
        const ext = (fileName || '').toLowerCase().split('.').pop();
        const blobUrl = URL.createObjectURL(blob);
        this._currentBlobUrl = blobUrl;

        // PDF
        if (this._PDF_TYPES.includes(contentType) || ext === 'pdf') {
            body.innerHTML = `<iframe src="${blobUrl}#toolbar=1&navpanes=0" class="fv-iframe" title="PDF Viewer"></iframe>`;
            return;
        }

        // Image
        if (this._IMAGE_TYPES.some(t => contentType.includes(t)) || this._IMAGE_EXTS.includes(ext)) {
            body.innerHTML = `
                <div class="fv-image-container">
                    <img src="${blobUrl}" alt="${this._escapeHtml(fileName)}" class="fv-image" />
                </div>`;
            return;
        }

        // Video
        if (this._VIDEO_TYPES.some(t => contentType.includes(t)) || this._VIDEO_EXTS.includes(ext)) {
            body.innerHTML = `
                <div class="fv-video-container">
                    <video src="${blobUrl}" controls class="fv-video" controlsList="nodownload">
                        Your browser does not support the video tag.
                    </video>
                </div>`;
            return;
        }

        // Text
        if (this._TEXT_TYPES.some(t => contentType.includes(t)) || this._TEXT_EXTS.includes(ext)) {
            const reader = new FileReader();
            reader.onload = () => {
                body.innerHTML = `
                    <div class="fv-text-container">
                        <pre class="fv-text-content">${this._escapeHtml(reader.result)}</pre>
                    </div>`;
            };
            reader.readAsText(blob);
            return;
        }

        // Unsupported
        body.innerHTML = `
            <div class="fv-unsupported">
                <i class="bi bi-file-earmark-x fs-1 text-muted"></i>
                <p class="mt-3 mb-1 fw-semibold">Preview not available</p>
                <p class="text-muted mb-3">This file type cannot be previewed in the browser.</p>
                <a href="${blobUrl}" download="${this._escapeHtml(fileName)}" class="btn btn-primary">
                    <i class="bi bi-download me-1"></i>Download File
                </a>
            </div>`;
    }

    static _setTitle(fileName) {
        const titleEl = this._modal.querySelector('.fv-modal-title');
        if (titleEl) titleEl.textContent = fileName || 'File Viewer';
    }

    static _setLoading() {
        const body = this._modal.querySelector('.fv-modal-body');
        body.innerHTML = `
            <div class="fv-loading">
                <div class="spinner-border text-primary" style="width:3rem;height:3rem;" role="status">
                    <span class="visually-hidden">Loading...</span>
                </div>
                <p class="mt-3 text-muted">Loading file...</p>
            </div>`;
    }

    static _setError(message) {
        const body = this._modal.querySelector('.fv-modal-body');
        body.innerHTML = `
            <div class="fv-error">
                <i class="bi bi-exclamation-triangle fs-1 text-danger"></i>
                <p class="mt-3 mb-1 fw-semibold">Unable to load file</p>
                <p class="text-muted">${this._escapeHtml(message)}</p>
            </div>`;
    }

    static _cleanup() {
        if (this._currentBlobUrl) {
            URL.revokeObjectURL(this._currentBlobUrl);
            this._currentBlobUrl = null;
        }
        const body = this._modal?.querySelector('.fv-modal-body');
        if (body) body.innerHTML = '';
    }

    static _escapeHtml(str) {
        if (!str) return '';
        const div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML;
    }
}

document.addEventListener('DOMContentLoaded', () => FileViewerModal.init());
